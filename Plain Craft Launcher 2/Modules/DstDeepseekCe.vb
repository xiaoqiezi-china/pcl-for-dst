Imports System.Diagnostics
Imports System.IO
Imports Microsoft.Win32

''' <summary>
''' Deepseek CE 桌面客户端与 DPCL 的集成层。
''' <para>
''' 分成两件事：一是「装没装、装的哪版、怎么装怎么卸怎么开」，
''' 二是「怎么用命令行把内容递给它」——后者见 <see cref="BuildArguments"/>。
''' 界面只调这里，不直接碰注册表和进程。
''' </para>
''' </summary>
Friend Module DstDeepseekCe

    ''' <summary>发布桌面客户端的仓库。</summary>
    Public Const RepoOwner As String = "xiaoqiezi-china"
    Public Const RepoName As String = "deepseek-chatapp"

    ''' <summary>
    ''' 卸载信息所在的注册表键（HKCU）。
    ''' 客户端由 NSIS 安装器安装，装完会在这里登记；卸载后整块消失。
    ''' 所以「这个键在不在」就是「装没装」，比找 exe 可靠——用户可能装在任意位置。
    ''' </summary>
    Public Const UninstallKey As String = "HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Deepseek CE"

    ''' <summary>注册表里的键路径（相对 HKCU），与 <see cref="UninstallKey"/> 指向同一处。</summary>
    Private Const UninstallSubKey As String = "Software\Microsoft\Windows\CurrentVersion\Uninstall\Deepseek CE"

    ''' <summary>安装器文件名前缀。Release 里同一后缀的附件不止一个，靠它挑出安装版。</summary>
    Private Const InstallerMarker As String = "-setup"

    ''' <summary>下载下来的安装包落在哪。固定路径，装完就删。</summary>
    Private ReadOnly Property InstallerPath As String
        Get
            Return PathTemp & "DeepseekCE\setup.exe"
        End Get
    End Property

    ''' <summary>Release 数据源：取版本、展开镜像、测速都走它。</summary>
    Private ReadOnly Feed As New ReleaseFeed(RepoOwner, RepoName)

    ''' <summary>本机是否已安装。</summary>
    Public ReadOnly Property IsInstalled As Boolean
        Get
            Return ReadRegistry("DisplayName") IsNot Nothing OrElse ReadRegistry("DisplayVersion") IsNot Nothing
        End Get
    End Property

    ''' <summary>已安装的版本号；没装或读不到时返回空串。</summary>
    Public ReadOnly Property InstalledVersion As String
        Get
            Return If(ReadRegistry("DisplayVersion"), "").Trim()
        End Get
    End Property

    ''' <summary>
    ''' 读取卸载信息里的某个值；键不存在或读不到时返回 Nothing。
    ''' </summary>
    Private Function ReadRegistry(Entry As String) As String
        Try
            Using Key As RegistryKey = Registry.CurrentUser.OpenSubKey(UninstallSubKey)
                If Key Is Nothing Then Return Nothing
                Dim Value As Object = Key.GetValue(Entry)
                Return If(Value Is Nothing, Nothing, Value.ToString())
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取 Deepseek CE 注册表失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 主程序路径；找不到返回空串。
    ''' <para>
    ''' 先认 DisplayIcon（安装器登记的就是主程序，最准），
    ''' 再退回 InstallLocation 里扫 exe，最后试默认安装目录。
    ''' </para>
    ''' </summary>
    Public Function FindAppExe() As String
        '1. DisplayIcon 通常是 "路径\app.exe,0"
        Dim Icon As String = ReadRegistry("DisplayIcon")
        If Not String.IsNullOrWhiteSpace(Icon) Then
            Dim PathOnly As String = Icon.Trim().Trim(""""c)
            Dim Comma As Integer = PathOnly.LastIndexOf(","c)
            If Comma > 0 Then PathOnly = PathOnly.Substring(0, Comma).Trim().Trim(""""c)
            If PathOnly.EndsWithF(".exe", True) AndAlso FileUtils.Exists(PathOnly) Then Return PathOnly
        End If
        '2. 安装目录里扫一遍
        Dim Folder As String = ReadRegistry("InstallLocation")
        If Not String.IsNullOrWhiteSpace(Folder) AndAlso DirectoryUtils.Exists(Folder) Then
            Dim Found As String = ScanForExe(Folder)
            If Found <> "" Then Return Found
        End If
        '3. 安装器没登记目录时的常见位置
        For Each Guess As String In {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Deepseek CE"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deepseek CE"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Deepseek CE")}
            If DirectoryUtils.Exists(Guess) Then
                Dim Found As String = ScanForExe(Guess)
                If Found <> "" Then Return Found
            End If
        Next
        Return ""
    End Function

    ''' <summary>在一个目录（含子目录）里找第一个像主程序的 exe。</summary>
    Private Function ScanForExe(Folder As String) As String
        Try
            '优先文件名里带 deepseek 的，避免捡到 uninstall.exe 之类的工具
            Dim All As List(Of String) = DirectoryUtils.EnumerateFiles(Folder, True, "*.exe").ToList()
            Dim Main As String = All.FirstOrDefault(
                Function(F) PathUtils.GetLastPart(F).StartsWithF("Deepseek", True) AndAlso
                            Not PathUtils.GetLastPart(F).StartsWithF("uninstall", True))
            Return If(Main, All.FirstOrDefault(Function(F) Not PathUtils.GetLastPart(F).StartsWithF("uninstall", True)))
        Catch ex As Exception
            Logger.Warn(ex, $"扫描 {Folder} 里的 Deepseek CE 主程序失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>程序是否正在运行。</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            Try
                Dim Exe As String = FindAppExe()
                If Exe = "" Then Return False
                Dim Name As String = PathUtils.GetFileNameWithoutExtension(Exe)
                Return Process.GetProcessesByName(Name).Any(Function(P) Not P.HasExited)
            Catch ex As Exception
                Logger.Warn(ex, "检查 Deepseek CE 是否在运行失败")
                Return False
            End Try
        End Get
    End Property

    ''' <summary>查询远端最新版本号；查不到返回空串。会走网络，不能在 UI 线程调用。</summary>
    Public Function FetchLatestVersion() As String
        Try
            Return FetchLatest().Version
        Catch ex As Exception
            Logger.Warn(ex, "查询 Deepseek CE 最新版本失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 查询最新 Release，并挑出安装包附件。
    ''' <para>
    ''' 同一版本里可能同时挂着安装版和便携版，都取第一个 hack 会拿错，
    ''' 所以按文件名里的 <c>-setup</c> 收窄。
    ''' </para>
    ''' </summary>
    Private Function FetchLatest() As ReleaseInfo
        Return Feed.FetchLatest(".exe", Function(Name) Name.IndexOf(InstallerMarker, StringComparison.OrdinalIgnoreCase) >= 0)
    End Function

    ''' <summary>版本比较：A 比 B 新返回 True。</summary>
    Public Function IsNewerVersion(Remote As String, Local As String) As Boolean
        If Remote = "" Then Return False
        If Local = "" Then Return True
        Try
            Return DstUpdate.CompareVersion(Remote, Local) > 0
        Catch ex As Exception
            Logger.Warn(ex, "比较 Deepseek CE 版本失败")
            Return False
        End Try
    End Function

#Region "下载与安装"

    '第一步与第三步之间靠这几个模块级变量传
    Private PendingInstaller As String = ""
    Private PendingSha256 As String = ""
    Private PendingVersion As String = ""

    ''' <summary>
    ''' 下载并安装，进度显示在右下角的任务列表里。
    ''' <para>
    ''' 三步交给 <see cref="LoaderCombo"/>：解析地址 → 下载 → 运行安装器并等注册表。
    ''' 装完之前不会返回，调用方自己在后台线程等它。
    ''' </para>
    ''' </summary>
    Public Function DownloadAndInstall() As LoaderCombo(Of Integer)
        If IsRunning Then Throw New Exception("Deepseek CE 正在运行，请先关闭再安装")

        Dim Resolve As New LoaderTask(Of Integer, List(Of NetFile))("解析安装包地址", AddressOf PrepareDownload) With {.ProgressWeight = 1}
        Dim Fetch As New LoaderDownload("下载 Deepseek CE", New List(Of NetFile)) With {.ProgressWeight = 8}
        Dim Install As New LoaderTask(Of Integer, Integer)("安装 Deepseek CE", AddressOf RunInstaller) With {.ProgressWeight = 2}

        Dim Combo As New LoaderCombo(Of Integer)("安装 Deepseek CE", New List(Of LoaderBase) From {Resolve, Fetch, Install})
        Combo.Start()
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Return Combo
    End Function

    ''' <summary>第一步：问 Release 要地址与摘要，产出待下载的文件列表。</summary>
    Private Sub PrepareDownload(Task As LoaderTask(Of Integer, List(Of NetFile)))
        PendingInstaller = InstallerPath
        DirectoryUtils.Create(PathUtils.RemoveLastPart(PendingInstaller))
        '残留的半个文件会被当成断点续传起点，最后「下载完成」但内容不全
        If FileUtils.Exists(PendingInstaller) Then FileUtils.Delete(PendingInstaller)
        PendingSha256 = ""
        PendingVersion = ""

        Dim Release As ReleaseInfo = FetchLatest()
        PendingVersion = Release.Version
        Dim AssetUrl As String
        If Release.CanDownload Then
            PendingSha256 = Release.AssetSha256
            AssetUrl = Release.AssetUrl
            Logger.Info($"Deepseek CE 最新版：{Release.Version}（{Release.AssetName}）")
        Else
            Throw New Exception($"没有找到 Deepseek CE 的安装包附件：{If(Release.Err, "Release 里没有 -setup.exe")}")
        End If

        '镜像之间速度差很大，先测速再从快到慢依次尝试
        Dim Sources As String() = Feed.RankSourcesBySpeed(Feed.BuildSources(AssetUrl))
        If Sources Is Nothing OrElse Sources.Length = 0 Then Throw New Exception("没有可用的下载地址")
        Logger.Info($"开始下载 Deepseek CE 安装包，候选源 {Sources.Length} 个")

        Task.Output = New List(Of NetFile) From {
            New NetFile(Sources, PendingInstaller, New FileChecker With {.MinSize = 1024 * 1024})}
    End Sub

    ''' <summary>
    ''' 第三步：校验摘要，静默运行安装器，并等注册表里出现登记项。
    ''' <para>
    ''' 不能只看安装器进程退出：NSIS 安装器常常先起一个子进程就返回，
    ''' 主进程退出并不代表装完了。注册表键才是「装好了」的可靠信号，
    ''' 所以这里以它为判据，最多等一分钟。
    ''' </para>
    ''' </summary>
    Private Sub RunInstaller(Task As LoaderTask(Of Integer, Integer))
        '有摘要就校验：下载会经过第三方镜像，没有摘要等于信任链断了
        If PendingSha256 <> "" Then
            Dim Actual As String = Feed.GetFileSha256(PendingInstaller)
            If Not String.Equals(Actual, PendingSha256, StringComparison.OrdinalIgnoreCase) Then
                FileUtils.Delete(PendingInstaller)
                Logger.Warn($"Deepseek CE 校验失败：期望 {PendingSha256}，实际 {Actual}")
                Throw New Exception("安装包校验失败，文件可能已损坏或被镜像替换，请稍后重试")
            End If
            Logger.Info($"Deepseek CE 安装包 SHA-256 校验通过：{Actual}")
        Else
            Logger.Warn("本次下载没有可用的 SHA-256 摘要，跳过完整性校验")
        End If

        If Not FileUtils.Exists(PendingInstaller) Then Throw New Exception("安装包不见了，请重新下载")

        Logger.Info($"开始运行 Deepseek CE 安装器（版本 {PendingVersion}）")
        Dim Proc As Process = Nothing
        Try
            '带界面运行：安装路径、是否创建快捷方式这些交给用户自己决定，
            '硬套静默参数会让安装位置脱离用户的预期。
            Proc = StartProcess(New ProcessStartInfo(PendingInstaller) With {.UseShellExecute = True})
        Catch ex As Exception
            Throw New Exception($"无法启动安装程序：{ex.Message}", ex)
        End Try
        Logger.Info($"安装器已启动（PID {If(Proc Is Nothing, "未知", Proc.Id.ToString())}），等待注册表登记")

        '判定装完的唯一依据是注册表出现登记项，而不是安装器进程退出：
        'NSIS 安装器常常先起一个子进程就返回，主进程退出不代表装完了。
        '超时从「安装器进程退出」那一刻开始算一分钟——用户点向导要花多久是他自己的事，
        '但安装器已经退出却迟迟没有登记项，就说明这次安装没成功。
        Dim ExitTick As Long = 0
        Dim StartTick As Long = GetTimeMs()
        Do While True
            If IsInstalled Then
                Logger.Info($"Deepseek CE 已安装，版本 {InstalledVersion}")
                Task.Progress = 1
                Try
                    FileUtils.Delete(PendingInstaller)
                Catch ex As Exception
                    Logger.Warn(ex, "删除 Deepseek CE 安装包失败")
                End Try
                Return
            End If
            If ExitTick = 0 AndAlso Proc IsNot Nothing Then
                Try
                    If Proc.HasExited Then ExitTick = GetTimeMs()
                Catch ex As Exception
                    '进程句柄失效（已经退出）时按退出处理
                    ExitTick = GetTimeMs()
                End Try
            End If
            If ExitTick > 0 AndAlso GetTimeMs() - ExitTick > 60000 Then Exit Do
            '安装器一直在跑也不能无限等，给一个十分钟的总上限兜底
            If GetTimeMs() - StartTick > 600000 Then Exit Do
            Thread.Sleep(500)
        Loop

        Logger.Warn("等待 Deepseek CE 安装完成超时")
        Throw New Exception("安装超时：安装结束后一分钟内没有检测到安装完成的注册表项。" & vbCrLf &
                            "如果安装程序还在等你操作，完成后再点一次「安装」即可重新检测。")
    End Sub

#End Region

#Region "卸载与启动"

    ''' <summary>
    ''' 卸载。静默执行，并等注册表项消失（同样最多一分钟）。
    ''' </summary>
    Public Function Uninstall() As LoaderTask(Of Integer, Integer)
        Dim Task As New LoaderTask(Of Integer, Integer)("卸载 Deepseek CE", AddressOf UninstallCore)
        Task.Start()
        Return Task
    End Function

    Private Sub UninstallCore(Task As LoaderTask(Of Integer, Integer))
        If Not IsInstalled Then Throw New Exception("Deepseek CE 没有安装")

        Dim Command As String = If(ReadRegistry("UninstallString"), "").Trim()
        If Command = "" Then Throw New Exception("读不到卸载命令，请到 Windows 的「应用和功能」里手动卸载")

        'UninstallString 常带引号，也可能带参数；拆出 exe 与参数分别处理
        Dim Exe As String = Command
        Dim Args As String = ""
        If Command.StartsWithF("""") Then
            Dim EndQuote As Integer = Command.IndexOf(""""c, 1)
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

        Logger.Info($"运行 Deepseek CE 卸载程序：{Exe} {Args}")
        Try
            StartProcess(New ProcessStartInfo(Exe) With {.UseShellExecute = True, .Arguments = Args})
        Catch ex As Exception
            Throw New Exception($"无法启动卸载程序：{ex.Message}", ex)
        End Try

        Dim Deadline As Long = GetTimeMs() + 60000
        Do While GetTimeMs() < Deadline
            If Not IsInstalled Then
                Logger.Info("Deepseek CE 已卸载")
                Return
            End If
            Thread.Sleep(500)
        Loop
        Throw New Exception("卸载超时：一分钟内没有检测到卸载完成。可能卸载程序正在等待你确认。")
    End Sub

    ''' <summary>
    ''' 启动客户端。参数见 <see cref="BuildArguments"/>。
    ''' </summary>
    Public Sub Launch(Arguments As String)
        Dim Exe As String = FindAppExe()
        If Exe = "" Then Throw New Exception("找不到 Deepseek CE 主程序，请重新安装")
        Logger.Info($"启动 Deepseek CE：{Exe} {Arguments}")
        StartProcess(New ProcessStartInfo(Exe) With {.UseShellExecute = True, .Arguments = Arguments})
    End Sub

#End Region

#Region "命令行参数"

    ''' <summary>
    ''' 一次「把内容递给客户端」的请求。
    ''' </summary>
    Public Class LaunchRequest
        ''' <summary>要发给 AI 的文本；空串表示不带文本。</summary>
        Public Text As String = ""
        ''' <summary>要作为附件发过去的文件，可以为空。</summary>
        Public Files As New List(Of String)
        ''' <summary>
        ''' 是否启用网络搜索。Nothing 表示不传这个开关，由客户端自己决定。
        ''' </summary>
        Public Search As Boolean? = True
        ''' <summary>
        ''' 是否启用深度思考。Nothing 表示不传这个开关，由客户端自己决定。
        ''' </summary>
        Public Thinking As Boolean? = True
        ''' <summary>是否直接发送。False 时只在客户端里填好内容，由用户自己按发送。</summary>
        Public Send As Boolean = False
    End Class

    ''' <summary>
    ''' 拼命令行：<c>--search/--unsearch</c>、<c>--thinking/--unthinking</c>、
    ''' <c>--text &lt;文本&gt;</c>、逐个 <c>--file &lt;路径&gt;</c>、最后按需加 <c>--send</c>。
    ''' <para>
    ''' 搜索与思考开关可以不传（见 <see cref="LaunchRequest.Search"/>），
    ''' 这样「只发一张图」这类请求就不会顺带改变客户端的模式。
    ''' </para>
    ''' <para>
    ''' 文本与路径一律加引号并把内部的引号转义，否则带空格或特殊字符的参数会被拆开。
    ''' </para>
    ''' </summary>
    Public Function BuildArguments(Request As LaunchRequest) As String
        Dim Parts As New List(Of String)
        '只发图片这类请求不需要动搜索与思考开关，留空就不传
        If Request.Search.HasValue Then Parts.Add(If(Request.Search.Value, "--search", "--unsearch"))
        If Request.Thinking.HasValue Then Parts.Add(If(Request.Thinking.Value, "--thinking", "--unthinking"))
        If Request.Text <> "" Then
            Parts.Add("--text")
            Parts.Add(QuoteArgument(Request.Text))
        End If
        For Each FilePath In Request.Files
            If String.IsNullOrWhiteSpace(FilePath) Then Continue For
            Parts.Add("--file")
            Parts.Add(QuoteArgument(FilePath))
        Next
        If Request.Send Then Parts.Add("--send")
        Return String.Join(" ", Parts)
    End Function

    ''' <summary>给参数加引号并转义内部引号。</summary>
    Private Function QuoteArgument(Value As String) As String
        Dim Quote As String = Chr(34)
        Return Quote & Value.Replace(Quote, Quote & Quote) & Quote
    End Function

#End Region

End Module
