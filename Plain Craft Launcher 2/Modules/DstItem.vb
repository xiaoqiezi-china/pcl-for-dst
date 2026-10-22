''' <summary>
''' 物品堆叠（ItemStack）的读写工具。
''' 只处理数据，不做任何渲染：一次物品在 NBT 里就是一个带 id / Count / tag 的复合标签。
''' </summary>
Public Module DstItem

    ''' <summary>一格物品的完整信息，界面直接用这个。</summary>
    Public Class ItemInfo
        ''' <summary>物品 ID，例如 minecraft:diamond_sword。</summary>
        Public Id As String = ""
        ''' <summary>数量。</summary>
        Public Count As Integer = 1
        ''' <summary>那一格的复合标签（Slot、id、Count、tag）。为空格子时为 Nothing。</summary>
        Public Tag As NbtTag = Nothing
        ''' <summary>显示名称：优先用自定义名，其次用物品 ID。</summary>
        Public DisplayName As String = ""
        ''' <summary>自定义名称（item.tag.display.Name）。</summary>
        Public CustomName As String = ""
        ''' <summary>附魔数量，0 表示没有附魔。</summary>
        Public EnchantCount As Integer = 0
        ''' <summary>是否有自定义 Lore。</summary>
        Public HasLore As Boolean = False
    End Class

    ''' <summary>读取一格物品。空槽位返回 Nothing。</summary>
    Public Function Read(Tag As NbtTag) As ItemInfo
        If Tag Is Nothing Then Return Nothing
        Dim Id As String = ""
        Dim Count As Integer = 1
        If Tag.TagType = DstNbt.TypeCompound Then
            Dim IdTag As NbtTag = DstNbt.CompoundGet(Tag, "id")
            If IdTag IsNot Nothing AndAlso IdTag.TagType = DstNbt.TypeString Then Id = DirectCast(IdTag.TagValue, String)
            Dim CountTag As NbtTag = DstNbt.CompoundGet(Tag, "Count")
            If CountTag IsNot Nothing AndAlso CountTag.TagType = DstNbt.TypeByte Then Count = CInt(CountTag.TagValue)
        ElseIf Tag.TagType = DstNbt.TypeString Then
            '1.20.5 之后玩家背包有时用 "minecraft:stone" 这种纯字符串表示
            Id = DirectCast(Tag.TagValue, String)
        End If
        If Id = "" Then Return Nothing
        Dim Info As New ItemInfo With {.Id = Id, .Count = Count, .Tag = Tag}
        Dim Custom As String = CustomNameOf(Tag)
        Info.CustomName = Custom
        Info.DisplayName = If(Custom <> "", Custom & "（" & DstNbt.StripNamespace(Id) & "）", DstNbt.StripNamespace(Id))
        Dim Ench As NbtTag = EnchantmentsOf(Tag)
        If Ench IsNot Nothing Then
            Dim Items As List(Of NbtTag) = DstNbt.Children(Ench)
            Info.EnchantCount = Items.Count
        End If
        Dim Lore As NbtTag = DstNbt.GetByPath(Tag, "tag.display.Lore")
        Info.HasLore = Lore IsNot Nothing AndAlso DstNbt.Children(Lore).Count > 0
        Return Info
    End Function

    ''' <summary>取 item.tag 这个复合标签，没有就建一个。</summary>
    Public Function EnsureTag(Slot As NbtTag) As NbtTag
        If Slot Is Nothing Then Return Nothing
        Dim Inner As NbtTag = DstNbt.CompoundGet(Slot, "tag")
        If Inner IsNot Nothing AndAlso Inner.TagType = DstNbt.TypeCompound Then Return Inner
        Inner = New NbtTag With {.TagType = DstNbt.TypeCompound, .TagName = "tag", .TagValue = New List(Of NbtTag)}
        DstNbt.AddChild(Slot, DstNbtPath.OfSteps(), Inner)
        Return Inner
    End Function

    ''' <summary>自定义名称（display.Name），没有返回空串。</summary>
    Public Function CustomNameOf(Slot As NbtTag) As String
        If Slot Is Nothing Then Return ""
        Dim Tag As NbtTag = DstNbt.GetByPath(Slot, "tag.display.Name")
        If Tag Is Nothing Then Return ""
        Dim Text As String = DstNbt.Describe(Tag)
        'JSON 文本里最常见的写法是 {"text":"名字"}
        If Text.Contains("""text""") Then
            Try
                Dim Obj As JObject = JObject.Parse(Text)
                If Obj("text") IsNot Nothing Then Return Obj("text").ToString()
                Return Text
            Catch
                Return Text
            End Try
        End If
        If Text.StartsWith("""") AndAlso Text.EndsWith("""") AndAlso Text.Length >= 2 Then Return Text.Substring(1, Text.Length - 2)
        Return Text
    End Function

    ''' <summary>附魔列表标签（tag.ench 或 tag.Enchantments），没有返回 Nothing。</summary>
    Public Function EnchantmentsOf(Slot As NbtTag) As NbtTag
        If Slot Is Nothing Then Return Nothing
        Dim Tag As NbtTag = DstNbt.GetByPath(Slot, "tag.ench")
        If Tag IsNot Nothing Then Return Tag
        Return DstNbt.GetByPath(Slot, "tag.Enchantments")
    End Function

    ''' <summary>槽位编号。没有 Slot 字段（比如末影箱按数组下标存）时返回 -1。</summary>
    Public Function SlotIndexOf(Slot As NbtTag) As Integer
        If Slot Is Nothing Then Return -1
        Dim Tag As NbtTag = DstNbt.CompoundGet(Slot, "Slot")
        If Tag Is Nothing Then Return -1
        If Tag.TagType = DstNbt.TypeByte Then Return CInt(Tag.TagValue)
        Return -1
    End Function

    ''' <summary>新建一格物品。</summary>
    Public Function NewSlot(Id As String, Count As Integer, Optional SlotIndex As Integer = -1) As NbtTag
        Dim Items As New List(Of NbtTag)
        If SlotIndex >= 0 Then
            Items.Add(New NbtTag With {.TagType = DstNbt.TypeByte, .TagName = "Slot", .TagValue = CSByte(SlotIndex And &HFF)})
        End If
        Items.Add(New NbtTag With {.TagType = DstNbt.TypeString, .TagName = "id", .TagValue = If(Id = "", "minecraft:stone", Id)})
        Items.Add(New NbtTag With {.TagType = DstNbt.TypeByte, .TagName = "Count", .TagValue = CSByte(Math.Max(1, Math.Min(127, Count)))})
        Return New NbtTag With {.TagType = DstNbt.TypeCompound, .TagName = "", .TagValue = Items}
    End Function

    ''' <summary>修改一格物品的 ID 与数量。</summary>
    Public Sub SetIdAndCount(Slot As NbtTag, Id As String, Count As Integer)
        If Slot Is Nothing OrElse Slot.TagType <> DstNbt.TypeCompound Then Return
        Dim IdTag As NbtTag = DstNbt.CompoundGet(Slot, "id")
        If IdTag Is Nothing Then
            IdTag = New NbtTag With {.TagType = DstNbt.TypeString, .TagName = "id"}
            DstNbt.AddChild(Slot, DstNbtPath.OfSteps(), IdTag)
        End If
        IdTag.TagType = DstNbt.TypeString
        IdTag.TagValue = If(Id = "", "minecraft:stone", Id)
        Dim CountTag As NbtTag = DstNbt.CompoundGet(Slot, "Count")
        If CountTag Is Nothing Then
            CountTag = New NbtTag With {.TagType = DstNbt.TypeByte, .TagName = "Count"}
            DstNbt.AddChild(Slot, DstNbtPath.OfSteps(), CountTag)
        End If
        CountTag.TagType = DstNbt.TypeByte
        CountTag.TagValue = CSByte(Math.Max(1, Math.Min(127, Count)))
    End Sub

    ''' <summary>
    ''' 背包的槽位布局。
    ''' 左边是背包主区（槽位 9–35）与快捷栏（槽位 0–8），右边单独一块是护甲（36–39）与副手（40）。
    ''' 全部按 Minecraft 的真实槽位号排，物品的 Slot 字段与格子位置严格一一对应。
    ''' </summary>
    Public Class SlotCell
        ''' <summary>列表里的下标；-1 表示这一格没有对应的元素（空格子）。</summary>
        Public ListIndex As Integer = -1
        ''' <summary>格子下方的标题，比如「护甲·头」「快捷栏 1」。</summary>
        Public Caption As String = ""
        ''' <summary>这一格属于哪个字段（Inventory / EnderItems）。</summary>
        Public Field As String = ""
        ''' <summary>槽位编号（物品里的 Slot 字段）。</summary>
        Public SlotIndex As Integer = -1
        ''' <summary>属于哪一块：True 是右侧的装备区，False 是左侧的背包区。</summary>
        Public IsEquip As Boolean = False
    End Class

    ''' <summary>
    ''' 左侧背包区：4 行 × 9 列 = 36 格。第 1–3 行是背包主区（槽位 9–35），第 4 行是快捷栏（槽位 0–8）。
    ''' </summary>
    Public Function BuildGrid(ListTag As NbtTag, Field As String) As List(Of SlotCell)
        Dim Cells As New List(Of SlotCell)
        For Row As Integer = 0 To 3
            For Col As Integer = 0 To 8
                Dim SlotIndex As Integer
                Dim Caption As String
                If Row < 3 Then
                    '背包主区：槽位 9 + 行*9 + 列
                    SlotIndex = 9 + Row * 9 + Col
                    Caption = "背包 " & (Row * 9 + Col + 1)
                Else
                    '快捷栏：槽位 0..8
                    SlotIndex = Col
                    Caption = "快捷栏 " & (Col + 1)
                End If
                Cells.Add(New SlotCell With {
                    .Field = Field, .SlotIndex = SlotIndex, .Caption = Caption, .IsEquip = False
                })
            Next
        Next
        PlaceItems(Cells, DstNbt.Children(ListTag))
        Return Cells
    End Function

    ''' <summary>右侧装备区：护甲 36–39 与副手 40，一列五格。</summary>
    Public Function BuildEquipGrid(ListTag As NbtTag, Field As String) As List(Of SlotCell)
        Dim Cells As New List(Of SlotCell)
        Dim Captions As String() = {"护甲·头", "护甲·胸", "护甲·腿", "护甲·脚", "副手"}
        For i = 0 To 4
            Cells.Add(New SlotCell With {
                .Field = Field, .SlotIndex = 36 + i, .Caption = Captions(i), .IsEquip = True
            })
        Next
        PlaceItems(Cells, DstNbt.Children(ListTag))
        Return Cells
    End Function

    ''' <summary>
    ''' 把物品放到对应槽位的格子上。
    ''' 先认物品自带的 Slot 字段；没有 Slot 字段的，按顺序填进还没被占的背包区格子。
    ''' </summary>
    Private Sub PlaceItems(Cells As List(Of SlotCell), Items As List(Of NbtTag))
        Dim IndexOfSlot As New Dictionary(Of Integer, Integer)
        For i = 0 To Cells.Count - 1
            IndexOfSlot(Cells(i).SlotIndex) = i
        Next
        For i = 0 To Items.Count - 1
            Dim SlotIndex As Integer = SlotIndexOf(Items(i))
            If SlotIndex >= 0 Then
                Dim CellPos As Integer = 0
                If IndexOfSlot.TryGetValue(SlotIndex, CellPos) Then
                    Cells(CellPos).ListIndex = i
                End If
            End If
        Next
        '没认出来的按顺序塞进空格子
        For i = 0 To Items.Count - 1
            Dim SlotIndex As Integer = SlotIndexOf(Items(i))
            If SlotIndex >= 0 Then Continue For
            For Each Cell In Cells
                If Cell.ListIndex < 0 Then
                    Cell.ListIndex = i
                    Exit For
                End If
            Next
        Next
    End Sub

    ''' <summary>末影箱的布局：27 格，三行九列。</summary>
    Public Function BuildEnderGrid(ListTag As NbtTag) As List(Of SlotCell)
        Dim Cells As New List(Of SlotCell)
        Dim Items As List(Of NbtTag) = DstNbt.Children(ListTag)
        For i = 0 To 26
            Cells.Add(New SlotCell With {
                .Field = "EnderItems",
                .SlotIndex = i,
                .Caption = (i \ 9 + 1) & "-" & (i Mod 9 + 1)
            })
        Next
        For i = 0 To Items.Count - 1
            Dim SlotIndex As Integer = SlotIndexOf(Items(i))
            Dim Pos As Integer = If(SlotIndex >= 0, SlotIndex, i)
            If Pos < 0 OrElse Pos > 26 Then Continue For
            Dim Guard As Integer = 0
            While Cells(Pos).ListIndex >= 0 AndAlso Guard < 27
                Pos = (Pos + 1) Mod 27
                Guard += 1
            End While
            Cells(Pos).ListIndex = i
        Next
        Return Cells
    End Function

End Module
