''' <summary>
''' 聊天消息行宿主。配合 VirtualizingStackPanel 使用：只有真正滚到可见范围才会创建内容。
''' </summary>
Friend Class DstChatRow
    Inherits ContentControl

    ''' <summary>由页面注入：把一条消息渲染成控件。</summary>
    Friend Shared Builder As Func(Of DstBot.DstMessage, Boolean, FrameworkElement)

    Private Built As Boolean

    Public Sub New()
        AddHandler Me.DataContextChanged, Sub() BuildContent()
        AddHandler Me.Loaded, Sub() BuildContent()
    End Sub

    Private Sub BuildContent()
        If Built Then Return
        Dim Msg As DstBot.DstMessage = TryCast(DataContext, DstBot.DstMessage)
        If Msg Is Nothing OrElse Builder Is Nothing Then Return
        Built = True
        Try
            Content = Builder(Msg, DstBot.IsSelfQq(Msg.UserQq))
        Catch ex As Exception
            Logger.Warn(ex, "渲染聊天消息失败")
        End Try
    End Sub

End Class
