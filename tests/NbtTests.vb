' NBT 与路径工具的回归测试。
'
' 这些模块是纯逻辑（不依赖 WPF），可以直接用 vbc 编成控制台程序跑，
' 所以不需要引入测试框架：一个断言函数加一份清单就够了。
' 运行方式见同目录的 run.sh。

Module NbtTests

    Private Failed As Integer = 0
    Private Passed As Integer = 0

    Private Sub Check(Ok As Boolean, Name As String, Optional Detail As String = "")
        If Ok Then
            Passed += 1
        Else
            Failed += 1
            Console.WriteLine("  [失败] " & Name & If(Detail = "", "", "  <- " & Detail))
        End If
    End Sub

    Private Sub CheckEqual(Actual As Object, Expected As Object, Name As String)
        Dim A As String = If(Actual Is Nothing, "<null>", Actual.ToString())
        Dim E As String = If(Expected Is Nothing, "<null>", Expected.ToString())
        Check(A = E, Name, "期望 " & E & "，实际 " & A)
    End Sub

    Sub Main()
        Console.OutputEncoding = Text.Encoding.UTF8
        Console.WriteLine("=== DstNbt ===")
        TestTypeName()
        TestRoundTrip()
        TestSignedByte()
        TestGZip()
        TestSetText()
        TestStripNamespace()

        Console.WriteLine("=== DstNbtPath ===")
        TestPathBasics()
        TestPathCompare()

        Console.WriteLine("=== 路径编辑 ===")
        TestResolve()
        TestAddRemove()
        TestRename()

        Console.WriteLine()
        Console.WriteLine($"通过 {Passed}，失败 {Failed}")
        Environment.ExitCode = If(Failed = 0, 0, 1)
    End Sub

    '---- 构造辅助 ----

    Private Function Compound(ParamArray Children As NbtTag()) As NbtTag
        Return New NbtTag With {
            .TagType = DstNbt.TypeCompound,
            .TagName = "",
            .TagValue = New List(Of NbtTag)(Children)}
    End Function

    Private Function NamedCompound(Name As String, ParamArray Children As NbtTag()) As NbtTag
        Return New NbtTag With {
            .TagType = DstNbt.TypeCompound,
            .TagName = Name,
            .TagValue = New List(Of NbtTag)(Children)}
    End Function

    Private Function IntTag(Name As String, Value As Integer) As NbtTag
        Return New NbtTag With {.TagType = DstNbt.TypeInt, .TagName = Name, .TagValue = Value}
    End Function

    Private Function StringTag(Name As String, Value As String) As NbtTag
        Return New NbtTag With {.TagType = DstNbt.TypeString, .TagName = Name, .TagValue = Value}
    End Function

    '---- DstNbt ----

    Private Sub TestTypeName()
        CheckEqual(DstNbt.TypeName(DstNbt.TypeCompound), "Compound", "TypeName 复合标签")
        CheckEqual(DstNbt.TypeName(DstNbt.TypeByteArray), "ByteArray", "TypeName 字节数组")
        CheckEqual(DstNbt.TypeName(99), "Unknown", "TypeName 未知类型")
    End Sub

    Private Sub TestRoundTrip()
        '写出去再读回来，所有标量类型与容器类型都要保持一致
        Dim Root As New NbtTag With {
            .TagType = DstNbt.TypeCompound,
            .TagName = "root",
            .TagValue = New List(Of NbtTag) From {
                New NbtTag With {.TagType = DstNbt.TypeByte, .TagName = "b", .TagValue = CSByte(7)},
                New NbtTag With {.TagType = DstNbt.TypeShort, .TagName = "s", .TagValue = CShort(-300)},
                New NbtTag With {.TagType = DstNbt.TypeInt, .TagName = "i", .TagValue = -123456},
                New NbtTag With {.TagType = DstNbt.TypeLong, .TagName = "l", .TagValue = 1234567890123L},
                New NbtTag With {.TagType = DstNbt.TypeFloat, .TagName = "f", .TagValue = 1.5F},
                New NbtTag With {.TagType = DstNbt.TypeDouble, .TagName = "d", .TagValue = -2.25},
                New NbtTag With {.TagType = DstNbt.TypeString, .TagName = "str", .TagValue = "你好 NBT"},
                New NbtTag With {.TagType = DstNbt.TypeByteArray, .TagName = "ba", .TagValue = New Byte() {1, 2, 3}},
                New NbtTag With {.TagType = DstNbt.TypeIntArray, .TagName = "ia", .TagValue = New Integer() {10, -20}},
                New NbtTag With {.TagType = DstNbt.TypeLongArray, .TagName = "la", .TagValue = New Long() {1L, -2L}},
                New NbtTag With {.TagType = DstNbt.TypeList, .TagName = "list", .TagValue = New List(Of NbtTag) From {
                    New NbtTag With {.TagType = DstNbt.TypeInt, .TagName = "", .TagValue = 11},
                    New NbtTag With {.TagType = DstNbt.TypeInt, .TagName = "", .TagValue = 22}}},
                NamedCompound("Compound", NamedCompound("deep", IntTag("value", 42)))
            }}

        Dim Raw As Byte() = DstNbt.WriteTag(Root)
        Dim Back As NbtTag = DstNbt.ParseBytes(Raw)

        CheckEqual(Back.TagType, DstNbt.TypeCompound, "往返：根是复合标签")
        CheckEqual(DstNbt.CompoundValue(Back, "b"), CByte(7), "往返：Byte")
        CheckEqual(DstNbt.CompoundValue(Back, "s"), CShort(-300), "往返：Short")
        CheckEqual(DstNbt.CompoundValue(Back, "i"), -123456, "往返：Int")
        CheckEqual(DstNbt.CompoundValue(Back, "l"), 1234567890123L, "往返：Long")
        CheckEqual(DstNbt.CompoundValue(Back, "f"), 1.5F, "往返：Float")
        CheckEqual(DstNbt.CompoundValue(Back, "d"), -2.25, "往返：Double")
        CheckEqual(DstNbt.CompoundString(Back, "str"), "你好 NBT", "往返：String（含中文）")
        CheckEqual(DirectCast(DstNbt.CompoundValue(Back, "ba"), Byte()).Length, 3, "往返：ByteArray 长度")
        CheckEqual(DirectCast(DstNbt.CompoundValue(Back, "ia"), Integer())(1), -20, "往返：IntArray 负值")
        CheckEqual(DirectCast(DstNbt.CompoundValue(Back, "la"), Long())(1), -2L, "往返：LongArray 负值")
        CheckEqual(DstNbt.Children(DstNbt.CompoundGet(Back, "list")).Count, 2, "往返：List 元素数")

        '深层路径要能一路取到底
        CheckEqual(DstNbt.Describe(DstNbt.GetByPath(Back, "Compound.deep.value")), "42", "往返：GetByPath 取深层值")
        Check(DstNbt.GetByPath(Back, "Compound.不存在") Is Nothing, "GetByPath 缺失返回 Nothing")
    End Sub

    Private Sub TestSignedByte()
        'NBT 的 Byte 是有符号的。负 Y 的 section 若被读成无符号，地图排序会整片错乱。
        Dim Raw As Byte() = {DstNbt.TypeByte, 0, 1, AscW("v"), 252}
        '252 作为有符号字节就是 -4
        Dim Tag As NbtTag = DstNbt.ParseBytes(Raw)
        CheckEqual(Tag.TagValue, CSByte(-4), "负 Byte 解析为 -4")
        CheckEqual(Tag.TagValue.GetType(), GetType(SByte), "Byte 标签用 SByte 存储")
    End Sub

    Private Sub TestGZip()
        Dim Root As NbtTag = Compound(IntTag("x", 9))
        Dim Temp As String = IO.Path.Combine(IO.Path.GetTempPath(), "dst_nbt_test_" & Guid.NewGuid().ToString("N") & ".dat")
        Try
            DstNbt.WriteFile(Temp, Root, UseGZip:=True)
            'GZip 的魔数必须是 1F 8B，ReadFile 靠它判断要不要解压
            Dim Head As Byte() = IO.File.ReadAllBytes(Temp)
            Check(Head.Length > 2 AndAlso Head(0) = &H1F AndAlso Head(1) = &H8B, "WriteFile 默认写出 GZip")
            CheckEqual(DstNbt.CompoundValue(DstNbt.ReadFile(Temp), "x"), 9, "GZip 往返")
        Finally
            If IO.File.Exists(Temp) Then IO.File.Delete(Temp)
        End Try
    End Sub

    Private Sub TestSetText()
        Dim B As New NbtTag With {.TagType = DstNbt.TypeByte, .TagValue = CSByte(0)}
        Check(DstNbt.SetText(B, "200"), "SetText 接受超出 SByte 的正数")
        CheckEqual(B.TagValue, CSByte(-56), "SetText 正数溢出回绕到 -56")

        Dim S As New NbtTag With {.TagType = DstNbt.TypeString, .TagValue = ""}
        Check(DstNbt.SetText(S, "abc"), "SetText 写入字符串")
        CheckEqual(S.TagValue, "abc", "SetText 字符串内容")

        Dim C As New NbtTag With {.TagType = DstNbt.TypeCompound, .TagValue = New List(Of NbtTag)}
        Check(Not DstNbt.SetText(C, "x"), "SetText 拒绝复合标签")
        Check(Not DstNbt.SetText(Nothing, "x"), "SetText 拒绝 Nothing")
    End Sub

    Private Sub TestStripNamespace()
        CheckEqual(DstNbt.StripNamespace("minecraft:overworld"), "overworld", "StripNamespace 去前缀")
        CheckEqual(DstNbt.StripNamespace("mymod:dim"), "mymod:dim", "StripNamespace 不动自定义命名空间")
        CheckEqual(DstNbt.StripNamespace(Nothing), "", "StripNamespace 处理 Nothing")
    End Sub

    '---- DstNbtPath ----

    Private Sub TestPathBasics()
        Dim P = DstNbtPath.Extend(DstNbtPath.Root(), "Inventory")
        P = DstNbtPath.ExtendIndex(P, 0)
        P = DstNbtPath.Extend(P, "id")
        CheckEqual(DstNbtPath.Describe(P), "Inventory.[0].id", "Describe 拼路径")
        CheckEqual(DstNbtPath.LastName(P), "id", "LastName 取末端")
        CheckEqual(DstNbtPath.LastName(DstNbtPath.Root()), "根标签", "LastName 空路径")
        CheckEqual(DstNbtPath.Describe(DstNbtPath.ParentOf(P)), "Inventory.[0]", "ParentOf 去掉末节")
        CheckEqual(DstNbtPath.ParentOf(DstNbtPath.Root()).Count, 0, "ParentOf 空路径仍为空")
    End Sub

    Private Sub TestPathCompare()
        Dim A = DstNbtPath.OfSteps(NbtPathStep.ByKey("a"), NbtPathStep.ByIndex(1))
        Dim B = DstNbtPath.OfSteps(NbtPathStep.ByKey("a"), NbtPathStep.ByIndex(1))
        Dim C = DstNbtPath.OfSteps(NbtPathStep.ByKey("a"), NbtPathStep.ByIndex(2))
        Check(DstNbtPath.Same(A, B), "Same 判断相同路径")
        Check(Not DstNbtPath.Same(A, C), "Same 判断不同下标")
        Check(DstNbtPath.HasPrefix(A, DstNbtPath.OfSteps(NbtPathStep.ByKey("a"))), "HasPrefix 命中")
        Check(Not DstNbtPath.HasPrefix(A, DstNbtPath.OfSteps(NbtPathStep.ByKey("b"))), "HasPrefix 不命中")
        Check(DstNbtPath.HasPrefix(A, DstNbtPath.Root()), "HasPrefix 空前缀恒真")
        'Copy 必须是深拷贝，否则改一处会连带改另一处
        Dim D = DstNbtPath.Copy(A)
        D.Add(NbtPathStep.ByKey("extra"))
        CheckEqual(A.Count, 2, "Copy 是独立副本")
    End Sub

    '---- 路径编辑 ----

    Private Function SampleRoot() As NbtTag
        Return Compound(
            New NbtTag With {.TagType = DstNbt.TypeList, .TagName = "Inventory", .TagValue = New List(Of NbtTag) From {
                Compound(StringTag("id", "minecraft:stone")),
                Compound(StringTag("id", "minecraft:dirt"))}},
            StringTag("LevelName", "测试世界"))
    End Function

    Private Sub TestResolve()
        Dim Root = SampleRoot()
        Dim P = DstNbtPath.OfSteps(NbtPathStep.ByKey("Inventory"), NbtPathStep.ByIndex(1), NbtPathStep.ByKey("id"))
        CheckEqual(DstNbt.Describe(DstNbt.Resolve(Root, P)), "minecraft:dirt", "Resolve 取到列表第二项")
        Check(DstNbt.Resolve(Root, DstNbtPath.OfSteps(NbtPathStep.ByIndex(0))) Is Nothing,
              "Resolve 对复合标签用下标返回 Nothing")
        Check(DstNbt.Resolve(Root, DstNbtPath.Root()) Is Root, "Resolve 空路径返回根")
        CheckEqual(DstNbt.Describe(DstNbt.ResolveParent(Root, P)), "{1 项}", "ResolveParent 取到父节点")
    End Sub

    Private Sub TestAddRemove()
        Dim Root = SampleRoot()
        Dim ListPath = DstNbtPath.OfSteps(NbtPathStep.ByKey("Inventory"))
        Check(DstNbt.AddChild(Root, ListPath, Compound(StringTag("id", "minecraft:apple"))), "AddChild 往列表追加")
        CheckEqual(DstNbt.Children(DstNbt.Resolve(Root, ListPath)).Count, 3, "追加后列表长度为 3")

        '列表元素插入时会清空 TagName（NBT 规范里列表元素没有名字）
        Dim Added = DstNbt.Children(DstNbt.Resolve(Root, ListPath))(2)
        CheckEqual(Added.TagName, "", "AddChild 追加的列表元素没有名字")

        Check(DstNbt.RemoveChild(Root, DstNbtPath.OfSteps(NbtPathStep.ByKey("Inventory"), NbtPathStep.ByIndex(0))),
              "RemoveChild 删除列表元素")
        CheckEqual(DstNbt.Children(DstNbt.Resolve(Root, ListPath)).Count, 2, "删除后列表长度为 2")
        Check(Not DstNbt.RemoveChild(Root, DstNbtPath.OfSteps(NbtPathStep.ByKey("Inventory"), NbtPathStep.ByIndex(99))),
              "RemoveChild 越界返回 False")
        Check(Not DstNbt.RemoveChild(Root, DstNbtPath.Root()), "RemoveChild 空路径返回 False")
    End Sub

    Private Sub TestRename()
        Dim Root = SampleRoot()
        Dim P = DstNbtPath.OfSteps(NbtPathStep.ByKey("LevelName"))
        Check(DstNbt.RenameChild(Root, P, "改名后"), "RenameChild 改名成功")
        CheckEqual(DstNbt.CompoundString(Root, "改名后"), "测试世界", "改名后能用新键名取到值")
        Check(DstNbt.CompoundGet(Root, "LevelName") Is Nothing, "旧键名已不存在")
    End Sub

End Module
