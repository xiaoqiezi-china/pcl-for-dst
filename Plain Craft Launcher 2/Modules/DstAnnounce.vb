Imports System.Text.RegularExpressions

''' <summary>
''' 版本更新公告。
''' <para>
''' 内容一律来自服务器（与「检查更新」同一个接口 dclvar 的 dclupgradeinfo 字段），
''' 不再在本地写死文案——本地文案一升级就会和实际发布内容对不上。
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
                    '这里只是复用同一个接口取公告正文，不关心是否有新版本
                    Content = If(Info.Announce, "").Trim()
                    ErrText = Info.Err
                    RunInUi(
                    Sub()
                        Try
                            If Content = "" Then
                                '服务器没给公告：静默跳过，仍然记下版本号避免每次启动都去请求
                                Logger.Info("更新公告：服务器未提供内容，跳过" &
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
