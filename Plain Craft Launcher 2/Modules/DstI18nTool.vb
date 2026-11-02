Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression

''' <summary>
''' DST MC I18n Tool（汉化工具）的本地集成。
''' <para>
''' 这是一个独立的小程序，用来扫描整合包里的 mods / KubeJS / FTB Quests 语言文件并生成汉化。
''' 它体积大（自包含发布，解开后约 100MB、三百多个文件），所以不随启动器分发，
''' 而是按需从 GitHub 的独立仓库下载，启动器这边只负责「装在哪、怎么启动、怎么装回来」。
''' </para>
''' <para>
''' 分层约定：这个模块只管外部程序的安装位置与进程生命周期，
''' 不掺任何界面逻辑；卡片与快捷方式按钮都调这里。
''' </para>
''' </summary>
Friend Module DstI18nTool

    ''' <summary>发布这个副程序的仓库。启动器从它的最新 Release 取下载地址。</summary>
    Public Const RepoOwner As String = "xiaoqiezi-china"
    Public Const RepoName As String = "DST-i18nPlatform"

    ''' <summary>Release 里附件的文件名。改了这里也要改打包脚本。</summary>
    Public Const AssetName As String = "DST-MC-I18n-Tool.zip"

    ''' <summary>安装目录（以 \ 结尾）。放在 PCL 下，跟其他本地数据保持一致。</summary>
    Public ReadOnly Property InstallFolder As String
        Get
            Return Paths.Base & "PCL\I18nTool\"
        End Get
    End Property

    ''' <summary>主程序路径。</summary>
    Public ReadOnly Property ExePath As String
        Get
            Return InstallFolder & ExeName
        End Get
    End Property

    Public Const ExeName As String = "DST-MC-I18n-Tool.exe"

    ''' <summary>该仓库的 Release 数据源。取版本、展开镜像、算摘要都走这一层。</summary>
    Private ReadOnly Feed As New ReleaseFeed(RepoOwner, RepoName)

    ''' <summary>
    ''' 手动指定仓库，形如 <c>owner/repo</c>；留空则用内置仓库。
    ''' </summary>
    ''' <remarks>
    ''' 允许被 config.ini 的 [Modules] I18nToolRepo 覆盖，
    ''' 这样换仓库时不用重新发版。
    ''' </remarks>
    Public ReadOnly Property RepoOverride As String
        Get
            Return ModConfigFile.Read(ModConfigFile.SectionModules, "I18nToolRepo").Trim()
        End Get
    End Property

    ''' <summary>
    ''' 直链下载地址；留空则按 Release 自动取。
    ''' <para>
    ''' 这是 API 被限流或仓库临时不可用时的逃生口：填了就只下这个地址，
    ''' 并且**跳过摘要校验**（因为拿不到 Release 声明的摘要）。
    ''' </para>
    ''' </summary>
    Public ReadOnly Property DirectUrl As String
        Get
            Return ModConfigFile.Read(ModConfigFile.SectionModules, "I18nToolUrl").Trim()
        End Get
    End Property

    ''' <summary>实际使用的数据源：配置里指定了仓库就用它，否则用内置仓库。</summary>
    Private Function GetFeed() As ReleaseFeed
        Dim Override_ As String = RepoOverride
        If Override_ = "" Then Return Feed
        Dim Parts As String() = Override_.Split("/"c)
        If Parts.Length <> 2 OrElse Parts(0).Trim() = "" OrElse Parts(1).Trim() = "" Then
            Logger.Warn($"config.ini 里的 I18nToolRepo 格式不对（应为 owner/repo）：{Override_}")
            Return Feed
        End If
        Return New ReleaseFeed(Parts(0).Trim(), Parts(1).Trim())
    End Function

    ''' <summary>本机是否已经装好了这个程序。</summary>
    Public ReadOnly Property IsInstalled As Boolean
        Get
            Return FileUtils.Exists(ExePath)
        End Get
    End Property

    ''' <summary>当前正在运行的实例；Nothing 表示没开。</summary>
    Private Running As Process = Nothing
    Private ReadOnly RunningLock As New Object

    ''' <summary>是否正在运行。</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            SyncLock RunningLock
                If Running Is Nothing Then Return False
                Try
                    If Running.HasExited Then
                        Running = Nothing
                        Return False
                    End If
                    Return True
                Catch
                    Running = Nothing
                    Return False
                End Try
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' 启动工具。
    ''' </summary>
    ''' <param name="WorkFolder">要打开的工作目录，即含 mods / kubejs 的那个文件夹。传空则由用户在网页里自己选。</param>
    ''' <param name="Port">监听端口。传 0 或负数表示让程序自己随机挑。</param>
    ''' <returns>实际使用的地址，形如 http://127.0.0.1:51234/；失败时抛异常。</returns>
    Public Function StartTool(WorkFolder As String, Port As Integer) As String
        If Not IsInstalled Then Throw New Exception("还没有安装汉化工具")
        If IsRunning Then Throw New Exception("汉化工具已经在运行了")

        '工作目录必须存在，否则程序会把它当成非法目录直接拒绝
        If WorkFolder <> "" AndAlso Not DirectoryUtils.Exists(WorkFolder) Then
            Throw New Exception("工作目录不存在：" & WorkFolder)
        End If

        Dim Args As New Text.StringBuilder
        If WorkFolder <> "" Then
            '路径里可能有空格，必须加引号
            Args.Append("--path ").Append("""").Append(WorkFolder.TrimEnd("\"c)).Append(""" ")
        End If
        If Port > 0 Then Args.Append("--port ").Append(Port).Append(" ")

        Dim Info As New ProcessStartInfo With {
            .FileName = ExePath,
            .Arguments = Args.ToString.Trim(),
            .WorkingDirectory = InstallFolder,
            .UseShellExecute = False,
            .CreateNoWindow = True
        }
        Logger.Info($"正在启动汉化工具：{ExePath} {Info.Arguments}")

        Dim Proc As Process
        Try
            Proc = Process.Start(Info)
        Catch ex As Exception
            Throw New Exception($"启动汉化工具失败：{ex.Message}", ex)
        End Try
        If Proc Is Nothing Then Throw New Exception("启动汉化工具失败：进程没有起来")

        SyncLock RunningLock
            Running = Proc
        End SyncLock

        '程序自己会打开浏览器；这里返回地址只是给界面显示用。
        '端口是 0 时实际端口只有程序自己知道，所以显示成「由程序随机选择」。
        If Port > 0 Then Return $"http://127.0.0.1:{Port}/"
        Return "http://127.0.0.1/（端口由程序随机选择，请看它的控制台窗口）"
    End Function

    ''' <summary>关掉正在运行的实例。没在运行时什么都不做。</summary>
    Public Sub StopTool()
        SyncLock RunningLock
            If Running Is Nothing Then Return
            Try
                If Not Running.HasExited Then Running.Kill()
            Catch ex As Exception
                Logger.Warn(ex, "关闭汉化工具失败")
            End Try
            Running = Nothing
        End SyncLock
    End Sub

    ''' <summary>
    ''' 下载并安装，进度显示在右下角的任务列表里。
    ''' <para>
    ''' 流程拆成三步交给 <see cref="LoaderCombo"/>：解析地址 → 下载 → 校验解压。
    ''' 必须走组合加载器而不是在这里直接 <c>WaitForExit</c>——后者不会把任务
    ''' 注册进 <see cref="LoaderTaskbar"/>，用户在界面上就看不到任何进度，
    ''' 只能干等着，不知道是在下载还是卡死了。
    ''' </para>
    ''' </summary>
    ''' <returns>已启动的组合加载器。调用方不要再自己 Start，也不要在 UI 线程上等它。</returns>
    Public Function DownloadAndInstall() As LoaderCombo(Of Integer)
        '正在运行的 exe 是锁着的，替换目录必然失败；在这里挡住比让用户看到一堆 IOException 好
        If IsRunning Then Throw New Exception("汉化工具正在运行，请先关闭再重新下载")

        Dim Download As New LoaderTask(Of Integer, List(Of NetFile))("解析下载地址", AddressOf PrepareDownload) With {.ProgressWeight = 2}
        Dim Fetch As New LoaderDownload("下载汉化工具", New List(Of NetFile)) With {.ProgressWeight = 8}
        Dim Install As New LoaderTask(Of Integer, Integer)("解压并安装汉化工具", AddressOf InstallDownloaded) With {.ProgressWeight = 2}

        Dim Combo As New LoaderCombo(Of Integer)("安装汉化工具", New List(Of LoaderBase) From {Download, Fetch, Install})
        Combo.Start()
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Return Combo
    End Function

    ''' <summary>下载完成后落在哪，以及要不要校验摘要。第一步与第三步之间靠这几个模块级变量传。</summary>
    Private PendingZip As String = ""
    Private PendingSha256 As String = ""

    ''' <summary>
    ''' 第一步：问 Release 要地址与摘要，产出待下载的文件列表。
    ''' <para>
    ''' 注意 LoaderTask 的委托是 <c>Action(Of LoaderTask)</c>，**没有返回值**：
    ''' 结果必须自己赋给 <c>Task.Output</c>。写成 Function 让返回值传出去是不生效的，
    ''' 下游会拿到 Nothing，于是下载器立刻「完成」、一个字节都没下。
    ''' </para>
    ''' </summary>
    Private Sub PrepareDownload(Task As LoaderTask(Of Integer, List(Of NetFile)))
        PendingZip = PathTemp & "I18nTool\package.zip"
        DirectoryUtils.Create(PathUtils.RemoveLastPart(PendingZip))
        '固定路径会残留上次的半个文件，被当成断点续传的起点，最后「下载完成」但内容不全
        If FileUtils.Exists(PendingZip) Then FileUtils.Delete(PendingZip)
        PendingSha256 = ""

        Dim Sources As String()
        Dim Direct As String = DirectUrl
        If Direct <> "" Then
            '用户显式指定了直链：尊重它，但没有摘要可校验
            Logger.Info($"汉化工具使用配置里的直链：{Direct}")
            Sources = GetFeed().BuildSources(Direct)
        Else
            Dim CurrentFeed As ReleaseFeed = GetFeed()
            '先试 API：能拿到就顺带拿到摘要，可以校验内容。
            '拿不到也不算失败——API 有匿名限流（走系统代理时尤其容易被别人的配额拖累），
            '而 releases/latest/download/<固定附件名> 是纯跳转、不吃配额，只要附件名固定就能用。
            Dim Release As ReleaseInfo = CurrentFeed.FetchLatest(".zip")
            Dim AssetUrl As String
            If Release.CanDownload Then
                PendingSha256 = Release.AssetSha256
                AssetUrl = Release.AssetUrl
                Logger.Info($"汉化工具最新版：{Release.Version}（{Release.AssetName}）")
            Else
                AssetUrl = CurrentFeed.LatestAssetUrl(AssetName)
                Logger.Info($"查询 API 没拿到地址（{If(Release.Err, "没有 zip 附件")}），改用 latest 跳转：{AssetUrl}")
            End If
            '镜像之间速度差很大，先测速再从快到慢依次尝试
            Sources = CurrentFeed.RankSourcesBySpeed(CurrentFeed.BuildSources(AssetUrl))
        End If
        If Sources Is Nothing OrElse Sources.Length = 0 Then Throw New Exception("没有可用的下载地址")

        Logger.Info($"开始下载汉化工具，候选源 {Sources.Length} 个")
        '必须显式写进 Output：LoaderCombo 靠它把文件列表交给下一个加载器
        Task.Output = New List(Of NetFile) From {
            New NetFile(Sources, PendingZip, New FileChecker With {.MinSize = 8 * 1024 * 1024})}
    End Sub

    ''' <summary>第三步：校验摘要、确认包结构、解压并替换安装目录。</summary>
    Private Sub InstallDownloaded(Task As LoaderTask(Of Integer, Integer))
        Dim TempZip As String = PendingZip

        '有摘要就校验。下载会经过第三方镜像，没有摘要等于信任链断了；
        '没有摘要时不能静默放行，至少留下记录
        If PendingSha256 <> "" Then
            Dim Actual As String = GetFeed().GetFileSha256(TempZip)
            If Not String.Equals(Actual, PendingSha256, StringComparison.OrdinalIgnoreCase) Then
                FileUtils.Delete(TempZip)
                Logger.Warn($"汉化工具校验失败：期望 {PendingSha256}，实际 {Actual}")
                Throw New Exception("汉化工具校验失败，文件可能已损坏或被镜像替换，请稍后重试")
            End If
            Logger.Info($"汉化工具 SHA-256 校验通过：{Actual}")
        Else
            Logger.Warn("本次下载没有可用的 SHA-256 摘要，跳过完整性校验")
        End If

        '解压前先确认这是个能读的 zip，并且里面确实有主程序。
        '这一步挡掉截断包与镜像返回的错误页——它们大小可能过得了 MinSize，
        '但解压时才炸，那时候已经浪费了用户一次下载。
        Dim EntryCount As Integer = 0
        Try
            Using Archive As ZipArchive = ZipFile.OpenRead(TempZip)
                EntryCount = Archive.Entries.Count
                Dim HasExe As Boolean = Archive.Entries.Any(
                    Function(E) E.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                If Not HasExe Then Throw New Exception("下载到的压缩包里没有 " & ExeName & "，可能不是汉化工具")
            End Using
        Catch ex As InvalidDataException
            FileUtils.Delete(TempZip)
            Throw New Exception("下载到的压缩包已损坏（可能被镜像替换），请稍后重试", ex)
        End Try
        Logger.Info($"压缩包检查通过，共 {EntryCount} 个条目")

        '先解到临时目录，确认里面有 exe 再替换正式目录，
        '避免下到一半的文件把已经装好的那份冲掉
        Dim TempDir As String = PathTemp & "I18nTool\unpack\"
        If DirectoryUtils.Exists(TempDir) Then DirectoryUtils.Delete(TempDir)
        DirectoryUtils.Create(TempDir)
        Logger.Info("正在解压汉化工具")
        FileUtils.ExtractToDirectory(TempZip, TempDir)

        '压缩包里可能多套了一层目录，往下找一层
        Dim Found As String = FindExe(TempDir)
        If Found = "" Then Throw New Exception("下载到的压缩包里没有找到 " & ExeName)
        Dim SrcDir As String = PathUtils.RemoveLastPart(Found)

        If DirectoryUtils.Exists(InstallFolder) Then DirectoryUtils.Delete(InstallFolder)
        DirectoryUtils.Create(InstallFolder)
        DirectoryUtils.Copy(SrcDir, InstallFolder)
        Logger.Info($"汉化工具已安装到 {InstallFolder}")

        Try
            FileUtils.Delete(TempZip)
            DirectoryUtils.Delete(TempDir)
        Catch ex As Exception
            Logger.Warn(ex, "清理汉化工具临时文件失败")
        End Try
    End Sub

    ''' <summary>在目录里递归找主程序，返回完整路径；找不到返回空串。</summary>
    Private Function FindExe(Folder As String) As String
        Try
            For Each One In DirectoryUtils.EnumerateFiles(Folder, True)
                If PathUtils.GetLastPart(One).Equals(ExeName, StringComparison.OrdinalIgnoreCase) Then Return One
            Next
        Catch ex As Exception
            Logger.Warn(ex, "查找汉化工具主程序失败")
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' 该给这个版本用哪个工作目录。
    ''' <para>
    ''' 工具要的是「含 mods / kubejs 的那个目录」：
    ''' 开了版本隔离就是版本文件夹，没开就是 .minecraft 根目录。
    ''' 这个判断和启动器实际启动游戏时用的是同一套（<see cref="McInstance.PathIndie"/>），
    ''' 不能自己另写一份，否则会和玩家实际生效的目录对不上。
    ''' </para>
    ''' </summary>
    Public Function WorkFolderOf(Instance As McInstance) As String
        If Instance Is Nothing Then Return ""
        Return Instance.PathIndie
    End Function

End Module
