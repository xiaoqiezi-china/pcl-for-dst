''' <summary>
''' 一个 NBT 标签。标签类型决定了标签值的含义。
''' </summary>
Public Class NbtTag
    ''' <summary>标签类型。参见 DstNbt 中的常量。</summary>
    Public TagType As Integer = 0
    ''' <summary>标签名称。复合标签内的标签才有名称。</summary>
    Public TagName As String = ""
    ''' <summary>标签的值。</summary>
    Public TagValue As Object = Nothing
End Class

''' <summary>
''' 一个指向 NBT 节点位置的路径步。可以是复合标签的键名，也可以是列表的下标。
''' </summary>
Public Structure NbtPathStep
    ''' <summary>复合标签中的键名；为 Nothing 时表示列表下标。</summary>
    Public Key As String
    ''' <summary>列表下标；为 -1 时表示键名。</summary>
    Public Index As Integer

    Public Shared Function ByKey(KeyName As String) As NbtPathStep
        Return New NbtPathStep With {.Key = KeyName, .Index = -1}
    End Function

    Public Shared Function ByIndex(IndexValue As Integer) As NbtPathStep
        Return New NbtPathStep With {.Key = Nothing, .Index = IndexValue}
    End Function

    Public ReadOnly Property IsIndex As Boolean
        Get
            Return Index >= 0
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return If(IsIndex, "[" & Index & "]", Key)
    End Function
End Structure

''' <summary>
''' Minecraft 的 NBT 数据格式的读取与写入。
''' 用于解析 level.dat、玩家数据、区域文件等。
''' </summary>
Public Module DstNbt

    Public Const TypeEnd As Integer = 0
    Public Const TypeByte As Integer = 1
    Public Const TypeShort As Integer = 2
    Public Const TypeInt As Integer = 3
    Public Const TypeLong As Integer = 4
    Public Const TypeFloat As Integer = 5
    Public Const TypeDouble As Integer = 6
    Public Const TypeByteArray As Integer = 7
    Public Const TypeString As Integer = 8
    Public Const TypeList As Integer = 9
    Public Const TypeCompound As Integer = 10
    Public Const TypeIntArray As Integer = 11
    Public Const TypeLongArray As Integer = 12

    ''' <summary>标签类型的名称，用于显示。</summary>
    Public Function TypeName(TypeId As Integer) As String
        Select Case TypeId
            Case TypeEnd
                Return "End"
            Case TypeByte
                Return "Byte"
            Case TypeShort
                Return "Short"
            Case TypeInt
                Return "Int"
            Case TypeLong
                Return "Long"
            Case TypeFloat
                Return "Float"
            Case TypeDouble
                Return "Double"
            Case TypeByteArray
                Return "ByteArray"
            Case TypeString
                Return "String"
            Case TypeList
                Return "List"
            Case TypeCompound
                Return "Compound"
            Case TypeIntArray
                Return "IntArray"
            Case TypeLongArray
                Return "LongArray"
            Case Else
                Return "Unknown"
        End Select
    End Function

#Region "文件读写"

    ''' <summary>
    ''' 读取 NBT 文件，自动识别 GZip 压缩。
    ''' </summary>
    Public Function ReadFile(FilePath As String) As NbtTag
        Dim Raw As Byte() = File.ReadAllBytes(FilePath)
        If Raw.Length < 2 Then Throw New Exception("NBT 文件长度异常")
        If Raw(0) = &H1F AndAlso Raw(1) = &H8B Then Raw = Decompress(Raw)
        Return ParseBytes(Raw)
    End Function

    ''' <summary>
    ''' 把 NBT 写入文件。
    ''' </summary>
    Public Sub WriteFile(FilePath As String, Root As NbtTag, Optional UseGZip As Boolean = True)
        Dim Raw As Byte() = WriteTag(Root)
        If UseGZip Then Raw = Compress(Raw)
        File.WriteAllBytes(FilePath, Raw)
    End Sub

    ''' <summary>把 NBT 序列化为字节数组（不压缩）。</summary>
    Public Function WriteTag(Tag As NbtTag) As Byte()
        Using Ms As New MemoryStream
            WriteTagTo(Ms, Tag)
            Return Ms.ToArray()
        End Using
    End Function

    ''' <summary>把字节数组解析为 NBT（不压缩）。</summary>
    Public Function ParseBytes(Raw As Byte()) As NbtTag
        If Raw.Length < 2 Then Throw New Exception("NBT 数据长度异常")
        Dim Pos As Integer = 0
        Return ReadTag(Raw, Pos)
    End Function

    Private Function Compress(Raw As Byte()) As Byte()
        Using Ms As New MemoryStream
            Using Gz As New GZipStream(Ms, CompressionMode.Compress, True)
                Gz.Write(Raw, 0, Raw.Length)
            End Using
            Return Ms.ToArray()
        End Using
    End Function

    Private Function Decompress(Raw As Byte()) As Byte()
        Using Ms As New MemoryStream(Raw)
            Using Gz As New GZipStream(Ms, CompressionMode.Decompress)
                Using OutMs As New MemoryStream
                    Gz.CopyTo(OutMs)
                    Return OutMs.ToArray()
                End Using
            End Using
        End Using
    End Function

#End Region

#Region "标签访问"
    ''' <summary>获取复合标签中指定名称的子标签。</summary>
    Public Function CompoundGet(Tag As NbtTag, ChildName As String) As NbtTag
        If Tag Is Nothing Then Return Nothing
        If Tag.TagType <> TypeCompound Then Return Nothing
        Dim Items As List(Of NbtTag) = TryCast(Tag.TagValue, List(Of NbtTag))
        If Items Is Nothing Then Return Nothing
        For Each Item In Items
            If Item.TagName = ChildName Then Return Item
        Next
        Return Nothing
    End Function

    ''' <summary>获取复合标签中指定名称的子标签的值。</summary>
    Public Function CompoundValue(Tag As NbtTag, ChildName As String) As Object
        Dim Child As NbtTag = CompoundGet(Tag, ChildName)
        If Child Is Nothing Then Return Nothing
        Return Child.TagValue
    End Function

    ''' <summary>获取复合标签中指定名称的字符串值。</summary>
    Public Function CompoundString(Tag As NbtTag, ChildName As String) As String
        Dim Child As NbtTag = CompoundGet(Tag, ChildName)
        If Child Is Nothing Then Return Nothing
        If Child.TagType <> TypeString Then Return Nothing
        Return DirectCast(Child.TagValue, String)
    End Function

    ''' <summary>设置复合标签中指定名称的字符串值。若标签不存在则返回 False。</summary>
    Public Function CompoundSetString(Tag As NbtTag, ChildName As String, Value As String) As Boolean
        Dim Target As NbtTag = CompoundGet(Tag, ChildName)
        If Target Is Nothing Then Return False
        Target.TagType = TypeString
        Target.TagValue = Value
        Return True
    End Function

    ''' <summary>按照 Data.LevelName 这样的路径查找标签。</summary>
    Public Function GetByPath(Root As NbtTag, Path As String) As NbtTag
        Dim Cur As NbtTag = Root
        For Each Part In Path.Split(".")
            If Part = "" Then Continue For
            Cur = CompoundGet(Cur, Part)
            If Cur Is Nothing Then Return Nothing
        Next
        Return Cur
    End Function

    ''' <summary>把标签转换为便于显示的文本。</summary>
    Public Function Describe(Tag As NbtTag) As String
        If Tag Is Nothing Then Return "（空）"
        Select Case Tag.TagType
            Case TypeString
                Return DirectCast(Tag.TagValue, String)
            Case TypeList
                Dim Items As List(Of NbtTag) = TryCast(Tag.TagValue, List(Of NbtTag))
                If Items Is Nothing Then Return "[]"
                Return "[" & Items.Count & " 项]"
            Case TypeCompound
                Dim Items2 As List(Of NbtTag) = TryCast(Tag.TagValue, List(Of NbtTag))
                If Items2 Is Nothing Then Return "{}"
                Return "{" & Items2.Count & " 项}"
            Case TypeByteArray
                Dim Bytes As Byte() = TryCast(Tag.TagValue, Byte())
                If Bytes Is Nothing Then Return "[]"
                Return "[" & Bytes.Length & " 字节]"
            Case TypeIntArray
                Dim Ints As Integer() = TryCast(Tag.TagValue, Integer())
                If Ints Is Nothing Then Return "[]"
                Return "[" & Ints.Length & " 个 int]"
            Case TypeLongArray
                Dim Longs As Long() = TryCast(Tag.TagValue, Long())
                If Longs Is Nothing Then Return "[]"
                Return "[" & Longs.Length & " 个 long]"
            Case Else
                If Tag.TagValue Is Nothing Then Return ""
                Return Tag.TagValue.ToString
        End Select
    End Function

#End Region

#Region "路径与编辑"

    ''' <summary>按照整条路径查找标签，路径为空时返回根标签。</summary>
    Public Function Resolve(Root As NbtTag, Steps As List(Of NbtPathStep)) As NbtTag
        Dim Cur As NbtTag = Root
        If Steps Is Nothing Then Return Cur
        For Each PathStep In Steps
            Cur = GetChild(Cur, PathStep)
            If Cur Is Nothing Then Return Nothing
        Next
        Return Cur
    End Function

    ''' <summary>取一个子节点。复合标签按键名，列表按下标。</summary>
    Public Function GetChild(Tag As NbtTag, PathStep As NbtPathStep) As NbtTag
        If Tag Is Nothing Then Return Nothing
        If PathStep.IsIndex Then
            If Tag.TagType <> TypeList Then Return Nothing
            Dim Items As List(Of NbtTag) = TryCast(Tag.TagValue, List(Of NbtTag))
            If Items Is Nothing OrElse PathStep.Index < 0 OrElse PathStep.Index >= Items.Count Then Return Nothing
            Return Items(PathStep.Index)
        Else
            Return CompoundGet(Tag, PathStep.Key)
        End If
    End Function

    ''' <summary>按路径查找父标签。路径为空时返回 Nothing。</summary>
    Public Function ResolveParent(Root As NbtTag, Steps As List(Of NbtPathStep)) As NbtTag
        If Steps Is Nothing OrElse Steps.Count = 0 Then Return Nothing
        Return Resolve(Root, Steps.GetRange(0, Steps.Count - 1))
    End Function

    ''' <summary>取父标签下的一批子标签。复合标签返回全部子项，列表返回全部元素。</summary>
    Public Function Children(Tag As NbtTag) As List(Of NbtTag)
        If Tag Is Nothing Then Return New List(Of NbtTag)
        If Tag.TagType = TypeCompound OrElse Tag.TagType = TypeList Then
            Dim Items As List(Of NbtTag) = TryCast(Tag.TagValue, List(Of NbtTag))
            If Items IsNot Nothing Then Return Items
        End If
        Return New List(Of NbtTag)
    End Function

    ''' <summary>在路径指定的父标签里添加一个子标签。列表会自动追加到末尾。</summary>
    Public Function AddChild(Root As NbtTag, ParentSteps As List(Of NbtPathStep), NewTag As NbtTag) As Boolean
        Dim ParentTag As NbtTag = Resolve(Root, ParentSteps)
        If ParentTag Is Nothing Then Return False
        If ParentTag.TagType = TypeCompound Then
            Dim Items As List(Of NbtTag) = TryCast(ParentTag.TagValue, List(Of NbtTag))
            If Items Is Nothing Then Return False
            Items.Add(NewTag)
            Return True
        ElseIf ParentTag.TagType = TypeList Then
            Dim Items2 As List(Of NbtTag) = TryCast(ParentTag.TagValue, List(Of NbtTag))
            If Items2 Is Nothing Then Return False
            Dim Clone As New NbtTag With {.TagType = NewTag.TagType, .TagName = "", .TagValue = NewTag.TagValue}
            Items2.Add(Clone)
            Return True
        End If
        Return False
    End Function

    ''' <summary>删除路径指定的标签。</summary>
    Public Function RemoveChild(Root As NbtTag, Steps As List(Of NbtPathStep)) As Boolean
        If Steps Is Nothing OrElse Steps.Count = 0 Then Return False
        Dim ParentTag As NbtTag = ResolveParent(Root, Steps)
        If ParentTag Is Nothing Then Return False
        Dim Last As NbtPathStep = Steps(Steps.Count - 1)
        If ParentTag.TagType = TypeCompound AndAlso Not Last.IsIndex Then
            Dim Items As List(Of NbtTag) = TryCast(ParentTag.TagValue, List(Of NbtTag))
            If Items Is Nothing Then Return False
            For i = 0 To Items.Count - 1
                If Items(i).TagName = Last.Key Then
                    Items.RemoveAt(i)
                    Return True
                End If
            Next
            Return False
        ElseIf ParentTag.TagType = TypeList AndAlso Last.IsIndex Then
            Dim Items2 As List(Of NbtTag) = TryCast(ParentTag.TagValue, List(Of NbtTag))
            If Items2 Is Nothing OrElse Last.Index < 0 OrElse Last.Index >= Items2.Count Then Return False
            Items2.RemoveAt(Last.Index)
            Return True
        End If
        Return False
    End Function

    ''' <summary>给路径指定的标签改名（只对复合标签的子项有效）。</summary>
    Public Function RenameChild(Root As NbtTag, Steps As List(Of NbtPathStep), NewName As String) As Boolean
        If Steps Is Nothing OrElse Steps.Count = 0 Then Return False
        Dim ParentTag As NbtTag = ResolveParent(Root, Steps)
        If ParentTag Is Nothing OrElse ParentTag.TagType <> TypeCompound Then Return False
        Dim Target As NbtTag = Resolve(Root, Steps)
        If Target Is Nothing Then Return False
        Target.TagName = NewName
        Return True
    End Function

    ''' <summary>列表的元素类型（取第一个元素的类型，空列表返回 End）。</summary>
    Public Function ListItemType(Tag As NbtTag) As Integer
        If Tag Is Nothing OrElse Tag.TagType <> TypeList Then Return TypeEnd
        Dim Items As List(Of NbtTag) = TryCast(Tag.TagValue, List(Of NbtTag))
        If Items Is Nothing OrElse Items.Count = 0 Then Return TypeEnd
        Return Items(0).TagType
    End Function

    ''' <summary>把文本写入标签，按标签原本的类型解析。解析失败返回 False。</summary>
    Public Function SetText(Tag As NbtTag, Text As String) As Boolean
        If Tag Is Nothing Then Return False
        Try
            Select Case Tag.TagType
                Case TypeByte
                    'SByte 取值范围是 -128..127
                    Dim V As Long = CLng(Text)
                    If V > 127 Then V -= 256
                    If V < -128 Then V += 256
                    Tag.TagValue = CSByte(V)
                Case TypeShort
                    Tag.TagValue = CShort(CLng(Text))
                Case TypeInt
                    Tag.TagValue = CInt(CLng(Text))
                Case TypeLong
                    Tag.TagValue = CLng(Text)
                Case TypeFloat
                    Tag.TagValue = CSng(Val(Text))
                Case TypeDouble
                    Tag.TagValue = CDbl(Val(Text))
                Case TypeString
                    Tag.TagValue = Text
                Case Else
                    Return False
            End Select
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>数据包、维度这类资源位置用于显示时去掉 minecraft: 前缀。</summary>
    Public Function StripNamespace(Id As String) As String
        If Id Is Nothing Then Return ""
        If Id.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) Then Return Id.Substring(10)
        Return Id
    End Function

#End Region

#Region "底层解析"

    Private Function ReadTag(Raw As Byte(), ByRef Pos As Integer) As NbtTag
        Dim TypeId As Integer = CInt(Raw(Pos))
        Pos += 1
        If TypeId = TypeEnd Then
            Return New NbtTag With {.TagType = TypeEnd, .TagName = "", .TagValue = Nothing}
        End If
        Dim NameText As String = ReadString(Raw, Pos)
        Dim Tag As New NbtTag With {.TagType = TypeId, .TagName = NameText}
        Tag.TagValue = ReadPayload(Raw, Pos, TypeId)
        Return Tag
    End Function

    Private Function ReadPayload(Raw As Byte(), ByRef Pos As Integer, TypeId As Integer) As Object
        Select Case TypeId
            Case TypeByte
                Return ReadByteValue(Raw, Pos)
            Case TypeShort
                Return ReadShortValue(Raw, Pos)
            Case TypeInt
                Return ReadIntValue(Raw, Pos)
            Case TypeLong
                Return ReadLongValue(Raw, Pos)
            Case TypeFloat
                Return ReadFloatValue(Raw, Pos)
            Case TypeDouble
                Return ReadDoubleValue(Raw, Pos)
            Case TypeByteArray
                Dim Len As Integer = ReadIntValue(Raw, Pos)
                If Len <= 0 Then Return New Byte() {}
                Dim Bytes As Byte() = New Byte(Len - 1) {}
                Array.Copy(Raw, Pos, Bytes, 0, Len)
                Pos += Len
                Return Bytes
            Case TypeString
                Return ReadString(Raw, Pos)
            Case TypeList
                Dim ItemType As Integer = CInt(Raw(Pos))
                Pos += 1
                Dim Count As Integer = ReadIntValue(Raw, Pos)
                Dim Items As New List(Of NbtTag)
                For i = 0 To Count - 1
                    Dim Item As New NbtTag With {.TagType = ItemType, .TagName = ""}
                    Item.TagValue = ReadPayload(Raw, Pos, ItemType)
                    Items.Add(Item)
                Next
                Return Items
            Case TypeCompound
                Dim Items2 As New List(Of NbtTag)
                Do
                    Dim Child As NbtTag = ReadTag(Raw, Pos)
                    If Child.TagType = TypeEnd Then Exit Do
                    Items2.Add(Child)
                Loop
                Return Items2
            Case TypeIntArray
                Dim Len2 As Integer = ReadIntValue(Raw, Pos)
                If Len2 <= 0 Then Return New Integer() {}
                Dim Arr As Integer() = New Integer(Len2 - 1) {}
                For i = 0 To Len2 - 1
                    Arr(i) = ReadIntValue(Raw, Pos)
                Next
                Return Arr
            Case TypeLongArray
                Dim Len3 As Integer = ReadIntValue(Raw, Pos)
                If Len3 <= 0 Then Return New Long() {}
                Dim Arr2 As Long() = New Long(Len3 - 1) {}
                For i = 0 To Len3 - 1
                    Arr2(i) = ReadLongValue(Raw, Pos)
                Next
                Return Arr2
            Case Else
                Throw New Exception("未知的 NBT 标签类型：" & TypeId)
        End Select
    End Function

    ''' <summary>
    ''' 读取 TAG_Byte。NBT 的 Byte 是**有符号**的 8 位整数，必须用 SByte 存。
    ''' 用 Byte 会把 -4 读成 252，负 Y 的 section 排序后会跑到最上面，整张地图就废了。
    ''' </summary>
    Private Function ReadByteValue(Raw As Byte(), ByRef Pos As Integer) As SByte
        Dim V As Integer = CInt(Raw(Pos))
        Pos += 1
        If V >= 128 Then V -= 256
        Return CSByte(V)
    End Function

    Private Function ReadShortValue(Raw As Byte(), ByRef Pos As Integer) As Int16
        Dim V As Int32 = (CInt(Raw(Pos)) << 8) Or CInt(Raw(Pos + 1))
        Pos += 2
        If V >= 32768 Then V -= 65536
        Return CShort(V)
    End Function

    Private Function ReadIntValue(Raw As Byte(), ByRef Pos As Integer) As Int32
        Dim V As Int32 = (CInt(Raw(Pos)) << 24) Or (CInt(Raw(Pos + 1)) << 16) Or
                         (CInt(Raw(Pos + 2)) << 8) Or CInt(Raw(Pos + 3))
        Pos += 4
        Return V
    End Function

    Private Function ReadLongValue(Raw As Byte(), ByRef Pos As Integer) As Int64
        Dim V As Int64 = 0
        For i = 0 To 7
            V = (V << 8) Or CInt(Raw(Pos + i))
        Next
        Pos += 8
        Return V
    End Function

    Private Function ReadFloatValue(Raw As Byte(), ByRef Pos As Integer) As Single
        Dim B As Byte() = {Raw(Pos + 3), Raw(Pos + 2), Raw(Pos + 1), Raw(Pos)}
        Pos += 4
        Return BitConverter.ToSingle(B, 0)
    End Function

    Private Function ReadDoubleValue(Raw As Byte(), ByRef Pos As Integer) As Double
        Dim B As Byte() = New Byte(7) {}
        For i = 0 To 7
            B(i) = Raw(Pos + 7 - i)
        Next
        Pos += 8
        Return BitConverter.ToDouble(B, 0)
    End Function

    Private Function ReadString(Raw As Byte(), ByRef Pos As Integer) As String
        Dim Len As Integer = (CInt(Raw(Pos)) << 8) Or CInt(Raw(Pos + 1))
        Pos += 2
        If Len <= 0 Then Return ""
        Dim B As Byte() = New Byte(Len - 1) {}
        Array.Copy(Raw, Pos, B, 0, Len)
        Pos += Len
        Return Encoding.UTF8.GetString(B)
    End Function

#End Region

#Region "底层序列化"

    Private Sub WriteTagTo(Ms As MemoryStream, Tag As NbtTag)
        Ms.WriteByte(CByte(Tag.TagType))
        If Tag.TagType = TypeEnd Then Exit Sub
        WriteString(Ms, Tag.TagName)
        WritePayload(Ms, Tag.TagType, Tag.TagValue)
    End Sub

    Private Sub WritePayload(Ms As MemoryStream, TypeId As Integer, Value As Object)
        Select Case TypeId
            Case TypeByte
                Ms.WriteByte(CByte(CInt(CSByte(Value)) And &HFF))
            Case TypeShort
                WriteShortValue(Ms, CInt(Value))
            Case TypeInt
                WriteIntValue(Ms, CInt(Value))
            Case TypeLong
                WriteLongValue(Ms, CLng(Value))
            Case TypeFloat
                Dim B As Byte() = BitConverter.GetBytes(CSng(Value))
                Ms.WriteByte(B(3))
                Ms.WriteByte(B(2))
                Ms.WriteByte(B(1))
                Ms.WriteByte(B(0))
            Case TypeDouble
                Dim B2 As Byte() = BitConverter.GetBytes(CDbl(Value))
                For i = 7 To 0 Step -1
                    Ms.WriteByte(B2(i))
                Next
            Case TypeByteArray
                Dim Bytes As Byte() = DirectCast(Value, Byte())
                WriteIntValue(Ms, Bytes.Length)
                Ms.Write(Bytes, 0, Bytes.Length)
            Case TypeString
                WriteString(Ms, DirectCast(Value, String))
            Case TypeList
                Dim Items As List(Of NbtTag) = DirectCast(Value, List(Of NbtTag))
                Dim ItemType As Integer = TypeEnd
                If Items.Count > 0 Then ItemType = Items(0).TagType
                Ms.WriteByte(CByte(ItemType))
                WriteIntValue(Ms, Items.Count)
                For Each Item In Items
                    WritePayload(Ms, ItemType, Item.TagValue)
                Next
            Case TypeCompound
                Dim Items2 As List(Of NbtTag) = DirectCast(Value, List(Of NbtTag))
                For Each Item In Items2
                    WriteTagTo(Ms, Item)
                Next
                Ms.WriteByte(CByte(TypeEnd))
            Case TypeIntArray
                Dim Arr As Integer() = DirectCast(Value, Integer())
                WriteIntValue(Ms, Arr.Length)
                For Each V In Arr
                    WriteIntValue(Ms, V)
                Next
            Case TypeLongArray
                Dim Arr2 As Long() = DirectCast(Value, Long())
                WriteIntValue(Ms, Arr2.Length)
                For Each V In Arr2
                    WriteLongValue(Ms, V)
                Next
            Case Else
                Throw New Exception("未知的 NBT 标签类型：" & TypeId)
        End Select
    End Sub

    Private Sub WriteShortValue(Ms As MemoryStream, V As Int32)
        Dim U As Integer = V And &HFFFF
        Ms.WriteByte(CByte(U >> 8))
        Ms.WriteByte(CByte(U And &HFF))
    End Sub

    Private Sub WriteIntValue(Ms As MemoryStream, V As Int32)
        Ms.WriteByte(CByte((V >> 24) And &HFF))
        Ms.WriteByte(CByte((V >> 16) And &HFF))
        Ms.WriteByte(CByte((V >> 8) And &HFF))
        Ms.WriteByte(CByte(V And &HFF))
    End Sub

    Private Sub WriteLongValue(Ms As MemoryStream, V As Int64)
        For i = 7 To 0 Step -1
            Ms.WriteByte(CByte((V >> (i * 8)) And &HFF))
        Next
    End Sub

    Private Sub WriteString(Ms As MemoryStream, Text As String)
        Dim B As Byte() = Encoding.UTF8.GetBytes(Text)
        WriteShortValue(Ms, B.Length)
        If B.Length > 0 Then Ms.Write(B, 0, B.Length)
    End Sub

#End Region

End Module
