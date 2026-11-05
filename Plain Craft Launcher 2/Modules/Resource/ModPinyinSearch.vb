Imports System.IO

''' <summary>
''' 「通用拼音搜索」：在版本里装 Just Enough Characters（简称 JEC），
''' 让物品搜索支持拼音首字母与全拼。
''' <para>
''' 它和 <see cref="ModHanhua"/> 是同一类东西——都是「启动前把某个体验补丁装好」，
''' 但两者判断方式完全不同：
''' </para>
''' <para>
''' 汉化资源包按 MC 版本映射到一个固定文件名，所以能靠文件名判断装没装；
''' 而拼音搜索是个普通模组，文件名随版本号变化（jecharacters-1.20.1-fabric-4.6.12.jar），
''' 没法靠文件名精确匹配。因此这里**只做快速模糊匹配**：扫一遍 mods 目录的文件名，
''' 看有没有包含 JEC 的常见写法，不打开 jar、不解析 modid、不读元数据。
''' </para>
''' <para>
''' 这个取舍是刻意的：启动流程里不能为了判断「装没装」去解压几百个 jar，
''' 那会让每次启动都卡上几秒。文件名匹配漏判的代价是「重复下载一次」，
''' 而真正下载时会走 PCL 现成的重复文件检查，不会真的装两份。
''' </para>
''' </summary>
Public Module ModPinyinSearch

    ''' <summary>
    ''' 拼音搜索模组在 Modrinth 上的项目 ID。
    ''' <para>
    ''' 选 Modrinth 而不是 CurseForge：CF 的接口需要 API Key，开源版默认没有；
    ''' Modrinth 免鉴权，且能直接按 loader + 游戏版本筛选，正好是这里需要的。
    ''' </para>
    ''' </summary>
    Public Const ModrinthProjectId As String = "I7k4B65h"

    ''' <summary>项目主页，用于出错时引导用户手动下载。</summary>
    Public Const ProjectPage As String = "https://modrinth.com/mod/justenoughcharacters"

    ''' <summary>
    ''' 快速判断文件名是不是拼音搜索模组。
    ''' <para>
    ''' 只做小写模糊匹配，覆盖这几种常见写法：
    ''' <c>jecharacters</c>、<c>justenoughcharacters</c>、<c>just-enough-characters</c>、
    ''' <c>just_enough_characters</c>、以及中文名 <c>通用拼音搜索</c> / <c>拼音搜索</c>。
    ''' 去掉分隔符后再匹配，因此 <c>JustEnoughCharacters-1.20.1.jar</c> 与
    ''' <c>just-enough-characters-4.6.jar</c> 都能命中。
    ''' </para>
    ''' </summary>
    Public Function IsPinyinModFile(FileName As String) As Boolean
        If String.IsNullOrEmpty(FileName) Then Return False
        Dim Name As String = FileName.ToLowerInvariant()
        '去掉常见的分隔符，让各种命名风格都能对上
        Dim Compact As String = Name.Replace("-", "").Replace("_", "").Replace(" ", "").Replace(".", "")
        Return Compact.Contains("jecharacters") OrElse
               Compact.Contains("justenoughcharacters") OrElse
               Name.Contains("通用拼音搜索") OrElse
               Name.Contains("拼音搜索")
    End Function

    ''' <summary>
    ''' 当前版本是否已经装了拼音搜索模组。
    ''' <para>
    ''' 只看 mods 目录的文件名，不打开 jar。被禁用的（.jar.disabled / .jar.old）也算装了——
    ''' 用户主动禁用的东西不该被自动重新启用。
    ''' </para>
    ''' </summary>
    Public Function IsInstalled(Instance As McInstance) As Boolean
        If Instance Is Nothing Then Return False
        Dim ModFolder As String = Instance.PathIndie & "mods"
        Try
            If Not DirectoryUtils.Exists(ModFolder) Then Return False
            For Each FilePath In DirectoryUtils.EnumerateFiles(ModFolder, False)
                If IsPinyinModFile(PathUtils.GetLastPart(FilePath)) Then
                    Logger.Info($"检测到拼音搜索模组：{PathUtils.GetLastPart(FilePath)}")
                    Return True
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "检查拼音搜索模组失败")
        End Try
        Return False
    End Function

    ''' <summary>
    ''' 找出与当前版本匹配的拼音搜索模组文件；找不到返回 Nothing。
    ''' <para>
    ''' 用 Modrinth 的筛选接口按 loader 与游戏版本过滤，再取第一个（接口默认按时间倒序，
    ''' 第一个就是最新的正式版）。
    ''' </para>
    ''' </summary>
    Public Function FindMatchingVersion(Instance As McInstance) As ResourceVersion
        If Instance Is Nothing Then Return Nothing
        Dim GameVersion As String = ""
        Dim LoaderName As String = ""
        Try
            GameVersion = Instance.Version.VanillaName
            '按 PCL 的加载器优先级挑一个：模组只认它自己支持的那几种
            If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then
                LoaderName = "fabric"
            ElseIf Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then
                LoaderName = "neoforge"
            ElseIf Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then
                LoaderName = "forge"
            Else
                '纯原版没有可装的加载器版本
                Logger.Info("该版本没有 Forge / Fabric / NeoForge 加载器，跳过拼音搜索模组")
                Return Nothing
            End If
        Catch ex As Exception
            Logger.Warn(ex, "识别实例版本失败，无法匹配拼音搜索模组")
            Return Nothing
        End Try
        If String.IsNullOrEmpty(GameVersion) OrElse GameVersion = "Unknown" Then Return Nothing

        Try
            'Modrinth 的筛选参数是 JSON 数组，需要 URL 编码
            Dim Url As String =
                $"https://api.modrinth.com/v2/project/{ModrinthProjectId}/version" &
                $"?loaders={Uri.EscapeDataString("[""" & LoaderName & """")}" &
                $"&game_versions={Uri.EscapeDataString("[""" & GameVersion & """")}"
            Logger.Info($"查询拼音搜索模组：{GameVersion} / {LoaderName}")
            Dim Raw As String = NetRequestByLoader({Url}, 20000, IsJson:=True)
            Dim Versions As JArray = JArray.Parse(Raw)
            If Versions.Count = 0 Then
                Logger.Info($"没有匹配 {GameVersion} / {LoaderName} 的拼音搜索模组")
                Return Nothing
            End If
            Dim Data As JObject = CType(Versions(0), JObject)
            Dim Result As ResourceVersion = ResourceVersion.FromPlatformJson(Data, ResourceTypes.Mod)
            If Result Is Nothing OrElse Result.FileName = "" Then Return Nothing
            Logger.Info($"匹配到拼音搜索模组：{Result.FileName}")
            Return Result
        Catch ex As Exception
            Logger.Warn(ex, "查询拼音搜索模组失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 启动游戏前的自动安装：设置里选了「汉化 + 拼音搜索」时才做。
    ''' 已经装了就跳过；当前版本没有匹配的模组也只是记一条日志，不打扰用户。
    ''' </summary>
    ''' <param name="Instance">即将启动的版本。</param>
    ''' <param name="WaitForExit">True 时阻塞到下载完成（启动流程里用）。</param>
    Public Function AutoFetch(Instance As McInstance, Optional WaitForExit As Boolean = True) As Boolean
        If Instance Is Nothing Then Return False
        '2 才代表「汉化 + 拼音搜索」
        If Settings.Get(Of Integer)("LaunchArgumentHanhua") <> 2 Then Return False
        If IsInstalled(Instance) Then
            Logger.Info("拼音搜索模组已存在，跳过自动安装")
            Return False
        End If
        Dim Target As ResourceVersion = FindMatchingVersion(Instance)
        If Target Is Nothing Then Return False
        Logger.Info($"自动安装拼音搜索模组：{Target.FileName}")
        Return Start(Instance, Target, WaitForExit)
    End Function

    ''' <summary>
    ''' 把模组装进当前版本的 mods 目录，任务挂在右下角。
    ''' </summary>
    ''' <param name="WaitForExit">True 时阻塞到下载结束，用于启动流程。</param>
    Public Function Start(Instance As McInstance, Target As ResourceVersion, Optional WaitForExit As Boolean = False) As Boolean
        If Instance Is Nothing OrElse Target Is Nothing Then Return False
        Dim ModFolder As String = Instance.PathIndie & "mods\"
        DirectoryUtils.Create(ModFolder)
        Dim OutputPath As String = ModFolder & Target.FileName
        Dim Urls As String() = Target.DownloadUrls.ToArray()
        If Urls.Length = 0 Then
            Logger.Warn("拼音搜索模组没有可用的下载地址")
            Return False
        End If

        Dim Download As New LoaderDownload("下载拼音搜索模组",
            New List(Of NetFile) From {
                New NetFile(Urls, OutputPath, New FileChecker With {.MinSize = 1024 * 10})}) With {.ProgressWeight = 1}
        Dim Combo As New LoaderCombo(Of Integer)("安装拼音搜索模组：" & Instance.Name & " ", New List(Of LoaderBase) From {Download})
        Combo.OnStateChanged =
        Sub()
            Select Case Combo.State
                Case LoadState.Finished
                    Hint("拼音搜索模组已就绪：" & Target.FileName, HintType.Green)
                    RunInUi(Sub()
                                If FrmInstanceMod IsNot Nothing AndAlso
                                   FrmMain.PageCurrent = FormMain.PageType.InstanceSetup AndAlso
                                   FrmMain.PageCurrentSub = FormMain.PageSubType.InstanceMod Then
                                    FrmInstanceMod.ReloadModList(ForceReload:=True)
                                End If
                            End Sub)
                Case LoadState.Failed
                    Hint("下载拼音搜索模组失败：" & Combo.Error.GetDisplay(False), HintType.Red)
                Case LoadState.Canceled
                    Hint("已取消下载拼音搜索模组", HintType.Blue)
                Case Else
                    Return
            End Select
        End Sub
        Combo.Start()
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        If WaitForExit Then
            Do While Combo.State = LoadState.Loading
                Thread.Sleep(100)
            Loop
            If Combo.State <> LoadState.Finished Then
                Throw New Exception("安装拼音搜索模组失败：" & If(Combo.Error Is Nothing, "已取消", Combo.Error.Message), Combo.Error)
            End If
        End If
        Return True
    End Function

End Module
