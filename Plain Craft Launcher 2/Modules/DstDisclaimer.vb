''' <summary>
''' 首次启动的免责声明。本机没点过「我知道了」就一直弹。
''' 点过之后写进配置，之后不再打扰；重置配置或换机器会重新出现。
''' </summary>
Public Module DstDisclaimer

    Public Const DisclaimerText As String =
        "本启动器基于 PCL 二次修改制作。请勿用于商用，请尊重原作者版权。" & vbCrLf & vbCrLf &
        "本启动器与帝释天 QQ 机器人进行联动。连接机器人服务才可以获得云功能。" & vbCrLf & vbCrLf &
        "您可以在设置中一键关闭大部分改动，使其作为一个定制化主题的完整版 PCL 使用。"

    Private Showing As Boolean = False

    ''' <summary>本机是否已经确认过免责声明。</summary>
    Public ReadOnly Property HasAgreed As Boolean
        Get
            Return Settings.Get(Of Boolean)("DstDisclaimerAgreed")
        End Get
    End Property

    ''' <summary>没确认过就弹一次。必须在 UI 线程调用。</summary>
    Public Sub ShowIfNeeded()
        If HasAgreed OrElse Showing Then Return
        Showing = True
        Try
            MyMsgBox(DisclaimerText, "免责声明", "我知道了")
            Settings.Set("DstDisclaimerAgreed", True)
        Catch ex As Exception
            Logger.Warn(ex, "显示免责声明失败")
        Finally
            Showing = False
        End Try
    End Sub

    ''' <summary>重新查看（设置里点按钮时用）。</summary>
    Public Sub ShowAlways()
        MyMsgBox(DisclaimerText, "免责声明", "关闭")
    End Sub

End Module