''' <summary>
''' QQ 页的收藏夹：分类子夹 + 与模组管理一致的列表与多选条，支持一键安装到当前整合包、分享收藏。
''' </summary>
Public Class PageGroupStar

    ''' <summary>当前分类，取值同 ResourceTypes。</summary>
    Private CurrentType As Integer = 1

    ''' <summary>列表视图，与版本设置里的收藏夹页共用同一份实现。</summary>
    Private View As FavoriteListView
    Public Sub New()
        InitializeComponent()
    End Sub

    ''' <summary>离线模式：隐藏云同步开关，换成一个「上线」按钮；收藏夹本地照常可用。</summary>
    Public Sub ApplyOfflineUi()
        Dim Offline As Boolean = DstBot.IsOffline
        CheckCloudSync.Visibility = If(Offline, Visibility.Collapsed, Visibility.Visible)
        BtnGoOnline.Visibility = If(Offline, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub BtnGoOnline_Click(sender As Object, e As EventArgs) Handles BtnGoOnline.Click
        If BtnGoOnline.IsEnabled = False Then Return
        BtnGoOnline.IsEnabled = False
        BtnGoOnline.Text = "连接中"
        RunInThread(
        Sub()
            Dim Ok As Boolean = DstBot.GoOnline()
            RunInUi(
            Sub()
                BtnGoOnline.IsEnabled = True
                BtnGoOnline.Text = "上线"
                If Ok Then
                    Hint("已回到在线模式", HintType.Green, False)
                    If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshDependentViewsAfterModeChange()
                Else
                    Hint("远程服务不可用，继续保持常规模式", HintType.Red)
                End If
                ApplyOfflineUi()
                RefreshList()
            End Sub)
        End Sub)
    End Sub

    ''' <summary>同步开关：只响应用户点击，程序里改 Checked 不会触发这里。</summary>
    Private Sub CheckCloudSync_Change(sender As Object, user As Boolean) Handles CheckCloudSync.Change
        If Not IsLoaded OrElse Not user Then Return
        ModFavorite.SyncToCloud = CheckCloudSync.Checked
        If Not CheckCloudSync.Checked Then
            Hint("已关闭云同步，收藏只保存在本地", HintType.Blue, False)
            Return
        End If
        If Not DstBot.IsQqBound Then
            Hint("还没绑定 QQ，无法云同步", HintType.Red)
            CheckCloudSync.Checked = False
            ModFavorite.SyncToCloud = False
            Return
        End If
        MergeWithCloud(ShowResult:=True)
    End Sub

    ''' <summary>
    ''' 与云端合并一次。合并而不是覆盖：新设备本地是空的，合并后会把云端的收藏拉下来。
    ''' </summary>
    Private Sub MergeWithCloud(ShowResult As Boolean)
        If Not DstBot.IsQqBound Then Return
        CheckCloudSync.IsEnabled = False
        RunInThread(
        Sub()
            Dim Err As String = ModFavorite.PullFromCloud(Force:=ShowResult)
            RunInUi(
            Sub()
                CheckCloudSync.IsEnabled = True
                If Err Is Nothing Then
                    If ShowResult Then Hint("收藏夹已与云端合并", HintType.Green, False)
                Else
                    If ShowResult Then Hint("同步失败：" & Err, HintType.Red)
                End If
                RefreshList()
            End Sub)
        End Sub)
    End Sub

    Private Sub PageGroupStar_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '多选条用 PCL 自带的图标，和模组管理保持一致
        BtnSelectInstall.Logo = Logo.IconButtonSave
        BtnSelectShare.Logo = Logo.IconButtonCard
        BtnSelectDelete.Logo = Logo.IconButtonCross
        BtnSelectCancel.Logo = Logo.IconButtonCross

        If View Is Nothing Then
            View = New FavoriteListView(PanList)
            AddHandler View.SelectionChanged, Sub() RefreshSelectBar()
            AddHandler View.EntryActivated, Sub(Entry) OpenDetail(Entry)
            AddHandler View.EntryRemoved, Sub(Entry)
                                                ModFavorite.Remove(Entry)
                                                RefreshList()
                                                Hint("已取消收藏", HintType.Blue, False)
                                            End Sub
        End If

        '按 Tag 决定初始分类
        Dim Tabs() As MyRadioButton = {BtnFilterMod, BtnFilterModPack, BtnFilterDataPack, BtnFilterResourcePack, BtnFilterShader}
        Dim Hit = Tabs.FirstOrDefault(Function(x) CInt(Val(x.Tag)) = CurrentType)
        If Hit IsNot Nothing Then Hit.SetChecked(True, False, False)
        CheckCloudSync.Checked = ModFavorite.SyncToCloud
        ApplyOfflineUi()
        RefreshList()
        If ModFavorite.SyncToCloud AndAlso DstBot.IsQqBound Then MergeWithCloud(ShowResult:=False)
    End Sub

    Private Sub Filter_Check(sender As Object, raiseByMouse As Boolean) Handles BtnFilterMod.Check, BtnFilterModPack.Check,
            BtnFilterDataPack.Check, BtnFilterResourcePack.Check, BtnFilterShader.Check
        If Not raiseByMouse Then Return
        Dim NewType As Integer = CInt(Val(CType(sender, MyRadioButton).Tag))
        If NewType = CurrentType Then Return
        CurrentType = NewType
        View?.ClearSelection()
        RefreshList()
    End Sub

    Private Sub SearchBox_TextChanged(sender As Object, e As EventArgs) Handles SearchBox.TextChanged
        ApplyFilter()
    End Sub

    ''' <summary>重画当前分类的收藏列表。</summary>
    Public Sub RefreshList()
        If PanList Is Nothing Then Return
        If View Is Nothing Then Return
        View.Reload(CType(CurrentType, ResourceTypes))
        RefreshSelectBar()
    End Sub

    Private Sub ApplyFilter()
        View?.ApplyFilter(SearchBox?.Text, Nothing)
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

    Private Sub BtnShare_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnShare.Click
        '没选中就分享当前分类全部
        Dim Entries = GetSelectedEntries()
        If Not Entries.Any() Then
            Entries = ModFavorite.GetByType(CType(CurrentType, ResourceTypes))
        End If
        ShareEntries(Entries)
    End Sub

    Private Function GetSelectedEntries() As List(Of FavoriteEntry)
        If View Is Nothing Then Return New List(Of FavoriteEntry)
        Return View.Shown.Where(Function(e) View.SelectedKeys.Contains(e.Key)).ToList()
    End Function

    ''' <summary>把条目编码成分享码放进剪贴板。</summary>
    Private Sub ShareEntries(Entries As List(Of FavoriteEntry))
        If Entries Is Nothing OrElse Not Entries.Any() Then
            Hint("没有可以分享的收藏", HintType.Red)
            Return
        End If
        Dim Items As New List(Of ModFavoriteShare.ShareItem)
        For Each Entry In Entries
            Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
            If Project Is Nothing Then Continue For
            Items.Add(New ModFavoriteShare.ShareItem With {
                .Platform = Project.Platform, .ProjectId = Project.Id})
        Next
        Dim Code As String = ModFavoriteShare.Encode(Items)
        If Code = "" Then
            Hint("这些收藏无法生成分享码（只支持 Mod 与整合包）", HintType.Red)
            Return
        End If
        ClipboardSet(Code, False)
        MyMsgBox($"分享码已复制到剪贴板，共 {Items.Count} 项：{vbCrLf}{vbCrLf}{Code}{vbCrLf}{vbCrLf}对方可以在收藏夹页点「导入分享码」粘贴这段文本。",
                 "分享收藏", "好")
    End Sub

    Private Sub BtnImport_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnImport.Click
        Dim Code As String = MyMsgBoxInput("导入分享码", "粘贴对方给你的分享码，导入后这些资源会出现在对应的分类里。",
                                           ClipboardGetText(), HintText:="PCLDST:...")
        If String.IsNullOrWhiteSpace(Code) Then Return
        Dim Items = ModFavoriteShare.Decode(Code)
        If Not Items.Any() Then
            Hint("无法识别这段分享码", HintType.Red)
            Return
        End If
        RunInThread(
        Sub()
            Dim Added As Integer = 0
            Dim Failed As Integer = 0
            For Each Item In Items
                Try
                    Dim Project = ResourceProject.FromProjectId(Item.ProjectId, Item.Platform)
                    Dim Category As ResourceTypes = If(Project.Types.HasFlagF(ResourceTypes.ModPack),
                        ResourceTypes.ModPack, ResourceTypes.Mod)
                    ModFavorite.Add(Project, Category)
                    Added += 1
                Catch ex As Exception
                    Failed += 1
                    Logger.Warn(ex, $"导入分享项失败（{Item.ProjectId}）")
                End Try
            Next
            RunInUi(
            Sub()
                RefreshList()
                Hint(If(Failed = 0, $"已导入 {Added} 项收藏", $"已导入 {Added} 项，{Failed} 项获取失败"),
                     If(Failed = 0, HintType.Green, HintType.Blue))
            End Sub)
        End Sub)
    End Sub

#End Region

#Region "安装到版本"

    Private Sub BtnOpenInstance_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenInstance.Click
        InstallToInstance(GetSelectedEntries())
    End Sub

    Private Sub BtnSelectInstall_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectInstall.Click
        InstallToInstance(GetSelectedEntries())
    End Sub

    ''' <summary>
    ''' 把若干收藏安装到当前选中的版本。会先分析版本、加载器与前置，给出清单再执行。
    ''' </summary>
    Private Sub InstallToInstance(Entries As List(Of FavoriteEntry))
        If Entries Is Nothing OrElse Not Entries.Any() Then
            Hint("请先选择要安装的收藏", HintType.Red)
            Return
        End If
        Dim Instance As McInstance = McInstanceSelected
        If Instance Is Nothing OrElse Not Instance.Modable Then
            Hint("请先在版本选择里选一个可以安装 Mod 的版本", HintType.Red)
            Return
        End If
        '只保留 Mod 类
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

#End Region

    ''' <summary>还原项目对象后打开资源详情页（与下载页点卡片走同一条路）。</summary>
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
