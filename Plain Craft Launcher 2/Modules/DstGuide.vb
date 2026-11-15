''' <summary>
''' 首次启动的教程引导。
''' <para>
''' 只弹一次：本机点过「知道了」或「打开教程」之后就写进配置，之后不再打扰。
''' 重置配置或换机器会重新出现。
''' </para>
''' </summary>
Public Module DstGuide

    ''' <summary>教程站地址。所有引导都指向这里，换地址只需改这一处。</summary>
    Public Const GuideUrl As String = "https://xiaoqiezi-china.github.io/dpcl/index.html"

    Private Showing As Boolean = False

    ''' <summary>本机是否已经看过教程引导。</summary>
    Public ReadOnly Property HasSeen As Boolean
        Get
            Return Settings.Get(Of Boolean)("DstGuideSeen")
        End Get
    End Property

    ''' <summary>没看过就弹一次。必须在 UI 线程调用。</summary>
    Public Sub ShowIfNeeded()
        If HasSeen OrElse Showing Then Return
        Showing = True
        Try
            Dim Text As String =
                "欢迎使用 DPCL！" & vbCrLf & vbCrLf &
                "第一次用的话，建议先花两分钟看一遍教程：" & vbCrLf &
                "从选版本、装整合包，到登录、联机、装 Mod，" & vbCrLf &
                "常踩的坑基本都写在那里了。" & vbCrLf & vbCrLf &
                "之后也可以在「更多 → 帮助 → 指南」里随时打开。"
            '按钮 1 = 知道了，按钮 2 = 打开教程
            Dim Choice As Integer = MyMsgBox(Text, "先看看教程？", "知道了", "打开教程")
            Settings.Set("DstGuideSeen", True)
            If Choice = 2 Then OpenGuide()
        Catch ex As Exception
            Logger.Warn(ex, "显示教程引导失败")
        Finally
            Showing = False
        End Try
    End Sub

    ''' <summary>打开教程网页。事件与按钮共用这一个入口。</summary>
    Public Sub OpenGuide()
        Try
            OpenWebsite(GuideUrl)
        Catch ex As Exception
            Logger.Warn(ex, "打开教程页面失败")
        End Try
    End Sub

End Module
