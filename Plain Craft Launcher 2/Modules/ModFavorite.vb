Imports Newtonsoft.Json.Linq

''' <summary>
''' 一条收藏记录。存 ResourceProject 的完整 JSON，重启后能还原成可跳转的对象。
''' </summary>
Public Class FavoriteEntry
    Public Property ResourceType As Integer
    Public Property Title As String = ""
    Public Property SubTitle As String = ""
    Public Property Description As String = ""
    Public Property Logo As String = ""
    Public Property ProjectJson As String = ""
    Public Property TimeTicks As Long
    Public Property Key As String = ""
End Class

''' <summary>
''' 社区资源收藏夹。本地存文件（PCL\DstFavorites.json），开启同步时再写一份到云端。
''' </summary>
Friend Module ModFavorite

    Private Const CloudKey As String = "dst_favorites"
    Private Const SyncSettingKey As String = "DstFavCloud"
    Private Const LegacyKey As String = "DstFavorites"

    Private ReadOnly Items As New List(Of FavoriteEntry)
    Private Loaded As Boolean
    Private ReadOnly Lock As New Object
    ''' <summary>上次与云端合并成功的时间，用于节流。</summary>
    Private LastSyncAt As Date = Date.MinValue
    ''' <summary>两次自动同步之间的最小间隔。</summary>
    Private ReadOnly SyncInterval As TimeSpan = TimeSpan.FromMinutes(3)

    ''' <summary>本地收藏文件。</summary>
    Public ReadOnly Property LocalSavePath As String
        Get
            Return Paths.Base & "PCL\DstFavorites.json"
        End Get
    End Property

    ''' <summary>是否同步至云。</summary>
    Public Property SyncToCloud As Boolean
        Get
            Return Settings.Get(Of Boolean)(SyncSettingKey)
        End Get
        Set(value As Boolean)
            Settings.Set(SyncSettingKey, value)
        End Set
    End Property

    Private Function Serialize() As String
        Dim Arr As New JArray
        SyncLock Lock
            For Each E In Items
                Arr.Add(New JObject From {
                    {"t", E.ResourceType}, {"n", E.Title}, {"s", E.SubTitle},
                    {"d", E.Description}, {"l", E.Logo}, {"p", E.ProjectJson},
                    {"k", E.TimeTicks}, {"id", E.Key}})
            Next
        End SyncLock
        Return Arr.ToString(Newtonsoft.Json.Formatting.None)
    End Function

    ''' <summary>云端数据的上限：超过就没法当成收藏夹，直接丢弃并记日志。</summary>
    Private Const CloudMaxBytes As Integer = 1024 * 1024
    Private Const CloudMaxItems As Integer = 10000

    Private Function ParseEntries(Json As String) As List(Of FavoriteEntry)
        Dim Result As New List(Of FavoriteEntry)
        If String.IsNullOrWhiteSpace(Json) Then Return Result
        '云端内容不是我们生成的，进来先做两道硬限制：
        '超长文本会把内存吃光，深层嵌套会让解析栈溢出
        If Json.Length > CloudMaxBytes Then
            Logger.Warn($"云端收藏数据过大（{Json.Length} 字符），已忽略")
            Return Result
        End If
        Try
            'Newtonsoft 的 JArray.Parse 没有深度参数，得自己用 JsonReader 卡住
            Dim Arr As JArray
            Using Reader As New IO.StringReader(Json)
                Using Jr As New Newtonsoft.Json.JsonTextReader(Reader) With {.MaxDepth = 32}
                    Arr = JArray.Load(Jr)
                End Using
            End Using
            If Arr.Count > CloudMaxItems Then
                Logger.Warn($"云端收藏条目过多（{Arr.Count} 条），已只取前 {CloudMaxItems} 条")
            End If
            For Each One As JToken In Arr
                If Result.Count >= CloudMaxItems Then Exit For
                Dim E As New FavoriteEntry With {
                    .ResourceType = Val(If(One("t"), "0").ToString()),
                    .Title = If(One("n"), "").ToString(),
                    .SubTitle = If(One("s"), "").ToString(),
                    .Description = If(One("d"), "").ToString(),
                    .Logo = If(One("l"), "").ToString(),
                    .ProjectJson = If(One("p"), "").ToString(),
                    .TimeTicks = Val(If(One("k"), "0").ToString()),
                    .Key = If(One("id"), "").ToString()
                }
                If E.Key <> "" Then Result.Add(E)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析收藏数据失败")
        End Try
        Return Result
    End Function

    Private Sub Deserialize(Json As String)
        Dim Parsed As List(Of FavoriteEntry) = ParseEntries(Json)
        SyncLock Lock
            Items.Clear()
            Items.AddRange(Parsed)
        End SyncLock
    End Sub

    Private Sub EnsureLoaded()
        SyncLock Lock
            If Loaded Then Return
            Loaded = True
        End SyncLock
        Try
            If FileUtils.Exists(LocalSavePath) Then
                Deserialize(FileUtils.ReadAsString(LocalSavePath))
                Return
            End If
            Dim Old As String = Settings.Get(Of String)(LegacyKey)
            If Not String.IsNullOrWhiteSpace(Old) Then
                Deserialize(Old)
                SaveLocal()
                Settings.Set(LegacyKey, "")
                Logger.Info("已把收藏夹从配置迁移到文件")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取本地收藏失败")
        End Try
    End Sub

    Public Sub SaveLocal()
        Try
            DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalSavePath))
            FileUtils.Write(LocalSavePath, Serialize())
        Catch ex As Exception
            Logger.Warn(ex, "保存本地收藏失败")
        End Try
    End Sub

    Private Sub Save(Optional PushCloud As Boolean = True)
        Dim Json As String = Serialize()
        SaveLocal()
        If Not PushCloud OrElse Not SyncToCloud Then Return
        If Not DstBot.IsQqBound Then Return
        RunInThread(
        Sub()
            Dim Err As String = DstBot.CloudWrite(CloudKey, Json)
            If Err IsNot Nothing Then Logger.Warn($"收藏夹云同步失败：{Err}")
        End Sub)
    End Sub

    Public Function KeyOf(Project As ResourceProject) As String
        If Project Is Nothing Then Return ""
        Return $"{CInt(Project.Platform)}|{Project.Slug}"
    End Function

    Public Function IsFavorite(Project As ResourceProject) As Boolean
        Dim K As String = KeyOf(Project)
        If K = "" Then Return False
        EnsureLoaded()
        SyncLock Lock
            Return Items.Any(Function(E) E.Key = K)
        End SyncLock
    End Function

    Public Function Toggle(Project As ResourceProject, Category As ResourceTypes,
                           Optional LogoOverride As String = "") As Boolean
        Dim K As String = KeyOf(Project)
        If K = "" Then Return False
        EnsureLoaded()
        Dim NowFav As Boolean
        SyncLock Lock
            Dim Exist = Items.FirstOrDefault(Function(E) E.Key = K)
            If Exist IsNot Nothing Then
                Items.Remove(Exist)
                NowFav = False
            Else
                Dim Titles = Project.GetControlTitle(True)
                Items.Add(New FavoriteEntry With {
                    .Key = K,
                    .ResourceType = CInt(Category),
                    .Title = Titles.Title,
                    .SubTitle = Titles.SubTitle,
                    .Description = If(Project.Description, "").ReplaceLineEndings(" "),
                    .Logo = If(LogoOverride <> "", LogoOverride, If(Project.LogoUrl, "")),
                    .ProjectJson = Project.ToJson().ToString(Newtonsoft.Json.Formatting.None),
                    .TimeTicks = Date.Now.Ticks
                })
                NowFav = True
            End If
        End SyncLock
        Save()
        Return NowFav
    End Function

    ''' <summary>
    ''' 加入收藏。已经收藏过则保持不变，不会像 Toggle 那样把它取消掉。
    ''' 返回是否为新加入。
    ''' </summary>
    Public Function Add(Project As ResourceProject, Category As ResourceTypes,
                       Optional LogoOverride As String = "") As Boolean
        Dim K As String = KeyOf(Project)
        If K = "" Then Return False
        EnsureLoaded()
        SyncLock Lock
            If Items.Any(Function(E) E.Key = K) Then Return False
            Dim Titles = Project.GetControlTitle(True)
            Items.Add(New FavoriteEntry With {
                .Key = K,
                .ResourceType = CInt(Category),
                .Title = Titles.Title,
                .SubTitle = Titles.SubTitle,
                .Description = If(Project.Description, "").ReplaceLineEndings(" "),
                .Logo = If(LogoOverride <> "", LogoOverride, If(Project.LogoUrl, "")),
                .ProjectJson = Project.ToJson().ToString(Newtonsoft.Json.Formatting.None),
                .TimeTicks = Date.Now.Ticks
            })
        End SyncLock
        Save()
        Return True
    End Function

    Public Sub Remove(Entry As FavoriteEntry)
        If Entry Is Nothing Then Return
        EnsureLoaded()
        SyncLock Lock
            Items.RemoveAll(Function(E) E.Key = Entry.Key)
        End SyncLock
        Save()
    End Sub

    Public Function GetByType(Type As ResourceTypes) As List(Of FavoriteEntry)
        EnsureLoaded()
        SyncLock Lock
            Return Items.Where(Function(E) E.ResourceType = CInt(Type)).
                         OrderByDescending(Function(E) E.TimeTicks).ToList()
        End SyncLock
    End Function

    Public Function CountOf(Type As ResourceTypes) As Integer
        EnsureLoaded()
        SyncLock Lock
            Return Items.Where(Function(E) E.ResourceType = CInt(Type)).Count()
        End SyncLock
    End Function

    Public Function ToProject(Entry As FavoriteEntry) As ResourceProject
        If Entry Is Nothing OrElse Entry.ProjectJson = "" Then Return Nothing
        Try
            Return New ResourceProject(JObject.Parse(Entry.ProjectJson))
        Catch ex As Exception
            Logger.Warn(ex, "还原收藏项目失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>推送到云端。返回 Nothing 表示成功。必须在工作线程调用。</summary>
    Public Function PushToCloud() As String
        EnsureLoaded()
        Return DstBot.CloudWrite(CloudKey, Serialize())
    End Function

    ''' <summary>
    ''' 与云端合并：按 Key 取并集，同一项保留 TimeTicks 较新的那份，合并结果写回本地并推上去。
    ''' 必须用合并而不是直接覆盖——云端是空数组时直接覆盖会把本地收藏清空。
    ''' 必须在工作线程调用。
    ''' </summary>
    Public Function PullFromCloud(Optional Force As Boolean = False) As String
        EnsureLoaded()
        '自动同步做节流：短时间内重复进出页不必每次都打接口
        If Not Force AndAlso Date.Now - LastSyncAt < SyncInterval Then Return Nothing
        Dim Remote As String
        Try
            Remote = DstBot.CloudRead(CloudKey)
        Catch ex As Exception
            Return FriendlyError(ex.Message)
        End Try
        Dim RemoteItems As List(Of FavoriteEntry) = ParseEntries(Remote)
        Dim Merged As New List(Of FavoriteEntry)
        SyncLock Lock
            Merged.AddRange(Items)
        End SyncLock
        For Each R In RemoteItems
            Dim L As FavoriteEntry = Merged.FirstOrDefault(Function(E) E.Key = R.Key)
            If L Is Nothing Then
                Merged.Add(R)
            ElseIf R.TimeTicks > L.TimeTicks Then
                Merged(Merged.IndexOf(L)) = R
            End If
        Next
        SyncLock Lock
            Items.Clear()
            Items.AddRange(Merged)
        End SyncLock
        SaveLocal()
        '合并结果回写云端，保持两边一致
        Dim Err As String = PushToCloud()
        If Err Is Nothing Then LastSyncAt = Date.Now
        Return Err
    End Function

End Module