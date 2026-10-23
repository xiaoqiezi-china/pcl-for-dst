''' <summary>
''' 左下角的常驻进度提示。给没有任务卡片、又要跑一阵子的流程（例如收藏夹安装的前置分析）用。
''' 复用 PCL 自带的 <see cref="MyHint"/>，不另画控件。
''' </summary>
Public Module ModProgressHint

    ''' <summary>当前是否正在显示进度。</summary>
    Private Showing As Boolean

    ''' <summary>
    ''' 显示或更新进度。Percent 为 0~1，传负数表示只显示文字、不显示百分比。
    ''' 可在任意线程调用。
    ''' </summary>
    Public Sub Show(Text As String, Optional Percent As Double = -1)
        RunInUi(
        Sub()
            Try
                If FrmMain?.HintProgress Is Nothing Then Return
                Dim Tip As String = If(Percent >= 0, $"{Text}  {Math.Round(Percent * 100)}%", Text)
                FrmMain.HintProgress.Text = Tip
                If Showing Then Return
                Showing = True
                FrmMain.HintProgress.Visibility = Visibility.Visible
                '提示条也在左下角，把它顶上去，免得和进度条叠在一起
                FrmMain.HintProgress.Measure(New Size(Double.PositiveInfinity, Double.PositiveInfinity))
                FrmMain.PanHint.Margin = New Thickness(0, 0, 0, 20 + FrmMain.HintProgress.DesiredSize.Height + 6)
            Catch ex As Exception
                Logger.Warn(ex, "显示进度提示失败")
            End Try
        End Sub)
    End Sub

    ''' <summary>隐藏进度提示。可在任意线程调用。</summary>
    Public Sub Hide()
        RunInUi(
        Sub()
            Try
                If FrmMain?.HintProgress Is Nothing Then Return
                Showing = False
                FrmMain.HintProgress.Visibility = Visibility.Collapsed
                FrmMain.PanHint.Margin = New Thickness(0, 0, 0, 20)
            Catch ex As Exception
                Logger.Warn(ex, "隐藏进度提示失败")
            End Try
        End Sub)
    End Sub

End Module
