Imports System.IO
Imports System.Security.Cryptography
Imports System.Threading
Imports Newtonsoft.Json.Linq

''' <summary>
''' DST-Mode（帝释天签发服务器）的 QQ 登录。
''' <para>
''' 流程：bind/start 拿 8 位绑定码 → 玩家在 QQ 里发 /dst 绑定 &lt;码&gt; &lt;游戏名&gt;
''' → 每 1.5 秒 poll 一次 → 拿到 accessToken。
''' </para>
''' <para>
''' 启动游戏走 **Agent 形态**（docs/launcher-agent-guide.md）：下载 dstmode-agent jar，
''' 用 -javaagent:"路径"=authBase 挂上去。Agent 只改未混淆的 authlib，
''' 所以任何版本、任何加载器都不需要判断，也不用往 mods 里放模组。
''' </para>
''' <para>
''' 凭据缓存在注册表（Encrypted:=True），有效期内重启启动器不用重新验证。
''' </para>
''' </summary>
Public Class ModDstMode

    ''' <summary>config.ini 里指定 authBase 的键。</summary>
    Public Const ConfigKeyAuthBase As String = "DstModeAuthBase"
    ''' <summary>config.ini 里指定稳定清单地址的键。</summary>
    Public Const ConfigKeyManifest As String = "DstModeManifest"
    ''' <summary>凭据缓存的设置键（注册表 + 加密，不落明文文件）。</summary>
    Public Const SettingKeyCredential As String = "DstModeCredential"

    ''' <summary>
    ''' 内置默认清单地址。留空表示必须由 config.ini 提供：
    ''' 各运营方部署的地址不同，写死一个反而会把玩家带到错误的服务上。
    ''' </summary>
    Public Const DefaultManifest As String = ""

    ''' <summary>清单缓存时长（毫秒）。运营方迁移时最多延迟这么久生效。</summary>
    Private Const AuthBaseCacheMs As Long = 10 * 60 * 1000

    ''' <summary>一份登录凭据。只在内存里存在。</summary>
    Public Class DstModeCredential
        Public AccessToken As String = ""
        ''' <summary>36 位带横线的 UUID；传给游戏时要转成 32 位。</summary>
        Public Uuid As String = ""
        Public Name As String = ""
        Public AuthBase As String = ""
        ''' <summary>过期时间（Unix 秒）。</summary>
        Public ExpiresAt As Long = 0

        ''' <summary>游戏参数需要的 32 位小写 UUID。</summary>
        Public ReadOnly Property Uuid32 As String
            Get
                Return DstModeCore.Uuid32(Uuid)
            End Get
        End Property

        Public ReadOnly Property ExpiresText As String
            Get
                If ExpiresAt <= 0 Then Return "未知"
                Return DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            End Get
        End Property

        ''' <summary>还能不能拿去启动游戏。留 60 秒余量，避免刚点启动就过期。</summary>
        Public ReadOnly Property IsUsable As Boolean
            Get
                Return AccessToken <> "" AndAlso Uuid32 <> "" AndAlso Name <> "" AndAlso
                       ExpiresAt > 0 AndAlso ExpiresAt > NowUnix() + 60
            End Get
        End Property
    End Class

    ''' <summary>绑定流程的状态。</summary>
    Public Enum BindState
        Idle = 0
        Starting = 1
        Waiting = 2
        Succeeded = 3
        Failed = 4
        Expired = 5
        NotConfigured = 6
    End Enum

    Private Shared _current As DstModeCredential = Nothing
    ''' <summary>
    ''' 当前登录凭据；未登录时为 Nothing。
    ''' 读取时顺手触发一次「上次凭据」的恢复，这样任何入口拿到的都是同一个值，
    ''' 不会出现「这个页面说有登录、那个页面说没有」。
    ''' </summary>
    Public Shared Property Current As DstModeCredential
        Get
            EnsureSavedLoaded()
            Return _current
        End Get
        Set(value As DstModeCredential)
            _current = value
        End Set
    End Property

    Public Shared State As BindState = BindState.Idle
    Public Shared StateText As String = ""
    ''' <summary>当前绑定码；不在等待中时为空。</summary>
    Public Shared BindCode As String = ""
    ''' <summary>绑定码过期时刻（毫秒时间戳）。</summary>
    Public Shared BindExpiresAt As Long = 0
    ''' <summary>服务端给的中文提示。</summary>
    Public Shared BindInstruction As String = ""
    ''' <summary>本次绑定要用的游戏名；命令里会带上它。</summary>
    Public Shared BindName As String = ""

    Private Shared BindNotify As Action = Nothing
    Private Shared BindGeneration As Integer = 0
    Private Shared BindRunning As Boolean = False
    Private Shared ReadOnly SyncRoot As New Object

    Private Shared CachedAuthBase As String = ""
    Private Shared CachedAuthBaseAt As Long = 0
    ''' <summary>是否已经尝试过从设置里恢复凭据。</summary>
    Private Shared SavedLoaded As Boolean = False

#Region "状态"

    Public Shared ReadOnly Property HasUsableCredential As Boolean
        Get
            EnsureSavedLoaded()
            Dim Cred As DstModeCredential = Current
            Return Cred IsNot Nothing AndAlso Cred.IsUsable
        End Get
    End Property

    ''' <summary>
    ''' 启动器重启后把上次的凭据捡回来。
    ''' <para>
    ''' 接入文档建议不要持久化，但每次重启都要玩家去 QQ 里重新发一遍命令实在没法用，
    ''' 所以这里按正版登录的做法缓存到注册表（Encrypted:=True），并且只在令牌有效期内认它；
    ''' 过期、解绑、退出登录都会立刻清掉。
    ''' </para>
    ''' </summary>
    Private Shared Sub EnsureSavedLoaded()
        If SavedLoaded Then Return
        '先标记再读，避免 Get 过程中再次触发本方法造成递归
        SavedLoaded = True
        Try
            Dim Raw As String = Settings.Get(Of String)(SettingKeyCredential)
            If String.IsNullOrWhiteSpace(Raw) Then Return
            Dim Obj As JObject = JObject.Parse(Raw)
            Dim ExpiresAt As Long = 0
            Long.TryParse(If(Obj("expiresAt"), "").ToString(), ExpiresAt)
            If ExpiresAt <= NowUnix() + 60 Then
                Settings.Set(SettingKeyCredential, "")
                Return
            End If
            Dim Cred As New DstModeCredential With {
                .AccessToken = If(Obj("token"), "").ToString(),
                .Uuid = If(Obj("uuid"), "").ToString(),
                .Name = If(Obj("name"), "").ToString(),
                .AuthBase = If(Obj("authBase"), "").ToString(),
                .ExpiresAt = ExpiresAt
            }
            If Cred.IsUsable Then
                Current = Cred
                Logger.Info("已恢复上次的 QQ 登录凭据：" & Cred.Name)
            Else
                Settings.Set(SettingKeyCredential, "")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "恢复 QQ 登录凭据失败，已丢弃")
            Try
                Settings.Set(SettingKeyCredential, "")
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>把凭据写进设置（注册表加密存储）。</summary>
    Private Shared Sub SaveCredential(Cred As DstModeCredential)
        If Cred Is Nothing Then Return
        Try
            Dim Obj As New JObject From {
                {"token", Cred.AccessToken},
                {"uuid", Cred.Uuid},
                {"name", Cred.Name},
                {"authBase", Cred.AuthBase},
                {"expiresAt", Cred.ExpiresAt}
            }
            Settings.Set(SettingKeyCredential, Obj.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            Logger.Warn(ex, "保存 QQ 登录凭据失败")
        End Try
    End Sub

    ''' <summary>还剩多少时间可以完成绑定，给界面直接显示。</summary>
    Public Shared ReadOnly Property CountdownText As String
        Get
            If State <> BindState.Waiting OrElse BindExpiresAt <= 0 Then Return ""
            Dim LeftMs As Long = BindExpiresAt - GetTimeMs()
            If LeftMs <= 0 Then Return "绑定码已过期"
            Dim LeftSec As Integer = CInt(Math.Floor(LeftMs / 1000.0))
            Return "剩余 " & CInt(Math.Floor(LeftSec / 60.0)) & " 分 " & (LeftSec Mod 60).ToString("00") & " 秒"
        End Get
    End Property

    ''' <summary>当前可用的 authBase（不走网络）。</summary>
    Public Shared ReadOnly Property ConfiguredAuthBase As String
        Get
            Dim Explicit As String = DstModeCore.NormalizeBase(ModConfigFile.Read(ModConfigFile.SectionModules, ConfigKeyAuthBase))
            If DstModeCore.IsValidBase(Explicit) Then Return Explicit
            If DstModeCore.IsValidBase(CachedAuthBase) Then Return CachedAuthBase
            Return BotServiceBase()
        End Get
    End Property

    ''' <summary>
    ''' 签发服务的 authBase：预设服务地址 + /dstmode。
    ''' <para>
    ''' 签发服务与预设服务是同一台服务器，但挂在 /dstmode 前缀下。
    ''' 这一段完全绕开机器人 /api 控制面的鉴权，不需要任何令牌——
    ''' 也正因为如此，绝不能让启动器去拿 WebUI 令牌（那是控制面令牌）。
    ''' </para>
    ''' </summary>
    Public Shared Function BotServiceBase() As String
        Try
            Dim Base As String = DstModeCore.NormalizeBase(DstBot.ApiBase)
            If Not DstModeCore.IsValidBase(Base) Then Return ""
            If Base.EndsWith("/dstmode", StringComparison.OrdinalIgnoreCase) Then Return Base
            Return Base & "/dstmode"
        Catch ex As Exception
            Logger.Warn(ex, "读取机器人服务地址失败")
        End Try
        Return ""
    End Function

    ''' <summary>稳定清单地址：优先 config.ini，其次内置默认值。</summary>
    Public Shared ReadOnly Property ManifestUrl As String
        Get
            Dim Custom As String = ModConfigFile.Read(ModConfigFile.SectionModules, ConfigKeyManifest).Trim()
            If Custom <> "" Then Return Custom
            Return DefaultManifest
        End Get
    End Property

#End Region

#Region "服务地址"

    ''' <summary>
    ''' 解析当前应该使用的 authBase。
    ''' 顺序：config.ini 指定 → 缓存 → 稳定清单 → 上次成功的地址。
    ''' 全部失败返回空串，由调用方提示「服务不可用」，绝不在这里抛异常卡住启动。
    ''' </summary>
    Public Shared Function ResolveAuthBase(Optional Force As Boolean = False) As String
        Dim Explicit As String = DstModeCore.NormalizeBase(ModConfigFile.Read(ModConfigFile.SectionModules, ConfigKeyAuthBase))
        If DstModeCore.IsValidBase(Explicit) Then Return Explicit
        If Not Force AndAlso DstModeCore.IsValidBase(CachedAuthBase) AndAlso GetTimeMs() - CachedAuthBaseAt < AuthBaseCacheMs Then
            Return CachedAuthBase
        End If
        '运营方填了稳定清单就听它的（换服务器时只改清单）
        Dim Url As String = ManifestUrl
        If Url <> "" Then
            Try
                Dim Json As String = NetRequestByClient(Url, HttpMethod.Get, Accept:="application/json",
                                                        Timeout:=10000, RequireJson:=True, MakeLog:=False)
                Dim Base As String = DstModeCore.NormalizeBase(DstModeCore.JsonString(Json, "authBase"))
                If DstModeCore.IsValidBase(Base) Then
                    CachedAuthBase = Base
                    CachedAuthBaseAt = GetTimeMs()
                    Logger.Info("DST-Mode 清单解析成功：" & Base)
                    Return Base
                End If
                Logger.Warn("DST-Mode 清单里没有可用的 authBase")
            Catch ex As Exception
                Logger.Warn(ex, "获取 DST-Mode 清单失败")
            End Try
        End If
        '验证服务与预设服务是同一台机器，直接用机器人服务地址
        Dim BotBase As String = BotServiceBase()
        If BotBase <> "" Then Return BotBase
        If DstModeCore.IsValidBase(CachedAuthBase) Then Return CachedAuthBase
        Return ""
    End Function

#End Region

#Region "绑定"

    ''' <summary>
    ''' 开启一次绑定。整个过程在后台线程里跑，状态变化通过 Notify 回到 UI 线程。
    ''' 同一时间只允许一个绑定流程（多窗口会互相抢 ok）。
    ''' </summary>
    ''' <param name="Name">玩家要用的游戏名，会拼进绑定命令里。</param>
    Public Shared Sub StartBind(Name As String, Optional Notify As Action = Nothing)
        SyncLock SyncRoot
            If BindRunning Then Return
            BindRunning = True
            BindGeneration += 1
        End SyncLock
        Dim Gen As Integer = BindGeneration
        BindNotify = Notify
        BindName = If(Name, "").Trim()
        BindCode = ""
        BindInstruction = ""
        BindExpiresAt = 0
        SetState(BindState.Starting, "正在连接登录服务……")
        RunInNewThread(
        Sub()
            Try
                BindLoop(Gen)
            Catch ex As Exception
                If ex.IsCanceled Then Return
                Logger.Warn(ex, "QQ 登录失败")
                SetState(BindState.Failed, DstModeCore.FriendlyError(StatusCodeOf(ex), BodyOf(ex)))
            Finally
                SyncLock SyncRoot
                    If Gen = BindGeneration Then BindRunning = False
                End SyncLock
            End Try
        End Sub, "DstMode Bind")
    End Sub

    ''' <summary>取消正在进行的绑定。</summary>
    Public Shared Sub CancelBind()
        SyncLock SyncRoot
            BindGeneration += 1
            BindRunning = False
        End SyncLock
        BindCode = ""
        BindExpiresAt = 0
        BindName = ""
        SetState(BindState.Idle, "已取消登录")
    End Sub

    ''' <summary>退出登录：内存与设置里的凭据一起清掉。</summary>
    Public Shared Sub SignOut()
        CancelBind()
        Current = Nothing
        SavedLoaded = True
        Try
            Settings.Set(SettingKeyCredential, "")
        Catch ex As Exception
            Logger.Warn(ex, "清除 QQ 登录凭据失败")
        End Try
        BindNotify = Nothing
        SetState(BindState.Idle, "")
    End Sub

    Private Shared Sub BindLoop(Gen As Integer)
        Dim Base As String = ResolveAuthBase()
        If Gen <> BindGeneration Then Return
        If Base = "" Then
            SetState(BindState.NotConfigured,
                     "找不到登录服务：请先在「设置 → 机器人」里配置服务地址，或在 config.ini 里填 DstModeAuthBase。")
            Return
        End If
        '1. 开绑定会话
        '把实际请求的地址写进日志：服务地址是跟预设走的，出问题时要能一眼看到打到哪儿了
        Logger.Info("QQ 登录：签发服务地址 " & Base)
        Dim Json As String
        Try
            Json = NetRequestByClient(Base & "/api/v1/launcher/bind/start", HttpMethod.Post,
                                      Accept:="application/json", Timeout:=15000,
                                      RequireJson:=True, MakeLog:=False)
        Catch ex As Exception
            Logger.Warn("QQ 登录：bind/start 失败，HTTP " & StatusCodeOf(ex) & "，" & BodyOf(ex))
            SetState(BindState.Failed, DstModeCore.FriendlyError(StatusCodeOf(ex), BodyOf(ex)))
            Return
        End Try
        Dim Session As DstModeCore.BindSession = DstModeCore.ParseBindSession(Json)
        If Not Session.Ok Then
            SetState(BindState.Failed, If(Session.ErrorText = "", "登录服务返回了无法识别的数据", Session.ErrorText))
            Return
        End If
        BindCode = Session.Code
        BindInstruction = Session.Instruction
        BindExpiresAt = GetTimeMs() + CLng(Session.ExpiresIn) * 1000
        SetState(BindState.Waiting, "")
        Logger.Info("QQ 登录：已获取绑定码，等待玩家在 QQ 中确认")
        '2. 轮询结果
        Thread.Sleep(DstModeCore.FirstPollDelayMs)
        Do
            If Gen <> BindGeneration Then Return
            If GetTimeMs() >= BindExpiresAt Then
                SetState(BindState.Expired, "绑定码已过期，请重新获取")
                Return
            End If
            Dim Result As DstModeCore.PollResult
            Try
                Result = PollOnce(Base, Session.State)
            Catch ex As Exception
                If StatusCodeOf(ex) = 404 Then
                    SetState(BindState.Expired, "这个绑定已经失效，请重新登录")
                    Return
                End If
                SetState(BindState.Failed, DstModeCore.FriendlyError(StatusCodeOf(ex), BodyOf(ex)))
                Return
            End Try
            If Gen <> BindGeneration Then Return
            Select Case Result.Status
                Case DstModeCore.PollStatus.Pending
                    '继续等
                Case DstModeCore.PollStatus.Ok
                    Dim ExpiresIn As Integer = If(Result.ExpiresIn > 0, Result.ExpiresIn, 43200)
                    Dim Cred As New DstModeCredential With {
                        .AccessToken = Result.AccessToken,
                        .Uuid = Result.Uuid,
                        .Name = Result.Name,
                        .AuthBase = Base,
                        .ExpiresAt = NowUnix() + ExpiresIn
                    }
                    Current = Cred
                    SaveCredential(Cred)
                    BindCode = ""
                    BindExpiresAt = 0
                    SetState(BindState.Succeeded, "登录成功：" & Cred.Name)
                    Logger.Info("QQ 登录成功：" & Cred.Name & "（令牌不写日志）")
                    Return
                Case DstModeCore.PollStatus.Expired
                    SetState(BindState.Expired, "绑定码已过期，请重新获取")
                    Return
                Case Else
                    SetState(BindState.Failed, "登录失败：" & If(Result.ErrorText = "", "未知原因", Result.ErrorText))
                    Return
            End Select
            Thread.Sleep(DstModeCore.PollIntervalMs)
        Loop
    End Sub

    Private Shared Function PollOnce(Base As String, BindState2 As String) As DstModeCore.PollResult
        'state 是取 token 的钥匙，绝不能进日志，所以 MakeLog 关掉
        Dim Url As String = Base & "/api/v1/launcher/bind/poll?state=" & Uri.EscapeDataString(BindState2)
        Dim Json As String
        Try
            Json = NetRequestByClient(Url, HttpMethod.Get, Accept:="application/json", Timeout:=10000, MakeLog:=False)
        Catch ex As Exception
            If StatusCodeOf(ex) = 404 Then
                Dim Expired As New DstModeCore.PollResult
                Expired.Status = DstModeCore.PollStatus.Expired
                Return Expired
            End If
            Throw
        End Try
        Return DstModeCore.ParsePoll(Json)
    End Function

    Private Shared Sub SetState(NewState As BindState, Text As String)
        State = NewState
        StateText = If(Text, "")
        Dim Notify As Action = BindNotify
        If Notify IsNot Nothing Then RunInUi(Sub() Notify())
    End Sub

#End Region

#Region "Agent"

    ' 参考：DST-Mode 的 launcher-agent-guide.md
    ' Agent 只改未混淆的 authlib，因此与加载器、游戏版本都无关，
    ' 启动器只需要下载 jar 并拼一个 -javaagent 参数。

    ''' <summary>config.ini 里覆盖 Agent 下载地址的键。</summary>
    Public Const ConfigKeyAgentUrl As String = "DstModeAgentUrl"
    ''' <summary>config.ini 里固定 Agent SHA-256 的键，留给需要强校验的运营方。</summary>
    Public Const ConfigKeyAgentSha256 As String = "DstModeAgentSha256"
    ''' <summary>Agent 的默认下载地址（发布页 v1.0.0）。</summary>
    Public Const DefaultAgentUrl As String = "https://github.com/xiaoqiezi-china/dst-login-mode/releases/download/v1.0.0/dstmode-agent-1.0.0.jar"

    ''' <summary>当前使用的 Agent 地址：config.ini 可覆盖，默认用官方发布地址。</summary>
    Public Shared ReadOnly Property AgentUrl As String
        Get
            Dim Custom As String = ModConfigFile.Read(ModConfigFile.SectionModules, ConfigKeyAgentUrl).Trim()
            If Custom <> "" Then Return Custom
            Return DefaultAgentUrl
        End Get
    End Property
    ''' <summary>国内经常拿不到 github.com，按顺序尝试镜像。</summary>
    Private Shared ReadOnly AgentMirrors As String() = {"", "https://ghfast.top/", "https://gh-proxy.com/"}
    ''' <summary>
    ''' Agent 的最小合理大小：用来挡掉「下载到的其实是 GitHub 的 404 页面」这种情况。
    ''' 不写死精确大小——同一个 Release 附件可能被重新打包上传，写死了就再也下不动。
    ''' </summary>
    Public Const AgentMinSize As Long = 32L * 1024
    ''' <summary>Agent jar 里必须存在的入口类，用来确认下到的确实是 Agent。</summary>
    Private Const AgentPremainClass As String = "dev.dstmode.agent.DstAgent"

    ''' <summary>
    ''' 运营方可以用 config.ini 指定期望的 SHA-256；留空表示不校验哈希，只做结构校验。
    ''' </summary>
    Public Shared ReadOnly Property ExpectedAgentSha256 As String
        Get
            Return ModConfigFile.Read(ModConfigFile.SectionModules, ConfigKeyAgentSha256).Trim().ToLowerInvariant()
        End Get
    End Property

    ''' <summary>
    ''' Agent 的缓存目录：PCL\runtime\dstmode\（接入文档推荐的位置）。
    ''' <para>
    ''' 绝不能放进版本的 mods 目录：Agent 靠 -javaagent 挂载，
    ''' 放在 mods 里会被模组加载器当成模组读，两套机制打架。
    ''' </para>
    ''' </summary>
    Private Shared ReadOnly Property AgentFolder As String
        Get
            Return Paths.Base & "PCL\runtime\dstmode\"
        End Get
    End Property

    Private Shared ReadOnly Property AgentFileName As String
        Get
            Return PathUtils.GetLastPart(AgentUrl)
        End Get
    End Property

    ''' <summary>早先版本把 Agent 误存在了 PCL\DST\Agent\，找到就搬过来。</summary>
    Private Shared Sub MigrateLegacyAgent()
        Try
            Dim Legacy As String = Paths.Base & "PCL\DST\Agent\" & AgentFileName
            If Not FileUtils.Exists(Legacy) Then Return
            DirectoryUtils.Create(AgentFolder)
            FileUtils.Move(Legacy, AgentFolder & AgentFileName)
            Logger.Info("已把 DST-Mode Agent 迁移到 " & AgentFolder)
        Catch ex As Exception
            Logger.Warn(ex, "迁移 DST-Mode Agent 失败")
        End Try
    End Sub

    ''' <summary>
    ''' 已缓存且能用的 Agent 路径；没有则返回空串。
    ''' <para>
    ''' 校验方式是「结构 + 可选哈希」而不是写死大小：
    ''' 同一个 Release 附件被重新打包上传过，写死大小会让老缓存被判无效、
    ''' 新下载又按旧大小校验失败，于是永远下不下来。
    ''' </para>
    ''' </summary>
    Public Shared Function AgentPath() As String
        MigrateLegacyAgent()
        Dim FilePath As String = AgentFolder & AgentFileName
        Dim Reason As String = ValidateAgent(FilePath)
        If Reason = "" Then Return FilePath
        If FileUtils.Exists(FilePath) Then Logger.Info("DST-Mode Agent 不可用（" & Reason & "），将重新下载")
        Return ""
    End Function

    ''' <summary>
    ''' 校验一个 Agent 文件：空串表示可用，否则是无法直接展示给玩家、但会写进日志的原因。
    ''' 只需大小与 Manifest 正确即可，不依赖某一次发布的具体字节数。
    ''' </summary>
    Private Shared Function ValidateAgent(FilePath As String) As String
        Try
            If Not FileUtils.Exists(FilePath) Then Return "文件不存在"
            If FileUtils.GetInfo(FilePath).Length < AgentMinSize Then Return "文件过小"
            Using Zip As IO.Compression.ZipArchive = IO.Compression.ZipFile.OpenRead(FilePath)
                Dim Manifest As IO.Compression.ZipArchiveEntry = ModPackInfo.FindEntry(Zip, "META-INF/MANIFEST.MF")
                If Manifest Is Nothing Then Return "不是有效的 jar（缺少 Manifest）"
                Dim Text As String
                Using Reader As New IO.StreamReader(Manifest.Open())
                    Text = Reader.ReadToEnd()
                End Using
                If Text.IndexOf(AgentPremainClass, StringComparison.Ordinal) < 0 Then
                    Return "不是 DST-Mode Agent（Manifest 里没有 " & AgentPremainClass & "）"
                End If
            End Using
            '运营方指定了哈希才校验哈希
            Dim Expected As String = ExpectedAgentSha256
            If Expected <> "" Then
                Using Sha As Security.Cryptography.SHA256 = Security.Cryptography.SHA256.Create()
                    Using Fs As New IO.FileStream(FilePath, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read)
                        Dim Hash As Byte() = Sha.ComputeHash(Fs)
                        Dim Sb As New Text.StringBuilder(Hash.Length * 2)
                        For Each B As Byte In Hash
                            Sb.Append(B.ToString("x2"))
                        Next
                        If Sb.ToString() <> Expected Then Return "SHA-256 不匹配"
                    End Using
                End Using
            End If
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "校验 DST-Mode Agent 失败")
            Return "无法读取：" & ex.Message
        End Try
    End Function

    ''' <summary>
    ''' 确保 Agent 已就位；失败不拦启动，但要提示玩家。
    ''' </summary>
    Public Shared Function EnsureAgent(Optional WaitForExit As Boolean = True) As Boolean
        Dim Existing As String = AgentPath()
        If Existing <> "" Then
            Logger.Info("DST-Mode Agent 已存在，跳过下载")
            Return True
        End If
        DirectoryUtils.Create(AgentFolder)
        Dim Urls As New List(Of String)
        For Each Mirror In AgentMirrors
            Urls.Add(Mirror & AgentUrl)
        Next
        '只要求最小大小：具体字节数会随发版变化（同一个 Release 附件也会被重新打包），
        '写死精确大小/哈希会让下载永远校验失败。真伪由下载后的 ValidateAgent 结构校验兜住。
        Dim Checker As New FileChecker With {.MinSize = AgentMinSize}
        Dim Expected As String = ExpectedAgentSha256
        If Expected <> "" Then Checker.Hash = Expected
        Dim Download As New LoaderDownload("下载 DST-Mode Agent",
            New List(Of NetFile) From {New NetFile(Urls.ToArray(), AgentFolder & AgentFileName, Checker)}) With {.ProgressWeight = 2}
        Dim Combo As New LoaderCombo(Of Integer)("准备 DST-Mode Agent：", New List(Of LoaderBase) From {Download})
        Combo.OnStateChanged =
        Sub()
            Select Case Combo.State
                Case LoadState.Finished
                    Hint("DST-Mode Agent 已就绪", HintType.Green)
                Case LoadState.Failed
                    Hint("下载 DST-Mode Agent 失败：" & Combo.Error.GetDisplay(False), HintType.Red)
                Case LoadState.Canceled
                    Hint("已取消下载 DST-Mode Agent", HintType.Blue)
                Case Else
                    Return
            End Select
        End Sub
        Combo.Start()
        '与汉化、拼音搜索模组的下载保持一致：挂到右下角任务栏，让玩家看得到进度
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        If WaitForExit Then
            Do While Combo.State = LoadState.Loading
                Thread.Sleep(100)
            Loop
            If Combo.State <> LoadState.Finished Then Return False
            '下载成功不代表文件可用：镜像返回错误页时大小校验能过，结构校验过不了
            Dim Reason As String = ValidateAgent(AgentFolder & AgentFileName)
            If Reason <> "" Then
                Logger.Warn("下载到的 DST-Mode Agent 不可用（" & Reason & "）")
                Try
                    FileUtils.Delete(AgentFolder & AgentFileName)
                Catch ex As Exception
                    Logger.Warn(ex, "清理无效的 DST-Mode Agent 失败")
                End Try
                Return False
            End If
            Return True
        End If
        Return True
    End Function

    ''' <summary>
    ''' 生成 -javaagent 参数。路径含空格时手动加引号（PCL 走命令行字符串而不是参数列表）。
    ''' </summary>
    Public Shared Function BuildAgentArgument(AgentFile As String, AuthBase As String) As String
        If AgentFile = "" OrElse AuthBase = "" Then Return ""
        Return "-javaagent:""" & AgentFile & """=" & AuthBase
    End Function

    ''' <summary>
    ''' 玩家自己装了 dstmode **模组**时，Agent 与模组不能同时用。
    ''' <para>
    ''' 只认 dstmode 模组本身：Agent 的 jar 名字里也带 dstmode（dstmode-agent-x.y.z.jar），
    ''' 早先按子串匹配会把 Agent 自己判成模组。这里要求文件名以 dstmode 开头，
    ''' 且不是 Agent，才算模组。
    ''' </para>
    ''' </summary>
    Public Shared Function HasLegacyMod(Instance As McInstance) As Boolean
        If Instance Is Nothing Then Return False
        Try
            Dim Folder As String = Instance.PathIndie & "mods"
            If Not DirectoryUtils.Exists(Folder) Then Return False
            For Each FilePath In DirectoryUtils.EnumerateFiles(Folder, False)
                Dim FileName As String = PathUtils.GetLastPart(FilePath).ToLowerInvariant()
                If Not FileName.StartsWithF("dstmode") Then Continue For
                If FileName.StartsWithF("dstmode-agent") Then Continue For
                Return True
            Next
        Catch ex As Exception
            Logger.Warn(ex, "检查 DST-Mode 模组失败")
        End Try
        Return False
    End Function

#End Region


#Region "工具"

    Private Shared Function StatusCodeOf(ex As Exception) As Integer
        Dim HttpEx As HttpRequestCodeException = TryCast(ex, HttpRequestCodeException)
        If HttpEx IsNot Nothing Then Return CInt(HttpEx.StatusCode)
        Return 0
    End Function

    Private Shared Function BodyOf(ex As Exception) As String
        Dim HttpEx As HttpRequestCodeException = TryCast(ex, HttpRequestCodeException)
        If HttpEx IsNot Nothing Then Return If(HttpEx.Response, "")
        Return ""
    End Function

    Private Shared Function NowUnix() As Long
        Return DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    End Function

#End Region

End Class
