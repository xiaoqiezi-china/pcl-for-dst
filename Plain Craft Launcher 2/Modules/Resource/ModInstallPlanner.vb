''' <summary>
''' 把收藏夹里的一批 Mod 安装到指定版本：分析版本与加载器、递归补齐前置、去重，最后给出清单。
''' 只做分析与计划，不产生任何文件改动；执行交给 <see cref="ModInstallRunner"/>。
''' </summary>
Public Module ModInstallPlanner

    ''' <summary>计划里一条目的状态。</summary>
    Public Enum ItemState
        ''' <summary>可以安装。</summary>
        Ready
        ''' <summary>当前版本里已经有它了。</summary>
        AlreadyInstalled
        ''' <summary>没有适配当前游戏版本与加载器的文件。</summary>
        NoMatchingVersion
        ''' <summary>项目信息获取失败（已删除、网络错误等）。</summary>
        Unavailable
    End Enum

    ''' <summary>计划里的一条。</summary>
    Public Class PlanItem
        Public Platform As ResourcePlatforms
        ''' <summary>项目 ID。可能与 <see cref="Project"/> 为 Nothing 时一起缺失。</summary>
        Public ProjectId As String = ""
        Public Project As ResourceProject = Nothing
        Public Version As ResourceVersion = Nothing
        ''' <summary>是否为被别的 Mod 拉进来的前置。</summary>
        Public IsDependency As Boolean = False
        Public State As ItemState = ItemState.Ready
        ''' <summary>不可安装时的原因描述。</summary>
        Public Reason As String = ""
        ''' <summary>显示名，尽量不为空。</summary>
        Public ReadOnly Property Display As String
            Get
                If Project IsNot Nothing Then Return Project.TranslatedName
                If Version IsNot Nothing Then Return Version.Display
                Return If(ProjectId, "未知项目")
            End Get
        End Property
    End Class

    ''' <summary>一次安装的完整计划。</summary>
    Public Class Plan
        Public Items As New List(Of PlanItem)
        ''' <summary>当前版本里已经装着的项目 ID，用于去重。</summary>
        Public InstalledProjectIds As New HashSet(Of String)
        Public ReadOnly Property Ready As List(Of PlanItem)
            Get
                Return Items.Where(Function(i) i.State = ItemState.Ready).ToList()
            End Get
        End Property
        Public ReadOnly Property Skipped As List(Of PlanItem)
            Get
                Return Items.Where(Function(i) i.State <> ItemState.Ready).ToList()
            End Get
        End Property
        ''' <summary>可以安装的条目里，真正需要下载的项目数（不含已存在的）。</summary>
        Public ReadOnly Property ReadyCount As Integer
            Get
                Return Ready.Count
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 分析一批项目在指定版本下的安装计划。耗时操作，必须在工作线程调用。
    ''' </summary>
    ''' <param name="References">要安装的项目。</param>
    ''' <param name="Instance">目标版本，不能为 Nothing。</param>
    ''' <param name="Progress">进度回调，参数是当前正在处理的项目名。</param>
    Public Function BuildPlan(References As IEnumerable(Of ModFavoriteShare.ShareItem),
                              Instance As McInstance,
                              Optional Progress As Action(Of String) = Nothing,
                              Optional ProgressScan As Action(Of Integer, Integer, String) = Nothing) As Plan
        Dim Result As New Plan
        If Instance Is Nothing Then Return Result
        If Not Instance.IsLoaded Then Instance.Load()

        '先摸清当前版本已经装了什么：既按项目 ID，也按文件内部 modid
        Dim LocalFiles As List(Of LocalResourceFile) = GetLocalMods(Instance)
        Dim LocalModIds As New HashSet(Of String)
        '读 jar 里的 modid 是整个分析里最慢的一步（每个包都要解压），单独报进度
        Dim ScanIndex As Integer = 0
        For Each Entry In LocalFiles
            If Entry.Project IsNot Nothing AndAlso Not String.IsNullOrEmpty(Entry.Project.Id) Then
                Result.InstalledProjectIds.Add(Entry.Project.Id)
            End If
            ScanIndex += 1
            ProgressScan?.Invoke(ScanIndex, LocalFiles.Count, Entry.File.Name)
            For Each ModId In ModFileIdentity.GetModIds(Entry.File.FullName)
                LocalModIds.Add(ModId)
                Dim Normalized As String = ModFileIdentity.NormalizeModId(ModId)
                If Normalized <> "" Then LocalModIds.Add(Normalized)
            Next
        Next

        '按项目 ID 去重，同时记录哪些是被前置拉进来的
        Dim Visited As New HashSet(Of String)
        Dim Queue As New Queue(Of (Item As ModFavoriteShare.ShareItem, IsDependency As Boolean))
        For Each Ref In References
            If Ref Is Nothing OrElse String.IsNullOrEmpty(Ref.ProjectId) Then Continue For
            Queue.Enqueue((Ref, False))
        Next

        While Queue.Count > 0
            Dim Current = Queue.Dequeue()
            Dim Key As String = $"{CInt(Current.Item.Platform)}|{Current.Item.ProjectId}"
            If Not Visited.Add(Key) Then Continue While

            Dim Node As New PlanItem With {
                .Platform = Current.Item.Platform,
                .ProjectId = Current.Item.ProjectId,
                .IsDependency = Current.IsDependency
            }
            Result.Items.Add(Node)

            '项目信息
            Try
                Progress?.Invoke(Current.Item.ProjectId)
                Node.Project = ResourceProject.FromProjectId(Current.Item.ProjectId, Current.Item.Platform)
            Catch ex As Exception
                Node.State = ItemState.Unavailable
                Node.Reason = "无法获取项目信息"
                Logger.Warn(ex, $"获取项目信息失败（{Current.Item.ProjectId}）")
                Continue While
            End Try

            '已经有同 ID 的项目了
            If Result.InstalledProjectIds.Contains(Node.Project.Id) Then
                Node.State = ItemState.AlreadyInstalled
                Node.Reason = "当前版本里已安装"
                Continue While
            End If

            '挑一个适配当前版本的文件
            Dim Versions As List(Of ResourceVersion)
            Try
                Versions = ResourceVersion.FromProjectId(Node.Project.Id, Node.Project.Platform)
            Catch ex As Exception
                Node.State = ItemState.Unavailable
                Node.Reason = "无法获取版本列表"
                Logger.Warn(ex, $"获取项目版本列表失败（{Node.Project.Id}）")
                Continue While
            End Try
            Node.Version = PickVersion(Versions, Instance)
            If Node.Version Is Nothing Then
                Node.State = ItemState.NoMatchingVersion
                Node.Reason = $"没有适配 {Instance.Version.VanillaName} 的版本"
                Continue While
            End If

            Node.State = ItemState.Ready

            '按文件内部 modid 再查一次：同一个 Mod 可能换了文件名或换了平台发布，
            '只比项目 ID 会漏掉这些情况。这一步只对 Mod 有意义
            If Node.Project.Types.HasFlagF(ResourceTypes.Mod) AndAlso LocalModIds.Any() Then
                Dim CandidateIds = ModFileIdentity.GetProjectIds(Node.Project)
                Dim HitId As String = CandidateIds.FirstOrDefault(Function(id) LocalModIds.Contains(id))
                If HitId IsNot Nothing Then
                    Node.State = ItemState.AlreadyInstalled
                    Node.Reason = $"当前版本里已安装（内部标识 {HitId}）"
                    Continue While
                End If
            End If

            '把这条版本要求的前置排进队列
            For Each DependencyId In Node.Version.Dependencies
                If String.IsNullOrWhiteSpace(DependencyId) Then Continue For
                Queue.Enqueue((New ModFavoriteShare.ShareItem With {
                    .Platform = Node.Project.Platform, .ProjectId = DependencyId}, True))
            Next
        End While

        '依赖项如果自己已经被别人当作直接目标列出来了，就不要再标成前置
        Dim DirectIds As New HashSet(Of String)
        For Each Ref In References
            If Ref IsNot Nothing Then DirectIds.Add(Ref.ProjectId)
        Next
        For Each Item In Result.Items
            If DirectIds.Contains(Item.ProjectId) Then Item.IsDependency = False
        Next

        Logger.Info($"安装计划：共 {Result.Items.Count} 项，可安装 {Result.Ready.Count} 项")
        Return Result
    End Function

    ''' <summary>
    ''' 从项目的版本列表里挑出最适合当前实例的一个。
    ''' 依次比较：是否支持当前游戏版本 → 加载器是否匹配 → 正式版优先 → 发布时间最新。
    ''' </summary>
    Private Function PickVersion(Versions As List(Of ResourceVersion), Instance As McInstance) As ResourceVersion
        If Versions Is Nothing OrElse Not Versions.Any() Then Return Nothing
        Dim VanillaName As String = Instance.Version.VanillaName
        Dim TargetLoaders As ModLoaders = Instance.Version.ModLoaders

        Dim Candidates = Versions.Where(
            Function(v)
                If Not v.DownloadAvailable Then Return False
                If v.GameVersions Is Nothing OrElse Not v.GameVersions.Contains(VanillaName) Then Return False
                '版本本身不带加载器信息时（资源包、数据包等）不参与加载器筛选
                If v.ModLoaders = ModLoaders.None OrElse TargetLoaders = ModLoaders.None Then Return True
                Return v.ModLoaders.Flags.Intersect(TargetLoaders.Flags).Any()
            End Function).ToList()
        If Not Candidates.Any() Then Return Nothing

        Return Candidates.
            OrderBy(Function(v) CInt(v.ReleaseType)).
            ThenByDescending(Function(v) v.ReleaseDate).
            First()
    End Function

    ''' <summary>
    ''' 取当前版本 mods 文件夹里的文件，并尽量补全它们的项目信息。
    ''' 优先复用模组管理页已经加载好的结果，避免重复扫盘与联网。
    ''' </summary>
    Public Function GetLocalMods(Instance As McInstance) As List(Of LocalResourceFile)
        If Instance Is Nothing Then Return New List(Of LocalResourceFile)
        '模组页已经加载过就直接用
        If FrmInstanceMod IsNot Nothing AndAlso
           LocalResourceLoader.State = LoadState.Finished AndAlso
           LocalResourceLoader.Output IsNot Nothing AndAlso
           PageInstanceLeft.Instance IsNot Nothing AndAlso
           PathUtils.IsSame(PageInstanceLeft.Instance.PathVersion, Instance.PathVersion) Then
            Return LocalResourceLoader.Output
        End If
        '否则自己扫一遍文件名，项目信息留空（只能做 modid 级别的去重）
        Dim Result As New List(Of LocalResourceFile)
        Dim ModFolder As String = Instance.PathIndie & "mods"
        Try
            If DirectoryUtils.Exists(ModFolder) Then
                For Each FilePath In DirectoryUtils.EnumerateFiles(ModFolder, False)
                    Dim Extension As String = PathUtils.GetExtension(FilePath).Lower
                    If Extension = "disabled" OrElse Extension = "old" Then
                        Extension = PathUtils.GetExtension(PathUtils.GetFileNameWithoutExtension(FilePath)).Lower
                    End If
                    If Extension = "jar" OrElse Extension = "litemod" OrElse Extension = "zip" Then
                        Result.Add(New LocalResourceFile(FilePath))
                    End If
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "扫描本地 Mod 文件夹失败")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 把计划渲染成给用户看的文本清单。
    ''' </summary>
    Public Function Describe(Plan As Plan, Instance As McInstance) As String
        Dim Lines As New List(Of String)
        Dim Loaders As String = If(Instance.Version.ModLoaders = ModLoaders.None, "原版", Instance.Version.ModLoaders.ToString)
        Lines.Add($"目标版本：{Instance.Name}（{Instance.Version.VanillaName}，{Loaders}）")
        Lines.Add("")

        Dim ReadyItems = Plan.Ready
        Dim DirectReady = ReadyItems.Where(Function(i) Not i.IsDependency).ToList()
        Dim DependencyReady = ReadyItems.Where(Function(i) i.IsDependency).ToList()
        If DirectReady.Any() Then
            Lines.Add($"将安装 {DirectReady.Count} 个 Mod：")
            For Each Item In DirectReady
                Lines.Add($"- {Item.Display}  {If(Item.Version?.Version, Item.Version?.Display)}")
            Next
        End If
        If DependencyReady.Any() Then
            If DirectReady.Any() Then Lines.Add("")
            Lines.Add($"将自动补装 {DependencyReady.Count} 个前置：")
            For Each Item In DependencyReady
                Lines.Add($"- {Item.Display}  {If(Item.Version?.Version, Item.Version?.Display)}")
            Next
        End If

        Dim Skipped = Plan.Skipped
        If Skipped.Any() Then
            If Lines.Count > 0 Then Lines.Add("")
            Lines.Add($"以下 {Skipped.Count} 项不会安装：")
            For Each Item In Skipped
                Dim Tag As String = If(Item.IsDependency, "（前置）", "")
                Lines.Add($"- {Item.Display}{Tag}：{Item.Reason}")
            Next
        End If

        If Not ReadyItems.Any() Then
            Lines.Add("")
            Lines.Add("没有可以安装的内容。")
        End If
        Return Lines.Join(vbCrLf)
    End Function

End Module
