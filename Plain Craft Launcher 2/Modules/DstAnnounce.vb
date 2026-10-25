Imports System.Text.RegularExpressions

''' <summary>
''' 版本更新公告。
''' <para>
''' 内容来自 GitHub Release 的正文（即发布时填的更新说明），
''' 不再从预设服务器取，也不在本地写死文案——那样一升级就对不上实际发布内容。
''' </para>
''' <para>
''' 每个版本只弹一次，点过之后记住版本号。取不到服务器内容时静默跳过，
''' 宁可这次不弹，也不要弹一段过期文案误导用户。
''' </para>
''' </summary>
Public Module DstAnnounce

    Private Showing As Boolean = False

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

        '公告正文要联网取，放到工作线程，取到再回 UI 弹窗
        RunInThread(
        Sub()
            Dim Content As String = ""
            Dim ErrText As String = ""
            Try
                DstUpdate.CheckLauncherUpdate(
                Sub(Info)
                    '这里只借用取数流程，不关心是否有新版本
                    '优先用 GitHub Release 的正文；没有才退回预设服务器的公告
                    '公告只认 GitHub Release 的正文；拿不到就这次不弹，
                    '绝不能退回预设服务器的公告——那份文案和实际发布内容早就对不上了
                    Content = Info.Notes
                    ErrText = Info.Err
                    RunInUi(
                    Sub()
                        Try
                            If Content = "" Then
                                '服务器没给公告：静默跳过，仍然记下版本号避免每次启动都去请求
                                Logger.Info("更新公告：没有可用的更新说明，跳过" &
                                            If(ErrText <> "", "（" & ErrText & "）", ""))
                                Settings.Set("DstLastAnnounceVersion", VersionBaseName)
                                Return
                            End If
                            MyMsgBox(Content, "更新公告 V" & VersionBaseName, "我知道了")
                            Settings.Set("DstLastAnnounceVersion", VersionBaseName)
                        Catch ex As Exception
                            Logger.Warn(ex, "显示更新公告失败")
                        Finally
                            Showing = False
                        End Try
                    End Sub)
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "获取更新公告失败")
                RunInUi(Sub() Showing = False)
            End Try
        End Sub)
    End Sub

End Module
