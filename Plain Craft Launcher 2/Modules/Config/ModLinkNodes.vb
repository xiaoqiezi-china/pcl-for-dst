Imports Newtonsoft.Json.Linq

''' <summary>
''' 联机用的 EasyTier 节点清单。
''' <para>
''' 原来 PCL 直接向 uptime.easytier.cn 要节点列表，那个服务已经没了。
''' 这里改成和云预设一样的思路：仓库里放一份清单，启动时拉下来，
''' 拉不到就用上次成功的备份，连备份都没有就用代码里内置的兜底节点。
''' </para>
''' <para>
''' 清单是**不可信内容**，只取地址与标签，其余字段一律忽略；
''' 格式不对的条目直接丢掉，不会让整份清单失效。
''' </para>
''' </summary>
Friend Module ModLinkNodes

    ''' <summary>
    ''' 节点清单的候选地址，按顺序尝试。
    ''' <para>
    ''' raw.githubusercontent.com 在国内经常解析不了（NameResolutionFailure），
    ''' 所以把加速镜像放前面。实测 ghfast 与 gh-proxy 可用，jsDelivr 在部分网络下也通，
    ''' 最后才是官方地址。
    ''' </para>
    ''' </summary>
    Public ReadOnly NodesUrls As String() = {
        "https://ghfast.top/https://raw.githubusercontent.com/xiaoqiezi-china/pcl-for-dst/refs/heads/main/link-nodes.json",
        "https://gh-proxy.com/https://raw.githubusercontent.com/xiaoqiezi-china/pcl-for-dst/refs/heads/main/link-nodes.json",
        "https://cdn.jsdelivr.net/gh/xiaoqiezi-china/pcl-for-dst@main/link-nodes.json",
        "https://raw.githubusercontent.com/xiaoqiezi-china/pcl-for-dst/refs/heads/main/link-nodes.json"
    }

    ''' <summary>本地备份目录。</summary>
    Public ReadOnly Property BackupFolder As String
        Get
            Return Paths.Base & "PCL\LinkNodesBackup\"
        End Get
    End Property

    Private Const MaxBackup As Integer = 5
    Private Const MaxBytes As Integer = 256 * 1024

    ''' <summary>
    ''' 代码内置的兜底节点。云端清单和备份都拿不到时至少还能联机。
    ''' 地址来自公开的公益共享节点，随时可能失效，所以只作为最后一层。
    ''' </summary>
    Private ReadOnly FallbackNodes As String() = {
        "tcp://easytier-us.slarker.me:11010"
    }

    ''' <summary>一条节点。</summary>
    Public Class LinkNode
        Public Property Id As Integer = 0
        Public Property Address As String = ""
        Public Property AllowRelay As Boolean = True
        Public Property Tags As New List(Of String)
        ''' <summary>TCP 握手耗时（毫秒）。负数表示还没测或不支持测。</summary>
        Public Property Latency As Integer = -1
        Public ReadOnly Property IsValid As Boolean
            Get
                Return Address <> ""
            End Get
        End Property
    End Class

    Private Cached As List(Of LinkNode) = Nothing
    Private CachedAt As Date = Date.MinValue
    Private Const CacheMinutes As Integer = 30

    ''' <summary>
    ''' 取节点清单。必须在工作线程调用。
    ''' </summary>
    ''' <param name="Force">忽略内存缓存强制重新拉取。</param>
    Public Function Fetch(Optional Force As Boolean = False) As List(Of LinkNode)
        If Not Force AndAlso Cached IsNot Nothing AndAlso Date.Now - CachedAt < TimeSpan.FromMinutes(CacheMinutes) Then
            Return Cached
        End If
        '1. 云端
        Dim FromCloud As List(Of LinkNode) = TryFetchCloud()
        If FromCloud IsNot Nothing AndAlso FromCloud.Count > 0 Then
            SaveBackup(FromCloud)
            Cached = FromCloud
            CachedAt = Date.Now
            CachedSource = CacheSources.Cloud
            Logger.Info($"联机节点：已从云端获取 {FromCloud.Count} 个")
            Return FromCloud
        End If
        '2. 本地备份
        Dim FromBackup As List(Of LinkNode) = LoadBackup()
        If FromBackup IsNot Nothing AndAlso FromBackup.Count > 0 Then
            Cached = FromBackup
            CachedAt = Date.Now
            CachedSource = CacheSources.Backup
            Logger.Warn($"联机节点：云端不可用，使用本地备份的 {FromBackup.Count} 个")
            Return FromBackup
        End If
        '3. 内置兜底
        Dim Result As New List(Of LinkNode)
        Dim Index As Integer = 1
        For Each Address In FallbackNodes
            Result.Add(New LinkNode With {.Id = Index, .Address = Address, .AllowRelay = True})
            Index += 1
        Next
        Cached = Result
        CachedAt = Date.Now
        CachedSource = CacheSources.Fallback
        Logger.Warn($"联机节点：云端与备份都不可用，使用内置的 {Result.Count} 个")
        Return Result
    End Function

    ''' <summary>
    ''' 判断一个地址是否在云端清单里。
    ''' <para>
    ''' 邀请码会带上房主实际使用的节点地址。地址如果在公开清单里，
    ''' 说明是大家共用的公益节点，直接连；不在清单里就是别人自建的，
    ''' 由调用方决定要不要提醒风险。
    ''' </para>
    ''' <para>
    ''' 必须在工作线程调用。清单拉不到时返回 <see cref="KnownResult.Unknown"/>，
    ''' 而不是「不在清单里」——拉不到清单是我们自己的问题，
    ''' 不该因此把正常节点报成可疑节点。
    ''' </para>
    ''' </summary>
    Public Enum KnownResult
        ''' <summary>在云端清单里。</summary>
        Known
        ''' <summary>清单拉到了，但里面没有这个地址。</summary>
        Unknown
        ''' <summary>清单没拉到，无法判断。</summary>
        Unavailable
    End Enum

    Public Function CheckKnown(Address As String) As KnownResult
        If String.IsNullOrWhiteSpace(Address) Then Return KnownResult.Unavailable
        Dim Nodes As List(Of LinkNode)
        Try
            Nodes = Fetch()
        Catch ex As Exception
            Logger.Warn(ex, "判断节点是否在清单里时拉取失败")
            Return KnownResult.Unavailable
        End Try
        If Nodes Is Nothing OrElse Nodes.Count = 0 Then Return KnownResult.Unavailable
        '只认云端拉到的清单。Fetch 在云端失败时会退到本地备份和内置兜底，
        '那份数据可能早就过期了，不能用来判定「这个地址是可信的」。
        If CachedSource <> CacheSources.Cloud Then Return KnownResult.Unavailable
        For Each Node In Nodes
            If String.Equals(Node.Address, Address.Trim(), StringComparison.OrdinalIgnoreCase) Then Return KnownResult.Known
        Next
        Return KnownResult.Unknown
    End Function

    ''' <summary>当前内存缓存里的清单是从哪来的。</summary>
    Private Enum CacheSources
        None
        Cloud
        Backup
        Fallback
    End Enum
    Private CachedSource As CacheSources = CacheSources.None

    ''' <summary>拉云端清单；逐个尝试候选地址，全失败返回 Nothing。</summary>
    Private Function TryFetchCloud() As List(Of LinkNode)
        For Each Url In NodesUrls
            Try
                Dim Json As String = NetRequestByClient(Url, RequireJson:=False)
                If String.IsNullOrWhiteSpace(Json) OrElse Json.Length > MaxBytes Then Continue For
                Dim Nodes As List(Of LinkNode) = Parse(Json)
                If Nodes IsNot Nothing AndAlso Nodes.Count > 0 Then
                    Logger.Info($"联机节点：从 {Url.Split("/"c)(2)} 获取成功，共 {Nodes.Count} 个")
                    Return Nodes
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"从 {Url.Split("/"c)(2)} 拉取节点清单失败")
            End Try
        Next
        Return Nothing
    End Function

    ''' <summary>解析清单。格式坏的条目跳过，不影响其它条目。</summary>
    Private Function Parse(Json As String) As List(Of LinkNode)
        Dim Result As New List(Of LinkNode)
        Try
            Dim Arr As JArray = JArray.Parse(Json)
            Dim Index As Integer = 1
            For Each Item As JToken In Arr
                Try
                    Dim Obj As JObject = TryCast(Item, JObject)
                    If Obj Is Nothing Then Continue For
                    Dim Address As String = If(Obj("address"), "").ToString().Trim()
                    If Address = "" Then Continue For
                    If Not (Address.StartsWithF("tcp://") OrElse Address.StartsWithF("udp://") OrElse
                            Address.StartsWithF("ws://") OrElse Address.StartsWithF("wss://")) Then Continue For
                    Dim Node As New LinkNode With {
                        .Id = If(Obj("id") Is Nothing, Index, Obj("id").ToObject(Of Integer)),
                        .Address = Address,
                        .AllowRelay = If(Obj("allow_relay") Is Nothing, True, Obj("allow_relay").ToObject(Of Boolean))}
                    Dim Tags = TryCast(Obj("tags"), JArray)
                    If Tags IsNot Nothing Then
                        For Each T In Tags
                            Dim S As String = T.ToString().Trim()
                            If S <> "" Then Node.Tags.Add(S)
                        Next
                    End If
                    Result.Add(Node)
                    Index += 1
                Catch ex As Exception
                    Logger.Warn(ex, "跳过一条格式异常的联机节点")
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析联机节点清单失败")
        End Try
        Return Result
    End Function

    ''' <summary>写一份带时间戳的备份，最多留 MaxBackup 份。</summary>
    Private Sub SaveBackup(Nodes As List(Of LinkNode))
        Try
            DirectoryUtils.Create(BackupFolder)
            Dim Arr As New JArray
            For Each N In Nodes
                Arr.Add(New JObject From {
                    {"id", N.Id}, {"address", N.Address}, {"allow_relay", N.AllowRelay},
                    {"tags", New JArray(N.Tags.ToArray())}})
            Next
            FileUtils.WriteAtomic(BackupFolder & "nodes-" & Date.Now.ToString("yyyyMMddHHmmss") & ".json",
                                  Arr.ToString(Newtonsoft.Json.Formatting.None))
            Dim Files = DirectoryUtils.EnumerateFiles(BackupFolder, False, "nodes-*.json").
                OrderByDescending(Function(F) F).ToList()
            For i As Integer = MaxBackup To Files.Count - 1
                Try
                    FileUtils.Delete(Files(i))
                Catch ex As Exception
                    Logger.Warn(ex, "清理旧的联机节点备份失败")
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, "保存联机节点备份失败")
        End Try
    End Sub

    ''' <summary>读最近一份能解析的备份。</summary>
    Private Function LoadBackup() As List(Of LinkNode)
        Try
            If Not DirectoryUtils.Exists(BackupFolder) Then Return Nothing
            Dim Files = DirectoryUtils.EnumerateFiles(BackupFolder, False, "nodes-*.json").
                OrderByDescending(Function(F) F).ToList()
            For Each F In Files
                Try
                    Dim Nodes = Parse(FileUtils.ReadAsString(F))
                    If Nodes.Count > 0 Then Return Nodes
                Catch ex As Exception
                    Logger.Warn(ex, $"读取联机节点备份失败：{F}")
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, "枚举联机节点备份失败")
        End Try
        Return Nothing
    End Function


    ''' <summary>
    ''' 并发探测每个节点的 TCP 握手耗时，结果写回 <see cref="LinkNode.Latency"/>。
    ''' 只测 tcp:// 与 ws:// 这类能直接建 TCP 的地址，其余保持 -1。
    ''' </summary>
    ''' <param name="Nodes">要探测的节点，结果原地写回。</param>
    ''' <param name="TimeoutMs">单个节点的超时。</param>
    Public Sub ProbeLatency(Nodes As List(Of LinkNode), Optional TimeoutMs As Integer = 2000)
        If Nodes Is Nothing OrElse Nodes.Count = 0 Then Return
        Dim Tasks As New List(Of Threading.Tasks.Task)
        For Each Node In Nodes
            Dim Target As LinkNode = Node
            Tasks.Add(Threading.Tasks.Task.Run(
                Sub()
                    Target.Latency = ProbeOne(Target.Address, TimeoutMs)
                End Sub))
        Next
        Try
            Threading.Tasks.Task.WaitAll(Tasks.ToArray, TimeoutMs + 1000)
        Catch ex As Exception
            Logger.Warn(ex, "探测联机节点延迟失败")
        End Try
    End Sub

    ''' <summary>测单个节点；失败返回 -1。</summary>
    Private Function ProbeOne(Address As String, TimeoutMs As Integer) As Integer
        Try
            '只处理能解析出 host:port 的地址
            Dim Raw As String = Address
            Dim SchemeEnd As Integer = Raw.IndexOf("://", StringComparison.Ordinal)
            If SchemeEnd < 0 Then Return -1
            Dim Scheme As String = Raw.Substring(0, SchemeEnd).ToLowerInvariant()
            If Scheme <> "tcp" AndAlso Scheme <> "ws" AndAlso Scheme <> "wss" Then Return -1
            Dim Rest As String = Raw.Substring(SchemeEnd + 3).Split("/"c)(0)
            Dim Parts As String() = Rest.Split(":"c)
            If Parts.Length < 2 Then Return -1
            Dim Host As String = Parts(0)
            Dim Port As Integer
            If Not Integer.TryParse(Parts(1), Port) Then Return -1
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            Using Client As New Net.Sockets.TcpClient()
                Dim Ar = Client.BeginConnect(Host, Port, Nothing, Nothing)
                If Not Ar.AsyncWaitHandle.WaitOne(TimeoutMs) Then Return -1
                Client.EndConnect(Ar)
            End Using
            Watch.Stop()
            Return CInt(Watch.ElapsedMilliseconds)
        Catch ex As Exception
            Return -1
        End Try
    End Function
End Module
