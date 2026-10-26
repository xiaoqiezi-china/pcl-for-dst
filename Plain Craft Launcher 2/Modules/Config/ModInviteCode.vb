Imports System.Text

''' <summary>
''' 邀请码里的节点地址编解码。
''' <para>
''' 邀请码必须把房主实际使用的节点地址带给加入者：节点清单是动态的，
''' 房主和加入者拉到的版本可能不同，只靠「节点 ID」会连到不同地址。
''' </para>
''' <para>
''' 但地址明文写在邀请码里既难看（一整条 tcp://host:port 直接摊在界面上），
''' 又把房主走的节点暴露给所有看到邀请码的人。所以这里先把地址按固定布局打成
''' 二进制，再用 Base32 转成字母数字，看起来就是一串普通的大写校验码。
''' </para>
''' <para>
''' 用 Crockford Base32：字母表去掉了 I、L、O、U 四个容易看错的字符，
''' 且天然全大写，正好和邀请码前半段的输入修正（O→0、I→1）对得上。
''' </para>
''' </summary>
Public Module ModInviteCode

    ''' <summary>Base32 字母表。去掉了 I、L、O、U。</summary>
    Private Const Alphabet As String = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

    ''' <summary>协议编号。下标即编码值，0 号留空表示非法。</summary>
    Private ReadOnly Schemes As String() = {"", "tcp", "udp", "ws", "wss"}

    ''' <summary>载荷尾部长度：协议 1 + 端口 2。</summary>
    Private Const TailSize As Integer = 3

    ''' <summary>
    ''' 打散用的固定掩码。
    ''' <para>
    ''' 纯 Base32 会让同一协议、同一端口的邀请码永远以相同字符开头
    ''' （tcp + 11010 一定以 04NG4 起头），别人一眼就能看出「这是个联机节点码」，
    ''' 甚至能猜出端口。异或一串固定字节后就没有这种固定前缀了。
    ''' </para>
    ''' <para>
    ''' 这只是混淆，不是加密：算法在客户端里，谁想解都能解。
    ''' 目的仅仅是让邀请码看起来是一串普通校验码，不把节点地址直接摆出来。
    ''' 节点地址本来也不是秘密（它就在公开清单里），要保密的是「房主走的哪个节点」。
    ''' </para>
    ''' </summary>
    Private ReadOnly Mask As Byte() = {&H5A, &H3C, &H91, &H27, &HE4, &H6D, &H08, &HB2}

    Private Sub ApplyMask(Data As Byte())
        For i As Integer = 0 To Data.Length - 1
            Data(i) = CByte(Data(i) Xor Mask(i Mod Mask.Length))
        Next
    End Sub

    ''' <summary>
    ''' 把节点地址打包成一串大写字母数字，每 5 个字符用 - 分隔。地址不合法时返回空串。
    ''' <code>
    ''' "tcp://dreamlife.indevs.in:11010" → "04NG4-4V4E9-JP2VB-..."
    ''' </code>
    ''' </summary>
    Public Function Encode(Address As String) As String
        If String.IsNullOrWhiteSpace(Address) Then Return ""
        Dim Rest As String = Address.Trim()
        '拆协议
        Dim Scheme As String = ""
        Dim SchemeEnd As Integer = Rest.IndexOf("://", StringComparison.Ordinal)
        If SchemeEnd > 0 Then
            Scheme = Rest.Substring(0, SchemeEnd).ToLowerInvariant()
            Rest = Rest.Substring(SchemeEnd + 3)
        End If
        Dim SchemeId As Integer = Array.IndexOf(Schemes, Scheme)
        If SchemeId <= 0 Then Return ""
        '允许 tcp://host:port/xxx 这种带路径的写法，路径对连接没用，丢掉
        Rest = Rest.Split("/"c)(0)
        '拆主机名与端口。主机名里不会有冒号，用最后一个冒号切就够
        Dim Colon As Integer = Rest.LastIndexOf(":"c)
        If Colon <= 0 Then Return ""
        Dim Host As String = Rest.Substring(0, Colon)
        Dim Port As Integer
        If Not Integer.TryParse(Rest.Substring(Colon + 1), Port) Then Return ""
        If Host = "" OrElse Port <= 0 OrElse Port > 65535 Then Return ""
        Dim HostBytes As Byte() = Encoding.UTF8.GetBytes(Host)
        'DNS 主机名上限 255 字节，超了说明这根本不是个地址
        If HostBytes.Length = 0 OrElse HostBytes.Length > 255 Then Return ""
        '布局：[主机名 n][协议 1][端口 2，大端]
        '协议和端口放在最后，是为了让编码结果的开头随主机名变化。
        '如果把它们放前面，所有 tcp + 11010 的邀请码都会以同一串字符开头，
        '等于把端口直接写在脸上。
        Dim Payload(HostBytes.Length + TailSize - 1) As Byte
        Array.Copy(HostBytes, 0, Payload, 0, HostBytes.Length)
        Payload(HostBytes.Length) = CByte(SchemeId)
        Payload(HostBytes.Length + 1) = CByte(Port >> 8)
        Payload(HostBytes.Length + 2) = CByte(Port And &HFF)
        ApplyMask(Payload)
        Return Group(ToBase32(Payload))
    End Function

    ''' <summary>
    ''' 把 <see cref="Encode"/> 的结果还原成节点地址。
    ''' 解不出来返回空串，调用方按「邀请码没带地址」处理，不要当成错误。
    ''' </summary>
    Public Function Decode(Blob As String) As String
        If String.IsNullOrWhiteSpace(Blob) Then Return ""
        Dim Data As Byte() = FromBase32(Blob)
        If Data Is Nothing OrElse Data.Length < TailSize + 1 Then Return ""
        ApplyMask(Data) '掩码是对称的，同一个函数再异或一次就还原
        '主机名占前面所有字节，长度由总长度推出来，不用单独存长度字段
        Dim HostLen As Integer = Data.Length - TailSize
        If HostLen <= 0 Then Return ""
        Dim SchemeId As Integer = Data(HostLen)
        If SchemeId <= 0 OrElse SchemeId >= Schemes.Length Then Return ""
        Dim Port As Integer = (CInt(Data(HostLen + 1)) << 8) Or CInt(Data(HostLen + 2))
        If Port <= 0 Then Return ""
        Dim Host As String
        Try
            Host = Encoding.UTF8.GetString(Data, 0, HostLen)
        Catch ex As Exception
            Return ""
        End Try
        If Host = "" Then Return ""
        Return Schemes(SchemeId) & "://" & Host & ":" & Port
    End Function

    ''' <summary>
    ''' 判断这串尾巴是不是明文地址。版本 3 的邀请码是明文，需要继续兼容。
    ''' </summary>
    Public Function IsPlainAddress(Text As String) As Boolean
        If String.IsNullOrWhiteSpace(Text) Then Return False
        For Each Scheme In Schemes
            If Scheme <> "" AndAlso Text.StartsWith(Scheme & "://", StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ''' <summary>把明文地址的协议前缀统一成小写，其余部分原样保留。</summary>
    Public Function NormalizePlainAddress(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return ""
        Dim Value As String = Text.Trim()
        Dim SchemeEnd As Integer = Value.IndexOf("://", StringComparison.Ordinal)
        If SchemeEnd <= 0 Then Return Value
        Return Value.Substring(0, SchemeEnd).ToLowerInvariant() & Value.Substring(SchemeEnd)
    End Function

#Region "Base32"

    ''' <summary>
    ''' 按位打包，不补零：8n 个比特编成 ⌈8n/5⌉ 个字符，
    ''' 最后一个字符不足 5 位就低位补零。这样不会在末尾露出一串补位的 0。
    ''' </summary>
    Private Function ToBase32(Data As Byte()) As String
        Dim TotalBits As Integer = Data.Length * 8
        Dim CharCount As Integer = (TotalBits + 4) \ 5
        Dim Sb As New StringBuilder(CharCount)
        For i As Integer = 0 To CharCount - 1
            Dim Value As Integer = 0
            For j As Integer = 0 To 4
                Value <<= 1
                Dim Pos As Integer = i * 5 + j
                If Pos < TotalBits Then
                    Dim ByteIndex As Integer = Pos \ 8
                    Dim BitInByte As Integer = 7 - (Pos Mod 8)
                    Value = Value Or ((Data(ByteIndex) >> BitInByte) And 1)
                End If
            Next
            Sb.Append(Alphabet(Value))
        Next
        Return Sb.ToString()
    End Function

    ''' <summary>反向按位解包；不足一字节的尾部比特丢弃。</summary>
    Private Function FromBase32(Text As String) As Byte()
        '先规范化并收集每个字符的值
        Dim Values As New List(Of Integer)(Text.Length)
        For Each Raw As Char In Text.ToUpperInvariant()
            If Raw = " "c OrElse Raw = "-"c Then Continue For
            Dim Ch As Char = Raw
            'Crockford 的别名写法
            If Ch = "O"c Then Ch = "0"c
            If Ch = "I"c OrElse Ch = "L"c Then Ch = "1"c
            Dim Index As Integer = Alphabet.IndexOf(Ch)
            If Index < 0 Then Return Nothing
            Values.Add(Index)
        Next
        If Values.Count = 0 Then Return Nothing
        Dim TotalBits As Integer = Values.Count * 5
        Dim ByteCount As Integer = TotalBits \ 8
        If ByteCount = 0 Then Return Nothing
        Dim Result(ByteCount - 1) As Byte
        For i As Integer = 0 To ByteCount - 1
            Dim Value As Integer = 0
            For j As Integer = 0 To 7
                Value <<= 1
                Dim Pos As Integer = i * 8 + j
                If Pos < TotalBits Then
                    Dim CharIndex As Integer = Pos \ 5
                    Dim BitInChar As Integer = 4 - (Pos Mod 5)
                    Value = Value Or ((Values(CharIndex) >> BitInChar) And 1)
                End If
            Next
            Result(i) = CByte(Value)
        Next
        Return Result
    End Function

    ''' <summary>每 5 个字符插一个 -，和邀请码前半段的分组风格一致，也方便肉眼核对。</summary>
    Private Function Group(Blob As String) As String
        Dim Sb As New StringBuilder(Blob.Length + Blob.Length \ 5)
        For i As Integer = 0 To Blob.Length - 1
            If i > 0 AndAlso i Mod 5 = 0 Then Sb.Append("-"c)
            Sb.Append(Blob(i))
        Next
        Return Sb.ToString()
    End Function

#End Region

End Module
