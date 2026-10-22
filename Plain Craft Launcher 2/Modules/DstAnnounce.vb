Imports System.Text.RegularExpressions

''' <summary>
''' 版本更新公告。
'''
''' 文案统一从 GitHub 发布页取：启动时请求 releases 接口，把该版本 Release 的正文当作公告内容。
''' 取不到（没配仓库、断网、限流、该版本没单独发 Release）时，回落到本文件里写死的本地文案。
''' 每个版本只弹一次，点过之后记住版本号。
''' </summary>
Public Module DstAnnounce

    ''' <summary>
    ''' 公告来源仓库。填上 owner/repo 后就会去发布页取正文；留空则只用本地文案。
    ''' 例：GitHubOwner = "someone"，GitHubRepo = "PCL"。
    ''' </summary>
    Private Const GitHubOwner As String = ""
    Private Const GitHubRepo As String = ""

    Private Showing As Boolean = False

    ''' <summary>是否已配置远程公告来源。</summary>
    Private ReadOnly Property RemoteEnabled As Boolean
        Get
            Return GitHubOwner.Trim() <> "" AndAlso GitHubRepo.Trim() <> ""
        End Get
    End Property

    ''' <summary>取某个版本的本地文案；没有就返回空串。</summary>
    Private Function ContentOf(Version As String) As String
        Select Case Version
            Case "0.2.0"
                Return "1. 对启动器的性能进行了优化。" & vbCrLf &
                       "2. 增强了光影管理" & vbCrLf &
                       "3. 移除了 Trop"
            Case "0.1.9"
                Return "一次大的测试版本更新。" & vbCrLf & vbCrLf &
                       "1. 优化了部分 UI" & vbCrLf &
                       "2. 支持在（设置 → 机器人）中关闭线上功能" & vbCrLf &
                       "3. 添加了资源包、光影包的管理" & vbCrLf &
                       "4. 添加了强大的存档管理工具，包括玩家 NBT 编辑、存档 NBT 编辑、地图渲染。在未来还会添加更为强大的区块编辑功能"
            Case "0.1.5"
                Return "1. 在 关于 → 百宝箱 中添加了音乐播放器。在设置中你可以绑定自己的网易云账号，并享受云上音乐服务。" & vbCrLf & vbCrLf &
                       "2. 修复了部分显示错误"
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>本机是否已经看过当前版本的公告。</summary>
    Public ReadOnly Property HasSeen As Boolean
        Get
            Return Settings.Get(Of String)("DstLastAnnounceVersion") = VersionBaseName
        End Get
    End Property

    ''' <summary>当前版本有公告且没看过就弹一次。必须在 UI 线程调用。</summary>
    Public Sub ShowIfNeeded()
        If HasSeen OrElse Showing Then Return
        Showing = True

        If Not RemoteEnabled Then
            '没配仓库：直接用本地文案，不产生任何网络请求
            Try
                ShowLocal()
            Finally
                Showing = False
            End Try
            Return
        End If

        '取远端文案会走网络，放到工作线程，取到再回 UI 弹窗
        RunInThread(
        Sub()
            Dim Title As String = "更新公告 V" & VersionBaseName
            Dim Content As String = ""
            Try
                Content = FetchReleaseBody(VersionBaseName, Title)
            Catch ex As Exception
                Logger.Warn(ex, "从 GitHub 获取更新公告失败")
            End Try
            RunInUi(
            Sub()
                Try
                    If Content = "" Then
                        '远端没取到，退回本地文案
                        Content = ContentOf(VersionBaseName)
                        Title = "更新公告 V" & VersionBaseName
                    End If
                    If Content = "" Then Return
                    MyMsgBox(Content, Title, "我知道了")
                    Settings.Set("DstLastAnnounceVersion", VersionBaseName)
                Catch ex As Exception
                    Logger.Warn(ex, "显示更新公告失败")
                Finally
                    Showing = False
                End Try
            End Sub)
        End Sub)
    End Sub

    ''' <summary>只弹本地文案。</summary>
    Private Sub ShowLocal()
        Dim Content As String = ContentOf(VersionBaseName)
        If Content = "" Then Return
        Try
            MyMsgBox(Content, "更新公告 V" & VersionBaseName, "我知道了")
            Settings.Set("DstLastAnnounceVersion", VersionBaseName)
        Catch ex As Exception
            Logger.Warn(ex, "显示更新公告失败")
        End Try
    End Sub

#Region "GitHub 发布页"

    ''' <summary>
    ''' 取当前版本的公告正文。
    ''' 先按 tag 找对应版本（v0.2.0 / 0.2.0 都试一次），找不到就退而取最新一条 Release。
    ''' Title 会被改成 Release 的标题（如果有）。
    ''' </summary>
    Private Function FetchReleaseBody(Version As String, ByRef Title As String) As String
        Dim Body As String = TryFetchRelease("tags/v" & Version, Title)
        If Body <> "" Then Return Body
        Body = TryFetchRelease("tags/" & Version, Title)
        If Body <> "" Then Return Body
        Return TryFetchRelease("latest", Title)
    End Function

    ''' <summary>试一个 Release 地址；该地址不存在（404）不能影响后续回落，所以在这里吞掉异常。</summary>
    Private Function TryFetchRelease(Tail As String, ByRef Title As String) As String
        Try
            Return FetchRelease(Tail, Title)
        Catch ex As Exception
            Logger.Info("公告源未命中，继续回退：" & Tail)
            Return ""
        End Try
    End Function

    ''' <summary>请求某个 Release，返回处理过的正文；失败或没有正文时返回空串。</summary>
    Private Function FetchRelease(Tail As String, ByRef Title As String) As String
        Dim Url As String = "https://api.github.com/repos/" & GitHubOwner.Trim() & "/" & GitHubRepo.Trim() & "/releases/" & Tail
        Dim Hdr(0, 1) As String
        Hdr(0, 0) = "Accept"
        Hdr(0, 1) = "application/vnd.github+json"
        Logger.Info("获取更新公告：" & Url)
        Dim Raw As String = NetRequestByClient(Url, HttpMethod.Get, Headers:=Hdr, Timeout:=8000, MakeLog:=False)
        If String.IsNullOrWhiteSpace(Raw) Then Return ""
        Dim Obj As JObject = JObject.Parse(Raw)
        Dim Name As String = If(Obj("name"), "").ToString().Trim()
        If Name = "" Then Name = If(Obj("tag_name"), "").ToString().Trim()
        If Name <> "" Then Title = "更新公告 " & Name
        Return NormalizeMarkdown(If(Obj("body"), "").ToString())
    End Function

    ''' <summary>
    ''' 把 Release 正文里的 Markdown 处理成适合弹窗的纯文本。
    ''' 不做完整解析，只去掉常见的标记符号与图片，保留分行与列表。
    ''' </summary>
    Private Function NormalizeMarkdown(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return ""
        Dim Lines As String() = Text.Replace(vbCrLf, vbLf).Split(ChrW(10))
        Dim Sb As New Text.StringBuilder
        For Each RawLine In Lines
            Dim Line As String = RawLine.TrimEnd()
            If Line.TrimStart().StartsWith("<!--") Then Continue For
            If Line.Contains("<img") Then Continue For
            Line = Regex.Replace(Line, "^\s{0,3}#{1,6}\s*", "")
            If Line.Trim() = "---" OrElse Line.Trim() = "***" Then Continue For
            Line = Regex.Replace(Line, "^\s*>\s?", "")
            Line = Line.Replace("**", "").Replace("__", "").Replace("~~", "")
            Line = Line.Replace(ChrW(96).ToString(), "")
            Line = Regex.Replace(Line, "!?\[([^\]]*)\]\(([^)]+)\)",
                                 Function(M) If(M.Groups(1).Value = "", M.Groups(2).Value,
                                                 M.Groups(1).Value & " (" & M.Groups(2).Value & ")"))
            If Line.Trim() <> "" OrElse Sb.Length > 0 Then Sb.AppendLine(Line.TrimEnd())
        Next
        Return Sb.ToString().Trim()
    End Function

#End Region

End Module
