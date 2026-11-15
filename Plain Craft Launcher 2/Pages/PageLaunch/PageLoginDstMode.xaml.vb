Imports System.IO
Imports System.Windows.Media.Imaging

''' <summary>
''' 启动页左侧的「QQ 登录」形态：沿用 QQ 形态的头像 + 名字，验证动作只占一个小按钮。
''' 绑定码放在提示行里，点按钮复制命令；验证成功后这里显示游戏名与有效期。
''' </summary>
Public Class PageLoginDstMode

    Public Sub New()
        InitializeComponent()
        '取消/退出用的叉号图标在这里挂，免得在 XAML 里贴路径
        BtnSecondary.Logo = Logo.IconButtonCross
        '皮肤按钮与正版登录页同款图标
        BtnSkin.Logo = Logo.IconButtonSkin
    End Sub

    ''' <summary>页面每次出现都按当前登录态重画一遍。</summary>
    Public Sub Reload(Optional KeepInput As Boolean = True)
        '先用缓存占位，再向服务器要一次最新昵称与头像
        RefreshUi()
        If DstBot.IsQqBound Then DstBot.FetchUserInfo(AddressOf RefreshUi)
    End Sub

    Private Sub PageLoginDstMode_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        RefreshUi()
        If DstBot.IsQqBound Then DstBot.FetchUserInfo(AddressOf RefreshUi)
    End Sub

    ''' <summary>按当前登录态刷新界面；绑定线程每次状态变化都会调用它（已在 UI 线程）。</summary>
    Public Sub RefreshUi()
        ApplyAvatar()
        '皮肤上传要 accessToken，没登录时不给入口，免得点了只会收到 401
        If PanSkin IsNot Nothing Then PanSkin.Visibility = If(ModDstMode.HasUsableCredential, Visibility.Visible, Visibility.Collapsed)
        If ModDstMode.HasUsableCredential Then
            Dim Cred As ModDstMode.DstModeCredential = ModDstMode.Current
            LabName.Text = Cred.Name
            LabHint.Text = "有效期至 " & Cred.ExpiresText
            BtnVerify.Text = "重新登录"
            UpdateSecondaryButton()
            Return
        End If
        Dim ShowName As String = If(DstBot.UserInfoName <> "", DstBot.UserInfoName,
                                    If(DstBot.QqNumber = "", "QQ 登录", "QQ " & DstBot.QqNumber))
        Select Case ModDstMode.State
            Case ModDstMode.BindState.Starting
                LabName.Text = ShowName
                LabHint.Text = "正在连接……"
                BtnVerify.Text = "取消"
            Case ModDstMode.BindState.Waiting
                LabName.Text = ShowName
                LabHint.Text = "在 QQ 里发送" & vbCrLf & DstModeCore.BindCommand(ModDstMode.BindCode, ModDstMode.BindName) &
                              vbCrLf & ModDstMode.CountdownText & vbCrLf & "登录后可用 /dst 皮肤 绑定皮肤"
                BtnVerify.Text = "复制命令"
            Case ModDstMode.BindState.Expired, ModDstMode.BindState.Failed, ModDstMode.BindState.NotConfigured
                LabName.Text = ShowName
                LabHint.Text = If(ModDstMode.StateText = "", "验证失败，点按钮重试", ModDstMode.StateText)
                BtnVerify.Text = "重试"
            Case Else
                LabName.Text = ShowName
                If DstBot.IsQqBound Then
                    LabHint.Text = "未验证"
                    BtnVerify.Text = "登录"
                Else
                    '没绑 QQ 时先绑 QQ：绑定码流程本来就要求玩家在 QQ 里发命令，
                    '没绑的话连机器人是谁都认不出来，直接点「登录」只会白等
                    LabHint.Text = "未绑定 QQ"
                    BtnVerify.Text = "绑定"
                End If
        End Select
        UpdateSecondaryButton()
    End Sub

    ''' <summary>
    ''' 主按钮右边的叉号：等待确认时取消绑定，登录后退出登录，
    ''' 只绑了 QQ 没验证时解除绑定。具体是什么由 ToolTip 说明。
    ''' </summary>
    Private Sub UpdateSecondaryButton()
        If BtnSecondary Is Nothing Then Return
        If ModDstMode.State = ModDstMode.BindState.Waiting Then
            BtnSecondary.ToolTip = "取消绑定"
            BtnSecondary.Visibility = Visibility.Visible
        ElseIf ModDstMode.HasUsableCredential Then
            BtnSecondary.ToolTip = "退出登录"
            BtnSecondary.Visibility = Visibility.Visible
        ElseIf DstBot.IsQqBound Then
            BtnSecondary.ToolTip = "解除 QQ 绑定"
            BtnSecondary.Visibility = Visibility.Visible
        Else
            BtnSecondary.Visibility = Visibility.Collapsed
        End If
    End Sub

#Region "皮肤"

    ''' <summary>
    ''' 皮肤按钮：点一下直接选图上传，右键菜单里可以恢复默认皮肤。
    ''' 与正版登录页那颗皮肤按钮一样，都是图标按钮。
    ''' </summary>
    Private Sub BtnSkin_Click(sender As Object, e As EventArgs) Handles BtnSkin.Click
        UploadSkin()
    End Sub

    Private Sub MenuResetSkin_Click(sender As Object, e As RoutedEventArgs)
        If Not ModDstMode.HasUsableCredential Then
            Hint("请先完成 QQ 登录", HintType.Red)
            Return
        End If
        If MyMsgBox("恢复默认后，你在游戏里会重新显示默认皮肤（服主开了头像皮肤则是你的 QQ 头像）。" & vbCrLf &
                    "已经进入游戏的话，需要重新进入服务器才会生效。" & vbCrLf & vbCrLf & "确定恢复吗？",
                    "恢复默认皮肤", "恢复", "取消", IsWarn:=True) <> 1 Then Return
        Hint("正在恢复默认皮肤……")
        RunInNewThread(
        Sub()
            Dim Result As ModDstSkin.SkinResult = ModDstSkin.ResetSkin()
            Hint(Result.Message, If(Result.Ok, HintType.Green, HintType.Red))
        End Sub, "Reset Dst Skin")
    End Sub

    ''' <summary>
    ''' 选一张 64×64 的皮肤上传。校验与请求都在后台线程做，避免卡住启动页。
    ''' </summary>
    Private Sub UploadSkin()
        If Not ModDstMode.HasUsableCredential Then
            Hint("请先完成 QQ 登录", HintType.Red)
            Return
        End If
        Dim FileName As String = Dialogs.SelectFile("选择皮肤文件（64×64 PNG）", False,
            filter:={({"png"}, "皮肤图片")}).FirstOrDefault()
        If String.IsNullOrEmpty(FileName) Then Return
        '本地先校验一遍：尺寸不对时服务端也会拒，但没必要为此发一次请求
        Dim LocalError As String = ModDstSkin.ValidateFile(FileName)
        If LocalError <> "" Then
            Hint(LocalError, HintType.Red)
            Return
        End If
        Hint("正在上传皮肤……")
        RunInNewThread(
        Sub()
            Dim Result As ModDstSkin.SkinResult = ModDstSkin.Upload(FileName)
            If Result.Ok Then
                Hint("皮肤已更新：" & Result.Message & "，重新进入服务器后生效", HintType.Green)
            Else
                Hint(Result.Message, HintType.Red)
            End If
        End Sub, "Upload Dst Skin")
    End Sub

#End Region

    Private Sub BtnSecondary_Click(sender As Object, e As EventArgs) Handles BtnSecondary.Click
        If ModDstMode.State = ModDstMode.BindState.Waiting Then
            ModDstMode.CancelBind()
            Hint("已取消绑定", HintType.Blue)
            RefreshUi()
            Return
        End If
        If ModDstMode.HasUsableCredential Then
            ModDstMode.SignOut()
            Hint("已退出登录", HintType.Blue)
            RefreshUi()
            Return
        End If
        If DstBot.IsQqBound Then
            If MyMsgBox("解除后要重新在 QQ 里绑定才能继续用 QQ 登录，确定吗？", "解除绑定", "解除", "取消", IsWarn:=True) <> 1 Then Return
            ModDstMode.SignOut()
            DstBot.UnbindQq(Sub(Ok) RefreshUi())
        End If
    End Sub

    Private Sub BtnVerify_Click(sender As Object, e As EventArgs) Handles BtnVerify.Click
        Select Case ModDstMode.State
            Case ModDstMode.BindState.Waiting
                CopyCommand()
            Case ModDstMode.BindState.Starting
                ModDstMode.CancelBind()
                RefreshUi()
            Case Else
                If Not DstBot.IsQqBound Then
                    '先绑定 QQ，绑定成功后会走到 RefreshUi，按钮自动变成「登录」
                    DstBot.ShowBindDialog(AddressOf OnQqBound)
                    Return
                End If
                Dim Name As String = AskName()
                If Name = "" Then Return
                ModDstMode.StartBind(Name, AddressOf OnBindStateChanged)
                RefreshUi()
        End Select
    End Sub

    ''' <summary>
    ''' 问玩家要用什么游戏名。取消返回空串；名字不合法会继续问。
    ''' 名字会拼进绑定命令，机器人据此把 QQ 与游戏角色绑在一起。
    ''' </summary>
    Private Function AskName() As String
        Dim DefaultName As String = Settings.Get(Of String)("DstModeName").Trim()
        Do
            Dim Input As String = MyMsgBoxInput("QQ 登录",
                "输入你想在游戏里使用的名字。" & vbCrLf &
                "3-16 位，只能包含字母、数字、下划线。" & vbCrLf & vbCrLf &
                "登录后还可以在 QQ 里发送 /dst 皮肤 来绑定皮肤。",
                DefaultName, HintText:="游戏名")
            If Input Is Nothing Then Return ""
            Dim Clean As String = Input.Trim()
            If Clean = "" Then Return ""
            If Not DstModeCore.IsValidName(Clean) Then
                Hint("名字只能包含字母、数字、下划线，长度 3-16 位", HintType.Red)
                DefaultName = Clean
                Continue Do
            End If
            Settings.Set("DstModeName", Clean)
            Return Clean
        Loop
    End Function

    Private Sub CopyCommand()
        If ModDstMode.BindCode = "" Then Return
        Try
            My.Computer.Clipboard.SetText(DstModeCore.BindCommand(ModDstMode.BindCode, ModDstMode.BindName))
            Hint("命令已复制，去 QQ 里粘贴发送", HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "复制绑定命令失败")
            Hint("复制失败，请手动输入命令", HintType.Red)
        End Try
    End Sub

    ''' <summary>刚绑好 QQ：重画界面，并把昵称头像补上（绑定接口只给 QQ 号）。</summary>
    Private Sub OnQqBound()
        RefreshUi()
        If DstBot.IsQqBound Then DstBot.FetchUserInfo(AddressOf RefreshUi)
    End Sub

    ''' <summary>绑定线程回调：始终刷新当前存在的那个页面实例。</summary>
    Private Shared Sub OnBindStateChanged()
        If FrmLoginDstMode IsNot Nothing Then FrmLoginDstMode.RefreshUi()
    End Sub

    ''' <summary>头像直接复用 QQ 形态那一套：命中缓存立即显示，否则后台抓取后回填。</summary>
    Private Sub ApplyAvatar()
        Dim Qq As String = If(DstBot.UserInfoQq <> "", DstBot.UserInfoQq, DstBot.QqNumber)
        If Qq = "" Then
            PanAvatar.Background = New SolidColorBrush(BackColorSub)
            Return
        End If
        Try
            Dim FilePath As String = DstBot.AvatarCacheFile(Qq)
            If FilePath <> "" AndAlso File.Exists(FilePath) Then
                Dim Cached As BitmapImage = LoadLocal(FilePath)
                If Cached IsNot Nothing Then
                    PanAvatar.Background = New ImageBrush(Cached) With {.Stretch = Stretch.UniformToFill}
                    Return
                End If
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取 QQ 头像失败")
        End Try
        RunInThread(
        Sub()
            Try
                Dim Saved As String = DstBot.EnsureAvatarFile(Qq)
                If Saved = "" Then Return
                Dim Bmp As BitmapImage = LoadLocal(Saved)
                If Bmp Is Nothing Then Return
                RunInUi(
                Sub()
                    PanAvatar.Background = New ImageBrush(Bmp) With {.Stretch = Stretch.UniformToFill}
                End Sub)
            Catch ex As Exception
                Logger.Info(ex, "加载 QQ 头像失败")
            End Try
        End Sub)
    End Sub

    Private Function LoadLocal(FilePath As String) As BitmapImage
        Try
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.UriSource = New Uri(FilePath, UriKind.Absolute)
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.DecodePixelWidth = 176
            Bmp.EndInit()
            Bmp.Freeze()
            Return Bmp
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>获取当前页面的登录信息。</summary>
    Public Shared Function GetLoginData() As McLoginData
        Return New McLoginDstMode()
    End Function

    ''' <summary>当前页面的登录信息是否有效。</summary>
    Public Shared Function IsVaild(LoginData As McLoginDstMode) As String
        If Not ModDstMode.HasUsableCredential Then Return "请先在启动页完成 QQ 登录"
        Return ""
    End Function

    Public Function IsVaild() As String
        Return IsVaild(New McLoginDstMode())
    End Function

    ''' <summary>退出登录（凭据只存在内存里，清掉即结束）。</summary>
    Public Shared Sub ExitLogin()
        ModDstMode.SignOut()
        OnBindStateChanged()
    End Sub

End Class
