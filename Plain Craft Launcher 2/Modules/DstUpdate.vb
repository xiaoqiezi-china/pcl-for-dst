Imports System.Net.Http

''' <summary>
''' 启动器自身的更新与「下载索引」相关逻辑，全部走帝释天服务端的 /v2/api 接口。
''' </summary>
Friend Module DstUpdate

    '接口路径
    Public Const PathGetVar As String = "/v2/api/getvar"
    Public Const PathDownload As String = "/v2/api/download"

    ''' <summary>一条更新信息。</summary>
    Public Class DstUpdateInfo
        Public Property RemoteVersion As String = ""
        Public Property Announce As String = ""
        Public Property HasNew As Boolean
        Public Property CompareOk As Boolean
        ''' <summary>服务器标记的重大更新（设置里选“仅重大更新提示”时起作用）。</summary>
        Public Property Critical As Boolean
        Public Property Err As String = ""
    End Class


#Region "版本号"

    ''' <summary>把 "0.0.2" / "v0.0.2" / "2.99.0.0" 这类字符串转成可比较的数字数组。</summary>
    Private Function VersionParts(Version As String) As Integer()
        Dim Clean As String = If(Version, "").Trim().TrimStart("v"c, "V"c)
        Dim Result As New List(Of Integer)
        For Each Seg In Clean.Split("."c, "-"c, "+"c)
            Dim N As Integer
            If Integer.TryParse(Seg, N) Then Result.Add(N)
        Next
        If Result.Count = 0 Then Return New Integer() {}
        Return Result.ToArray()
    End Function

    ''' <summary>返回值 &gt; 0 表示 Remote 更新。无法比较时返回 0 并把 CompareOk 置 False。</summary>
    Public Function CompareVersion(Remote As String, Local As String) As Integer
        Dim A As Integer() = VersionParts(Remote)
        Dim B As Integer() = VersionParts(Local)
        If A.Length = 0 OrElse B.Length = 0 Then Return 0
        Dim Len As Integer = Math.Max(A.Length, B.Length)
        For i = 0 To Len - 1
            Dim X As Integer = If(i < A.Length, A(i), 0)
            Dim Y As Integer = If(i < B.Length, B(i), 0)
            If X <> Y Then Return If(X > Y, 1, -1)
        Next
        Return 0
    End Function

#End Region

#Region "getvar"

    ''' <summary>
    ''' 取全部远程变量。服务端会把所有变量一次性返回，这里统一拿回来再挑。
    ''' 必须在工作线程调用。
    ''' </summary>
    Public Function FetchVars() As JObject
        '1. POST（带个 name 只是为了兼容只认参数的实现）
        Try
            Dim Body As New JObject From {{"name", "dclversion"}, {"key", "dclversion"}}
            Dim Json As String = DstBot.RequestJson(PathGetVar, Body.ToString())
            Dim Vars As JObject = ParseVars(Json)
            If Vars IsNot Nothing AndAlso Vars.Count > 0 Then Return Vars
        Catch ex As Exception
            Logger.Info($"getvar(POST) 失败：{FriendlyError(ex.Message)}")
        End Try
        '2. GET
        Try
            Dim Vars As JObject = ParseVars(DstBot.RequestJson(PathGetVar, Nothing))
            If Vars IsNot Nothing Then Return Vars
        Catch ex As Exception
            Logger.Info($"getvar(GET) 失败：{FriendlyError(ex.Message)}")
        End Try
        Return New JObject
    End Function

    Private Function ParseVars(Json As String) As JObject
        If String.IsNullOrWhiteSpace(Json) Then Return Nothing
        Dim Trimmed As String = Json.Trim()
        If Not Trimmed.StartsWithF("{") Then Return Nothing
        Try
            Dim Obj As JObject = JObject.Parse(Trimmed)
            Dim Nested As JObject = TryCast(Obj("data"), JObject)
            If Nested IsNot Nothing AndAlso Nested.Count > 0 Then Return Nested
            Return Obj
        Catch ex As Exception
            Logger.Info("解析 getvar 返回失败：" & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 从变量表里挑一个值。键名允许少量拼写差异（服务端出现过 dclupgardinfo 这种漏字母的情况）。
    ''' </summary>
    Private Function PickVar(Vars As JObject, ParamArray Keys As String()) As String
        If Vars Is Nothing Then Return ""
        For Each Key In Keys
            Dim Prop As JToken = Vars(Key)
            If Prop IsNot Nothing AndAlso Prop.Type <> JTokenType.Object AndAlso Prop.Type <> JTokenType.Array Then
                Dim V As String = Prop.ToString()
                If V <> "" Then Return V
            End If
        Next
        '模糊匹配：忽略大小写，允许 1~2 个字符的差异
        For Each Item As JProperty In Vars.Properties()
            Dim PropName As String = Item.Name.ToLowerInvariant()
            For Each Key In Keys
                Dim Target As String = Key.ToLowerInvariant()
                If PropName = Target OrElse EditDistance(PropName, Target) <= 2 Then
                    Dim V As String = Item.Value.ToString()
                    If V <> "" Then Return V
                End If
            Next
        Next
        Return ""
    End Function

    ''' <summary>编辑距离，用于容忍键名拼写差异。</summary>
    Private Function EditDistance(A As String, B As String) As Integer
        Dim D(A.Length, B.Length) As Integer
        For i = 0 To A.Length
            D(i, 0) = i
        Next
        For j = 0 To B.Length
            D(0, j) = j
        Next
        For i = 1 To A.Length
            For j = 1 To B.Length
                Dim Cost As Integer = If(A(i - 1) = B(j - 1), 0, 1)
                D(i, j) = Math.Min(Math.Min(D(i - 1, j) + 1, D(i, j - 1) + 1), D(i - 1, j - 1) + Cost)
            Next
        Next
        Return D(A.Length, B.Length)
    End Function
#End Region

#Region "更新检查"
    ''' <summary>检查启动器更新（工作线程取数据，回调在 UI 线程）。</summary>
    Public Sub CheckLauncherUpdate(OnDone As Action(Of DstUpdateInfo))
        RunInThread(
        Sub()
            Dim Info As New DstUpdateInfo
            Try
                Dim Vars As JObject = FetchVars()
                Info.RemoteVersion = PickVar(Vars, "dclversion", "version")
                Info.Announce = PickVar(Vars, "dclupgradeinfo", "dclupgradinfo", "upgradeinfo", "upgradenote")
                Dim CriticalText As String = PickVar(Vars, "dclcritical", "critical", "important")
                Info.Critical = CriticalText.Trim().ToLower() = "1" OrElse CriticalText.Trim().ToLower() = "true"
                If Info.RemoteVersion = "" Then
                    Info.Err = "没有获取到版本号"
                Else
                    Dim Cmp As Integer = CompareVersion(Info.RemoteVersion, VersionBaseName)
                    Info.HasNew = Cmp > 0
                    Info.CompareOk = Cmp <> 0 OrElse Info.RemoteVersion = VersionBaseName
                End If
            Catch ex As Exception
                Info.Err = FriendlyError(ex.Message)
            End Try
            RunInUi(Sub() OnDone(Info))
        End Sub)
    End Sub

    ''' <summary>检查按钮的统一入口：结果用弹窗呈现，可选择直接下载更新。</summary>
    Public Sub CheckLauncherUpdateByButton()
        Hint("正在检查更新……", HintType.Blue)
        CheckLauncherUpdate(
        Sub(Info)
            If Info.Err <> "" Then
                MyMsgBox($"检查更新失败：{Info.Err}", "更新检查", "关闭", IsWarn:=True)
                Return
            End If
            Dim Text As String = $"当前版本：{VersionDisplay}" & vbCrLf &
                                 $"服务器版本：{Info.RemoteVersion}" & vbCrLf & vbCrLf &
                                 If(Info.Announce = "", "（服务器未提供更新说明）", Info.Announce)
            If Not Info.HasNew Then
                MyMsgBox(Text & vbCrLf & vbCrLf & "已经是最新版本。", "更新检查", "关闭")
                Return
            End If
            If MyMsgBox(Text, "发现新版本", "下载并安装", "稍后再说") = 1 Then
                DownloadLauncherUpdate(Info.RemoteVersion)
            End If
        End Sub)
    End Sub

    ''' <summary>
    ''' 每次启动时的自动检查。按设置 SystemSystemUpdate 决定行为：
    ''' 0 自动下载 / 1 提示 / 2 仅重大更新提示 / 3 关闭。
    ''' 检查失败的静默处理，不打扰用户。
    ''' </summary>
    Public Sub AutoCheckOnStartup()
        Dim Mode As Integer = Settings.Get(Of Integer)("SystemSystemUpdate")
        If Mode >= 3 Then
            Logger.Info("启动器更新：已按设置关闭自动检查")
            Return
        End If
        Logger.Info($"启动器更新：开始自动检查（模式 {Mode}）")
        CheckLauncherUpdate(
        Sub(Info)
            If Info.Err <> "" Then
                Logger.Info($"启动器更新：检查失败（{Info.Err}），本次跳过")
                Return
            End If
            If Not Info.HasNew Then
                Logger.Info($"启动器更新：已是最新（本地 {VersionBaseName}，服务器 {Info.RemoteVersion}）")
                Return
            End If
            Logger.Info($"启动器更新：发现新版本 {Info.RemoteVersion}（重大={Info.Critical}）")
            Select Case Mode
                Case 0 '自动下载
                    Hint($"发现新版本 {Info.RemoteVersion}，正在后台下载……", HintType.Blue)
                    DownloadLauncherUpdate(Info.RemoteVersion)
                Case 1 '提示
                    ShowPrompt(Info)
                Case 2 '仅重大更新
                    If Info.Critical Then
                        ShowPrompt(Info)
                    Else
                        Logger.Info("启动器更新：非重大更新，按设置不提示")
                    End If
            End Select
        End Sub)
    End Sub

    Private Sub ShowPrompt(Info As DstUpdateInfo)
        Dim Text As String = $"当前版本：{VersionDisplay}" & vbCrLf &
                             $"服务器版本：{Info.RemoteVersion}" & vbCrLf & vbCrLf &
                             If(Info.Announce = "", "（服务器未提供更新说明）", Info.Announce)
        If MyMsgBox(Text, "发现新版本", "下载并安装", "稍后再说") = 1 Then
            DownloadLauncherUpdate(Info.RemoteVersion)
        End If
    End Sub

#End Region

#Region "启动器自更新"

    ''' <summary>下载并安装启动器更新（索引固定为 dcl）。</summary>
    Public Sub DownloadLauncherUpdate(RemoteVersion As String)
        RunInThread(
        Sub()
            Try
                '下载接口直接回流文件（octet-stream），不是返回 JSON 地址
                Dim LocalFile As String = PathTemp & "Update\DST-" & RemoteVersion & ".bin"
                DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalFile))
                RunInUi(Sub() Hint("正在下载更新……", HintType.Blue))
                Dim Suggested As String = DstBot.DownloadToFile(PathDownload & "?index=dcl", LocalFile)
                If Not FileUtils.Exists(LocalFile) Then Throw New Exception("下载后未找到更新包")
                Dim Size As Long = New IO.FileInfo(LocalFile).Length
                Logger.Info($"更新包已下载：{Size} 字节，服务器给的文件名：{Suggested}")
                If Size = 0 Then Throw New Exception("更新包为空")
                RunInUi(Sub() InstallDownloadedUpdate(LocalFile, Suggested))
            Catch ex As Exception
                ex.ThrowIfCanceled()
                Logger.Warn(ex, "下载启动器更新失败")
                RunInUi(Sub() MyMsgBox($"下载更新失败：{FriendlyError(ex.Message)}", "更新失败", "关闭", IsWarn:=True))
            End Try
        End Sub)
    End Sub

    ''' <summary>把下载好的更新包装好，用 --update 参数交给新进程完成替换。</summary>
    Private Sub InstallDownloadedUpdate(Package As String, SuggestedName As String)
        Try
            Dim Root As String = PathTemp & "Update\Extract\"
            If DirectoryUtils.Exists(Root) Then DirectoryUtils.Delete(Root)
            DirectoryUtils.Create(Root)
            '按文件内容判断类型，不依赖扩展名
            Dim Kind As String = SniffPackage(Package)
            If Kind = "unknown" Then
                MyMsgBox($"下载到的文件不是有效的更新包（{New IO.FileInfo(Package).Length} 字节，服务器文件名：{If(SuggestedName = "", "未知", SuggestedName)}）。", "更新失败", "关闭", IsWarn:=True)
                Return
            End If
            Dim Extracted As String = ExtractOrCopy(Package, Root, Kind)
            Dim NewExe As String = FindExe(Extracted)
            If NewExe = "" Then
                MyMsgBox("更新包里没有找到启动器主程序，已取消更新。", "更新失败", "关闭", IsWarn:=True)
                Return
            End If
            Dim OldExe As String = PathExe
            Dim FileCount As Integer = DirectoryUtils.EnumerateFiles(Extracted, True).Count()
            Dim Text As String = $"更新包已就绪：{vbCrLf}主程序：{PathUtils.GetLastPart(NewExe)}{vbCrLf}共 {FileCount} 个文件{vbCrLf}{vbCrLf}" &
                                 $"现在重启并把启动器目录里的文件替换成新版本吗？{vbCrLf}（PCL、.minecraft 等用户数据不会覆盖）"
            If MyMsgBox(Text, "准备更新", "立即更新", "稍后") <> 1 Then Return
            '用新程序带 --update 启动，然后退出自己
            Dim Updater As Process = StartProcess(New ProcessStartInfo(NewExe,
                $"--update {Process.GetCurrentProcess.Id} ""{OldExe}"" ""{NewExe}"" 1") With {.UseShellExecute = True})
            Updater?.Dispose()
            FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
        Catch ex As Exception
            Logger.Warn(ex, "准备更新失败")
            MyMsgBox($"准备更新失败：{FriendlyError(ex.Message)}", "更新失败", "关闭", IsWarn:=True)
        End Try
    End Sub

    ''' <summary>按文件头判断更新包类型：zip / exe / rar / 未知。</summary>
    Friend Function SniffPackage(Package As String) As String
        Try
            Dim Head(3) As Byte
            Using Fs As New IO.FileStream(Package, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                If Fs.Read(Head, 0, 4) < 4 Then Return "unknown"
            End Using
            If Head(0) = &H50 AndAlso Head(1) = &H4B Then Return "zip"      ' PK.. = zip
            If Head(0) = &H4D AndAlso Head(1) = &H5A Then Return "exe"      ' MZ   = PE
            If Head(0) = &H52 AndAlso Head(1) = &H61 Then Return "rar"      ' Ra   = rar
        Catch ex As Exception
        End Try
        Return "unknown"
    End Function

    ''' <summary>解压更新包；不是压缩包就直接当 exe 复制。统一返回目录。</summary>
    Private Function ExtractOrCopy(Package As String, Root As String, Kind As String) As String
        If Kind = "exe" Then
            '单程序更新：把 exe 放进临时目录，后面按“目录”统一处理
            IO.File.Copy(Package, Root & "newlauncher.exe", True)
            Return Root
        End If
        Dim DestFile As String = Root & "package.zip"
        IO.File.Copy(Package, DestFile, True)
        Using Archive = FileUtils.OpenZip(DestFile)
            For Each Entry As IO.Compression.ZipArchiveEntry In Archive.Entries
                Dim Rel As String = Entry.FullName.Replace("/"c, "\"c)
                If Rel.EndsWith("\", StringComparison.Ordinal) Then Continue For
                Dim Full As String = Root & Rel
                DirectoryUtils.Create(PathUtils.RemoveLastPart(Full))
                Entry.ExtractToFile(Full, True)
            Next
        End Using
        Return Root
    End Function

    Private Function FindExe(Root As String) As String
        For Each F In DirectoryUtils.EnumerateFiles(Root, True)
            If PathUtils.GetExtension(F).ToLower() = "exe" Then Return F
        Next
        Return ""
    End Function

#End Region

End Module
