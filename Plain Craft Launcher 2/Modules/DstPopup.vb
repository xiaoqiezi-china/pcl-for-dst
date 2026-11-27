''' <summary>
''' 弹窗模式。
''' <para>
''' 由 设置 → 个性化 → 基础 里的下拉框控制，写入设置项 <c>DstPopupMode</c>：
''' 0 = 无弹窗（只屏蔽纯通知，确认框与输入框照常），1 = 免打扰（不弹群公告），2 = 全部。
''' </para>
''' <para>
''' 屏蔽是在 <see cref="MyMsgBox"/> 这一层做的，因为界面上所有弹窗最终都走这里。
''' 只屏蔽单按钮的通知类：这类弹窗点掉就完事，跳过不会改变任何结果。
''' 带多个按钮的确认框、输入框、选择框一律照常显示——它们要用户拿主意，
''' 替用户点下去等于替他删了东西，或者替他把操作取消了。
''' </para>
''' </summary>
Public Module DstPopup

    ''' <summary>无弹窗：屏蔽纯通知。</summary>
    Public Const ModeNone As Integer = 0
    ''' <summary>免打扰：只屏蔽群公告弹窗。</summary>
    Public Const ModeQuiet As Integer = 1
    ''' <summary>全部：不屏蔽。</summary>
    Public Const ModeAll As Integer = 2

    ''' <summary>
    ''' 当前模式。设置项本身带缓存，这里直接读，改完立刻生效。
    ''' 读失败或值越界时按「免打扰」处理。
    ''' </summary>
    Public ReadOnly Property CurrentMode As Integer
        Get
            Try
                Dim Value As Integer = CInt(Settings.Get(Of Integer)("DstPopupMode"))
                If Value < ModeNone OrElse Value > ModeAll Then Return ModeQuiet
                Return Value
            Catch ex As Exception
                Logger.Warn(ex, "读取弹窗模式失败，按免打扰处理")
                Return ModeQuiet
            End Try
        End Get
    End Property

    ''' <summary>是否屏蔽纯通知类弹窗。确认框、输入框不受影响。</summary>
    Public ReadOnly Property IsSilent As Boolean
        Get
            Return CurrentMode = ModeNone
        End Get
    End Property

    ''' <summary>是否允许弹群公告。</summary>
    Public ReadOnly Property AllowsGroupReport As Boolean
        Get
            Return CurrentMode = ModeAll
        End Get
    End Property

    ''' <summary>
    ''' 一条弹窗被屏蔽时记日志。标题和正文都可能很长，只留前 80 个字。
    ''' </summary>
    Public Sub LogSuppressed(Title As String, Caption As String)
        Dim Brief As String = If(Caption, "")
        Brief = Brief.Replace(vbCrLf, " ").Replace(vbLf, " ").Replace(vbCr, " ").Trim()
        If Brief.Length > 80 Then Brief = Brief.Substring(0, 80) & "…"
        Try
            '启动早期 Logger 可能还没装配好，不能因为记日志把弹窗流程带崩
            Logger.Info($"弹窗模式为无弹窗，已跳过弹窗「{If(Title, "")}」：{Brief}")
        Catch
        End Try
    End Sub

End Module
