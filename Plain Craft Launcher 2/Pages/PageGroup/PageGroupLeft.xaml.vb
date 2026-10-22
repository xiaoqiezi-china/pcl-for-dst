Public Class PageGroupLeft

    Public PageID As FormMain.PageSubType
    Public Sub New()
        InitializeComponent()
        PageID = FormMain.PageSubType.GroupStar '先进收藏夹，聊天记录按需再加载
    End Sub


    ''' <summary>离线模式下侧边栏不做改动，三个入口都保留（页面自己会显示上线按钮）。</summary>
    Public Sub ApplyOfflineUi()
    End Sub
    Private Sub PageGroupLeft_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanItem.Margin = New Thickness(0, 12, 0, 0)
        '控件初始化完成后，按 PageID 再对一次勾选状态
        '（SetPage 可能在控件还没 Initialize 时就调用过，那时 SetChecked 会被忽略）
        SyncChecks()
    End Sub

    Private Sub PageCheck(sender As MyListItem, e As EventArgs) Handles ItemInfo.Check, ItemReport.Check, ItemStar.Check
        If IsSyncing Then Return
        If sender.Tag IsNot Nothing Then PageChange(Val(sender.Tag))
    End Sub

    ''' <summary>外部跳转（比如主页的「查看全部公告」）用：同步左侧勾选与 PageID，但自己不切页面。</summary>
    Private IsSyncing As Boolean

    Private Sub SyncChecks()
        IsSyncing = True
        Try
            Select Case PageID
                Case FormMain.PageSubType.GroupReport
                    ItemInfo.SetChecked(False, False, False)
                    ItemStar.SetChecked(False, False, False)
                    ItemReport.SetChecked(True, False, False)
                Case FormMain.PageSubType.GroupStar
                    ItemInfo.SetChecked(False, False, False)
                    ItemReport.SetChecked(False, False, False)
                    ItemStar.SetChecked(True, False, False)
                Case Else
                    ItemReport.SetChecked(False, False, False)
                    ItemStar.SetChecked(False, False, False)
                    ItemInfo.SetChecked(True, False, False)
            End Select
        Finally
            IsSyncing = False
        End Try
    End Sub

    Public Sub SetPage(ID As FormMain.PageSubType)
        PageID = ID
        SyncChecks()
    End Sub

    Public Function PageGet(Optional ID As FormMain.PageSubType = -1)
        If ID = -1 Then ID = PageID
        Select Case ID
            Case FormMain.PageSubType.GroupInfo
                If FrmGroupInfo Is Nothing Then FrmGroupInfo = New PageGroupInfo
                Return FrmGroupInfo
            Case FormMain.PageSubType.GroupReport
                If FrmGroupReport Is Nothing Then FrmGroupReport = New PageGroupReport
                Return FrmGroupReport
            Case FormMain.PageSubType.GroupStar
                If FrmGroupStar Is Nothing Then FrmGroupStar = New PageGroupStar
                Return FrmGroupStar
            Case Else
                Throw New Exception("未知的群聊子页面种类：" & ID)
        End Select
    End Function

    Public Sub PageChange(ID As FormMain.PageSubType)
        If PageID = ID Then Return
        AniControlEnabled += 1
        Try
            Select Case ID
                Case FormMain.PageSubType.GroupInfo
                    If FrmGroupInfo Is Nothing Then FrmGroupInfo = New PageGroupInfo
                    PageChangeRun(FrmGroupInfo)
                Case FormMain.PageSubType.GroupReport
                    If FrmGroupReport Is Nothing Then FrmGroupReport = New PageGroupReport
                    PageChangeRun(FrmGroupReport)
                Case FormMain.PageSubType.GroupStar
                    If FrmGroupStar Is Nothing Then FrmGroupStar = New PageGroupStar
                    PageChangeRun(FrmGroupStar)
                    FrmGroupStar.RefreshList()
            End Select
            PageID = ID
        Catch ex As Exception
            Logger.Error(ex, $"切换群聊子页面失败（{ID}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Shared Sub PageChangeRun(Target As MyPageRight)
        AniStop("FrmMain PageChangeRight") '停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        FrmMain.PageRight = Target
        CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
        AniStart({
            AaCode(
            Sub()
                CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                FrmMain.PanMainRight.Child = FrmMain.PageRight
                FrmMain.PageRight.Opacity = 0
            End Sub, 130),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                FrmMain.PageRight.Opacity = 1
                FrmMain.PageRight.PageOnEnter()
            End Sub, 30, True)
        }, "PageLeft PageChange")
    End Sub

End Class
