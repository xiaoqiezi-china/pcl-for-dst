''' <summary>
''' 版本更新公告。每个版本第一次启动时弹一次，点过之后记住版本号，同版本不再打扰。
''' 新版本只要在 ContentOf 里加一个 Case 即可。
''' </summary>
Public Module DstAnnounce

    Private Showing As Boolean = False

    ''' <summary>取某个版本的公告内容；没有就返回空串。</summary>
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
        Dim Content As String = ContentOf(VersionBaseName)
        If Content = "" Then Return
        If HasSeen OrElse Showing Then Return
        Showing = True
        Try
            MyMsgBox(Content, "更新公告 V" & VersionBaseName, "我知道了")
            Settings.Set("DstLastAnnounceVersion", VersionBaseName)
        Catch ex As Exception
            Logger.Warn(ex, "显示更新公告失败")
        Finally
            Showing = False
        End Try
    End Sub

End Module
