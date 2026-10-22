''' <summary>
''' 设置 - 帝释天机器人。预设服务信息不在此展示，只说明状态；要改服务走「自定义服务」并保存。
''' </summary>
Public Class PageSetupDst

    Private Sub PageSetupDst_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        Static Reloaded As Boolean = False
        If Reloaded Then
            RefreshAll()
            Return
        End If
        Reloaded = True
        RefreshAll()
    End Sub

    Private Sub RefreshAll()
        '自定义服务的输入框只在勾选后才写入设置，未勾选时仅回显已保存的值
        CheckDstUseCustom.Checked = DstBot.UseCustom
        TextDstApi.Text = Settings.Get(Of String)("DstBotApiUrl")
        TextDstToken.Text = Settings.Get(Of String)("DstBotToken")
        RefreshState()
        RefreshGroupList()
        RefreshHomeOrder()
        RefreshCacheInfo()
        DstBot.FetchGroups(OnDone:=AddressOf RefreshGroupList)
    End Sub

#Region "本地数据"

    Private Sub RefreshCacheInfo()
        If LabCacheInfo Is Nothing Then Return
        Dim Size As Long = DstBot.ChatCacheSize()
        Dim Groups As Integer = DstBot.ChatCacheGroupCount()
        Dim AvatarSize As Long = DstBot.AvatarCacheSize()
        LabCacheInfo.Text = $"聊天记录：{Groups} 个群 {FormatSize(Size)}　·　头像缓存 {FormatSize(AvatarSize)}"
    End Sub

    Private Function FormatSize(Bytes As Long) As String
        If Bytes < 1024 Then Return Bytes & " B"
        If Bytes < 1024 * 1024 Then Return (Bytes / 1024).ToString("F1") & " KB"
        Return (Bytes / 1024 / 1024).ToString("F2") & " MB"
    End Function

    Private Sub BtnCacheClear_Click(sender As Object, e As EventArgs) Handles BtnCacheClear.Click
        If DstBot.ChatCacheGroupCount() = 0 Then
            Hint("没有可清理的缓存", HintType.Blue)
            Return
        End If
        If MyMsgBox("确定清空聊天记录与头像缓存吗？清理后需要重新从服务器拉取。", "清理缓存", "清理", "取消", IsWarn:=True) <> 1 Then Return
        DstBot.ClearChatCache()
        DstBot.ClearAvatarCache()
        RefreshCacheInfo()
    End Sub

    Private Sub BtnCacheFolder_Click(sender As Object, e As EventArgs) Handles BtnCacheFolder.Click
        Try
            DirectoryUtils.Create(DstBot.ChatCacheFolder)
            OpenExplorer(DstBot.ChatCacheFolder)
        Catch ex As Exception
            Hint("打开目录失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region

#Region "主页展示顺序"

    Private Sub RefreshHomeOrder()
        If PanHomeOrder Is Nothing Then Return
        PanHomeOrder.Children.Clear()
        Dim Order As List(Of String) = DstBot.HomeOrder()
        For i = 0 To Order.Count - 1
            PanHomeOrder.Children.Add(BuildHomeOrderRow(Order(i), i, Order.Count))
        Next
    End Sub

    Private Function BuildHomeOrderRow(Key As String, Index As Integer, Total As Integer) As FrameworkElement
        Dim Row As New Grid With {.Height = 34, .Margin = New Thickness(0, 0, 0, 4)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim Name As New TextBlock With {
            .Text = $"{Index + 1}. {DstBot.HomeKeyName(Key)}",
            .VerticalAlignment = VerticalAlignment.Center, .FontSize = 13
        }
        Row.Children.Add(Name)

        Dim Check As New MyCheckBox With {
            .Text = "显示", .Checked = DstBot.HomeShow(Key),
            .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 12, 0)
        }
        AddHandler Check.Change, Sub() DstBot.SetHomeShow(Key, Check.Checked)
        Grid.SetColumn(Check, 1)
        Row.Children.Add(Check)

        Dim Up As New MyButton With {.Text = "↑", .Width = 40, .Height = 28, .Margin = New Thickness(0, 0, 6, 0), .IsEnabled = Index > 0}
        AddHandler Up.Click, Sub() MoveHomeKey(Index, -1)
        Grid.SetColumn(Up, 2)
        Row.Children.Add(Up)

        Dim Down As New MyButton With {.Text = "↓", .Width = 40, .Height = 28, .IsEnabled = Index < Total - 1}
        AddHandler Down.Click, Sub() MoveHomeKey(Index, 1)
        Grid.SetColumn(Down, 3)
        Row.Children.Add(Down)

        Return Row
    End Function

    Private Sub MoveHomeKey(Index As Integer, Delta As Integer)
        Dim Order As List(Of String) = DstBot.HomeOrder()
        Dim Target As Integer = Index + Delta
        If Target < 0 OrElse Target > Order.Count - 1 Then Return
        Dim Tmp As String = Order(Index)
        Order(Index) = Order(Target)
        Order(Target) = Tmp
        DstBot.SetHomeOrder(Order)
        RefreshHomeOrder()
    End Sub

#End Region

#Region "QQ 群列表"

    Private Sub RefreshGroupList()
        If PanDstGroups Is Nothing Then Return
        Dim List As List(Of DstGroup)
        SyncLock DstBot.Groups
            List = DstBot.Groups.ToList()
        End SyncLock
        If List.Count = 0 Then
            LabDstGroupTip.Text = If(DstBot.ErrLast = "", "还没有获取到群列表，点右侧刷新", "获取失败：" & DstBot.ErrLast)
        Else
            LabDstGroupTip.Text = $"机器人共在 {List.Count} 个群，选一个作为启动器使用的群"
        End If
        PanDstGroups.Children.Clear()
        For Each Item In List
            PanDstGroups.Children.Add(BuildGroupRow(Item, Item.GroupId = DstBot.GroupNo))
        Next
        Dim Sel As DstGroup = DstBot.SelectedGroup
        If Sel Is Nothing Then
            LabDstGroupInfo.Text = ""
            LabDstGroupReport.Text = ""
        Else
            LabDstGroupInfo.Text = If(Sel.GroupInfo = "", "暂无简介", "简介：" & Sel.GroupInfo)
            Dim Latest As DstReport = Sel.LatestReport
            If Latest Is Nothing OrElse String.IsNullOrWhiteSpace(Latest.Content) Then
                LabDstGroupReport.Text = "暂无公告"
            Else
                Dim Text As String = Latest.Content
                If Text.Length > 120 Then Text = Text.Substring(0, 120) & "……"
                LabDstGroupReport.Text = $"最新公告（共 {Sel.ReportCount} 条）：{Text}"
            End If
        End If
        RefreshState()
    End Sub

    Private Function BuildGroupRow(Item As DstGroup, IsSelected As Boolean) As FrameworkElement
        Dim Row As New Border With {
            .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(12, 8, 12, 8),
            .CornerRadius = New CornerRadius(6),
            .Cursor = Cursors.Hand,
            .Tag = Item.GroupId,
            .Background = New SolidColorBrush(If(IsSelected, New MyColor(37, 99, 235), New MyColor(31, 40, 53)))
        }
        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Title As New TextBlock With {
            .Text = Item.GroupName, .FontSize = 13.5, .FontWeight = FontWeights.Bold,
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis
        }
        G.Children.Add(Title)
        Dim Id As New TextBlock With {
            .Text = If(IsSelected, "使用中 · ", "") & Item.GroupId, .FontSize = 12,
            .VerticalAlignment = VerticalAlignment.Center,
            .Foreground = New SolidColorBrush(If(IsSelected, New MyColor(220, 232, 255), New MyColor(150, 165, 190)))
        }
        Grid.SetColumn(Id, 1)
        G.Children.Add(Id)
        Row.Child = G
        AddHandler Row.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                Dim Id2 As String = CType(sender, Border).Tag.ToString()
                If Id2 = DstBot.GroupNo Then Return
                Dim PickedName As String = ""
                SyncLock DstBot.Groups
                    Dim Hit = DstBot.Groups.FirstOrDefault(Function(Grp) Grp.GroupId = Id2)
                    If Hit IsNot Nothing Then PickedName = Hit.GroupName
                End SyncLock
                DstBot.SetGroup(Id2, PickedName)
                '切换群后清掉服务器缓存，重新取
                SyncLock DstBot.ServerList
                    DstBot.ServerList.Clear()
                End SyncLock
                DstBot.UpdatedAt = Date.MinValue
                Hint("已切换群：" & DstBot.GroupDisplay, HintType.Green)
                RefreshGroupList()
                '主页与 QQ 页一起刷新，避免各处还显示上一个群
                If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshGroupDependentViews()
            End Sub
        Return Row
    End Function

    ''' <summary>离线模式下也能手动检查启动器更新（更新检查走独立接口，不受离线判定影响）。</summary>
    Private Sub BtnCheckUpdate_Click(sender As Object, e As EventArgs) Handles BtnCheckUpdate.Click
        If BtnCheckUpdate.IsEnabled = False Then Return
        BtnCheckUpdate.IsEnabled = False
        LabUpdateState.Text = "检查中……"
        DstUpdate.CheckLauncherUpdate(
        Sub(Info)
            BtnCheckUpdate.IsEnabled = True
            If Info.Err <> "" Then
                LabUpdateState.Text = "检查失败：" & Info.Err
                Return
            End If
            If Not Info.HasNew Then
                LabUpdateState.Text = $"已是最新（{VersionBaseName}）"
                Return
            End If
            LabUpdateState.Text = $"发现新版本 {Info.RemoteVersion}"
            If MyMsgBox($"当前版本：{VersionDisplay}" & vbCrLf &
                        $"服务器版本：{Info.RemoteVersion}" & vbCrLf & vbCrLf &
                        If(Info.Announce = "", "（服务器未提供更新说明）", Info.Announce),
                        "发现新版本", "下载并安装", "稍后再说") = 1 Then
                DstUpdate.DownloadLauncherUpdate(Info.RemoteVersion)
            End If
        End Sub)
    End Sub
    ''' <summary>切换常规（离线）模式：写配置后立刻刷新主页与 QQ 页。</summary>
    Private Sub CheckOfflineMode_Change(sender As Object, user As Boolean) Handles CheckOfflineMode.Change
        If Not user Then Return
        Settings.Set("DstOfflineMode", CheckOfflineMode.Checked)
        If CheckOfflineMode.Checked Then
            Hint("已切换到常规模式（离线）", HintType.Blue, False)
            If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshDependentViewsAfterModeChange()
        Else
            '取消离线：先探测一次，连不上就自动退回离线
            RunInThread(
            Sub()
                Dim Ok As Boolean = DstBot.ProbeOnline()
                RunInUi(
                Sub()
                    If Ok Then
                        Hint("已回到在线模式", HintType.Green, False)
                    Else
                        Hint("远程服务不可用，继续保持常规模式", HintType.Red)
                        CheckOfflineMode.Checked = True
                        Settings.Set("DstOfflineMode", True)
                    End If
                    If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshDependentViewsAfterModeChange()
                End Sub)
            End Sub)
        End If
    End Sub
    Private Sub BtnDstGroupRefresh_Click(sender As Object, e As EventArgs) Handles BtnDstGroupRefresh.Click
        DstBot.FetchGroups(Force:=True, OnDone:=AddressOf RefreshGroupList)
    End Sub

#End Region

    Private Sub RefreshState()
        If CheckOfflineMode IsNot Nothing Then CheckOfflineMode.Checked = Settings.Get(Of Boolean)("DstOfflineMode")
        LabDstService.Text = DstBot.ServiceName
        LabDstGroup.Text = DstBot.GroupDisplay
        LabDstStatus.Text = DstBot.StatusText & If(DstBot.ErrLast = "", "", "（" & DstBot.ErrLast & "）")
        If DstBot.IsQqBound Then
            LabDstQq.Text = $"已绑定 QQ {DstBot.QqNumber}（key {DstBot.UserKeyMasked}）"
        Else
            LabDstQq.Text = "未绑定"
        End If
        BtnDstQq.Text = If(DstBot.IsQqBound, "解除绑定", "绑定 QQ")
        LabDstQuota.Text = $"服务器配额剩余：{DstBot.RateLeft} / 4 次（滚动 60 秒窗口）　今日成功绑定：{DstBot.TodaySuccessCount} / 10"
    End Sub

    Private Sub BtnDstTest_Click(sender As Object, e As EventArgs) Handles BtnDstTest.Click
        DstBot.TestConnect()
        RefreshState()
    End Sub

    Private Sub BtnDstJoinGroup_Click(sender As Object, e As EventArgs) Handles BtnDstJoinGroup.Click
        DstBot.JoinGroup()
    End Sub

    Private Sub BtnDstQq_Click(sender As Object, e As EventArgs) Handles BtnDstQq.Click
        If DstBot.IsQqBound Then
            BtnDstQq.Text = "正在解绑……"
            DstBot.UnbindQq(Sub() RefreshState())
        Else
            BtnDstQq.Text = "等待确认……"
            DstBot.ShowBindDialog(AddressOf RefreshState)
        End If
    End Sub

    Private Sub BtnDstSave_Click(sender As Object, e As EventArgs) Handles BtnDstSave.Click
        If CheckDstUseCustom.Checked Then
            Dim Api As String = TextDstApi.Text.Trim()
            Dim ApiToken As String = TextDstToken.Text.Trim()
            If Api = "" OrElse ApiToken = "" Then
                Hint("自定义服务需要填写 API 地址与令牌", HintType.Red)
                Return
            End If
            If Not Api.StartsWithF("http") Then
                Hint("API 地址要以 http:// 或 https:// 开头", HintType.Red)
                Return
            End If
            Settings.Set("DstBotApiUrl", Api.TrimEnd("/"c))
            Settings.Set("DstBotToken", ApiToken)
        End If
        Settings.Set("DstBotUseCustom", CheckDstUseCustom.Checked)
        '切换服务后清空缓存，避免显示上一个服务的数据
        SyncLock DstBot.ServerList
            DstBot.ServerList.Clear()
        End SyncLock
        SyncLock DstBot.Groups
            DstBot.Groups.Clear()
        End SyncLock
        DstBot.UpdatedAt = Date.MinValue
        LabDstSaveTip.Text = "已保存 " & Date.Now.ToString("HH:mm:ss")
        Hint("机器人配置已保存", HintType.Green)
        RefreshState()
        '群列表跟着新服务重新拉一次，选中的群由服务端返回的列表决定
        DstBot.FetchGroups(Force:=True, OnDone:=AddressOf RefreshGroupList)
    End Sub

End Class
