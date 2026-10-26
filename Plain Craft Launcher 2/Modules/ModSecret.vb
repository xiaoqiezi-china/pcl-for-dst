'由于包含加解密等安全信息，本文件中的部分代码已被删除
Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "DST"
    '注册表根，同时也是本启动器的内部标识。
    '统一用 "PCL" 是为了兼容原版 PCL 的既有设置（主题、账号、路径等开机即生效）；
    '旧版曾用过 "DSTLauncher"，启动时会自动搬一次，见 Application.MigrateLegacyRegistry。
    Public Const RegFolder As String = "PCL"
    ''' <summary>
    ''' 账号凭据单独的注册表子键，相对于 RegFolder 的完整路径。
    ''' <para>
    ''' 为什么账号要单独存：微软的 refresh token 是滚动失效的，
    ''' 每用一次就会作废上一个。如果和上游 PCL 共用同一份凭据，
    ''' 两边交替打开就会互相抢掉对方的 token，表现为「登录状态无故掉」。
    ''' </para>
    ''' <para>
    ''' 还有一层原因：加密的密钥是 "PCL" &amp; Identify，而官方发行版的 Identify
    ''' 是真实设备码，与我们的不同，互相解不开。
    ''' 而我们的读取逻辑在解不开时会「重置为默认值并写回」，
    ''' 等于把对方的凭据抹掉。分开存就不会有这个问题。
    ''' </para>
    ''' </summary>
    Public Const RegFolderAccount As String = "PCL\DST"

    ''' <summary>判断一个设置项是否属于账号凭据。</summary>
    Public Function IsAccountSetting(Key As String) As Boolean
        Return Key.StartsWithF("CacheMs", True) OrElse Key.StartsWithF("CacheNide", True) OrElse
               Key.StartsWithF("CacheAuth", True) OrElse Key = "LoginMsJson" OrElse
               Key.StartsWithF("LoginLegacy", True)
    End Function

    ''' <summary>某个设置项应该用的注册表子键。</summary>
    Public Function RegFolderOf(Key As String) As String
        Return If(IsAccountSetting(Key), RegFolderAccount, RegFolder)
    End Function
#Region "账号与 API 凭据"

    '凭据分两层：
    '  1. 编译期内置值（Builtin*）：闭源版来自 ModSecret.Local.vb，开源版为空，也可用环境变量给；
    '  2. 用户在「设置 → 其他 → 账号与 API」里填写的值：存在 PCL\Setup.ini，填了就以它为准。
    '这样同一套代码既能做内置凭据的闭源版，也能做让用户自备凭据的开源版。

#If DST_LOCAL Then
    '闭源版：凭据来自 ModSecret.Local.vb（该文件不进仓库）
    Public Const BuiltinOAuthClientId As String = ModSecretLocal.ClientId
    Public Const BuiltinCurseForgeKey As String = ModSecretLocal.CurseForgeKey
#Else
    '开源版：默认不带任何凭据，可由使用者在环境变量或设置里提供
    Public ReadOnly BuiltinOAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "")
    Public ReadOnly BuiltinCurseForgeKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
#End If

    ''' <summary>设置里填写的 ClientId；空串表示「用内置默认值」。</summary>
    Friend Function MsClientIdUserValue() As String
        Return If(Settings.Get(Of String)("DstOAuthClientId"), "").Trim()
    End Function

    ''' <summary>
    ''' 实际使用的微软登录 ClientId：用户填了就用用户的，没填就用内置的，都没有返回空串。
    ''' 空串时不应发起正版登录，调用方需引导用户去设置里填写。
    ''' </summary>
    Public Function MsClientIdResolve() As String
        Dim User As String = MsClientIdUserValue()
        If User <> "" Then Return User
        '设置里没填就看配置文件：发布版会把初始值释放到 PCL\config.ini，
        '这样更新启动器之后凭据还在（更新只覆盖程序，不动 PCL 目录）
        Dim FromFile As String = ModConfigFile.Read(ModConfigFile.SectionCredential, "ClientId")
        If FromFile <> "" Then Return FromFile
        Return If(BuiltinOAuthClientId, "").Trim()
    End Function

    ''' <summary>设置里填写的 CurseForge Key；空串表示「用内置默认值」。</summary>
    Friend Function CurseForgeKeyUserValue() As String
        Return If(Settings.Get(Of String)("DstCurseForgeKey"), "").Trim()
    End Function

    ''' <summary>
    ''' 实际使用的 CurseForge API Key：用户填了就用用户的，没填就用内置的，都没有返回空串。
    ''' 空串时搜索社区资源会失败，调用方需引导用户去设置里填写。
    ''' </summary>
    Public Function CurseForgeKeyResolve() As String
        Dim User As String = CurseForgeKeyUserValue()
        If User <> "" Then Return User
        Dim FromFile As String = ModConfigFile.Read(ModConfigFile.SectionCredential, "CurseForgeKey")
        If FromFile <> "" Then Return FromFile
        Return If(BuiltinCurseForgeKey, "").Trim()
    End Function

    ''' <summary>当前是否已经具备可用的 CurseForge Key。</summary>
    Public ReadOnly Property HasCurseForgeKey As Boolean
        Get
            Return CurseForgeKeyResolve() <> ""
        End Get
    End Property

    ''' <summary>当前是否已经具备可用的微软登录 ClientId。</summary>
    Public ReadOnly Property HasMsClientId As Boolean
        Get
            Return MsClientIdResolve() <> ""
        End Get
    End Property

#End Region
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "机器人预设服务"

    '预设服务地址与令牌。本地开发取 ModSecret.Local.vb，发布版留空由使用者自行配置
#If DST_LOCAL Then
    Public Const DstBotPresetApi As String = ModSecretLocal.PresetApi
    Public Const DstBotPresetToken As String = ModSecretLocal.PresetToken
    ''' <summary>闭源版默认选中的预设 id，开源版为空。</summary>
    Public Const BuiltinPresetId As String = ModSecretLocal.PresetId
    Public Const BuiltinGroupId As String = ModSecretLocal.PresetGroup
#Else
    Public Const DstBotPresetApi As String = ""
    Public Const DstBotPresetToken As String = ""
    Public Const BuiltinPresetId As String = ""
    Public Const BuiltinGroupId As String = ""
#End If

#End Region

#Region "网络鉴权"

    Public Const RestrictMcDownload As Boolean = False
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeKeyResolve())
    End Sub

#End Region


#Region "更新"

    Friend Sub UpdateCheckByButton()
        DstUpdate.CheckLauncherUpdateByButton()
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    ''' <summary>
    ''' 由新版本进程带着 --update 调用：等旧进程退出后替换主程序，必要时拉起新版本。
    ''' NewFileName 可以是单个 exe（单程序更新），也可以是同目录下带依赖的整包。
    ''' </summary>
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Dim Bak As String = OldFileName & ".old"
        Try
            '1. 等旧进程退出
            Dim Old As Process = Nothing
            Try
                Old = Process.GetProcessById(ProcessId)
            Catch ex As Exception
                Logger.Warn(ex, $"按进程 ID 打开旧进程失败：{ProcessId}")
            End Try
            If Old IsNot Nothing Then
                Try
                    Old.WaitForExit(60000)
                Catch ex As Exception
                    Logger.Warn(ex, "等待旧进程退出失败")
                End Try
            End If
            Thread.Sleep(300)

            Dim TargetDir As String = PathUtils.RemoveLastPart(OldFileName)
            Dim SourceDir As String = PathUtils.RemoveLastPart(NewFileName)
            Logger.Info($"开始替换启动器：{NewFileName} → {OldFileName}")

            '替换前确认两个路径都合法：目标必须是 exe，源文件必须存在且不是自己
            If Not OldFileName.EndsWithF(".exe", ignoreCase:=True) OrElse Not NewFileName.EndsWithF(".exe", ignoreCase:=True) Then
                Throw New Exception($"更新文件名不合法，已中止：{OldFileName} / {NewFileName}")
            End If
            If Not FileUtils.Exists(NewFileName) Then Throw New Exception($"新版本文件不存在，已中止：{NewFileName}")
            If String.Equals(IO.Path.GetFullPath(NewFileName), IO.Path.GetFullPath(OldFileName), StringComparison.OrdinalIgnoreCase) Then
                Throw New Exception("新旧文件是同一个文件，已中止替换")
            End If

            '2. 旧 exe 先改名让位，否则文件占用写不进去
            Try
                If FileUtils.Exists(Bak) Then FileUtils.Delete(Bak)
            Catch ex As Exception
                Logger.Warn(ex, $"删除备份文件失败：{Bak}")
            End Try
            If FileUtils.Exists(OldFileName) Then IO.File.Move(OldFileName, Bak)

            '3. 先拷到同目录的临时文件，校验通过后再整体替换，避免写到一半留下残缺的主程序
            Dim Staging As String = OldFileName & ".new"
            Try
                If FileUtils.Exists(Staging) Then FileUtils.Delete(Staging)
                IO.File.Copy(NewFileName, Staging, True)
                If New IO.FileInfo(Staging).Length <> New IO.FileInfo(NewFileName).Length Then
                    Throw New Exception("新文件拷贝不完整")
                End If
                IO.File.Move(Staging, OldFileName)
            Catch ex As Exception
                '临时文件失败时清掉，保持目标目录干净
                Try
                    If FileUtils.Exists(Staging) Then FileUtils.Delete(Staging)
                Catch ex2 As Exception
                    Logger.Warn(ex2, $"清理暂存文件失败：{Staging}")
                End Try
                Throw
            End Try
            Logger.Info("主程序已替换")

            '配置文件跟着改名：.NET 只认 <exe名>.config，名字对不上就不会生效
            Dim SrcConfig As String = NewFileName & ".config"
            If FileUtils.Exists(SrcConfig) Then
                Try
                    IO.File.Copy(SrcConfig, OldFileName & ".config", True)
                    Logger.Info("配置文件已同步")
                Catch ex As Exception
                    Logger.Warn(ex, "同步配置文件失败")
                End Try
            End If

            '4. 新版本目录里若还带了别的依赖文件，一并覆盖（跳过用户数据与新主程序自身）
            Dim SkipDirs As String() = {"PCL", ".minecraft", "PCLData"}
            Dim Count As Integer = 0
            For Each FilePath In DirectoryUtils.EnumerateFiles(SourceDir, True)
                If String.Equals(FilePath, NewFileName, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim Rel As String = FilePath.Substring(SourceDir.Length).TrimStart("\"c, "/"c)
                Dim Top As String = Rel.Split("\"c, "/"c)(0)
                If SkipDirs.Contains(Top, StringComparer.OrdinalIgnoreCase) Then Continue For
                Dim Dest As String = IO.Path.Combine(TargetDir, Rel)
                Try
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(Dest))
                    IO.File.Copy(FilePath, Dest, True)
                    Count += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"覆盖文件失败：{Rel}")
                End Try
            Next
            If Count > 0 Then Logger.Info($"附带覆盖了 {Count} 个依赖文件")

            '5. 启动新版本、清理备份
            Try
                FileUtils.Delete(Bak)
            Catch ex As Exception
                Logger.Warn(ex, $"删除备份文件失败：{Bak}")
            End Try
            '只有替换确实成功后才会走到这里，此时才拉起新版本
            If TriggerRestart Then StartLauncherProcess(OldFileName)
        Catch ex As Exception
            Logger.Error(ex, "替换启动器文件失败")
            '失败时把旧文件还回去，别把程序弄坏
            Dim Restored As Boolean = False
            Try
                If FileUtils.Exists(Bak) Then
                    If FileUtils.Exists(OldFileName) Then FileUtils.Delete(OldFileName)
                    IO.File.Move(Bak, OldFileName)
                End If
                Restored = FileUtils.Exists(OldFileName)
            Catch ex2 As Exception
                Logger.Error(ex2, "回滚启动器文件失败")
            End Try
            '只有确认旧文件已经回来才重新启动，否则宁可不启动也不要拉起一个残缺的程序
            If TriggerRestart AndAlso Restored Then StartLauncherProcess(OldFileName)
        End Try
    End Sub
    ''' <summary>启动指定的启动器进程；失败只记日志，不再向外抛。</summary>
    Private Sub StartLauncherProcess(ExePath As String)
        Try
            '走 PCL 的统一入口，顺带处理长路径
            Dim P As Process = StartProcess(New ProcessStartInfo With {
                .FileName = ExePath,
                .WorkingDirectory = PathUtils.RemoveLastPart(ExePath),
                .UseShellExecute = True})
            P?.Dispose()
        Catch ex As Exception
            Logger.Warn(ex, $"拉起启动器失败：{ExePath}")
        End Try
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

#Region "联机配置"

    ''' <summary>
    ''' 联机功能的内置配置。
    ''' <para>
    ''' 配置编译期内置，不再依赖远端下发：
    ''' 结构与数据源固定，数据源为 EasyTier 官方 Release。
    ''' </para>
    ''' </summary>
    Public ReadOnly Property BuiltinServerConfig As JObject
        Get
            If _BuiltinServerConfig IsNot Nothing Then Return _BuiltinServerConfig
            _BuiltinServerConfig = JObject.Parse("{
                ""Link"": {
                    ""EasyTierVersion"": 264,
                    ""Downloads"": [
                        ""https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip"",
                        ""https://ghfast.top/https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip"",
                        ""https://gh-proxy.com/https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip""
                    ],
                    ""DiscoverPeer"": ""tcp://easytier-us.slarker.me:11010"",
                    ""DiscoverPeerId"": 1,
                    ""RandomPeer"": 2,
                    ""Peers"": [],
                    ""PeersBlackList"": [],
                    ""MandatoryPeers"": [],
                    ""Argument"": """",
                    ""MinVersionCode"": 0
                }
            }")
            Return _BuiltinServerConfig
        End Get
    End Property
    Private _BuiltinServerConfig As JObject

#End Region
    ''' <summary>联网获取的配置；本版没有远端来源，直接用内置的那份。</summary>
    Public ServerConfig As JObject = BuiltinServerConfig

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
