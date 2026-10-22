''' <summary>
''' 物品栏管理器与末影箱管理器。
''' 不做任何渲染：画出格子，点一格就展示那一格的物品信息并允许编辑。
''' </summary>
Public Class DstItemPanel

    ''' <summary>正在编辑的文档。</summary>
    Public ReadOnly Property Doc As DstNbtDocument
    ''' <summary>列表字段名：Inventory 或 EnderItems。</summary>
    Private ReadOnly Field As String
    ''' <summary>容器：调用方插入界面元素的地方。</summary>
    Private ReadOnly Host As Panel
    ''' <summary>确认可以写入之后才执行。</summary>
    Private ReadOnly Persist As Func(Of Boolean)
    ''' <summary>通用的「查看原始 NBT」入口，点开某个格子时可以跳过去。</summary>
    Private ReadOnly OpenRaw As Action(Of List(Of NbtPathStep))

    ''' <summary>当前选中的格子下标，-1 表示没选。</summary>
    Private Selected As Integer = -1

    Private GridHost As System.Windows.Controls.Primitives.UniformGrid = Nothing
    Private EquipHost As System.Windows.Controls.Primitives.UniformGrid = Nothing
    Private EquipBox As StackPanel = Nothing
    Private MissingBox As StackPanel = Nothing
    Private DetailHost As StackPanel = Nothing

    ''' <summary>当前所有格子（左侧背包 + 右侧装备），选中信息从这里取。</summary>
    Private AllCells As New List(Of DstItem.SlotCell)

    Public Sub New(Document As DstNbtDocument, ListField As String, Outer As Panel,
                   OnPersist As Func(Of Boolean), OpenRawEditor As Action(Of List(Of NbtPathStep)))
        Doc = Document
        Field = ListField
        Host = Outer
        Persist = OnPersist
        OpenRaw = OpenRawEditor
        Dim Panel As New StackPanel
        Panel.Children.Add(New TextBlock With {
            .Text = If(Field = "EnderItems",
                       "末影箱有 27 格。点一格就能看那一格的物品，可以改、删、或往空格子里放新东西。",
                       "左边是背包与快捷栏，右边是护甲与副手。点一格就能看那一格的物品，可以改、删、或往空格子里放新东西。"),
            .FontSize = 11.5, .Opacity = 0.55, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 4, 0, 10)
        })
        '两栏：左边背包，右边装备
        Dim Columns As New StackPanel With {.Orientation = Orientation.Horizontal, .HorizontalAlignment = HorizontalAlignment.Left}
        GridHost = New System.Windows.Controls.Primitives.UniformGrid With {
            .Columns = 9, .HorizontalAlignment = HorizontalAlignment.Left
        }
        Columns.Children.Add(GridHost)
        EquipBox = New StackPanel With {.Orientation = Orientation.Vertical, .Margin = New Thickness(18, 0, 0, 0)}
        EquipHost = New System.Windows.Controls.Primitives.UniformGrid With {
            .Columns = 1, .HorizontalAlignment = HorizontalAlignment.Left
        }
        EquipBox.Children.Add(EquipHost)
        Columns.Children.Add(EquipBox)
        Panel.Children.Add(Columns)
        '列表整个不存在时，给一个直接补字段+塞物品的入口
        MissingBox = New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 10, 0, 0)}
        Dim BtnCreate As New MyButton With {
            .Text = "放入第一个物品", .Height = 30, .MinWidth = 130
        }
        AddHandler BtnCreate.Click,
            Sub()
                Dim Target As New DstItem.SlotCell With {.Field = Field, .SlotIndex = 0}
                PutNewItem(Target)
            End Sub
        MissingBox.Children.Add(BtnCreate)
        MissingBox.Children.Add(New TextBlock With {
            .Text = "这个文件里还没有 " & Field & " 字段，点这里会新建它并放入一格物品。",
            .FontSize = 11, .Opacity = 0.5, .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(10, 0, 0, 0), .TextWrapping = TextWrapping.Wrap
        })
        Panel.Children.Add(MissingBox)
        DetailHost = New StackPanel With {.Margin = New Thickness(0, 14, 0, 0)}
        Panel.Children.Add(DetailHost)
        Host.Children.Add(Panel)
    End Sub

    ''' <summary>重新画格子。</summary>
    Public Sub Refresh()
        '列表不存在时补一个空的，这样空格子也能放东西
        Dim WasMissing As Boolean = IsListMissing()
        Dim ListTag As NbtTag = EnsureList()
        If ListTag Is Nothing Then
            DetailHost.Children.Clear()
            GridHost.Children.Clear()
            EquipHost.Children.Clear()
            DetailHost.Children.Add(New TextBlock With {
                .Text = "这个文件里没有 " & Field & " 字段，也无法在顶层创建它，因此没法编辑物品。",
                .FontSize = 11.5, .Opacity = 0.6, .TextWrapping = TextWrapping.Wrap
            })
            Return
        End If
        Dim Items As List(Of NbtTag) = DstNbt.Children(ListTag)
        '这一栏只在「字段原本不存在」时显示
        If MissingBox IsNot Nothing Then
            MissingBox.Visibility = If(WasMissing, Visibility.Visible, Visibility.Collapsed)
        End If
        AllCells.Clear()
        If Field = "EnderItems" Then
            '末影箱只有一组 27 格，不画装备栏
            EquipBox.Visibility = Visibility.Collapsed
            GridHost.Visibility = Visibility.Visible
            Dim EndCells As List(Of DstItem.SlotCell) = DstItem.BuildEnderGrid(ListTag)
            AllCells.AddRange(EndCells)
            GridHost.Columns = 9
            GridHost.Children.Clear()
            For i = 0 To EndCells.Count - 1
                GridHost.Children.Add(BuildCell(EndCells(i), Items))
            Next
        Else
            EquipBox.Visibility = Visibility.Visible
            GridHost.Visibility = Visibility.Visible
            Dim BagCells As List(Of DstItem.SlotCell) = DstItem.BuildGrid(ListTag, Field)
            Dim EqCells As List(Of DstItem.SlotCell) = DstItem.BuildEquipGrid(ListTag, Field)
            AllCells.AddRange(BagCells)
            AllCells.AddRange(EqCells)
            GridHost.Columns = 9
            GridHost.Children.Clear()
            For i = 0 To BagCells.Count - 1
                GridHost.Children.Add(BuildCell(BagCells(i), Items))
            Next
            EquipHost.Columns = 1
            EquipHost.Children.Clear()
            For i = 0 To EqCells.Count - 1
                EquipHost.Children.Add(BuildCell(EqCells(i), Items))
            Next
        End If
        '选中的格子被清空了也要把信息跟着刷新
        If Selected >= 0 Then
            ShowDetail(Items)
        Else
            DetailHost.Children.Clear()
        End If
    End Sub

    ''' <summary>一个格子按钮。</summary>
    Private Function BuildCell(Cell As DstItem.SlotCell, Items As List(Of NbtTag)) As FrameworkElement
        Dim Box As New Border With {
            .Width = 46, .Height = 46, .Margin = New Thickness(2),
            .CornerRadius = New CornerRadius(4), .BorderThickness = New Thickness(1)
        }
        Dim Brush As New SolidColorBrush(BackColorSub)
        Box.Background = Brush
        Dim SelectedStroke As New SolidColorBrush(Color.FromRgb(&H9, &H6D, &HCE))
        '只有「这一格有物品且被选中」才描蓝框
        Dim IsPicked As Boolean = Selected = Cell.SlotIndex
        If IsPicked Then
            Box.BorderBrush = SelectedStroke
            Box.BorderThickness = New Thickness(2)
        Else
            Box.BorderBrush = New SolidColorBrush(Color.FromArgb(40, 128, 128, 128))
        End If
        Dim G As New Grid
        Dim Info As DstItem.ItemInfo = Nothing
        If Cell.ListIndex >= 0 AndAlso Cell.ListIndex < Items.Count Then Info = DstItem.Read(Items(Cell.ListIndex))
        If Info IsNot Nothing Then
            '只写文字，不渲染物品图标
            G.Children.Add(New TextBlock With {
                .Text = DstNbt.StripNamespace(Info.Id), .FontSize = 8.5, .Margin = New Thickness(3),
                .TextWrapping = TextWrapping.Wrap, .TextAlignment = TextAlignment.Center,
                .VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Center,
                .TextTrimming = TextTrimming.CharacterEllipsis, .MaxHeight = 30
            })
            G.Children.Add(New TextBlock With {
                .Text = Info.Count.ToString(), .FontSize = 10, .FontWeight = FontWeights.Bold,
                .HorizontalAlignment = HorizontalAlignment.Right, .VerticalAlignment = VerticalAlignment.Bottom,
                .Margin = New Thickness(0, 0, 3, 1), .Opacity = 0.85
            })
            If Info.EnchantCount > 0 OrElse Info.CustomName <> "" Then
                Dim Dot As New TextBlock With {
                    .Text = "★", .FontSize = 8, .Margin = New Thickness(3, 2, 0, 0),
                    .HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Top,
                    .Opacity = 0.7
                }
                G.Children.Add(Dot)
            End If
        Else
            '空格子给一个很淡的加号，鼠标扫过就知道可以点
            G.Children.Add(New TextBlock With {
                .Text = "+", .FontSize = 16, .Opacity = 0.16,
                .HorizontalAlignment = HorizontalAlignment.Center,
                .VerticalAlignment = VerticalAlignment.Center
            })
        End If
        Box.Child = G
        Dim Cap As String = Cell.Caption
        Dim Stack As New StackPanel With {.Margin = New Thickness(0, 0, 0, 8)}
        Stack.Children.Add(Box)
        If Cap IsNot Nothing AndAlso Cap <> "" Then
            Stack.Children.Add(New TextBlock With {
                .Text = Cap, .FontSize = 8.5, .Opacity = 0.4, .HorizontalAlignment = HorizontalAlignment.Center,
                .MaxWidth = 50, .TextTrimming = TextTrimming.CharacterEllipsis, .Margin = New Thickness(0, 2, 0, 0)
            })
        End If
        Dim Hit As New Border With {.Child = Stack, .Background = Brushes.Transparent}
        AddHandler Hit.MouseLeftButtonUp,
            Sub()
                Selected = Cell.SlotIndex
                Refresh()
            End Sub
        Return Hit
    End Function

    ''' <summary>显示某一格的详细信息与编辑区。</summary>
    Private Sub ShowDetail(Items As List(Of NbtTag))
        DetailHost.Children.Clear()
        Dim Cell As DstItem.SlotCell = Nothing
        For Each C In AllCells
            If C.SlotIndex = Selected Then
                Cell = C
                Exit For
            End If
        Next
        If Cell Is Nothing Then Return
        DetailHost.Children.Add(New TextBlock With {
            .Text = If(Cell.Caption <> "", Cell.Caption & "（槽位 " & Cell.SlotIndex & "）", "槽位 " & Cell.SlotIndex),
            .FontSize = 13, .FontWeight = FontWeights.Bold, .Margin = New Thickness(0, 0, 0, 8)
        })
        Dim Slot As NbtTag = Nothing
        If Cell.ListIndex >= 0 AndAlso Cell.ListIndex < Items.Count Then Slot = Items(Cell.ListIndex)
        Dim Info As DstItem.ItemInfo = DstItem.Read(Slot)
        If Info Is Nothing Then
            DetailHost.Children.Add(New TextBlock With {
                .Text = "这一格是空的。放一个物品进去就等于在这个位置新建了一格物品数据。",
                .FontSize = 11.5, .Opacity = 0.55, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 0, 0, 8)
            })
            Dim BtnPut As New MyButton With {.Text = "往这一格放一个物品", .Height = 30, .MinWidth = 160}
            AddHandler BtnPut.Click, Sub() PutNewItem(Cell)
            DetailHost.Children.Add(BtnPut)
            Return
        End If

        '物品信息
        Dim InfoPanel As New StackPanel With {.Margin = New Thickness(0, 0, 0, 10)}
        InfoPanel.Children.Add(New TextBlock With {
            .Text = Info.DisplayName, .FontSize = 12.5, .FontWeight = FontWeights.Bold,
            .TextTrimming = TextTrimming.CharacterEllipsis, .Margin = New Thickness(0, 0, 0, 4)
        })
        Dim Facts As New List(Of String)
        Facts.Add("ID " & Info.Id)
        Facts.Add("数量 " & Info.Count)
        If Info.CustomName <> "" Then Facts.Add("自定义名 " & Info.CustomName)
        If Info.EnchantCount > 0 Then Facts.Add("附魔 " & Info.EnchantCount & " 条")
        If Info.HasLore Then Facts.Add("有 Lore")
        Facts.Add("槽位 " & DstItem.SlotIndexOf(Slot))
        InfoPanel.Children.Add(New TextBlock With {
            .Text = String.Join(" · ", Facts), .FontSize = 11, .Opacity = 0.55, .TextWrapping = TextWrapping.Wrap
        })
        DetailHost.Children.Add(InfoPanel)

        Dim Bar As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 0, 0, 6)}
        Dim BtnEdit As New MyButton With {.Text = "修改 ID 与数量", .Height = 30, .MinWidth = 130}
        AddHandler BtnEdit.Click, Sub() EditIdAndCount(Cell, Slot)
        Bar.Children.Add(BtnEdit)
        Dim BtnName As New MyButton With {.Text = "改自定义名", .Height = 30, .MinWidth = 110, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnName.Click, Sub() EditCustomName(Cell, Slot)
        Bar.Children.Add(BtnName)
        Dim BtnRaw As New MyButton With {.Text = "查看原始 NBT", .Height = 30, .MinWidth = 120, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnRaw.Click,
            Sub()
                If OpenRaw Is Nothing Then Return
                Dim Steps As New List(Of NbtPathStep)
                Steps.Add(NbtPathStep.ByKey(Field))
                Steps.Add(NbtPathStep.ByIndex(Cell.ListIndex))
                OpenRaw(Steps)
            End Sub
        Bar.Children.Add(BtnRaw)
        Dim BtnClear As New MyButton With {.Text = "清空这一格", .Height = 30, .MinWidth = 110, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnClear.Click, Sub() ClearCell(Cell)
        Bar.Children.Add(BtnClear)
        DetailHost.Children.Add(Bar)

        '字段概览：把这一格的 NBT 键列出来，点进去就是原始编辑器
        Dim Keys As New List(Of String)
        For Each Child In DstNbt.Children(Slot)
            Keys.Add(Child.TagName & "（" & DstNbt.TypeName(Child.TagType) & "）")
        Next
        If Keys.Count > 0 Then
            DetailHost.Children.Add(New TextBlock With {
                .Text = "这一格里的字段：" & String.Join("、", Keys),
                .FontSize = 11, .Opacity = 0.5, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 6, 0, 0)
            })
        End If
    End Sub

    ''' <summary>
    ''' 取物品列表，没有就建一个空的。
    ''' 玩家数据里没有 Inventory / EnderItems 是常事（从没开过背包、没碰过末影箱），
    ''' 这种情况必须能补出字段，否则往空格子里放东西会直接失败。
    ''' </summary>
    Private Function EnsureList() As NbtTag
        Dim ListTag As NbtTag = DstNbt.GetByPath(Doc.Root, Field)
        If ListTag IsNot Nothing AndAlso ListTag.TagType = DstNbt.TypeList Then Return ListTag
        If ListTag IsNot Nothing Then Return Nothing
        ListTag = New NbtTag With {.TagType = DstNbt.TypeList, .TagName = Field, .TagValue = New List(Of NbtTag)}
        DstNbt.AddChild(Doc.Root, DstNbtPath.OfSteps(), ListTag)
        Return ListTag
    End Function

    ''' <summary>物品列表是否本来就不存在（界面要提示一句）。</summary>
    Private Function IsListMissing() As Boolean
        Return DstNbt.GetByPath(Doc.Root, Field) Is Nothing
    End Function

    ''' <summary>往空格子放一个物品。</summary>
    Private Sub PutNewItem(Cell As DstItem.SlotCell)
        Dim Id As String = MyMsgBoxInput("放入物品",
            "输入物品 ID，例如 diamond_sword。不写命名空间会自动补上 minecraft:。", "minecraft:stone")
        If String.IsNullOrWhiteSpace(Id) Then Return
        If Not DstItemValidate.IsValidId(Id) Then
            Hint("物品 ID 不合法，只能用英文字母、数字和下划线", HintType.Red)
            Return
        End If
        Dim CountText As String = MyMsgBoxInput("放入物品", "输入数量，1 到 127 之间。", "1")
        If CountText Is Nothing Then Return
        Dim Count As Integer = 1
        Try
            Count = CInt(Val(CountText))
        Catch
        End Try
        If Count < 1 Then Count = 1
        If Count > 127 Then Count = 127
        '列表不存在时先补出来
        If EnsureList() Is Nothing Then
            Hint("无法创建 " & Field & "，这个文件的顶层结构不支持", HintType.Red)
            Return
        End If
        Dim NewSlot As NbtTag = DstItem.NewSlot(DstItemValidate.NormalizeId(Id), Count, Cell.SlotIndex)
        If Not DstNbt.AddChild(Doc.Root, DstNbtPath.OfSteps(NbtPathStep.ByKey(Field)), NewSlot) Then
            Hint("写入失败，没能往列表里加元素", HintType.Red)
            Return
        End If
        If Persist() Then
            Hint("已放入 " & DstNbt.StripNamespace(DstItemValidate.NormalizeId(Id)) & " ×" & Count, HintType.Blue)
        Else
            Hint("保存失败：" & Doc.LastError, HintType.Red)
        End If
        Selected = Cell.SlotIndex
        Refresh()
    End Sub

    ''' <summary>改 ID 与数量。ID 变了就当另一个物品处理，原地替换。</summary>
    Private Sub EditIdAndCount(Cell As DstItem.SlotCell, Slot As NbtTag)
        If Slot Is Nothing Then Return
        Dim Info As DstItem.ItemInfo = DstItem.Read(Slot)
        Dim Id As String = MyMsgBoxInput("修改物品", "把物品换成别的 ID，数量也一起改。",
                                         If(Info Is Nothing, "minecraft:stone", Info.Id))
        If String.IsNullOrWhiteSpace(Id) Then Return
        If Not DstItemValidate.IsValidId(Id) Then
            Hint("物品 ID 不合法，只能用英文字母、数字和下划线", HintType.Red)
            Return
        End If
        Dim CountText As String = MyMsgBoxInput("修改物品", "输入数量，1 到 127 之间。",
                                                If(Info Is Nothing, "1", Info.Count.ToString()))
        If CountText Is Nothing Then Return
        Dim Count As Integer = 1
        Try
            Count = CInt(Val(CountText))
        Catch
        End Try
        DstItem.SetIdAndCount(Slot, DstItemValidate.NormalizeId(Id), Count)
        If Persist() Then
            Hint("已保存物品", HintType.Blue)
        Else
            Hint("保存失败：" & Doc.LastError, HintType.Red)
        End If
        Refresh()
    End Sub

    ''' <summary>改自定义名称。</summary>
    Private Sub EditCustomName(Cell As DstItem.SlotCell, Slot As NbtTag)
        If Slot Is Nothing Then Return
        Dim Old As String = DstItem.CustomNameOf(Slot)
        Dim NewName As String = MyMsgBoxInput("自定义名称", "物品会显示这个名字。留空表示使用原名。", Old)
        If NewName Is Nothing Then Return
        Dim Inner As NbtTag = DstItem.EnsureTag(Slot)
        If Inner Is Nothing Then Return
        If NewName.Trim() = "" Then
            Dim Display As NbtTag = DstNbt.CompoundGet(Inner, "display")
            If Display IsNot Nothing Then
                DstNbt.RemoveChild(Display, DstNbtPath.OfSteps(NbtPathStep.ByKey("Name")))
            End If
        Else
            Dim Display As NbtTag = DstNbt.CompoundGet(Inner, "display")
            If Display Is Nothing Then
                Display = New NbtTag With {.TagType = DstNbt.TypeCompound, .TagName = "display", .TagValue = New List(Of NbtTag)}
                DstNbt.AddChild(Inner, DstNbtPath.OfSteps(), Display)
            End If
            Dim JsonText As String = "{""text"":""" & NewName.Replace("""", "\""") & """}"
            Dim NameTag As NbtTag = DstNbt.CompoundGet(Display, "Name")
            If NameTag Is Nothing Then
                DstNbt.AddChild(Display, DstNbtPath.OfSteps(),
                    New NbtTag With {.TagType = DstNbt.TypeString, .TagName = "Name", .TagValue = JsonText})
            Else
                NameTag.TagType = DstNbt.TypeString
                NameTag.TagValue = JsonText
            End If
        End If
        If Persist() Then
            Hint("已保存名称", HintType.Blue)
        Else
            Hint("保存失败：" & Doc.LastError, HintType.Red)
        End If
        Refresh()
    End Sub

    ''' <summary>从列表里删掉这一格的元素。</summary>
    Private Sub ClearCell(Cell As DstItem.SlotCell)
        If Cell.ListIndex < 0 Then Return
        If MyMsgBox("确定要清空这一格吗？", "清空格子", "清空", "取消") <> 1 Then Return
        Dim Steps As New List(Of NbtPathStep)
        Steps.Add(NbtPathStep.ByKey(Field))
        Steps.Add(NbtPathStep.ByIndex(Cell.ListIndex))
        If Not DstNbt.RemoveChild(Doc.Root, Steps) Then
            Hint("清空失败", HintType.Red)
            Return
        End If
        If Persist() Then
            Hint("已清空", HintType.Blue)
        Else
            Hint("保存失败：" & Doc.LastError, HintType.Red)
        End If
        Selected = -1
        Refresh()
    End Sub

End Class
