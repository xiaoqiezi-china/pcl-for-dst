''' <summary>
''' 群聊 → 查看公告。列出该群所有公告，最新的排最上面。
''' </summary>
Public Class PageGroupReport

    ''' <summary>离线模式：整页只留一个上线按钮。</summary>
    Public Sub ApplyOfflineUi()
        If PanOffline Is Nothing Then Return
        PanOffline.Visibility = If(DstBot.IsOffline, Visibility.Visible, Visibility.Collapsed)
        PanBack.Visibility = If(DstBot.IsOffline, Visibility.Collapsed, Visibility.Visible)
    End Sub

    Private Sub BtnGoOnlineReport_Click(sender As Object, e As EventArgs) Handles BtnGoOnlineReport.Click
        If BtnGoOnlineReport.IsEnabled = False Then Return
        BtnGoOnlineReport.IsEnabled = False
        BtnGoOnlineReport.Text = "正在连接"
        RunInThread(
        Sub()
            Dim Ok As Boolean = DstBot.GoOnline()
            RunInUi(
            Sub()
                BtnGoOnlineReport.IsEnabled = True
                BtnGoOnlineReport.Text = "上线"
                If Ok Then
                    Hint("已回到在线模式", HintType.Green, False)
                    ApplyOfflineUi()
                    If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshDependentViewsAfterModeChange()
                Else
                    Hint("远程服务不可用", HintType.Red)
                End If
            End Sub)
        End Sub)
    End Sub
    Private Sub PageGroupReport_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ApplyOfflineUi()
        PanBack.ScrollToHome()
        Refresh()
        DstBot.FetchGroups(OnDone:=AddressOf Refresh)
    End Sub

    Private Sub BtnReportRefresh_Click(sender As Object, e As EventArgs) Handles BtnReportRefresh.Click
        DstBot.FetchGroups(Force:=True, OnDone:=AddressOf Refresh)
    End Sub

    Public Sub Refresh()
        Dim G As DstGroup = DstBot.SelectedGroup
        PanReports.Children.Clear()
        If G Is Nothing Then
            LabReportTitle.Text = "还没有获取到群信息，点右侧刷新试试"
            Return
        End If
        LabReportTitle.Text = $"{G.GroupName}（{G.GroupId}）· 共 {G.ReportCount} 条公告"
        If G.ReportCount = 0 Then
            PanReports.Children.Add(New TextBlock With {
                .Text = "这个群还没有公告", .FontSize = 13, .Opacity = 0.6,
                .Margin = New Thickness(0, 8, 0, 0)
            })
            Return
        End If
        For i = 0 To G.Reports.Count - 1
            PanReports.Children.Add(BuildReportRow(G.Reports(i), i))
        Next
    End Sub

    Private Function BuildReportRow(Item As DstReport, Index As Integer) As FrameworkElement
        Dim Card As New Border With {
            .Margin = New Thickness(0, 0, 0, 8),
            .Padding = New Thickness(14, 10, 14, 12),
            .CornerRadius = New CornerRadius(6),
            .Background = New SolidColorBrush(If(Index = 0, BackColorMain, BackColorSub))
        }
        Dim Stack As New StackPanel
        Dim Head As New TextBlock With {
            .Text = If(Index = 0, "最新公告", $"第 {Index + 1} 条") & If(Item.TimeText = "", "", "　" & Item.TimeText),
            .FontSize = 12, .Opacity = 0.6, .Margin = New Thickness(0, 0, 0, 6)
        }
        Stack.Children.Add(Head)
        '公告正文用只读 TextBox：外观与 TextBlock 一致，但支持鼠标划选与 Ctrl+C
        Dim Body As New TextBox With {
            .Text = Item.Content,
            .FontSize = 13,
            .TextWrapping = TextWrapping.Wrap,
            .IsReadOnly = True,
            .BorderThickness = New Thickness(0),
            .Background = Brushes.Transparent,
            .Padding = New Thickness(0),
            .IsTabStop = False,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        }
        Body.SetResourceReference(TextBox.ForegroundProperty, "ColorBrush1")
        Stack.Children.Add(Body)
        Card.Child = Stack
        Return Card
    End Function

End Class
