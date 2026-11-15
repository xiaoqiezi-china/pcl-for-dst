Imports System.Globalization
Imports System.Text

''' <summary>
''' DST-Mode（帝释天签发服务器）QQ 登录的纯逻辑。
''' <para>
''' 这里只做字符串与格式处理：不联网、不读设置、不碰 UI，便于用 vbc 单独编译测试。
''' 真正的请求、轮询与登录态在 <see cref="ModDstMode"/> 里。
''' </para>
''' </summary>
Public Class DstModeCore

    ''' <summary>绑定码长度，服务端固定 8 位。</summary>
    Public Const BindCodeLength As Integer = 8

    ''' <summary>绑定码字符集：去掉了 0/O/1/I，与服务端一致。</summary>
    Public Const BindCodeAlphabet As String = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"

    ''' <summary>accessToken 长度，43 位 base64url。</summary>
    Public Const TokenLength As Integer = 43

    ''' <summary>轮询间隔（毫秒）。</summary>
    Public Const PollIntervalMs As Integer = 1500

    ''' <summary>首次轮询前的等待（毫秒）。</summary>
    Public Const FirstPollDelayMs As Integer = 500

    ''' <summary>给玩家看的统一失败文案，接口里的脏细节只进日志。</summary>
    Public Const ServiceUnavailableText As String = "登录服务暂时不可用，请稍后再试"

    ''' <summary>
    ''' 规范化 authBase：去掉首尾空白与末尾斜杠。
    ''' 末尾斜杠必须去掉，模组自己会拼 /session/minecraft/...，多一个斜杠就 404。
    ''' </summary>
    Public Shared Function NormalizeBase(Base As String) As String
        If Base Is Nothing Then Return ""
        Dim Clean As String = Base.Trim()
        Do While Clean.EndsWith("/", StringComparison.Ordinal)
            Clean = Clean.Substring(0, Clean.Length - 1)
        Loop
        Return Clean
    End Function

    ''' <summary>authBase 是否是一个像样的 http(s) 绝对地址。</summary>
    Public Shared Function IsValidBase(Base As String) As Boolean
        Dim Clean As String = NormalizeBase(Base)
        If Clean = "" Then Return False
        If Clean.Contains(" ") Then Return False
        If Not (Clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
                Clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Then Return False
        Dim Parsed As Uri = Nothing
        If Not Uri.TryCreate(Clean, UriKind.Absolute, Parsed) Then Return False
        Return Parsed.Host <> ""
    End Function

    ''' <summary>
    ''' 把 36 位带横线的 UUID 收敛成游戏需要的 32 位小写形式。
    ''' 不合法时返回空串。
    ''' </summary>
    Public Shared Function Uuid32(Uuid As String) As String
        If Uuid Is Nothing Then Return ""
        Dim Clean As String = Uuid.Trim().Replace("-", "").ToLowerInvariant()
        If Clean.Length <> 32 Then Return ""
        For Each C As Char In Clean
            Dim Ok As Boolean = (C >= "0"c AndAlso C <= "9"c) OrElse (C >= "a"c AndAlso C <= "f"c)
            If Not Ok Then Return ""
        Next
        Return Clean
    End Function

    ''' <summary>accessToken 是否是 43 位 base64url。</summary>
    Public Shared Function IsValidToken(Token As String) As Boolean
        If Token Is Nothing Then Return False
        Dim Clean As String = Token.Trim()
        If Clean.Length <> TokenLength Then Return False
        For Each C As Char In Clean
            Dim Ok As Boolean = (C >= "A"c AndAlso C <= "Z"c) OrElse
                               (C >= "a"c AndAlso C <= "z"c) OrElse
                               (C >= "0"c AndAlso C <= "9"c) OrElse
                               C = "-"c OrElse C = "_"c
            If Not Ok Then Return False
        Next
        Return True
    End Function

    ''' <summary>绑定码是否是 8 位合法字符。</summary>
    Public Shared Function IsValidBindCode(Code As String) As Boolean
        If Code Is Nothing Then Return False
        Dim Clean As String = Code.Trim().ToUpperInvariant()
        If Clean.Length <> BindCodeLength Then Return False
        For Each C As Char In Clean
            If BindCodeAlphabet.IndexOf(C) < 0 Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' 游戏名：3-16 位，只允许字母、数字、下划线。
    ''' 与服务端的校验保持一致，免得名字发出去了才被拒。
    ''' </summary>
    Public Shared Function IsValidName(Name As String) As Boolean
        If Name Is Nothing Then Return False
        Dim Clean As String = Name.Trim()
        If Clean.Length < 3 OrElse Clean.Length > 16 Then Return False
        For Each C As Char In Clean
            Dim Ok As Boolean = (C >= "a"c AndAlso C <= "z"c) OrElse
                               (C >= "A"c AndAlso C <= "Z"c) OrElse
                               (C >= "0"c AndAlso C <= "9"c) OrElse C = "_"c
            If Not Ok Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' 玩家要在 QQ 里发送的完整命令。带上游戏名时由机器人直接把名字绑定到 QQ。
    ''' </summary>
    Public Shared Function BindCommand(Code As String, Optional Name As String = "") As String
        If Code Is Nothing Then Return ""
        Dim Command As String = "/dst 绑定 " & Code.Trim().ToUpperInvariant()
        If Not String.IsNullOrWhiteSpace(Name) Then Command &= " " & Name.Trim()
        Return Command
    End Function

#Region "极简 JSON 读取"

    ' 启动器只能拿到一层扁平对象，没必要为此引入完整的 JSON 库；
    ' 更重要的是这几个解析要能在「零依赖」的测试工程里跑。

    Private Shared ReadOnly Property QuoteChar As Char
        Get
            Return ChrW(34)
        End Get
    End Property

    Private Shared ReadOnly Property BackslashChar As Char
        Get
            Return ChrW(92)
        End Get
    End Property

    ''' <summary>
    ''' 找到某个「键」对应的值起始下标。找不到返回 -1。
    ''' <para>
    ''' 不能直接 IndexOf：值里也可能出现与键相同的文字，例如
    ''' {"status":"error","error":"boom"}，直接找 "error" 会先命中 status 的值。
    ''' 因此必须确认这个键后面跟的是冒号。
    ''' </para>
    ''' </summary>
    Private Shared Function FindValueStart(Json As String, Key As String) As Integer
        Dim Token As String = QuoteChar & Key & QuoteChar
        Dim SearchAt As Integer = 0
        Do
            Dim Pos As Integer = Json.IndexOf(Token, SearchAt, StringComparison.Ordinal)
            If Pos < 0 Then Return -1
            Dim After As Integer = Pos + Token.Length
            Do While After < Json.Length AndAlso Char.IsWhiteSpace(Json(After))
                After += 1
            Loop
            If After < Json.Length AndAlso Json(After) = ":"c Then
                After += 1
                Do While After < Json.Length AndAlso Char.IsWhiteSpace(Json(After))
                    After += 1
                Loop
                Return After
            End If
            SearchAt = Pos + Token.Length
        Loop
    End Function

    ''' <summary>取出字符串字段；不存在或类型不符时返回空串。</summary>
    Public Shared Function JsonString(Json As String, Key As String) As String
        If String.IsNullOrEmpty(Json) OrElse String.IsNullOrEmpty(Key) Then Return ""
        Dim Pos As Integer = FindValueStart(Json, Key)
        If Pos < 0 OrElse Pos >= Json.Length OrElse Json(Pos) <> QuoteChar Then Return ""
        Pos += 1
        Dim Sb As New StringBuilder
        Do While Pos < Json.Length
            Dim C As Char = Json(Pos)
            If C = QuoteChar Then Exit Do
            If C = BackslashChar Then
                Pos += 1
                If Pos >= Json.Length Then Exit Do
                Dim Esc As Char = Json(Pos)
                Select Case Esc
                    Case "n"c
                        Sb.Append(ControlChars.Lf)
                    Case "r"c
                        Sb.Append(ControlChars.Cr)
                    Case "t"c
                        Sb.Append(ControlChars.Tab)
                    Case "b"c
                        Sb.Append(ChrW(8))
                    Case "f"c
                        Sb.Append(ChrW(12))
                    Case "u"c
                        If Pos + 4 < Json.Length Then
                            Dim Code As Integer
                            If Integer.TryParse(Json.Substring(Pos + 1, 4), NumberStyles.HexNumber,
                                                CultureInfo.InvariantCulture, Code) Then
                                Sb.Append(ChrW(Code))
                            End If
                            Pos += 4
                        End If
                    Case Else
                        Sb.Append(Esc)
                End Select
            Else
                Sb.Append(C)
            End If
            Pos += 1
        Loop
        Return Sb.ToString()
    End Function

    ''' <summary>取出整数字段；不存在或解析失败时返回默认值。</summary>
    Public Shared Function JsonInteger(Json As String, Key As String, Optional DefaultValue As Integer = 0) As Integer
        If String.IsNullOrEmpty(Json) OrElse String.IsNullOrEmpty(Key) Then Return DefaultValue
        Dim Pos As Integer = FindValueStart(Json, Key)
        If Pos < 0 Then Return DefaultValue
        Dim Start As Integer = Pos
        If Start < Json.Length AndAlso Json(Start) = "-"c Then Start += 1
        Dim [End] As Integer = Start
        Do While [End] < Json.Length AndAlso Char.IsDigit(Json([End]))
            [End] += 1
        Loop
        If [End] <= Start Then Return DefaultValue
        Dim Value As Integer
        If Integer.TryParse(Json.Substring(Pos, [End] - Pos), NumberStyles.Integer, CultureInfo.InvariantCulture, Value) Then
            Return Value
        End If
        Return DefaultValue
    End Function

#End Region

#Region "接口返回"

    ''' <summary>bind/start 的结果。</summary>
    Public Structure BindSession
        ''' <summary>32 位十六进制轮询凭据，私密，不显示给玩家。</summary>
        Public State As String
        ''' <summary>8 位绑定码，显示给玩家。</summary>
        Public Code As String
        ''' <summary>有效秒数。</summary>
        Public ExpiresIn As Integer
        ''' <summary>服务端给的中文提示，可原样展示。</summary>
        Public Instruction As String
        ''' <summary>解析失败时的原因。</summary>
        Public ErrorText As String

        Public ReadOnly Property Ok As Boolean
            Get
                Return State <> "" AndAlso Code <> ""
            End Get
        End Property
    End Structure

    Public Shared Function ParseBindSession(Json As String) As BindSession
        Dim Result As New BindSession
        Result.State = JsonString(Json, "state").Trim()
        Result.Code = JsonString(Json, "code").Trim().ToUpperInvariant()
        Result.ExpiresIn = JsonInteger(Json, "expiresIn", 300)
        Result.Instruction = JsonString(Json, "instruction").Trim()
        If Result.ExpiresIn <= 0 Then Result.ExpiresIn = 300
        If Result.Code <> "" AndAlso Not IsValidBindCode(Result.Code) Then
            Result.ErrorText = "绑定码格式不正确"
            Result.Code = ""
        End If
        If Result.State = "" AndAlso Result.ErrorText = "" Then Result.ErrorText = "登录服务没有返回绑定会话"
        Return Result
    End Function

    ''' <summary>轮询的四种结果。</summary>
    Public Enum PollStatus
        Pending = 0
        Ok = 1
        Expired = 2
        Failed = 3
    End Enum

    Public Structure PollResult
        Public Status As PollStatus
        Public AccessToken As String
        ''' <summary>36 位带横线的 UUID，与游戏参数要求的 32 位不同，别混用。</summary>
        Public Uuid As String
        Public Name As String
        Public ExpiresIn As Integer
        Public ErrorText As String
    End Structure

    Public Shared Function ParsePoll(Json As String) As PollResult
        Dim Result As New PollResult
        Dim RawStatus As String = JsonString(Json, "status").Trim().ToLowerInvariant()
        Select Case RawStatus
            Case "pending"
                Result.Status = PollStatus.Pending
            Case "ok"
                Result.AccessToken = JsonString(Json, "accessToken").Trim()
                Result.Uuid = JsonString(Json, "uuid").Trim()
                Result.Name = JsonString(Json, "name").Trim()
                Result.ExpiresIn = JsonInteger(Json, "expiresIn", 0)
                If Result.AccessToken = "" OrElse Result.Name = "" OrElse Uuid32(Result.Uuid) = "" Then
                    Result.Status = PollStatus.Failed
                    Result.ErrorText = "登录服务返回的凭据不完整"
                Else
                    Result.Status = PollStatus.Ok
                End If
            Case "expired"
                Result.Status = PollStatus.Expired
            Case "error"
                Result.Status = PollStatus.Failed
                Result.ErrorText = JsonString(Json, "error").Trim()
                If Result.ErrorText = "" Then Result.ErrorText = "登录服务返回了错误"
            Case Else
                Result.Status = PollStatus.Failed
                Result.ErrorText = "无法识别的响应"
        End Select
        Return Result
    End Function

    ''' <summary>
    ''' 把 HTTP 状态码与响应体翻译成给玩家看的一句话。
    ''' 登录流程里玩家只需要知道「失效了」「服务不可用」这两种事。
    ''' </summary>
    Public Shared Function FriendlyError(StatusCode As Integer, Body As String) As String
        Dim Code As String = JsonString(Body, "error").Trim()
        Select Case StatusCode
            Case 400
                Return "启动器参数异常，请更新到最新版本"
            Case 401
                Return "登录服务拒绝了这个请求"
            Case 404
                Return "这个绑定已经失效，请重新登录"
            Case 429
                Return "请求太频繁，请稍后再试"
            Case 500 To 599
                Return ServiceUnavailableText
        End Select
        If Code = "ForbiddenOperationException" Then Return ServiceUnavailableText
        If Code = "TooManyRequests" Then Return "请求太频繁，请稍后再试"
        If Code = "NotFoundException" Then Return "这个绑定已经失效，请重新登录"
        Return "网络不太通畅，请稍后再试"
    End Function


    ''' <summary>服务端强制的皮肤尺寸（像素）。</summary>
    Public Const SkinSize As Integer = 64
    ''' <summary>服务端接受的皮肤体积上限。</summary>
    Public Const SkinMaxBytes As Long = 2 * 1024 * 1024

    ''' <summary>
    ''' 皮肤尺寸是否合格。服务端**不做缩放**，尺寸不对就明确拒绝，
    ''' 所以这里也要挡一次，免得白白上传一张 2 MB 的图再被打回。
    ''' </summary>
    Public Shared Function IsValidSkinSize(Width As Integer, Height As Integer) As Boolean
        Return Width = SkinSize AndAlso Height = SkinSize
    End Function

    ''' <summary>把尺寸渲染成玩家习惯的写法：「64×64」「128×128」。</summary>
    Public Shared Function SkinSizeText(Width As Integer, Height As Integer) As String
        Return Width.ToString(CultureInfo.InvariantCulture) & "×" & Height.ToString(CultureInfo.InvariantCulture)
    End Function

    ''' <summary>
    ''' 把皮肤接口的错误翻译成给玩家看的一句话。
    ''' <para>
    ''' 与登录流程不同：400 在这里是「图片不合格」而不是「启动器参数异常」，
    ''' 而且服务端会在 errorMessage 里写清具体原因（例如实际尺寸），
    ''' 所以优先展示它，不要用统一文案盖掉。
    ''' </para>
    ''' </summary>
    Public Shared Function FriendlySkinError(StatusCode As Integer, Body As String) As String
        Dim ServerText As String = JsonString(Body, "errorMessage").Trim()
        Select Case StatusCode
            Case 400
                Return If(ServerText <> "", ServerText, "这张皮肤不符合要求")
            Case 401
                Return "登录已失效，请重新登录后再试"
            Case 404
                Return "当前服务器没有启用皮肤功能"
            Case 413
                Return "皮肤文件不能超过 2 MB"
            Case 429
                Return "上传太频繁，请稍后再试"
            Case 500 To 599
                Return ServiceUnavailableText
        End Select
        Return If(ServerText <> "", ServerText, "上传失败，请稍后再试")
    End Function

    ''' <summary>
    ''' 皮肤接口的地址。传入 AUTH_BASE（不带末尾斜杠）。
    ''' 单独列出来，省得每条调用各拼一次字符串、拼错了才发现。
    ''' </summary>
    Public Shared Function SkinUploadUrl(AuthBase As String) As String
        Return NormalizeBase(AuthBase) & "/api/v1/skin/upload"
    End Function
    Public Shared Function SkinMeUrl(AuthBase As String) As String
        Return NormalizeBase(AuthBase) & "/api/v1/skin/me"
    End Function

#End Region

End Class
