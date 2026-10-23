''' <summary>
''' 用户自己添加的服务器。与机器人下发的服务器列表完全分开存放：
''' 机器人列表是远程数据，切群、切服务时会被整批清空；
''' 自定义列表是本地数据，只在用户自己增删时变化，任何刷新都不该动它。
''' </summary>
Public Module DstCustomServer

    ''' <summary>一条自定义服务器。</summary>
    Public Class Entry
        ''' <summary>稳定标识，用于把探测结果与列表项对应起来。</summary>
        Public Id As String = ""
        ''' <summary>显示名称。用户不填时用默认名。</summary>
        Public Name As String = ""
        ''' <summary>地址，形如 host 或 host:port。</summary>
        Public Ip As String = ""
    End Class

    ''' <summary>用户没有填写名称时使用的默认名。</summary>
    Public Const DefaultName As String = "新的服务器"

    Private ReadOnly Items As New List(Of Entry)
    Private ReadOnly Lock As New Object
    Private Loaded As Boolean

    ''' <summary>本地保存路径。</summary>
    Public ReadOnly Property SavePath As String
        Get
            Return Paths.Base & "PCLDstCustomServers.json"
        End Get
    End Property

    Private Sub EnsureLoaded()
        SyncLock Lock
            If Loaded Then Return
            Loaded = True
        End SyncLock
        Try
            If Not FileUtils.Exists(SavePath) Then Return
            Dim Content As String = FileUtils.TryReadAsString(SavePath)
            If String.IsNullOrWhiteSpace(Content) Then Return
            Dim Arr As JArray = JArray.Parse(Content)
            Dim Parsed As New List(Of Entry)
            For Each Token As JToken In Arr
                Dim One As New Entry With {
                    .Id = If(Token("id"), "").ToString(),
                    .Name = If(Token("name"), "").ToString(),
                    .Ip = If(Token("ip"), "").ToString()
                }
                '没有 id 或地址的记录直接丢弃，否则会在列表里留下打不开的空行
                If String.IsNullOrWhiteSpace(One.Ip) Then Continue For
                If String.IsNullOrWhiteSpace(One.Id) Then One.Id = NewId()
                If String.IsNullOrWhiteSpace(One.Name) Then One.Name = DefaultName
                Parsed.Add(One)
            Next
            SyncLock Lock
                Items.Clear()
                Items.AddRange(Parsed)
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, "读取自定义服务器失败")
        End Try
    End Sub

    Private Function NewId() As String
        Return "custom-" & Guid.NewGuid().ToString("N").Substring(0, 12)
    End Function

    Private Sub Save()
        Try
            Dim Arr As New JArray
            SyncLock Lock
                For Each One In Items
                    Arr.Add(New JObject From {
                        {"id", One.Id}, {"name", One.Name}, {"ip", One.Ip}})
                Next
            End SyncLock
            DirectoryUtils.Create(PathUtils.RemoveLastPart(SavePath))
            FileUtils.Write(SavePath, Arr.ToString(Newtonsoft.Json.Formatting.Indented))
        Catch ex As Exception
            Logger.Warn(ex, "保存自定义服务器失败")
        End Try
    End Sub

    ''' <summary>当前全部自定义服务器（副本）。</summary>
    Public Function All() As List(Of Entry)
        EnsureLoaded()
        SyncLock Lock
            Return Items.Select(Function(E) New Entry With {.Id = E.Id, .Name = E.Name, .Ip = E.Ip}).ToList()
        End SyncLock
    End Function

    ''' <summary>
    ''' 校验并规范化地址。返回 Nothing 表示可用，否则返回错误说明。
    ''' </summary>
    Public Function ValidateAddress(Ip As String) As String
        If String.IsNullOrWhiteSpace(Ip) Then Return "请填写服务器地址"
        Dim Text As String = Ip.Trim()
        If Text.Contains(" ") Then Return "地址里不能有空格"
        Dim Parts = Text.Split(":"c)
        If Parts.Length > 2 Then Return "地址格式应为 主机名 或 主机名:端口"
        If String.IsNullOrWhiteSpace(Parts(0)) Then Return "请填写主机名或 IP"
        If Parts.Length = 2 Then
            Dim Port As Integer
            If Not Integer.TryParse(Parts(1), Port) Then Return "端口必须是数字"
            If Port < 1 OrElse Port > 65535 Then Return "端口需在 1 到 65535 之间"
        End If
        Return Nothing
    End Function

    ''' <summary>
    ''' 添加一台自定义服务器。返回新记录；地址非法时返回 Nothing 并通过 ErrorText 给出原因。
    ''' </summary>
    Public Function Add(Ip As String, Optional Name As String = "") As Entry
        ErrorText = ValidateAddress(Ip)
        If ErrorText IsNot Nothing Then Return Nothing
        Dim One As New Entry With {
            .Id = NewId(),
            .Ip = Ip.Trim(),
            .Name = If(String.IsNullOrWhiteSpace(Name), DefaultName, Name.Trim())
        }
        EnsureLoaded()
        SyncLock Lock
            '同一地址不重复添加，避免出现两行一模一样、探测结果还各自为政的条目
            Dim Exist = Items.FirstOrDefault(Function(E) String.Equals(E.Ip, One.Ip, StringComparison.OrdinalIgnoreCase))
            If Exist IsNot Nothing Then
                ErrorText = "这个地址已经在列表里了"
                Return Nothing
            End If
            Items.Add(One)
        End SyncLock
        Save()
        ErrorText = Nothing
        Return One
    End Function

    ''' <summary>上一次操作的错误说明。为 Nothing 表示没有错误。</summary>
    Public Property ErrorText As String = Nothing

    ''' <summary>按 Id 删除。</summary>
    Public Sub Remove(Id As String)
        If String.IsNullOrWhiteSpace(Id) Then Return
        EnsureLoaded()
        Dim Removed As Boolean
        SyncLock Lock
            Removed = Items.RemoveAll(Function(E) E.Id = Id) > 0
        End SyncLock
        If Removed Then Save()
    End Sub

    ''' <summary>
    ''' 把自定义记录转成界面使用的 <see cref="DstServer"/>。
    ''' 每次都新建对象：探测状态属于运行时数据，不该写进本地文件。
    ''' </summary>
    Public Function ToServer(One As Entry) As DstServer
        Return New DstServer With {
            .InstanceId = One.Id,
            .Nickname = One.Name,
            .Ip = One.Ip,
            .Source = "自定义",
            .IsCustom = True
        }
    End Function

End Module
