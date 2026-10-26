' 邀请码节点地址编解码的回归测试。
'
' 这段是纯逻辑（不依赖 WPF），和 NbtTests 一样用 vbc 编成控制台程序跑。
' 邀请码一旦生成就发出去了，编解码必须稳定：任何一次改动导致老码解不出来，
' 都会让已经发出去的邀请码失效，所以这里把边界情况都钉住。

Module InviteCodeTests

    Private Failed As Integer = 0
    Private Passed As Integer = 0

    Private Sub Check(Ok As Boolean, Name As String, Optional Detail As String = "")
        If Ok Then
            Passed += 1
        Else
            Failed += 1
            Console.WriteLine("  [失败] " & Name & If(Detail = "", "", "  <- " & Detail))
        End If
    End Sub

    Private Sub CheckEqual(Actual As Object, Expected As Object, Name As String)
        Dim A As String = If(Actual Is Nothing, "<null>", Actual.ToString())
        Dim E As String = If(Expected Is Nothing, "<null>", Expected.ToString())
        Check(A = E, Name, "期望 " & E & "，实际 " & A)
    End Sub

    ''' <summary>
    ''' 编码结果应当是「5 个字符一组、用 - 分隔」的纯大写字母数字，
    ''' 且不含 I/L/O/U 这些容易看错的字符。
    ''' </summary>
    Private Sub CheckShape(Blob As String, Name As String)
        Check(Blob.Length > 0, Name & "：非空")
        Dim Groups As String() = Blob.Split("-"c)
        Dim Ok As Boolean = True
        For i As Integer = 0 To Groups.Length - 1
            '只有最后一组可能不满 5 个
            If i < Groups.Length - 1 AndAlso Groups(i).Length <> 5 Then Ok = False
            If Groups(i).Length = 0 OrElse Groups(i).Length > 5 Then Ok = False
            For Each Ch As Char In Groups(i)
                If Not ((Ch >= "0"c AndAlso Ch <= "9"c) OrElse (Ch >= "A"c AndAlso Ch <= "Z"c)) Then Ok = False
            Next
        Next
        Check(Ok, Name & "：按 5 个一组分隔", Blob)
        Check(Not Blob.Contains("I") AndAlso Not Blob.Contains("L") AndAlso
              Not Blob.Contains("O") AndAlso Not Blob.Contains("U"), Name & "：不含 I/L/O/U")
        '不该在末尾露出一串补位的 0
        Check(Not Blob.EndsWith("00000"), Name & "：末尾没有整组补零", Blob)
    End Sub

    Sub Main()
        Console.OutputEncoding = Text.Encoding.UTF8
        Console.WriteLine("=== 邀请码节点地址 ===")

        TestRoundTrip()
        TestShape()
        TestStability()
        TestSchemes()
        TestRejects()
        TestPlainAddress()
        TestLength()
        TestNotReadable()

        Console.WriteLine()
        Console.WriteLine($"通过 {Passed}，失败 {Failed}")
        Environment.ExitCode = If(Failed = 0, 0, 1)
    End Sub

    ''' <summary>常见地址必须能原样还原。</summary>
    Private Sub TestRoundTrip()
        Dim Cases As String() = {
            "tcp://dreamlife.indevs.in:11010",
            "tcp://easytier-us.slarker.me:11010",
            "tcp://1.2.3.4:65535",
            "tcp://127.0.0.1:1",
            "udp://example.com:19132",
            "ws://a.b:8080",
            "wss://a.b:443",
            "tcp://中文域名.测试:11010"}
        For Each Address As String In Cases
            Dim Blob As String = ModInviteCode.Encode(Address)
            CheckEqual(ModInviteCode.Decode(Blob), Address, "往返 " & Address)
        Next
    End Sub

    Private Sub TestShape()
        CheckShape(ModInviteCode.Encode("tcp://dreamlife.indevs.in:11010"), "长域名")
        CheckShape(ModInviteCode.Encode("tcp://1.2.3.4:11010"), "IPv4")
        CheckShape(ModInviteCode.Encode("tcp://a:1"), "最短")
    End Sub

    ''' <summary>
    ''' 编码结果必须固定。这不是为了好看，而是因为邀请码会被存进
    ''' LinkLastAutoJoinInviteCode 并可能被用户收藏；算法一变，
    ''' 老码至少还要能解出来，编码侧也要保证同一地址得到同一结果。
    ''' </summary>
    Private Sub TestStability()
        Dim A As String = ModInviteCode.Encode("tcp://dreamlife.indevs.in:11010")
        Dim B As String = ModInviteCode.Encode("tcp://dreamlife.indevs.in:11010")
        CheckEqual(A, B, "同一地址编码结果稳定")
        '不同地址不能撞
        Check(A <> ModInviteCode.Encode("tcp://dreamlife.indevs.in:11011"), "端口不同结果不同")
        Check(A <> ModInviteCode.Encode("udp://dreamlife.indevs.in:11010"), "协议不同结果不同")
        Check(A <> ModInviteCode.Encode("tcp://dreamlife.indevs.in2:11010"), "主机名不同结果不同")
    End Sub

    ''' <summary>四种协议都要能编能解，且互不混淆。</summary>
    Private Sub TestSchemes()
        Dim Seen As New List(Of String)
        For Each Scheme As String In {"tcp", "udp", "ws", "wss"}
            Dim Address As String = Scheme & "://host.test:11010"
            Dim Blob As String = ModInviteCode.Encode(Address)
            CheckEqual(ModInviteCode.Decode(Blob), Address, "协议 " & Scheme)
            Check(Not Seen.Contains(Blob), "协议 " & Scheme & "：编码不重复")
            Seen.Add(Blob)
        Next
    End Sub

    ''' <summary>畸形输入一律返回空串，不能抛异常。</summary>
    Private Sub TestRejects()
        Dim Bad As String() = {
            "", "   ", "tcp://", "tcp://:11010", "tcp://host", "tcp://host:",
            "tcp://host:0", "tcp://host:65536", "tcp://host:abc",
            "http://host:11010", "://host:11010", "host:11010"}
        For Each Address As String In Bad
            CheckEqual(ModInviteCode.Encode(Address), "", "拒绝编码 " & If(Address = "", "<空>", Address))
        Next
        '解码侧：乱码不能崩
        Dim BadBlobs As String() = {"", "A", "ABCDEFG", "ABCDEFGH", "!!!!!!", "11111111", "ZZZZZZZZ"}
        For Each Blob As String In BadBlobs
            Dim R As String = ""
            Try
                R = ModInviteCode.Decode(Blob)
            Catch ex As Exception
                Check(False, "解码不抛异常 " & If(Blob = "", "<空>", Blob), ex.Message)
                Continue For
            End Try
            Check(True, "解码不抛异常 " & If(Blob = "", "<空>", Blob))
            '解不出来必须是空串，不能返回半个地址
            If R <> "" Then Check(R.Contains("://"), "解码结果要么空要么是地址 " & Blob, R)
        Next
    End Sub

    ''' <summary>版本 3 的明文地址仍要能被识别并规范化。</summary>
    Private Sub TestPlainAddress()
        Check(ModInviteCode.IsPlainAddress("tcp://host:11010"), "明文 tcp")
        Check(ModInviteCode.IsPlainAddress("TCP://host:11010"), "明文大写协议也算")
        Check(Not ModInviteCode.IsPlainAddress("4R8ZABCD"), "编码串不算明文")
        Check(Not ModInviteCode.IsPlainAddress(""), "空串不算明文")
        CheckEqual(ModInviteCode.NormalizePlainAddress("TCP://Host:11010"), "tcp://Host:11010", "只把协议转小写")
        CheckEqual(ModInviteCode.NormalizePlainAddress("tcp://Host:11010"), "tcp://Host:11010", "已经是小写则不变")
    End Sub

    ''' <summary>
    ''' 编码结果不能把地址直接露出来，也不能有固定的开头。
    ''' <para>
    ''' 邀请码是要发出去的，如果一眼就能看出用的是哪个节点，
    ''' 那编码就白做了；同理如果所有 tcp + 11010 的码都以同一串开头，
    ''' 等于把端口写在脸上。
    ''' </para>
    ''' </summary>
    Private Sub TestNotReadable()
        Dim Cases As String() = {
            "tcp://dreamlife.indevs.in:11010",
            "tcp://easytier-us.slarker.me:11010",
            "tcp://1.2.3.4:11010"}
        For Each Address As String In Cases
            Dim Blob As String = ModInviteCode.Encode(Address)
            Dim Bare As String = Blob.Replace("-", "")
            Check(Not Bare.Contains("TCP"), "不露协议 " & Address)
            Check(Not Bare.Contains("11010"), "不露端口 " & Address)
            '主机名里最长的那一段不该原样出现
            Dim Host As String = Address.Split("/"c)(2).Split(":"c)(0)
            For Each Part As String In Host.Split("."c)
                If Part.Length >= 4 Then
                    Check(Not Bare.Contains(Part.ToUpperInvariant()), "不露主机名片段 " & Part)
                End If
            Next
        Next
        '不同主机名的编码不该共享长前缀
        Dim A As String = ModInviteCode.Encode("tcp://dreamlife.indevs.in:11010")
        Dim B As String = ModInviteCode.Encode("tcp://easytier-us.slarker.me:11010")
        Check(A.Substring(0, 3) <> B.Substring(0, 3), "不同主机名开头不同", A.Substring(0, 5) & " / " & B.Substring(0, 5))
    End Sub

    ''' <summary>主机名长度不同，编码长度跟着变，但都要能原样还原。</summary>
    Private Sub TestLength()
        For HostLen As Integer = 1 To 40
            Dim Host As String = New String("a"c, HostLen)
            Dim Blob As String = ModInviteCode.Encode("tcp://" & Host & ":11010")
            Check(Blob.Length > 0, "主机名长度 " & HostLen & " 编得出来")
            CheckEqual(ModInviteCode.Decode(Blob), "tcp://" & Host & ":11010", "主机名长度 " & HostLen & " 往返")
        Next
        '最长合法主机名（255 字节）
        Dim Longest As String = New String("a"c, 255)
        CheckEqual(ModInviteCode.Decode(ModInviteCode.Encode("tcp://" & Longest & ":11010")),
                   "tcp://" & Longest & ":11010", "255 字节主机名往返")
        '超长主机名要拒绝，不能截断
        CheckEqual(ModInviteCode.Encode("tcp://" & New String("a"c, 256) & ":11010"), "", "256 字节主机名拒绝编码")
    End Sub

End Module