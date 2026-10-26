Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Net.Sockets
Imports System.Reflection
Imports PCL

Public Class PageLinkMain
    Private Const INVITE_CODE_VERSION As Integer = 4

    '===============================
    '  状态机与前端页面
    '===============================

#Region "状态管理"

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared LinkState As LinkStates = LinkStates.Waiting

    ''' <summary>
    ''' 切换到指定的状态。
    ''' </summary>
    Private Sub ChangeState(NewState As LinkStates)
        If LinkState = NewState Then Return
        Dim OldState = LinkState
        LinkState = NewState
        Logger.Info($"主状态由 {OldState} 变更为 {NewState}")
        '触发状态切换
        RunInUi(
        Sub()
            Select Case NewState
                Case LinkStates.Waiting
                    SwitchToWaiting(OldState)
                Case LinkStates.Loading
                    SwitchToLoading(OldState)
                Case LinkStates.Failed
                    SwitchToFailed(OldState)
                Case LinkStates.Finished
                    SwitchToFinished(OldState)
            End Select
        End Sub)
    End Sub

    '让加载器的 成功/失败 事件触发状态切换
    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, PanAlways, LinkLoader, AutoRun:=False)
        AddHandler LinkLoader.OnStateChangedUi,
        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
            Select Case NewState
                Case LoadState.Finished
                    ChangeState(LinkStates.Finished)
                Case LoadState.Failed
                    Telemetry("联机失败", "Exception", FilterUserName(Loader.Error.GetDisplay(True), "*"))
                    ChangeState(LinkStates.Failed)
            End Select
        End Sub
    End Sub

    '页面动画兼容
    Private Sub UpdatePanelVisibility() Handles Me.PageEnter
        FrmLinkMain.PanSelect.Visibility = If(LinkState = LinkStates.Waiting, Visibility.Visible, Visibility.Collapsed)
        FrmLinkMain.PanFinish.Visibility = If(LinkState = LinkStates.Finished, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "初始页面 (Waiting)"

    '由任意状态切换到 Waiting
    Private Sub SwitchToWaiting(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        SyncLock LinkLoader.LockState
            LinkLoader.State = LoadState.Waiting
        End SyncLock
        ProcessStop()
        '页面切换
        If OldState <> LinkStates.Finished Then
            '从完成页面退出时，PageOnContentExit 结束会触发 PageEnter 事件，这会调用 UpdatePanelVisibility
            '如果在现在提前调用了，会丢失完成页面的退出动画
            UpdatePanelVisibility()
        End If
        PageOnContentExit()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    ''' <summary>左上角「联机设置」：进入节点设置副页面。</summary>
    Private Sub BtnLinkSettings_Click(sender As Object, e As EventArgs) Handles BtnLinkSettings.Click
        FrmMain.PageChange(FormMain.PageType.LinkSettings)
    End Sub

    '创建
    Private Sub Create_MouseLeftButtonUp() Handles PanSelectCreate.MouseLeftButtonUp
        '输入端口号
        Dim Port As String = MyMsgBoxInput("输入端口", $"在单人游戏的暂停菜单选择 {vbLQ}对局域网开放{vbRQ}，然后输入端口数字。{vbCrLf}甚至可以输入其他游戏的端口……嗯……",
            ValidateRules:=New Collection(Of Validate) From {New ValidateInteger(1024, 65535)},
            HintText:="端口号")
        If Port Is Nothing Then Return
        '基础信息
        IsServerSide = True
        ServerPort = Port
        Dim GenerateRandomCode =
        Function() As String
            Dim Result As String = ""
            For i = 1 To 5
                Result &= RandomInteger(0, 35).ConvertRadix(36)
            Next
            Return Result.Replace("O", "0").Replace("I", "1")
        End Function
        NetworkName = $"P{ServerPort.ConvertRadix(16).PadLeft(4, "0"c)}-{GenerateRandomCode()}"
        NetworkSecret = GenerateRandomCode()
        DiscoverNodeID = -1
        Logger.Info($"尝试创建房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}")
        '启动
        ChangeState(LinkStates.Loading)
    End Sub

    '加入
    Private Shared LastCode As String = Nothing
    ''' <summary>
    ''' 输入邀请码，切换到联机页并立即加入房间。
    ''' </summary>
    Public Shared Sub Join() Handles PanSelectJoin.MouseLeftButtonUp
        Dim Code As String = MyMsgBoxInput("输入邀请码", "输入房主发给你的邀请码。",
            HintText:=If(String.IsNullOrEmpty(LastCode), "", "使用上一次的邀请码"))
        If Not String.IsNullOrEmpty(LastCode) AndAlso Code IsNot Nothing AndAlso Code = "" Then Code = LastCode
        If Code Is Nothing Then Return
        Join(Code)
    End Sub
    Public Sub JoinInternal(Code As String)
        If Code Is Nothing Then Return
        '基础格式校验
        Dim ValidateResult = ValidateCodeFormat(Code)
        If ValidateResult IsNot Nothing Then
            Hint(ValidateResult, HintType.Red)
            Return
        End If
        Code = FixCodeFormat(Code)
        Logger.Info($"实际使用的邀请码：{Code}")
        '基础信息
        IsServerSide = False
        ServerPort = Code.Substring(1, 4).Upper.ConvertRadix(16, 10)
        NetworkName = Code.Substring(0, 11)
        NetworkSecret = Code.Substring(12, 5)
        If Code.Substring(20, 3) = "000" Then
            DiscoverNodeID = -2
        Else
            DiscoverNodeID = Code.Substring(20, 3).Upper.ConvertRadix(16, 10)
        End If
        '版本 4 起：第 24 位之后是编码过的节点地址（见 ModInviteCode）。
        '版本 3 的明文地址仍要认，老邀请码不能失效。
        DiscoverNodeAddress = ""
        If Code.Length > 24 AndAlso Code(23) = "-"c Then
            Dim Tail As String = Code.Substring(24).Trim()
            If ModInviteCode.IsPlainAddress(Tail) Then
                DiscoverNodeAddress = ModInviteCode.NormalizePlainAddress(Tail)
            Else
                DiscoverNodeAddress = ModInviteCode.Decode(Tail)
                If DiscoverNodeAddress = "" Then
                    Logger.Warn($"邀请码里的节点信息无法解析，将回退到按节点 ID 选择：{Tail}")
                End If
            End If
        End If
        Logger.Info($"尝试加入房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}，发现节点 {DiscoverNodeID}，节点地址 {If(DiscoverNodeAddress = "", "(未提供)", DiscoverNodeAddress)}")
        '启动
        LastCode = Code
        ChangeState(LinkStates.Loading)
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        If Code Is Nothing Then Return "邀请码为空！"
        Code = FixCodeFormat(Code)
        '判断类型
        If Not (Code.Length >= 14 AndAlso Code(0) = "P"c AndAlso Code(5) = "-"c AndAlso Code(11) = "-"c) Then
            If Code.StartsWithF("U/") Then 'HMCL
                Return "请让房主使用 PCL 创建房间！"
            ElseIf Code.Length = 10 Then 'PCL CE
                Return "请让房主使用非社区版的 PCL 创建房间！"
            Else
                Return "邀请码有误，请让房主使用 PCL 创建房间！"
            End If
        End If
        '校验版本
        If Code.Length >= 23 AndAlso Code(17) = "-"c AndAlso
            Val(Code.Substring(18, 2)) > INVITE_CODE_VERSION Then Return "你的 PCL 版本太老了，请在更新 PCL 之后再联机！"
        Return Nothing
    End Function
    Private Shared Function FixCodeFormat(Code As String) As String
        Code = Code.Between("【", "】").Between("[", "]") '从完整消息中提取
        Code = Code.Trim()
        '只把前半段（网络名/密码/版本/ID）转大写做输入修正，
        '后面的节点地址要保留原样——域名和协议都是小写的，转大写就连不上了
        Dim AddrStart As Integer = -1
        For Each Scheme In {"-tcp://", "-udp://", "-ws://", "-wss://"}
            Dim Hit As Integer = Code.IndexOf(Scheme, StringComparison.OrdinalIgnoreCase)
            If Hit >= 0 AndAlso Hit > 20 Then
                AddrStart = Hit
                Exit For
            End If
        Next
        If AddrStart >= 0 Then
            Dim Head As String = Code.Substring(0, AddrStart).Upper.Replace("O", "0").Replace("I", "1")
            Return Head & Code.Substring(AddrStart)
        End If
        Code = Code.Upper.Replace("O", "0").Replace("I", "1") '输入修正
        '版本 1 兼容
        If Code.Length >= 17 AndAlso (Code.Length < 23 OrElse (Code.Length >= 18 AndAlso Code(17) <> "-"c)) Then
            Code = Code.Substring(0, 17) & "-0105E"
        End If
        Return Code
    End Function

    '自动加入

    ''' <summary>
    ''' 切换到联机页并立即加入指定房间。
    ''' </summary>
    Public Shared Sub Join(Code As String)
        If LinkState <> LinkStates.Waiting Then
            Hint("你已经在联机房间中了！", HintType.Red)
        ElseIf FrmMain.PageCurrent = FormMain.PageType.Link Then
            FrmLinkMain.JoinInternal(Code)
        Else
            AutoJoinCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub
    '在进入页面时自动尝试加入房间
    Private Shared AutoJoinCode As String = Nothing
    ''' <summary>
    ''' 每次进入本页都检查一次待加入的邀请码。
    ''' <para>
    ''' 不能只挂在 PanSelectJoin.Loaded 上：联机设置是独立副页面，
    ''' 从设置页返回时主页面早已加载过，Loaded 不会再触发，待加入的邀请码就丢了。
    ''' </para>
    ''' </summary>
    Private Sub PanSelectJoin_Enter() Handles PanSelectJoin.Loaded, Me.PageEnter
        If AutoJoinCode Is Nothing Then Return
        If LinkState = LinkStates.Waiting Then JoinInternal(AutoJoinCode)
        AutoJoinCode = Nothing
    End Sub

#End Region

#Region "加载页面 (Loading | Failed)"

    '由 Waiting 或 Failed 状态切换到 Loading
    Private Sub SwitchToLoading(OldState As LinkStates)
        '清理变量
        Peers = Nothing
        FailCount = 0
        '加载器与进程状态
        LinkLoader.Start(IsForceRestart:=True)
        '挂到任务管理器：下载内核有 32MB，用户要能在「下载」里看到进度
        LoaderTaskbarAdd(LinkLoader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        '页面切换会由 Loader 调用 MyPageRight 来触发
        'UI 更新
        UpdateProgressBar(0)
        LabLoadTitle.Text = If(IsServerSide, "创建房间中", "加入房间中")
        UpdateLoadingPage("正在初始化……", "准备初始化")
    End Sub

    '由 Loading 状态切换到 Failed
    Private Sub SwitchToFailed(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        ProcessStop()
        'UI 更新
        UpdateProgressBar(1)
        '显示错误信息。取消时 Error 是 Nothing，这里必须判空，
        '否则取 GetDisplay 会抛异常，界面停在一片空白
        LabLoadTitle.Text = FailReason
        If LinkLoader.Error Is Nothing Then
            LabLoadDesc.Text = "已取消"
            Return
        End If
        Dim Brief As String = LinkLoader.Error.GetDisplay(False)
        Dim InnerEx As Exception = LinkLoader.Error
        If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Do Until InnerEx.InnerException Is Nothing
            InnerEx = InnerEx.InnerException
            If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Loop
        LabLoadDesc.Text = If(Brief.StartsWithF("$"), Brief.TrimStart("$"), LinkLoader.Error.GetDisplay(True))
        Logger.Warn(LinkLoader.Error, LabLoadTitle.Text)
    End Sub

    '===============================
    '  页面 UI
    '===============================

    Private Sub UpdateLoadingPage(Title As String, FailBrief As String)
        If FailReason = FailBrief & "失败" Then Return
        FailReason = FailBrief & "失败"
        Logger.Info($"开始步骤：{Title}")
        RunInUiWait(
        Sub()
            If FrmLinkMain Is Nothing OrElse Not FrmLinkMain.LabLoadDesc.IsLoaded Then Return
            FrmLinkMain.LabLoadDesc.Text = Title
        End Sub)
    End Sub
    Private FailReason As String = "准备初始化"

    '取消加载
    Private Sub BtnLoadCancel_Click() Handles BtnLoadCancel.Click
        ChangeState(LinkStates.Waiting)
    End Sub

    '点击重试
    Private Sub CardLoad_MouseLeftButtonUp() Handles CardLoad.MouseLeftButtonUp
        ChangeState(LinkStates.Loading)
    End Sub

    '进度条
    Private Sub UpdateProgressBar(Optional Value As Double = -1)
        If Value = -1 Then Value = LinkLoader.Progress
        Dim DisplayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(Value - DisplayingProgress, 3) = 0 Then Return
        If DisplayingProgress <= Value Then
            Dim NewProgress As Double = If(Value = 1, 1, DisplayingProgress +
                (Value - DisplayingProgress) ^ 2 * 0.5)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, NewProgress - ColumnProgressA.Width.Value, 200),
                AaGridLengthWidth(ColumnProgressB, (1 - NewProgress) - ColumnProgressB.Width.Value, 200)
            }, "Link Progress")
        Else
            ColumnProgressA.Width = New GridLength(Value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - Value, GridUnitType.Star)
            AniStop("Link Progress")
        End If
    End Sub
    Private Sub CardLoad_SizeChanged() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "完成页面 (Finished)"

    '由 Loading 状态切换到 Finished
    Private Sub SwitchToFinished(OldState As LinkStates)
        '由于只可能从加载器的完成事件触发，不需要管加载器
        UpdatePanelVisibility() '页面的实际切换会由 Loader 调用 MyPageRight 来触发

        'UI 更新
        If IsServerSide Then
            LabFinishTitle.Text = "已创建房间"
            LabFinishDesc.Text = $"把邀请码发给朋友，让大家加入房间吧！{vbCrLf}邀请码：{GetInviteCode()}"
            BtnFinishExit.Text = "关闭"
            BtnFinishPing.ToolTip = "网络延迟"
            BtnFinishCopy.Visibility = Visibility.Visible
            Copy() '立即复制邀请码
            '下边栏
            BtnFinishPort.Visibility = Visibility.Visible
            LabFinishPort.Visibility = Visibility.Visible
            BtnFinishIp.Visibility = Visibility.Collapsed
            LabFinishIp.Visibility = Visibility.Collapsed
            LabFinishPort.Text = ServerPort
        Else
            LabFinishTitle.Text = "已加入房间"
            LabFinishDesc.Text = $"在多人游戏页面的最下方就能找到联机房间！{vbCrLf}注意：使用离线登录时不要手动输入 IP！"
            BtnFinishExit.Text = "离开"
            BtnFinishPing.ToolTip = "与房主的延迟"
            BtnFinishCopy.Visibility = Visibility.Collapsed
            '下边栏
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            LabFinishIp.Text = ClientAddress
            BtnFinishIp.Visibility = Visibility.Visible
            LabFinishIp.Visibility = Visibility.Visible
        End If
        Update() '立即刷新
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '退出
    Private Sub BtnFinishExit_Click() Handles BtnFinishExit.Click
        TryExit(False, False)
    End Sub
    ''' <summary>
    ''' 退出联机。
    ''' 返回是否弹出了警告窗口并且玩家选择了取消。
    ''' </summary>
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If LinkState = LinkStates.Waiting OrElse LinkState = LinkStates.Failed Then Return False
        If Not Silent Then
            If IsServerSide AndAlso PeopleCount > 1 Then
                If MyMsgBox("你确定要关闭联机房间吗？" & vbCrLf & "所有玩家都需要重新输入邀请码才可加入游戏！", "退出联机", "确定", "取消", IsWarn:=True) = 2 Then Return True
            ElseIf Closing Then
                If MyMsgBox(If(IsServerSide, "你确定要关闭联机房间吗？", "你确定要离开联机房间吗？"), "退出联机", "确定", "取消") = 2 Then Return True
            End If
        End If
        ChangeState(LinkStates.Waiting)
        Return False
    End Function

    '复制邀请码
    Private Sub Copy() Handles BtnFinishCopy.Click
        Dim CodeText As String = $"在 DPCL 启动器中输入邀请码【{GetInviteCode()}】，即可加入联机房间！"
        ClipboardSet(CodeText, False)
        Settings.Set("LinkLastAutoJoinInviteCode", CodeText)
        Hint("已复制邀请码！", HintType.Green)
    End Sub
    ''' <summary>
    ''' 生成邀请码。版本 4 起在末尾追加房主实际使用的节点地址，
    ''' 这样成员不必依赖「节点 ID 到地址」的映射——那份清单是动态的，
    ''' 房主和成员拉到的版本可能不同，ID 就会指向不同地址。
    ''' <para>
    ''' 地址不是明文拼进去的，而是先打包成二进制再转 Base32（见 <see cref="ModInviteCode"/>）。
    ''' 邀请码会被到处转发，明文写 tcp://host:port 等于把房主走的节点公开，
    ''' 编码后就是一串普通的大写字母数字，和前半段看起来没区别。
    ''' </para>
    ''' </summary>
    Private Function GetInviteCode() As String
        Dim Base As String = $"{NetworkName}-{NetworkSecret}-{INVITE_CODE_VERSION.ToString.PadLeft(2, "0"c)}{ _
            If(DiscoverNodeID = -1, 0, DiscoverNodeID).ConvertRadix(16).PadLeft(3, "0"c)}"
        Dim Blob As String = ModInviteCode.Encode(DiscoverNodeAddress)
        If Blob <> "" Then
            Base &= "-" & Blob
        ElseIf Not String.IsNullOrWhiteSpace(DiscoverNodeAddress) Then
            '编不出来说明地址本身有问题，这时候宁可让成员按 ID 兜底，
            '也不要把畸形地址写进邀请码
            Logger.Warn($"节点地址无法编码进邀请码，将回退到按节点 ID 选择：{DiscoverNodeAddress}")
        End If
        Return Base
    End Function

    '复制 IP
    Private Sub BtnFinishIp_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles BtnFinishIp.MouseLeftButtonUp
        ClipboardSet(ClientAddress, False)
        Hint("已复制服务器地址！", HintType.Green)
    End Sub

#End Region

    '===============================
    '  后端
    '===============================

    ''' <summary>
    ''' EasyTier 所在的文件夹路径，以 \ 结尾。
    ''' </summary>
    Private PathEasyTier As String = Paths.AppDataThenName & "EasyTier\"
    ''' <summary>
    ''' 当前是服务端还是客户端。
    ''' </summary>
    Private IsServerSide As Boolean
    ''' <summary>
    ''' 服务端映射前的端口号。
    ''' </summary>
    Private ServerPort As Integer
    ''' <summary>
    ''' 客户端映射后的端口号。
    ''' </summary>
    Private ClientPort As Integer
    ''' <summary>
    ''' RPC 端口号。
    ''' </summary>
    Private RPCPort As Integer
    ''' <summary>
    ''' 在客户端映射后的地址。
    ''' 除非已作为客户端启动 EasyTier，否则一直为 Nothing。
    ''' </summary>
    Private ClientAddress As String = Nothing
    ''' <summary>
    ''' 发现节点的 ID。
    ''' 若必须设定自定义节点则为 -2；若等待选择则为 -1；选择回退节点则为 0；否则为对应节点的 ID。
    ''' </summary>
    Private DiscoverNodeID As Integer = -1
    ''' <summary>房主实际使用的发现节点地址。版本 3 起随邀请码一起发出去。</summary>
    Private DiscoverNodeAddress As String = ""
    ''' <summary>
    ''' 网络信息。
    ''' </summary>
    Private NetworkName As String, NetworkSecret As String

#Region "加载"

    Private WithEvents LinkLoader As New LoaderCombo(Of Integer)("联机", {
        New LoaderTask(Of Integer, Integer)("获取配置", AddressOf InitConfig) With {.ProgressWeight = 8},
        New LoaderTask(Of Integer, List(Of NetFile))("准备下载联机模块", AddressOf InitPrepareDownload) With {.ProgressWeight = 2},
        New LoaderDownload("下载联机模块", New List(Of NetFile)) With {.ProgressWeight = 40},
        New LoaderTask(Of Integer, Integer)("启动联机模块", AddressOf InitLaunch) With {.ProgressWeight = 50}
    })

    '1. 获取服务器配置
    Private Sub InitConfig(Task As LoaderTask(Of Integer, Integer))
        UpdateLoadingPage("正在联网获取配置……", "联网获取配置")
        ServerLoader.WaitForExit(LoaderToSyncProgress:=Task)
        If ServerConfig Is Nothing Then Throw New Exception("无法从服务器获取配置")
        '检查是否已禁用联机功能
        Dim DisableReason = ServerConfig("Link")?("DisableReason2")?.ToString
        If Not String.IsNullOrEmpty(DisableReason) Then Throw New Exception("$" & DisableReason) 'TODO: 可能不会显示错误原因？
        If CType(ServerConfig("Link"), JObject).ContainsKey("MinVersionCode") AndAlso
           VersionCode < ServerConfig("Link")("MinVersionCode").ToObject(Of Integer) Then
            Throw New Exception("$你的 PCL 版本太老了，请在更新 PCL 之后再联机！")
        End If
    End Sub

    '2. 获取需要下载的文件
    Private ServerVersion As Integer
    Private Sub InitPrepareDownload(Task As LoaderTask(Of Integer, List(Of NetFile)))
        '获取 CPU 架构
        UpdateLoadingPage("正在获取 CPU 架构……", "获取 CPU 架构")
        Dim Architecture As String = GetType(String).Assembly.GetName().ProcessorArchitecture
        Select Case Architecture
            Case ProcessorArchitecture.X86
                Architecture = "i686"
            Case ProcessorArchitecture.Amd64
                Architecture = "x86_64"
            Case ProcessorArchitecture.Arm
                Architecture = "arm64"
            Case Else
                Logger.Warn($"CPU 是不支持的 {Architecture} 架构，这可能会导致联机模块无法启动！")
                Architecture = "arm64"
        End Select
        Logger.Info($"CPU 架构：{Architecture}")
        Telemetry("联机开始")
        '检查 EasyTier 版本
        UpdateLoadingPage("正在检查联机模块版本……", "检查联机模块版本")
        If Not (FileUtils.Exists(PathEasyTier & "联机模块 CLI.exe") AndAlso FileUtils.Exists(PathEasyTier & "联机模块.exe") AndAlso
                FileUtils.Exists(PathEasyTier & "Packet.dll")) Then
            Settings.Set("LinkEasyTierVersion", -1)
        End If
        Dim LocalVersion As Integer = Settings.Get(Of Integer)("LinkEasyTierVersion")
        ServerVersion = ServerConfig("Link")("EasyTierVersion")
        Dim RequiredFiles As New List(Of NetFile)
        Logger.Info($"EasyTier 本地版本：{LocalVersion}，需求版本：{ServerVersion}")
        If LocalVersion < ServerVersion Then
            '内核有 30 多 MB，源之间的速度差距很明显：先测速再决定用哪个，
            '否则可能一直卡在最慢的那个上（实测有用户只有 20 KB/s）
            Dim Candidates As String() = ServerConfig("Link")("Downloads").
                Select(Function(UrlEntry) UrlEntry.ToString.Replace("{arch}", Architecture)).ToArray()
            UpdateLoadingPage("正在测试下载源速度……", "测试下载源速度")
            Dim Ranked As String() = ModUpdateSource.RankSourcesBySpeed(Candidates, 512 * 1024, 6000)
            For Each Url In Ranked
                Logger.Info($"下载源排序：{Url}")
            Next
            RequiredFiles.Add(New NetFile(Ranked,
                PathEasyTier & "EasyTier.zip",
                New FileChecker With {.MinSize = 1024 * 1024 * 2}))
        End If
        '开始下载
        UpdateLoadingPage("正在下载联机模块……", "下载联机模块")
        Task.Output = RequiredFiles
    End Sub

    '3. 启动联机模块
    Private Sub InitLaunch(Task As LoaderTask(Of Integer, Integer))
        '解压文件
        UpdateLoadingPage("正在解压联机模块……", "解压联机模块")
        If FileUtils.Exists(PathEasyTier & "EasyTier.zip") Then
            '解压。这一步原本被注释掉了，导致下面的 First() 在空目录上抛异常
            Dim ExtractPath As String = RequestTaskTempFolder()
            DirectoryUtils.Create(ExtractPath)
            Dim ExtractProgress As New ProgressProvider()
            AddHandler ExtractProgress.ProgressChanged,
                Sub(Sender As (actual As Double, skiped As Double))
                    Task.Progress = Sender.actual * 0.05
                End Sub
            FileUtils.ExtractToDirectory(PathEasyTier & "EasyTier.zip", ExtractPath, p:=ExtractProgress)
            '压缩包内通常有一层同名目录，把里面的东西搬出来；
            '也可能直接摊在根目录，两种情况都要能处理
            Dim SubDirs As List(Of String) = DirectoryUtils.EnumerateDirectories(ExtractPath, False).ToList()
            If SubDirs.Count = 1 AndAlso DirectoryUtils.EnumerateFiles(ExtractPath, False).Count = 0 Then
                DirectoryUtils.Copy(SubDirs(0), PathEasyTier)
            Else
                DirectoryUtils.Copy(ExtractPath, PathEasyTier)
            End If
            '重命名
            If FileUtils.Exists(PathEasyTier & "easytier-core.exe") Then
                FileUtils.Move(PathEasyTier & "easytier-core.exe", PathEasyTier & "联机模块.exe")
            Else
                Throw New Exception("压缩包里没有 easytier-core.exe，下载到的可能不是 EasyTier 内核")
            End If
            If FileUtils.Exists(PathEasyTier & "easytier-cli.exe") Then
                FileUtils.Move(PathEasyTier & "easytier-cli.exe", PathEasyTier & "联机模块 CLI.exe")
            End If
            '清理
            FileUtils.Delete(PathEasyTier & "EasyTier.zip")
            Settings.Set("LinkEasyTierVersion", ServerVersion)
        End If
        Task.Progress = 0.07
        '获取节点列表
        UpdateLoadingPage("正在获取节点列表……", "获取节点列表")
        Dim RawPeers As List(Of String)
        Dim CustomPeers As String = Settings.Get(Of String)("LinkCustomPeer")
        '邀请码里带了房主实际使用的节点地址时，以它为准。
        '本机的「自定义节点」是用户自己填的，可能和房主填的不一致，
        '以本机设置为先会导致两边连到不同节点、互相看不见。
        '（服务端不走这条路：它自己就是要发地址的一方）
        Dim AddressFromCode As String = If(IsServerSide, "", DiscoverNodeAddress.Trim())
        If AddressFromCode <> "" Then
            If Not String.IsNullOrWhiteSpace(CustomPeers) Then
                Logger.Info($"邀请码里带了节点地址，忽略本机自定义节点设置：{CustomPeers}")
            End If
            RawPeers = GetTargetPeers()
        ElseIf String.IsNullOrWhiteSpace(CustomPeers) Then
            '没有地址可依，房主又是用自定义节点建的房，这时只能让用户自己填
            If DiscoverNodeID = -2 AndAlso Not IsServerSide Then
                Panic("未填写自定义节点设置", $"$你必须在 {vbLQ}自定义节点{vbRQ} 设置中填写与房主相同的内容，{vbCrLf}才能进入该房间！")
                Return
            End If
            RawPeers = GetTargetPeers()
        Else
            If DiscoverNodeID <> -2 AndAlso Not IsServerSide Then Hint("房主可能没有使用自定义节点设置，请确认你们的自定义节点设置是否一致！")
            RawPeers = CustomPeers.Split("，,".ToCharArray).Select(Function(p) p.Trim).Where(Function(p) Not String.IsNullOrEmpty(p)).ToList()
            Logger.Info($"使用自定义节点：{CustomPeers}")
            '自定义节点是用户自己填的，同样要随邀请码发出去：
            '否则房主用自定义节点建房时，邀请码里没有地址段，
            '成员只能按节点 ID 去公开清单里找，必然找不到。
            '取第一个作为发现节点——其余地址只用来直连，成员不需要知道。
            If IsServerSide AndAlso RawPeers.Count > 0 Then
                DiscoverNodeAddress = RawPeers(0)
                DiscoverNodeID = 0 '自定义节点不在公开清单里，没有 ID
                Logger.Info($"房主使用自定义节点，邀请码将带上地址：{DiscoverNodeAddress}")
            End If
        End If
        Task.Progress = 0.13
        '获取空闲端口
        UpdateLoadingPage("正在启动联机模块……", "启动联机模块")
        Dim FreePorts = FindFreePorts(5, ServerPort)
        ClientPort = FreePorts(0)
        RPCPort = FreePorts(1)
        Dim ListenersPort As Integer = FreePorts(2) '它会占用【当前、当前 +1、当前 +2】共三个端口
        '获取启动参数
        Dim Arguments As String = ServerConfig("Link")("Argument")
        Arguments += $" --network-name={NetworkName} --network-secret={NetworkSecret} --listeners {ListenersPort} --rpc-portal {RPCPort} --private-mode true"
        Dim HostName = If(IsServerSide, "Server-", "Client-") & Identify.GetStableHashCode().ConvertRadix(36).TrimStart("-"c).EnsureLength("0", 6)
        If IsServerSide Then
            Arguments += $" -i 10.114.114.114 --hostname={HostName} --tcp-whitelist={ServerPort} --udp-whitelist={ServerPort}"
        Else
            Arguments += $" -d --hostname={HostName} --tcp-whitelist=0 --udp-whitelist=0"
            '端口转发
            ClientAddress = $"localhost:{ClientPort}"
            Arguments += $" --port-forward tcp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}"
            Arguments += $" --port-forward udp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}"
            Arguments += $" --port-forward tcp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}"
            Arguments += $" --port-forward udp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}"
        End If
        For Each Peer As String In RawPeers
            Arguments += $" -p=""{Peer}"""
        Next
        If Settings.Get(Of Integer)("LinkLatencyMode") = 1 Then Arguments += " --latency-first"
        '启动进程
        ProcessStart(Arguments)
        Task.Progress = 0.15
        '等待连接
        Dim TimeoutCounter As Integer = 0
        Do
            Thread.Sleep(500) '为避免还没启动好先跑 CLI 了，先等半秒
            '刷新信息
            CheckCrash()
            RefreshPeerLoader.WaitForExit(IsForceRestart:=True)
            '查找目标节点
            Dim Ping = GetPeerPing()
            If Ping > 0 Then
                Logger.Info($"已与目标建立连接，当前 Ping 为 {Ping:0.0}ms")
                Telemetry("联机成功", "Server", IsServerSide, "NAT", NATType)
                Exit Do '退出循环
            End If
            '更新进度
            Dim LastProgress = Task.Progress
            Dim PeerCount As Integer = If(Peers Is Nothing, -1, Peers.Where(Function(p) p.Ping > 0).Count)
            Select Case PeerCount
                Case -1 'CLI 无返回
                    Task.Progress = (Task.Progress + 0.02).Clamp(0.15, 0.25)
                Case 0 'CLI 有返回，但未连接到任何节点
                    UpdateLoadingPage("正在连接到节点……", "连接节点")
                    Task.Progress = (Task.Progress + 0.02).Clamp(If(IsServerSide, 0.5, 0.3), If(IsServerSide, 0.95, 0.5))
                Case Else '已连接到节点，但未连接到房主
                    UpdateLoadingPage("正在连接到房主……", "连接房主")
                    Task.Progress = (Task.Progress + 0.02).Clamp(Math.Min(0.45 + PeerCount * 0.05, 0.65), 0.95)
            End Select
            '超时判定
            If LastProgress <> Task.Progress Then
                TimeoutCounter = 0
            ElseIf TimeoutCounter < 30 * 2 Then
                TimeoutCounter += 1
            Else '进度停滞超过 30s
                Select Case PeerCount
                    Case -1 'CLI 无返回
                        Panic("无法启动联机模块", "近期日志：" & vbCrLf & LogHistory.Join(vbCrLf))
                    Case 0 'CLI 有返回，但未连接到任何节点
                        Panic("无法连接到节点", $"请检查你的网络环境是否良好。")
                    Case Else '已连接到节点，但未连接到房主
                        Panic("无法连接到房主", $"可能的原因：{vbCrLf}- 你或者房主的网络环境不佳{vbCrLf}- 房主已关闭房间{vbCrLf}- 邀请码输错了")
                End Select
            End If
        Loop Until Task.IsCanceled
        If Task.IsCanceled Then Throw New OperationCanceledException
        '等待连接稳定，最多 5s
        If IsServerSide Then Return
        UpdateLoadingPage("连接优化中……", "优化连接")
        Task.Progress = 0.999
        For i = 1 To 50
            Dim Server = GetTargetPeer()
            If Server IsNot Nothing AndAlso Not Server.Relay AndAlso Server.Ping < 100 Then Return '结束
            If Task.IsCanceled Then Throw New OperationCanceledException
            Thread.Sleep(100)
        Next
    End Sub

    ''' <summary>
    ''' 选取要连接的节点。
    ''' <para>
    ''' 走 <see cref="ModLinkNodes"/>：云端清单 → 本地备份 → 内置兜底，三层都能给出地址。
    ''' </para>
    ''' <para>
    ''' 发现节点 ID 的语义不变：-1 表示房主自选，0 表示用回退，>0 表示按 ID 指定。
    ''' </para>
    ''' </summary>
    Private Function GetTargetPeers() As List(Of String)
        Dim FinalPeers As New List(Of String), FinalDiscoverID As Integer = -1
        Try
            Dim Nodes As List(Of ModLinkNodes.LinkNode) = ModLinkNodes.Fetch()
            If Nodes Is Nothing OrElse Nodes.Count = 0 Then Throw New Exception("没有可用的联机节点")

            '用户在节点卡片里指定了首选节点时，把它提到最前面；
            '其余节点仍保留，测速排序只影响备选顺序
            Dim Preferred As String = Settings.Get(Of String)("LinkPreferredPeer").Trim()
            If Preferred <> "" Then
                Dim Hit As ModLinkNodes.LinkNode = Nodes.FirstOrDefault(
                    Function(N) String.Equals(N.Address, Preferred, StringComparison.OrdinalIgnoreCase))
                If Hit IsNot Nothing Then
                    Nodes.Remove(Hit)
                    Nodes.Insert(0, Hit)
                    Logger.Info($"使用用户指定的节点：{Preferred}")
                Else
                    Logger.Warn($"指定的节点不在清单里，改回自动选择：{Preferred}")
                End If
            End If

            '先测一轮延迟，按快的在前排序；测不出来的（-1）排到最后
            UpdateLoadingPage("正在测试节点延迟……", "测试节点延迟")
            ModLinkNodes.ProbeLatency(Nodes)
            Nodes = Nodes.OrderBy(Function(N) If(N.Latency < 0, Integer.MaxValue, N.Latency)).ToList()
            For Each N In Nodes
                Logger.Info($"节点 {N.Address} 延迟 {If(N.Latency < 0, "未知", N.Latency & " ms")}")
            Next

            '选取发现节点
            Dim Selected As ModLinkNodes.LinkNode = Nothing
            Dim AddressFromCode As String = DiscoverNodeAddress.Trim()

            If AddressFromCode <> "" Then
                '成员：邀请码里直接带了房主用的地址，优先用它。
                '这样不依赖「节点 ID → 地址」的映射，避免双方清单版本不同导致连错节点。
                Selected = Nodes.FirstOrDefault(Function(N) String.Equals(N.Address, AddressFromCode, StringComparison.OrdinalIgnoreCase))
                If Selected Is Nothing Then
                    '清单里没有这个地址。是「清单拉到了但确实没有」，
                    '还是「清单压根没拉到」，风险不一样，要分开处理：
                    '前者说明房主用了自建节点，提醒一下；后者是我们自己的问题，
                    '不能因此把正常节点报成可疑节点。
                    Dim Known As ModLinkNodes.KnownResult = ModLinkNodes.CheckKnown(AddressFromCode)
                    Select Case Known
                        Case ModLinkNodes.KnownResult.Unknown
                            '地址能解析出来，只是不在公开清单里——多数是房主自建的节点。
                            '不由我们替用户决定，摆清楚风险让他自己选。
                            Logger.Warn($"邀请码中的节点不在公开清单里，交给用户确认：{AddressFromCode}")
                            Dim Choice As Integer = MyMsgBox(
                                "这个房间使用的联机节点不在公开节点清单中：" & vbCrLf &
                                AddressFromCode & vbCrLf & vbCrLf &
                                "它可能是房主自建的节点。连接未知节点意味着你的流量会经过它，" & vbCrLf &
                                "对方有可能看到你的连接信息。" & vbCrLf & vbCrLf &
                                "信任这个节点并继续连接吗？", "未知节点", "信任并连接", "取消", IsWarn:=True)
                            If Choice <> 1 Then
                                Logger.Info("用户取消了连接未知节点")
                                Throw New OperationCanceledException
                            End If
                        Case ModLinkNodes.KnownResult.Unavailable
                            '清单没拉到，无从判断。不拦，但留个记录便于排查。
                            Logger.Warn($"节点清单不可用，无法校验邀请码中的地址是否公开：{AddressFromCode}")
                    End Select
                    '按地址直连
                    Selected = New ModLinkNodes.LinkNode With {
                        .Id = DiscoverNodeID,
                        .Address = AddressFromCode,
                        .AllowRelay = False}
                End If
            ElseIf DiscoverNodeID = -1 Then
                '房主：优先挑不允许中继的（这类节点更稳），没有就按延迟取第一个
                Selected = Nodes.FirstOrDefault(Function(N) Not N.AllowRelay)
            ElseIf DiscoverNodeID > 0 Then
                '老版本邀请码：只能按 ID 找
                Selected = Nodes.FirstOrDefault(Function(N) N.Id = DiscoverNodeID)
                If Selected Is Nothing Then
                    Logger.Warn($"未找到 ID {DiscoverNodeID} 的发现节点，改用延迟最低的")
                    Selected = Nodes.FirstOrDefault()
                End If
            End If
            If Selected Is Nothing Then Selected = Nodes(0)
            FinalPeers.Add(Selected.Address)
            FinalDiscoverID = Selected.Id
            DiscoverNodeAddress = Selected.Address
            Logger.Info($"发现节点：{Selected.Address} (ID: {FinalDiscoverID})")

            '房主只连发现节点，不连中继
            If ModeDebug OrElse Not IsServerSide Then
                Dim RelayCount As Integer = 2
                Dim RelayNodes = Nodes.Where(Function(N) N.AllowRelay AndAlso N IsNot Selected).ToList()
                FinalPeers.AddRange(RelayNodes.Take(RelayCount).Select(Function(N) N.Address))
            End If
        Catch ex As Exception
            ex.ThrowIfCanceled()
            Logger.Error(ex, "获取联机节点失败", LogBehavior.Toast)
        End Try

        '一个都没取到就直接把错误抛给上层，让用户看到明确的失败原因
        If FinalPeers.Count = 0 Then
            Throw New Exception("联机节点全部不可用，请稍后重试")
        End If
        DiscoverNodeID = FinalDiscoverID
        Return FinalPeers.Distinct.ToList
    End Function

#End Region

#Region "进程与日志"

    ''' <summary>
    ''' EasyTier Core 进程。
    ''' </summary>
    Private Shared ProcessCore As Process
    Private Shared ProcessOutputHandle As AutoResetEvent, ProcessErrorHandle As AutoResetEvent

    ''' <summary>
    ''' 若程序正在运行，则结束程序进程。
    ''' 即使失败也不会抛出异常。
    ''' </summary>
    Public Shared Sub ProcessStop()
        Try
            '关闭未捕获的进程
            For Each ProcessObject In Process.GetProcesses
                If ProcessObject.Id = ProcessCore?.Id Then Continue For
                If ProcessObject.ProcessName <> "联机模块" AndAlso ProcessObject.ProcessName <> "联机模块 CLI" Then Continue For
                Try
                    Logger.Info($"停止残留的联机模块，PID：{ProcessObject.Id}，进程名：{ProcessObject.ProcessName}")
                    ProcessObject.Kill()
                    ProcessObject.Close()
                Catch exx As Exception
                    Logger.Warn(exx, $"结束进程失败（{ProcessObject.ProcessName}，PID {ProcessObject.Id}）")
                End Try
            Next
            '关闭由自身启动的进程
            If ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited Then
                Logger.Info($"停止所启动的联机模块，PID：{ProcessCore.Id}")
                ProcessCore.Kill()
                ProcessCore.Close()
                ProcessCore = Nothing
            End If
            '释放资源
            ProcessOutputHandle?.Dispose()
            ProcessErrorHandle?.Dispose()
        Catch ex As Exception
            Logger.Warn(ex, "停止联机模块失败")
        End Try
    End Sub
    ''' <summary>
    ''' 出现意外错误，给出错误信息并结束联机。
    ''' </summary>
    Private Sub Panic(Brief As String, Detail As String)
        '常见原因分析
        If Detail.Contains("failed to listen on ") Then
            Detail = $"监听端口失败。{vbCrLf}请点击重试，如果还是出现此错误，可以重启电脑解决。{vbCrLf}{vbCrLf}{Detail}"
        End If
        '显示信息
        If LinkState = LinkStates.Loading Then
            FailReason = Brief
            LinkLoader.Failed(New Exception(Detail))
        Else
            MyMsgBox(Detail, "联机出错：" & Brief, IsWarn:=True)
            ChangeState(LinkStates.Waiting)
            Telemetry("联机失败", "Exception", FilterUserName(Brief & "：" & Detail, "*"))
        End If
    End Sub

    ''' <summary>
    ''' 清掉上次异常退出留下的 TUN 网卡。
    ''' <para>
    ''' EasyTier 在 Windows 上被强杀时不会删网卡，下次启动复用同名网卡，
    ''' 再添加同一个 IPv4 就会报「对象已存在 (5010)」，整个联机直接起不来。
    ''' </para>
    ''' <para>
    ''' 必须用 pnputil 删设备：Remove-NetAdapter 在部分系统上不存在
    ''' （Windows 自带的 NetAdapter 模块只有 Get/Set/Disable 那一批）。
    ''' 网卡是 Wintun 虚拟设备，按 PnP 实例 ID 删才删得掉。
    ''' </para>
    ''' </summary>
    Private Sub CleanupLeftoverTun()
        Try
            '先杀掉还活着的联机模块，否则它会立刻把网卡重新建出来
            Try
                For Each Proc In Process.GetProcessesByName("联机模块")
                    Try
                        Logger.Info($"结束残留的联机模块进程：{Proc.Id}")
                        Proc.Kill()
                        Proc.WaitForExit(3000)
                    Catch ex As Exception
                        Logger.Warn(ex, $"结束联机模块进程 {Proc.Id} 失败")
                    End Try
                Next
            Catch ex As Exception
                Logger.Warn(ex, "枚举联机模块进程失败")
            End Try
            '按 PnP 实例 ID 删掉残留的 Wintun 设备
            Dim Names As List(Of String) = RunPowerShell(
                "Get-NetAdapter -ErrorAction SilentlyContinue | " &
                "Where-Object { $_.Name -like 'et_*' } | " &
                "Select-Object -ExpandProperty PnPDeviceID")
            For Each Raw As String In Names
                Dim Id As String = Raw.Trim()
                If Id = "" OrElse Not Id.StartsWith("SWD\Wintun", StringComparison.OrdinalIgnoreCase) Then Continue For
                Logger.Info($"清理残留联机网卡：{Id}")
                RunPowerShell($"pnputil /remove-device '{Id}'")
            Next
        Catch ex As Exception
            Logger.Warn(ex, "清理残留联机网卡失败")
        End Try
    End Sub

    ''' <summary>跑一段 PowerShell 并取回标准输出。</summary>
    Private Shared Function RunPowerShell(Script As String) As List(Of String)
        Dim Result As New List(Of String)
        Try
            Dim Escaped As String = Script.Replace("""", "\""")
            Dim Info As New ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" & Escaped & "\"") With {
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .CreateNoWindow = True,
                .StandardOutputEncoding = Encoding.UTF8}
            Using Proc As Process = Process.Start(Info)
                If Proc Is Nothing Then Return Result
                Dim Out As String = Proc.StandardOutput.ReadToEnd()
                Proc.WaitForExit(8000)
                For Each L In Out.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Result.Add(L)
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "调用 PowerShell 失败")
        End Try
        Return Result
    End Function
    Private Sub ProcessStart(Arguments As String)
        '先清残留网卡，否则可能因 IP 冲突起不来
        CleanupLeftoverTun()
        Dim Info = New ProcessStartInfo With {
            .FileName = PathEasyTier & "联机模块.exe",
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardError = True,
            .RedirectStandardOutput = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Logger.Info($"正在启动 EasyTier：{Arguments}")
        ProcessCore = New Process With {.StartInfo = Info}
        Dim LogLineHandler =
        Sub(sender As Object, e As DataReceivedEventArgs, Handle As AutoResetEvent)
            Try
                If e.Data Is Nothing Then
                    Handle.[Set]()
                    Return
                End If
                ProcessLogLine(e.Data)
            Catch unused As ObjectDisposedException
            Catch ex As Exception
                Logger.Warn(ex, "读取 EasyTier 信息失败")
            End Try
        End Sub
        ProcessOutputHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.OutputDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessOutputHandle)
        ProcessErrorHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.ErrorDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessErrorHandle)
        ProcessCore.Start()
        ProcessCore.BeginOutputReadLine()
        ProcessCore.BeginErrorReadLine()
    End Sub
    Private Sub ProcessLogLine(Line As String)
        '记录日志
        Logger.Info(Line)
        LogHistory.Enqueue(Line)
        If LogHistory.Count >= 10 Then LogHistory.Dequeue()
        '检查日志内容
        If Line.ContainsIgnoreCase("new peer connection added") Then
            Logger.Warn($"已建立连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        ElseIf Line.ContainsIgnoreCase("peer connection removed") Then
            Logger.Warn($"已断开连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        End If
    End Sub
    Private LogHistory As New Queue(Of String)(10)

#End Region

#Region "CLI 节点信息获取"

    Private Class Peer
        ''' <summary>
        ''' 该节点的类型。
        ''' </summary>
        Public ReadOnly Type As Types
        Public Enum Types
            ''' <summary>
            ''' 客户端。
            ''' </summary>
            Client
            ''' <summary>
            ''' 服务器。
            ''' </summary>
            Server
            ''' <summary>
            ''' 公共节点。
            ''' </summary>
            Misc
            ''' <summary>
            ''' 自己，或者自己的重复（它可能返回多个自己）。
            ''' </summary>
            Self
        End Enum
        ''' <summary>
        ''' 与该节点的延迟 (ms)。
        ''' 若是自己则为 0。
        ''' </summary>
        Public ReadOnly Ping As Double
        ''' <summary>
        ''' 名称。
        ''' </summary>
        Public ReadOnly Name As String
        ''' <summary>
        ''' 是否通过中继连接。
        ''' </summary>
        Public ReadOnly Relay As Boolean
        ''' <summary>
        ''' NAT 类型。
        ''' </summary>
        Public ReadOnly NATType As NATTypes

        ''' <summary>
        ''' 从 CLI 给出的信息分析对应的数据。
        ''' </summary>
        Public Sub New(Info As JObject)
            '类别
            Dim PeerName = Info("hostname").ToString
            If Info("cost").ToString = "Local" Then
                Type = Types.Self
            ElseIf PeerName.StartsWithF("Client") Then
                Type = Types.Client
            ElseIf Info("ipv4") = "10.114.114.114" Then '只允许这个 IP 作为房主，避免网络被多个房主搞炸的情况
                Type = Types.Server
            Else
                Type = Types.Misc
            End If
            '基础信息
            Double.TryParse(Info("lat_ms"), NumberStyles.Any, CultureInfo.InvariantCulture, Ping)
            Name = PeerName
            Relay = Info("cost").ToString.ContainsIgnoreCase("relay")
            NATType = EnumUtils.FromString(Of NATTypes)(Info("nat_type"))
        End Sub
        Public Overrides Function ToString() As String
            Return $"{Type} - {Name} - Ping {Ping:0.0}ms [中继? {Relay}] - NAT {NATType}"
        End Function
    End Class
    Private Enum NATTypes
        'https://github.com/EasyTier/EasyTier/blob/6bb2fd9a15ab2499bdeabdcc3a925e9bd9aebf50/easytier/src/proto/common.proto#L129
        Unknown = 0
        OpenInternet = 1
        NoPAT = 2
        FullCone = 3
        Restricted = 4
        PortRestricted = 5
        Symmetric = 6
        SymUdpFirewall = 7
        SymmetricEasyInc = 8
        SymmetricEasyDec = 9
        ''' <summary>
        ''' 尚未获取。
        ''' </summary>
        Pending = 10
    End Enum

    ''' <summary>
    ''' 当前的节点列表，使用 RefreshPeerLoader 来刷新。
    ''' 若尚未成功获取过则为 Nothing，但保证在加载完成后至少是一个列表。
    ''' </summary>
    Private Peers As List(Of Peer) = Nothing
    ''' <summary>
    ''' 自己的 NAT 类型。
    ''' </summary>
    Private NATType As NATTypes = NATTypes.Pending

    ''' <summary>
    ''' 调用 EasyTier CLI 获取已连接节点信息。
    ''' 这会过滤掉尚未完成连接的其他节点以及自己。
    ''' 若在加载完成后连续获取失败 3 次，则会强制断开连接。
    ''' </summary>
    Private RefreshPeerLoader As New LoaderTask(Of Integer, List(Of Peer))("EasyTier CLI", AddressOf RefreshPeer)
    ''' <summary>
    ''' 调 EasyTier CLI 并取回标准输出。超时就杀掉进程返回空串，
    ''' 让上层按「CLI 调用失败」处理，不至于卡住整个联机流程。
    ''' </summary>
    Private Function RunCli(Arguments As String, TimeoutMs As Integer) As String
        Try
            Dim Info As New ProcessStartInfo(PathEasyTier & "联机模块 CLI.exe", Arguments) With {
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True,
                .StandardOutputEncoding = Encoding.UTF8
            }
            Using Proc As Process = Process.Start(Info)
                If Proc Is Nothing Then Return ""
                Dim Out As String = Proc.StandardOutput.ReadToEnd()
                If Not Proc.WaitForExit(TimeoutMs) Then
                    Try
                        Proc.Kill()
                    Catch ex As Exception
                        Logger.Warn(ex, "结束 EasyTier CLI 进程失败")
                    End Try
                    Return ""
                End If
                Return Out
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "调用 EasyTier CLI 失败")
            Return ""
        End Try
    End Function

    Private Sub RefreshPeer()
        Try
            Dim CliResult As String = RunCli($"-o json -p 127.0.0.1:{RPCPort} peer", 2000)
            '解析
            If Not CliResult.Contains("lat_ms") Then Throw New Exception("CLI 调用失败：" & vbCrLf & CliResult)
            If GetUuid() Mod If(ModeDebug, 23, 103) = 0 Then Logger.Info($"CLI 输出抽样：{vbCrLf}{CliResult}")
            Dim NewPeers As New List(Of Peer)
            For Each Line As JObject In CType(CliResult.DeserializeJson(), JArray)
                Try
                    Dim Peer = New Peer(Line)
                    If Peer.Type = Peer.Types.Self Then
                        NATType = Peer.NATType '记录自己的 NAT
                    Else
                        NewPeers.Add(Peer)
                    End If
                Catch exx As Exception
                    Logger.Warn(exx, $"错误的信息（{Line}）")
                End Try
            Next
            '完成
            Peers = NewPeers
            FailCount = 0
        Catch ex As Exception
            Logger.Warn(ex, "获取节点信息失败")
            If LinkState = LinkStates.Finished Then
                FailCount += 1
                If FailCount >= 4 Then Panic("获取节点信息失败", ex.Message)
            End If
        End Try
    End Sub
    ''' <summary>
    ''' CLI 调用连续失败的次数。
    ''' </summary>
    Private FailCount As Integer = 0

    ''' <summary>
    ''' 根据当前的节点列表获取 Ping 值。
    ''' 服务端会返回所有节点中最低的 Ping，客户端会返回服务端的 Ping。
    ''' 0 代表尚不可用。
    ''' </summary>
    Private Function GetPeerPing() As Double
        Dim Peer = GetTargetPeer()
        If Peer Is Nothing Then Return 0
        Return Peer.Ping
    End Function
    ''' <summary>
    ''' 服务端会返回所有节点中 Ping 大于 0 最低的那一个，客户端会返回服务端。
    ''' 若没有则为 Nothing。
    ''' </summary>
    Private Function GetTargetPeer() As Peer
        If Peers Is Nothing Then Return Nothing
        Dim Targets As IEnumerable(Of Peer)
        If IsServerSide Then
            Targets = Peers.Where(Function(p) p.Ping > 0)
        Else
            Targets = Peers.Where(Function(p) p.Type = Peer.Types.Server AndAlso p.Ping > 0)
        End If
        If Not Targets.Any Then Return Nothing
        '返回 Ping 且最低的那个
        Dim MinPing = Targets.Min(Function(p) p.Ping)
        Return Targets.First(Function(p) p.Ping = MinPing)
    End Function

    ''' <summary>
    ''' 获取当前房间中的人数，包括自己。
    ''' 0 代表不可用。
    ''' </summary>
    Private ReadOnly Property PeopleCount As Integer
        Get
            If Peers Is Nothing Then Return 0
            Return Peers.Where(Function(p) p.Type <> Peer.Types.Misc).Count() + 1 '加上自己
        End Get
    End Property

#End Region

#Region "定时任务"

    '启动
    Private Sub StartTimerThread() Handles Me.Loaded
        Static IsTimerStarted As Boolean = False
        If IsTimerStarted Then Return
        RunInNewThread(
        Sub()
            Dim Counter As Integer = 0
            Do While True
                Try
                    Thread.Sleep(200)
                    Counter += 200
                    '每秒执行一次
                    If Counter >= 1000 Then
                        Counter = 0
                        Update()
                    End If
                    '每 200ms 更新进度条
                    If LinkState = LinkStates.Loading Then RunInUi(AddressOf UpdateProgressBar)
                Catch ex As Exception
                    Logger.Error(ex, "联机模块主时钟出错")
                    Thread.Sleep(10000)
                End Try
            Loop
        End Sub, "Link Timer")
    End Sub

    '每秒或进入页面时触发
    Private Sub Update() Handles Me.Loaded
        If LinkState <> LinkStates.Finished Then Return
        '重新获取信息
        SyncLock RefreshPeerLoader.LockState
            If RefreshPeerLoader.State <> LoadState.Loading Then RefreshPeerLoader.Start(IsForceRestart:=True)
        End SyncLock
        '更新 UI
        If FrmMain.PageCurrent = FormMain.PageType.Link Then
            RunInUi(
            Sub()
                'Logo 旋转动画
                AniStart(AaRotateTransform(ImgFinishLogo, 500, 5000), "Link Logo Rotation")
                'Ping
                Dim Ping As Double = GetPeerPing()
                Dim Connecting As Boolean = Ping Mod 500 = 0 OrElse $"{Ping:0.0}" = "1.0" OrElse FailCount > 0
                If Connecting Then
                    LabFinishPing.Text = "连接优化中"
                Else
                    LabFinishPing.Text = If(Ping >= 10, $"{Ping:0} ms", $"{Ping:0.0} ms")
                End If
                '人数显示
                LabFinishPlayer.Text = PeopleCount & "  人"
                '------------------------
                '  提示条
                '------------------------
                HintFinish.Visibility = Visibility.Collapsed
                If IsServerSide Then Return '服务端没有需要显示的提示
                If Connecting Then Return '连接稳定过程中不显示提示
                Dim Server As Peer = GetTargetPeer()
                If Server Is Nothing Then Return
                '中继提示不再显示：现在没有可直连的公网节点，所有连接都要经社区节点中转，
                '这条提示会一直挂着，等于常驻噪声。中继本身在延迟栏里看得见。
                '环境比房主更好的提示
                If NATType >= NATTypes.PortRestricted Then Return '自身的 NAT 为 2 或更好
                If Server.NATType < NATTypes.PortRestricted Then Return '房主的 NAT 为 3 或更差
                Dim OtherPlayers = Peers.Where(Function(p) p.Type = Peer.Types.Client).ToList()
                If Not OtherPlayers.Any() Then Return '房间里还有其他玩家
                If OtherPlayers.Any(Function(p) p.Relay) Then Return '自己可以与所有玩家打洞
                If OtherPlayers.All(Function(p) p.NATType < NATTypes.PortRestricted) Then Return '任意其他玩家的 NAT 为 3 或更差
                HintFinish.Visibility = Visibility.Visible
                HintFinish.Theme = MyHint.Themes.Blue
                HintFinish.Text = "你的网络环境比房主更好！如果你来当房主，其他玩家或许能更加流畅！"
            End Sub)
        End If
        '检查核心状态
        CheckCrash()
        '检查节点状态
        If Not Peers.Any Then
            Panic("网络连接已断开", "请检查你的网络环境是否正常。")
            Return
        End If
        If Not IsServerSide AndAlso Not Peers.Any(Function(p) p.Type = Peer.Types.Server) Then
            MyMsgBox("房主已离开房间！", "联机结束")
            ChangeState(LinkStates.Waiting)
            Return
        End If
        '广播联机房间端口
        If Not IsServerSide Then
            Try
                Static BroadcastSocket As New Socket(SocketType.Dgram, ProtocolType.Udp)
                BroadcastSocket.SendTo(
                    Encoding.UTF8.GetBytes($"[MOTD]PCL 联机房间[/MOTD][AD]{ClientPort}[/AD]"),
                    SocketFlags.None,
                    New IPEndPoint(IPAddress.Loopback, 4445))
            Catch ex As Exception
                Logger.Warn(ex, "广播联机房间端口失败")
            End Try
        End If
    End Sub
    ''' <summary>
    ''' 检查联机模块是否崩溃。
    ''' </summary>
    Private Sub CheckCrash()
        If ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited Then Return
        Panic("联机模块已崩溃", "近期日志：" & vbCrLf & LogHistory.Join(vbCrLf))
    End Sub

#End Region

End Class
