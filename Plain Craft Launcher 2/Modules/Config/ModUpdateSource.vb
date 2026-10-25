Imports System.Net
Imports System.Text

''' <summary>
''' 启动器自动更新的数据源。
''' <para>
''' 这里**只认 GitHub Release**，与预设服务器（<see cref="DstBot"/>）没有任何关系：
''' 预设服务器是可替换的、由使用者挑选的第三方服务，让它来决定「装哪个启动器」风险太大。
''' </para>
''' <para>
''' 仓库里有两条产品线，版本号各不相同（DST 走 0.3.x，DPCL 走 1.0.x）。
''' 它们共用一个仓库，所以 tag 必须带产品前缀（<c>dst-v</c> / <c>dpcl-v</c>），
''' 客户端只在自己那条线的 Release 里找，否则 DST 会被推去装 DPCL。
''' </para>
''' <para>
''' 对国内网络做了降级：官方地址 → 加速镜像。镜像只是加速通道，不是信任源：
''' 它们转发的是同一份文件，而且下载完会校验文件头，拿到的必须真的是启动器。
''' </para>
''' </summary>
Friend Module ModUpdateSource

    ''' <summary>仓库标识。发版用的 Release 就挂在这里。</summary>
    Private Const RepoOwner As String = "xiaoqiezi-china"
    Private Const RepoName As String = "pcl-for-dst"


    ''' <summary>列出最近的 Release。接口按发布时间倒序返回。</summary>
    Private ReadOnly Property ApiReleases As String
        Get
            Return $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=20"
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

    ''' <summary>一条 Release 信息。</summary>
    Public Class ReleaseInfo
        Public Property Version As String = ""
        Public Property Notes As String = ""
        Public Property AssetUrl As String = ""
        Public Property AssetName As String = ""
        Public Property Err As String = ""
        Public ReadOnly Property Ok As Boolean
            Get
                Return Err = "" AndAlso Version <> "" AndAlso AssetUrl <> ""
            End Get
        End Property
    End Class

    Private Cached As ReleaseInfo = Nothing
    Private CachedAt As Date = Date.MinValue
    Private Const CacheMinutes As Integer = 30

    ''' <summary>
    ''' 查询本产品线的最新 Release。必须在工作线程调用。
    ''' </summary>
    ''' <param name="Force">忽略内存缓存强制重新请求。</param>
    Public Function FetchLatest(Optional Force As Boolean = False) As ReleaseInfo
        If Not Force AndAlso Cached IsNot Nothing AndAlso Date.Now - CachedAt < TimeSpan.FromMinutes(CacheMinutes) Then
            Return Cached
        End If
        Dim Result As New ReleaseInfo
        Try
            Dim Json As String = HttpGetText(ApiReleases)
            If String.IsNullOrWhiteSpace(Json) Then
                Result.Err = "服务器返回为空"
                Return Result
            End If
            '只发一条产品线的 Release，接口按发布时间倒序，第一个就是最新的
            Dim Arr As JArray = JArray.Parse(Json)
            If Arr.Count = 0 Then
                Result.Err = "仓库里还没有发布过 Release"
                Return Result
            End If
            Dim Target As JObject = CType(Arr(0), JObject)
            'tag 形如 v1.0.0 或 1.0.0，去掉前导 v 就是版本号
            Result.Version = If(Target("tag_name"), "").ToString().Trim().TrimStart("v"c, "V"c)
            Result.Notes = If(Target("body"), "").ToString()
            '取第一个 .exe 附件。发版时 CI 只挂这一个，不需要更复杂的挑选
            Dim Assets = TryCast(Target("assets"), JArray)
            If Assets IsNot Nothing Then
                For Each Item As JToken In Assets
                    Dim Name As String = If(Item("name"), "").ToString()
                    If Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then
                        Result.AssetName = Name
                        Result.AssetUrl = If(Item("browser_download_url"), "").ToString()
                        Exit For
                    End If
                Next
            End If
            If Result.Version = "" Then
                Result.Err = "Release 里没有版本号"
            ElseIf Result.AssetUrl = "" Then
                Result.Err = "Release 里没有可下载的附件"
            End If
        Catch ex As Exception
            Result.Err = FriendlyError(ex.Message)
            Logger.Warn(ex, "查询 GitHub Release 失败")
        End Try
        If Result.Ok Then
            Cached = Result
            CachedAt = Date.Now
        End If
        Return Result
    End Function

    ''' <summary>
    ''' 下载更新包。依次尝试官方地址与各镜像，任一成功即返回。必须在工作线程调用。
    ''' </summary>
    ''' <returns>实际使用的下载地址；全部失败时返回空串。</returns>
    Public Function Download(Url As String, LocalFile As String,
                             Optional OnProgress As Action(Of Long, Long) = Nothing) As String
        For Each Mirror In Mirrors
            Dim FullUrl As String = If(Mirror = "", Url, Mirror & Url)
            Dim Label As String = If(Mirror = "", "官方源", Mirror)
            Try
                Logger.Info($"尝试下载更新包：{Label}")
                DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalFile))
                DownloadTo(FullUrl, LocalFile, OnProgress)
                If Not FileUtils.Exists(LocalFile) Then Continue For
                '拿到的东西必须真的是启动器，别把镜像的错误页当成更新包
                If DstUpdate.SniffPackage(LocalFile) = "unknown" Then
                    Logger.Warn($"{Label} 返回的不是有效更新包，换下一个")
                    FileUtils.Delete(LocalFile)
                    Continue For
                End If
                Return FullUrl
            Catch ex As Exception
                ex.ThrowIfCanceled()
                Logger.Warn(ex, $"从 {Label} 下载失败")
            End Try
        Next
        Return ""
    End Function

    ''' <summary>流式下载到文件。不经过任何业务模块，避免把预设服务的认证带出去。</summary>
    Private Sub DownloadTo(Url As String, LocalFile As String, OnProgress As Action(Of Long, Long))
        Dim Req As HttpWebRequest = CType(WebRequest.Create(Url), HttpWebRequest)
        Req.Method = "GET"
        Req.Timeout = 30000
        Req.ReadWriteTimeout = 120000
        'GitHub 对没有 User-Agent 的请求直接 403
        Req.UserAgent = "PCL-Updater"
        Req.AllowAutoRedirect = True
        Using Resp As HttpWebResponse = CType(Req.GetResponse(), HttpWebResponse)
            Dim Total As Long = Resp.ContentLength
            Dim Received As Long = 0
            Using Stream As IO.Stream = Resp.GetResponseStream(),
                  FileStream As New IO.FileStream(LocalFile, IO.FileMode.Create, IO.FileAccess.Write)
                Dim Buffer(16383) As Byte
                Do
                    Dim Read As Integer = Stream.Read(Buffer, 0, Buffer.Length)
                    If Read <= 0 Then Exit Do
                    FileStream.Write(Buffer, 0, Read)
                    Received += Read
                    If OnProgress IsNot Nothing Then OnProgress(Received, Total)
                Loop
            End Using
        End Using
    End Sub

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

End Module
