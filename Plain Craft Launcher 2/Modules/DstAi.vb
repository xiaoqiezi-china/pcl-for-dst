''' <summary>
''' 把内容交给 Deepseek CE 的统一入口。
''' <para>
''' 界面上所有「询问 AI」「发送给 AI」最终都走这里：拼参数、判断装没装、
''' 按设置决定是「只填充」还是「填充并发送」都在这一处完成，
''' 各个调用点只负责说清楚「发什么文字、带哪些文件」。
''' </para>
''' </summary>
Friend Module DstAi

    ''' <summary>调用方式：只把内容填进输入框，还是连发送一起做。</summary>
    Public Enum InvokeMode
        ''' <summary>仅填充，用户自己决定发不发。</summary>
        FillOnly = 0
        ''' <summary>填充并发送。</summary>
        FillAndSend = 1
    End Enum

    ''' <summary>
    ''' AI 功能总开关（百宝箱里的「是否启用 DeepseekCE」）。
    ''' 关掉时所有入口都只提示、不动作，避免用户莫名其妙被拉起客户端。
    ''' </summary>
    Public ReadOnly Property IsEnabled As Boolean
        Get
            Try
                Return Settings.Get(Of Boolean)("DstAiEnabled")
            Catch ex As Exception
                Logger.Warn(ex, "读取 AI 总开关失败")
                Return False
            End Try
        End Get
    End Property

    ''' <summary>当前设置里的调用方式。</summary>
    Public ReadOnly Property CurrentMode As InvokeMode
        Get
            Try
                Return If(Settings.Get(Of Integer)("DstAiInvokeMode") = 1, InvokeMode.FillAndSend, InvokeMode.FillOnly)
            Catch ex As Exception
                Logger.Warn(ex, "读取 AI 调用方式失败")
                Return InvokeMode.FillOnly
            End Try
        End Get
    End Property

    ''' <summary>
    ''' 当前正在玩的版本，用于报错文案。
    ''' <para>
    ''' 优先用选中的版本名（玩家视角的「我玩的版本」）；
    ''' 没有选中版本时退回启动器自身的版本信息，保证文案里总有东西。
    ''' </para>
    ''' </summary>
    Public ReadOnly Property GameVersionText As String
        Get
            Try
                If McInstanceSelected IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(McInstanceSelected.Name) Then
                    Return McInstanceSelected.Name
                End If
            Catch ex As Exception
                Logger.Warn(ex, "读取当前版本名失败")
            End Try
            Return VersionDisplay
        End Get
    End Property

    ''' <summary>
    ''' 把内容交给 Deepseek CE。
    ''' </summary>
    ''' <param name="Text">要发的文本，可以为空。</param>
    ''' <param name="Files">要作为附件发过去的文件，可以为空。</param>
    ''' <param name="Search">是否启用网络搜索。传 Nothing 表示不传该开关，由客户端自己决定。</param>
    ''' <param name="Thinking">是否启用深度思考。传 Nothing 表示不传该开关，由客户端自己决定。</param>
    ''' <returns>是否成功递出去。</returns>
    Public Function Ask(Text As String, Optional Files As IEnumerable(Of String) = Nothing,
                        Optional Search As Boolean? = True, Optional Thinking As Boolean? = True) As Boolean
        If Not IsEnabled Then
            PromptEnable()
            Return False
        End If
        If Not DstDeepseekCe.IsInstalled Then
            PromptInstall()
            Return False
        End If

        Dim Request As New DstDeepseekCe.LaunchRequest With {
            .Text = If(Text, ""),
            .Search = Search,
            .Thinking = Thinking,
            .Send = CurrentMode = InvokeMode.FillAndSend}
        If Files IsNot Nothing Then
            For Each FilePath In Files
                If Not String.IsNullOrWhiteSpace(FilePath) AndAlso FileUtils.Exists(FilePath) Then Request.Files.Add(FilePath)
            Next
        End If

        Try
            DstDeepseekCe.Launch(DstDeepseekCe.BuildArguments(Request))
            Hint(If(Request.Send, "已发送给 Deepseek CE", "已填入 Deepseek CE，请在客户端里确认后发送"), HintType.Green, False)
            Return True
        Catch ex As Exception
            Logger.Error(ex, "调用 Deepseek CE 失败", LogBehavior.Alert)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 只把文件递给客户端，不带文字、也不动搜索与思考开关。
    ''' <para>
    ''' 图片这类请求用户只想让客户端「看到这张图」，
    ''' 顺带改掉客户端的模式并不是他要的，所以这里两个开关都不传。
    ''' </para>
    ''' </summary>
    Public Function AskFilesOnly(Files As IEnumerable(Of String)) As Boolean
        Return Ask("", Files, Search:=Nothing, Thinking:=Nothing)
    End Function

    ''' <summary>
    ''' 跳到百宝箱的 DeepseekCE 卡片，并把它滚进视野。
    ''' <para>
    ''' 页面是首次进入时才创建的，卡片也要等这一轮布局，所以滚动放到后台优先级里做。
    ''' </para>
    ''' </summary>
    Public Sub GotoSettingPage()
        RunInUi(
        Sub()
            FrmMain.PageChange(FormMain.PageType.Other, FormMain.PageSubType.OtherTest)
            FrmMain.Dispatcher.InvokeAsync(
            New Action(
            Sub()
                Try
                    FrmOtherTest?.ScrollToDsc()
                Catch ex As Exception
                    Logger.Warn(ex, "定位 DeepseekCE 卡片失败")
                End Try
            End Sub), System.Windows.Threading.DispatcherPriority.Background)
        End Sub)
    End Sub

    ''' <summary>总开关没开时引导去百宝箱打开。</summary>
    Private Sub PromptEnable()
        RunInUi(
        Sub()
            If MyMsgBox("AI 功能还没有启用。" & vbCrLf &
                        "到百宝箱的 DeepseekCE 卡片里勾选「是否启用 DeepseekCE」即可使用。",
                        "AI 功能未启用", "去启用", "取消") = 1 Then
                GotoSettingPage()
            End If
        End Sub)
    End Sub

    ''' <summary>没装客户端时引导去百宝箱装。</summary>
    Private Sub PromptInstall()
        RunInUi(
        Sub()
            If MyMsgBox("当前未安装 DeepseekCE，无法调用 AI。" & vbCrLf &
                        "百宝箱里可以一键安装，装好之后这些按钮就能用了。",
                        "未安装 DeepseekCE", "去安装", "取消") = 1 Then
                GotoSettingPage()
            End If
        End Sub)
    End Sub

End Module
