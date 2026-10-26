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
    ''' <summary>点截图条目的「信息」按钮时触发。</summary>
    Public Event ScreenshotInfoRequested As Action(Of FavoriteEntry)

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
        For Each Row In Panel.Children.OfType(Of MyFavoriteItem)()
            Dim Entry As FavoriteEntry = TryCast(Row.Tag, FavoriteEntry)
            If Entry Is Nothing Then Continue For
            Row.Checked = SelectedKeys.Contains(Entry.Key)
        Next
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>
    ''' 构造一行。信息层级对齐下载页：标题 / 标签 / 描述，外加一行「版本 · 下载量 · 最近更新 · 来源」。
    ''' <para>
    ''' 这四项全部来自 <see cref="ResourceProject"/>，措辞也由它统一给出，
    ''' 不在本类里另写一套格式化，免得和下载页出现两套说法。
    ''' </para>
    ''' </summary>
    Private Function BuildRow(Entry As FavoriteEntry) As MyFavoriteItem
        Dim Item As New MyFavoriteItem With {
            .SnapsToDevicePixels = True,
            .Tag = Entry,
            .Title = If(Entry.Title = "", Entry.SubTitle, Entry.Title),
            .Description = If(Entry.Description = "", Entry.SubTitle, Entry.Description),
            .Checked = SelectedKeys.Contains(Entry.Key)
        }
        '图标：优先用项目对象自己的 LogoUrl（与详情页同一条路径，含镜像兜底）
        Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
        If Project IsNot Nothing Then
            Project.ApplyLogoToMyImage(Item.PathLogo)
            '老版本存下的收藏可能没有项目 JSON，那种情况只显示标题与描述，不硬凑一行空的下边栏
            FillMeta(Item, Project)
        Else
            Item.ShowMeta = False
            If Entry.Logo <> "" Then Item.LogoSource = Entry.Logo
            '截图收藏走这条分支：没有项目 JSON，文件还可能已经被删掉
            If Entry.ResourceType = CInt(ResourceTypes.Screenshot) Then
                Dim Shot As String = Entry.Key.Replace("screenshot:", "")
                If Not FileUtils.Exists(Shot) Then
                    Item.Description = "原文件已不存在"
                    Item.Opacity = 0.5
                End If
            End If
        End If
        '左键只负责勾选，与模组管理完全一致：这样点一下就能选，不会被详情页抢走
        AddHandler Item.Click, Sub() ToggleSelect(Entry)
        '右键进详情，同样是模组管理的习惯
        AddHandler Item.MouseRightButtonUp, Sub() RaiseEvent EntryActivated(Entry)
        '截图条目跟截图管理页保持一致：信息、打开文件夹、删除；
        '社区资源仍是详情 + 取消收藏
        If Entry.ResourceType = CInt(ResourceTypes.Screenshot) Then
            Dim Shot As String = Entry.Key.Replace("screenshot:", "")
            Dim BtnEdit As New MyIconButton With {
                .LogoScale = 0.8, .Logo = Logo.IconButtonEdit, .ToolTip = "用画图编辑"}
            AddHandler BtnEdit.Click,
                Sub()
                    If Not FileUtils.Exists(Shot) Then
                        Hint("原文件已不存在", HintType.Red)
                        Return
                    End If
                    Try
                        StartProcess("mspaint.exe", """" & Shot & """")
                    Catch ex As Exception
                        Logger.Error(ex, "打开画图失败", LogBehavior.Alert)
                    End Try
                End Sub
            Dim BtnInfo As New MyIconButton With {
                .LogoScale = 0.8, .Logo = Logo.IconButtonInfo, .ToolTip = "截图信息"}
            AddHandler BtnInfo.Click, Sub() RaiseEvent ScreenshotInfoRequested(Entry)
            Dim BtnOpen As New MyIconButton With {
                .LogoScale = 0.8, .Logo = Logo.IconButtonOpen, .ToolTip = "打开所在文件夹"}
            AddHandler BtnOpen.Click,
                Sub()
                    If FileUtils.Exists(Shot) Then
                        OpenExplorer(Shot)
                    Else
                        Hint("原文件已不存在", HintType.Red)
                    End If
                End Sub
            Dim BtnDelete As New MyIconButton With {
                .LogoScale = 0.8, .Logo = Logo.IconButtonDelete, .ToolTip = "删除这张截图"}
            AddHandler BtnDelete.Click, Sub() RaiseEvent EntryRemoved(Entry)
            Item.Buttons = {BtnEdit, BtnInfo, BtnOpen, BtnDelete}
            Return Item
        End If

        '悬停按钮：详情、取消收藏
        Dim BtnInfoRes As New MyIconButton With {
            .LogoScale = 1, .Logo = Logo.IconButtonInfo, .ToolTip = "查看详情"}
        AddHandler BtnInfoRes.Click, Sub() RaiseEvent EntryActivated(Entry)
        Dim BtnRemove As New MyIconButton With {
            .LogoScale = 0.62, .Logo = Logo.IconButtonCross, .ToolTip = "取消收藏"}
        AddHandler BtnRemove.Click, Sub() RaiseEvent EntryRemoved(Entry)
        Item.Buttons = {BtnInfoRes, BtnRemove}
        Return Item
    End Function

    ''' <summary>
    ''' 截图信息框。内容与截图管理页的「截图信息」一致，
    ''' 所以放在这里共用，两个收藏夹页面都调它。
    ''' </summary>
    Public Shared Sub ShowScreenshotInfo(Entry As FavoriteEntry)
        If Entry Is Nothing Then Return
        Dim Shot As String = Entry.Key.Replace("screenshot:", "")
        RunInThread(
        Sub()
            Dim Lines As New List(Of String)
            Lines.Add("文件名：" & Entry.Title)
            If Not FileUtils.Exists(Shot) Then
                Lines.Add("状态：原文件已不存在")
                Lines.Add("原位置：" & Shot)
                RunInUi(Sub() MyMsgBox(String.Join(vbCrLf, Lines), "截图信息", "确定"))
                Return
            End If
            Try
                Dim Info As New IO.FileInfo(Shot)
                Try
                    Using Fs As IO.FileStream = IO.File.OpenRead(Shot)
                        Dim Frame As BitmapFrame = BitmapFrame.Create(Fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
                        Lines.Add($"尺寸：{Frame.PixelWidth} × {Frame.PixelHeight}")
                    End Using
                Catch ex As Exception
                    Lines.Add("尺寸：读取失败")
                End Try
                Lines.Add("大小：" & FormatSizeText(Info.Length))
                Lines.Add("修改时间：" & Info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))
                Lines.Add("位置：" & Shot)
            Catch ex As Exception
                Logger.Warn(ex, $"读取截图信息失败：{Shot}")
            End Try
            RunInUi(Sub() MyMsgBox(String.Join(vbCrLf, Lines), "截图信息", "确定"))
        End Sub)
    End Sub

    Private Shared Function FormatSizeText(Bytes As Long) As String
        If Bytes < 1024 Then Return Bytes & " B"
        If Bytes < 1024 * 1024 Then Return Math.Round(Bytes / 1024, 1) & " KB"
        Return Math.Round(Bytes / 1024 / 1024, 1) & " MB"
    End Function

    ''' <summary>把项目信息填进下边栏。任何一项取不到就留空，由控件自己把图标一起隐藏。</summary>
    Private Sub FillMeta(Item As MyFavoriteItem, Project As ResourceProject)
        Try
            Dim LoaderFull As String = Nothing, LoaderPart As String = Nothing
            Project.GetModLoaderDescription(LoaderFull, LoaderPart)
            Dim Version As String = Project.GetGameVersionDescription()
            '版本与加载器都有时，显示成「Fabric 1.16+」，和下载页的「全部显示」一致
            Item.VersionText = If(LoaderPart = "", Version, LoaderPart & " " & Version)
            Item.DownloadText = Project.GetDownloadText()
            Item.UpdateText = Project.GetUpdateText()
            Item.SourceText = Project.Platform.ToString
            Item.Tags = Project.Tags
        Catch ex As Exception
            '单行信息填失败不该让整个列表崩掉，退回到只显示标题
            Logger.Warn(ex, $"填充收藏夹条目信息失败：{Item.Title}")
            Item.ShowMeta = False
        End Try
    End Sub

    ''' <summary>切换某条目的选中状态。</summary>
    Public Sub ToggleSelect(Entry As FavoriteEntry)
        If Not SelectedKeys.Remove(Entry.Key) Then SelectedKeys.Add(Entry.Key)
        RefreshCheckedState()
    End Sub

End Class
