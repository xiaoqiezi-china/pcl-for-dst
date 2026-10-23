''' <summary>
''' 皮肤事件的分派中心。
''' <para>
''' 皮肤只能通过**动作名**触发行为，不能直接引用或执行任意代码。
''' 这样既让皮肤具备完整的交互能力，又不会把启动器变成脚本宿主——
''' 皮肤包是从外部导入的，绝不能给它执行任意逻辑的入口。
''' </para>
''' </summary>
Public Module SkinEvents

    ''' <summary>最近一次分派的错误说明；Nothing 表示成功。</summary>
    Public Property ErrorText As String = Nothing

    ''' <summary>
    ''' 执行一个皮肤动作。找不到或执行失败时返回 False，并写 <see cref="ErrorText"/>。
    ''' </summary>
    Public Function Invoke(ActionName As String) As Boolean
        ErrorText = Nothing
        If String.IsNullOrWhiteSpace(ActionName) Then
            ErrorText = "动作名为空"
            Return False
        End If
        Dim Name As String = ActionName.Trim()
        Try
            Select Case Name.ToLowerInvariant()
                '---- 启动页 ----
                Case "launchgame"
                    If FrmLaunchLeft Is Nothing Then Return Fail("启动页尚未加载")
                    FrmLaunchLeft.LaunchButtonClick()
                Case "selectversion"
                    FrmMain.PageChange(FormMain.PageType.InstanceSelect)
                Case "openversionsetup"
                    If PageInstanceLeft.Instance Is Nothing Then Return Fail("还没有选择版本")
                    FrmMain.PageChange(FormMain.PageType.InstanceSetup, 0)
                Case "selectaccount"
                    If FrmLaunchLeft Is Nothing Then Return Fail("启动页尚未加载")
                    FrmLaunchLeft.PageChangeToLogin()
                Case "goonline"
                    If FrmLaunchLeft Is Nothing Then Return Fail("启动页尚未加载")
                    FrmLaunchLeft.GoOnlineFromUi(Nothing)
                Case "switchgroup"
                    If FrmLaunchLeft Is Nothing Then Return Fail("启动页尚未加载")
                    FrmLaunchLeft.BtnVersion_Click()
                '---- 导航 ----
                Case "golaunch"
                    FrmMain.PageChange(FormMain.PageType.Launch)
                Case "godownload"
                    FrmMain.PageChange(FormMain.PageType.Download)
                Case "gosetup"
                    FrmMain.PageChange(FormMain.PageType.Setup)
                Case "gomore"
                    FrmMain.PageChange(FormMain.PageType.Other)
                Case "goextra"
                    FrmMain.PageChange(FormMain.PageType.Other, FormMain.PageSubType.OtherAbout)
                Case "goback"
                    FrmMain.PageBack()
                '---- 窗口 ----
                Case "minimizewindow"
                    FrmMain.WindowState = WindowState.Minimized
                Case "closewindow"
                    FrmMain.Close()
                '---- 皮肤自身 ----
                Case "reloadskin"
                    SkinManager.SwitchTo(SkinManager.CurrentId)
                Case Else
                    Return Fail("未知动作：" & Name)
            End Select
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "执行皮肤动作失败：" & Name)
            Return Fail(ex.Message)
        End Try
    End Function

    Private Function Fail(Msg As String) As Boolean
        ErrorText = Msg
        Return False
    End Function

End Module
