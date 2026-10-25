Imports System.Text

''' <summary>
''' 远程预设列表（yushe.json）的获取与本地备份。
''' <para>
''' 预设文件本身被当作**不可信内容**处理：它只提供「服务地址、群号、展示名」这类信息，
''' 真正的凭据（ClientId / CurseForge Key）由服务端接口按地址下发，不写在这个文件里。
''' 即便如此，仍然做了两件事：
''' </para>
''' <para>
''' 1. 每次成功拉取都留一份带时间戳的备份，格式异常的版本不会被写进去，
'''    这样即使远端被篡改，本地也还留着之前能用的那份；
''' 2. 所有字段都过一遍净化与长度限制，避免把脏数据带进配置或界面。
''' </para>
''' </summary>
Friend Module ModPresetSource

    ''' <summary>预设文件地址。放在独立分支上，与程序代码分开维护。</summary>
    Public Const PresetUrl As String =
        "https://raw.githubusercontent.com/xiaoqiezi-china/pcl-for-dst/refs/heads/main/yushe.json"
    ''' <summary>国内备用源。留空表示没配置，界面上会显示为未启用。</summary>
    Public Const GiteeUrl As String = ""


    ''' <summary>本地备份目录。</summary>
    Public ReadOnly Property BackupFolder As String
        Get
            Return Paths.Base & "PCL\\PresetBackup\\"
        End Get
    End Property

    ''' <summary>备份最多保留几份。</summary>
    Private Const MaxBackup As Integer = 5

    ''' <summary>预设文件大小上限，防止拉回一个超大响应。</summary>
    Private Const MaxBytes As Integer = 256 * 1024

    ''' <summary>一条预设。</summary>
    Public Class Preset
        Public Property Id As String = ""
        Public Property Name As String = ""
        ''' <summary>服务地址；空串表示需要用户自己填。</summary>
        Public Property Api As String = ""
        ''' <summary>服务端颁发的令牌，可能为空。</summary>
        Public Property ApiKey As String = ""
        ''' <summary>展示用的 QQ 群号，可能为空。</summary>
        Public Property QqGroup As String = ""
        ''' <summary>一句话说明，可能为空。</summary>
        Public Property Note As String = ""
        ''' <summary>是否可用。缺 id 或 name 的条目会被丢掉。</summary>
        Public ReadOnly Property IsValid As Boolean
            Get
                Return Id <> "" AndAlso Name <> ""
            End Get
        End Property
        ''' <summary>是否代表「自己填地址」的那一项。</summary>
        Public ReadOnly Property IsCustom As Boolean
            Get
                Return Api = ""
            End Get
        End Property
    End Class

    ''' <summary>最近一次拉取的结果说明，供设置页显示。</summary>
    Public Property LastStatus As String = "未获取"
    ''' <summary>最近一次拉取是否成功。</summary>
    Public Property LastOk As Boolean = False
    ''' <summary>当前可用的预设列表（内存缓存）。</summary>
    Public Property Items As New List(Of Preset)

    ''' <summary>
    ''' 从远端拉取预设列表。成功则刷新缓存并写一份备份；失败时保留旧缓存不动。
    ''' 必须在工作线程调用。
    ''' </summary>
    Public Function Fetch() As Boolean
        Try
            Dim Json As String = NetRequestByClient(PresetUrl, HttpMethod.Get, Timeout:=15000, MakeLog:=False)
            If String.IsNullOrWhiteSpace(Json) Then
                LastStatus = "服务器返回为空"
                LastOk = False
                Return False
            End If
            If Json.Length > MaxBytes Then
                LastStatus = "预设文件过大，已拒绝"
                LastOk = False
                Logger.Warn($"预设文件超过 {MaxBytes} 字节，已忽略")
                Return False
            End If
            Dim Parsed As List(Of Preset) = Parse(Json)
            If Parsed.Count = 0 Then
                LastStatus = "预设列表为空或格式不正确"
                LastOk = False
                Return False
            End If
            Items = Parsed
            SaveBackup(Json)
            LastStatus = $"已获取 {Parsed.Count} 个预设"
            LastOk = True
            Logger.Info($"已获取远程预设 {Parsed.Count} 个")
            Return True
        Catch ex As Exception
            LastStatus = "获取失败：" & ex.Message
            LastOk = False
            Logger.Warn(ex, "获取远程预设失败")
            Return False
        End Try
    End Function

    ''' <summary>从上次成功的备份里读回预设列表。</summary>
    Public Function LoadFromBackup() As Boolean
        Try
            Dim Newest As String = NewestBackup()
            If Newest = "" Then Return False
            Dim Parsed As List(Of Preset) = Parse(FileUtils.ReadAsString(Newest))
            If Parsed.Count = 0 Then Return False
            Items = Parsed
            LastStatus = $"已从本地备份读取（{Date.Now.ToString("MM-dd HH:mm")} 获取的版本）"
            LastOk = True
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "读取预设备份失败")
            Return False
        End Try
    End Function

    ''' <summary>找最新的那份备份。</summary>
    Private Function NewestBackup() As String
        Try
            If Not DirectoryUtils.Exists(BackupFolder) Then Return ""
            Dim Files As List(Of String) = DirectoryUtils.EnumerateFiles(BackupFolder, False, "preset-*.json").ToList()
            If Files.Count = 0 Then Return ""
            Return Files.OrderByDescending(Function(F) PathUtils.GetFileNameWithoutExtension(F)).First()
        Catch ex As Exception
            Logger.Warn(ex, "查找预设备份失败")
            Return ""
        End Try
    End Function

    ''' <summary>写一份备份；内容与最新的那份相同则不写，避免刷屏。</summary>
    Private Sub SaveBackup(Json As String)
        Try
            DirectoryUtils.Create(BackupFolder)
            '和最新一份一样就不重复写
            Dim Newest As String = NewestBackup()
            If Newest <> "" Then
                Try
                    If FileUtils.ReadAsString(Newest).Trim() = Json.Trim() Then Return
                Catch
                End Try
            End If
            Dim Stamp As String = Date.Now.ToString("yyyyMMdd-HHmmss")
            FileUtils.WriteAtomic(BackupFolder & "preset-" & Stamp & ".json", Json)
            '超出的旧备份删掉
            Dim Files As List(Of String) = DirectoryUtils.EnumerateFiles(BackupFolder, False, "preset-*.json").ToList()
            If Files.Count > MaxBackup Then
                For Each Old In Files.OrderBy(Function(F) PathUtils.GetFileNameWithoutExtension(F)).Take(Files.Count - MaxBackup)
                    Try
                        FileUtils.Delete(Old)
                    Catch
                    End Try
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "保存预设备份失败")
        End Try
    End Sub

    ''' <summary>
    ''' 解析预设文件。兼容两种形状：顶层是数组，或顶层是带 presets 字段的对象。
    ''' 每个字段都做净化与长度限制，脏数据直接丢弃。
    ''' </summary>
    Public Function Parse(Json As String) As List(Of Preset)
        Dim Result As New List(Of Preset)
        If String.IsNullOrWhiteSpace(Json) Then Return Result
        Try
            '预设文件里允许写 // 注释（第一版就是这么写的），
            'Newtonsoft 默认不认，先逐行剔掉再解析
            Json = StripComments(Json)
            If String.IsNullOrWhiteSpace(Json) Then Return Result
            Dim Root As JToken
            Using Reader As New IO.StringReader(Json)
                Using Jr As New Newtonsoft.Json.JsonTextReader(Reader) With {.MaxDepth = 16}
                    Root = JToken.Load(Jr)
                End Using
            End Using
            Dim Arr As JArray = TryCast(Root, JArray)
            If Arr Is Nothing Then
                Dim Obj As JObject = TryCast(Root, JObject)
                If Obj IsNot Nothing Then Arr = TryCast(Obj("presets"), JArray)
            End If
            If Arr Is Nothing Then Return Result
            For Each One As JToken In Arr
                If Result.Count >= 64 Then Exit For
                Dim Obj As JObject = TryCast(One, JObject)
                If Obj Is Nothing Then Continue For
                '字段名兼容两套写法：
                '  yushe.json 用 api_url / api_key / qq / nickname；
                '  我们自己约定的写法用 api / apikey / qqgroup / name。
                Dim ApiUrl As String = ReadText(Obj, "api_url", "apiUrl", "api")
                Dim ApiKey As String = ReadText(Obj, "api_key", "apiKey", "token")
                Dim Nick As String = Tidy(ReadText(Obj, "nickname", "name", "title"), 64)
                Dim GroupNo As String = Tidy(ReadText(Obj, "qq", "qqgroup", "qqGroup", "qq_group"), 32)
                '没有 id 就用地址派生一个，保证下拉框里能区分
                Dim RawId As String = Tidy(ReadText(Obj, "id"), 32)
                If RawId = "" Then RawId = ApiUrl
                Dim P As New Preset With {
                    .Id = RawId,
                    .Name = If(Nick = "", ApiUrl, Nick),
                    .Api = TidyUrl(ApiUrl),
                    .ApiKey = Tidy(ApiKey, 256),
                    .QqGroup = GroupNo,
                    .Note = Tidy(ReadText(Obj, "note", "desc", "description"), 120)
                }
                '地址无效就跳过：宁可少一项，也不要让下拉框里出现点不动的死项
                If P.Api = "" Then Continue For
                If P.Name = "" Then P.Name = P.Api
                '同一个 id 只留第一个
                If Result.Any(Function(E) E.Id = P.Id) Then Continue For
                Result.Add(P)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析预设文件失败")
        End Try
        Return Result
    End Function

    ''' <summary>剔掉整行的 // 注释与首尾空白。只处理注释行，不动字符串里的内容。</summary>
    Private Function StripComments(Json As String) As String
        If String.IsNullOrEmpty(Json) Then Return ""
        Dim Sb As New StringBuilder
        For Each RawLine In Json.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim Line As String = RawLine.Trim()
            If Line.StartsWith("//") OrElse Line.StartsWith("#") Then Continue For
            If Line = "" Then Continue For
            Sb.AppendLine(Line)
        Next
        Return Sb.ToString()
    End Function

    Private Function ReadText(Obj As JObject, ParamArray Keys As String()) As String
        For Each Key In Keys
            Dim Prop As JToken = Obj(Key)
            If Prop IsNot Nothing AndAlso Prop.Type <> JTokenType.Object AndAlso Prop.Type <> JTokenType.Array Then
                Dim V As String = Prop.ToString()
                If V <> "" Then Return V
            End If
        Next
        Return ""
    End Function

    ''' <summary>去控制字符并截断。</summary>
    Private Function Tidy(Value As String, MaxLength As Integer) As String
        If String.IsNullOrEmpty(Value) Then Return ""
        Dim Sb As New StringBuilder
        For Each C As Char In Value
            If Not Char.IsControl(C) Then Sb.Append(C)
            If Sb.Length >= MaxLength Then Exit For
        Next
        Return Sb.ToString().Trim()
    End Function

    ''' <summary>
    ''' 校验服务地址：只接受 http/https，其余一律当空处理。
    ''' 空串是有意义的，它代表「自定义」那一项。
    ''' </summary>
    Private Function TidyUrl(Value As String) As String
        Dim Clean As String = Tidy(Value, 256).TrimEnd("/"c)
        If Clean = "" Then Return ""
        Dim Uri As Uri = Nothing
        If Not Uri.TryCreate(Clean, UriKind.Absolute, Uri) Then
            Logger.Warn($"预设里的服务地址不合法，已忽略：{Clean}")
            Return ""
        End If
        If Uri.Scheme <> "http" AndAlso Uri.Scheme <> "https" Then
            Logger.Warn($"预设里的服务地址协议不受支持，已忽略：{Clean}")
            Return ""
        End If
        Return Clean
    End Function

    ''' <summary>按 id 找一个预设。</summary>
    Public Function FindById(Id As String) As Preset
        If String.IsNullOrWhiteSpace(Id) Then Return Nothing
        Return Items.FirstOrDefault(Function(P) P.Id.Equals(Id.Trim(), StringComparison.OrdinalIgnoreCase))
    End Function

End Module
