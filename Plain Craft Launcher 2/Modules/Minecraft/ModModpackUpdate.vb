Imports System.Collections.Concurrent

''' <summary>
''' 整合包来源信息。记录在版本文件夹的 PCL\config.json 里，
''' 用于判断这个版本是不是从资源站下载的整合包，以及它对应哪个工程、哪个包版本。
''' </summary>
Public Class ModpackSource

    ''' <summary>来源平台，取值为 ResourcePlatforms 的名称（CurseForge / Modrinth）。</summary>
    Public Property Platform As String = ""
    ''' <summary>工程 ID。</summary>
    Public Property ProjectId As String = ""
    ''' <summary>工程名（显示用）。</summary>
    Public Property ProjectName As String = ""
    ''' <summary>整合包版本 ID（CurseForge 是 fileID，Modrinth 是 version id）。</summary>
    Public Property VersionId As String = ""
    ''' <summary>整合包版本号（显示用）。</summary>
    Public Property VersionName As String = ""
    ''' <summary>安装时对应的 Minecraft 版本。只在这个版本下检查更新。</summary>
    Public Property GameVersion As String = ""
    ''' <summary>该整合包版本的发布时间，用于判断谁更新。</summary>
    Public Property VersionDate As Date = Date.MinValue
    ''' <summary>工程页面链接。</summary>
    Public Property Url As String = ""

End Class

''' <summary>
''' 整合包自动更新：来源记录、静默检查、更新流程。
''' </summary>
Public Module ModModpackUpdate

#Region "来源记录"

    '版本配置文件里存放来源信息的键名
    Private Const ConfigKey As String = "ModpackSource"

    '按配置文件路径共享的 Provider：同一个 config.json 必须只有一个实例，
    '否则两边各自缓存一份 JSON，谁的 Save 后落盘就把对方写入的键覆盖掉。
    Private ReadOnly Providers As New ConcurrentDictionary(Of String, JsonConfigProvider)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>取某个版本 PCL\config.json 的共享配置提供器。</summary>
    Friend Function GetProvider(VersionFolder As String) As JsonConfigProvider
        Dim ConfigPath As String = PathUtils.AddSlashSuffix(VersionFolder) & "PCL\config.json"
        Return Providers.GetOrAdd(ConfigPath, Function(Path) New JsonConfigProvider(Path))
    End Function

    ''' <summary>读取某个版本记录的整合包来源；不是整合包时返回 Nothing。</summary>
    Public Function ReadSource(VersionFolder As String) As ModpackSource
        If String.IsNullOrWhiteSpace(VersionFolder) Then Return Nothing
        Try
            Dim Json As JObject = GetProvider(VersionFolder).Read(Of JObject)(ConfigKey, Nothing, False)
            If Json Is Nothing Then Return Nothing
            Dim Result = Json.ToObject(Of ModpackSource)()
            If Result Is Nothing OrElse String.IsNullOrWhiteSpace(Result.ProjectId) Then Return Nothing
            Return Result
        Catch ex As Exception
            Logger.Warn(ex, $"读取整合包来源失败（{VersionFolder}）")
            Return Nothing
        End Try
    End Function

    ''' <summary>把整合包来源写进版本的 PCL\config.json，保留文件里已有的其它配置。</summary>
    Public Sub WriteSource(VersionFolder As String, Source As ModpackSource)
        If Source Is Nothing Then Return
        Try
            Dim Provider As JsonConfigProvider = GetProvider(VersionFolder)
            Provider.Set(ConfigKey, JObject.FromObject(Source), False)
            Provider.Save()
            Logger.Info($"已记录整合包来源：{Source.ProjectName} {Source.VersionName}（{VersionFolder}）")
        Catch ex As Exception
            Logger.Warn(ex, $"记录整合包来源失败（{VersionFolder}）")
        End Try
    End Sub

#End Region

#Region "更新状态缓存"

    ''' <summary>
    ''' 整合包版本的显示文本。
    ''' <para>
    ''' CurseForge 的文件没有独立的版本号字段（解析时 Version 恒为 Nothing），
    ''' 只有 displayName 可读，所以这里统一回退到 Display，避免拼出一句「有新的版本：」后面什么都没有。
    ''' </para>
    ''' </summary>
    Public Function VersionText(V As ResourceVersion) As String
        If V Is Nothing Then Return ""
        If Not String.IsNullOrWhiteSpace(V.Version) Then Return V.Version
        If Not String.IsNullOrWhiteSpace(V.Display) Then Return V.Display
        Return "未知版本"
    End Function

    ''' <summary>整合包相对远端的状态。</summary>
    Public Enum PackUpdateStates
        ''' <summary>还没检查过。</summary>
        Unknown = 0
        ''' <summary>正在检查。</summary>
        Checking = 1
        ''' <summary>已经是最新的（或远端不再提供这个游戏版本）。</summary>
        Latest = 2
        ''' <summary>有新的整合包版本。</summary>
        Outdated = 3
        ''' <summary>检查失败，可以重试。</summary>
        Failed = 4
    End Enum

    ''' <summary>某个版本的整合包检查结果。</summary>
    Public Class PackUpdateResult
        Public State As PackUpdateStates = PackUpdateStates.Unknown
        ''' <summary>可更新时对应的最新版本。</summary>
        Public Latest As ResourceVersion = Nothing
    End Class

    '以版本文件夹（规范化后）为键
    Private ReadOnly UpdateResults As New ConcurrentDictionary(Of String, PackUpdateResult)

    ''' <summary>取某个版本的检查结果，没有则返回 Unknown。</summary>
    Public Function GetUpdateResult(VersionFolder As String) As PackUpdateResult
        Dim Result As PackUpdateResult = Nothing
        If UpdateResults.TryGetValue(NormalizeKey(VersionFolder), Result) AndAlso Result IsNot Nothing Then Return Result
        Return New PackUpdateResult()
    End Function

    Private Function NormalizeKey(VersionFolder As String) As String
        Return PathUtils.ForCompare(VersionFolder).TrimEnd("\")
    End Function

    Private Sub SetResult(VersionFolder As String, State As PackUpdateStates, Optional Latest As ResourceVersion = Nothing)
        Dim Result As New PackUpdateResult With {.State = State, .Latest = Latest}
        UpdateResults(NormalizeKey(VersionFolder)) = Result
    End Sub

    ''' <summary>这个版本是不是有待更新的整合包。</summary>
    Public Function HasUpdate(VersionFolder As String) As Boolean
        Return GetUpdateResult(VersionFolder).State = PackUpdateStates.Outdated
    End Function

#End Region

#Region "检查"

    ''' <summary>启动后静默检查所有整合包。只跑一次，失败不影响使用。</summary>
    Public Sub CheckAllInBackground()
        If IsCheckingAll Then Return
        IsCheckingAll = True
        RunInThread(
        Sub()
            Try
                '等版本列表就绪，否则一个版本都拿不到
                Dim Waited As Integer = 0
                Do While McInstanceList Is Nothing OrElse Not McInstanceList.Any() OrElse McInstanceListLoader.State = LoadState.Loading
                    Thread.Sleep(500)
                    Waited += 1
                    If Waited > 120 Then Return '最多等一分钟
                Loop
                Dim Instances As New List(Of McInstance)
                For Each Card In McInstanceList.Values
                    For Each Instance In Card
                        If Instance.State = McInstance.McInstanceState.Error Then Continue For
                        If Instance.ModpackSource Is Nothing Then Continue For
                        Instances.Add(Instance)
                    Next
                Next
                If Not Instances.Any() Then
                    Logger.Info("没有需要检查更新的整合包")
                    Return
                End If
                Logger.Info($"开始检查 {Instances.Count} 个整合包的更新")
                For Each Instance In Instances
                    Try
                        CheckOneInternal(Instance)
                    Catch ex As Exception
                        Logger.Warn(ex, $"检查整合包更新失败（{Instance.Name}）")
                    End Try
                Next
                '标识可能从「整合包」变成「可更新」，一律重画一次
                RunInUi(Sub() RefreshInstanceList())
            Catch ex As Exception
                Logger.Warn(ex, "检查整合包更新时出错")
            Finally
                IsCheckingAll = False
            End Try
        End Sub)
    End Sub
    Private IsCheckingAll As Boolean = False

    ''' <summary>单个整合包的用户主动检查。会在界面上给出结果提示。</summary>
    Public Sub CheckOne(Instance As McInstance, Optional ShowHint As Boolean = True)
        If Instance Is Nothing OrElse Instance.ModpackSource Is Nothing Then Return
        Dim Source As ModpackSource = Instance.ModpackSource
        If ShowHint Then Hint($"正在检查 {Instance.Name} 的更新……", HintType.Blue, False)
        SetResult(Instance.PathVersion, PackUpdateStates.Checking)
        RefreshInstanceList()
        RunInThread(
        Sub()
            Dim Outdated As Boolean = False
            Dim Message As String = ""
            Try
                Outdated = CheckOneInternal(Instance)
                Message = If(Outdated,
                    $"{Instance.Name} 有新的整合包版本：{VersionText(GetUpdateResult(Instance.PathVersion).Latest)}",
                    $"{Instance.Name} 已经是最新的整合包版本")
            Catch ex As Exception
                Logger.Warn(ex, $"检查整合包更新失败（{Instance.Name}）")
                Message = $"检查 {Instance.Name} 的更新失败：{ex.Message}"
                SetResult(Instance.PathVersion, PackUpdateStates.Failed)
            End Try
            RunInUi(
            Sub()
                RefreshInstanceList()
                If Not ShowHint Then Return
                '查到新版本就直接把更新确认摆出来：只丢一句提示，用户还得再点一次按钮才知道要干什么
                If Outdated AndAlso GetUpdateResult(Instance.PathVersion).Latest IsNot Nothing Then
                    StartUpdate(Instance)
                Else
                    Hint(Message, HintType.Blue)
                End If
            End Sub)
        End Sub)
    End Sub

    ''' <summary>
    ''' 检查一个整合包并写回结果，返回状态是否发生了变化。必须在工作线程调用。
    ''' </summary>
    Private Function CheckOneInternal(Instance As McInstance) As Boolean
        Dim Source As ModpackSource = Instance.ModpackSource
        Dim Latest As ResourceVersion = FindLatest(Source)
        Dim After As PackUpdateStates
        If Latest Is Nothing Then
            '远端已经没有这个游戏版本的包了，按“没有新版本”处理
            After = PackUpdateStates.Latest
        ElseIf SameFile(Latest.Id, Source.VersionId) Then
            After = PackUpdateStates.Latest
        Else
            '整合包普遍没有版本号，没法比大小：同游戏版本下发布时间最新的那个就是最新版，
            '它不是当前装的那个文件，就说明有更新。
            After = PackUpdateStates.Outdated
        End If
        SetResult(Instance.PathVersion, After, If(After = PackUpdateStates.Outdated, Latest, Nothing))
        Logger.Info($"整合包更新检查：{Instance.Name} → {After}（当前 {Source.VersionName}，远端 {VersionText(Latest)}）")
        '返回「有没有新版本」，不是「状态有没有变化」：
        '调用方拿它决定要不要弹更新确认，从未知变最新也会让状态变化，但那不代表有更新。
        Return After = PackUpdateStates.Outdated
    End Function

    ''' <summary>比较两个平台文件 ID，忽略空白与大小写。</summary>
    Private Function SameFile(A As String, B As String) As Boolean
        Return String.Equals(If(A, "").Trim(), If(B, "").Trim(), StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' 找到与当前游戏版本对应的最新整合包版本。
    ''' <para>
    ''' 只认同一个 Minecraft 版本：整合包如果跳到别的游戏版本，就不算这个整合包的新版本。
    ''' </para>
    ''' </summary>
    Public Function FindLatest(Source As ModpackSource) As ResourceVersion
        If Source Is Nothing Then Return Nothing
        Dim Platform As ResourcePlatforms = PlatformOf(Source.Platform)
        If Not Platform.Flags.IsSingle Then Return Nothing
        Dim Versions As List(Of ResourceVersion) = ResourceVersion.FromProjectId(Source.ProjectId, Platform, LoadDependencies:=False)
        Dim GameVersion As String = Source.GameVersion
        '整合包普遍不填版本号，能用来排序的只有发布时间，
        '所以「同游戏版本下发布时间最新的那个文件」就是最新版。
        Dim Matched = Versions.
            Where(Function(V) String.IsNullOrWhiteSpace(GameVersion) OrElse
                V.GameVersions.Any(Function(G) String.Equals(G, GameVersion, StringComparison.OrdinalIgnoreCase))).
            OrderByDescending(Function(V) V.ReleaseDate).
            ToList()
        If Not Matched.Any() Then
            '清单里的游戏版本写法可能和本地记录不一致（作者改过、带后缀等），
            '退回整份文件列表，宁可多报一次也不要漏报。
            Logger.Warn($"整合包 {Source.ProjectName} 没有匹配 {GameVersion} 的文件，改用全部文件判断最新版")
            Matched = Versions.OrderByDescending(Function(V) V.ReleaseDate).ToList()
        End If
        If Not Matched.Any() Then Return Nothing
        Dim Latest As ResourceVersion = Matched(0)
        Logger.Info($"整合包 {Source.ProjectName}：候选 {Matched.Count} 个文件，最新为 {VersionText(Latest)}（{Latest.Id}，发布于 {Latest.ReleaseDate:yyyy-MM-dd}）")
        Return Latest
    End Function

    ''' <summary>把平台名转成枚举，无法识别时返回一个多值标志（调用方应当放弃）。</summary>
    Private Function PlatformOf(Name As String) As ResourcePlatforms
        Select Case Name
            Case "CurseForge" : Return ResourcePlatforms.CurseForge
            Case "Modrinth" : Return ResourcePlatforms.Modrinth
            Case Else : Return ResourcePlatforms.Any '多值标志，调用方会用 IsSingle 判断并放弃
        End Select
    End Function

    ''' <summary>重画版本列表，让标识与按钮状态跟上。</summary>
    Private Sub RefreshInstanceList()
        Try
            '不判断当前页：检查结果可能在别的页面上拿到，列表必须重画才看得到新标识
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        Catch ex As Exception
            Logger.Warn(ex, "刷新版本列表失败")
        End Try
    End Sub

#End Region

#Region "更新流程"

    ''' <summary>执行一次整合包更新。会先确认，再建立任务。</summary>
    Public Sub StartUpdate(Instance As McInstance)
        Dim Source As ModpackSource = Instance.ModpackSource
        If Source Is Nothing Then Return
        Dim Latest As ResourceVersion = GetUpdateResult(Instance.PathVersion).Latest
        If Latest Is Nothing Then
            Hint("没有可用的整合包新版本，先检查一次更新", HintType.Red)
            Return
        End If
        If McInstanceLock.IsLocked(Instance) Then
            Hint("该版本正在被修改，请稍后再更新！", HintType.Red)
            Return
        End If
        Dim ConfirmText As String =
            "是否更新整合包？这可能导致您的游戏损坏，并且如果有不规范的模组将存档数据存储在了 config 目录这也会导致存档数据的丢失，" &
            "您自行添加的游戏模组也会被覆盖，可以自行备份游戏文件。"
        If MyMsgBox(ConfirmText, $"整合包最新版本:{VersionText(Latest)}", "确认", "取消", IsWarn:=True) <> 1 Then Return

        Dim VersionFolder As String = Instance.PathVersion
        Dim GameVersion As String = Source.GameVersion
        If String.IsNullOrWhiteSpace(GameVersion) Then GameVersion = Instance.Version.VanillaName

        '下载到的安装包放在任务临时目录，流程结束时连同解压目录一起清掉
        Dim WorkFolder As String = RequestTaskTempFolder()
        Dim PackFile As String = WorkFolder & "update." & If(Source.Platform = "CurseForge", "zip", "mrpack")
        Dim ExtractFolder As String = WorkFolder & "extract\"
        '新模组先下到临时目录的 mods 子目录里，全部就绪后才动游戏目录
        Dim StagingRoot As String = WorkFolder & "staging\"
        '新版配置同样先解压到暂存区，最后统一覆盖
        Dim OverrideStage As String = WorkFolder & "overrides\"

        Dim Loaders As New List(Of LoaderBase)
        '1. 下载新的安装包
        Loaders.Add(New LoaderDownload("下载新的安装包",
            {Latest.ToNetFile(PackFile, ResourceVersion.DownloadReason.Update, GameVersion, Latest.ModLoaders)}) With
            {.ProgressWeight = Math.Max(3, Latest.Size / 1024 / 1024 / 6), .Block = True})
        '2. 解压安装包，准备模组清单与配置暂存区（此时不动游戏文件）
        Loaders.Add(New LoaderTask(Of Integer, List(Of NetFile))("解析新的安装包",
            Sub(Task)
                Task.Output = PrepareUpdate(Task, PackFile, ExtractFolder, StagingRoot, OverrideStage, GameVersion)
            End Sub) With {.ProgressWeight = 3, .Block = True})
        '3. 下载新的模组到暂存区
        Loaders.Add(New LoaderDownload("下载新的模组", New List(Of NetFile)) With {.ProgressWeight = 6, .Block = True})
        '4. 全部下载成功后，才替换模组与配置
        Loaders.Add(New LoaderTask(Of Integer, Integer)("覆盖模组与配置",
            Sub(Task)
                ApplyUpdate(Task, VersionFolder, StagingRoot & "mods\", OverrideStage, ExtractFolder)
            End Sub) With {.ProgressWeight = 3, .Block = True})
        '5. 更新模组加载器（如果新版换了加载器版本）
        Loaders.Add(New LoaderTask(Of Integer, Integer)("更新模组加载器",
            Sub(Task)
                UpdateModLoader(Instance, ExtractFolder, GameVersion, Task)
            End Sub) With {.ProgressWeight = 8, .Block = True})
        '6. 收尾：写入新来源、清缓存
        Loaders.Add(New LoaderTask(Of Integer, Integer)("整理整合包更新",
            Sub(Task)
                Dim NewSource As New ModpackSource With {
                    .Platform = Source.Platform,
                    .ProjectId = Source.ProjectId,
                    .ProjectName = Source.ProjectName,
                    .VersionId = Latest.Id,
                    .VersionName = VersionText(Latest),
                    .GameVersion = GameVersion,
                    .VersionDate = Latest.ReleaseDate,
                    .Url = Source.Url}
                WriteSource(VersionFolder, NewSource)
                SetResult(VersionFolder, PackUpdateStates.Latest)
                '来源变了，实例里缓存的旧值必须作废
                Instance.InvalidateModpackSource()
                Try
                    DirectoryUtils.Delete(WorkFolder)
                Catch ex As Exception
                    Logger.Warn(ex, "清理整合包更新临时文件失败")
                End Try
            End Sub) With {.ProgressWeight = 0.1, .Show = False})

        Dim LoaderName As String = $"整合包更新：{Instance.Name} "
        If LoaderTaskbar.Any(Function(L) L.Name = LoaderName) Then
            Hint("该整合包正在更新中！", HintType.Red)
            Return
        End If

        '不能用 McInstallState：它在失败时会删掉 Loader.Input，而这里 Input 就是版本文件夹本身，
        '一旦下载失败就会连存档一起清掉。更新只允许清理自己的临时目录。
        Dim Combo As New LoaderCombo(Of String)(LoaderName, Loaders) With
            {.OnStateChanged = Sub(L) UpdateState(L, WorkFolder)}
        '和「修改版本」一样先占住版本锁，避免更新过程中被别处并发改写
        Dim Worker As New RedoableWorker(Sub() Combo.WaitForExit(VersionFolder))
        If Not McInstanceLock.TryAcquire(Worker, VersionFolder) Then
            Hint("该版本正在被修改，请稍后再更新！", HintType.Red)
            Try
                DirectoryUtils.Delete(WorkFolder)
            Catch
            End Try
            Return
        End If
        AddHandler Worker.Stopped, Sub() RunInUi(Sub() LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\"))
        Worker.Start()
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.PageChange(FormMain.PageType.TaskManager)
    End Sub

    ''' <summary>
    ''' 更新任务的状态回调。
    ''' <para>
    ''' 刻意不复用 McInstallState：那套逻辑失败时会删除 Loader.Input，
    ''' 对整合包更新来说 Input 是版本文件夹，删掉就连存档一起没了。
    ''' 这里只清理本次更新自己的临时目录。
    ''' </para>
    ''' </summary>
    Private Sub UpdateState(Loader As LoaderBase, WorkFolder As String)
        RunInUi(
        Sub()
            Try
                Select Case Loader.State
                    Case LoadState.Finished
                        Hint($"{Loader.Name}成功！", HintType.Green)
                    Case LoadState.Failed
                        Hint($"{Loader.Name}失败：{Loader.Error.GetDisplay(False)}", HintType.Red)
                    Case LoadState.Canceled
                        Hint($"{Loader.Name}已取消，游戏文件未改动", HintType.Blue)
                    Case Else
                        Return
                End Select
                '只在没成功时清理临时目录；成功时由收尾步骤自己清
                If Loader.State <> LoadState.Finished Then
                    RunInThread(
                    Sub()
                        Try
                            Thread.Sleep(1000) '等文件句柄释放
                            DirectoryUtils.Delete(WorkFolder)
                        Catch ex As Exception
                            Logger.Warn(ex, "清理整合包更新临时目录失败")
                        End Try
                    End Sub)
                End If
                LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Catch ex As Exception
                Logger.Warn(ex, "处理整合包更新状态失败")
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 解压新包，生成模组下载清单（目标指向暂存区），并把配置复制到暂存区。
    ''' <para>
    ''' 这一步完全不碰游戏目录：模组要等全部下载成功后才替换，
    ''' 否则下载失败会留下一个被清空 mods 却装不回模组的版本。
    ''' </para>
    ''' </summary>
    Private Function PrepareUpdate(Task As LoaderTask(Of Integer, List(Of NetFile)),
                                   PackFile As String, ExtractFolder As String,
                                   StagingRoot As String, OverrideStage As String,
                                   GameVersion As String) As List(Of NetFile)
        'CurseForge 分支会自己拼 mods 子目录，所以暂存根目录就是父级
        Dim StagingMods As String = StagingRoot & "mods\"
        DirectoryUtils.Delete(ExtractFolder)
        DirectoryUtils.Delete(StagingRoot)
        DirectoryUtils.Delete(OverrideStage)
        FileUtils.ExtractToDirectory(PackFile, ExtractFolder, Task.CreateCancellationToken(),
                                     Task.CreateSyncProgressProvider(ToPercentage:=0.5))
        Dim BaseFolder As String = FindPackRoot(ExtractFolder)
        DirectoryUtils.Create(StagingMods)
        Task.Progress = 0.6
        '配置先整份复制到暂存区，真正覆盖放到下载全部成功之后
        Dim OverrideFolders As New List(Of String)
        CollectOverrideFolders(BaseFolder, ExtractFolder, OverrideFolders)
        Dim StagedFiles As Integer = 0
        For Each OverrideFolder In OverrideFolders
            If Not DirectoryUtils.Exists(OverrideFolder) Then Continue For
            For Each Relative In EnumerateOverrideFiles(OverrideFolder)
                If IsProtectedPath(Relative) Then Continue For
                Try
                    Dim TargetFile As String = OverrideStage & Relative
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(TargetFile))
                    FileUtils.Copy(OverrideFolder & Relative, TargetFile)
                    StagedFiles += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"暂存整合包配置失败：{Relative}")
                End Try
            Next
        Next
        Logger.Info($"整合包更新：已暂存 {StagedFiles} 个配置文件")
        Task.Progress = 0.75
        '按新清单生成模组下载信息，目标全在暂存区
        Dim Files As New List(Of NetFile)
        Dim Manifest As JObject = ReadPackManifest(BaseFolder, ExtractFolder)
        If Manifest Is Nothing Then Return Files
        If Manifest("files") Is Nothing Then Return Files
        Dim TargetVersion As New McVersion(GameVersion)
        For Each Loader In InstanceModLoaders(Manifest)
            If Loader.Key = ModLoaders.None Then Continue For
            TargetVersion.ModLoaderVersions(Loader.Key) = Loader.Value
        Next
        If MeaningItIsCurseForge(Manifest) Then
            'CurseForge 清单里只有 fileID，必须批量换成下载地址：一个文件一次请求会被限流
            Dim FileIds As New List(Of String)
            For Each Entry In Manifest("files")
                If Entry("required") IsNot Nothing AndAlso Not Entry("required").ToObject(Of Boolean) Then
                    Logger.Info($"跳过可选模组：fileID {Entry("fileID")}")
                    Continue For
                End If
                Dim FileId As String = If(Entry("fileID"), "").ToString
                If FileId <> "" Then FileIds.Add(FileId)
            Next
            Files.AddRange(BuildCurseForgeNetFiles(FileIds, StagingMods, GameVersion, TargetVersion))
        Else
            'Modrinth 清单里直接给了下载地址
            For Each Entry In Manifest("files")
                Try
                    'env 里声明不支持的项（例如仅服务端 Mod）不该装到客户端
                    If Entry("env") IsNot Nothing AndAlso Entry("env")("client") IsNot Nothing Then
                        Dim Client As String = Entry("env")("client").ToString
                        If Client = "unsupported" OrElse Client = "optional" Then Continue For
                    End If
                    Dim RelativePath As String = Entry("path").ToString.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    '只保留落在 mods 下的项，其余由 overrides 负责
                    If Not RelativePath.Replace("\", "/").StartsWithF("mods/", True) Then Continue For
                    Dim TargetPath As String = Path.Combine(StagingMods, PathUtils.GetLastPart(RelativePath))
                    Dim Urls = Entry("downloads").SelectMany(Function(U) ResourceVersion.ParseCurseForgeDownloadUrls(U.ToString)).ToList
                    Urls = Urls.Distinct().ToList()
                    Dim Hash As String = Nothing
                    If Entry("hashes") IsNot Nothing AndAlso Entry("hashes")("sha1") IsNot Nothing Then Hash = Entry("hashes")("sha1").ToString
                    Files.Add(New NetFile(Urls, TargetPath, New FileChecker With {.Hash = Hash}, True))
                Catch ex As Exception
                    Logger.Warn(ex, "解析 Modrinth 模组清单失败")
                End Try
            Next
        End If
        Logger.Info($"整合包更新：新清单共 {Files.Count} 个模组待下载")
        Return Files
    End Function

    ''' <summary>
    ''' 全部下载完成后才动游戏目录：替换 mods、覆盖配置、重建版本隔离设置。
    ''' </summary>
    Private Sub ApplyUpdate(Task As LoaderTask(Of Integer, Integer), VersionFolder As String,
                            StagingMods As String, OverrideStage As String, ExtractFolder As String)
        VersionFolder = PathUtils.AddSlashSuffix(VersionFolder)
        Dim C As CancellationToken = Task.CreateCancellationToken()
        '1. 换掉模组：把新的整个搬过去，旧的整体删掉
        Dim ModsFolder As String = VersionFolder & "mods\"
        Dim NewMods As String = VersionFolder & "mods.new\"
        Dim OldMods As String = VersionFolder & "mods.old\"
        DirectoryUtils.Delete(NewMods)
        DirectoryUtils.Delete(OldMods)
        If DirectoryUtils.Exists(StagingMods) Then DirectoryUtils.Copy(StagingMods, NewMods)
        C.ThrowIfCancellationRequested()
        Task.Progress = 0.3
        '先改名腾位，再删除旧的：中途失败也不会出现「mods 已清空但新模组还没到位」
        If DirectoryUtils.Exists(ModsFolder) Then DirectoryUtils.Move(ModsFolder, OldMods)
        If DirectoryUtils.Exists(NewMods) Then DirectoryUtils.Move(NewMods, ModsFolder)
        C.ThrowIfCancellationRequested()
        Task.Progress = 0.6
        '2. 覆盖配置
        Dim Applied As Integer = 0
        If DirectoryUtils.Exists(OverrideStage) Then
            For Each SourceFile In DirectoryUtils.EnumerateFiles(OverrideStage, includeSubDirectories:=True).ToList()
                Dim Relative As String = SourceFile.Substring(OverrideStage.Length)
                If IsProtectedPath(Relative) Then Continue For
                Dim TargetFile As String = VersionFolder & Relative
                If Not PathUtils.IsParentOf(VersionFolder, TargetFile) Then Continue For
                Try
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(TargetFile))
                    FileUtils.Copy(SourceFile, TargetFile)
                    Applied += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"覆盖配置失败：{TargetFile}")
                End Try
            Next
        End If
        '3. 恢复版本隔离，避免整合包自带的 Setup.ini 把它关掉导致存档跑到公共目录
        Dim VersionIni As String = VersionFolder & "PCL\Setup.ini"
        Try
            WriteIni(VersionIni, "VersionArgumentIndie", 1)
            WriteIni(VersionIni, "VersionArgumentIndieV2", True)
            IniClearCache(VersionIni)
        Catch ex As Exception
            Logger.Warn(ex, "恢复版本隔离设置失败")
        End Try
        Task.Progress = 0.9
        '4. 旧模组放到回收站，出问题还能捞回来
        Try
            If DirectoryUtils.Exists(OldMods) Then DirectoryUtils.Delete(OldMods, toRecycleBin:=True)
        Catch ex As Exception
            Logger.Warn(ex, "清理旧模组失败")
        End Try
        Logger.Info($"整合包更新：已覆盖 {Applied} 个配置文件，模组已替换")
    End Sub

    ''' <summary>
    ''' 新版整合包如果换了模组加载器版本，就地把加载器换掉。
    ''' <para>
    ''' 不复用「修改版本」的整套安装流程：那要先重新下载原版 JSON 与资源，
    ''' 只是换个加载器版本却要依赖 Mojang 元数据可达，代价和风险都不成比例。
    ''' 这里只取新加载器的 profile JSON，合并进现有版本 JSON，再补下新增的库文件。
    ''' </para>
    ''' </summary>
    Private Sub UpdateModLoader(Instance As McInstance, ExtractFolder As String, GameVersion As String,
                                Task As LoaderTask(Of Integer, Integer))
        Dim Manifest As JObject = ReadPackManifest(FindPackRoot(ExtractFolder), ExtractFolder)
        If Manifest Is Nothing Then Return
        Dim TargetLoaders As Dictionary(Of ModLoaders, String) = InstanceModLoaders(Manifest)
        If Not TargetLoaders.Any() Then Return
        Dim Current As McComponents = Nothing
        Try
            Current = Configs.McInstallManifest.Get(Instance.Config)?.Components
        Catch ex As Exception
            Logger.Warn(ex, "读取当前版本的组件信息失败")
        End Try
        If Current Is Nothing Then
            Logger.Info("版本没有组件清单，跳过加载器更新")
            Return
        End If
        '逐个比较，找出版本号不同的加载器
        Dim Changed As New Dictionary(Of ModLoaders, String)
        For Each Loader In TargetLoaders
            Dim Old As String = Nothing
            If Not Current.ModLoaderVersions.TryGetValue(Loader.Key, Old) Then Old = ""
            If Not String.Equals(Old, Loader.Value, StringComparison.OrdinalIgnoreCase) Then Changed(Loader.Key) = Loader.Value
        Next
        If Not Changed.Any() Then
            Logger.Info("整合包更新：模组加载器版本没有变化")
            Return
        End If
        For Each Loader In Changed
            Dim Old As String = Nothing
            If Not Current.ModLoaderVersions.TryGetValue(Loader.Key, Old) Then Old = "无"
            Logger.Info($"整合包更新：模组加载器 {Loader.Key} 将由 {Old} 更新至 {Loader.Value}")
        Next
        If Changed.Count <> 1 OrElse Not Changed.ContainsKey(ModLoaders.Fabric) Then
            'Forge / NeoForge 的 profile 来自各自的安装器，合并规则也更复杂，暂时只记录不处理
            Logger.Warn($"整合包更新：暂不支持自动切换加载器 {Changed.Keys.Join("、")}，请手动在版本设置中修改")
            Hint("整合包换了 " & Changed.Keys.Join("、") & " 加载器版本，已跳过自动切换，请到版本设置里手动修改", HintType.Red)
            Return
        End If

        Dim FabricVersion As String = Changed(ModLoaders.Fabric)
        Task.Progress = 0.15
        '1. 只下载新加载器的 profile JSON
        Dim WorkFolder As String = RequestTaskTempFolder()
        Dim ProfileFolder As String = WorkFolder & "loader\"
        Dim ProfilePath As String = ProfileFolder & "fabric-loader-" & FabricVersion & "-" & GameVersion & ".json"
        Try
            DirectoryUtils.Create(ProfileFolder)
            Dim ProfileLoader As New LoaderDownload("下载新的加载器信息", {
                New NetFile({
                    "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/" & GameVersion & "/" & FabricVersion & "/profile/json",
                    "https://meta.fabricmc.net/v2/versions/loader/" & GameVersion & "/" & FabricVersion & "/profile/json"
                }, ProfilePath, New FileChecker With {.IsJson = True})})
            ProfileLoader.WaitForExit(Nothing, Task, c:=Task.CreateCancellationToken(),
                                      p:=Task.CreateSyncProgressProvider(FromPercentage:=0.2, ToPercentage:=0.5))
            Task.Progress = 0.5
            '2. 合并进现有版本 JSON
            MergeLoaderProfile(Instance, ProfilePath)
            Task.Progress = 0.65
            '3. 补下加载器新增的库文件（含主 jar，缺的才会真的下载）
            Dim NewInstance As McInstance = New McInstance(Instance.PathVersion)
            NewInstance.ResetSettingsCache()
            NewInstance.Load()
            Dim LibFiles As List(Of NetFile) = McLibNetFilesFromInstance(NewInstance)
            If LibFiles.Any() Then
                Dim LibLoader As New LoaderDownload("下载新的加载器库文件", LibFiles)
                LibLoader.WaitForExit(Nothing, Task, c:=Task.CreateCancellationToken(),
                                      p:=Task.CreateSyncProgressProvider(FromPercentage:=0.7, ToPercentage:=0.95))
            End If
            Task.Progress = 0.97
            '4. 更新组件清单
            Dim NewLoaders As New Dictionary(Of ModLoaders, String)(Current.ModLoaderVersions)
            For Each Pair In Changed
                NewLoaders(Pair.Key) = Pair.Value
            Next
            Dim OldFiles = If(Configs.McInstallManifest.Get(Instance.Config)?.ManagedFiles, {})
            Dim NewComponents As New McComponents(Current.MinecraftVersion, NewLoaders, Current.OptiFineEntry,
                                                  Current.FabricApiVersion, Current.OptiFabricVersion)
            Configs.McInstallManifest.Set(New McInstallManifest(Versions.McInstallManifestVersion, NewComponents, Date.UtcNow, OldFiles), Instance.Config)
            Instance.Config.Save()
            Instance.ResetSettingsCache()
            Logger.Info("整合包更新：模组加载器已更新")
        Finally
            Try
                DirectoryUtils.Delete(WorkFolder)
            Catch ex As Exception
                Logger.Warn(ex, "清理加载器更新临时目录失败")
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' 把加载器 profile 合并进现有版本 JSON。
    ''' <para>
    ''' 以现有 JSON 为底：它已经是完整的（整合包安装时就把原版合并进来了），
    ''' 加载器的 mainClass、库与参数覆盖上去即可。绝不能保留 profile 里的
    ''' inheritsFrom ——整合包版本通常没有对应的原版文件夹，写了就会解析失败，
    ''' 整个版本从列表里消失。
    ''' </para>
    ''' </summary>
    Private Sub MergeLoaderProfile(Instance As McInstance, ProfilePath As String)
        Dim MainJsonPath As String = Instance.GetJsonPath()
        If Not FileUtils.Exists(MainJsonPath) Then Throw New Exception("找不到当前版本的 JSON，无法更新加载器")
        Dim CurrentJson As JObject = FileUtils.ReadAsJson(MainJsonPath)
        If CurrentJson Is Nothing Then Throw New Exception("当前版本的 JSON 无法解析，无法更新加载器")
        Dim ProfileJson As JObject = FileUtils.ReadAsJson(ProfilePath)
        If ProfileJson Is Nothing Then Throw New Exception("下载到的加载器信息无法解析")

        '合并本体：现有 JSON 为底，profile 覆盖主类等字段
        Dim Merged As JObject = CType(CurrentJson.DeepClone(), JObject)
        For Each Key In {"mainClass", "arguments", "minecraftArguments", "minimumLauncherVersion"}
            If ProfileJson(Key) IsNot Nothing Then Merged(Key) = ProfileJson(Key).DeepClone()
        Next
        '库列表：加载器的库排在前面（同名类优先用加载器的），再补上原有的
        Dim ProfileLibs As JArray = TryCast(ProfileJson("libraries"), JArray)
        Dim CurrentLibs As JArray = TryCast(CurrentJson("libraries"), JArray)
        Dim MergedLibs As New JArray()
        Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If ProfileLibs IsNot Nothing Then
            For Each Library In ProfileLibs
                Dim LibraryName As String = If(Library("name")?.ToString(), "")
                If LibraryName = "" OrElse Not Seen.Add(LibraryName) Then Continue For
                MergedLibs.Add(Library.DeepClone())
            Next
        End If
        If CurrentLibs IsNot Nothing Then
            For Each Library In CurrentLibs
                Dim LibraryName As String = If(Library("name")?.ToString(), "")
                If LibraryName = "" OrElse Not Seen.Add(LibraryName) Then Continue For
                MergedLibs.Add(Library.DeepClone())
            Next
        End If
        Merged("libraries") = MergedLibs
        '这两项只在「以原版为父版本」时才有意义，整合包版本里必须去掉
        Merged.Remove("inheritsFrom")
        Merged.Remove("jar")
        Merged.Remove("releaseTime")
        Merged.Remove("time")
        Merged("id") = Instance.Name
        FileUtils.WriteAtomic(MainJsonPath, Merged.ToString())
        Logger.Info($"整合包更新：已把新加载器合并进版本 JSON（{MainJsonPath}，库 {MergedLibs.Count} 项）")
    End Sub

    ''' <summary>列出整合包里所有可能存放配置的目录。</summary>
    Private Sub CollectOverrideFolders(BaseFolder As String, ExtractFolder As String, Result As List(Of String))
        Dim Manifest As JObject = ReadPackManifest(BaseFolder, ExtractFolder)
        'CurseForge 的 overrides 目录名写在清单里（多数是 overrides），Modrinth 固定是 overrides 与 client-overrides
        Dim Declared As String = If(Manifest?("overrides")?.ToString(), "").Trim()
        If Declared <> "" AndAlso Declared <> "." AndAlso Declared <> "./" Then
            Result.Add(PathUtils.AddSlashSuffix(BaseFolder & Declared.TrimEnd("/"c)))
        End If
        Result.Add(PathUtils.AddSlashSuffix(BaseFolder & "overrides"))
        Result.Add(PathUtils.AddSlashSuffix(BaseFolder & "client-overrides"))
        '去重：清单里声明的名字常常就是 overrides
        Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For i = Result.Count - 1 To 0 Step -1
            If Not Seen.Add(PathUtils.ForCompare(Result(i))) Then Result.RemoveAt(i)
        Next
    End Sub

    ''' <summary>列出某个配置目录下所有文件的相对路径。</summary>
    Private Iterator Function EnumerateOverrideFiles(OverrideFolder As String) As IEnumerable(Of String)
        For Each File In DirectoryUtils.EnumerateFiles(OverrideFolder, includeSubDirectories:=True)
            Yield File.Substring(OverrideFolder.Length)
        Next
    End Function

#End Region

    ''' <summary>存档与整合包来源记录不允许被整合包覆盖。</summary>
    Private Function IsProtectedPath(RelativePath As String) As Boolean
        Dim Normalized As String = RelativePath.Replace("\", "/").TrimStart("/")
        If Normalized.StartsWithF("saves/", True) Then Return True
        If Normalized.Equals("PCL/config.json", StringComparison.OrdinalIgnoreCase) Then Return True
        Return False
    End Function

    ''' <summary>找到解压目录里真正放着 manifest 的那一层。</summary>
    Private Function FindPackRoot(ExtractFolder As String) As String
        ExtractFolder = PathUtils.AddSlashSuffix(ExtractFolder)
        If FileUtils.Exists(ExtractFolder & "manifest.json") OrElse FileUtils.Exists(ExtractFolder & "modrinth.index.json") Then Return ExtractFolder
        For Each Folder In DirectoryUtils.EnumerateDirectories(ExtractFolder)
            Dim Sub1 As String = PathUtils.AddSlashSuffix(Folder)
            If FileUtils.Exists(Sub1 & "manifest.json") OrElse FileUtils.Exists(Sub1 & "modrinth.index.json") Then Return Sub1
        Next
        Return ExtractFolder
    End Function

    Private Function ReadPackManifest(BaseFolder As String, ExtractFolder As String) As JObject
        For Each Name In {"manifest.json", "modrinth.index.json"}
            Dim Path As String = BaseFolder & Name
            If Not FileUtils.Exists(Path) Then Continue For
            Try
                Return TryCast(FileUtils.ReadAsJson(Path), JObject)
            Catch ex As Exception
                Logger.Warn(ex, $"读取整合包清单失败：{Path}")
            End Try
        Next
        Return Nothing
    End Function

    ''' <summary>Modrinth 清单没有 manifest.json 的 files 结构，用文件里的键区分。</summary>
    Private Function MeaningItIsCurseForge(Manifest As JObject) As Boolean
        Return Manifest("minecraft") IsNot Nothing OrElse Manifest("files")?.First?("fileID") IsNot Nothing
    End Function

    ''' <summary>从清单里读出加载器版本，用于给 Modrinth 下载地址打追踪参数。</summary>
    Private Function InstanceModLoaders(Manifest As JObject) As Dictionary(Of ModLoaders, String)
        Dim Result As New Dictionary(Of ModLoaders, String)
        Try
            If Manifest("minecraft") IsNot Nothing AndAlso Manifest("minecraft")("modLoaders") IsNot Nothing Then
                For Each Entry In Manifest("minecraft")("modLoaders")
                    Dim Id As String = If(Entry("id"), "").ToString.Lower
                    If Id.StartsWithF("forge-") Then
                        Result(ModLoaders.Forge) = Id.Replace("forge-", "")
                    ElseIf Id.StartsWithF("neoforge-") Then
                        Result(ModLoaders.NeoForge) = Id.Replace("neoforge-", "")
                    ElseIf Id.StartsWithF("fabric-") Then
                        Result(ModLoaders.Fabric) = Id.Replace("fabric-", "")
                    End If
                Next
            End If
            If Manifest("dependencies") IsNot Nothing Then
                For Each Entry As JProperty In Manifest("dependencies")
                    Select Case Entry.Name.Lower
                        Case "forge" : Result(ModLoaders.Forge) = Entry.Value.ToString
                        Case "neoforge", "neo-forge" : Result(ModLoaders.NeoForge) = Entry.Value.ToString
                        Case "fabric-loader" : Result(ModLoaders.Fabric) = Entry.Value.ToString
                    End Select
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取整合包加载器信息失败")
        End Try
        Return Result
    End Function

    'CurseForge 的文件查询接口一次最多接受 1000 个 ID，分批更稳妥
    Private Const CurseForgeBatchSize As Integer = 500

    ''' <summary>把一批 CurseForge fileID 批量换成下载信息。接口会跳过已删除的文件，缺失项直接舍弃。</summary>
    Private Function BuildCurseForgeNetFiles(FileIds As List(Of String), VersionFolder As String,
                                             GameVersion As String, TargetVersion As McVersion) As List(Of NetFile)
        Dim Result As New List(Of NetFile)
        If Not FileIds.Any() Then Return Result
        Dim Distinct = FileIds.Distinct().ToList()
        For Offset As Integer = 0 To Distinct.Count - 1 Step CurseForgeBatchSize
            Dim Batch = Distinct.Skip(Offset).Take(CurseForgeBatchSize).ToList()
            Dim Response As JObject
            Try
                Response = DlModRequest("https://api.curseforge.com/v1/mods/files", HttpMethod.Post,
                                        "{""fileIds"": [" & Batch.Join(",") & "]}", "application/json")
            Catch ex As Exception
                Logger.Warn(ex, $"批量获取 CurseForge 文件信息失败（{Batch.Count} 项）")
                Continue For
            End Try
            If Response("data") Is Nothing Then Continue For
            For Each ModJson As JObject In Response("data")
                Try
                    Dim NetFileItem As NetFile = CurseForgeJsonToNetFile(ModJson, VersionFolder, GameVersion, TargetVersion)
                    If NetFileItem IsNot Nothing Then Result.Add(NetFileItem)
                Catch ex As Exception
                    Logger.Warn(ex, "解析 CurseForge 模组信息失败")
                End Try
            Next
        Next
        Dim Missing As Integer = Distinct.Count - Result.Count
        If Missing > 0 Then Logger.Warn($"整合包更新：有 {Missing} 个模组在 CurseForge 上已不可用，已跳过")
        Return Result
    End Function

    ''' <summary>
    ''' 把 CurseForge 的文件 JSON 转成下载信息。整合包安装与更新共用这一处判断资源类型。
    ''' </summary>
    Friend Function CurseForgeJsonToNetFile(ModJson As JObject, VersionFolder As String,
                                            GameVersion As String, TargetVersion As McVersion) As NetFile
        Dim TargetFolder As String, Type As ResourceTypes
        If ModJson("modules").Any Then
            Dim ModuleNames = CType(ModJson("modules"), JArray).Select(Function(L) L("name").ToString).ToList
            If ModuleNames.Contains("META-INF") OrElse ModuleNames.Contains("mcmod.info") OrElse ModuleNames.Contains("fabric.mod.json") OrElse
                ModJson?("FileName")?.ToString.EndsWithF(".jar", True) Then
                TargetFolder = "mods" : Type = ResourceTypes.Mod
            ElseIf ModuleNames.Contains("pack.mcmeta") Then
                TargetFolder = "resourcepacks" : Type = ResourceTypes.ResourcePack
            ElseIf ModuleNames.Contains("shaders") Then
                TargetFolder = "shaderpacks" : Type = ResourceTypes.Shader
            Else
                TargetFolder = "saves" : Type = ResourceTypes.Map
            End If
        Else
            TargetFolder = "mods" : Type = ResourceTypes.Mod
        End If
        Dim File = ResourceVersion.FromPlatformJson(ModJson, Type)
        If Not File.DownloadAvailable Then Return Nothing
        Return File.ToNetFile(PathUtils.AddSlashSuffix(Path.Combine(VersionFolder, TargetFolder)),
            ResourceVersion.DownloadReason.ModPack, GameVersion, TargetVersion.ModLoaders)
    End Function


End Module
