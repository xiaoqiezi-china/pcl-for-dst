Imports System.Diagnostics
Imports System.IO

''' <summary>
''' DST MC I18n Tool（汉化工具）的本地集成。
''' <para>
''' 这是一个独立的小程序，用来扫描整合包里的 mods / KubeJS / FTB Quests 语言文件并生成汉化。
''' 它体积大（自包含发布，约 100MB、三百多个文件），所以不随启动器分发，
''' 而是按需从 GitHub 的独立仓库下载，启动器这边只负责「装在哪、怎么启动、怎么装回来」。
''' </para>
''' <para>
''' 分层约定：这个模块只管外部程序的安装位置与进程生命周期，
''' 不掺任何界面逻辑；卡片与快捷方式按钮都调这里。
''' </para>
''' </summary>
Friend Module DstI18nTool

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

    ''' <summary>下载地址。</summary>
    ''' <remarks>
    ''' 这个工具是独立仓库里单独发布的，不在启动器的 Release 里。
    ''' 地址允许被 config.ini 的 [Modules] I18nToolUrl 覆盖，
    ''' 这样换仓库或换镜像时不用重新发版。
    ''' </remarks>
    Public ReadOnly Property DownloadUrl As String
        Get
            Dim FromFile As String = ModConfigFile.Read(ModConfigFile.SectionModules, "I18nToolUrl").Trim()
            If FromFile <> "" Then Return FromFile
            Return "https://github.com/xiaoqiezi-china/utlis/releases/latest/download/DST-MC-I18n-Tool.zip"
        End Get
    End Property

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
    ''' 下载并安装。
    ''' </summary>
    ''' <param name="SyncProgress">用于同步进度的加载器，可传 Nothing。</param>
    Public Sub DownloadAndInstall(Optional SyncProgress As LoaderBase = Nothing)
        Dim Url As String = DownloadUrl
        If Url = "" Then Throw New Exception("没有配置汉化工具的下载地址")

        Dim TempZip As String = PathTemp & "I18nTool\package.zip"
        DirectoryUtils.Create(PathUtils.RemoveLastPart(TempZip))
        Logger.Info($"开始下载汉化工具：{Url}")

        Dim Task As New LoaderDownload("下载汉化工具", New List(Of NetFile) From {
            New NetFile({Url}, TempZip, New FileChecker With {.MinSize = 1024 * 1024})}) With {.ProgressWeight = 10}
        Task.WaitForExit(LoaderToSyncProgress:=SyncProgress)

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
