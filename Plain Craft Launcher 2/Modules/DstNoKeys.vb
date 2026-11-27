Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Text.RegularExpressions
Imports Microsoft.Win32

''' <summary>
''' nokeys（Windows 全局轮盘 / 列表快捷启动器）与 DPCL 的集成层。
''' <para>
''' 只管三件事：装没装、装的哪一版、怎么装怎么卸怎么开。界面只调这里，
''' 不直接碰注册表和进程。
''' </para>
''' <para>
''' 发布的是 MSI 包（WiX 打的，perMachine）：装到 <c>Program Files\NoKeys</c>，
''' 注册表登记走 HKLM 的「应用和功能」，因此安装与卸载都要过 UAC。
''' </para>
''' </summary>
Friend Module DstNoKeys

    ''' <summary>发布仓库。</summary>
    Public Const RepoOwner As String = "xiaoqiezi-china"
    Public Const RepoName As String = "nokeys"

    ''' <summary>仓库主页。</summary>
    Public Const HomePage As String = "https://github.com/xiaoqiezi-china/nokeys"

    ''' <summary>「应用和功能」里的显示名。按它认登记项，比按 GUID 认稳。</summary>
    Private Const DisplayName As String = "NoKeys"

    ''' <summary>主程序文件名。</summary>
    Private Const AppExeName As String = "nokeys.exe"

    ''' <summary>主程序名（不含扩展名），查进程用。</summary>
    Private Const AppProcessName As String = "nokeys"

    ''' <summary>下载下来的安装包落在哪，装完就删。</summary>
    Private ReadOnly Property InstallerPath As String
        Get
            Return PathTemp & "NoKeys\setup" & PendingExtension
        End Get
    End Property

    ''' <summary>Release 数据源：取版本、展开镜像、测速都走它。</summary>
    Private ReadOnly Feed As New ReleaseFeed(RepoOwner, RepoName)

    ''' <summary>
    ''' 默认安装目录。MSI 是 perMachine 安装，落在 Program Files。
    ''' <para>旧版曾按当前用户装到 LocalAppData，这里一并认着，免得老用户被当成没装。</para>
    ''' </summary>
    Public ReadOnly Property DefaultInstallDir As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NoKeys")
        End Get
    End Property

    ''' <summary>旧版按当前用户安装时的目录。</summary>
    Private ReadOnly Property LegacyInstallDir As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NoKeys")
        End Get
    End Property

#Region "读取安装状态"

    ''' <summary>登记项里读出来的一条安装信息。</summary>
    Private Class EntryInfo
        ''' <summary>注册表键名；MSI 下就是一个 {GUID}，卸载时要拿它当 ProductCode。</summary>
        Public KeyName As String = ""
        Public DisplayVersion As String = ""
        Public DisplayIcon As String = ""
        Public InstallLocation As String = ""
        Public UninstallString As String = ""
        Public QuietUninstallString As String = ""
        ''' <summary>键名是 {GUID} 形式，说明走的是 MSI，卸载得交给 msiexec。</summary>
        Public ReadOnly Property IsMsi As Boolean
            Get
                Return Regex.IsMatch(KeyName, "^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 在「应用和功能」里找 NoKeys 的登记项。
    ''' <para>
    ''' MSI 装在 HKLM（64 位登记在 64 位视图，32 位包在 WOW6432Node），
    ''' 旧版 NSIS 装在 HKCU，三处都要扫，否则升级上来的用户会被当成没装。
    ''' </para>
    ''' </summary>
    Private Function FindEntry() As EntryInfo
        For Each Hive As RegistryHive In {RegistryHive.LocalMachine, RegistryHive.CurrentUser}
            For Each View As RegistryView In {RegistryView.Registry64, RegistryView.Registry32}
                Dim Found As EntryInfo = FindEntryIn(Hive, View)
                If Found IsNot Nothing Then Return Found
            Next
        Next
        Return Nothing
    End Function

    Private Function FindEntryIn(Hive As RegistryHive, View As RegistryView) As EntryInfo
        Try
            Using Base As RegistryKey = RegistryKey.OpenBaseKey(Hive, View)
                Using Root As RegistryKey = Base.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Uninstall")
                    If Root Is Nothing Then Return Nothing
                    For Each Name As String In Root.GetSubKeyNames()
                        Using Key As RegistryKey = Root.OpenSubKey(Name)
                            If Key Is Nothing Then Continue For
                            If Not String.Equals(CStr(If(Key.GetValue("DisplayName"), "")).Trim(), DisplayName, StringComparison.OrdinalIgnoreCase) Then Continue For
                            Return New EntryInfo With {
                                .KeyName = Name,
                                .DisplayVersion = CStr(If(Key.GetValue("DisplayVersion"), "")).Trim(),
                                .DisplayIcon = CStr(If(Key.GetValue("DisplayIcon"), "")).Trim(),
                                .InstallLocation = CStr(If(Key.GetValue("InstallLocation"), "")).Trim(),
                                .UninstallString = CStr(If(Key.GetValue("UninstallString"), "")).Trim(),
                                .QuietUninstallString = CStr(If(Key.GetValue("QuietUninstallString"), "")).Trim()}
                        End Using
                    Next
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"读取 nokeys 登记项失败（{Hive}/{View}）")
        End Try
        Return Nothing
    End Function

    ''' <summary>本机是否已安装（以「应用和功能」里的登记项为准）。</summary>
    Public ReadOnly Property IsInstalled As Boolean
        Get
            Return FindEntry() IsNot Nothing
        End Get
    End Property

    ''' <summary>已安装的版本号；没装或读不到时返回空串。</summary>
    Public ReadOnly Property InstalledVersion As String
        Get
            Dim Entry As EntryInfo = FindEntry()
            Return If(Entry Is Nothing, "", Entry.DisplayVersion)
        End Get
    End Property

    ''' <summary>
    ''' 主程序路径；找不到返回空串。
    ''' <para>
    ''' MSI 的 DisplayIcon 指向的是图标缓存，不是主程序，所以这里按
    ''' 安装目录 → 默认目录 → 旧版目录 → DisplayIcon 的顺序找。
    ''' </para>
    ''' </summary>
    Public Function FindAppExe() As String
        Dim Entry As EntryInfo = FindEntry()
        Dim Folders As New List(Of String)
        If Entry IsNot Nothing AndAlso Entry.InstallLocation <> "" Then Folders.Add(Entry.InstallLocation)
        Folders.Add(DefaultInstallDir)
        Folders.Add(LegacyInstallDir)
        For Each Folder In Folders
            Dim Guess As String = Path.Combine(Folder, AppExeName)
            If FileUtils.Exists(Guess) Then Return Guess
        Next
        '目录被挪过时，DisplayIcon 里可能还是主程序
        If Entry IsNot Nothing AndAlso Entry.DisplayIcon <> "" Then
            Dim PathOnly As String = Entry.DisplayIcon.Trim().Trim(Chr(34))
            Dim Comma As Integer = PathOnly.LastIndexOf(","c)
            If Comma > 0 Then PathOnly = PathOnly.Substring(0, Comma).Trim().Trim(Chr(34))
            If PathOnly.EndsWithF(".exe", True) AndAlso FileUtils.Exists(PathOnly) Then Return PathOnly
        End If
        Return ""
    End Function

    ''' <summary>程序是否正在运行。</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            Try
                Return Process.GetProcessesByName(AppProcessName).Any(Function(P) Not P.HasExited)
            Catch ex As Exception
                Logger.Warn(ex, "检查 nokeys 是否在运行失败")
                Return False
            End Try
        End Get
    End Property

#End Region

#Region "版本查询"

    ''' <summary>查询远端最新版本号；查不到返回空串。会走网络，不能在 UI 线程调用。</summary>
    Public Function FetchLatestVersion() As String
        Try
            Return FetchLatest().Version
        Catch ex As Exception
            Logger.Warn(ex, "查询 nokeys 最新版本失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 查询最新 Release，并挑出安装包附件。
    ''' <para>
    ''' 现在发布的是 <c>.msi</c>；同一个 Release 里还有便携版 zip，
    ''' 万一哪天又发回 <c>-setup.exe</c>，这里也认。
    ''' </para>
    ''' </summary>
    Private Function FetchLatest() As ReleaseInfo
        Dim Info As ReleaseInfo = Feed.FetchLatest(".msi", Nothing)
        If Info.CanDownload Then Return Info
        'API 拿不到附件列表（限流、被墙）时，只要还能从 releases/latest 的跳转里读到版本号，
        '就按发布者的命名约定拼一条直链。这样限流时仍然装得上——
        '代价是这次没有 SHA 可校验，那一步会如实记日志后跳过。
        If Info.VersionOnly AndAlso Info.Version <> "" Then
            Dim Guessed As String = Feed.LatestAssetUrl($"NoKeys-{Info.Version}.msi")
            If Guessed <> "" Then
                Info.AssetName = $"NoKeys-{Info.Version}.msi"
                Info.AssetUrl = Guessed
                Info.Err = ""
                Logger.Info($"拿不到 Release 附件列表，按命名约定改用直链：{Guessed}")
                Return Info
            End If
        End If
        '确实是这个 Release 里没有 msi，才退回旧版的 -setup.exe 命名
        Return Feed.FetchLatest(".exe", Function(Name) Name.IndexOf("-setup", StringComparison.OrdinalIgnoreCase) >= 0)
    End Function

    ''' <summary>版本比较：A 比 B 新返回 True。</summary>
    Public Function IsNewerVersion(Remote As String, Local As String) As Boolean
        If Remote = "" Then Return False
        If Local = "" Then Return True
        Try
            Return DstUpdate.CompareVersion(Remote, Local) > 0
        Catch ex As Exception
            Logger.Warn(ex, "比较 nokeys 版本失败")
            Return False
        End Try
    End Function

#End Region

#Region "下载与安装"

    '三步之间靠这几个模块级变量传
    Private PendingInstaller As String = ""
    Private PendingSha256 As String = ""
    Private PendingVersion As String = ""
    Private PendingExtension As String = ".msi"

    ''' <summary>
    ''' 下载并安装，进度显示在右下角的任务列表里。
    ''' <para>
    ''' 三步交给 <see cref="LoaderCombo"/>：解析地址 → 下载 → 运行安装器并等登记项。
    ''' </para>
    ''' </summary>
    Public Function DownloadAndInstall() As LoaderCombo(Of Integer)
        Dim Resolve As New LoaderTask(Of Integer, List(Of NetFile))("解析安装包地址", AddressOf PrepareDownload) With {.ProgressWeight = 1}
        Dim Fetch As New LoaderDownload("下载 NoKeys", New List(Of NetFile)) With {.ProgressWeight = 8}
        Dim Install As New LoaderTask(Of Integer, Integer)("安装 NoKeys", AddressOf RunInstaller) With {.ProgressWeight = 2}

        Dim Combo As New LoaderCombo(Of Integer)("安装 NoKeys", New List(Of LoaderBase) From {Resolve, Fetch, Install})
        Combo.Start()
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Return Combo
    End Function

    ''' <summary>第一步：问 Release 要地址与摘要，产出待下载的文件列表。</summary>
    Private Sub PrepareDownload(Task As LoaderTask(Of Integer, List(Of NetFile)))
        PendingSha256 = ""
        PendingVersion = ""

        Dim Release As ReleaseInfo = FetchLatest()
        PendingVersion = Release.Version
        If Not Release.CanDownload Then
            Throw New Exception($"没有找到 NoKeys 的安装包附件：{If(Release.Err, "Release 里没有 .msi")}")
        End If
        PendingSha256 = Release.AssetSha256
        PendingExtension = If(Release.AssetName.EndsWithF(".msi", True), ".msi", ".exe")
        Logger.Info($"NoKeys 最新版：{Release.Version}（{Release.AssetName}）")

        PendingInstaller = InstallerPath
        DirectoryUtils.Create(PathUtils.RemoveLastPart(PendingInstaller))
        '残留的半个文件会被当成断点续传起点，最后「下载完成」但内容不全
        If FileUtils.Exists(PendingInstaller) Then FileUtils.Delete(PendingInstaller)

        '镜像之间速度差很大，先测速再从快到慢依次尝试
        Dim Sources As String() = Feed.RankSourcesBySpeed(Feed.BuildSources(Release.AssetUrl))
        If Sources Is Nothing OrElse Sources.Length = 0 Then Throw New Exception("没有可用的下载地址")
        Logger.Info($"开始下载 NoKeys 安装包，候选源 {Sources.Length} 个")

        '把摘要交给下载器：这样校验发生在下载阶段，坏包会自动换下一个源重试，
        '而不是等下载完、要提权装的时候才发现它是坏的
        Dim Checker As New FileChecker With {.MinSize = 1024 * 1024}
        If PendingSha256 <> "" Then Checker.Hash = PendingSha256

        Task.Output = New List(Of NetFile) From {New NetFile(Sources, PendingInstaller, Checker)}
    End Sub

    ''' <summary>
    ''' 第三步：校验摘要，运行安装器，并等「应用和功能」里的版本真的变过去。
    ''' <para>
    ''' MSI 是 perMachine 安装，需要管理员权限；用户点了 UAC 的「否」会直接抛错，
    ''' 这里翻成人话。装完之后不能只看进程退出——升级时旧登记项本来就在，
    ''' 必须比对版本号才算数。
    ''' </para>
    ''' </summary>
    Private Sub RunInstaller(Task As LoaderTask(Of Integer, Integer))
        '有摘要就校验：下载会经过第三方镜像，没有摘要等于信任链断了
        If PendingSha256 <> "" Then
            Dim Actual As String = Feed.GetFileSha256(PendingInstaller)
            If Not String.Equals(Actual, PendingSha256, StringComparison.OrdinalIgnoreCase) Then
                FileUtils.Delete(PendingInstaller)
                Logger.Warn($"NoKeys 校验失败：期望 {PendingSha256}，实际 {Actual}")
                Throw New Exception("安装包校验失败，文件可能已损坏或被镜像替换，请稍后重试")
            End If
            Logger.Info($"NoKeys 安装包 SHA-256 校验通过：{Actual}")
        Else
            Logger.Warn("本次下载没有可用的 SHA-256 摘要，跳过完整性校验")
        End If

        If Not FileUtils.Exists(PendingInstaller) Then Throw New Exception("安装包不见了，请重新下载")

        Logger.Info($"开始运行 NoKeys 安装包（版本 {PendingVersion}）：{PendingInstaller}")
        Dim Proc As Process
        Try
            If PendingExtension = ".msi" Then
                '直接跑 msiexec，这样拿得到进程句柄；UAC 由 runas 触发
                Proc = StartElevated("msiexec.exe", "/i " & Quote(PendingInstaller))
            Else
                Proc = StartProcess(New ProcessStartInfo(PendingInstaller) With {.UseShellExecute = True})
            End If
        Catch ex As Win32Exception
            '1223 是用户在 UAC 弹窗上点了「否」
            If ex.NativeErrorCode = 1223 Then Throw New Exception("安装需要管理员权限，你取消了授权。" & vbCrLf & "请再点一次「安装」并在弹窗里选择「是」。")
            Throw New Exception($"无法启动安装程序：{ex.Message}", ex)
        Catch ex As Exception
            Throw New Exception($"无法启动安装程序：{ex.Message}", ex)
        End Try

        '等安装向导走完。给 15 分钟：用户可能中途去干别的
        If Proc IsNot Nothing Then
            Try
                If Not Proc.WaitForExit(900000) Then
                    Throw New Exception("安装窗口长时间没有结束，请先完成或关闭安装向导，再点一次「安装」重新检测。")
                End If
            Catch ex As InvalidOperationException
                '拿不到退出事件时退回轮询
            End Try
        End If

        '进程退出不代表装好了（向导可能被取消），以登记项里的版本为准
        Dim Deadline As Long = GetTimeMs() + 60000
        Do While GetTimeMs() < Deadline
            Dim Now As String = InstalledVersion
            If Now <> "" AndAlso (PendingVersion = "" OrElse Not IsNewerVersion(PendingVersion, Now)) Then
                Logger.Info($"NoKeys 已安装，版本 {Now}")
                Task.Progress = 1
                CleanupInstaller()
                Return
            End If
            Thread.Sleep(500)
        Loop

        Logger.Warn($"NoKeys 安装后版本仍为「{InstalledVersion}」，期望 {PendingVersion}")
        Throw New Exception("安装没有完成：安装向导可能被取消，或安装过程中出错了。" & vbCrLf &
                            $"当前登记的版本：{If(InstalledVersion = "", "无", InstalledVersion)}。")
    End Sub

    Private Sub CleanupInstaller()
        Try
            FileUtils.Delete(PendingInstaller)
        Catch ex As Exception
            Logger.Warn(ex, "删除 NoKeys 安装包失败")
        End Try
    End Sub

#End Region

#Region "卸载与启动"

    ''' <summary>
    ''' 卸载。MSI 交给 msiexec，旧版 NSIS 交给它自己的卸载程序。
    ''' <para>
    ''' 两者都会弹窗（UAC 或确认框），所以这里不假装静默，装完再核对登记项是否消失。
    ''' </para>
    ''' </summary>
    Public Function Uninstall() As LoaderTask(Of Integer, Integer)
        Dim Task As New LoaderTask(Of Integer, Integer)("卸载 NoKeys", AddressOf UninstallCore)
        Task.Start()
        Return Task
    End Function

    Private Sub UninstallCore(Task As LoaderTask(Of Integer, Integer))
        Dim Entry As EntryInfo = FindEntry()
        If Entry Is Nothing Then Throw New Exception("NoKeys 没有安装")

        Dim Proc As Process = Nothing
        Try
            If Entry.IsMsi Then
                '键名就是 ProductCode，直接 /x 比解析 UninstallString 稳
                Logger.Info($"运行 msiexec 卸载 NoKeys：{Entry.KeyName}")
                Proc = StartElevated("msiexec.exe", "/x " & Entry.KeyName)
            Else
                Dim Command As String = If(Entry.QuietUninstallString <> "", Entry.QuietUninstallString, Entry.UninstallString)
                If Command = "" Then Throw New Exception("读不到卸载命令，请到 Windows 的「应用」里手动卸载")
                Dim Exe As String = Command
                Dim Args As String = ""
                If Command.StartsWithF(Chr(34)) Then
                    Dim EndQuote As Integer = Command.IndexOf(Chr(34), 1)
                    If EndQuote > 0 Then
                        Exe = Command.Substring(1, EndQuote - 1)
                        Args = Command.Substring(EndQuote + 1).Trim()
                    End If
                Else
                    Dim Space As Integer = Command.IndexOf(" "c)
                    If Space > 0 Then
                        Exe = Command.Substring(0, Space)
                        Args = Command.Substring(Space + 1).Trim()
                    End If
                End If
                Logger.Info($"运行 NoKeys 卸载程序：{Exe} {Args}")
                Proc = StartProcess(New ProcessStartInfo(Exe) With {.UseShellExecute = True, .Arguments = Args})
            End If
        Catch ex As Win32Exception
            If ex.NativeErrorCode = 1223 Then Throw New Exception("卸载需要管理员权限，你取消了授权。")
            Throw New Exception($"无法启动卸载程序：{ex.Message}", ex)
        End Try

        If Proc IsNot Nothing Then
            Try
                Proc.WaitForExit(300000)
            Catch ex As InvalidOperationException
            End Try
        End If

        Dim Deadline As Long = GetTimeMs() + 120000
        Do While GetTimeMs() < Deadline
            If Not IsInstalled Then
                Logger.Info("NoKeys 已卸载")
                Task.Progress = 1
                Return
            End If
            Thread.Sleep(500)
        Loop
        Throw New Exception("卸载超时：两分钟内登记项还在。可能卸载程序正在等待你确认。")
    End Sub

    ''' <summary>启动程序。没有参数，它就是个常驻托盘的工具。</summary>
    Public Sub Launch()
        Dim Exe As String = FindAppExe()
        If Exe = "" Then Throw New Exception("找不到 nokeys 主程序，请重新安装")
        Logger.Info($"启动 NoKeys：{Exe}")
        StartProcess(New ProcessStartInfo(Exe) With {.UseShellExecute = True})
    End Sub

#End Region

#Region "启动进程"

    ''' <summary>
    ''' 以管理员身份启动进程。用户在 UAC 上点「否」会抛 <see cref="Win32Exception"/>（1223）。
    ''' </summary>
    Private Function StartElevated(FileName As String, Arguments As String) As Process
        Return Process.Start(New ProcessStartInfo(FileName) With {
            .UseShellExecute = True,
            .Verb = "runas",
            .Arguments = Arguments})
    End Function

    ''' <summary>给命令行参数加引号，路径里有空格时必须。</summary>
    Private Function Quote(Value As String) As String
        Dim Q As String = Chr(34)
        Return Q & Value.Replace(Q, Q & Q) & Q
    End Function

#End Region

End Module
