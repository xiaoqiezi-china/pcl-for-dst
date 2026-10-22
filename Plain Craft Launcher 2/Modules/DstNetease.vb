Imports System.Net.WebSockets
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 网易云音乐接入。
''' 登录方式：用系统里的 Chromium 浏览器（Edge/Chrome）以独立配置目录 + 调试端口打开网易云，
''' 用户在里面正常登录，我们从调试端口读回 Cookie（含 HttpOnly 的 MUSIC_U），不碰用户自己的浏览器数据。
''' 歌单与歌曲只存 ID，落在 PCL\DstNetease.json，重启后继续可用。
''' </summary>
Public Module DstNetease

    '==== 本地存储 ====
    Public ReadOnly Property NeSavePath As String
        Get
            Return Paths.Base & "PCL\DstNetease.json"
        End Get
    End Property

    ''' <summary>Cookie 名值对。</summary>
    Public ReadOnly NeCookies As New Dictionary(Of String, String)
    ''' <summary>登录用户的 UID，未登录为 0。</summary>
    Public NeUserId As Long = 0
    ''' <summary>登录用户的昵称。</summary>
    Public NeNickname As String = ""
    ''' <summary>VIP 类型：0 非会员，10/11 为黑胶 VIP（由账号接口返回）。</summary>
    Public NeVipType As Integer = 0

    ''' <summary>是否 VIP 账号。</summary>
    Public ReadOnly Property NeIsVip As Boolean
        Get
            Return NeVipType > 0
        End Get
    End Property

    ''' <summary>用户选择的默认音质（standard / higher / exhigh / lossless）。</summary>
    Public ReadOnly Property NeMusicLevel As String
        Get
            Dim Level As String = Settings.Get(Of String)("DstNeMusicLevel")
            Return If(String.IsNullOrWhiteSpace(Level), "standard", Level)
        End Get
    End Property

    ''' <summary>
    ''' 实际请求用的音质，直接取用户选择。
    ''' 实测非会员同样能拿到 320K，VIP 是按曲目限制的，所以这里不做账户级降级：
    ''' 原样请求，服务端会返回当前账号能拿到的最高档（比如要 flac 可能回 320K mp3）。
    ''' </summary>
    Public ReadOnly Property NeEffectiveLevel As String
        Get
            Return NeMusicLevel
        End Get
    End Property

    ''' <summary>音质对应的编码格式。</summary>
    Private ReadOnly Property NeEncodeType As String
        Get
            Select Case NeEffectiveLevel
                Case "lossless" : Return "flac"
                Case "exhigh", "higher" : Return "mp3"
                Case Else : Return "aac"
            End Select
        End Get
    End Property

    Public ReadOnly Property NeIsLoggedIn As Boolean
        Get
            Return NeCookies.ContainsKey("MUSIC_U") AndAlso NeCookies("MUSIC_U") <> ""
        End Get
    End Property

    ''' <summary>拼成 Cookie 请求头。</summary>
    Public Function NeCookieHeader() As String
        If NeCookies.Count = 0 Then Return ""
        Return String.Join("; ", NeCookies.Select(Function(K) K.Key & "=" & K.Value))
    End Function

    Private Loaded As Boolean = False
    Private ReadOnly SaveLock As New Object

    ''' <summary>从本地读回会话与歌单。</summary>
    Public Sub NeEnsureLoaded()
        If Loaded Then Return
        Loaded = True
        Try
            If Not FileUtils.Exists(NeSavePath) Then Return
            Dim Obj As JObject = JObject.Parse(FileUtils.ReadAsString(NeSavePath))
            NeCookies.Clear()
            Dim C = Obj("cookies")
            If C IsNot Nothing Then
                For Each One As JProperty In C.Children(Of JProperty)()
                    NeCookies(One.Name) = If(One.Value, "").ToString()
                Next
            End If
            NeUserId = Val(If(Obj("userId"), "0").ToString())
            NeNickname = If(Obj("nickname"), "").ToString()

            NeVipType = CInt(Val(If(Obj("vipType"), "0").ToString()))
            '歌单缓存
            SyncLock PlaylistLock
                NePlaylists.Clear()
                Dim Pl = Obj("playlists")
                If Pl IsNot Nothing Then
                    For Each One As JToken In Pl
                        NePlaylists.Add(New NeteasePlaylist With {
                            .Id = Val(If(One("id"), "0").ToString()),
                            .Name = If(One("name"), "").ToString(),
                            .TrackCount = Val(If(One("count"), "0").ToString()),
                            .Cover = If(One("cover"), "").ToString(),
                            .TrackIds = If(One("tracks"), "").ToString()
                        })
                    Next
                End If
            End SyncLock
            '播放队列
            SyncLock NeSongs
                NeQueue.Clear()
                Dim Q = Obj("queue")
                If Q IsNot Nothing Then
                    For Each One As JToken In Q
                        Dim Id As Long = Val(One.ToString())
                        If Id > 0 Then NeQueue.Add(Id)
                    Next
                End If
            End SyncLock
            '歌曲元数据
            SyncLock NeSongs
                NeSongs.Clear()
                Dim S = Obj("songs")
                If S IsNot Nothing Then
                    For Each One As JProperty In S.Children(Of JProperty)()
                        Dim Id As Long = Val(One.Name)
                        If Id <= 0 Then Continue For
                        Dim O As JObject = CType(One.Value, JObject)
                        NeSongs(Id) = New NeteaseSong With {
                            .Id = Id,
                            .Name = If(O("n"), "").ToString(),
                            .Artist = If(O("a"), "").ToString(),
                            .DurationMs = CInt(Val(If(O("d"), "0").ToString())),
                            .Cover = If(O("l"), "").ToString(),
                            .Fee = CInt(Val(If(O("f"), "0").ToString())),
                            .Status = If(String.IsNullOrWhiteSpace(If(O("s"), "").ToString()), "ok", O("s").ToString()),
                            .Probed = If(O("p") Is Nothing, False, Val(O("p").ToString()) = 1)
                        }
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, "读取网易云本地数据失败")
        End Try
    End Sub

    ''' <summary>把会话与歌单写回本地。</summary>
    Public Sub NeSaveLocal()
        '没读过文件就写，会把磁盘上的队列直接覆盖成空——先加载再写
        NeEnsureLoaded()
        SyncLock SaveLock
            Try
                Dim Obj As New JObject
                Obj("cookies") = JObject.FromObject(NeCookies)
                Obj("userId") = NeUserId
                Obj("nickname") = NeNickname

                Obj("vipType") = NeVipType
                Dim Arr As New JArray
                SyncLock PlaylistLock
                    For Each One In NePlaylists
                        Arr.Add(New JObject From {
                            {"id", One.Id}, {"name", One.Name}, {"count", One.TrackCount},
                            {"cover", One.Cover}, {"tracks", One.TrackIds}
                        })
                    Next
                End SyncLock
                Obj("playlists") = Arr
                Dim SongObj As New JObject
                SyncLock NeSongs
                    For Each One In NeSongs.Values
                        SongObj(One.Id.ToString()) = New JObject From {
                            {"n", One.Name}, {"a", One.Artist}, {"d", One.DurationMs}, {"l", One.Cover},
                            {"f", One.Fee}, {"s", One.Status}, {"p", One.Probed}
                        }
                    Next
                End SyncLock
                Obj("songs") = SongObj
                Dim Q As New JArray
                SyncLock NeSongs
                    For Each Id In NeQueue
                        Q.Add(Id)
                    Next
                End SyncLock
                Obj("queue") = Q
                Logger.Info("网易云数据保存：队列 " & NeQueue.Count & " 首，歌曲信息 " & NeSongs.Count & " 条")
                DirectoryUtils.Create(Paths.Base & "PCL")
                FileUtils.Write(NeSavePath, Obj.ToString(Newtonsoft.Json.Formatting.Indented))
            Catch ex As Exception
                Logger.Warn(ex, "保存网易云本地数据失败")
            End Try
        End SyncLock
    End Sub

#Region "歌单与歌曲（只存 ID）"

    Public Class NeteasePlaylist
        Public Property Id As Long
        Public Property Name As String = ""
        Public Property TrackCount As Integer
        Public Property Cover As String = ""
        ''' <summary>歌曲 ID，逗号分隔，例如 ",186016,347230,"。</summary>
        Public Property TrackIds As String = ""
    End Class

    ''' <summary>网易云歌曲（只存 ID 与用于显示的元数据）。</summary>
    Public Class NeteaseSong
        Public Property Id As Long
        Public Property Name As String = ""
        Public Property Artist As String = ""
        Public Property DurationMs As Integer
        Public Property Cover As String = ""
        ''' <summary>0 免费，1 付费/VIP 专享，8 低音质免费等。</summary>
        Public Property Fee As Integer = 0
        ''' <summary>
        ''' 播放可用性："ok" 正常；"vip" 确认拿不到地址（付费专享）；"dead" 曲目失效/无版权。
        ''' 每次成功取到地址都会被清回 "ok"。
        ''' </summary>
        Public Property Status As String = "ok"
        ''' <summary>是否已经实际请求过一次播放地址（状态是否可信）。</summary>
        Public Property Probed As Boolean = False
    End Class

    ''' <summary>播放队列（有序，只存歌曲 ID）。</summary>
    Public ReadOnly NeQueue As New List(Of Long)

    Public Function NeQueueSnapshot() As List(Of Long)
        '不先加载的话，冷启动时内存队列是空的，界面就会显示没有歌
        NeEnsureLoaded()
        SyncLock NeSongs
            Return NeQueue.ToList()
        End SyncLock
    End Function

    ''' <summary>把歌曲加到队列末尾（已在队列里的跳过）。</summary>
    Public Sub NeQueueAdd(Ids As IEnumerable(Of Long))
        NeEnsureLoaded()
        Dim Added As Integer = 0
        SyncLock NeSongs
            For Each Id In Ids
                If Not NeQueue.Contains(Id) Then
                    NeQueue.Add(Id)
                    Added += 1
                End If
            Next
            Logger.Info("网易云加入队列：" & Added & " 首，现有 " & NeQueue.Count & " 首")
        End SyncLock
        NeSaveLocal()
    End Sub

    ''' <summary>一键清空播放列表（不动歌单与本地缓存文件）。</summary>
    Public Sub NeQueueClear()
        NeEnsureLoaded()
        SyncLock NeSongs
            NeQueue.Clear()
        End SyncLock
        NeSaveLocal()
    End Sub
    Public Sub NeQueueRemove(Id As Long)
        NeEnsureLoaded()
        SyncLock NeSongs
            NeQueue.Remove(Id)
        End SyncLock
        NeSaveLocal()
    End Sub

    ''' <summary>调整队列顺序。</summary>
    Public Sub NeQueueMove(FromIndex As Integer, ToIndex As Integer)
        NeEnsureLoaded()
        SyncLock NeSongs
            If FromIndex < 0 OrElse FromIndex >= NeQueue.Count Then Return
            If ToIndex < 0 Then ToIndex = 0
            If ToIndex >= NeQueue.Count Then ToIndex = NeQueue.Count - 1
            If FromIndex = ToIndex Then Return
            Dim Item As Long = NeQueue(FromIndex)
            NeQueue.RemoveAt(FromIndex)
            NeQueue.Insert(ToIndex, Item)
        End SyncLock
        NeSaveLocal()
    End Sub

    ''' <summary>搜索结果条目。</summary>
    Public Class NeSearchHit
        Public Property Id As Long
        Public Property Name As String = ""
        Public Property Info As String = ""
        Public Property IsPlaylist As Boolean = False
    End Class
    ''' <summary>歌曲 ID → 歌曲信息。</summary>
    Public ReadOnly NeSongs As New Dictionary(Of Long, NeteaseSong)
    Public ReadOnly NePlaylists As New List(Of NeteasePlaylist)
    Private ReadOnly PlaylistLock As New Object

    Public Function NeGetPlaylistsSnapshot() As List(Of NeteasePlaylist)
        SyncLock PlaylistLock
            Return NePlaylists.ToList()
        End SyncLock
    End Function

    ''' <summary>取歌曲 ID 列表。</summary>
    Public Function NeParseTrackIds(Raw As String) As List(Of Long)
        Dim Result As New List(Of Long)
        If String.IsNullOrWhiteSpace(Raw) Then Return Result
        For Each Part In Raw.Split(","c)
            Dim Id As Long
            If Long.TryParse(Part.Trim(), Id) AndAlso Id > 0 AndAlso Not Result.Contains(Id) Then Result.Add(Id)
        Next
        Return Result
    End Function

    ''' <summary>把歌单里的歌曲并入本地歌单（纯本地存储）。</summary>
    Public Sub NeAddTracks(PlaylistId As Long, TrackIds As IEnumerable(Of Long))
        NeEnsureLoaded()
        SyncLock PlaylistLock
            Dim Target As NeteasePlaylist = NePlaylists.FirstOrDefault(Function(P) P.Id = PlaylistId)
            If Target Is Nothing Then Return
            Dim Current As List(Of Long) = NeParseTrackIds(Target.TrackIds)
            For Each Id In TrackIds
                If Not Current.Contains(Id) Then Current.Add(Id)
            Next
            Target.TrackIds = "," & String.Join(",", Current) & ","
        End SyncLock
        NeSaveLocal()
    End Sub

    ''' <summary>用服务器上的歌单刷新本地缓存，保留已加入的歌曲。</summary>
    Public Sub NeUpsertPlaylist(Id As Long, Name As String, Count As Integer, Cover As String)
        NeEnsureLoaded()
        SyncLock PlaylistLock
            Dim Target As NeteasePlaylist = NePlaylists.FirstOrDefault(Function(P) P.Id = Id)
            If Target Is Nothing Then
                NePlaylists.Add(New NeteasePlaylist With {.Id = Id, .Name = Name, .TrackCount = Count, .Cover = Cover})
            Else
                Target.Name = Name
                Target.TrackCount = Count
                Target.Cover = Cover
            End If
        End SyncLock
    End Sub

#End Region

#Region "浏览器登录"

    Private LoginPort As Integer = 0
    Private LoginProcess As Process = Nothing
    Private LoginThread As Threading.Thread = Nothing
    Private LoginCancel As Boolean = False

    ''' <summary>找一个可用的 Chromium 浏览器。</summary>
    Public Function FindBrowser() As String
        Dim Candidates As String() = {
            Environment.GetEnvironmentVariable("ProgramFiles") & "\Google\Chrome\Application\chrome.exe",
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") & "\Google\Chrome\Application\chrome.exe",
            Environment.GetEnvironmentVariable("ProgramFiles") & "\Microsoft\Edge\Application\msedge.exe",
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") & "\Microsoft\Edge\Application\msedge.exe",
            Environment.GetEnvironmentVariable("LocalAppData") & "\Google\Chrome\Application\chrome.exe"
        }
        For Each Path In Candidates
            If Not String.IsNullOrWhiteSpace(Path) AndAlso FileUtils.Exists(Path) Then Return Path
        Next
        Return ""
    End Function

    Private Function GetFreePort() As Integer
        Dim Listener As New Net.Sockets.TcpListener(Net.IPAddress.Loopback, 0)
        Listener.Start()
        Dim Port As Integer = CType(Listener.LocalEndpoint, Net.IPEndPoint).Port
        Listener.Stop()
        Return Port
    End Function

    ''' <summary>
    ''' 打开独立配置的浏览器并等待用户登录。登录成功后写回本地并回调。
    ''' OnDone 在 UI 线程执行，参数为错误信息（成功为空）。
    ''' </summary>
    Public Sub StartLogin(OnDone As Action(Of String))
        NeEnsureLoaded()
        Dim Browser As String = FindBrowser()
        If Browser = "" Then
            RunInUi(Sub() OnDone("没有找到 Edge 或 Chrome，无法打开登录页面"))
            Return
        End If
        If LoginThread IsNot Nothing AndAlso LoginThread.IsAlive Then
            RunInUi(Sub() OnDone("登录窗口已经打开了，去浏览器里完成登录"))
            Return
        End If
        LoginPort = GetFreePort()
        LoginCancel = False
        Dim ProfileDir As String = Paths.Base & "PCL\DstNetease\Profile"
        DirectoryUtils.Create(ProfileDir)
        Try
            LoginProcess = Process.Start(New ProcessStartInfo With {
                .FileName = Browser,
                .Arguments = $"--remote-debugging-port={LoginPort} --user-data-dir=""{ProfileDir}"" " &
                             "--no-first-run --no-default-browser-check https://music.163.com/",
                .UseShellExecute = False
            })
        Catch ex As Exception
            Logger.Warn(ex, "打开登录浏览器失败")
            RunInUi(Sub() OnDone("打开浏览器失败：" & ex.Message))
            Return
        End Try
        LoginThread = New Threading.Thread(
            Sub()
                Dim Err As String = WaitForLogin()
                RunInUi(Sub() OnDone(Err))
            End Sub) With {.IsBackground = True}
        LoginThread.Start()
    End Sub

    ''' <summary>
    ''' 手动读一次当前浏览器会话（用户点按钮时调用）。
    ''' 返回错误信息，成功返回空串。必须在工作线程调用。
    ''' </summary>
    Public Function NeGrabNow(Port As Integer) As String
        If Port <= 0 Then Return "登录窗口还没打开过，先点登录按钮"
        LoginPort = Port
        Try
            Dim Grabbed As Dictionary(Of String, String) = GrabCookies()
            If Grabbed.Count = 0 Then Return "调试端口没返回任何 Cookie"
            If Not Grabbed.ContainsKey("MUSIC_U") OrElse Grabbed("MUSIC_U") = "" Then
                Return "没读到 MUSIC_U，请确认浏览器窗口里已经登录成功"
            End If
            NeCookies.Clear()
            For Each One In Grabbed
                NeCookies(One.Key) = One.Value
            Next
            FetchAccount()
            NeSaveLocal()
            If NeUserId = 0 Then Return "已读到 Cookie，但没取到账号信息，请查看日志里的接口返回"
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "读取网易云会话失败")
            Return "读取失败：" & ex.Message
        End Try
    End Function

    ''' <summary>当前登录窗口用的调试端口，0 表示没开过。</summary>
    Public ReadOnly Property NeLoginPort As Integer
        Get
            Return LoginPort
        End Get
    End Property
    ''' <summary>取消等待并关掉登录窗口。</summary>
    Public Sub CancelLogin()
        LoginCancel = True
        Try
            If LoginProcess IsNot Nothing AndAlso Not LoginProcess.HasExited Then LoginProcess.Kill()
        Catch ex As Exception
        End Try
        LoginProcess = Nothing
    End Sub

    ''' <summary>轮询调试端口，直到拿到 MUSIC_U 或超时。</summary>
    Private Function WaitForLogin() As String
        Const TimeoutMs As Integer = 300000
        Dim Start As Long = GetTimeMs()
        '等 CDP 起来
        While GetTimeMs() - Start < 20000
            If LoginCancel Then Return "已取消登录"
            Try
                NeCdpGet("/json/version", 2000)
                Exit While
            Catch
            End Try
            Threading.Thread.Sleep(400)
        End While
        While GetTimeMs() - Start < TimeoutMs
            If LoginCancel Then Return "已取消登录"
            Try
                Dim Grabbed As Dictionary(Of String, String) = GrabCookies()
                If Grabbed.ContainsKey("MUSIC_U") AndAlso Grabbed("MUSIC_U") <> "" Then
                    NeCookies.Clear()
                    For Each One In Grabbed
                        NeCookies(One.Key) = One.Value
                    Next
                    FetchAccount()
                    NeSaveLocal()
                    Return ""
                End If
            Catch ex As Exception
                Logger.Warn(ex, "读取网易云登录状态失败")
            End Try
            Threading.Thread.Sleep(1500)
        End While
        Return "等待登录超时，可以重新点一次登录"
    End Function

    ''' <summary>
    ''' 读调试端口的 HTTP 接口。
    ''' 必须绕开系统代理：走 PCL 的封装时，如果系统设了代理，127.0.0.1 的请求可能被代理吞掉，
    ''' 表现出来就是「一直读不到登录状态」。
    ''' </summary>
    Private Function NeCdpGet(Path As String, Optional TimeoutMs As Integer = 4000) As String
        Dim Req As Net.HttpWebRequest =
            CType(Net.WebRequest.Create($"http://127.0.0.1:{LoginPort}{Path}"), Net.HttpWebRequest)
        Req.Method = "GET"
        Req.Proxy = Nothing
        Req.Timeout = TimeoutMs
        Req.ReadWriteTimeout = TimeoutMs
        Req.KeepAlive = False
        Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
            Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                Return Reader.ReadToEnd()
            End Using
        End Using
    End Function
    ''' <summary>通过 CDP 读取 music.163.com 的 Cookie（含 HttpOnly）。</summary>
    Private Function GrabCookies() As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)
        Dim Targets As JArray = JArray.Parse(NeCdpGet("/json/list", 4000))
        Dim Page As JObject = Nothing
        For Each One As JObject In Targets
            If One("type")?.ToString() = "page" AndAlso One("url")?.ToString().Contains("music.163.com") Then
                Page = One
                Exit For
            End If
        Next
        If Page Is Nothing Then Return Result
        Using Ws As New ClientWebSocket
            Dim Ct As Threading.CancellationToken = Threading.CancellationToken.None
            'Wait 返回 False 表示超时，此时连接还没建立，继续发请求只会抛异常
            If Not Ws.ConnectAsync(New Uri(Page("webSocketDebuggerUrl").ToString()), Ct).Wait(8000) Then
                Logger.Warn("网易云 CDP 连接超时")
                Return Result
            End If
            Dim Body As Byte() = Encoding.UTF8.GetBytes(
                "{""id"":1,""method"":""Network.getCookies"",""params"":{""urls"":[""https://music.163.com""]}}")
            If Not Ws.SendAsync(New ArraySegment(Of Byte)(Body), Net.WebSockets.WebSocketMessageType.Text, True, Ct).Wait(4000) Then
                Logger.Warn("网易云 CDP 发送请求超时")
                Return Result
            End If
            Dim Buffer(65535) As Byte
            Dim Task = Ws.ReceiveAsync(New ArraySegment(Of Byte)(Buffer), Ct)
            If Not Task.Wait(8000) Then Return Result
            Dim Text As String = Encoding.UTF8.GetString(Buffer, 0, Task.Result.Count)
            Dim Obj As JObject = JObject.Parse(Text)
            Dim CookieArr = Obj("result")?("cookies")
            If CookieArr IsNot Nothing Then
                Dim Names As New List(Of String)
                For Each One As JToken In CookieArr
                    Dim Name As String = If(One("name"), "").ToString()
                    If Name <> "" Then
                        Result(Name) = If(One("value"), "").ToString()
                        Names.Add(Name)
                    End If
                Next
                Logger.Info("网易云 CDP 读到 Cookie：" & String.Join(", ", Names))
            Else
                Logger.Warn("网易云 CDP 返回里没有 cookies 字段")
            End If
        End Using
        Return Result
    End Function

    ''' <summary>已登录但 UID 还空着时补取一次。必须在工作线程调用。</summary>
    Public Function NeRefreshAccount() As Boolean
        NeEnsureLoaded()
        If Not NeIsLoggedIn Then Return False
        FetchAccount()
        If NeUserId > 0 Then
            NeSaveLocal()
            Return True
        End If
        Return False
    End Function

    ''' <summary>用 Cookie 取账号信息。多个端点依次试，任一拿到 UID 就收工。</summary>
    Private Sub FetchAccount()
        Dim WasVip As Boolean = NeIsVip
        For Each Path In {"/api/nuser/account/get", "/api/w/nuser/account/get", "/api/v1/user/info"}
            Try
                Dim Raw As String = NeApiGet(Path)
                Logger.Info("网易云 " & Path & " 返回：" & SafeText(Raw).Substring(0, Math.Min(160, SafeText(Raw).Length)))
                Dim Obj As JObject = JObject.Parse(Raw)
                Dim Account = Obj("account")
                Dim Profile = Obj("profile")
                If Account IsNot Nothing Then NeUserId = Val(If(Account("id"), "0").ToString())
                If Profile IsNot Nothing Then
                    Dim Name As String = If(Profile("nickname"), "").ToString()
                    If Name <> "" Then NeNickname = Name
                    Dim Id = Profile("userId")
                    If Id IsNot Nothing AndAlso NeUserId = 0 Then NeUserId = Val(Id.ToString())
                    If Profile("vipType") IsNot Nothing Then NeVipType = CInt(Val(Profile("vipType").ToString()))
                End If
                If Account IsNot Nothing AndAlso Account("vipType") IsNot Nothing Then
                    NeVipType = CInt(Val(Account("vipType").ToString()))
                End If
                If NeUserId = 0 AndAlso Obj("id") IsNot Nothing Then NeUserId = Val(Obj("id").ToString())
                If NeUserId > 0 Then
                    '刚开通 VIP：把之前按「非会员」判死的曲目放行
                    If Not WasVip AndAlso NeIsVip Then NeResetVipStatuses()
                    Return
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取网易云账号信息失败（" & Path & "）")
            End Try
        Next
    End Sub

    Public Sub NeLogout()
        NeEnsureLoaded()
        NeCookies.Clear()
        NeUserId = 0
        NeNickname = ""
        NeSaveLocal()
        '登录用的独立配置目录一起清掉，否则下次登录还是旧会话
        Try
            DirectoryUtils.Delete(Paths.Base & "PCL\DstNetease\Profile")
        Catch ex As Exception
        End Try
    End Sub

#End Region

#Region "接口"

    ''' <summary>
    ''' 发一个网易云请求。
    ''' .NET 不允许用 Headers.Add("Cookie", ...) 传 Cookie（受限标头，直接抛异常），
    ''' 必须走 CookieContainer，否则请求其实是不带登录态的匿名请求。
    ''' 必须在工作线程调用。
    ''' </summary>
    Private Function NeRequest(Path As String, Optional Method As String = "GET",
                               Optional Body As String = Nothing) As String
        Dim Req As Net.HttpWebRequest =
            CType(Net.WebRequest.Create("https://music.163.com" & Path), Net.HttpWebRequest)
        Req.Method = Method
        Req.Timeout = 15000
        Req.ReadWriteTimeout = 15000
        'Referer / Accept 属于受限标头，必须用属性赋值；
        '用 Headers("Referer") = ... 会抛 ArgumentException，请求直接失败（表现为拿不到账号信息）
        Req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"
        Req.Referer = "https://music.163.com/"
        Req.Accept = "*/*"
        Dim Jar As New Net.CookieContainer
        For Each One In NeCookies
            Try
                Jar.SetCookies(New Uri("https://music.163.com"), One.Key & "=" & One.Value)
            Catch ex As Exception
            End Try
        Next
        Req.CookieContainer = Jar
        If Body IsNot Nothing Then
            Req.ContentType = "application/x-www-form-urlencoded"
            Dim Data As Byte() = Text.Encoding.UTF8.GetBytes(Body)
            Req.ContentLength = Data.Length
            Using S As IO.Stream = Req.GetRequestStream()
                S.Write(Data, 0, Data.Length)
            End Using
        End If
        Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
            Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                Return Reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Function NeApiGet(Path As String) As String
        Return NeRequest(Path)
    End Function

    ''' <summary>拉取当前登录用户的歌单（只读接口，必须在工作线程调用）。</summary>
    Public Function NeFetchMyPlaylists() As String
        NeEnsureLoaded()
        If Not NeIsLoggedIn OrElse NeUserId = 0 Then Return "还没登录网易云"
        Try
            Dim Json As String = NeApiGet($"/api/user/playlist?uid={NeUserId}&limit=1000&offset=0")
            Dim Obj As JObject = JObject.Parse(Json)
            Dim Arr = Obj("playlist")
            If Arr Is Nothing Then Return "接口没有返回歌单"
            Dim Count As Integer = 0
            For Each One As JToken In Arr
                Dim Id As Long = Val(If(One("id"), "0").ToString())
                If Id = 0 Then Continue For
                NeUpsertPlaylist(Id,
                               If(One("name"), "").ToString(),
                               CInt(Val(If(One("trackCount"), "0").ToString())),
                               If(One("coverImgUrl"), "").ToString())
                Count += 1
            Next
            NeSaveLocal()
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "拉取网易云歌单失败")
            Return "拉取歌单失败：" & FriendlyError(ex.Message)
        End Try
    End Function

    ''' <summary>把某个歌单的全部歌曲 ID 并入本地（只在本地存 ID）。</summary>
    Public Function NeImportPlaylistTracks(PlaylistId As Long) As String
        NeEnsureLoaded()
        If Not NeIsLoggedIn Then Return "还没登录网易云"
        Try
            Dim Json As String = NeRequest("/api/v6/playlist/detail", "POST", "id=" & PlaylistId)
            Dim Obj As JObject = JObject.Parse(Json)
            Dim Pl = Obj("playlist")
            If Pl Is Nothing Then Return "接口没有返回歌单"
            '关键：tracks 是分页的（一次只给 10 首），完整的 ID 列表在 trackIds 里
            Dim Ids As New List(Of Long)
            Dim TrackIds = Pl("trackIds")
            If TrackIds IsNot Nothing Then
                For Each One As JToken In TrackIds
                    Dim Id As Long = Val(If(One("id"), "0").ToString())
                    If Id > 0 AndAlso Not Ids.Contains(Id) Then Ids.Add(Id)
                Next
            End If
            If Ids.Count = 0 Then
                Dim Tracks = Pl("tracks")
                If Tracks IsNot Nothing Then
                    For Each One As JToken In Tracks
                        Dim Id As Long = Val(If(One("id"), "0").ToString())
                        If Id > 0 Then Ids.Add(Id)
                    Next
                End If
            End If
            If Ids.Count = 0 Then Return "这个歌单里没有歌曲"
            NeAddTracks(PlaylistId, Ids)
            '顺便把歌曲名字取回来，列表才有东西显示
            NeFetchSongDetails(Nothing)
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "导入歌单失败")
            Return "导入歌单失败：" & FriendlyError(ex.Message)
        End Try
    End Function

    ''' <summary>本地记录里所有歌单涉及的歌曲 ID（去重）。</summary>
    Public Function NeAllTrackIds() As List(Of Long)
        NeEnsureLoaded()
        Dim Result As New List(Of Long)
        SyncLock PlaylistLock
            For Each One In NePlaylists
                For Each Id In NeParseTrackIds(One.TrackIds)
                    If Not Result.Contains(Id) Then Result.Add(Id)
                Next
            Next
        End SyncLock
        Return Result
    End Function

    ''' <summary>补齐缺失的歌曲信息（名字/歌手/时长）。必须在工作线程调用。</summary>
    Public Function NeFetchSongDetails(Optional Force As Boolean = False) As String
        NeEnsureLoaded()
        '队列里可能有用搜索添加的歌（不属于任何歌单），这里必须一起算进来，
        '否则它们永远拿不到歌名，界面只能显示「歌曲 ID」
        Dim All As New List(Of Long)
        For Each Id In NeAllTrackIds()
            If Not All.Contains(Id) Then All.Add(Id)
        Next
        SyncLock NeSongs
            For Each Id In NeQueue
                If Not All.Contains(Id) Then All.Add(Id)
            Next
        End SyncLock
        Dim Need As New List(Of Long)
        For Each Id In All
            Dim Known As Boolean
            SyncLock NeSongs
                Known = NeSongs.ContainsKey(Id)
            End SyncLock
            If Force OrElse Not Known Then Need.Add(Id)
        Next
        If Need.Count = 0 Then Return ""
        Try
            '接口一次能带一批，控制长度避免 URL 过长
            For Start As Integer = 0 To Need.Count - 1 Step 200
                Dim Batch As List(Of Long) = Need.Skip(Start).Take(200).ToList()
                '方括号必须转义，否则 WebRequest 会当成非法 URI 直接抛异常，元数据永远取不回来
                Dim IdsJson As String = Uri.EscapeDataString("[" & String.Join(",", Batch) & "]")
                Dim Json As String = NeApiGet("/api/song/detail?ids=" & IdsJson)
                Dim Obj As JObject = JObject.Parse(Json)
                Dim Songs = Obj("songs")
                If Songs Is Nothing OrElse Songs.Count = 0 Then
                    Logger.Info("网易云歌曲详情没有返回数据：ids=" & String.Join(",", Batch) &
                                " 响应=" & SafeText(Json).Substring(0, Math.Min(120, SafeText(Json).Length)))
                    Continue For
                End If
                SyncLock NeSongs
                    For Each One As JToken In Songs
                        Dim Id As Long = Val(If(One("id"), "0").ToString())
                        If Id <= 0 Then Continue For
                        '歌手字段：旧接口是 artists，新接口是 ar
                        Dim Artists As New List(Of String)
                        Dim ArtistArr = If(One("ar") IsNot Nothing, One("ar"), One("artists"))
                        If ArtistArr IsNot Nothing Then
                            For Each A As JToken In ArtistArr
                                Dim N As String = If(A("name"), "").ToString()
                                If N <> "" Then Artists.Add(N)
                            Next
                        End If
                        '专辑字段与时长同理
                        Dim Album = If(One("al") IsNot Nothing, One("al"), One("album"))
                        Dim Duration As String = If(One("dt") IsNot Nothing, One("dt").ToString(),
                                                     If(One("duration"), "0").ToString())
                        '保留已有的可播状态，别被重新拉详情冲掉
                        Dim Prev As NeteaseSong = Nothing
                        NeSongs.TryGetValue(Id, Prev)
                        NeSongs(Id) = New NeteaseSong With {
                            .Id = Id,
                            .Name = If(One("name"), "").ToString(),
                            .Artist = String.Join(" / ", Artists),
                            .DurationMs = CInt(Val(Duration)),
                            .Cover = If(Album?("picUrl"), "").ToString(),
                            .Fee = CInt(Val(If(One("fee"), "0").ToString())),
                            .Status = If(Prev Is Nothing, "ok", Prev.Status),
                            .Probed = If(Prev Is Nothing, False, Prev.Probed)
                        }
                    Next
                End SyncLock
            Next
            NeSaveLocal()
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "拉取网易云歌曲信息失败")
            Return "拉取歌曲信息失败：" & FriendlyError(ex.Message)
        End Try
    End Function

    ''' <summary>歌曲显示名，取不到名字就退回 ID。</summary>
    Public Function NeSongTitle(Id As Long) As String
        NeEnsureLoaded()
        SyncLock NeSongs
            Dim S As NeteaseSong = Nothing
            If NeSongs.TryGetValue(Id, S) Then
                Return If(S.Artist = "", S.Name, S.Name & " - " & S.Artist)
            End If
        End SyncLock
        Return "歌曲 " & Id
    End Function
    ''' <summary>搜索歌曲。必须在工作线程调用。</summary>
    Public Function NeSearchSongs(Keyword As String, Optional Limit As Integer = 20) As List(Of NeSearchHit)
        Dim Result As New List(Of NeSearchHit)
        If String.IsNullOrWhiteSpace(Keyword) Then Return Result
        Try
            Dim Json As String = NeApiGet("/api/search/get?s=" & Uri.EscapeDataString(Keyword) &
                                          "&type=1&limit=" & Limit & "&offset=0")
            Dim Obj As JObject = JObject.Parse(Json)
            Dim Songs = Obj("result")?("songs")
            If Songs Is Nothing Then Return Result
            For Each One As JToken In Songs
                Dim Artists As New List(Of String)
                Dim Ar = One("artists")
                If Ar IsNot Nothing Then
                    For Each A As JToken In Ar
                        Artists.Add(If(A("name"), "").ToString())
                    Next
                End If
                Result.Add(New NeSearchHit With {
                    .Id = Val(If(One("id"), "0").ToString()),
                    .Name = If(One("name"), "").ToString(),
                    .Info = String.Join(" / ", Artists) &
                            If(One("album")?("name") Is Nothing, "",
                               " · " & One("album")("name").ToString()),
                    .IsPlaylist = False
                })
            Next
        Catch ex As Exception
            Logger.Warn(ex, "搜索网易云歌曲失败")
        End Try
        Return Result
    End Function

    ''' <summary>搜索歌单。必须在工作线程调用。</summary>
    Public Function NeSearchPlaylists(Keyword As String, Optional Limit As Integer = 20) As List(Of NeSearchHit)
        Dim Result As New List(Of NeSearchHit)
        If String.IsNullOrWhiteSpace(Keyword) Then Return Result
        Try
            Dim Json As String = NeApiGet("/api/search/get?s=" & Uri.EscapeDataString(Keyword) &
                                          "&type=1000&limit=" & Limit & "&offset=0")
            Dim Obj As JObject = JObject.Parse(Json)
            Dim Pls = Obj("result")?("playlists")
            If Pls Is Nothing Then Return Result
            For Each One As JToken In Pls
                Result.Add(New NeSearchHit With {
                    .Id = Val(If(One("id"), "0").ToString()),
                    .Name = If(One("name"), "").ToString(),
                    .Info = "共 " & If(One("trackCount"), "0").ToString() & " 首" &
                            If(One("creator")?("nickname") Is Nothing, "",
                               " · " & One("creator")("nickname").ToString()),
                    .IsPlaylist = True
                })
            Next
        Catch ex As Exception
            Logger.Warn(ex, "搜索网易云歌单失败")
        End Try
        Return Result
    End Function

    ''' <summary>把搜索到的单曲加入队列，并补齐元数据。必须在工作线程调用。</summary>
    Public Function NeAddSong(Id As Long, Optional Name As String = "", Optional Artist As String = "") As String
        If Id <= 0 Then Return "无效的歌曲 ID"
        NeEnsureLoaded()
        '搜索结果本来就带着歌名，先存下来，界面立刻就能显示，不必等详情接口
        If Name <> "" Then
            SyncLock NeSongs
                Dim Prev As NeteaseSong = Nothing
                NeSongs.TryGetValue(Id, Prev)
                If Prev Is Nothing OrElse Prev.Name = "" Then
                    NeSongs(Id) = New NeteaseSong With {
                        .Id = Id, .Name = Name, .Artist = Artist,
                        .Status = If(Prev Is Nothing, "ok", Prev.Status)
                    }
                End If
            End SyncLock
            NeSaveLocal()
        End If
        NeQueueAdd(New List(Of Long) From {Id})
        NeFetchSongDetails(Nothing)
        Return ""
    End Function

    ''' <summary>把搜索到的歌单整份加入队列。必须在工作线程调用。</summary>
    Public Function NeAddPlaylistToQueue(PlaylistId As Long) As String
        Try
            Dim Json As String = NeRequest("/api/v6/playlist/detail", "POST", "id=" & PlaylistId)
            Dim Obj As JObject = JObject.Parse(Json)
            Dim Pl = Obj("playlist")
            If Pl Is Nothing Then Return "接口没有返回歌单"
            Dim Ids As New List(Of Long)
            Dim TrackIds = Pl("trackIds")
            If TrackIds IsNot Nothing Then
                For Each One As JToken In TrackIds
                    Dim Id As Long = Val(If(One("id"), "0").ToString())
                    If Id > 0 AndAlso Not Ids.Contains(Id) Then Ids.Add(Id)
                Next
            End If
            If Ids.Count = 0 Then Return "这个歌单里没有歌曲"
            NeQueueAdd(Ids)
            NeFetchSongDetails(Nothing)
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "把歌单加入播放列表失败")
            Return "加入失败：" & FriendlyError(ex.Message)
        End Try
    End Function
#Region "播放地址（eapi 签名）"

    'eapi 固定密钥
    Private Const EapiKey As String = "e82ckenh8dichen8"

    Private Function NeMd5Hex(Text As String) As String
        Using Md5 As Security.Cryptography.MD5 = Security.Cryptography.MD5.Create()
            Dim Hash As Byte() = Md5.ComputeHash(Encoding.UTF8.GetBytes(Text))
            Dim Sb As New Text.StringBuilder
            For Each B As Byte In Hash
                Sb.Append(B.ToString("x2"))
            Next
            Return Sb.ToString()
        End Using
    End Function

    ''' <summary>AES-128-ECB 加密并输出大写十六进制，eapi 用的就是这套。</summary>
    Private Function NeAesEcbHex(Plain As String) As String
        Using Aes As Security.Cryptography.Aes = Security.Cryptography.Aes.Create()
            Aes.Mode = Security.Cryptography.CipherMode.ECB
            Aes.Padding = Security.Cryptography.PaddingMode.PKCS7
            Aes.Key = Encoding.UTF8.GetBytes(EapiKey)
            Using Enc As Security.Cryptography.ICryptoTransform = Aes.CreateEncryptor()
                Dim Data As Byte() = Encoding.UTF8.GetBytes(Plain)
                Dim Out As Byte() = Enc.TransformFinalBlock(Data, 0, Data.Length)
                Dim Sb As New Text.StringBuilder
                For Each B As Byte In Out
                    Sb.Append(B.ToString("X2"))
                Next
                Return Sb.ToString()
            End Using
        End Using
    End Function

    ''' <summary>拼 eapi 的 params：明文是 path-36cd479b6b5-数据-36cd479b6b5-md5。</summary>
    Private Function NeEapiParams(Path As String, JsonData As String) As String
        Dim Digest As String = NeMd5Hex("nobody" & Path & "use" & JsonData & "md5forencrypt")
        Return NeAesEcbHex(Path & "-36cd479b6b5-" & JsonData & "-36cd479b6b5-" & Digest)
    End Function

    ''' <summary>
    ''' 取单曲播放地址。没有版权或需要会员时返回空串。
    ''' 地址有时效，每次播放都要重新取。必须在工作线程调用。
    ''' </summary>
    Public Function NeGetSongUrl(Id As Long) As String
        If Id <= 0 Then Return ""
        '必须先把本地会话读回内存，否则 NeCookies 是空的，请求会当成游客 → 全部 code=-110
        NeEnsureLoaded()
        If Not NeIsLoggedIn Then
            Logger.Info("网易云取地址时没有登录态，会拿不到高音质或直接失败")
        End If
        Const Path As String = "/api/song/enhance/player/url/v1"
        Try
            '有些曲目对特定音质/编码会返回 404，挨个组合试一遍，能拿到就用
            Dim Combos As New List(Of (Level As String, Encode As String)) From {
                (NeEffectiveLevel, NeEncodeType),
                ("standard", "mp3"),
                ("standard", "aac"),
                ("exhigh", "mp3")
            }
            For Each Combo In Combos.Distinct().ToList()
            Dim JsonData As String = "{""ids"":""[" & Id & "]"",""level"":""" & Combo.Level &
                                     """,""encodeType"":""" & Combo.Encode & """,""csrf_token"":""""}"
            Dim Params As String = NeEapiParams(Path, JsonData)
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create("https://music.163.com/eapi" & Path), Net.HttpWebRequest)
            Req.Method = "POST"
            Req.Proxy = Nothing
            Req.Timeout = 15000
            Req.ReadWriteTimeout = 15000
            Req.UserAgent = "Mozilla/5.0"
            Req.Referer = "https://music.163.com/"
            Req.Headers("os") = "pc"
            Req.Headers("appver") = "8.10.05"
            Req.Headers("osver") = "10.0.0"
            Req.Headers("deviceId") = "nmkbdzVdLzQpYqGn"
            Dim Jar As New Net.CookieContainer
            For Each One In NeCookies
                Try
                    Jar.SetCookies(New Uri("https://music.163.com"), One.Key & "=" & One.Value)
                Catch ex As Exception
                End Try
            Next
            Req.CookieContainer = Jar
            Req.ContentType = "application/x-www-form-urlencoded"
            Dim Body As Byte() = Encoding.UTF8.GetBytes("params=" & Uri.EscapeDataString(Params))
            Req.ContentLength = Body.Length
            Using S As IO.Stream = Req.GetRequestStream()
                S.Write(Body, 0, Body.Length)
            End Using
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                    Dim Raw As String = Reader.ReadToEnd()
                    Dim Obj As JObject = JObject.Parse(Raw)
                    Dim Data = Obj("data")
                    If Data Is Nothing OrElse Data.Count = 0 Then Return ""
                    Dim First As JToken = Data(0)
                    Dim Url As String = If(First("url"), "").ToString()
                    Dim Code As String = If(First("code"), "?").ToString()
                    If Url = "" Then
                        Logger.Info("网易云 " & Id & " 换个音质再试：" & Combo.Level & "/" & Combo.Encode &
                                    " code=" & Code)
                        Continue For
                    End If
                    Dim Trial As Boolean = First("freeTrialInfo") IsNot Nothing AndAlso
                                           First("freeTrialInfo").Type <> JTokenType.Null
                    NeLastUrlWasTrial = Trial
                    MarkSongStatus(Id, "ok")
                    If Trial Then Logger.Info("网易云 " & Id & " 只能播放试听片段")
                    Return Url
                End Using
            End Using
            Next
            '所有组合都拿不到
            MarkSongStatus(Id, "vip")
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "获取网易云播放地址失败（" & Id & "）")
            Return ""
        End Try
    End Function

    ''' <summary>缓存目录。</summary>
    Public ReadOnly Property NeCacheDir As String
        Get
            Return Paths.Base & "PCL\DstNetease\Cache\"
        End Get
    End Property

    ''' <summary>
    ''' 确保这首歌在本地有一份可播放的文件，返回本地路径；失败返回空串。
    ''' 地址每次现取，取不到就下不了。必须在工作线程调用。
    ''' </summary>
    Public Function NeEnsureSongFile(Id As Long, Optional OnProgress As Action(Of Long, Long) = Nothing) As String
        NeEnsureLoaded()
        DirectoryUtils.Create(NeCacheDir)
        '已经有缓存就直接用
        For Each Ext In {"m4a", "mp3"}
            Dim Cached As String = NeCacheDir & Id & "." & Ext
            If FileUtils.Exists(Cached) Then Return Cached
        Next
        Dim Url As String = NeGetSongUrl(Id)
        If Url = "" Then Return ""
        Dim Ext2 As String = If(Url.Contains(".mp3"), "mp3", "m4a")
        Dim Target As String = NeCacheDir & Id & "." & Ext2
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Proxy = Nothing
            Req.Timeout = 30000
            Req.ReadWriteTimeout = 60000
            Req.UserAgent = "Mozilla/5.0"
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Dim Total As Long = Resp.ContentLength
                Dim Received As Long = 0
                Using Src As IO.Stream = Resp.GetResponseStream(),
                      Dst As New IO.FileStream(Target, IO.FileMode.Create, IO.FileAccess.Write)
                    Dim Buffer(32767) As Byte
                    Do
                        Dim Read As Integer = Src.Read(Buffer, 0, Buffer.Length)
                        If Read <= 0 Then Exit Do
                        Dst.Write(Buffer, 0, Read)
                        Received += Read
                        If OnProgress IsNot Nothing Then OnProgress(Received, Total)
                    Loop
                End Using
            End Using
            Return Target
        Catch ex As Exception
            Logger.Warn(ex, "下载网易云歌曲失败（" & Id & "）")
            Return ""
        End Try
    End Function

#End Region
    ''' <summary>
    ''' 一键把自己所有歌单的歌曲并入队列。必须在工作线程调用。
    ''' 返回错误信息，成功为空串。
    ''' </summary>
    Public Function NeImportAllPlaylistsToQueue() As String
        Dim Err As String = NeFetchMyPlaylists()
        If Err <> "" Then Return Err
        Dim All As New List(Of Long)
        Dim Failed As Integer = 0
        For Each One In NeGetPlaylistsSnapshot()
            Try
                Dim Json As String = NeRequest("/api/v6/playlist/detail", "POST", "id=" & One.Id)
                Dim Obj As JObject = JObject.Parse(Json)
                Dim Pl = Obj("playlist")
                If Pl Is Nothing Then Continue For
                Dim TrackIds = Pl("trackIds")
                If TrackIds Is Nothing Then Continue For
                For Each Item As JToken In TrackIds
                    Dim Id As Long = Val(If(Item("id"), "0").ToString())
                    If Id > 0 AndAlso Not All.Contains(Id) Then All.Add(Id)
                Next
                '节流，连续请求容易触发风控
                Threading.Thread.Sleep(300)
            Catch ex As Exception
                Failed += 1
                Logger.Warn(ex, "导入歌单失败（" & One.Id & "）")
            End Try
        Next
        If All.Count = 0 Then Return If(Failed > 0, "全部歌单导入失败", "歌单里没有歌曲")
        NeQueueAdd(All)
        NeFetchSongDetails(Nothing)
        Return ""
    End Function
    ''' <summary>上一次取到的地址是不是试听片段。</summary>
    Public NeLastUrlWasTrial As Boolean = False

    ''' <summary>把因为「非会员」被标跳过的曲目全部放行，等下重新取地址时再判定。</summary>
    Public Sub NeResetVipStatuses()
        Dim Changed As Boolean = False
        SyncLock NeSongs
            For Each One In NeSongs.Values
                If One.Status = "vip" Then
                    One.Status = "ok"
                    Changed = True
                End If
            Next
        End SyncLock
        If Changed Then
            Logger.Info("检测到 VIP，已清除旧的跳过标记")
            NeSaveLocal()
        End If
    End Sub

    Private Sub MarkSongStatus(Id As Long, Status As String)
        Dim Changed As Boolean = False
        SyncLock NeSongs
            Dim S As NeteaseSong = Nothing
            If NeSongs.TryGetValue(Id, S) Then
                If S.Status <> Status Then
                    S.Status = Status
                    Changed = True
                End If
                If Not S.Probed Then
                    S.Probed = True
                    Changed = True
                End If
            End If
        End SyncLock
        If Changed Then NeSaveLocal()
    End Sub

    ''' <summary>这个 ID 在本地记录里的状态（"ok" / "vip" / "dead"）。</summary>
    Public Function NeSongStatus(Id As Long) As String
        NeEnsureLoaded()
        SyncLock NeSongs
            Dim S As NeteaseSong = Nothing
            If NeSongs.TryGetValue(Id, S) Then Return S.Status
        End SyncLock
        Return "ok"
    End Function

    ''' <summary>付费/VIP 专享（仅用于打标，不代表一定播不了——有些歌给了试听）。</summary>
    Public Function NeSongIsPaid(Id As Long) As Boolean
        NeEnsureLoaded()
        SyncLock NeSongs
            Dim S As NeteaseSong = Nothing
            If NeSongs.TryGetValue(Id, S) Then Return S.Fee = 1
        End SyncLock
        Return False
    End Function

    ''' <summary>已经确认播不了的（VIP 专享且无试听 / 曲目失效），队列自动跳过。</summary>
    Public Function NeSongSkippable(Id As Long) As Boolean
        '状态完全来自“实际能不能拿到地址”，不按会员身份猜
        Dim S As String = NeSongStatus(Id)
        Return S = "vip" OrElse S = "dead"
    End Function

    ''' <summary>从队列里找出下一首能播的，找不到返回 -1。</summary>
    Public Function NeNextPlayableIndex(StartIndex As Integer) As Integer
        Dim Q As List(Of Long) = NeQueueSnapshot()
        If Q.Count = 0 Then Return -1
        For Offset As Integer = 0 To Q.Count - 1
            Dim Index As Integer = (StartIndex + Offset) Mod Q.Count
            If Not NeSongSkippable(Q(Index)) Then Return Index
        Next
        Return -1
    End Function
    ''' <summary>当前正在播放的网易云歌曲 ID（0 表示没在播网易云）。</summary>
    Public NePlayingId As Long = 0

    ''' <summary>从缓存文件名还原歌曲 ID，用于把「1394165835.m4a」显示成歌名。</summary>
    Public Function NeIdFromCachePath(FilePath As String) As Long
        If String.IsNullOrWhiteSpace(FilePath) Then Return 0
        If Not FilePath.Replace("/", "\").Contains("\DstNetease\Cache\") Then Return 0
        Dim Name As String = PathUtils.GetFileNameWithoutExtension(FilePath)
        Dim Id As Long
        If Long.TryParse(Name, Id) Then Return Id
        Return 0
    End Function

    ''' <summary>
    ''' 在队列里按偏移找下一首能播的（Delta 为 +1 下一首、-1 上一首）。
    ''' 找不到返回 0。
    ''' </summary>
    Public Function NePlayOffset(Delta As Integer) As Long
        Dim Q As List(Of Long) = NeQueueSnapshot()
        If Q.Count = 0 Then Return 0
        Dim Current As Integer = Q.IndexOf(NePlayingId)
        If Current < 0 Then Current = 0
        For StepCount As Integer = 1 To Q.Count
            Dim Index As Integer = ((Current + Delta * StepCount) Mod Q.Count + Q.Count) Mod Q.Count
            If Not NeSongSkippable(Q(Index)) Then Return Q(Index)
        Next
        Return 0
    End Function
    ''' <summary>
    ''' 逐首实际请求播放地址来判定可播性（只问地址，不下载）。
    ''' 必须在工作线程调用；OnProgress 为（已处理, 总数）。
    ''' </summary>
    Public Sub NeProbeQueueStatus(Optional Concurrency As Integer = 4,
                                  Optional OnProgress As Action(Of Integer, Integer) = Nothing)
        Dim Q As List(Of Long) = NeQueueSnapshot()
        If Q.Count = 0 Then Return
        '只探还没探过的，已经确认过的直接跳过
        Dim Pending As New List(Of Long)
        For Each Id In Q
            Dim Probed As Boolean
            SyncLock NeSongs
                Dim S As NeteaseSong = Nothing
                Probed = NeSongs.TryGetValue(Id, S) AndAlso S.Probed
            End SyncLock
            If Not Probed Then Pending.Add(Id)
        Next
        If Pending.Count = 0 Then
            If OnProgress IsNot Nothing Then OnProgress(Q.Count, Q.Count)
            Return
        End If
        Dim Total As Integer = Pending.Count
        Dim Done As Integer = 0
        Dim Options As New Threading.Tasks.ParallelOptions With {
            .MaxDegreeOfParallelism = Math.Max(1, Concurrency)
        }
        Threading.Tasks.Parallel.ForEach(Pending, Options,
            Sub(Id As Long)
                Try
                    NeGetSongUrl(Id)      '内部按结果写 Status / Probed
                Catch ex As Exception
                    Logger.Warn(ex, "探测可播放性失败（" & Id & "）")
                End Try
                Dim Now As Integer = Threading.Interlocked.Increment(Done)
                If OnProgress IsNot Nothing Then OnProgress(Now, Total)
            End Sub)
    End Sub
#End Region

End Module
