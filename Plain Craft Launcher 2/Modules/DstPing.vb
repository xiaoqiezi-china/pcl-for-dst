Imports System.IO
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading

''' <summary>
''' Minecraft Server List Ping 协议的极简实现（1.7+ 全版本兼容）。
''' 面板接口不再返回玩家数据，返回 null 的服务器由这里本地探测补全。
''' </summary>
Friend Class DstPing

    Public Class PingResult
        ''' <summary>往返延迟毫秒。-1 = 失败。</summary>
        Public Property PingMs As Integer = -1
        ''' <summary>当前在线人数。-1 = 未知。</summary>
        Public Property OnlinePlayers As Integer = -1
        ''' <summary>最大人数。-1 = 未知。</summary>
        Public Property MaxPlayers As Integer = -1
        ''' <summary>在线玩家列表（可能为空， MOTD 里带 anonymous 时已转义）。</summary>
        Public Property Players As New List(Of String)
        ''' <summary>服务器版本名。</summary>
        Public Property Version As String = ""
        ''' <summary>失败原因。</summary>
        Public Property [Error] As String = ""
    End Class

    ''' <summary>
    ''' 对 host:port 做一次完整的握手 + 请求 + 响应读取。
    ''' 在工作线程调用；超时由 TimeoutMs 控制，默认 5 秒。
    ''' </summary>
    Public Shared Function Probe(Host As String, Port As Integer, Optional TimeoutMs As Integer = 5000) As PingResult
        Dim R As New PingResult
        If Host = "" Then
            R.Error = "地址为空"
            Return R
        End If
        Dim StartTick As Long = GetTimeMs()
        Try
            Using Client As New TcpClient()
                Dim ConnectTask = Client.ConnectAsync(Host, Port)
                If Not ConnectTask.Wait(TimeoutMs) Then
                    R.Error = "连接超时"
                    Return R
                End If
                Client.ReceiveTimeout = TimeoutMs
                Client.SendTimeout = TimeoutMs
                'TCP 握手完成的时间点才是“真实网络延迟”（与系统 ping 基本一致）；
                '应用层的 JSON 生成与传输耗时不算在内，GTNH 那种 25KB 响应会虚高 100ms+
                R.PingMs = CInt(GetTimeMs() - StartTick)
                Using Stream = Client.GetStream()
                    '握手包：包ID 0 + 协议版本 -1 + 主机 + 端口 + 下个状态 1
                    Dim HandshakeBody = PackVarInt(0) & PackVarInt(-1) & PackString(Host) & PackUShort(CUShort(Port)) & PackVarInt(1)
                    WritePacket(Stream, Frame(HandshakeBody))
                    '状态请求包：只有包 ID 0
                    WritePacket(Stream, Frame(PackVarInt(0)))
                    '读响应
                    Dim PacketLen = ReadVarInt(Stream)
                    Dim BodyLen = PacketLen
                    Dim PacketId = ReadVarInt(Stream)
                    BodyLen -= LengthOfVarInt(PacketId)
                    If PacketId <> 0 Then
                        R.Error = "响应包类型异常"
                        Return R
                    End If
                    Dim JsonLen = ReadVarInt(Stream)
                    BodyLen -= LengthOfVarInt(JsonLen)
                    Dim Json As String = ReadString(Stream, JsonLen)
                    ParseJson(Json, R)
                    Return R
                End Using
            End Using
        Catch ex As Exception
            R.Error = If(R.PingMs >= 0, "解析失败", If(ex.Message.Contains("超时") OrElse ex.Message.Contains("timed out"), "连接超时", "无法连接"))
            If R.PingMs < 0 Then R.PingMs = -1
            Return R
        End Try
    End Function

    ''' <summary>解析 status 响应 JSON，只取我们关心的字段。</summary>
    Private Shared Sub ParseJson(Json As String, R As PingResult)
        Try
            Dim Obj As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(Json)
            Dim Ver = Obj("version")
            If Ver IsNot Nothing Then
                R.Version = If(Ver("name"), "").ToString()
            End If
            Dim Players = Obj("players")
            If Players IsNot Nothing Then
                R.OnlinePlayers = CInt(Val(If(Players("online"), "-1").ToString()))
                R.MaxPlayers = CInt(Val(If(Players("max"), "-1").ToString()))
                Dim Sample = Players("sample")
                If Sample IsNot Nothing AndAlso Sample.Type = Newtonsoft.Json.Linq.JTokenType.Array Then
                    For Each Item In Sample
                        Dim N = If(Item("name"), "").ToString().Trim()
                        If N = "" Then Continue For
                        'anonymous player 是正版离线名，统一转义
                        If N.ToLower().StartsWith("anonymous player") Then
                            '格式 "Anonymous Player ***b2f3"，取后 4 位显示 匿名(b2f3)
                            Dim Hash = ""
                            Dim Star = N.LastIndexOf("*"c)
                            If Star >= 0 AndAlso Star + 1 < N.Length Then Hash = N.Substring(Star + 1)
                            R.Players.Add(If(Hash <> "", $"匿名({Hash})", "匿名"))
                        Else
                            R.Players.Add(N)
                        End If
                    Next
                End If
            End If
        Catch ex As Exception
            R.Error = "响应解析失败"
        End Try
    End Sub

#Region "协议编解码"

    Private Shared Function PackVarInt(Value As Long) As String
        '返回的是每字节一个字符（低 8 位有效），后面统一转 byte[]
        '协议里的“-1”（未知协议版本）在 varint 里是 5 字节的无符号 0xFFFFFFFF，必须按无符号处理，否则 >> 会无限循环
        Dim Unsigned As Long = Value And &HFFFFFFFF&
        Dim Bytes As New List(Of Byte)
        Do
            Dim B = CInt(Unsigned And &H7F)
            Unsigned >>= 7
            If Unsigned <> 0 Then B = B Or &H80
            Bytes.Add(CByte(B))
        Loop While Unsigned <> 0
        Return Encoding.GetEncoding(1252).GetString(Bytes.ToArray())
    End Function

    Private Shared Function LengthOfVarInt(Value As Integer) As Integer
        Dim N = 0
        Do
            Value >>= 7
            N += 1
        Loop While Value <> 0
        Return N
    End Function

    Private Shared Function PackString(S As String) As String
        Dim Bytes = Encoding.UTF8.GetBytes(S)
        Return PackVarInt(Bytes.Length) & Encoding.GetEncoding(1252).GetString(Bytes)
    End Function

    Private Shared Function PackUShort(V As UShort) As String
        Return Chr((V >> 8) And &HFF) & Chr(V And &HFF)
    End Function

    ''' <summary>给包体加"长度 varint"前缀。长度按字节数算（不是字符数）。</summary>
    Private Shared Function Frame(Body As String) As String
        Dim ByteLen = Encoding.GetEncoding(1252).GetBytes(Body).Length
        Return PackVarInt(ByteLen) & Body
    End Function
    Private Shared Sub WritePacket(Stream As NetworkStream, Payload As String)
        Dim Bytes = Encoding.GetEncoding(1252).GetBytes(Payload)
        Stream.Write(Bytes, 0, Bytes.Length)
        Stream.Flush()
    End Sub

    Private Shared Function ReadVarInt(Stream As NetworkStream) As Integer
        Dim Value As Integer = 0, Shift As Integer = 0, B As Integer = 0
        Do
            B = Stream.ReadByte()
            If B < 0 Then Throw New IOException("读取 VarInt 时流结束")
            Value = Value Or ((B And &H7F) << Shift)
            Shift += 7
            If Shift > 35 Then Throw New IOException("VarInt 过长")
        Loop While (B And &H80) <> 0
        Return Value
    End Function

    Private Shared Function ReadString(Stream As NetworkStream, Length As Integer) As String
        If Length <= 0 OrElse Length > 2097152 Then Throw New IOException("字符串长度异常")
        Dim Bytes(Length - 1) As Byte
        Dim Got As Integer = 0
        Do While Got < Length
            Dim N = Stream.Read(Bytes, Got, Length - Got)
            If N <= 0 Then Throw New IOException("读取字符串时流结束")
            Got += N
        Loop
        Return Encoding.UTF8.GetString(Bytes)
    End Function

#End Region

End Class
