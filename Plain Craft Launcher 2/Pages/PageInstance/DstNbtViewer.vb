''' <summary>
''' 通用 NBT 浏览器与编辑器。
''' 可以逐层展开复合标签与列表，对每一个标量节点改名、改值、删除、新增。
''' 自己维护「当前所在层级」，由调用方提供顶层容器元素。
''' </summary>
Public Class DstNbtViewer

    ''' <summary>正在编辑的文档。</summary>
    Public ReadOnly Property Doc As DstNbtDocument

    ''' <summary>容器：调用方插入界面元素的地方。</summary>
    Private ReadOnly Host As Panel
    ''' <summary>面包屑所在的位置。</summary>
    Private ReadOnly CrumbHost As StackPanel
    ''' <summary>内容区。</summary>
    Private ReadOnly ContentHost As StackPanel
    ''' <summary>确认可以写入之后才执行。</summary>
    Private ReadOnly Persist As Func(Of Boolean)

    ''' <summary>当前所在层级（从根开始）。</summary>
    Private Cur As New List(Of NbtPathStep)

    ''' <summary>信息行，用来提示「已保存」之类的状态。</summary>
    Private StatusText As TextBlock = Nothing

    ''' <summary>
    ''' 构造一个浏览器。
    ''' </summary>
    ''' <param name="Document">要编辑的文档。</param>
    ''' <param name="Outer">外层容器，浏览器自己的面板会被加进去。</param>
    ''' <param name="OnPersist">把整份文档写回磁盘；返回 False 表示失败。</param>
    Public Sub New(Document As DstNbtDocument, Outer As Panel, OnPersist As Func(Of Boolean))
        Doc = Document
        Host = Outer
        Persist = OnPersist
        Dim Panel As New StackPanel
        CrumbHost = New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 8, 0, 8)}
        ContentHost = New StackPanel
        Panel.Children.Add(CrumbHost)
        Panel.Children.Add(ContentHost)
        Host.Children.Add(Panel)
    End Sub

    ''' <summary>从根开始显示。</summary>
    Public Sub ShowRoot()
        Cur = New List(Of NbtPathStep)
        Rebuild()
    End Sub

    ''' <summary>定位到某个路径。</summary>
    Public Sub ShowAt(Steps As List(Of NbtPathStep))
        Cur = DstNbtPath.Copy(Steps)
        Rebuild()
    End Sub

    ''' <summary>当前层级。</summary>
    Public ReadOnly Property CurrentPath As List(Of NbtPathStep)
        Get
            Return DstNbtPath.Copy(Cur)
        End Get
    End Property

#Region "界面构建"

    Private Sub Rebuild()
        CrumbHost.Children.Clear()
        ContentHost.Children.Clear()
        Dim Node As NbtTag = Doc.Find(Cur)
        '面包屑：根 / a / b / [0]
        Dim BtnHome As New MyTextButton With {.Text = "根标签", .Height = 24, .Margin = New Thickness(0, 0, 6, 0)}
        AddHandler BtnHome.Click, Sub() ShowRoot()
        CrumbHost.Children.Add(BtnHome)
        Dim Walk As New List(Of NbtPathStep)
        For i = 0 To Cur.Count - 1
            Walk.Add(Cur(i))
            Dim Target As New List(Of NbtPathStep)
            Target.AddRange(Walk)
            CrumbHost.Children.Add(New TextBlock With {
                .Text = "/", .Opacity = 0.35, .VerticalAlignment = VerticalAlignment.Center,
                .Margin = New Thickness(0, 0, 6, 0), .FontSize = 11
            })
            Dim Btn As New MyTextButton With {
                .Text = If(Cur(i).IsIndex, "[" & Cur(i).Index & "]", Cur(i).Key),
                .Height = 24, .Margin = New Thickness(0, 0, 6, 0)
            }
            AddHandler Btn.Click, Sub() ShowAt(Target)
            CrumbHost.Children.Add(Btn)
        Next
        If Node Is Nothing Then
            ContentHost.Children.Add(EmptyLine("这个位置没有数据，可能刚刚被删掉了。"))
            Return
        End If
        If Node.TagType <> DstNbt.TypeCompound AndAlso Node.TagType <> DstNbt.TypeList Then
            ContentHost.Children.Add(EmptyLine("这是一个" & DstNbt.TypeName(Node.TagType) & "值：" & DstNbt.Describe(Node)))
            ContentHost.Children.Add(BuildValueEditor(Node))
            Return
        End If
        '上一级
        If Cur.Count > 0 Then
            Dim BtnUp As New MyButton With {.Text = "返回上一级", .Height = 30, .MinWidth = 110, .Margin = New Thickness(0, 0, 0, 10)}
            AddHandler BtnUp.Click, Sub() ShowAt(DstNbtPath.ParentOf(Cur))
            ContentHost.Children.Add(BtnUp)
        End If
        '新增子标签
        Dim AddBar As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 0, 0, 10)}
        Dim BtnAdd As New MyButton With {.Text = "新增子标签", .Height = 30, .MinWidth = 110}
        AddHandler BtnAdd.Click, Sub() AddNewChild(Node)
        AddBar.Children.Add(BtnAdd)
        Dim LabHint As New TextBlock With {
            .Text = If(Node.TagType = DstNbt.TypeList, "列表只能追加同类型的值", "复合标签可以随便加键"),
            .FontSize = 11, .Opacity = 0.5, .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(10, 0, 0, 0)
        }
        AddBar.Children.Add(LabHint)
        ContentHost.Children.Add(AddBar)

        Dim Children As List(Of NbtTag) = DstNbt.Children(Node)
        If Children.Count = 0 Then
            ContentHost.Children.Add(EmptyLine("这里什么都没有。"))
            Return
        End If
        For i = 0 To Children.Count - 1
            ContentHost.Children.Add(BuildChildRow(Node, Children(i), i))
        Next
    End Sub

    ''' <summary>一行子节点。</summary>
    Private Function BuildChildRow(ParentTag As NbtTag, Child As NbtTag, Index As Integer) As FrameworkElement
        Dim IsList As Boolean = ParentTag.TagType = DstNbt.TypeList
        '列表按下标，复合按键名
        Dim Target As New List(Of NbtPathStep)
        Target.AddRange(Cur)
        If IsList Then
            Target.Add(NbtPathStep.ByIndex(Index))
        Else
            Target.Add(NbtPathStep.ByKey(Child.TagName))
        End If
        Dim Title As String = If(IsList, "[" & Index & "]", Child.TagName)
        Dim IsContainer As Boolean = Child.TagType = DstNbt.TypeCompound OrElse Child.TagType = DstNbt.TypeList
        Dim Info As String = DstNbt.TypeName(Child.TagType)
        If Not IsContainer Then Info &= " · " & ShortText(DstNbt.Describe(Child))
        If IsContainer Then Info &= " · " & DstNbt.Children(Child).Count & " 项"

        Dim Buttons As New List(Of FrameworkElement)
        If IsContainer Then
            Dim BtnEnter As New MyIconButton With {
                .Logo = "M96 64 512 448 928 64 1024 128 512 576 0 128 Z",
                .Width = 26, .Height = 26, .LogoScale = 0.6,
                .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "进入这一层"
            }
            AddHandler BtnEnter.Click, Sub() ShowAt(Target)
            Buttons.Add(BtnEnter)
        End If
        Dim BtnRename As New MyIconButton With {
            .Logo = "M732.64 64.32C688.576 21.216 613.696 21.216 569.6 64.32L120.128 499.52c-17.6 12.896-26.432 30.144-30.848 51.68L32 870.048c0 25.856 8.8 56 26.432 73.248 17.632 17.216 17.632 48.704 88.64 48.704h13.248l326.08-56c22.016-4.32 39.68-12.928 52.864-30.176l449.472-435.2c22.048-21.536 35.264-47.36 35.264-77.536 0-30.176-13.216-56-35.264-77.568l-256.096-251.2zM139.712 903.776l56-326.912 311.04-295.136 267.104 269.44-310.976 295.168-323.168 57.44zM844.576 467.84l-273.984-260.672 61.856-59.84c8.832-8.512 26.528-8.512 39.776 0l234.24 226.496c4.384 4.288 8.832 12.8 8.832 17.088s-4.416 8.544-8.864 12.8l-61.856 64.128z",
            .Width = 26, .Height = 26, .LogoScale = 0.72,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = If(IsList, "修改这个值", "重命名")
        }
        AddHandler BtnRename.Click, Sub() RenameOrEdit(ParentTag, Target, Child, IsList)
        Buttons.Add(BtnRename)
        Dim BtnDelete As New MyIconButton With {
            .Logo = Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.72,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "删除"
        }
        AddHandler BtnDelete.Click, Sub() DeleteChild(Target, Title)
        Buttons.Add(BtnDelete)

        Dim Row As FrameworkElement = BuildSimpleRow(Title, Info, Buttons)
        If Not IsContainer Then
            AddHandler CType(Row, Border).MouseLeftButtonDown, Sub() EditValue(Target, Child, Title)
        End If
        Return Row
    End Function

    ''' <summary>把一句太长的话缩短。</summary>
    Private Shared Function ShortText(Text As String) As String
        If Text Is Nothing Then Return ""
        If Text.Length > 64 Then Return Text.Substring(0, 64) & "…"
        Return Text
    End Function

    Private Function BuildSimpleRow(Title As String, Info As String, Buttons As List(Of FrameworkElement)) As FrameworkElement
        Dim Row As New Border With {
            .Height = 44, .CornerRadius = New CornerRadius(6), .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(12, 0, 12, 0), .Background = New SolidColorBrush(BackColorSub)
        }
        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Info2 As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Info2.Children.Add(New TextBlock With {
            .Text = Title, .FontSize = 12.5, .FontWeight = FontWeights.Bold,
            .TextTrimming = TextTrimming.CharacterEllipsis
        })
        Info2.Children.Add(New TextBlock With {
            .Text = Info, .FontSize = 11, .Opacity = 0.5, .Margin = New Thickness(0, 2, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis
        })
        G.Children.Add(Info2)
        Dim RightBox As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        If Buttons IsNot Nothing Then
            For Each Btn In Buttons
                RightBox.Children.Add(Btn)
            Next
        End If
        Grid.SetColumn(RightBox, 1)
        G.Children.Add(RightBox)
        Row.Child = G
        Return Row
    End Function

    Private Function EmptyLine(Text As String) As FrameworkElement
        Return New TextBlock With {
            .Text = Text, .FontSize = 11.5, .Opacity = 0.45,
            .Margin = New Thickness(2, 6, 0, 10), .TextWrapping = TextWrapping.Wrap
        }
    End Function

    ''' <summary>一个标量节点的编辑条。</summary>
    Private Function BuildValueEditor(Tag As NbtTag) As FrameworkElement
        Dim Panel As New StackPanel With {.Margin = New Thickness(0, 4, 0, 0)}
        Dim Box As New MyTextBox With {
            .Text = DstNbt.Describe(Tag), .Height = 32, .Width = 420,
            .HorizontalAlignment = HorizontalAlignment.Left
        }
        Dim Btn As New MyButton With {.Text = "保存此值", .Height = 30, .MinWidth = 100, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler Btn.Click,
            Sub()
                If Not DstNbt.SetText(Tag, Box.Text) Then
                    Hint("无法解析这个值，请检查格式是否正确", HintType.Red)
                    Return
                End If
                Commit("值已保存")
            End Sub
        Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal}
        Row.Children.Add(Box)
        Row.Children.Add(Btn)
        Panel.Children.Add(Row)
        Panel.Children.Add(New TextBlock With {
            .Text = "类型：" & DstNbt.TypeName(Tag.TagType), .FontSize = 11, .Opacity = 0.5,
            .Margin = New Thickness(0, 6, 0, 0)
        })
        Return Panel
    End Function

#End Region

#Region "编辑操作"

    ''' <summary>改一个标量的值；在列表里则允许改整个元素。</summary>
    Private Sub EditValue(Steps As List(Of NbtPathStep), Tag As NbtTag, Title As String)
        Dim Value As String = MyMsgBoxInput("修改 " & Title,
            "当前类型：" & DstNbt.TypeName(Tag.TagType) & "。直接输入新的值。", DstNbt.Describe(Tag))
        If Value Is Nothing Then Return
        If Not DstNbt.SetText(Tag, Value) Then
            Hint("无法解析这个值，请检查格式是否正确", HintType.Red)
            Return
        End If
        Commit("已保存")
    End Sub

    ''' <summary>复合标签改键名；列表元素改值。</summary>
    Private Sub RenameOrEdit(ParentTag As NbtTag, Steps As List(Of NbtPathStep), Child As NbtTag, IsList As Boolean)
        If IsList Then
            EditValue(Steps, Child, DstNbtPath.LastName(Steps))
            Return
        End If
        Dim NewName As String = MyMsgBoxInput("重命名标签", "给这个标签换一个名字。", Child.TagName,
            HintText:="不能和同层的其它标签重名")
        If String.IsNullOrWhiteSpace(NewName) OrElse NewName = Child.TagName Then Return
        If DstNbt.CompoundGet(ParentTag, NewName) IsNot Nothing Then
            Hint("同一层已经有叫「" & NewName & "」的标签了", HintType.Red)
            Return
        End If
        Child.TagName = NewName
        '路径里最后一段同步改名
        Steps(Steps.Count - 1) = NbtPathStep.ByKey(NewName)
        Commit("已重命名")
        Rebuild()
    End Sub

    Private Sub DeleteChild(Steps As List(Of NbtPathStep), Title As String)
        Dim Tip As String = "确定要删除「" & Title & "」吗？删掉之后只能从备份恢复。"
        If MyMsgBox(Tip, "删除标签", "删除", "取消") <> 1 Then Return
        If Not DstNbt.RemoveChild(Doc.Root, Steps) Then
            Hint("删除失败", HintType.Red)
            Return
        End If
        Commit("已删除")
        Rebuild()
    End Sub

    ''' <summary>在当前层级新增一个子标签。</summary>
    Private Sub AddNewChild(ParentTag As NbtTag)
        Dim IsList As Boolean = ParentTag.TagType = DstNbt.TypeList
        Dim ItemType As Integer = If(IsList, DstNbt.ListItemType(ParentTag), DstNbt.TypeString)
        '列表只能追加同类型的元素
        If IsList Then
            If ItemType = DstNbt.TypeEnd Then
                Hint("空列表没法判断元素类型，不能直接追加", HintType.Red)
                Return
            End If
            AddValueToList(ParentTag, ItemType)
            Return
        End If
        Dim Name As String = MyMsgBoxInput("新增标签", "给新标签起个名字。新增后是一个空的字符串标签。", "NewTag")
        If String.IsNullOrWhiteSpace(Name) Then Return
        If DstNbt.CompoundGet(ParentTag, Name) IsNot Nothing Then
            Hint("同一层已经有叫「" & Name & "」的标签了", HintType.Red)
            Return
        End If
        Dim NewTag As New NbtTag With {.TagType = DstNbt.TypeString, .TagName = Name, .TagValue = ""}
        DstNbt.AddChild(Doc.Root, Cur, NewTag)
        Commit("已新增")
        Rebuild()
    End Sub

    Private Sub AddValueToList(ParentTag As NbtTag, ItemType As Integer)
        Dim NewTag As New NbtTag With {.TagType = ItemType, .TagName = ""}
        Select Case ItemType
            Case DstNbt.TypeByte
                NewTag.TagValue = CSByte(0)
            Case DstNbt.TypeShort
                NewTag.TagValue = CShort(0)
            Case DstNbt.TypeInt
                NewTag.TagValue = 0
            Case DstNbt.TypeLong
                NewTag.TagValue = 0L
            Case DstNbt.TypeFloat
                NewTag.TagValue = 0.0F
            Case DstNbt.TypeDouble
                NewTag.TagValue = 0.0R
            Case DstNbt.TypeString
                NewTag.TagValue = ""
            Case DstNbt.TypeCompound
                NewTag.TagValue = New List(Of NbtTag)
            Case DstNbt.TypeList
                NewTag.TagValue = New List(Of NbtTag)
            Case Else
                Return
        End Select
        DstNbt.AddChild(Doc.Root, Cur, NewTag)
        Commit("已追加")
        Rebuild()
    End Sub

    ''' <summary>写盘并提示。</summary>
    Private Sub Commit(Message As String)
        If Persist Is Nothing Then Return
        If Persist() Then
            Hint(Message, HintType.Blue)
        Else
            Hint("保存失败：" & Doc.LastError, HintType.Red)
        End If
    End Sub

#End Region

End Class
