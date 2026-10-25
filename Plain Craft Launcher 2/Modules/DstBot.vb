Imports System.Text.RegularExpressions
Imports System.Threading

''' <summary>一台服务器的信息。</summary>
Public Class DstServer
    Public Property InstanceId As String = ""
    Public Property Nickname As String = ""
    Public Property Ip As String = ""
    Public Property IsOnline As Boolean
    Public Property Players As New List(Of String)
    Public Property PlayerCount As Integer
    Public Property MaxPlayerCount As Integer
    Public Property Source As String = ""
    '非空表示该实例可以通过 /v2/api/download 拉到整合包
    Public Property DownloadIndex As String = ""
    ''' <summary>是否为用户自己添加的服务器（来自 DstCustomServer，不随机器人列表刷新）。</summary>
    Public Property IsCustom As Boolean = False

    '==== 服务器列表探测状态（面板只探测一部分，其余由客户端用 SLPing 本地补全）====
    ''' <summary>面板是否返回了人数。false = 需要本地探测。</summary>
    Public Property ProbeDone As Boolean = False
    ''' <summary>本地 SLPing 的往返延迟（毫秒）。-1 = 尚未探测或失败。</summary>
    Public Property PingMs As Integer = -1
    ''' <summary>本地探测进行中。</summary>
    Public Property Probing As Boolean = False
    ''' <summary>本地探测失败（连不上 / 超时）。</summary>
    Public Property ProbeFailed As Boolean = False
End Class

''' <summary>一条群公告。</summary>
Public Class DstReport
    Public Property Content As String = ""
    Public Property TimeText As String = ""
End Class

''' <summary>一个 QQ 群的信息。</summary>
Public Class DstGroup
    Public Property GroupId As String = ""
    Public Property GroupName As String = ""
    Public Property GroupInfo As String = ""
    Public Property Reports As New List(Of DstReport)

    ''' <summary>最新一条公告，没有则为 Nothing。</summary>
    Public ReadOnly Property LatestReport As DstReport
        Get
            Return If(Reports.Count > 0, Reports(0), Nothing)
        End Get
    End Property

    Public ReadOnly Property ReportCount As Integer
        Get
            Return Reports.Count
        End Get
    End Property
End Class

''' <summary>
''' 帝释天 QQ 机器人接入层。
''' 预设服务写死在 ModSecret（界面上不展示），用户可在「设置 → 机器人」里保存自定义服务覆盖。
''' 接口：GET /v2/api、GET /v2/api/list?qqgroup=xxx
''' 鉴权：Authorization: Bearer &lt;token&gt;
''' 限流：同一 IP 每分钟 5 次，超出返回 429 + Retry-After
''' </summary>
Friend Module DstBot

#Region "接口常量"

    '/v2/api 根路径在网关上会返回 502，探测用具体接口
    Private Const PathList As String = "/v2/api/list"
    Private Const PathGroupList As String = "/v2/api/list_qqgroups"
    Private Const PathGetInfo As String = "/v2/api/getinfo"
    Private Const PathUserInfo As String = "/v2/api/userinfo"
    Private Const PathUnbind As String = "/v2/api/unbind"
    Private Const PathUserWrite As String = "/v2/api/userwrite"
    Private Const PathUserRead As String = "/v2/api/userread"
    Private Const TimeoutMs As Integer = 12000
    '官方限流：每分钟 5 次。本地留一次余量，按 4 次/分钟排队
    Private Const RateMax As Integer = 5
    Private Const RateWindowMs As Integer = 60000
    '服务器列表缓存，避免主页刷新时狂打接口
    Private Const CacheMs As Integer = 20000
    '群列表请求在途标记，防止多个页面同时请求把限流额度吃光
    Private GroupsFetching As Boolean = False

#End Region

    Public Enum BotStatus
        NotConfigured = 0
        Online = 1
        Offline = 2
        CheckFailed = 3
        RateLimited = 4
    End Enum

#Region "服务器数据"

    Public ServerList As New List(Of DstServer)
    Public UpdatedAt As Date = Date.MinValue
    Private ErrRaw As String = ""

    ''' <summary>已脱敏的错误描述，界面只能读这个。</summary>
    Public ReadOnly Property ErrLast As String
        Get
            Return FriendlyError(ErrRaw)
        End Get
    End Property

#End Region

#Region "脱敏"

    ''' <summary>
    ''' 把文本里的服务地址、令牌抹掉。界面、提示、弹窗一律先过这里。
    ''' </summary>
    Public Function SafeText(Msg As String) As String
        If String.IsNullOrWhiteSpace(Msg) Then Return ""
        Dim S As String = Msg
        '已知的内置地址与令牌。发布版这两项是空串，Replace 的 oldValue 为空会抛异常，
        '所以必须先判空——这个函数是在异常处理路径上被调用的，自己再炸一次会直接崩掉启动器。
        If DstBotPresetApi <> "" Then S = S.Replace(DstBotPresetApi, "帝释天服务")
        If DstBotPresetToken <> "" Then S = S.Replace(DstBotPresetToken, "***")
        '自定义服务也不能露
        Dim CustomUrl As String = Settings.Get(Of String)("DstBotApiUrl").Trim()
        If CustomUrl <> "" Then S = S.Replace(CustomUrl, "自定义服务")
        Dim CustomToken As String = Settings.Get(Of String)("DstBotToken").Trim()
        If CustomToken <> "" Then S = S.Replace(CustomToken, "***")
        '兜底：任何 http(s) 地址、IP:端口、Bearer 令牌都抹掉
        S = Regex.Replace(S, "https?://[^\s，,。）)\]]+", "服务地址")
        S = Regex.Replace(S, "Bearer\s+[A-Za-z0-9_\-\.]+", "Bearer ***")
        S = Regex.Replace(S, "ak_[A-Za-z0-9]+", "***")
        Return S
    End Function

    ''' <summary>
    ''' 把异常信息翻成用户能看的一句话，不暴露地址。
    ''' </summary>
    Public Function FriendlyError(Raw As String) As String
        If String.IsNullOrWhiteSpace(Raw) Then Return ""
        If Raw.Contains("429") OrElse Raw.Contains("过于频繁") Then Return "触发限频，请稍后再试"
        If Raw.Contains("404") Then Return "接口尚未部署"
        If Raw.Contains("401") OrElse Raw.Contains("403") Then Return "鉴权失败"
        If Raw.Contains("Timeout") OrElse Raw.Contains("超时") Then Return "连接超时"
        If Raw.Contains("Name or service not known") OrElse Raw.Contains("未能解析") Then Return "无法解析服务地址"
        Dim Clean As String = SafeText(Raw)
        If Clean.Length > 60 Then Clean = Clean.Substring(0, 60) & "…"
        Return Clean
    End Function

#End Region

#Region "主页展示配置"

    ''' <summary>主页三张卡片的键与默认顺序。</summary>
    Public ReadOnly Property HomeKeys As String() = {"servers", "report", "bot"}

    Public Function HomeKeyName(Key As String) As String
        Select Case Key
            Case "servers" : Return "服务器状态"
            Case "report" : Return "群公告"
            Case "bot" : Return "帝释天机器人"
            Case Else : Return Key
        End Select
    End Function

    ''' <summary>当前顺序，缺失的键自动补在末尾。</summary>
    Public Function HomeOrder() As List(Of String)
        Dim Raw As String = Settings.Get(Of String)("DstHomeOrder")
        Dim Result As New List(Of String)
        For Each Part In Raw.Split(","c)
            Dim K As String = Part.Trim().ToLower()
            If HomeKeys.Contains(K) AndAlso Not Result.Contains(K) Then Result.Add(K)
        Next
        For Each K In HomeKeys
            If Not Result.Contains(K) Then Result.Add(K)
        Next
        Return Result
    End Function

    Public Sub SetHomeOrder(Order As List(Of String))
        Settings.Set("DstHomeOrder", String.Join(",", Order))
    End Sub

    Public Function HomeShow(Key As String) As Boolean
        Select Case Key
            Case "servers" : Return Settings.Get(Of Boolean)("DstHomeShowServers")
            Case "report" : Return Settings.Get(Of Boolean)("DstHomeShowReport")
            Case "bot" : Return Settings.Get(Of Boolean)("DstHomeShowBot")
            Case Else : Return True
        End Select
    End Function

    Public Sub SetHomeShow(Key As String, Value As Boolean)
        Select Case Key
            Case "servers" : Settings.Set("DstHomeShowServers", Value)
            Case "report" : Settings.Set("DstHomeShowReport", Value)
            Case "bot" : Settings.Set("DstHomeShowBot", Value)
        End Select
    End Sub

#End Region

#Region "公告"

    ''' <summary>把 group_report 解析成公告数组：兼容 字符串 / 字符串数组 / 对象数组。</summary>
    Private Sub ParseReports(Token As JToken, Target As DstGroup)
        Target.Reports.Clear()
        If Token Is Nothing Then Return
        Select Case Token.Type
            Case JTokenType.String
                Dim Text As String = WebUtility.HtmlDecode(Token.ToString()).Trim()
                If Text <> "" Then Target.Reports.Add(New DstReport With {.Content = Text})
            Case JTokenType.Array
                For Each Sub_ As JToken In Token
                    If Sub_.Type = JTokenType.String Then
                        Dim Text As String = WebUtility.HtmlDecode(Sub_.ToString()).Trim()
                        If Text <> "" Then Target.Reports.Add(New DstReport With {.Content = Text})
                    ElseIf Sub_.Type = JTokenType.Object Then
                        Dim Content As String = FirstText(Sub_, "content", "text", "report", "message")
                        If Content = "" Then Continue For
                        Target.Reports.Add(New DstReport With {
                            .Content = WebUtility.HtmlDecode(Content).Trim(),
                            .TimeText = FirstText(Sub_, "time", "date", "publish_time", "created_at", "timestamp")
                        })
                    End If
                Next
            Case Else
                Dim Text As String = WebUtility.HtmlDecode(Token.ToString()).Trim()
                If Text <> "" Then Target.Reports.Add(New DstReport With {.Content = Text})
        End Select
    End Sub

    Private Function FirstText(Token As JToken, ParamArray Keys As String()) As String
        For Each Key In Keys
            Dim V As JToken = Token(Key)
            If V IsNot Nothing AndAlso V.Type <> JTokenType.Null Then
                Dim S As String = V.ToString().Trim()
                If S <> "" Then Return S
            End If
        Next
        Return ""
    End Function

    ''' <summary>当前选中群的最新公告。</summary>
    Public ReadOnly Property LatestReport As DstReport
        Get
            Dim G As DstGroup = SelectedGroup
            Return If(G Is Nothing, Nothing, G.LatestReport)
        End Get
    End Property

    ''' <summary>记住每个群看过的公告特征，用于“有新公告”提示。</summary>
    Private Function SeenMap() As JObject
        Try
            Dim Raw As String = Settings.Get(Of String)("DstSeenReports")
            If String.IsNullOrWhiteSpace(Raw) Then Return New JObject
            Return JObject.Parse(Raw)
        Catch ex As Exception
            Return New JObject
        End Try
    End Function

    Private Sub SaveSeenMap(Map As JObject)
        Try
            Settings.Set("DstSeenReports", Map.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            Logger.Info(ex, "保存公告已读标记失败")
        End Try
    End Sub

    ''' <summary>对比上次记录，发现新公告就弹窗。</summary>
    Private Sub CheckNewReport(All As List(Of DstGroup))
        Dim G As DstGroup = Nothing
        For Each Item In All
            If Item.GroupId = GroupNo Then G = Item : Exit For
        Next
        If G Is Nothing OrElse G.LatestReport Is Nothing Then Return
        Dim Sig As String = G.LatestReport.Content.GetStableHashCode().ToString()
        Dim Map As JObject = SeenMap()
        Dim Old As String = If(Map(G.GroupId), "").ToString()
        Map(G.GroupId) = Sig
        SaveSeenMap(Map)
        '第一次看到这个群时只记录，不打扰
        If Old = "" OrElse Old = Sig Then Return
        Dim Content As String = G.LatestReport.Content
        If Content.Length > 400 Then Content = Content.Substring(0, 400) & "……"
        RunInUi(Sub() MyMsgBox(Content, $"群公告更新 · {G.GroupName}", "知道了", "查看全部",
                               Button2Action:=Sub() FrmMain.PageChange(FormMain.PageType.Group, FormMain.PageSubType.GroupReport)))
    End Sub

#End Region

#Region "群消息（/v2/api/getinfo）"

    ''' <summary>一条群消息。</summary>
    Public Class DstMessage
        Public Property UserName As String = ""
        Public Property UserQq As String = ""
        Public Property GroupName As String = ""
        Public Property TimeText As String = ""
        Public Property Content As String = ""
        ''' <summary>去重用的稳定标识。</summary>
        Public ReadOnly Property Key As String
            Get
                Return UserQq & "|" & TimeText & "|" & Content.GetStableHashCode()
            End Get
        End Property
    End Class

    ''' <summary>CQ 码解析出的一段内容。</summary>
    Public Class CqSegment
        Public Property Kind As String = "text"   ' text / image / face / at / video / file / record / other
        Public Property Text As String = ""
        Public Property Url As String = ""
        Public Property FileName As String = ""
        Public Property FileSize As Long = 0      ' CQ 里带的文件大小，下载后用来校验完整性
    End Class

    '聊天接口单独限流：官方 20 次/分钟，本地按 16 次排队
    Private ReadOnly ChatLock As New Object
    Private ChatHits As New List(Of Long)
    Private Const ChatRateMax As Integer = 16
    Private Const ChatWindowMs As Integer = 60000

    ''' <summary>等待限流时间片的累计上限。超过则放弃本次请求，避免工作线程被无限期占用。</summary>
    Private Const ChatRateMaxWaitMs As Integer = 15000

    ''' <summary>
    ''' 申请一个消息发送配额；拿到返回 True，超时返回 False。
    ''' 注意：绝对不能在持有锁的情况下 Sleep，否则 UI 线程读 ChatQuotaLeft 会被一起堵住。
    ''' </summary>
    Private Function ChatRateWait() As Boolean
        Dim WaitedMs As Integer = 0
        Do
            Dim WaitMs As Integer = 0
            SyncLock ChatLock
                Dim Now As Long = GetTimeMs()
                ChatHits.RemoveAll(Function(T) Now - T > ChatWindowMs)
                If ChatHits.Count < ChatRateMax Then
                    ChatHits.Add(Now)
                    Return True
                End If
                WaitMs = CInt(ChatWindowMs - (Now - ChatHits(0))) + 120
            End SyncLock
            If WaitMs <= 0 Then Continue Do
            '累计等待超过上限就放弃，不再无限循环
            If WaitedMs + WaitMs > ChatRateMaxWaitMs Then Return False
            Thread.Sleep(WaitMs)
            WaitedMs += WaitMs
        Loop
    End Function

    Public ReadOnly Property ChatQuotaLeft As Integer
        Get
            SyncLock ChatLock
                Dim Now As Long = GetTimeMs()
                ChatHits.RemoveAll(Function(T) Now - T > ChatWindowMs)
                Return Math.Max(0, ChatRateMax - ChatHits.Count)
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' 拉取最近 30 条群消息。OnDone(消息列表, 错误信息)，在 UI 线程回调。
    ''' </summary>
    Public Sub FetchMessages(GroupId As String, OnDone As Action(Of List(Of DstMessage), String))
        If Not IsQqBound Then
            RunInUi(Sub() OnDone(Nothing, "还没有绑定 QQ，绑定后才能查看群消息"))
            Return
        End If
        If String.IsNullOrWhiteSpace(GroupId) Then
            RunInUi(Sub() OnDone(Nothing, "没有选择群"))
            Return
        End If
        RunInThread(
        Sub()
            Dim List_ As List(Of DstMessage) = Nothing
            Dim Err As String = ""
            Try
                If Not ChatRateWait() Then
                    Err = "发送太频繁，请稍后再试"
                    Exit Try
                End If
                Dim Body As New JObject From {
                    {"qqgroup", GroupId},
                    {"user_key", UserKey}
                }
                Dim Hdr(0, 1) As String
                Hdr(0, 0) = "Authorization"
                Hdr(0, 1) = "Bearer " & ApiToken
                Dim Json As String = NetRequestByClient(ApiBase & PathGetInfo, HttpMethod.Post,
                                                        Body.ToString(), "application/json; charset=utf-8",
                                                        Timeout:=TimeoutMs, Headers:=Hdr, MakeLog:=False)
                RespLast = Json
                Dim Parsed As New List(Of DstMessage)
                Dim Arr As JArray = JArray.Parse(Json)
                For Each Item As JToken In Arr
                    Parsed.Add(New DstMessage With {
                        .UserName = WebUtility.HtmlDecode(FirstText(Item, "user_name")).Trim(),
                        .UserQq = FirstText(Item, "user_qq").Trim(),
                        .GroupName = SafeRemoteText(WebUtility.HtmlDecode(FirstText(Item, "user_group_name")), 64),
                        .TimeText = FirstText(Item, "time").Trim(),
                        .Content = WebUtility.HtmlDecode(FirstText(Item, "content", "message", "text"))
                    })
                Next
                List_ = Parsed
            Catch ex As Exception
                Err = FriendlyError(ex.Message)
                Logger.Warn(ex, "拉取群消息失败")
            End Try
            RunInUi(Sub() OnDone(List_, Err))
        End Sub)
    End Sub

    ''' <summary>把 CQ 码内容拆成文本/图片/视频/文件等片段。</summary>
    Public Function ParseCq(Content As String) As List(Of CqSegment)
        Dim Result As New List(Of CqSegment)
        If Content = "" Then Return Result
        Dim Pattern As New Regex("\[CQ:([A-Za-z_]+)((?:,[^\]]*)?)\]")
        Dim Last As Integer = 0
        For Each M As Match In Pattern.Matches(Content)
            If M.Index > Last Then
                Result.Add(New CqSegment With {.Kind = "text", .Text = UnescapeCq(Content.Substring(Last, M.Index - Last))})
            End If
            Dim Type As String = M.Groups(1).Value.ToLower()
            Dim Params As Dictionary(Of String, String) = ParseCqParams(M.Groups(2).Value)
            Dim Seg As New CqSegment
            Select Case Type
                Case "image", "mface"
                    Seg.Kind = "image"
                    Seg.Url = If(Params.ContainsKey("url"), Params("url"), "")
                    Seg.FileName = If(Params.ContainsKey("file"), Params("file"), "")
                Case "cardimage"
                    'QQ 卡片图片：file 通常就是直链
                    Dim CardUrl As String = If(Params.ContainsKey("file"), Params("file"), "")
                    If CardUrl.StartsWithF("http") Then
                        Seg.Kind = "image"
                        Seg.Url = CardUrl
                    Else
                        Seg.Kind = "other"
                        Seg.Text = "[卡片]"
                    End If
                Case "json"
                    Seg.Kind = "json"
                    Seg.Text = If(Params.ContainsKey("data"), Params("data"), "")
                Case "xml"
                    Seg.Kind = "xml"
                    Seg.Text = If(Params.ContainsKey("data"), Params("data"), "")
                Case "video"
                    Seg.Kind = "video"
                    Seg.Url = If(Params.ContainsKey("url"), Params("url"), "")
                    Seg.FileName = If(Params.ContainsKey("file"), Params("file"), "")
                Case "file"
                    Seg.Kind = "file"
                    Seg.FileName = If(Params.ContainsKey("name"), Params("name"), If(Params.ContainsKey("file"), Params("file"), "文件"))
                    Seg.Url = If(Params.ContainsKey("url"), Params("url"), "")
                    Dim SizeText As String = If(Params.ContainsKey("file_size"), Params("file_size"), If(Params.ContainsKey("size"), Params("size"), "0"))
                    Dim SizeValue As Long
                    If Long.TryParse(SizeText, SizeValue) Then Seg.FileSize = SizeValue
                Case "face"
                    Seg.Kind = "face"
                Case "at"
                    Seg.Kind = "at"
                    Seg.Text = "@" & If(Params.ContainsKey("name"), Params("name"), If(Params.ContainsKey("qq"), Params("qq"), "某人"))
                Case "record"
                    Seg.Kind = "record"
                Case Else
                    Seg.Kind = "other"
                    Seg.Text = $"[{Type}]"
            End Select
            Result.Add(Seg)
            Last = M.Index + M.Length
        Next
        If Last < Content.Length Then
            Result.Add(New CqSegment With {.Kind = "text", .Text = UnescapeCq(Content.Substring(Last))})
        End If
        Return Result
    End Function

    ''' <summary>
    ''' 解析 CQ 参数段。data= 这类值里可能带逗号，所以带等号的才是新键，其余拼回上一个值。
    ''' </summary>
    Private Function ParseCqParams(Raw As String) As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)
        If String.IsNullOrEmpty(Raw) Then Return Result
        Dim Source As String = Raw.TrimStart(","c)
        Dim Segments As New List(Of String)
        Dim Builder As New Text.StringBuilder
        For Each Ch As Char In Source
            If Ch = ","c Then
                Segments.Add(Builder.ToString())
                Builder.Clear()
            Else
                Builder.Append(Ch)
            End If
        Next
        Segments.Add(Builder.ToString())

        Dim LastKey As String = Nothing
        For Each Part In Segments
            Dim Eq As Integer = Part.IndexOf("="c)
            If Eq > 0 Then
                Dim Key As String = Part.Substring(0, Eq).Trim()
                Result(Key) = UnescapeCq(Part.Substring(Eq + 1))
                LastKey = Key
            ElseIf LastKey IsNot Nothing Then
                '没有等号，说明是上一个值的续行
                Result(LastKey) = Result(LastKey) & "," & UnescapeCq(Part)
            End If
        Next
        Return Result
    End Function

    Private Function UnescapeCq(Text As String) As String
        If String.IsNullOrEmpty(Text) Then Return ""
        Return Text.Replace("&#91;", "[").Replace("&#93;", "]").Replace("&#44;", ",").Replace("&amp;", "&")
    End Function

    ''' <summary>QQ 头像地址。</summary>
    Public Function AvatarUrl(Qq As String) As String
        If Not Regex.IsMatch(If(Qq, ""), "^\d+$") Then Return ""
        Return $"https://q1.qlogo.cn/g?b=qq&nk={Qq}&s=100"
    End Function

    ''' <summary>QQ 头像的本地缓存目录（压缩后的小图）。</summary>
    Public ReadOnly Property AvatarCacheFolder As String
        Get
            Return PathTemp & "DstAvatar\"
        End Get
    End Property

    ''' <summary>某个 QQ 号在本地的头像文件路径。</summary>
    Public Function AvatarCacheFile(Qq As String) As String
        If Qq = "" Then Return ""
        Return AvatarCacheFolder & Qq & ".jpg"
    End Function

    ''' <summary>头像缓存占用（字节）。</summary>
    Public Function AvatarCacheSize() As Long
        Try
            If Not DirectoryUtils.Exists(AvatarCacheFolder) Then Return 0
            Dim Total As Long = 0
            For Each F In DirectoryUtils.EnumerateFiles(AvatarCacheFolder, False)
                Try
                    Total += New IO.FileInfo(F).Length
                Catch ex As Exception
                    Logger.Warn(ex, $"统计聊天缓存大小时跳过文件：{F}")
                End Try
            Next
            Return Total
        Catch ex As Exception
            Return 0
        End Try
    End Function

    Public Sub ClearAvatarCache()
        Try
            If DirectoryUtils.Exists(AvatarCacheFolder) Then DirectoryUtils.Delete(AvatarCacheFolder)
            Hint("头像缓存已清空", HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "清空头像缓存失败")
        End Try
    End Sub

    ''' <summary>
    ''' 下载并压缩头像到本地缓存（96px JPEG，通常 3~8 KB）。
    ''' 已存在则直接返回。必须在工作线程调用。
    ''' </summary>
    Public Function EnsureAvatarFile(Qq As String) As String
        Dim Target As String = AvatarCacheFile(Qq)
        If Target = "" Then Return ""
        If FileUtils.Exists(Target) AndAlso New IO.FileInfo(Target).Length > 0 Then Return Target
        Dim Url As String = AvatarUrl(Qq)
        If Url = "" Then Return ""
        Dim Raw As String = PathTemp & "DstAvatar\tmp_" & Qq & ".img"
        Try
            DirectoryUtils.Create(AvatarCacheFolder)
            NetDownloadByLoader(Url, Raw)
            If Not FileUtils.Exists(Raw) Then Return ""
            '解码到 96px 再压成 JPEG，兼顾高分屏与体积
            Dim Source As New BitmapImage()
            Source.BeginInit()
            Source.UriSource = New Uri(Raw, UriKind.Absolute)
            Source.CacheOption = BitmapCacheOption.OnLoad
            Source.DecodePixelWidth = 96
            Source.EndInit()
            Source.Freeze()
            Dim Encoder As New JpegBitmapEncoder With {.QualityLevel = 80}
            Encoder.Frames.Add(BitmapFrame.Create(Source))
            Using Stream As New IO.FileStream(Target, IO.FileMode.Create, IO.FileAccess.Write)
                Encoder.Save(Stream)
            End Using
            Return Target
        Catch ex As Exception
            Logger.Warn(ex, $"缓存头像失败：{Qq}")
            Return ""
        Finally
            Try
                If FileUtils.Exists(Raw) Then FileUtils.Delete(Raw)
            Catch ex As Exception
                Logger.Warn(ex, $"删除原始聊天记录失败：{Raw}")
            End Try
        End Try
    End Function
    ''' <summary>
    ''' 本地视角下的“我的 QQ 号”。绑定后以服务器返回的为准，未拉到资料时退回本地记录的号。
    ''' </summary>
    Public ReadOnly Property SelfQq As String
        Get
            If UserInfoQq.Trim() <> "" Then Return UserInfoQq.Trim()
            Return QqNumber.Trim()
        End Get
    End Property

    ''' <summary>判断某条消息是不是自己发的。容忍前后空格与两种来源的差异。</summary>
    Public Function IsSelfQq(Qq As String) As Boolean
        Dim Target As String = If(Qq, "").Trim()
        If Target = "" Then Return False
        Dim A As String = UserInfoQq.Trim()
        If A <> "" AndAlso String.Equals(Target, A, StringComparison.Ordinal) Then Return True
        Dim B As String = QqNumber.Trim()
        If B <> "" AndAlso String.Equals(Target, B, StringComparison.Ordinal) Then Return True
        Return False
    End Function
    ''' <summary>按扩展名判断是不是能播的视频文件。</summary>
    Public Function IsVideoFile(FileName As String) As Boolean
        If String.IsNullOrWhiteSpace(FileName) Then Return False
        Dim Ext As String = PathUtils.GetExtension(FileName).ToLower()
        Return {"mp4", "m4v", "mov", "wmv", "avi", "mkv", "webm", "flv"}.Contains(Ext)
    End Function

    ''' <summary>按扩展名判断是不是图片文件。</summary>
    Public Function IsImageFile(FileName As String) As Boolean
        If String.IsNullOrWhiteSpace(FileName) Then Return False
        Dim Ext As String = PathUtils.GetExtension(FileName).ToLower()
        Return {"jpg", "jpeg", "png", "gif", "bmp", "webp"}.Contains(Ext)
    End Function


#End Region

#Region "用户信息（/v2/api/userinfo）"

    Public UserInfoQq As String = ""
    Public UserInfoName As String = ""
    Public UserInfoAvatar As String = ""

    ''' <summary>拉取已绑定用户的详细信息。OnDone 在 UI 线程回调。</summary>
    Public Sub FetchUserInfo(Optional OnDone As Action = Nothing)
        If Not IsQqBound Then
            UserInfoQq = ""
            UserInfoName = ""
            UserInfoAvatar = ""
            If OnDone IsNot Nothing Then RunInUi(OnDone)
            Return
        End If
        RunInThread(
        Sub()
            Try
                If Not ChatRateWait() Then Exit Try
                Dim Body As New JObject From {{"user_key", UserKey}}
                Dim Hdr(0, 1) As String
                Hdr(0, 0) = "Authorization"
                Hdr(0, 1) = "Bearer " & ApiToken
                Dim Json As String = NetRequestByClient(ApiBase & PathUserInfo, HttpMethod.Post,
                                                        Body.ToString(), "application/json; charset=utf-8",
                                                        Timeout:=TimeoutMs, Headers:=Hdr, MakeLog:=False)
                RespLast = Json
                Dim Obj As JObject = JObject.Parse(Json)
                UserInfoQq = FirstText(Obj, "qq").Trim()
                UserInfoName = WebUtility.HtmlDecode(FirstText(Obj, "nickname")).Trim()
                UserInfoAvatar = FirstText(Obj, "head_image").Trim()
                Logger.Info("用户信息已更新（QQ 号与昵称不写日志）")
            Catch ex As Exception
                Logger.Warn(ex, "拉取绑定用户信息失败")
                If UserInfoQq = "" Then UserInfoQq = QqNumber
                If UserInfoName = "" Then UserInfoName = "QQ " & QqNumber
                If UserInfoAvatar = "" Then UserInfoAvatar = AvatarUrl(QqNumber)
            End Try
            RunInUi(Sub() If OnDone IsNot Nothing Then OnDone())
        End Sub)
    End Sub

#End Region

#Region "聊天记录本地缓存"

    ''' <summary>聊天记录的本地存放目录。</summary>
    Public ReadOnly Property ChatCacheFolder As String
        Get
            Return Paths.Base & "PCL\DstChat\"
        End Get
    End Property

    ''' <summary>
    ''' 把远程返回的文本净化成可持久化、可展示的形式。
    ''' <para>
    ''' 去掉所有控制字符（含换行、制表符与 NUL）并截断长度：
    ''' 这些值会被写进 ini / 注册表，也可能直接显示在界面上，
    ''' 留换行会让它有机会伪造出额外的配置行。
    ''' </para>
    ''' </summary>
    Friend Function SafeRemoteText(Value As String, MaxLength As Integer) As String
        If String.IsNullOrEmpty(Value) Then Return ""
        Dim Clean As New StringBuilder
        For Each C As Char In Value
            If Not Char.IsControl(C) Then Clean.Append(C)
            If Clean.Length >= MaxLength Then Exit For
        Next
        Return Clean.ToString().Trim()
    End Function

    ''' <summary>
    ''' 把群号收敛成只含数字的安全文件名片段。
    ''' <para>
    ''' 群号本来就只有数字，但它是从远程接口拿回来的字符串，
    ''' 直接拼进路径等于把目录穿越的口子交给对方（..\..\ 之类）。
    ''' 非数字一律丢弃，长度也做上限，拿不到合法值就退回 unknown。
    ''' </para>
    ''' </summary>
    Private Function SafeGroupId(GroupId As String) As String
        If String.IsNullOrWhiteSpace(GroupId) Then Return "unknown"
        Dim Clean As New StringBuilder
        For Each C As Char In GroupId
            If Char.IsDigit(C) Then Clean.Append(C)
            If Clean.Length >= 20 Then Exit For
        Next
        Return If(Clean.Length = 0, "unknown", Clean.ToString())
    End Function

    Private Function ChatCacheFile(GroupId As String) As String
        Return ChatCacheFolder & SafeGroupId(GroupId) & ".json"
    End Function

    ''' <summary>读取某个群的本地聊天记录。</summary>
    Public Function LoadChatCache(GroupId As String) As List(Of DstMessage)
        Dim Result As New List(Of DstMessage)
        Dim FilePath As String = ChatCacheFile(GroupId)
        Try
            If Not FileUtils.Exists(FilePath) Then Return Result
            Dim Arr As JArray = JArray.Parse(FileUtils.ReadAsString(FilePath))
            For Each Item As JToken In Arr
                Result.Add(New DstMessage With {
                    .UserName = FirstText(Item, "n"),
                    .UserQq = FirstText(Item, "q"),
                    .GroupName = FirstText(Item, "g"),
                    .TimeText = FirstText(Item, "t"),
                    .Content = FirstText(Item, "c")
                })
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取聊天记录缓存失败")
        End Try
        Return Result
    End Function

    ''' <summary>写入某个群的本地聊天记录（最多保留 300 条）。</summary>
    Public Sub SaveChatCache(GroupId As String, Items As List(Of DstMessage))
        Try
            DirectoryUtils.Create(ChatCacheFolder)
            Dim Arr As New JArray
            Dim Start As Integer = Math.Max(0, Items.Count - 300)
            For i = Start To Items.Count - 1
                Dim M As DstMessage = Items(i)
                Arr.Add(New JObject From {
                    {"n", M.UserName}, {"q", M.UserQq}, {"g", M.GroupName},
                    {"t", M.TimeText}, {"c", M.Content}
                })
            Next
            FileUtils.WriteAtomic(ChatCacheFile(GroupId), Arr.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            Logger.Warn(ex, "写入聊天记录缓存失败")
        End Try
    End Sub

    ''' <summary>本地数据占用（字节）。</summary>
    Public Function ChatCacheSize() As Long
        Try
            If Not DirectoryUtils.Exists(ChatCacheFolder) Then Return 0
            Dim Total As Long = 0
            For Each F In DirectoryUtils.EnumerateFiles(ChatCacheFolder, True)
                Try
                    Total += New IO.FileInfo(F).Length
                Catch ex As Exception
                    Logger.Warn(ex, $"统计缓存大小时跳过文件：{F}")
                End Try
            Next
            Return Total
        Catch ex As Exception
            Return 0
        End Try
    End Function

    ''' <summary>本地缓存的群数量。</summary>
    Public Function ChatCacheGroupCount() As Integer
        Try
            If Not DirectoryUtils.Exists(ChatCacheFolder) Then Return 0
            Return DirectoryUtils.EnumerateFiles(ChatCacheFolder, False).Count()
        Catch ex As Exception
            Return 0
        End Try
    End Function

    ''' <summary>清空全部聊天记录缓存。</summary>
    Public Sub ClearChatCache()
        Try
            If DirectoryUtils.Exists(ChatCacheFolder) Then DirectoryUtils.Delete(ChatCacheFolder)
            Hint("聊天记录缓存已清空", HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "清空聊天记录缓存失败")
            Hint("清空失败：" & FriendlyError(ex.Message), HintType.Red)
        End Try
    End Sub

#End Region

#Region "服务配置"

    ''' <summary>是否使用用户保存的自定义服务。</summary>
    Public ReadOnly Property UseCustom As Boolean
        Get
            Return Settings.Get(Of Boolean)("DstBotUseCustom")
        End Get
    End Property

    Public ReadOnly Property ApiBase As String
        Get
            If UseCustom Then
                Dim Raw As String = Settings.Get(Of String)("DstBotApiUrl").Trim()
                If Raw <> "" Then Return Raw.TrimEnd("/"c)
            End If
            '配置文件里的服务地址优先于编译期内置：
            '发布版把初始地址释放到 config.ini，更新启动器后它还在
            Dim FromFile As String = ModConfigFile.Read(ModConfigFile.SectionCloud, "ApiBase").Trim()
            If FromFile <> "" Then Return FromFile.TrimEnd("/"c)
            Return DstBotPresetApi
        End Get
    End Property

    Public ReadOnly Property GroupNo As String
        Get
            '用户挑过的群优先；没挑过就看配置文件里的预设群号。
            '预设版把群号一起释放到 config.ini，换机器或更新后都能恢复。
            Dim FromSetting As String = Settings.Get(Of String)("DstGroupId").Trim()
            If FromSetting <> "" Then Return FromSetting
            Return ModConfigFile.Read(ModConfigFile.SectionCloud, "GroupId").Trim()
        End Get
    End Property

    Public ReadOnly Property ApiToken As String
        Get
            If UseCustom Then
                Dim Raw As String = Settings.Get(Of String)("DstBotToken").Trim()
                If Raw <> "" Then Return Raw
            End If
            Dim FromFile As String = ModConfigFile.Read(ModConfigFile.SectionCloud, "ApiKey").Trim()
            If FromFile <> "" Then Return FromFile
            Return DstBotPresetToken
        End Get
    End Property

    ''' <summary>
    ''' 切换预设后立刻生效：把地址写进配置，清掉内存里的探测状态。
    ''' 传空串表示切到「自定义」，此时不动配置文件里的地址，等用户自己填。
    ''' </summary>
    Public Sub ApplyPreset(ApiUrl As String, Optional ApiKey As String = "")
        Try
            If ApiUrl <> "" Then
                ModConfigFile.Write(ModConfigFile.SectionCloud, "ApiBase", ApiUrl.TrimEnd("/"c))
            End If
            '令牌一并落盘：预设里带了就用预设的，没带就保留原值不动
            If ApiKey <> "" Then
                ModConfigFile.Write(ModConfigFile.SectionCloud, "ApiKey", ApiKey)
            End If
            '地址变了，之前探测出来的连接状态、群列表、服务器列表都不再作数
            AutoOfflineFlag = False
            SyncLock Groups
                Groups.Clear()
            End SyncLock
            GroupsUpdatedAt = Date.MinValue
            SyncLock ServerList
                ServerList.Clear()
            End SyncLock
            UpdatedAt = Date.MinValue
            '没有任何服务地址时状态要归位，否则界面会一直显示上一次的连接结果
            If ApiBase = "" Then
                StatusLast = BotStatus.NotConfigured
                ErrRaw = ""
            End If
            Logger.Info($"已切换服务预设，当前地址：{If(ApiBase = "", "(无)", ApiBase)}")
        Catch ex As Exception
            Logger.Warn(ex, "切换服务预设失败")
        End Try
    End Sub

    ''' <summary>界面展示用的服务名，预设服务不暴露具体信息。</summary>
    Public ReadOnly Property ServiceName As String
        Get
            Return If(UseCustom, "自定义服务", "帝释天预设服务")
        End Get
    End Property

    ''' <summary>群号打码显示。</summary>
    Public ReadOnly Property GroupMasked As String
        Get
            Dim G As String = GroupNo
            If G = "" Then Return "未选择"
            If G.Length <= 5 Then Return G
            Return G.Substring(0, 3) & New String("*"c, G.Length - 5) & G.Substring(G.Length - 2)
        End Get
    End Property

#End Region

#Region "群列表（/v2/api/list_qqgroups）"

    Public Groups As New List(Of DstGroup)
    Public GroupsUpdatedAt As Date = Date.MinValue

    Public ReadOnly Property SelectedGroup As DstGroup
        Get
            Dim Id As String = GroupNo
            If Id = "" Then Return Nothing
            SyncLock Groups
                Return Groups.FirstOrDefault(Function(G) G.GroupId = Id)
            End SyncLock
        End Get
    End Property

    ''' <summary>群名 + 打码群号，用于界面显示。</summary>
    Public ReadOnly Property GroupDisplay As String
        Get
            Dim S As DstGroup = SelectedGroup
            If S IsNot Nothing AndAlso S.GroupName <> "" Then Return S.GroupName & "（" & S.GroupId & "）"
            If GroupNo = "" Then Return "未选择群"
            Return GroupNo
        End Get
    End Property

    ''' <summary>拉取机器人所在的 QQ 群列表（带缓存）。</summary>
    Public Sub FetchGroups(Optional Force As Boolean = False, Optional OnDone As Action = Nothing)
        '已经在拉了就别重复发请求（启动时两个页面都会调）
        If GroupsFetching Then
            If OnDone IsNot Nothing Then RunInUi(OnDone)
            Return
        End If
        GroupsFetching = True
        If Not Force AndAlso Groups.Count > 0 AndAlso (Date.Now - GroupsUpdatedAt).TotalMilliseconds < CacheMs Then
            GroupsFetching = False
            If OnDone IsNot Nothing Then RunInUi(OnDone)
            Return
        End If
        RunInThread(
        Sub()
            Try
                Dim Json As String = Request(ApiBase & PathGroupList)
                RespLast = Json
                Dim Parsed As New List(Of DstGroup)
                Dim Arr As JArray = JArray.Parse(Json)
                For Each Item As JToken In Arr
                    Dim G As New DstGroup With {
                        .GroupId = If(Item("group_id"), "").ToString().Trim(),
                        .GroupName = WebUtility.HtmlDecode(If(Item("group_name"), "").ToString()).Trim(),
                        .GroupInfo = WebUtility.HtmlDecode(If(Item("group_info"), "").ToString()).Trim()
                    }
                    If G.GroupId = "" Then Continue For
                    If G.GroupName = "" Then G.GroupName = "QQ 群 " & G.GroupId
                    ParseReports(Item("group_report"), G)
                    Parsed.Add(G)
                Next
                SyncLock Groups
                    Groups = Parsed
                End SyncLock
                GroupsUpdatedAt = Date.Now
                ErrRaw = ""
                StatusLast = BotStatus.Online
                '没选过群时自动选第一个，避免主页空着
                If GroupNo = "" AndAlso Parsed.Count > 0 Then
                    Settings.Set("DstGroupId", SafeGroupId(Parsed(0).GroupId))
                    Settings.Set("DstGroupName", SafeRemoteText(Parsed(0).GroupName, 64))
                End If
                RememberGroupName(Parsed) '本机缓存群名，下次启动不必等群列表
                CheckNewReport(Parsed)
            Catch ex As Exception
                ErrRaw = ex.Message
                StatusLast = If(ex.Message.Contains("429"), BotStatus.RateLimited, BotStatus.CheckFailed)
                Logger.Warn(ex, "拉取 QQ 群列表失败")
            End Try
            GroupsFetching = False
            RunInUi(Sub() If OnDone IsNot Nothing Then OnDone())
        End Sub)
    End Sub

#End Region

#Region "状态"

    Public StatusLast As BotStatus = BotStatus.NotConfigured
    Public MsgLast As String = ""
    Public RespLast As String = ""
    Public QqMsgLast As String = ""

    ''' <summary>最近一次健康检查的往返耗时（毫秒）。-1 表示还没测过。</summary>
    Public Property LastLatencyMs As Integer = -1

    ''' <summary>给界面看的延迟文本。</summary>
    Public ReadOnly Property LatencyText As String
        Get
            If LastLatencyMs < 0 Then Return "—"
            Return LastLatencyMs & " ms"
        End Get
    End Property
    Public ReadOnly Property StatusText As String
        Get
            Select Case StatusLast
                Case BotStatus.Online
                    Return "已连接"
                Case BotStatus.Offline
                    Return "离线"
                Case BotStatus.CheckFailed
                    Return "连接失败"
                Case BotStatus.RateLimited
                    Return "触发限频"
                Case Else
                    Return "未配置"
            End Select
        End Get
    End Property

#End Region

#Region "限频"

    Private ReadOnly RateLock As New Object
    Private RateHits As New List(Of Long)

    ''' <summary>
    ''' 本地排队：保证 60 秒内最多发出 RateMax 次请求。超限时阻塞等待。
    ''' </summary>
    Private Sub RateWait()
        '同样不能在锁内 Sleep：UI 线程要读 RateRemain
        Do
            Dim WaitMs As Integer = 0
            SyncLock RateLock
                Dim Now As Long = GetTimeMs()
                RateHits.RemoveAll(Function(T) Now - T > RateWindowMs)
                If RateHits.Count < RateMax Then
                    RateHits.Add(Now)
                    Return
                End If
                WaitMs = CInt(RateWindowMs - (Now - RateHits(0))) + 120
            End SyncLock
            If WaitMs <= 0 Then Continue Do
            If WaitMs > 15000 Then WaitMs = 15000
            Thread.Sleep(WaitMs)
        Loop
    End Sub

    Private Function RateRemain() As Integer
        SyncLock RateLock
            Dim Now As Long = GetTimeMs()
            RateHits.RemoveAll(Function(T) Now - T > RateWindowMs)
            Return Math.Max(0, RateMax - RateHits.Count)
        End SyncLock
    End Function

#End Region

#Region "请求"

    Private Function Request(Url As String, Optional SkipRateWait As Boolean = False) As String
        If ApiBase = "" Then Throw New Exception("尚未配置机器人服务地址")
        If Not SkipRateWait Then RateWait()
        '日志只留接口路径，域名、群号、令牌都不进日志
        Dim Clean As String = Url.Replace(ApiBase, "").Split("?"c)(0)
        Logger.Info("机器人接口请求：" & Clean)
        Dim Hdr(0, 1) As String
        Hdr(0, 0) = "Authorization"
        Hdr(0, 1) = "Bearer " & ApiToken
        Return NetRequestByClient(Url, HttpMethod.Get, Headers:=Hdr, Timeout:=TimeoutMs, MakeLog:=False)
    End Function

    ''' <summary>
    ''' 下载接口返回的外链信息。
    ''' 新版 /v2/api/download 只回一段 JSON：{ok, index, type, url, name, size, expires_in}，
    ''' 真正的文件要去 url 取（外链、带时效签名）。
    ''' </summary>
    Friend Class DownloadTarget
        Public Property Url As String = ""
        Public Property FileName As String = ""
        Public Property Size As Long = -1
    End Class

    ''' <summary>
    ''' 请求下载接口拿到外链。
    ''' 服务端约定：GET /v2/api/download?index=xxx&amp;type=url 返回
    ''' {"ok":true,"url":"...","name":"...","size":123}，真正的文件要从 url 取。
    ''' 本版本只支持这一种返回方式。
    ''' </summary>
    Friend Function ResolveDownloadTarget(PathAndQuery As String) As DownloadTarget
        Dim R As New DownloadTarget
        Dim FullUrl As String = If(PathAndQuery.StartsWithF("http"), PathAndQuery, ApiBase & PathAndQuery)
        '强制要求返回外链；调用方已经带了 type 参数就不再重复加
        If FullUrl.IndexOf("type=", StringComparison.OrdinalIgnoreCase) < 0 Then
            FullUrl &= If(FullUrl.Contains("?"), "&", "?") & "type=url"
        End If
        Logger.Info("机器人接口下载：" & FullUrl.Replace(ApiBase, "").Split("?"c)(0))
        Dim Hdr(0, 1) As String
        Hdr(0, 0) = "Authorization"
        Hdr(0, 1) = "Bearer " & ApiToken
        Dim Body As String = NetRequestByClient(FullUrl, HttpMethod.Get, Headers:=Hdr, Timeout:=TimeoutMs, MakeLog:=False)
        Dim Obj As JObject
        Try
            Obj = JObject.Parse(Body)
        Catch ex As Exception
            Throw New Exception("下载接口没有返回外链信息：" & SafeText(Body))
        End Try
        If Obj("ok") IsNot Nothing AndAlso Obj("ok").ToString().ToLower() = "false" Then
            Throw New Exception(If(Obj("error"), "下载接口返回失败").ToString())
        End If
        R.Url = If(Obj("url"), "").ToString().Trim()
        R.FileName = If(Obj("name"), "").ToString().Trim()
        R.Size = Val(If(Obj("size"), "-1").ToString())
        If R.Url = "" Then Throw New Exception("下载接口没有给出下载地址")
        Return R
    End Function

    ''' <summary>
    ''' 直接把接口响应当文件流下载到本地（/v2/api/download 就是这样）。
    ''' 返回服务器建议的文件名，可能为空。必须在工作线程调用。
    ''' 新接口会先换外链再下载，老接口直接下。
    ''' 返回服务器建议的文件名。OnProgress 参数为（已下载, 总字节）。
    ''' 必须在工作线程调用。
    ''' </summary>
    Friend Function DownloadToFile(PathAndQuery As String, LocalFile As String,
                                   Optional OnProgress As Action(Of Long, Long) = Nothing) As String
        If ApiBase = "" Then Throw New Exception("尚未配置机器人服务地址")
        Dim Target As DownloadTarget = ResolveDownloadTarget(PathAndQuery)
        Dim DownloadUrl As String = Target.Url
        Dim NeedAuth As Boolean = DownloadUrl.StartsWithF(ApiBase, True) '外链可能是别家域名，别把令牌带出去
        Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(DownloadUrl), Net.HttpWebRequest)
        Req.Method = "GET"
        Req.Timeout = 60000
        Req.ReadWriteTimeout = 120000
        If NeedAuth Then Req.Headers.Add("Authorization", "Bearer " & ApiToken)
        DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalFile))
        Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
            '文件名：优先用接口给的，其次看响应头
            Dim Suggested As String = Target.FileName
            If Suggested = "" Then
                Try
                    Dim Disp As String = Resp.Headers("Content-Disposition")
                    If Not String.IsNullOrWhiteSpace(Disp) Then
                        Dim M As System.Text.RegularExpressions.Match =
                            System.Text.RegularExpressions.Regex.Match(Disp, "filename\*?=(?:UTF-8''|"")?([^"";]+)")
                        If M.Success Then Suggested = Uri.UnescapeDataString(M.Groups(1).Value.Trim())
                    End If
                Catch ex As Exception
                End Try
            End If
            '总大小：响应头优先，其次接口给的
            Dim Total As Long = Resp.ContentLength
            If Total <= 0 Then Total = Target.Size
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
            Return Suggested
        End Using
    End Function

    ''' <summary>云数据写入（/v2/api/userwrite）。ValueJson 必须是 JSON 文本。成功返回 Nothing，失败返回错误信息。</summary>
    Friend Function CloudWrite(Key As String, ValueJson As String) As String
        If Not IsQqBound Then Return "还没绑定 QQ，无法同步"
        Try
            If Not ChatRateWait() Then Return "发送太频繁，请稍后再试"
            Dim Body As New JObject From {
                {"user_key", UserKey},
                {"key", Key},
                {"value", JToken.Parse(ValueJson)}
            }
            Dim Hdr(0, 1) As String
            Hdr(0, 0) = "Authorization"
            Hdr(0, 1) = "Bearer " & ApiToken
            Dim Resp As String = NetRequestByClient(ApiBase & PathUserWrite, HttpMethod.Post,
                                                    Body.ToString(), "application/json; charset=utf-8",
                                                    Timeout:=TimeoutMs, Headers:=Hdr, MakeLog:=False)
            Dim Obj As JObject = JObject.Parse(Resp)
            If Obj("ok") IsNot Nothing AndAlso Obj("ok").ToString().ToLower() = "false" Then
                Return If(Obj("error"), "同步失败").ToString()
            End If
            Return Nothing
        Catch ex As Exception
            Logger.Warn(ex, "云数据写入失败")
            Return FriendlyError(ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' 云数据读取（/v2/api/userread）。返回 value 的 JSON 文本。
    ''' 键还不存在（服务端回 404 或 ok=false + "不存在"）视为空串，不算错误。
    ''' </summary>
    Friend Function CloudRead(Key As String) As String
        If Not IsQqBound Then Throw New Exception("还没绑定 QQ，无法同步")
        If Not ChatRateWait() Then Throw New Exception("发送太频繁，请稍后再试")
        Dim Body As New JObject From {
            {"user_key", UserKey},
            {"key", Key}
        }
        Dim Hdr(0, 1) As String
        Hdr(0, 0) = "Authorization"
        Hdr(0, 1) = "Bearer " & ApiToken
        Dim Resp As String
        Try
            Resp = NetRequestByClient(ApiBase & PathUserRead, HttpMethod.Post,
                                      Body.ToString(), "application/json; charset=utf-8",
                                      Timeout:=TimeoutMs, Headers:=Hdr, MakeLog:=False)
        Catch ex As Exception
            '首次开启同步时云端还没有这个键，服务端按 404 处理，属于正常情况
            If IsNotFound(ex.Message) Then Return ""
            Throw
        End Try
        Dim Obj As JObject
        Try
            Obj = JObject.Parse(Resp)
        Catch ex As Exception
            Throw New Exception("云端返回的内容无法解析：" & SafeText(Resp))
        End Try
        If Obj("ok") IsNot Nothing AndAlso Obj("ok").ToString().ToLower() = "false" Then
            Dim ErrText As String = If(Obj("error"), "读取失败").ToString()
            If IsNotFound(ErrText) Then Return ""
            Throw New Exception(ErrText)
        End If
        Dim V = Obj("value")
        If V Is Nothing OrElse V.Type = JTokenType.Null Then Return ""
        Return V.ToString(Newtonsoft.Json.Formatting.None)
    End Function

    '==== 离线模式 ====
    '自动降级用：远程接口连不上时置 True，恢复后自动清掉，不写配置
    Private AutoOfflineFlag As Boolean = False

    ''' <summary>
    ''' 是否处于常规（离线）模式。
    ''' 手动开关（DstOfflineMode，持久化）或自动降级任一成立即为离线。
    ''' </summary>
    Public ReadOnly Property IsOffline As Boolean
        Get
            Return Settings.Get(Of Boolean)("DstOfflineMode") OrElse AutoOfflineFlag
        End Get
    End Property

    ''' <summary>
    ''' 是否配置了远程服务地址。
    ''' 开源版默认不带预设服务，也没配自定义地址时为 False，此时界面完全不露出与机器人的关系。
    ''' </summary>
    Public ReadOnly Property HasService As Boolean
        Get
            Return ApiBase <> ""
        End Get
    End Property

    ''' <summary>是否因为连不上而临时降级（界面上可以提示用户）。</summary>
    Public ReadOnly Property IsAutoOffline As Boolean
        Get
            Return AutoOfflineFlag
        End Get
    End Property

    ''' <summary>探测远程接口是否可用，顺便更新自动降级标记。必须在工作线程调用。</summary>
    Public Function ProbeOnline() As Boolean
        If ApiBase = "" Then
            AutoOfflineFlag = True
            Return False
        End If
        Try
            Dim Hdr(0, 1) As String
            Hdr(0, 0) = "Authorization"
            Hdr(0, 1) = "Bearer " & ApiToken
            'getvar 不需要参数，是最轻的健康检查；list 必须带 qqgroup，拿来做探测会被判 400
            NetRequestByClient(ApiBase & DstUpdate.PathGetVar, HttpMethod.Get, Headers:=Hdr, Timeout:=8000, MakeLog:=False)
            AutoOfflineFlag = False
            Return True
        Catch ex As Exception
            Dim Msg As String = ex.Message
            '4xx 说明服务器活着，只是参数或鉴权不对；只有 5xx 和连不上才算离线
            If Msg.Contains("(4") OrElse Msg.Contains("429") Then
                AutoOfflineFlag = False
                Return True
            End If
            Logger.Info("远程接口不可用，进入离线模式：" & FriendlyError(Msg))
            AutoOfflineFlag = True
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 手动上线：清掉手动与自动两种离线标记，再试一次连通性。
    ''' 失败就把自动标记重新置上。必须在工作线程调用。
    ''' </summary>
    Public Function GoOnline() As Boolean
        Settings.Set("DstOfflineMode", False)
        AutoOfflineFlag = False
        Return ProbeOnline()
    End Function
    ''' <summary>判断是不是“云端还没有这个键”。</summary>
    Private Function IsNotFound(Msg As String) As Boolean
        If String.IsNullOrWhiteSpace(Msg) Then Return False
        Return Msg.Contains("404") OrElse Msg.Contains("不存在") OrElse
               Msg.ToLower().Contains("not found") OrElse
               Msg.Contains("未找到") OrElse Msg.Contains("no such")
    End Function
    ''' <summary>通用接口调用：Body 传 Nothing 走 GET，否则走 POST JSON。必须在工作线程调用。</summary>
    Friend Function RequestJson(PathAndQuery As String, Body As String) As String
        If ApiBase = "" AndAlso Not PathAndQuery.StartsWithF("http") Then Throw New Exception("尚未配置机器人服务地址")
        Dim Hdr(0, 1) As String
        Hdr(0, 0) = "Authorization"
        Hdr(0, 1) = "Bearer " & ApiToken
        Dim Url As String = If(PathAndQuery.StartsWithF("http"), PathAndQuery, ApiBase & PathAndQuery)
        Logger.Info("机器人接口请求：" & Url.Replace(ApiBase, "").Split("?"c)(0))
        If Body Is Nothing Then
            Return NetRequestByClient(Url, HttpMethod.Get, Headers:=Hdr, Timeout:=TimeoutMs, MakeLog:=False)
        Else
            Return NetRequestByClient(Url, HttpMethod.Post, Body, "application/json; charset=utf-8", Timeout:=TimeoutMs, Headers:=Hdr, MakeLog:=False)
        End If
    End Function

    ''' <summary>
    ''' 拉取服务器列表（带缓存）。OnDone 在 UI 线程回调。
    ''' </summary>
    Public Sub RefreshServers(Optional Force As Boolean = False, Optional OnDone As Action = Nothing, Optional SkipRateWait As Boolean = False)
        If GroupNo = "" Then
            '还没选群：先拉群列表，选完之后由调用方再刷新
            MsgLast = "请先在 设置 → 机器人 里选择一个 QQ 群"
            If OnDone IsNot Nothing Then RunInUi(OnDone)
            Return
        End If
        If Not Force AndAlso ServerList.Count > 0 AndAlso (Date.Now - UpdatedAt).TotalMilliseconds < CacheMs Then
            If OnDone IsNot Nothing Then RunInUi(OnDone)
            Return
        End If
        RunInThread(
        Sub()
            Try
                Dim Url As String = ApiBase & PathList & "?qqgroup=" & GroupNo
                Dim Json As String = Request(Url, SkipRateWait)
                RespLast = Json
                Dim Parsed As New List(Of DstServer)
                Dim Arr As JArray = JArray.Parse(Json)
                For Each Item As JToken In Arr
                    Dim S As New DstServer With {
                        .InstanceId = If(Item("instance_id"), "").ToString(),
                        .Nickname = If(Item("nickname"), "").ToString(),
                        .Ip = If(Item("ip"), "").ToString(),
                        .IsOnline = If(Item("isOnline"), "").ToString().ToLower() = "true",
                        .PlayerCount = CInt(Val(If(Item("player_count"), "0").ToString())),
                        .MaxPlayerCount = CInt(Val(If(Item("max_player_count"), "0").ToString())),
                        .Source = If(Item("source"), "").ToString(),
                        .DownloadIndex = If(Item("download_index"), "").ToString().Trim()
                    }
                    Dim PlayerArr = Item("player")
                    If PlayerArr IsNot Nothing AndAlso PlayerArr.Type = JTokenType.Array Then
                        For Each P As JToken In PlayerArr
                            If P.Type = JTokenType.String Then S.Players.Add(P.ToString())
                        Next
                    End If
                    If S.Nickname = "" Then S.Nickname = S.Ip
                    Parsed.Add(S)
                Next
                SyncLock ServerList
                    ServerList = Parsed
                End SyncLock
                UpdatedAt = Date.Now
                ErrRaw = ""
                StatusLast = If(Parsed.Count > 0, BotStatus.Online, BotStatus.Offline)
                MsgLast = $"共 {Parsed.Count} 个服务器，{Parsed.Where(Function(X) X.IsOnline).Count()} 个在线"
            Catch ex As Exception
                ErrRaw = ex.Message
                StatusLast = If(ex.Message.Contains("429") OrElse ex.Message.Contains("过于频繁"), BotStatus.RateLimited, BotStatus.CheckFailed)
                Logger.Warn(ex, "拉取帝释天服务器列表失败")
            End Try
            RunInUi(
            Sub()
                If OnDone IsNot Nothing Then OnDone()
            End Sub)
        End Sub)
    End Sub

    ''' <summary>
    ''' 测试连通性：请求 /v2/api，成功即视为可用。
    ''' </summary>
    Public Sub TestConnect(Optional Silent As Boolean = False)
        RunInThread(
        Sub()
            Dim CostMs As Integer = GetTimeMs()
            Try
                Dim Json As String = Request(ApiBase & PathList)
                RespLast = Json
                StatusLast = BotStatus.Online
                ErrRaw = ""
                CostMs = GetTimeMs() - CostMs
                If Not Silent Then RunInUi(Sub() Hint($"连接成功（{ServiceName}，{CostMs} ms）", HintType.Green))
            Catch ex As Exception
                StatusLast = BotStatus.CheckFailed
                ErrRaw = ex.Message
                If Not Silent Then RunInUi(Sub() Hint($"连接失败：{FriendlyError(ex.Message)}", HintType.Red))
            End Try
        End Sub)
    End Sub

    Public ReadOnly Property RateLeft As Integer
        Get
            Return RateRemain()
        End Get
    End Property

#End Region

#Region "加入群"

    ''' <summary>
    ''' 一键加群：用浏览器打开 QQ 的群分享页，由腾讯自己的页面去唤起 QQ。
    ''' 不走 mqqapi，避免新版 QQ 报「当前QQ暂不支持该内容」。
    ''' </summary>
    Public Sub JoinGroup()
        Dim G As String = GroupNo
        If G = "" Then
            Hint("还没有选择 QQ 群，请先到 设置 → 机器人 里选一个", HintType.Red)
            Return
        End If
        '先复制群号，万一浏览器没唤起也好手动搜
        Try
            Clipboard.SetText(G)
        Catch ex As Exception
            Logger.Info(ex, "复制群号失败")
        End Try
        Try
            OpenWebsite($"https://qun.qq.com/universal-share/share?groupcode={Uri.EscapeDataString(G)}")
            Hint($"已在浏览器打开群 {GroupMasked} 的分享页，按页面提示加入", HintType.Blue)
        Catch ex As Exception
            Hint($"打开分享页失败：{FriendlyError(ex.Message)}。群号 {GroupMasked} 已复制到剪贴板", HintType.Red)
        End Try
    End Sub

#End Region

#Region "QQ 绑定（/v2/api/login）"

    ''' <summary>发起登录绑定时附带的应用名（会显示给 QQ 用户）。</summary>
    Public ReadOnly Property AppInfo As String
        Get
            Return LauncherName
        End Get
    End Property

    ''' <summary>应用说明，同样会显示给 QQ 用户。</summary>
    Public ReadOnly Property AppDesc As String
        Get
            Return $"在启动器内查看群服务器状态与公告，并绑定 QQ 接收通知（{VersionDisplay}）"
        End Get
    End Property

    ''' <summary>从服务端错误文本里抽出 info 字段，给用户看原话。</summary>
    Private Function ServerInfo(Msg As String) As String
        If String.IsNullOrWhiteSpace(Msg) Then Return ""
        Dim M As Match = Regex.Match(Msg, """info""\s*:\s*""([^""]+)""")
        If M.Success Then Return M.Groups(1).Value
        Return Msg
    End Function

    Public Enum LoginKind
        Success = 0
        MultiGroup = 1
        Refused = 2
        RateLimited = 3
        NotDeployed = 4
        Failed = 5
        NoAuth = 6
        BadRequest = 7
    End Enum

    Public Class LoginResult
        Public Kind As LoginKind = LoginKind.Failed
        Public UserKey As String = ""
        Public Groups As New List(Of String)
        Public Message As String = ""
    End Class

    '接口路径
    Private Const PathLogin As String = "/v2/api/login"
    '用户的 5 分钟等待 + 余量
    Private Const LoginTimeoutMs As Integer = 330000
    '成功冷却 5 分钟
    Private Const LoginSuccessGapSec As Integer = 300
    '失败限频：1 分钟 5 次
    Private Const LoginFailMax As Integer = 5
    Private Const LoginFailWindowSec As Integer = 60
    '同一 IP 每天最多 10 次成功
    Private Const LoginDailyMax As Integer = 10
    '登录失败记录（内存内即可，进程重启后重置）
    Private ReadOnly LoginFailHits As New List(Of Long)
    Private ReadOnly LoginLock As New Object
    '正在等待用户确认
    Public LoginWaiting As Boolean = False

    Public ReadOnly Property QqNumber As String
        Get
            Return Settings.Get(Of String)("DstQqNumber").Trim()
        End Get
    End Property

    Public ReadOnly Property UserKey As String
        Get
            Return Settings.Get(Of String)("DstQqUserKey").Trim()
        End Get
    End Property

    Public ReadOnly Property IsQqBound As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(UserKey)
        End Get
    End Property

    ''' <summary>
    ''' 本机缓存的群名。群列表还没拉下来时也能显示群名，而不是只有一个群号。
    ''' 每次拿到群列表、或用户切换群时都会刷新它。
    ''' </summary>
    Public ReadOnly Property GroupNameCached As String
        Get
            Dim Name As String = Settings.Get(Of String)("DstGroupName").Trim()
            If Name <> "" Then Return Name
            Dim Id As String = GroupNo
            Return If(Id = "", "", "QQ 群 " & Id)
        End Get
    End Property

    ''' <summary>把群名记到本机，供下次启动直接显示。</summary>
    Private Sub RememberGroupName(Groups_ As List(Of DstGroup))
        Dim Id As String = GroupNo
        If Id = "" Then Return
        Dim Hit As DstGroup = Groups_.FirstOrDefault(Function(G) G.GroupId = Id)
        If Hit IsNot Nothing AndAlso Hit.GroupName <> "" Then
            If Settings.Get(Of String)("DstGroupName") <> Hit.GroupName Then
                Settings.Set("DstGroupName", SafeRemoteText(Hit.GroupName, 64))
            End If
        End If
    End Sub

    ''' <summary>切换群时同步缓存群名。</summary>
    Public Sub SetGroup(Id As String, Optional Name As String = "")
        Settings.Set("DstGroupId", SafeGroupId(Id))
        If Name <> "" Then Settings.Set("DstGroupName", Name)
    End Sub
    ''' <summary>界面展示用的 key 掩码，绝不完整展示。</summary>
    Public ReadOnly Property UserKeyMasked As String
        Get
            Dim K As String = UserKey
            If K.Length <= 8 Then Return If(K = "", "", "****")
            Return K.Substring(0, 4) & "****" & K.Substring(K.Length - 4)
        End Get
    End Property

    Public ReadOnly Property TodaySuccessCount As Integer
        Get
            If Settings.Get(Of Integer)("DstQqBindDay") <> TodayKey() Then Return 0
            Return Settings.Get(Of Integer)("DstQqBindCount")
        End Get
    End Property

#Region "本地配额"

    ''' <summary>
    ''' 检查本地是否允许发起登录。返回空字符串表示允许，否则返回原因。
    ''' </summary>
    Public Function LoginBlockReason() As String
        '成功冷却
        Dim LastOkTicks As Long = Settings.Get(Of Long)("DstQqLastOkTicks")
        If LastOkTicks > 0 Then
            Dim LastOk As Date = New Date(LastOkTicks)
            Dim Gap As Double = (Date.Now - LastOk).TotalSeconds
            If Gap >= 0 AndAlso Gap < LoginSuccessGapSec Then
                Return $"绑定成功后 {LoginSuccessGapSec} 秒内不能再次发起，还需等待 {CInt(LoginSuccessGapSec - Gap)} 秒"
            End If
        End If
        '失败限频
        SyncLock LoginLock
            Dim NowMs As Long = GetTimeMs()
            LoginFailHits.RemoveAll(Function(T) NowMs - T > LoginFailWindowSec * 1000)
            If LoginFailHits.Count >= LoginFailMax Then
                Return $"{LoginFailWindowSec} 秒内失败次数已达 {LoginFailMax} 次，请稍后再试"
            End If
        End SyncLock
        '每日成功上限
        If TodaySuccessCount >= LoginDailyMax Then
            Return $"今日成功绑定次数已达 {LoginDailyMax} 次上限"
        End If
        Return ""
    End Function

    Private Sub RecordLoginFail()
        SyncLock LoginLock
            LoginFailHits.Add(GetTimeMs())
        End SyncLock
    End Sub

    Private Sub RecordLoginSuccess()
        Settings.Set("DstQqLastOkTicks", Date.Now.Ticks)
        Dim DayKey As Integer = TodayKey()
        If Settings.Get(Of Integer)("DstQqBindDay") <> DayKey Then
            Settings.Set("DstQqBindDay", DayKey)
            Settings.Set("DstQqBindCount", 1)
        Else
            Settings.Set("DstQqBindCount", Settings.Get(Of Integer)("DstQqBindCount") + 1)
        End If
    End Sub

#End Region

#Region "发起绑定"

    ''' <summary>
    ''' 解析绑定结果。成功则落库。
    ''' </summary>
    Private Function ParseLogin(Json As String) As LoginResult
        Dim R As New LoginResult With {.Message = Json}
        Try
            Dim Obj As JObject = JObject.Parse(Json)
            Dim Key As String = If(Obj("user_key"), "").ToString().Trim()
            If Key <> "" Then
                R.Kind = LoginKind.Success
                R.UserKey = Key
                Return R
            End If
            Dim Info As String = If(Obj("info"), "").ToString()
            R.Message = Info
            If Info.Contains("多个群聊存在") Then
                R.Kind = LoginKind.MultiGroup
                Dim List_ = Obj("qqgroupslist")
                If List_ IsNot Nothing AndAlso List_.Type = JTokenType.Array Then
                    For Each Item As JToken In List_
                        If Item.ToString().Trim() <> "" Then R.Groups.Add(Item.ToString().Trim())
                    Next
                End If
            ElseIf Info.Contains("绑定失败") Then
                R.Kind = LoginKind.Refused
            Else
                R.Kind = LoginKind.Failed
            End If
        Catch ex As Exception
            R.Kind = If(Json.TrimStart().StartsWithF("{") OrElse Json.TrimStart().StartsWithF("["),
                        LoginKind.Failed, LoginKind.NotDeployed)
            R.Message = FriendlyError(ex.Message)
        End Try
        Return R
    End Function

    ''' <summary>
    ''' 发起一次绑定。OnResult 在 UI 线程回调。GroupNo 为空表示让服务端自行判断。
    ''' </summary>
    Public Sub StartLogin(Qq As String, GroupNo As String, OnResult As Action(Of LoginResult))
        Qq = If(Qq, "").Trim()
        If Not Regex.IsMatch(Qq, "^\d{5,12}$") Then
            Hint("QQ 号格式不正确", HintType.Red)
            Return
        End If
        Dim Block As String = LoginBlockReason()
        If Block <> "" Then
            Hint(Block, HintType.Red)
            Return
        End If
        LoginWaiting = True
        Hint("绑定请求已发出，请在 QQ 群里按提示回复 OK（5 分钟内有效）", HintType.Blue)
        RunInThread(
        Sub()
            Dim R As LoginResult
            Try
                '登录接口：参数走 JSON body（文档写 query，实测 body 才对），鉴权与其他接口一致用 Bearer
                Dim Body As New JObject From {
                    {"qq", Qq},
                    {"app", AppInfo},
                    {"info", AppDesc}
                }
                If Not String.IsNullOrWhiteSpace(GroupNo) Then Body("qqgroup") = GroupNo.Trim()
                Dim Hdr(0, 1) As String
                Hdr(0, 0) = "Authorization"
                Hdr(0, 1) = "Bearer " & ApiToken
                Dim Json As String = NetRequestByClient(ApiBase & PathLogin, HttpMethod.Post,
                                                        Body.ToString(), "application/json; charset=utf-8",
                                                        Timeout:=LoginTimeoutMs, Headers:=Hdr, MakeLog:=False)
                RespLast = Json
                R = ParseLogin(Json)
            Catch ex As Exception
                Dim Msg As String = ex.Message
                Dim Info As String = ServerInfo(Msg)
                R = New LoginResult With {.Message = Info}
                If Msg.Contains("429") OrElse Msg.Contains("过于频繁") Then
                    R.Kind = LoginKind.RateLimited
                ElseIf Msg.Contains("401") OrElse Msg.Contains("未授权") OrElse Msg.Contains("令牌") Then
                    R.Kind = LoginKind.NoAuth
                ElseIf Info <> Msg AndAlso Info <> "" Then
                    '服务端给了 info 原话（未找到该用户、参数不合法等），直接展示
                    R.Kind = If(Msg.Contains("400"), LoginKind.BadRequest, LoginKind.Failed)
                ElseIf Msg.Contains("404") OrElse Msg.Contains("405") OrElse Msg.Contains("Not Found") Then
                    R.Kind = LoginKind.NotDeployed
                Else
                    R.Kind = LoginKind.Failed
                End If
                Logger.Warn(ex, "QQ 绑定请求失败")
            End Try
            LoginWaiting = False
            '落库与计数
            If R.Kind = LoginKind.Success Then
                Settings.Set("DstQqNumber", Qq)
                Settings.Set("DstQqUserKey", R.UserKey)
                RecordLoginSuccess()
            ElseIf R.Kind = LoginKind.Refused OrElse R.Kind = LoginKind.Failed OrElse R.Kind = LoginKind.RateLimited Then
                RecordLoginFail()
            End If
            RunInUi(
            Sub()
                Select Case R.Kind
                    Case LoginKind.Success
                        Hint($"绑定成功，QQ {Qq} 已关联", HintType.Green)
                    Case LoginKind.MultiGroup
                        Hint("该 QQ 号在多个群聊中，需要选择其中一个", HintType.Blue)
                    Case LoginKind.Refused
                        Hint("绑定失败：等待超时或用户拒绝", HintType.Red)
                    Case LoginKind.RateLimited
                        Hint("触发服务端限频，请稍后再试", HintType.Red)
                    Case LoginKind.NotDeployed
                        Hint("绑定服务尚未部署（接口不可用），等后端上线后再试", HintType.Red)
                    Case LoginKind.NoAuth
                        Hint("绑定接口鉴权失败，可能是令牌不接受该接口，稍后再试或联系群主", HintType.Red)
                    Case LoginKind.BadRequest
                        Hint("绑定请求被拒绝：" & FriendlyError(R.Message), HintType.Red)
                    Case Else
                        Hint($"绑定失败：{FriendlyError(R.Message)}", HintType.Red)
                End Select
                If OnResult IsNot Nothing Then OnResult(R)
            End Sub)
        End Sub)
    End Sub

    ''' <summary>
    ''' 交互式绑定流程：输入 QQ → 发起 → 多群时让用户选一个群再重试。
    ''' </summary>
    Public Sub ShowBindDialog(Optional OnChanged As Action = Nothing)
        Dim Qq As String = MyMsgBoxInput("绑定 QQ", "输入要绑定的 QQ 号。发起后帝释天机器人会在群里 @" & "你并发送确认消息，5 分钟内回复 OK 即完成绑定。",
                                         QqNumber, HintText:="QQ 号")
        If Qq Is Nothing Then Return
        Qq = Qq.Trim()
        If Not Regex.IsMatch(Qq, "^\d{5,12}$") Then
            Hint("QQ 号格式不正确", HintType.Red)
            Return
        End If
        '优先用设置里选好的群，服务端会在这个群里 @ 用户

        StartLogin(Qq, GroupNo, Sub(R) HandleLoginResult(Qq, OnChanged, R))
    End Sub

    Private Sub HandleLoginResult(Qq As String, OnChanged As Action, Result As LoginResult)
        If Result.Kind = LoginKind.MultiGroup Then
            If Result.Groups.Count = 0 Then
                Hint("服务端返回多群但群列表为空，请手动在设置里指定群号", HintType.Red)
                Return
            End If
            Dim Selections = Result.Groups.Select(Function(G) CType(New MyRadioBox With {.Text = "QQ 群 " & MaskGroup(G)}, IMyRadio))
            Dim Index As Integer? = MyMsgBoxSelect(Selections, "该 QQ 号在多个群聊中，请选择要绑定哪个群")
            If Index Is Nothing Then
                Hint("已取消绑定", HintType.Blue)
                Return
            End If
            '重试：带上选中的群号
            StartLogin(Qq, Result.Groups(Index.Value), Sub(R) HandleLoginResult(Qq, OnChanged, R))
            Return
        End If
        If OnChanged IsNot Nothing Then OnChanged()
    End Sub

    Private Function TodayKey() As Integer
        Return Integer.Parse(Date.Now.ToString("yyyyMMdd"))
    End Function

    Private Function MaskGroup(G As String) As String
        If G.Length <= 5 Then Return G
        Return G.Substring(0, 3) & New String("*"c, G.Length - 5) & G.Substring(G.Length - 2)
    End Function

    ''' <summary>解绑：先请求服务器作废 user_key，成功后再清本地。回调在 UI 线程。</summary>
    Public Sub UnbindQq(Optional OnDone As Action(Of Boolean) = Nothing)
        If Not IsQqBound Then
            If OnDone IsNot Nothing Then RunInUi(Sub() OnDone(False))
            Return
        End If
        If MyMsgBox($"确定解除当前绑定吗？{vbCrLf}解绑后需要重新在群里完成一次确认才能再次绑定。",
                    "解除绑定", "解除", "取消", IsWarn:=True) <> 1 Then
            If OnDone IsNot Nothing Then RunInUi(Sub() OnDone(False))
            Return
        End If
        Dim Key As String = UserKey
        RunInThread(
        Sub()
            Dim Ok As Boolean = False
            Dim Err As String = ""
            Try
                If Not ChatRateWait() Then
                    Err = "发送太频繁，请稍后再试"
                    Exit Try
                End If
                Dim Body As New JObject From {{"user_key", Key}}
                Dim Hdr(0, 1) As String
                Hdr(0, 0) = "Authorization"
                Hdr(0, 1) = "Bearer " & ApiToken
                NetRequestByClient(ApiBase & PathUnbind, HttpMethod.Post,
                                   Body.ToString(), "application/json; charset=utf-8",
                                   Timeout:=TimeoutMs, Headers:=Hdr, MakeLog:=False)
                Ok = True
            Catch ex As Exception
                '404 表示 key 已失效或早已解绑，按成功处理
                If ex.Message.Contains("404") Then
                    Ok = True
                Else
                    Err = FriendlyError(ex.Message)
                End If
                Logger.Warn(ex, "解绑请求失败")
            End Try
            If Ok Then
                UserInfoQq = ""
                UserInfoName = ""
                UserInfoAvatar = ""
            End If
            RunInUi(
            Sub()
                If Ok Then
                    Settings.Set("DstQqUserKey", "")
                    Settings.Set("DstQqNumber", "")
                    Hint("已解除绑定", HintType.Green)
                Else
                    Hint("解绑失败：" & Err, HintType.Red)
                End If
                If OnDone IsNot Nothing Then OnDone(Ok)
            End Sub)
        End Sub)
    End Sub
#End Region

#End Region

End Module
