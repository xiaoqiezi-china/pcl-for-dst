''' <summary>
''' 版本设置 - 收藏夹页。
''' 与 QQ 页的收藏夹共用同一套列表与安装逻辑，区别只是安装目标固定为当前打开的版本。
''' </summary>
Public Class PageInstanceFavorites
    Implements IRefreshable

    ''' <summary>当前分类，取值同 ResourceTypes。</summary>
    Private CurrentType As Integer = 1

    Private View As FavoriteListView

    Private Sub PageInstanceFavorites_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        BtnSelectInstall.Logo = Logo.IconButtonSave
        BtnSelectShare.Logo = Logo.IconButtonCard
        BtnSelectDelete.Logo = Logo.IconButtonCross
        BtnSelectCancel.Logo = Logo.IconButtonCross

        If View Is Nothing Then
            View = New FavoriteListView(PanList)
            AddHandler View.SelectionChanged, Sub() RefreshSelectBar()
            AddHandler View.ScreenshotInfoRequested, Sub(Entry) FavoriteListView.ShowScreenshotInfo(Entry)
            AddHandler View.EntryActivated,
                Sub(Entry)
                    '截图没有资源详情页，双击直接看图
                    If Entry.ResourceType = CInt(ResourceTypes.Screenshot) Then
                        Dim Shot As String = Entry.Key.Replace("screenshot:", "")
                        If Not FileUtils.Exists(Shot) Then
                            Hint("原文件已不存在", HintType.Red)
                            Return
                        End If
                        Try
                            Dim Window As New WindowImagePreview(Shot)
                            Window.ShowDialog()
                        Catch ex As Exception
                            Logger.Error(ex, "打开截图预览失败", LogBehavior.Alert)
                        End Try
                        Return
                    End If
                    OpenDetail(Entry)
                End Sub
            AddHandler View.EntryRemoved,
                Sub(Entry)
                    '截图条目的删除按钮是「删掉这张图」，社区资源仍是「取消收藏」
                    If Entry.ResourceType = CInt(ResourceTypes.Screenshot) Then
                        Dim Shot As String = Entry.Key.Replace("screenshot:", "")
                        If MyMsgBox("确定要删除这张截图吗？" & vbCrLf &
                                    "文件会被永久删除，收藏记录也会一并移除。",
                                    "删除截图", "删除", "取消", IsWarn:=True) <> 1 Then Return
                        Try
                            If FileUtils.Exists(Shot) Then FileUtils.Delete(Shot)
                        Catch ex As Exception
                            Logger.Error(ex, "删除截图失败", LogBehavior.Alert)
                            Return
                        End Try
                        ModFavorite.Remove(Entry)
                        RefreshList()
                        Hint("截图已删除", HintType.Green, False)
                        Return
                    End If
                    ModFavorite.Remove(Entry)
                    RefreshList()
                    Hint("已取消收藏", HintType.Blue, False)
                End Sub
        End If
        RefreshList()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshList()
    End Sub

    Private Sub Filter_Check(sender As Object, raiseByMouse As Boolean) Handles BtnFilterMod.Check, BtnFilterModPack.Check,
            BtnFilterDataPack.Check, BtnFilterResourcePack.Check, BtnFilterShader.Check, BtnFilterScreenshot.Check
        If Not raiseByMouse Then Return
        Dim NewType As Integer = CInt(Val(CType(sender, MyRadioButton).Tag))
        If NewType = CurrentType Then Return
        CurrentType = NewType
        View?.ClearSelection()
        RefreshList()
    End Sub

    Private Sub SearchBox_TextChanged(sender As Object, e As EventArgs) Handles SearchBox.TextChanged
        View?.ApplyFilter(SearchBox?.Text, Nothing)
        RefreshSelectBar()
    End Sub

    Public Sub RefreshList()
        If PanList Is Nothing OrElse View Is Nothing Then Return
        View.Reload(CType(CurrentType, ResourceTypes))
        '截图分类没有资源详情页，那行说明换成收藏策略的提示
        If LabListHint IsNot Nothing Then
            LabListHint.Text = If(CurrentType = CInt(ResourceTypes.Screenshot),
                "收藏时会按设置里的策略处理文件，可在「设置 → 其他 → 截图收藏」中更改。",
                "左键点击勾选，右键或悬停后的「详情」按钮进入资源详情页。")
        End If
        RefreshSelectBar()
    End Sub

#Region "多选条"

    ''' <summary>
    ''' 底部多选条的状态切换。
    ''' 只在「隐藏 → 显示」时播入场动画，数量变化只更新文字。
    ''' <para>
    ''' AaTranslateY 是相对增量（引擎内部执行 Y += Delta），不是绝对位置。
    ''' 所以这里绝不读回当前的 Y 去算「还差多少」，而是在每次入场前重建一个 Y = 0 的
    ''' TranslateTransform，再传固定增量。动画落点恒定，反复勾选也不会累积偏移。
    ''' </para>
    ''' </summary>
    Private Sub RefreshSelectBar()
        If CardSelect Is Nothing OrElse View Is Nothing Then Return
        Dim Count As Integer = View.SelectedKeys.Count
        Dim Selected As Boolean = Count > 0
        If Selected Then LabSelect.Text = $"已选择 {Count} 项"
        '安装只对 Mod 有意义
        BtnSelectInstall.IsEnabled = Selected AndAlso CurrentType = CInt(ResourceTypes.Mod)
        '给底部操作条预留高度，避免盖住列表
        CardList.Margin = New Thickness(0, 0, 0, If(Selected, 95, 15))

        '状态没变就只更新文字，不碰动画
        If Selected = SelectBarShown Then Return
        SelectBarShown = Selected

        AniStop("Fav Sidebar")
        If Selected Then
            '入场：重建变换，从 0 出发。固定增量 -27 → +3 → -1，落点恒为 -25
            CardSelect.RenderTransform = New TranslateTransform(0, 0)
            CardSelect.Visibility = Visibility.Visible
            AniStart({
                AaOpacity(CardSelect, 1 - CardSelect.Opacity, 60),
                AaTranslateY(CardSelect, -27, 120, Ease:=New AniEaseOutFluent(AniEasePower.Weak)),
                AaTranslateY(CardSelect, 3, 150, 120, Ease:=New AniEaseInoutFluent(AniEasePower.Weak)),
                AaTranslateY(CardSelect, -1, 90, 270, Ease:=New AniEaseInoutFluent(AniEasePower.Weak))
            }, "Fav Sidebar")
        Else
            '出场：从固定的 -25 往下淡出即可，不需要精确落点
            AniStart({
                AaOpacity(CardSelect, -CardSelect.Opacity, 90),
                AaTranslateY(CardSelect, 15, 90, Ease:=New AniEaseInFluent(AniEasePower.Weak)),
                AaCode(Sub() CardSelect.Visibility = Visibility.Collapsed, After:=True)
            }, "Fav Sidebar")
        End If
    End Sub
    ''' <summary>多选条当前是否处于显示状态。</summary>
    Private SelectBarShown As Boolean = False

    Private Sub BtnSelectCancel_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectCancel.Click
        View?.ClearSelection()
    End Sub

    Private Sub BtnSelectAll_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSelectAll.Click
        If View Is Nothing Then Return
        View.ChangeAllSelected(View.SelectedKeys.Count < View.Shown.Count)
    End Sub

    Private Sub BtnSelectDelete_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectDelete.Click
        If View Is Nothing OrElse Not View.SelectedKeys.Any() Then Return
        Dim Keys = View.SelectedKeys.ToList()
        For Each Entry In ModFavorite.GetByType(CType(CurrentType, ResourceTypes))
            If Keys.Contains(Entry.Key) Then ModFavorite.Remove(Entry)
        Next
        View.ClearSelection()
        RefreshList()
        Hint($"已取消收藏 {Keys.Count} 项", HintType.Blue, False)
    End Sub

    Private Sub BtnSelectShare_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectShare.Click
        ShareEntries(GetSelectedEntries())
    End Sub

    Private Function GetSelectedEntries() As List(Of FavoriteEntry)
        If View Is Nothing Then Return New List(Of FavoriteEntry)
        Return View.Shown.Where(Function(e) View.SelectedKeys.Contains(e.Key)).ToList()
    End Function

    Private Sub ShareEntries(Entries As List(Of FavoriteEntry))
        If Entries Is Nothing OrElse Not Entries.Any() Then
            Hint("没有可以分享的收藏", HintType.Red)
            Return
        End If
        Dim Items As New List(Of ModFavoriteShare.ShareItem)
        For Each Entry In Entries
            Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
            If Project Is Nothing Then Continue For
            Items.Add(New ModFavoriteShare.ShareItem With {.Platform = Project.Platform, .ProjectId = Project.Id})
        Next
        Dim Code As String = ModFavoriteShare.Encode(Items)
        If Code = "" Then
            Hint("这些收藏无法生成分享码", HintType.Red)
            Return
        End If
        ClipboardSet(Code, False)
        MyMsgBox($"分享码已复制到剪贴板，共 {Items.Count} 项：{vbCrLf}{vbCrLf}{Code}", "分享收藏", "好")
    End Sub

#End Region

#Region "安装"

    Private Sub BtnInstall_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnInstall.Click
        InstallSelected()
    End Sub

    Private Sub BtnSelectInstall_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectInstall.Click
        InstallSelected()
    End Sub

    Private Sub InstallSelected()
        Dim Instance As McInstance = PageInstanceLeft.Instance
        If Instance Is Nothing Then
            Hint("还没有选择版本", HintType.Red)
            Return
        End If
        If Not Instance.Modable Then
            Hint("当前版本不能安装 Mod", HintType.Red)
            Return
        End If
        Dim Entries = GetSelectedEntries()
        If Not Entries.Any() Then
            Hint("请先选择要安装的收藏", HintType.Red)
            Return
        End If
        Dim Refs As New List(Of ModFavoriteShare.ShareItem)
        For Each Entry In Entries
            Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
            If Project Is Nothing Then Continue For
            Refs.Add(New ModFavoriteShare.ShareItem With {.Platform = Project.Platform, .ProjectId = Project.Id})
        Next
        If Not Refs.Any() Then
            Hint("这些收藏里没有可以安装的 Mod", HintType.Red)
            Return
        End If
        ModInstallDialog.Show(Refs, Instance)
    End Sub

    Private Sub BtnImport_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnImport.Click
        Dim Code As String = MyMsgBoxInput("导入分享码", "粘贴对方给你的分享码。", ClipboardGetText(), HintText:="PCLDST:...")
        If String.IsNullOrWhiteSpace(Code) Then Return
        Dim Items = ModFavoriteShare.Decode(Code)
        If Not Items.Any() Then
            Hint("无法识别这段分享码", HintType.Red)
            Return
        End If
        RunInThread(
        Sub()
            Dim Added As Integer = 0
            For Each Item In Items
                Try
                    Dim Project = ResourceProject.FromProjectId(Item.ProjectId, Item.Platform)
                    ModFavorite.Add(Project, If(Project.Types.HasFlagF(ResourceTypes.ModPack), ResourceTypes.ModPack, ResourceTypes.Mod))
                    Added += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"导入分享项失败（{Item.ProjectId}）")
                End Try
            Next
            RunInUi(
            Sub()
                RefreshList()
                Hint($"已导入 {Added} 项收藏", HintType.Green)
            End Sub)
        End Sub)
    End Sub

#End Region

    Private Sub OpenDetail(Entry As FavoriteEntry)
        Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
        If Project Is Nothing Then
            Hint("这个收藏的项目信息已失效，请重新搜索后收藏", HintType.Red)
            Return
        End If
        Try
            FrmMain.PageChange(New FormMain.PageStackData With {
                .Page = FormMain.PageType.ResourceDetail,
                .Additional = {Project, New List(Of String), "", ModLoaders.None, CType(Entry.ResourceType, ResourceTypes)}
            })
        Catch ex As Exception
            Logger.Warn(ex, "打开收藏的资源详情失败")
            Hint("打开失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

End Class
