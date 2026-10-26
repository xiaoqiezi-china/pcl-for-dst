Imports System.Net
Imports System.Text

''' <summary>
''' 启动器自动更新的数据源。
''' <para>
''' 这里**只认 GitHub Release**，与预设服务器（<see cref="DstBot"/>）没有任何关系：
''' 预设服务器是可替换的、由使用者挑选的第三方服务，让它来决定「装哪个启动器」风险太大。
''' </para>
''' <para>
''' 查询分两步，尽量避免吃 API 速率限制：
''' </para>
''' <para>
''' 1. <c>github.com/…/releases/latest</c> 是页面地址，302 跳到具体 tag，**不计入 API 配额**，
'''    版本号就从跳转后的地址里取；
''' 2. 正文与附件列表必须走 API，匿名每小时只有 60 次。失败时不报错，
'''    降级成「只知道有新版本、但拿不到说明和下载地址」，用户可以去发布页手动下载。
''' </para>
''' <para>
''' 拿到过的结果会写一份本地缓存，下次启动先用缓存，避免每次开机都请求。
''' </para>
''' </summary>
Friend Module ModUpdateSource

    ''' <summary>仓库标识。发版用的 Release 就挂在这里。</summary>
    Private Const RepoOwner As String = "xiaoqiezi-china"
    Private Const RepoName As String = "pcl-for-dst"

    ''' <summary>Release 列表 API。会消耗匿名配额。</summary>
    Private ReadOnly Property ApiReleases As String
        Get
            Return $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=20"
        End Get
    End Property

    ''' <summary>最新 Release 的页面地址。只做 302 跳转，不吃 API 配额。</summary>
    Private ReadOnly Property LatestPage As String
        Get
            Return $"https://github.com/{RepoOwner}/{RepoName}/releases/latest"
        End Get
    End Property

    ''' <summary>仓库 Releases 页，用于「手动下载」。</summary>
    Public ReadOnly Property ReleasesPage As String
        Get
            Return $"https://github.com/{RepoOwner}/{RepoName}/releases"
        End Get
    End Property

    ''' <summary>加速镜像前缀。按顺序尝试，空串代表官方地址。</summary>
    Private ReadOnly Mirrors As String() = {
        "",
        "https://ghfast.top/",
        "https://gh-proxy.com/"
    }

    ''' <summary>缓存文件，存上次成功拿到的完整信息。</summary>
    Private ReadOnly Property CacheFile As String
        Get
            Return Paths.Base & "PCL\\UpdateCache.json"
        End Get
    End Property

    ''' <summary>一条 Release 信息。</summary>
    Public Class ReleaseInfo
        Public Property Version As String = ""
        Public Property Notes As String = ""
        Public Property AssetUrl As String = ""
        Public Property AssetName As String = ""
        ''' <summary>Release 里声明的 SHA-256（小写十六进制）；空串表示该 Release 没提供。</summary>
        Public Property AssetSha256 As String = ""
        Public Property Err As String = ""
        ''' <summary>只拿到了版本号，附件列表取不到（多半是 API 限流）。</summary>
        Public Property VersionOnly As Boolean = False
        Public ReadOnly Property Ok As Boolean
            Get
                Return Err = "" AndAlso Version <> ""
            End Get
        End Property
        ''' <summary>能否直接下载。</summary>
        Public ReadOnly Property CanDownload As Boolean
            Get
                Return Ok AndAlso AssetUrl <> ""
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 查询最新 Release。必须在工作线程调用。
    ''' </summary>
    ''' <param name="Force">忽略缓存强制重新请求。</param>
    Public Function FetchLatest(Optional Force As Boolean = False) As ReleaseInfo
        If Not Force Then
            '先用内存缓存
            Dim Mem As ReleaseInfo = MemoryCache
            If Mem IsNot Nothing AndAlso Date.Now - MemoryCachedAt < TimeSpan.FromMinutes(CacheMinutes) Then Return Mem
            '再用磁盘缓存
            Dim Disk As ReleaseInfo = ReadCache()
            If Disk IsNot Nothing Then
                MemoryCache = Disk
                MemoryCachedAt = Date.Now
                Return Disk
            End If
        End If
        Dim Result As ReleaseInfo = FetchFromApi()
        'API 拿不到就退一步：只要版本号，至少能提示有新版
        If Not Result.Ok Then
            Dim VersionOnly = FetchVersionOnly()
            If VersionOnly IsNot Nothing Then
                VersionOnly.Err = ""
                VersionOnly.VersionOnly = True
                If Result.Err <> "" Then VersionOnly.Notes = ""
                Result = VersionOnly
            End If
        End If
        If Result.Ok Then
            MemoryCache = Result
            MemoryCachedAt = Date.Now
            '只缓存信息完整的，免得把「只知道版本号」也固化下来
            If Result.CanDownload Then WriteCache(Result)
        End If
        Return Result
    End Function

    Private MemoryCache As ReleaseInfo = Nothing
    Private MemoryCachedAt As Date = Date.MinValue
    Private Const CacheMinutes As Integer = 30

    ''' <summary>走 API 拿完整的 Release 信息。</summary>
    Private Function FetchFromApi() As ReleaseInfo
        Dim Result As New ReleaseInfo
        Try
            Dim Json As String = HttpGetText(ApiReleases)
            If String.IsNullOrWhiteSpace(Json) Then
                Result.Err = "服务器返回为空"
                Return Result
            End If
            Dim Arr As JArray = JArray.Parse(Json)
            If Arr.Count = 0 Then
                Result.Err = "仓库里还没有发布过 Release"
                Return Result
            End If
            Dim Target As JObject = CType(Arr(0), JObject)
            'tag 形如 v1.0.0 或 1.0.0，去掉前导 v 就是版本号
            Result.Version = If(Target("tag_name"), "").ToString().Trim().TrimStart("v"c, "V"c)
            Result.Notes = If(Target("body"), "").ToString()
            '取第一个 .exe 附件
            Dim Assets = TryCast(Target("assets"), JArray)
            If Assets IsNot Nothing Then
                For Each Item As JToken In Assets
                    Dim Name As String = If(Item("name"), "").ToString()
                    If Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then
                        Result.AssetName = Name
                        Result.AssetUrl = If(Item("browser_download_url"), "").ToString()
                        'GitHub 给出 sha256:… 形式的摘要，用来校验下载到的文件
                        Dim Digest As String = If(Item("digest"), "").ToString().Trim()
                        If Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) Then
                            Result.AssetSha256 = Digest.Substring(7).Trim().ToLowerInvariant()
                        End If
                        Exit For
                    End If
                Next
            End If
            If Result.Version = "" Then Result.Err = "Release 里没有版本号"
        Catch ex As HttpRequestCodeException
            Result.Err = "API 请求失败（多半是匿名配额用尽）"
            Logger.Info($"查询 Release 失败：{ex.Message}")
        Catch ex As Exception
            Result.Err = FriendlyError(ex.Message)
            Logger.Warn(ex, "查询 GitHub Release 失败")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 只取版本号：请求 releases/latest，读 302 跳转后的地址。
    ''' 这条路径不消耗 API 配额，是最稳的兜底。
    ''' </summary>
    Private Function FetchVersionOnly() As ReleaseInfo
        Try
            Dim Req As HttpWebRequest = CType(WebRequest.Create(LatestPage), HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = 15000
            Req.UserAgent = "PCL-Updater"
            Req.AllowAutoRedirect = False
            Using Resp As HttpWebResponse = CType(Req.GetResponse(), HttpWebResponse)
                Dim Location = Resp.Headers("Location")
                If String.IsNullOrEmpty(Location) Then Return Nothing
                '形如 https://github.com/owner/repo/releases/tag/1.0.0
                Dim Marker As String = "/releases/tag/"
                Dim Index As Integer = Location.IndexOf(Marker, StringComparison.OrdinalIgnoreCase)
                If Index < 0 Then Return Nothing
                Dim Tag As String = Location.Substring(Index + Marker.Length)
                If Tag = "" Then Return Nothing
                Return New ReleaseInfo With {.Version = Tag.Trim().TrimStart("v"c, "V"c)}
            End Using
        Catch ex As Exception
            Logger.Info($"只取版本号也失败了：{ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 把一个 Release 附件地址展开成所有可用下载源：官方在前，镜像在后。
    ''' 交给 NetFile 依次尝试，前一个失败会自动换下一个。
    ''' </summary>
    Public Function BuildSources(Url As String) As String()
        Dim Result As New List(Of String)
        For Each Mirror In Mirrors
            Result.Add(If(Mirror = "", Url, Mirror & Url))
        Next
        Return Result.ToArray()
    End Function

    ''' <summary>读本地缓存。没有或坏了都返回 Nothing。</summary>
    Private Function ReadCache() As ReleaseInfo
        Try
            If Not FileUtils.Exists(CacheFile) Then Return Nothing
            Dim Obj As JObject = JObject.Parse(FileUtils.ReadAsString(CacheFile))
            Dim Result As New ReleaseInfo With {
                .Version = If(Obj("version"), "").ToString(),
                .Notes = If(Obj("notes"), "").ToString(),
                .AssetName = If(Obj("assetName"), "").ToString(),
                .AssetUrl = If(Obj("assetUrl"), "").ToString(),
                .AssetSha256 = If(Obj("assetSha256"), "").ToString()
            }
            If Result.Version = "" OrElse Result.AssetUrl = "" Then Return Nothing
            Return Result
        Catch ex As Exception
            Logger.Info("读取更新缓存失败：" & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>写本地缓存，失败就算了，不影响更新流程。</summary>
    Private Sub WriteCache(Info As ReleaseInfo)
        Try
            DirectoryUtils.Create(PathUtils.RemoveLastPart(CacheFile))
            Dim Obj As New JObject From {
                {"version", Info.Version}, {"notes", Info.Notes},
                {"assetName", Info.AssetName}, {"assetUrl", Info.AssetUrl},
                {"assetSha256", Info.AssetSha256}}
            FileUtils.WriteAtomic(CacheFile, Obj.ToString(Newtonsoft.Json.Formatting.Indented))
        Catch ex As Exception
            Logger.Info("写入更新缓存失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>算文件的 SHA-256，返回小写十六进制。失败返回空串。</summary>
    Public Function GetFileSha256(FilePath As String) As String
        Try
            Using Sha As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
                Using Fs As New IO.FileStream(FilePath, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read)
                    Dim Hash As Byte() = Sha.ComputeHash(Fs)
                    Dim Sb As New Text.StringBuilder(Hash.Length * 2)
                    For Each B As Byte In Hash
                        Sb.Append(B.ToString("x2"))
                    Next
                    Return Sb.ToString()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"计算文件 SHA-256 失败：{FilePath}")
            Return ""
        End Try
    End Function

    ''' <summary>GET 文本。GitHub API 要求带 User-Agent 与 Accept，否则直接 403。</summary>
    Private Function HttpGetText(Url As String) As String
        Dim Req As HttpWebRequest = CType(WebRequest.Create(Url), HttpWebRequest)
        Req.Method = "GET"
        Req.Timeout = 15000
        Req.UserAgent = "PCL-Updater"
        Req.Accept = "application/vnd.github+json"
        Using Resp As HttpWebResponse = CType(Req.GetResponse(), HttpWebResponse)
            Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                Return Reader.ReadToEnd()
            End Using
        End Using
    End Function

    ''' <summary>
    ''' 测一批下载源的速度，按从快到慢返回。
    ''' <para>
    ''' 只取每个源的前若干字节，测完就断开——目的是比较谁快，不是下完。
    ''' 测不出来的源（超时、报错）排到最后，仍然保留作为兜底。
    ''' </para>
    ''' </summary>
    ''' <param name="Urls">候选地址。</param>
    ''' <param name="ProbeBytes">每个源试探下载的字节数。</param>
    ''' <param name="TimeoutMs">单个源的超时。</param>
    Public Function RankSourcesBySpeed(Urls As String(), Optional ProbeBytes As Integer = 256 * 1024, Optional TimeoutMs As Integer = 5000) As String()
        If Urls Is Nothing OrElse Urls.Length <= 1 Then Return Urls
        Dim Results As New List(Of KeyValuePair(Of String, Double))
        Dim LockObj As New Object()
        Dim Tasks As New List(Of Threading.Tasks.Task)
        For Each Url In Urls
            Dim Target As String = Url
            Tasks.Add(Threading.Tasks.Task.Run(
                Sub()
                    Dim Speed As Double = MeasureOne(Target, ProbeBytes, TimeoutMs)
                    SyncLock LockObj
                        Results.Add(New KeyValuePair(Of String, Double)(Target, Speed))
                    End SyncLock
                End Sub))
        Next
        Try
            Threading.Tasks.Task.WaitAll(Tasks.ToArray, TimeoutMs + 3000)
        Catch ex As Exception
            Logger.Warn(ex, "测速过程出错")
        End Try
        '按速度降序；测不出来的（0）排最后
        Dim Sorted = Results.OrderByDescending(Function(P) P.Value).Select(Function(P) P.Key).ToList()
        '没测出结果的源也要保留，否则会少一条兜底
        For Each Url In Urls
            If Not Sorted.Contains(Url) Then Sorted.Add(Url)
        Next
        Return Sorted.ToArray()
    End Function

    ''' <summary>测单个源：返回字节/秒，失败返回 0。</summary>
    Private Function MeasureOne(Url As String, ProbeBytes As Integer, TimeoutMs As Integer) As Double
        Try
            Dim Req As HttpWebRequest = CType(WebRequest.Create(Url), HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = TimeoutMs
            Req.ReadWriteTimeout = TimeoutMs
            Req.UserAgent = "PCL-Updater"
            '只要前一段，服务器支持 Range 就能省流量；不支持也无所谓，读完就断
            Req.AddRange(0, ProbeBytes - 1)
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            Using Resp As HttpWebResponse = CType(Req.GetResponse(), HttpWebResponse)
                Using Stream As IO.Stream = Resp.GetResponseStream()
                    Dim Buffer(8191) As Byte
                    Dim Total As Long = 0
                    Do While Total < ProbeBytes AndAlso Watch.ElapsedMilliseconds < TimeoutMs
                        Dim Read As Integer = Stream.Read(Buffer, 0, Buffer.Length)
                        If Read <= 0 Then Exit Do
                        Total += Read
                    Loop
                    Watch.Stop()
                    If Total <= 0 OrElse Watch.ElapsedMilliseconds <= 0 Then Return 0
                    Return Total * 1000.0 / Watch.ElapsedMilliseconds
                End Using
            End Using
        Catch ex As Exception
            Logger.Info($"测速失败（{Url}）：{ex.Message}")
            Return 0
        End Try
    End Function

End Module

