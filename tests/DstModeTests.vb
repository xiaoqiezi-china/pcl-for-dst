' DST-Mode（QQ 登录）纯逻辑的回归测试。
'
' 这里最容易出错的是接口字段：uuid 在 poll 里带横线、传给游戏时必须 32 位；
' 绑定码字符集排除了 0/O/1/I；state 是取 token 的钥匙，解析错一步就整条流程失效。

Module DstModeTests

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

    Private ReadOnly Q As Char = ChrW(34)
    Private ReadOnly Bs As Char = ChrW(92)

    Sub Main()
        Console.OutputEncoding = Text.Encoding.UTF8
        Console.WriteLine("=== DST-Mode 登录逻辑 ===")

        TestBase()
        TestUuid()
        TestToken()
        TestBindCode()
        TestJson()
        TestBindSession()
        TestPoll()
        TestFriendlyError()
        TestSkin()

        Console.WriteLine()
        Console.WriteLine($"通过 {Passed}，失败 {Failed}")
        Environment.ExitCode = If(Failed = 0, 0, 1)
    End Sub

    Private Sub TestBase()
        Check(DstModeCore.NormalizeBase(" https://a.com/dstmode/// ") = "https://a.com/dstmode", "规范化 authBase")
        Check(DstModeCore.NormalizeBase("") = "", "空 authBase")
        Check(DstModeCore.IsValidBase("https://a.com/dstmode"), "https 合法")
        Check(DstModeCore.IsValidBase("http://127.0.0.1:18080"), "http 合法")
        Check(Not DstModeCore.IsValidBase("ftp://a.com"), "ftp 非法")
        Check(Not DstModeCore.IsValidBase("a.com"), "缺少协议非法")
        Check(Not DstModeCore.IsValidBase("https://a.com/a b"), "含空格非法")
        Check(Not DstModeCore.IsValidBase(""), "空串非法")
    End Sub

    Private Sub TestUuid()
        Check(DstModeCore.Uuid32("4443CF34-46EF-7C5F-1C51-1ECE97CD053A") = "4443cf3446ef7c5f1c511ece97cd053a", "去横线转小写")
        Check(DstModeCore.Uuid32("4443cf3446ef7c5f1c511ece97cd053a") = "4443cf3446ef7c5f1c511ece97cd053a", "32 位原样")
        Check(DstModeCore.Uuid32("4443cf34-46ef-7c5f-1c51-1ece97cd053") = "", "位数不足")
        Check(DstModeCore.Uuid32("zzzzcf3446ef7c5f1c511ece97cd053a") = "", "非十六进制")
        Check(DstModeCore.Uuid32("") = "", "空 uuid")
    End Sub

    Private Sub TestToken()
        Dim Good As String = New String("a"c, 43)
        Check(DstModeCore.IsValidToken(Good), "43 位合法")
        Check(Not DstModeCore.IsValidToken(New String("a"c, 42)), "42 位非法")
        Check(Not DstModeCore.IsValidToken(New String("a"c, 44)), "44 位非法")
        Check(DstModeCore.IsValidToken(New String("a"c, 42) & "_"), "下划线合法")
        Check(DstModeCore.IsValidToken(New String("a"c, 42) & "-"), "连字符合法")
        Check(Not DstModeCore.IsValidToken(New String("a"c, 42) & "+"), "加号非法")
        Check(Not DstModeCore.IsValidToken(""), "空 token")
    End Sub

    Private Sub TestBindCode()
        Check(DstModeCore.IsValidBindCode("K7M2Q9PX"), "标准绑定码")
        Check(DstModeCore.IsValidBindCode("k7m2q9px"), "小写也认")
        Check(Not DstModeCore.IsValidBindCode("K7M2Q9P"), "7 位非法")
        Check(Not DstModeCore.IsValidBindCode("K7M2Q9P0"), "含 0 非法")
        Check(Not DstModeCore.IsValidBindCode("K7M2Q9PO"), "含 O 非法")
        Check(Not DstModeCore.IsValidBindCode("K7M2Q9PI"), "含 I 非法")
        Check(Not DstModeCore.IsValidBindCode("K7M2Q9P1"), "含 1 非法")
        Check(DstModeCore.BindCommand("k7m2q9px") = "/dst 绑定 K7M2Q9PX", "绑定命令")
        Check(DstModeCore.BindCommand("k7m2q9px", "Alex_01") = "/dst 绑定 K7M2Q9PX Alex_01", "命令带游戏名")
        Check(DstModeCore.BindCommand("k7m2q9px", "  ") = "/dst 绑定 K7M2Q9PX", "空名字不拼")

        Check(DstModeCore.IsValidName("Alex_01"), "合法名字")
        Check(DstModeCore.IsValidName("a" & New String("b"c, 15)), "16 位合法")
        Check(Not DstModeCore.IsValidName("ab"), "太短")
        Check(Not DstModeCore.IsValidName("a" & New String("b"c, 16)), "太长")
        Check(Not DstModeCore.IsValidName("小茄"), "中文非法")
        Check(Not DstModeCore.IsValidName("Alex-01"), "连字符非法")
        Check(Not DstModeCore.IsValidName(""), "空名字非法")
    End Sub

    Private Sub TestJson()
        Dim Json As String = "{" & Q & "a" & Q & ":" & Q & "x" & Q & "," & Q & "n" & Q & ":12," & Q & "neg" & Q & ":-3}"
        Check(DstModeCore.JsonString(Json, "a") = "x", "取字符串")
        Check(DstModeCore.JsonInteger(Json, "n") = 12, "取整数")
        Check(DstModeCore.JsonInteger(Json, "neg") = -3, "取负整数")
        Check(DstModeCore.JsonInteger(Json, "missing", 7) = 7, "缺字段用默认值")
        Check(DstModeCore.JsonString(Json, "missing") = "", "缺字段返回空")

        '转义：引号、反斜杠、unicode
        Dim Escaped As String = "{" & Q & "s" & Q & ":" & Q & "a" & Bs & Q & "b" & Bs & Bs & "c" & Bs & "u4e2d" & Q & "}"
        Check(DstModeCore.JsonString(Escaped, "s") = "a" & Q & "b" & Bs & "c" & "中", "处理 JSON 转义")
    End Sub

    Private Sub TestBindSession()
        Dim Json As String = "{" & Q & "state" & Q & ":" & Q & "9f2c1a4b8e7d6c5b4a39281706f5e4d3" & Q & "," &
                             Q & "code" & Q & ":" & Q & "k7m2q9px" & Q & "," &
                             Q & "expiresIn" & Q & ":300," &
                             Q & "instruction" & Q & ":" & Q & "在 QQ 里发送" & Q & "}"
        Dim Session = DstModeCore.ParseBindSession(Json)
        Check(Session.Ok, "绑定会话可用")
        Check(Session.State = "9f2c1a4b8e7d6c5b4a39281706f5e4d3", "state 原样保留")
        Check(Session.Code = "K7M2Q9PX", "code 转大写")
        Check(Session.ExpiresIn = 300, "有效期")
        Check(Session.Instruction = "在 QQ 里发送", "提示文案")

        Dim Bad As String = "{" & Q & "state" & Q & ":" & Q & "abc" & Q & "," & Q & "code" & Q & ":" & Q & "123" & Q & "}"
        Dim BadSession = DstModeCore.ParseBindSession(Bad)
        Check(Not BadSession.Ok, "非法绑定码被丢")
        Check(BadSession.ErrorText <> "", "给出错误原因")
    End Sub

    Private Sub TestPoll()
        Dim Pending = DstModeCore.ParsePoll("{" & Q & "status" & Q & ":" & Q & "pending" & Q & "}")
        Check(Pending.Status = DstModeCore.PollStatus.Pending, "pending")

        Dim OkJson As String = "{" & Q & "status" & Q & ":" & Q & "ok" & Q & "," &
                               Q & "accessToken" & Q & ":" & Q & New String("a"c, 43) & Q & "," &
                               Q & "uuid" & Q & ":" & Q & "4443cf34-46ef-7c5f-1c51-1ece97cd053a" & Q & "," &
                               Q & "name" & Q & ":" & Q & "Alex" & Q & "," &
                               Q & "expiresIn" & Q & ":43180}"
        Dim OkResult = DstModeCore.ParsePoll(OkJson)
        Check(OkResult.Status = DstModeCore.PollStatus.Ok, "ok 状态")
        Check(OkResult.Name = "Alex", "玩家名")
        Check(OkResult.ExpiresIn = 43180, "有效期")
        Check(DstModeCore.Uuid32(OkResult.Uuid) = "4443cf3446ef7c5f1c511ece97cd053a", "uuid 可转 32 位")

        Dim Expired = DstModeCore.ParsePoll("{" & Q & "status" & Q & ":" & Q & "expired" & Q & "}")
        Check(Expired.Status = DstModeCore.PollStatus.Expired, "expired")

        Dim ErrorResult = DstModeCore.ParsePoll("{" & Q & "status" & Q & ":" & Q & "error" & Q & "," & Q & "error" & Q & ":" & Q & "boom" & Q & "}")
        Check(ErrorResult.Status = DstModeCore.PollStatus.Failed, "error 状态")
        Check(ErrorResult.ErrorText = "boom", "错误文案")

        'ok 但缺 uuid：不能当成登录成功
        Dim BrokenJson As String = "{" & Q & "status" & Q & ":" & Q & "ok" & Q & "," &
                                   Q & "accessToken" & Q & ":" & Q & New String("a"c, 43) & Q & "," &
                                   Q & "name" & Q & ":" & Q & "Alex" & Q & "}"
        Dim Broken = DstModeCore.ParsePoll(BrokenJson)
        Check(Broken.Status = DstModeCore.PollStatus.Failed, "缺 uuid 判失败")

        Dim Unknown = DstModeCore.ParsePoll("{}")
        Check(Unknown.Status = DstModeCore.PollStatus.Failed, "空响应判失败")
    End Sub

    Private Sub TestFriendlyError()
        Check(DstModeCore.FriendlyError(404, "") = "这个绑定已经失效，请重新登录", "404 文案")
        Check(DstModeCore.FriendlyError(429, "") = "请求太频繁，请稍后再试", "429 文案")
        Check(DstModeCore.FriendlyError(500, "") = DstModeCore.ServiceUnavailableText, "500 文案")
        Check(DstModeCore.FriendlyError(0, "") <> "", "网络错误有文案")
        Check(DstModeCore.FriendlyError(502, "{" & Q & "error" & Q & ":" & Q & "ForbiddenOperationException" & Q & "}") =
              DstModeCore.ServiceUnavailableText, "上游不可达按服务不可用处理")
    End Sub


    Private Sub TestSkin()
        '尺寸：服务端只认 64×64，不缩放
        Check(DstModeCore.IsValidSkinSize(64, 64), "64×64 合法")
        Check(Not DstModeCore.IsValidSkinSize(64, 32), "64×32（老皮肤格式）不合法")
        Check(Not DstModeCore.IsValidSkinSize(128, 128), "128×128 不合法")
        Check(Not DstModeCore.IsValidSkinSize(0, 0), "0×0 不合法")
        Check(DstModeCore.SkinSizeText(128, 128) = "128×128", "尺寸文案")
        Check(DstModeCore.SkinSize = 64, "尺寸常量为 64")
        Check(DstModeCore.SkinMaxBytes = 2L * 1024 * 1024, "体积上限为 2 MB")

        '地址拼接：去掉末尾斜杠，避免 //api
        Check(DstModeCore.SkinUploadUrl("https://a.com/dstmode") = "https://a.com/dstmode/api/v1/skin/upload", "上传地址")
        Check(DstModeCore.SkinUploadUrl("https://a.com/dstmode/") = "https://a.com/dstmode/api/v1/skin/upload", "末尾斜杠不产生双斜杠")
        Check(DstModeCore.SkinMeUrl("http://127.0.0.1:18080/dstmode") = "http://127.0.0.1:18080/dstmode/api/v1/skin/me", "皮肤状态地址")

        '错误映射：400 必须把服务端写的中文原因透出来，而不是统一文案
        Const SizeErr As String = "{""error"":""IllegalArgumentException"",""errorMessage"":""皮肤必须是 64×64 像素（ 这张是 128×128 ）""}"
        Check(DstModeCore.FriendlySkinError(400, SizeErr) = "皮肤必须是 64×64 像素（ 这张是 128×128 ）", "400 透出服务端原因")
        Check(DstModeCore.FriendlySkinError(400, "{}") = "这张皮肤不符合要求", "400 无原因时的兜底")
        Check(DstModeCore.FriendlySkinError(401, "") = "登录已失效，请重新登录后再试", "401 提示重新登录")
        Check(DstModeCore.FriendlySkinError(404, "") = "当前服务器没有启用皮肤功能", "404 提示未启用")
        Check(DstModeCore.FriendlySkinError(413, "") = "皮肤文件不能超过 2 MB", "413 提示体积")
        Check(DstModeCore.FriendlySkinError(429, "") = "上传太频繁，请稍后再试", "429 提示限流")
        Check(DstModeCore.FriendlySkinError(500, "") = DstModeCore.ServiceUnavailableText, "5xx 统一文案")
        Check(DstModeCore.FriendlySkinError(0, "") = "上传失败，请稍后再试", "无状态码的兜底")

        'JSON 里的中文与转义不能把错误文案截断
        Const EscErr As String = "{""errorMessage"":""第一行\n第二行 \""引号\""""}"
        Check(DstModeCore.FriendlySkinError(400, EscErr) = "第一行" & ControlChars.Lf & "第二行 ""引号""", "错误文案里的转义")
    End Sub

End Module
