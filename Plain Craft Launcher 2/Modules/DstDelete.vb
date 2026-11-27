''' <summary>
''' 删除操作的后台执行。
''' <para>
''' 删除一个存档、一个版本目录动辄要处理成百上千个文件，走回收站时还要等 Shell 完成；
''' 直接写在事件处理里会把 UI 线程占住，窗口连重绘都做不了，用户看到的就是「卡死」。
''' 这里统一把它们丢到工作线程，删完再回 UI 线程通知调用方。
''' </para>
''' </summary>
Public Module DstDelete

    ''' <summary>
    ''' 在后台删除一个文件或文件夹，完成后回 UI 线程回调（参数为是否成功）。
    ''' <para>
    ''' 路径不存在视为成功；删除过程中抛出的异常记日志后回报失败，
    ''' 不会把异常甩到工作线程外面。
    ''' </para>
    ''' </summary>
    Public Sub DeleteAsync(Target As String, Optional ToRecycleBin As Boolean = False,
                           Optional OnDone As Action(Of Boolean) = Nothing)
        If String.IsNullOrWhiteSpace(Target) Then
            If OnDone IsNot Nothing Then OnDone(False)
            Return
        End If
        RunInThread(
        Sub()
            Dim Ok As Boolean = DeleteCore(Target, ToRecycleBin)
            If OnDone IsNot Nothing Then RunInUi(Sub() OnDone(Ok))
        End Sub)
    End Sub

    ''' <summary>
    ''' 在后台依次删除多个目标，全部处理完后回 UI 线程回调。
    ''' </summary>
    ''' <param name="OnDone">参数为成功删除的数量与失败的目标列表。</param>
    Public Sub DeleteBatchAsync(Targets As IEnumerable(Of String), Optional ToRecycleBin As Boolean = False,
                                Optional OnDone As Action(Of Integer, List(Of String)) = Nothing)
        Dim List As List(Of String) = If(Targets Is Nothing, New List(Of String), Targets.Where(Function(T) Not String.IsNullOrWhiteSpace(T)).ToList())
        If List.Count = 0 Then
            If OnDone IsNot Nothing Then OnDone(0, New List(Of String))
            Return
        End If
        RunInThread(
        Sub()
            Dim Done As Integer = 0
            Dim Failed As New List(Of String)
            For Each Target In List
                If DeleteCore(Target, ToRecycleBin) Then
                    Done += 1
                Else
                    Failed.Add(Target)
                End If
            Next
            If OnDone IsNot Nothing Then RunInUi(Sub() OnDone(Done, Failed))
        End Sub)
    End Sub

    ''' <summary>
    ''' 同步删除，供已经处在工作线程的代码调用。
    ''' 返回是否成功；路径不存在视为成功。
    ''' </summary>
    Public Function DeleteCore(Target As String, Optional ToRecycleBin As Boolean = False) As Boolean
        If String.IsNullOrWhiteSpace(Target) Then Return False
        Try
            If DirectoryUtils.Exists(Target) Then
                DirectoryUtils.Delete(Target, ToRecycleBin)
            ElseIf FileUtils.Exists(Target) Then
                FileUtils.Delete(Target, ToRecycleBin)
            End If
            Return True
        Catch ex As OperationCanceledException
            Logger.Warn(ex, $"删除被主动取消：{Target}")
            Return False
        Catch ex As Exception
            Logger.Warn(ex, $"删除失败：{Target}")
            Return False
        End Try
    End Function

End Module
