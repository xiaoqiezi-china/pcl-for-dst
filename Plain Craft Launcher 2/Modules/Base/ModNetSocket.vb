Imports System.Collections.Concurrent
Imports System.IO
Imports System.IO.Compression
Imports System.Net
Imports System.Net.Http
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Security.Authentication
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' 自实现的 socket 层 HTTP 处理器。
''' <para>
''' 存在的唯一理由：.NET Framework 4.8 没有任何受支持的 API 能指定
''' “连接时优先使用 IPv4 还是 IPv6”：
''' </para>
''' <list type="bullet">
''' <item>ServicePoint.BindIPEndPointDelegate 只能绑本地端点。返回一个地址族不匹配的端点
''' 不会让运行时换用另一个族的远端地址，而是对同一个地址一直重试（实测重试 50 次后失败）。</item>
''' <item>AppContext 开关 Switch.System.Net.DontEnableIPv6 只存在于 .NET Core，
''' .NET Framework 4.8 的 System.dll 里根本没有这个字符串（实测扫描）。</item>
''' <item>HttpClientHandler.ConnectCallback 是 .NET Core 2.1 之后才有的。</item>
''' </list>
''' <para>
''' 于是只能自己解析 DNS、自己挑地址、自己建 socket、自己走 TLS。
''' 上层那一千多行的多线程下载引擎完全不用改：它照旧发 HttpRequestMessage、
''' 照旧按 ContentLength 分片、照旧读 Response.Content 的流。
''' </para>
''' <para>
''' 刻意不发送 Accept-Encoding：下载引擎靠 Content-Length 做分片与完整性校验，
''' 服务器一旦压缩响应，Content-Length 就变成压缩后的大小，与真实文件长度对不上。
''' 但服务器若无视这一点仍然压缩，这里也要能正确解压。
''' </para>
''' <para>
''' 连接复用：下载几千个小文件时，如果每个请求都重新 TCP + TLS 握手，
''' 开销会非常明显（外网一次握手几百毫秒）。所以这里带一个按
''' “协议 + 主机 + 端口” 归档的空闲连接池，响应体完整读完后把连接放回去。
''' 只复用确认读完的连接；复用的连接若已被对端关闭，会自动换新连接重试一次。
''' </para>
''' </summary>
Friend Class NetSocketHandler
    Inherits HttpMessageHandler

    ''' <summary>最多跟随几次重定向。</summary>
    Private Const MaxRedirect As Integer = 10
    ''' <summary>单个地址的连接超时。一个域名可能有多个地址，逐个试。</summary>
    Private Const ConnectTimeoutMs As Integer = 8000
    ''' <summary>空闲连接最多留多久，超过就当它已经过期。</summary>
    Private Const IdleLimitMs As Integer = 45000
    ''' <summary>每个站点最多缓存几条空闲连接，避免无限堆积。</summary>
    Private Const PoolPerHost As Integer = 8

    ''' <summary>空闲连接池，Key 为“协议://主机:端口”。</summary>
    Private Shared ReadOnly Pool As New ConcurrentDictionary(Of String, ConcurrentStack(Of NetConnection))

    Protected Overrides Function SendAsync(Request As HttpRequestMessage, Token As CancellationToken) As Task(Of HttpResponseMessage)
        '整个 socket 过程是同步的，扔到线程池里跑，交给上层用 GetResultWithTimeout 等
        Return Task.Run(Function() SendSync(Request, Token))
    End Function

    ''' <summary>同步完成一次请求（含重定向）。</summary>
    Private Function SendSync(Request As HttpRequestMessage, Token As CancellationToken) As HttpResponseMessage
        Dim Method As HttpMethod = If(Request.Method, HttpMethod.Get)
        Dim Headers As New List(Of KeyValuePair(Of String, String))
        For Each H In Request.Headers
            For Each V In H.Value
                Headers.Add(New KeyValuePair(Of String, String)(H.Key, V))
            Next
        Next
        Dim Body As Byte() = Nothing
        If Request.Content IsNot Nothing Then
            Body = Request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            For Each H In Request.Content.Headers
                For Each V In H.Value
                    Headers.Add(New KeyValuePair(Of String, String)(H.Key, V))
                Next
            Next
        End If

        Dim Url As Uri = Request.RequestUri
        Dim Hop As Integer = 0
        Do
            Token.ThrowIfCancellationRequested()
            Dim Raw As NetRawResponse = SendOnce(Url, Method, Headers, Body, Token)
            If Hop < MaxRedirect AndAlso Raw.Location <> "" AndAlso IsRedirect(Raw.StatusCode) Then
                Dim NextUrl As Uri = Nothing
                If Not Uri.TryCreate(Url, Raw.Location, NextUrl) Then
                    Raw.Dispose()
                    Throw New HttpRequestException($"重定向地址无效：{Raw.Location}")
                End If
                Dim Code As Integer = Raw.StatusCode
                Raw.Dispose()
                Logger.Trace(Function() $"下载请求重定向：{Url} → {NextUrl}")
                Url = NextUrl
                Hop += 1
                '303，以及 301/302 下的 POST，都要退化成 GET
                If Code = 303 OrElse ((Code = 301 OrElse Code = 302) AndAlso Method <> HttpMethod.Get AndAlso Method <> HttpMethod.Head) Then
                    Method = HttpMethod.Get
                    Body = Nothing
                End If
                Continue Do
            End If
            '跟随重定向后，响应上要挂最终地址：HttpClient 自己也会这么做，
            '上层下载引擎靠它判断有没有发生重定向
            Request.RequestUri = Url
            Return BuildResponse(Request, Raw)
        Loop
        Throw New HttpRequestException("重定向次数过多")
    End Function

    Private Shared Function IsRedirect(Code As Integer) As Boolean
        Return Code = 301 OrElse Code = 302 OrElse Code = 303 OrElse Code = 307 OrElse Code = 308
    End Function

    ''' <summary>把裸响应包装成 HttpResponseMessage。</summary>
    Private Shared Function BuildResponse(Request As HttpRequestMessage, Raw As NetRawResponse) As HttpResponseMessage
        Dim Resp As New HttpResponseMessage(CType(Raw.StatusCode, HttpStatusCode))
        Resp.RequestMessage = Request
        Resp.ReasonPhrase = Raw.ReasonPhrase
        For Each Pair In Raw.Headers
            '内容相关的头要挂到 Content.Headers 上，否则 HttpClient 会认为它们是非法的响应头
            If Pair.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) AndAlso
               Not Pair.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) Then Continue For
            Resp.Headers.TryAddWithoutValidation(Pair.Key, Pair.Value)
        Next
        Dim Content As New StreamContent(Raw.Body)
        If Raw.Headers.ContainsKey("Content-Type") Then
            Content.Headers.TryAddWithoutValidation("Content-Type", Raw.Headers("Content-Type"))
        End If
        If Raw.Headers.ContainsKey("Content-Encoding") Then
            Content.Headers.TryAddWithoutValidation("Content-Encoding", Raw.Headers("Content-Encoding"))
        End If
        '只有未经压缩时，Content-Length 才等于真实文件长度
        If Raw.DeclaredLength >= 0 Then Content.Headers.ContentLength = Raw.DeclaredLength
        Resp.Content = Content
        Return Resp
    End Function

    ''' <summary>真正发一次请求，不做重定向。优先复用池里的空闲连接。</summary>
    Private Shared Function SendOnce(Url As Uri, Method As HttpMethod, Headers As List(Of KeyValuePair(Of String, String)),
            Body As Byte(), Token As CancellationToken) As NetRawResponse
        Dim IsHttps As Boolean = Url.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
        Dim Port As Integer = If(Url.Port > 0, Url.Port, If(IsHttps, 443, 80))
        Dim Host As String = Url.IdnHost
        Dim Key As String = (If(IsHttps, "https://", "http://")) & Host & ":" & Port
        Dim FromPool As Boolean = False
        Dim Conn As NetConnection = TakePooled(Key, FromPool)
        If Conn Is Nothing Then
            Try
                Conn = OpenConnection(Host, Port, IsHttps, Key, Token)
            Catch ex As Exception
                Throw New HttpRequestException($"连接 {Host}:{Port} 失败：{ex.Message}", ex)
            End Try
        End If
        Try
            Return Exchange(Conn, Url, Method, Headers, Body, Token)
        Catch ex As Exception
            '复用的连接可能已经被服务器悄悄关掉了：换一条新连接再试一次
            Conn.Dispose()
            If FromPool AndAlso Not Token.IsCancellationRequested Then
                Dim Fresh As NetConnection = OpenConnection(Host, Port, IsHttps, Key, Token)
                Try
                    Return Exchange(Fresh, Url, Method, Headers, Body, Token)
                Catch ex2 As Exception
                    Fresh.Dispose()
                    Throw
                End Try
            End If
            Throw
        End Try
    End Function

    ''' <summary>在一条已经连好的连接上完成一次请求/响应。</summary>
    Private Shared Function Exchange(Conn As NetConnection, Url As Uri, Method As HttpMethod,
            Headers As List(Of KeyValuePair(Of String, String)), Body As Byte(), Token As CancellationToken) As NetRawResponse
        '写请求
        Dim Head As New StringBuilder
        Head.Append(Method.Method).Append(" ").Append(If(Url.PathAndQuery = "", "/", Url.PathAndQuery)).Append(" HTTP/1.1").Append(vbCrLf)
        Dim HasHost As Boolean = False
        For Each Pair In Headers
            If Pair.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) Then HasHost = True
            If Pair.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) OrElse
               Pair.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) Then Continue For
            If Pair.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) Then Continue For
            If Pair.Key.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase) Then Continue For '见类注释：不发压缩协商
            Head.Append(Pair.Key).Append(": ").Append(Pair.Value).Append(vbCrLf)
        Next
        If Not HasHost Then
            Dim DefaultPort As Integer = If(Url.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase), 443, 80)
            Head.Append("Host: ").Append(If(Url.Port = DefaultPort OrElse Url.Port <= 0, Url.IdnHost, Url.IdnHost & ":" & Url.Port)).Append(vbCrLf)
        End If
        If Body IsNot Nothing Then Head.Append("Content-Length: ").Append(Body.Length).Append(vbCrLf)
        Head.Append("Connection: keep-alive").Append(vbCrLf)
        Head.Append(vbCrLf)
        Dim HeadBytes As Byte() = Encoding.UTF8.GetBytes(Head.ToString)
        Conn.Stream.Write(HeadBytes, 0, HeadBytes.Length)
        If Body IsNot Nothing AndAlso Body.Length > 0 Then Conn.Stream.Write(Body, 0, Body.Length)
        Conn.Stream.Flush()

        '读响应头
        Dim Reader As NetBufferedReader = Conn.Reader
        Dim StatusLine As String = Reader.ReadLine()
        If StatusLine Is Nothing Then Throw New IOException("服务器没有返回任何内容")
        Dim Parts As String() = StatusLine.Split(" "c)
        If Parts.Length < 2 Then Throw New HttpRequestException($"无法解析状态行：{StatusLine}")
        Dim Code As Integer
        If Not Integer.TryParse(Parts(1), Code) Then Throw New HttpRequestException($"无法解析状态码：{StatusLine}")
        Dim Raw As New NetRawResponse With {
            .StatusCode = Code,
            .ReasonPhrase = If(Parts.Length > 2, String.Join(" ", Parts.Skip(2)), "")
        }
        Do
            Dim Line As String = Reader.ReadLine()
            If Line Is Nothing OrElse Line = "" Then Exit Do
            Dim Split As Integer = Line.IndexOf(":"c)
            If Split <= 0 Then Continue Do
            Dim K As String = Line.Substring(0, Split).Trim()
            Dim V As String = Line.Substring(Split + 1).Trim()
            If Raw.Headers.ContainsKey(K) Then
                Raw.Headers(K) = Raw.Headers(K) & ", " & V
            Else
                Raw.Headers(K) = V
            End If
        Loop
        If Raw.Headers.ContainsKey("Location") Then Raw.Location = Raw.Headers("Location")

        '这条连接还能不能继续用
        Dim KeepAlive As Boolean = True
        If Raw.Headers.ContainsKey("Connection") Then
            If Raw.Headers("Connection").ToLower.Contains("close") Then KeepAlive = False
        ElseIf Code = 400 OrElse Code = 408 OrElse Code = 411 OrElse Code = 413 OrElse Code = 414 OrElse Code = 500 Then
            '这些状态码下服务器通常直接关连接
            KeepAlive = False
        End If

        '响应体：先处理分帧（chunked），再处理内容编码（gzip / deflate）
        Dim Chunked As Boolean = Raw.Headers.ContainsKey("Transfer-Encoding") AndAlso
                                 Raw.Headers("Transfer-Encoding").ToLower.Contains("chunked")
        Dim Length As Long = -1
        If Not Chunked AndAlso Raw.Headers.ContainsKey("Content-Length") Then
            Dim Parsed As Long
            If Long.TryParse(Raw.Headers("Content-Length"), Parsed) AndAlso Parsed >= 0 Then Length = Parsed
        End If

        '没有响应体的情况：HEAD、204、304，以及 Content-Length: 0
        Dim NoBody As Boolean = Method = HttpMethod.Head OrElse Code = 204 OrElse Code = 304 OrElse
                                (Not Chunked AndAlso Length = 0)
        If NoBody Then
            ReleaseOrDispose(Conn, KeepAlive)
            Raw.Body = New MemoryStream(New Byte() {})
            Return Raw
        End If

        Dim Inner As NetBodyStream
        If Chunked Then
            Inner = New NetChunkedStream(Conn, KeepAlive)
        ElseIf Length >= 0 Then
            Inner = New NetLengthStream(Conn, Length, KeepAlive)
        Else
            '长度未知：读到连接关闭为止，这条连接不能复用
            Inner = New NetUntilEndStream(Conn)
        End If
        Dim Encoding_ As String = ""
        If Raw.Headers.ContainsKey("Content-Encoding") Then Encoding_ = Raw.Headers("Content-Encoding").ToLower.Trim()
        Dim Final As Stream = Inner
        If Encoding_.Contains("gzip") Then
            Final = New NetGZipStream(Inner)
        ElseIf Encoding_.Contains("deflate") Then
            Final = New NetDeflateStream(Inner)
        Else
            Encoding_ = ""
        End If
        Raw.Body = Final
        '压缩过的响应，Content-Length 是压缩后的长度，不能当作文件长度上报
        If Encoding_ = "" AndAlso Length >= 0 Then Raw.DeclaredLength = Length
        Return Raw
    End Function

    Private Shared Sub ReleaseOrDispose(Conn As NetConnection, KeepAlive As Boolean)
        If KeepAlive Then
            Release(Conn)
        Else
            Conn.Dispose()
        End If
    End Sub

    ''' <summary>证书校验：沿用 PCL 已经装好的那套回调（正版登录时可能放宽）。</summary>
    Private Shared Function ValidateCertificate(Sender As Object, Certificate As X509Certificate, Chain As X509Chain, Errors As SslPolicyErrors) As Boolean
        Dim Callback As RemoteCertificateValidationCallback = ServicePointManager.ServerCertificateValidationCallback
        If Callback IsNot Nothing Then
            Try
                Return Callback(Sender, Certificate, Chain, Errors)
            Catch ex As Exception
                Logger.Warn(ex, "证书校验回调出错")
            End Try
        End If
        Return Errors = SslPolicyErrors.None
    End Function

    ''' <summary>建立一条新连接（含 TLS 握手）。</summary>
    Private Shared Function OpenConnection(Host As String, Port As Integer, IsHttps As Boolean, Key As String, Token As CancellationToken) As NetConnection
        Dim Socket As Socket = ConnectAny(Host, Port, Token)
        Dim Conn As NetConnection = Nothing
        Try
            Conn = New NetConnection With {.Socket = Socket, .Key = Key, .Preference = IpPreference}
            Dim Net As New NetworkStream(Socket, True)
            Dim Stream As Stream = Net
            If IsHttps Then
                Dim Ssl As New SslStream(Net, False, AddressOf ValidateCertificate)
                '不用 SslProtocols.None：在 .NET Framework 4.8 里它不是一个合法枚举值，会直接抛 ArgumentException
                Ssl.AuthenticateAsClient(Host, Nothing, SslProtocols.Tls12 Or SslProtocols.Tls11, False)
                Stream = Ssl
            End If
            Conn.Stream = Stream
            Conn.Reader = New NetBufferedReader(Stream)
            Conn.Touch()
            Return Conn
        Catch ex As Exception
            If Conn IsNot Nothing Then
                Conn.Dispose()
            Else
                Try
                    Socket.Dispose()
                Catch
                End Try
            End If
            Throw
        End Try
    End Function

    ''' <summary>按设置的偏好顺序逐个尝试地址，返回第一个连上的 socket。</summary>
    Private Shared Function ConnectAny(Host As String, Port As Integer, Token As CancellationToken) As Socket
        Dim Addresses As IPAddress() = ResolveOrdered(Host)
        If Addresses.Length = 0 Then Throw New HttpRequestException($"无法解析域名：{Host}")
        Dim Errors As New List(Of String)
        For Each Address In Addresses
            Token.ThrowIfCancellationRequested()
            Dim S As New Socket(Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            Try
                S.NoDelay = True
                Dim Task_ = S.ConnectAsync(Address, Port)
                If Not Task_.Wait(ConnectTimeoutMs) Then
                    S.Dispose()
                    Errors.Add($"{Address} 连接超时")
                    Continue For
                End If
                If Not S.Connected Then
                    S.Dispose()
                    Errors.Add($"{Address} 未连接")
                    Continue For
                End If
                Logger.Trace(Function() $"下载连接：{Host}:{Port} → {Address}（{Address.AddressFamily}）")
                Return S
            Catch ex As Exception
                Try
                    S.Dispose()
                Catch
                End Try
                Errors.Add($"{Address} {ex.Message}")
            End Try
        Next
        Throw New HttpRequestException($"连接 {Host}:{Port} 失败（{String.Join("；", Errors)}）")
    End Function

    ''' <summary>
    ''' 按“优先 IPv4 / 优先 IPv6 / 跟随系统”的顺序返回地址。
    ''' 偏好族没有地址时自动退回另一族。
    ''' </summary>
    Friend Shared Function ResolveOrdered(Host As String) As IPAddress()
        Dim Addresses As IPAddress()
        Dim Literal As IPAddress = Nothing
        If IPAddress.TryParse(Host.Trim("["c, "]"c), Literal) Then
            Addresses = {Literal}
        Else
            Try
                Addresses = Dns.GetHostAddresses(Host)
            Catch ex As Exception
                Throw New HttpRequestException($"无法解析域名：{Host}", ex)
            End Try
        End If
        Dim Preference As Integer = Settings.Get(Of Integer)("ToolDownloadIpProtocol")
        If Preference <> 0 AndAlso Preference <> 1 Then Return Addresses '跟随系统：保持解析顺序
        Dim Preferred As AddressFamily = If(Preference = 0, AddressFamily.InterNetwork, AddressFamily.InterNetworkV6)
        Dim Other As AddressFamily = If(Preference = 0, AddressFamily.InterNetworkV6, AddressFamily.InterNetwork)
        Dim First As New List(Of IPAddress), Second As New List(Of IPAddress)
        For Each One In Addresses
            If One.AddressFamily = Preferred Then
                First.Add(One)
            ElseIf One.AddressFamily = Other Then
                Second.Add(One)
            End If
        Next
        Return First.Concat(Second).ToArray()
    End Function

#Region "空闲连接池"

    ''' <summary>当前的 IP 协议偏好：0=优先 IPv4 1=优先 IPv6 2=跟随系统。</summary>
    Friend Shared ReadOnly Property IpPreference As Integer
        Get
            Return Settings.Get(Of Integer)("ToolDownloadIpProtocol")
        End Get
    End Property

    ''' <summary>取一条空闲连接；没有可用连接时返回 Nothing，并置 FromPool 为 False。</summary>
    Private Shared Function TakePooled(Key As String, ByRef FromPool As Boolean) As NetConnection
        FromPool = False
        Dim Stack As ConcurrentStack(Of NetConnection) = Nothing
        If Not Pool.TryGetValue(Key, Stack) Then Return Nothing
        Dim Conn As NetConnection = Nothing
        Do While Stack.TryPop(Conn)
            If Conn.IsUsable Then
                FromPool = True
                Return Conn
            End If
            Conn.Dispose()
        Loop
        Return Nothing
    End Function

    ''' <summary>把读完的干净连接放回池里。</summary>
    Private Shared Sub Release(Conn As NetConnection)
        If Conn Is Nothing OrElse Not Conn.IsUsable Then
            If Conn IsNot Nothing Then Conn.Dispose()
            Return
        End If
        Dim Stack As ConcurrentStack(Of NetConnection) = Pool.GetOrAdd(Conn.Key, Function(K) New ConcurrentStack(Of NetConnection)())
        If Stack.Count >= PoolPerHost Then
            Conn.Dispose()
            Return
        End If
        Conn.Touch()
        Stack.Push(Conn)
    End Sub

    ''' <summary>一条到具体站点的连接。</summary>
    Private Class NetConnection
        Public Socket As Socket
        Public Stream As Stream
        Public Reader As NetBufferedReader
        Public Key As String = ""
        ''' <summary>建立这条连接时用的 IP 协议偏好。偏好改了之后就不该再复用它。</summary>
        Public Preference As Integer = -1
        Private LastUsed As Long = GetTimeMs()
        Private Disposed As Boolean

        Public Sub Touch()
            LastUsed = GetTimeMs()
        End Sub

        ''' <summary>还能不能拿来发下一个请求。</summary>
        Public ReadOnly Property IsUsable As Boolean
            Get
                If Disposed OrElse Socket Is Nothing Then Return False
                '用户在设置里换了协议偏好：这条连接是按老偏好连的，直接作废
                If Preference <> IpPreference Then Return False
                If GetTimeMs() - LastUsed > IdleLimitMs Then Return False
                Try
                    If Not Socket.Connected Then Return False
                    '对端已经关了的话，Poll 会立刻可读且没有数据
                    If Socket.Poll(0, SelectMode.SelectRead) AndAlso Socket.Available = 0 Then Return False
                Catch
                    Return False
                End Try
                Return True
            End Get
        End Property

        Public Sub Dispose()
            If Disposed Then Return
            Disposed = True
            Try
                If Stream IsNot Nothing Then Stream.Dispose()
            Catch
            End Try
            Try
                If Socket IsNot Nothing Then Socket.Dispose()
            Catch
            End Try
        End Sub
    End Class

#End Region

    ''' <summary>一次请求的裸响应。</summary>
    Private Class NetRawResponse
        Public StatusCode As Integer
        Public ReasonPhrase As String = ""
        Public ReadOnly Headers As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public Location As String = ""
        ''' <summary>真实文件长度；-1 表示未知（压缩或分块传输时无法确定）。</summary>
        Public DeclaredLength As Long = -1
        Public Body As Stream

        ''' <summary>重定向时把这条响应的连接放掉。</summary>
        Public Sub Dispose()
            If Body IsNot Nothing Then
                Try
                    Body.Dispose()
                Catch
                End Try
            End If
        End Sub
    End Class

    ''' <summary>
    ''' 带缓冲的逐行读取器：响应头读完之后，缓冲区里剩下的字节要交给响应体流。
    ''' </summary>
    Private Class NetBufferedReader
        Private ReadOnly Inner As Stream
        '缓冲开大一点：下载引擎每次要 16KB，缓冲区太小时一次 Read 会被拆成多次系统调用
        Private Buffer As Byte() = New Byte(32767) {}
        Private Pos As Integer = 0
        Private Len As Integer = 0
        Public Sub New(Inner As Stream)
            Me.Inner = Inner
        End Sub

        ''' <summary>
        ''' 读一块数据。这里是同步阻塞读；上层要中断它只能 Dispose 响应流，
        ''' 那会一路 Dispose 到 socket，阻塞中的 Read 随即抛异常退出。
        ''' 因此不给 socket 设读超时：设了就会把慢速服务器误判成断线。
        ''' </summary>
        Private Function Fill() As Integer
            Return Inner.Read(Buffer, 0, Buffer.Length)
        End Function

        ''' <summary>读一行（不含 CRLF）。返回 Nothing 表示流已结束。</summary>
        Public Function ReadLine() As String
            Dim Mem As New MemoryStream
            Do
                If Pos >= Len Then
                    Len = Fill()
                    Pos = 0
                    If Len <= 0 Then
                        If Mem.Length = 0 Then Return Nothing
                        Return Encoding.ASCII.GetString(Mem.ToArray())
                    End If
                End If
                Dim B As Byte = Buffer(Pos)
                Pos += 1
                If B = 10 Then
                    Dim Bytes = Mem.ToArray()
                    If Bytes.Length > 0 AndAlso Bytes(Bytes.Length - 1) = 13 Then
                        Return Encoding.ASCII.GetString(Bytes, 0, Bytes.Length - 1)
                    End If
                    Return Encoding.ASCII.GetString(Bytes)
                End If
                Mem.WriteByte(B)
                If Mem.Length > 65536 Then Throw New HttpRequestException("响应头过长")
            Loop
        End Function

        ''' <summary>读若干字节，返回实际读到的数量；0 表示流结束。</summary>
        Public Function ReadSome(Target As Byte(), Offset As Integer, Count As Integer) As Integer
            If Count <= 0 Then Return 0
            If Pos >= Len Then
                Len = Fill()
                Pos = 0
                If Len <= 0 Then Return 0
            End If
            Dim Take As Integer = Math.Min(Count, Len - Pos)
            Array.Copy(Buffer, Pos, Target, Offset, Take)
            Pos += Take
            Return Take
        End Function

        ''' <summary>
        ''' 异步版读若干字节。
        ''' <para>
        ''' 下载引擎是用 ReadAsync + 超时取消来中断的，同步 Read 被阻塞时根本看不到取消，
        ''' 所以必须提供真正走异步 IO 的路径，让取消能立刻生效。
        ''' 连接是独占的（从池里取出后不会被别的请求同时用），缓冲不会被并发写坏。
        ''' </para>
        ''' </summary>
        Public Async Function ReadSomeAsync(Target As Byte(), Offset As Integer, Count As Integer,
                Token As CancellationToken) As Task(Of Integer)
            If Count <= 0 Then Return 0
            If Pos >= Len Then
                Len = Await Inner.ReadAsync(Buffer, 0, Buffer.Length, Token).ConfigureAwait(False)
                Pos = 0
                If Len <= 0 Then Return 0
            End If
            Dim Take As Integer = Math.Min(Count, Len - Pos)
            Array.Copy(Buffer, Pos, Target, Offset, Take)
            Pos += Take
            Return Take
        End Function
    End Class

    ''' <summary>响应体流的公共部分：读完之后把连接放回池里，没读完就销毁连接。</summary>
    Private MustInherit Class NetBodyStream
        Inherits Stream
        Protected ReadOnly Conn As NetConnection
        Private ReadOnly KeepAlive As Boolean
        Private Completed As Boolean
        Private Released As Boolean

        Protected Sub New(Conn As NetConnection, KeepAlive As Boolean)
            Me.Conn = Conn
            Me.KeepAlive = KeepAlive
        End Sub

        ''' <summary>标记响应体已经完整读完。</summary>
        Protected Sub MarkCompleted()
            If Completed Then Return
            Completed = True
            ReleaseOnce()
        End Sub

        Private Sub ReleaseOnce()
            If Released Then Return
            Released = True
            ReleaseOrDispose(Conn, KeepAlive)
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return True
            End Get
        End Property
        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property
        Public Overrides Property Position As Long
            Get
                Return 0
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property
        Public Overrides Sub Flush()
        End Sub
        Public Overrides Function Seek(Offset As Long, Origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function
        Public Overrides Sub SetLength(Value As Long)
            Throw New NotSupportedException()
        End Sub
        Public Overrides Sub Write(Buffer() As Byte, Offset As Integer, Count As Integer)
            Throw New NotSupportedException()
        End Sub
        ''' <summary>
        ''' 异步读：默认把同步实现丢到线程池。
        ''' 子类会覆盖 <see cref="ReadCoreAsync"/>，走真正的异步 socket IO。
        ''' </summary>
        Public Overrides Function ReadAsync(Buffer() As Byte, Offset As Integer, Count As Integer,
                CancellationToken As CancellationToken) As Task(Of Integer)
            Return ReadCoreAsync(Buffer, Offset, Count, CancellationToken)
        End Function
        ''' <summary>真正的异步读实现，由子类提供。</summary>
        Protected MustOverride Function ReadCoreAsync(Buffer() As Byte, Offset As Integer, Count As Integer,
                Token As CancellationToken) As Task(Of Integer)
        Protected Overrides Sub Dispose(Disposing As Boolean)
            If Disposing AndAlso Not Completed Then
                '没读完就被丢弃：连接里还残留着响应体，不能再复用
                Released = True
                Conn.Dispose()
            End If
            MyBase.Dispose(Disposing)
        End Sub
    End Class

    ''' <summary>已知长度的响应体。</summary>
    Private Class NetLengthStream
        Inherits NetBodyStream
        Private Left As Long
        Public Sub New(Conn As NetConnection, Length As Long, KeepAlive As Boolean)
            MyBase.New(Conn, KeepAlive)
            Left = Length
        End Sub
        Public Overrides Function Read(Buffer() As Byte, Offset As Integer, Count As Integer) As Integer
            If Left <= 0 Then
                MarkCompleted()
                Return 0
            End If
            Dim Want As Integer = CInt(Math.Min(Count, Left))
            Dim Got As Integer = Conn.Reader.ReadSome(Buffer, Offset, Want)
            If Got <= 0 Then
                '对端提前断了：这条连接已经不可用
                Conn.Dispose()
                Return 0
            End If
            Left -= Got
            If Left <= 0 Then MarkCompleted()
            Return Got
        End Function
        Protected Overrides Async Function ReadCoreAsync(Buffer() As Byte, Offset As Integer, Count As Integer,
                Token As CancellationToken) As Task(Of Integer)
            If Left <= 0 Then
                MarkCompleted()
                Return 0
            End If
            Dim Want As Integer = CInt(Math.Min(Count, Left))
            Dim Got As Integer = Await Conn.Reader.ReadSomeAsync(Buffer, Offset, Want, Token).ConfigureAwait(False)
            If Got <= 0 Then
                Conn.Dispose()
                Return 0
            End If
            Left -= Got
            If Left <= 0 Then MarkCompleted()
            Return Got
        End Function
    End Class

    ''' <summary>读到连接关闭为止的响应体（连接不可复用）。</summary>
    Private Class NetUntilEndStream
        Inherits NetBodyStream
        Public Sub New(Conn As NetConnection)
            MyBase.New(Conn, False)
        End Sub
        Public Overrides Function Read(Buffer() As Byte, Offset As Integer, Count As Integer) As Integer
            Dim Got As Integer = Conn.Reader.ReadSome(Buffer, Offset, Count)
            If Got <= 0 Then MarkCompleted()
            Return Got
        End Function
        Protected Overrides Async Function ReadCoreAsync(Buffer() As Byte, Offset As Integer, Count As Integer,
                Token As CancellationToken) As Task(Of Integer)
            Dim Got As Integer = Await Conn.Reader.ReadSomeAsync(Buffer, Offset, Count, Token).ConfigureAwait(False)
            If Got <= 0 Then MarkCompleted()
            Return Got
        End Function
    End Class

    ''' <summary>chunked 分块传输的响应体。</summary>
    Private Class NetChunkedStream
        Inherits NetBodyStream
        Private ChunkLeft As Long = 0
        Private Ended As Boolean = False
        Public Sub New(Conn As NetConnection, KeepAlive As Boolean)
            MyBase.New(Conn, KeepAlive)
        End Sub
        Public Overrides Function Read(Buffer() As Byte, Offset As Integer, Count As Integer) As Integer
            If Ended OrElse Count <= 0 Then
                If Ended Then MarkCompleted()
                Return 0
            End If
            If ChunkLeft = 0 Then
                Dim Line As String = Conn.Reader.ReadLine()
                If Line Is Nothing Then
                    Ended = True
                    MarkCompleted()
                    Return 0
                End If
                Line = Line.Trim()
                Dim Semicolon As Integer = Line.IndexOf(";"c)
                If Semicolon >= 0 Then Line = Line.Substring(0, Semicolon).Trim()
                Dim Size As Long
                If Not Long.TryParse(Line, Globalization.NumberStyles.HexNumber, Nothing, Size) Then
                    Conn.Dispose()
                    Throw New HttpRequestException($"无法解析分块长度：{Line}")
                End If
                If Size = 0 Then
                    '吃掉结尾的空行与可能的 trailer
                    Do
                        Dim Tail As String = Conn.Reader.ReadLine()
                        If Tail Is Nothing OrElse Tail = "" Then Exit Do
                    Loop
                    Ended = True
                    MarkCompleted()
                    Return 0
                End If
                ChunkLeft = Size
            End If
            Dim Want As Integer = CInt(Math.Min(Count, ChunkLeft))
            Dim Got As Integer = Conn.Reader.ReadSome(Buffer, Offset, Want)
            If Got <= 0 Then
                Conn.Dispose()
                Throw New HttpRequestException("分块数据提前结束")
            End If
            ChunkLeft -= Got
            If ChunkLeft = 0 Then Conn.Reader.ReadLine() '块尾的 CRLF
            Return Got
        End Function
        ''' <summary>
        ''' 分块响应基本只出现在接口返回的 JSON 上（体积小），
        ''' 走线程池包一层同步读即可，不必为它单独写一套异步分帧状态机。
        ''' 大文件下载都带 Content-Length，走的是 <see cref="NetLengthStream"/> 的真异步路径。
        ''' </summary>
        Protected Overrides Function ReadCoreAsync(Buffer() As Byte, Offset As Integer, Count As Integer,
                Token As CancellationToken) As Task(Of Integer)
            Return Task.Run(Function() Read(Buffer, Offset, Count), Token)
        End Function
    End Class

    ''' <summary>
    ''' gzip 解压流。
    ''' <para>
    ''' 不能直接用 GZipStream 包一层：它的 Dispose 会把底层连接一起关掉，
    ''' 而这里希望底层读完后连接还能复用。所以自己转发 Read。
    ''' </para>
    ''' </summary>
    Private Class NetGZipStream
        Inherits Stream
        Private ReadOnly Inner As Stream
        Private ReadOnly Gzip As GZipStream
        Public Sub New(Inner As Stream)
            Me.Inner = Inner
            Gzip = New GZipStream(Inner, CompressionMode.Decompress, True)
        End Sub
        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return True
            End Get
        End Property
        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property
        Public Overrides Property Position As Long
            Get
                Return 0
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property
        Public Overrides Function Read(Buffer() As Byte, Offset As Integer, Count As Integer) As Integer
            Return Gzip.Read(Buffer, Offset, Count)
        End Function
        Public Overrides Sub Flush()
        End Sub
        Public Overrides Function Seek(Offset As Long, Origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function
        Public Overrides Sub SetLength(Value As Long)
            Throw New NotSupportedException()
        End Sub
        Public Overrides Sub Write(Buffer() As Byte, Offset As Integer, Count As Integer)
            Throw New NotSupportedException()
        End Sub
        Protected Overrides Sub Dispose(Disposing As Boolean)
            If Disposing Then
                Try
                    Gzip.Dispose()
                Catch
                End Try
                Try
                    Inner.Dispose()
                Catch
                End Try
            End If
            MyBase.Dispose(Disposing)
        End Sub
    End Class

    ''' <summary>
    ''' 把已经读出来的一小段字节补到流开头，其余部分转发给底层流。
    ''' 用于“偷看几个字节再决定怎么解压”的场景。
    ''' </summary>
    Private Class NetPrependStream
        Inherits Stream
        Private ReadOnly Prefix As Byte()
        Private PrefixPos As Integer = 0
        Private ReadOnly Inner As Stream
        Public Sub New(Prefix As Byte(), PrefixLength As Integer, Inner As Stream)
            Me.Prefix = New Byte(PrefixLength - 1) {}
            Array.Copy(Prefix, Me.Prefix, PrefixLength)
            Me.Inner = Inner
        End Sub
        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return True
            End Get
        End Property
        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property
        Public Overrides Property Position As Long
            Get
                Return 0
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property
        Public Overrides Function Read(Buffer() As Byte, Offset As Integer, Count As Integer) As Integer
            If PrefixPos < Prefix.Length Then
                Dim Take As Integer = Math.Min(Count, Prefix.Length - PrefixPos)
                Array.Copy(Prefix, PrefixPos, Buffer, Offset, Take)
                PrefixPos += Take
                Return Take
            End If
            Return Inner.Read(Buffer, Offset, Count)
        End Function
        Public Overrides Sub Flush()
        End Sub
        Public Overrides Function Seek(Offset As Long, Origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function
        Public Overrides Sub SetLength(Value As Long)
            Throw New NotSupportedException()
        End Sub
        Public Overrides Sub Write(Buffer() As Byte, Offset As Integer, Count As Integer)
            Throw New NotSupportedException()
        End Sub
    End Class

    ''' <summary>
    ''' deflate 解压流。
    ''' <para>
    ''' 坑在这里：HTTP 的 <c>Content-Encoding: deflate</c> 按 RFC 7230 指的是
    ''' <b>zlib 包装</b>（2 字节头 + Adler-32 校验尾）的 deflate 数据，
    ''' 而 .NET Framework 的 <see cref="DeflateStream"/> 只认裸 deflate 流。
    ''' 直接套上去，遇到真正的 zlib 数据会抛 InvalidDataException，
    ''' 表现为“下载莫名其妙失败”，且只在少数服务器上复现。
    ''' </para>
    ''' <para>
    ''' 所以先偷看前两个字节：满足 zlib 头特征（CMF=0x78 且 (CMF*256+FLG) Mod 31 = 0）
    ''' 就跳过这两个字节按 zlib 解，否则把这两个字节补回去按裸 deflate 解。
    ''' 两种写法都要能跑，因为现实里两种服务器都存在。
    ''' </para>
    ''' </summary>
    Private Class NetDeflateStream
        Inherits Stream
        Private ReadOnly Inner As Stream
        Private ReadOnly Deflate As DeflateStream
        Public Sub New(Inner As Stream)
            Me.Inner = Inner
            '先读两个字节判断是不是 zlib 包装
            Dim Head(1) As Byte
            Dim Got As Integer = 0
            While Got < 2
                Dim N As Integer = Inner.Read(Head, Got, 2 - Got)
                If N <= 0 Then Exit While
                Got += N
            End While
            Dim IsZlib As Boolean = Got = 2 AndAlso Head(0) = &H78 AndAlso ((CInt(Head(0)) * 256 + Head(1)) Mod 31) = 0
            If IsZlib Then
                'zlib：两个字节是包装头，丢掉，剩下的交给 DeflateStream
                Deflate = New DeflateStream(Inner, CompressionMode.Decompress, True)
            Else
                '裸 deflate：把刚才读出来的字节补回流的开头
                Deflate = New DeflateStream(New NetPrependStream(Head, Got, Inner), CompressionMode.Decompress, True)
            End If
        End Sub
        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return True
            End Get
        End Property
        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return False
            End Get
        End Property
        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property
        Public Overrides Property Position As Long
            Get
                Return 0
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property
        Public Overrides Function Read(Buffer() As Byte, Offset As Integer, Count As Integer) As Integer
            Return Deflate.Read(Buffer, Offset, Count)
        End Function
        Public Overrides Sub Flush()
        End Sub
        Public Overrides Function Seek(Offset As Long, Origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function
        Public Overrides Sub SetLength(Value As Long)
            Throw New NotSupportedException()
        End Sub
        Public Overrides Sub Write(Buffer() As Byte, Offset As Integer, Count As Integer)
            Throw New NotSupportedException()
        End Sub
        Protected Overrides Sub Dispose(Disposing As Boolean)
            If Disposing Then
                Try
                    Deflate.Dispose()
                Catch
                End Try
                Try
                    Inner.Dispose()
                Catch
                End Try
            End If
            MyBase.Dispose(Disposing)
        End Sub
    End Class

End Class
