' 联机模块启动失败识别的回归测试。
'
' 这里用的都是 EasyTier 真实输出过的日志行。
' 识别错了的后果很直接：要么用户看到一堆英文报错不知道怎么办，
' 要么正常日志被误判成「权限不足」，把真正的原因盖掉。

Module LinkDiagnosticsTests

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

    Sub Main()
        Console.OutputEncoding = Text.Encoding.UTF8
        Console.WriteLine("=== 联机启动失败识别 ===")

        TestBlocked()
        TestNormal()

        Console.WriteLine()
        Console.WriteLine($"通过 {Passed}，失败 {Failed}")
        Environment.ExitCode = If(Failed = 0, 0, 1)
    End Sub

    ''' <summary>这些都应判为「被系统拦下」。</summary>
    Private Sub TestBlocked()
        Dim Cases As String() = {
            "2026-10-26T18:29:28.5980609+08:00  WARN CORE: Failed to add Easytier to firewall allowlist, Subnet proxy and KCP proxy may not work properly. error=拒绝访问。 (0x80070005)",
            "2026-10-26T18:29:28.659535+08:00  ERROR CORE::INSTANCE: [6a60782f-b376-4ce9-9003-60767ceeff34] tun device error err=rust tun error Failed to create adapter",
            "ERROR CORE: tun device error err=rust tun error Failed to create adapter",
            "error=拒绝访问。 (0x80070005)",
            "FAILED TO ADD EASYTIER TO FIREWALL ALLOWLIST",
            "failed to create adapter"
        }
        For Each Line As String In Cases
            Check(ModLinkDiagnostics.IsPermissionDenied(Line), "判为权限不足 " & Line.Substring(0, Math.Min(40, Line.Length)))
        Next
    End Sub

    ''' <summary>正常日志不能被误判。</summary>
    Private Sub TestNormal()
        Dim Cases As String() = {
            "",
            "INFO CORE::INSTANCE: new listener added listener=tcp://0.0.0.0:11010",
            "INFO CORE::INSTANCE::CONNECTION: connecting to peer dst=tcp://dreamlife.indevs.in:11010",
            "INFO CORE: new peer added peer_id=612402216",
            "WARN CORE: 已建立连接：tcp://dreamlife.indevs.in:11010",
            "INFO CORE: tun device ready dev=""et_7_ddkw""",
            "failed to listen on tcp://0.0.0.0:11010",
            "ERROR CORE: instance stopped error=some instances stopped with errors"}
        For Each Line As String In Cases
            Check(Not ModLinkDiagnostics.IsPermissionDenied(Line), "不误判 " & If(Line = "", "<空>", Line.Substring(0, Math.Min(40, Line.Length))))
        Next
        '说明文案必须给出可照做的下一步，否则用户看到弹窗也不知道干什么
        Dim Advice As String = ModLinkDiagnostics.PermissionAdvice()
        Check(Advice.Contains("权限"), "说明里点明是权限问题")
        Check(Advice.Contains("管理员身份运行"), "说明里给出以管理员身份运行的办法")
    End Sub

End Module
