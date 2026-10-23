''' <summary>
''' 收藏夹的列表视图。由调用方提供一个 <see cref="StackPanel"/>，这里负责生成与模组管理一致的列表行、
''' 维护多选状态，并把交互回传给页面。收藏夹页与版本设置里的收藏夹页共用这一份逻辑。
''' </summary>
Public Class FavoriteListView

    ''' <summary>列表行被放置到的容器。</summary>
    Public ReadOnly Panel As StackPanel
    ''' <summary>当前选中的条目 Key。</summary>
    Public ReadOnly SelectedKeys As New HashSet(Of String)
    ''' <summary>当前显示出来的条目（已过滤）。</summary>
    Public ReadOnly Property Shown As List(Of FavoriteEntry)
        Get
            Return _Shown
        End Get
    End Property
    Private _Shown As New List(Of FavoriteEntry)
    Private AllEntries As New List(Of FavoriteEntry)

    ''' <summary>选中项变化时触发。</summary>
    Public Event SelectionChanged As EventHandler
    ''' <summary>双击或点击条目时触发，用于打开详情。</summary>
    Public Event EntryActivated As Action(Of FavoriteEntry)
    ''' <summary>点条目的取消收藏按钮时触发。</summary>
    Public Event EntryRemoved As Action(Of FavoriteEntry)

    Public Sub New(Panel As StackPanel)
        Me.Panel = Panel
    End Sub

    ''' <summary>重新从收藏夹读取某一分类的条目并重画。</summary>
    Public Sub Reload(Type As ResourceTypes)
        AllEntries = ModFavorite.GetByType(Type)
        '丢掉已经不存在的选中项
        Dim ValidKeys = New HashSet(Of String)(AllEntries.Select(Function(e) e.Key))
        SelectedKeys.RemoveWhere(Function(k) Not ValidKeys.Contains(k))
        ApplyFilter(Nothing, Nothing)
    End Sub

    ''' <summary>按关键字与额外条件过滤后重画。</summary>
    Public Sub ApplyFilter(Keyword As String, ExtraPredicate As Func(Of FavoriteEntry, Boolean))
        If Panel Is Nothing Then Return
        Panel.Children.Clear()
        Dim Key As String = If(Keyword, "").Trim
        _Shown = AllEntries.Where(
            Function(e)
                If Key <> "" AndAlso
                   e.Title.IndexOf(Key, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                   e.Description.IndexOf(Key, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                   e.SubTitle.IndexOf(Key, StringComparison.OrdinalIgnoreCase) < 0 Then Return False
                If ExtraPredicate IsNot Nothing AndAlso Not ExtraPredicate(e) Then Return False
                Return True
            End Function).ToList()

        If Not _Shown.Any() Then
            Dim Tip As String = If(AllEntries.Any(),
                "没有符合条件的收藏。",
                "这个分类还没有收藏。在下载页点资源卡片右上角的星星就能收藏。")
            Panel.Children.Add(New TextBlock With {
                .Text = Tip, .FontSize = 12.5, .Margin = New Thickness(2, 10, 0, 0),
                .TextWrapping = TextWrapping.Wrap, .Foreground = New SolidColorBrush(TextColorSub)})
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
            Return
        End If

        For Each Entry In _Shown
            Panel.Children.Add(BuildRow(Entry))
        Next
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>全选 / 取消全选当前显示出来的条目。</summary>
    Public Sub ChangeAllSelected(ShouldSelect As Boolean)
        If ShouldSelect Then
            For Each Entry In _Shown
                SelectedKeys.Add(Entry.Key)
            Next
        Else
            SelectedKeys.Clear()
        End If
        RefreshCheckedState()
    End Sub

    ''' <summary>清空选择。</summary>
    Public Sub ClearSelection()
        SelectedKeys.Clear()
        RefreshCheckedState()
    End Sub

    ''' <summary>把选中状态同步到已有行，不重建控件。</summary>
    Private Sub RefreshCheckedState()
        For Each Row In Panel.Children.OfType(Of MyLocalModItem)()
            Dim Entry As FavoriteEntry = TryCast(Row.Tag, FavoriteEntry)
            If Entry Is Nothing Then Continue For
            Row.Checked = SelectedKeys.Contains(Entry.Key)
        Next
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>构造一行。与模组管理使用同一个控件，保证手感一致。</summary>
    Private Function BuildRow(Entry As FavoriteEntry) As MyLocalModItem
        Dim Item As New MyLocalModItem With {
            .SnapsToDevicePixels = True,
            .Tag = Entry,
            .Title = If(Entry.Title = "", Entry.SubTitle, Entry.Title),
            .Description = If(Entry.Description = "", Entry.SubTitle, Entry.Description),
            .ItemIsEnabled = True,
            .Checked = SelectedKeys.Contains(Entry.Key)
        }
        '图标：优先用项目对象自己的 LogoUrl（与详情页同一条路径，含镜像兜底）
        Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
        If Project IsNot Nothing Then
            Project.ApplyLogoToMyImage(Item.PathLogo)
        ElseIf Entry.Logo <> "" Then
            Item.LogoSource = Entry.Logo
        End If
        '左键只负责勾选，与模组管理完全一致：这样点一下就能选，不会被详情页抢走
        AddHandler Item.Click, Sub() ToggleSelect(Entry)
        '右键进详情，同样是模组管理的习惯
        AddHandler Item.MouseRightButtonUp, Sub() RaiseEvent EntryActivated(Entry)
        '悬停按钮：详情、取消收藏
        Dim BtnInfo As New MyIconButton With {
            .LogoScale = 1, .Logo = Logo.IconButtonInfo, .ToolTip = "查看详情"}
        AddHandler BtnInfo.Click, Sub() RaiseEvent EntryActivated(Entry)
        Dim BtnRemove As New MyIconButton With {
            .LogoScale = 0.62, .Logo = Logo.IconButtonCross, .ToolTip = "取消收藏"}
        AddHandler BtnRemove.Click, Sub() RaiseEvent EntryRemoved(Entry)
        Item.Buttons = {BtnInfo, BtnRemove}
        Return Item
    End Function

    ''' <summary>切换某条目的选中状态。</summary>
    Public Sub ToggleSelect(Entry As FavoriteEntry)
        If Not SelectedKeys.Remove(Entry.Key) Then SelectedKeys.Add(Entry.Key)
        RefreshCheckedState()
    End Sub

End Class
