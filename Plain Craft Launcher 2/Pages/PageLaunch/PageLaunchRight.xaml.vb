Imports System.Windows.Shapes
Imports System.Windows.Threading

Public Class PageLaunchRight
    Implements IRefreshable, IDispatcherUnhandledException

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        PanLog.Visibility = ModeDebug.ToVisibility
        '快照版提示
        PanHint.Visibility =
            (BuildType <> BuildTypes.Release AndAlso VersionBranchMain = "Official" AndAlso
             CurrentRank < DonationRank.Rank23 AndAlso Not Settings.Get(Of Boolean)("HintSnapshot")).ToVisibility
        '「+」用主题强调色，跟随深浅色主题
        If BtnDstAddServer IsNot Nothing Then
            BtnDstAddServer.Theme = MyIconButton.Themes.Custom
            BtnDstAddServer.Foreground = New SolidColorBrush(Color3)
        End If
        '帝释天机器人卡片与服务器列表
        ApplyHomeLayout()
        ApplyOfflineUi()
        RefreshBotCard()
        RefreshReport()
        '常规模式下没有任何远程数据可拉，只画本地自定义服务器
        If DstBot.IsOffline Then Return
        RefreshServerList(IsLoading:=True) '先画加载动画，别写"暂无服务器"
        '群号来自 /v2/api/list_qqgroups，先拉群列表再拉服务器
        DstBot.FetchGroups(OnDone:=AddressOf OnGroupsReady)
    End Sub

    Private Sub OnGroupsReady()
        RefreshBotCard()
        RefreshReport()
        DstBot.RefreshServers(OnDone:=AddressOf OnServersLoaded)
    End Sub

#Region "主页卡片布局"

    ''' <summary>
    ''' 切换 QQ 群之后调用：重新拉服务器列表与群公告，把主页两张卡片都刷新掉。
    ''' 只清缓存不刷新的话，用户看到的是上一个群的数据。
    ''' </summary>
    Public Sub ReloadForGroupChange()
        Try
            RefreshBotCard()
            RefreshReport()
            RefreshServerList(IsLoading:=True)
            '群公告依赖 Groups 列表，先确认群信息是新的，再拉服务器
            DstBot.FetchGroups(Force:=True, OnDone:=AddressOf OnGroupChangedRefresh)
        Catch ex As Exception
            Logger.Warn(ex, "换群后刷新主页失败")
        End Try
    End Sub

    Private Sub OnGroupChangedRefresh()
        RefreshBotCard()
        RefreshReport()
        DstBot.RefreshServers(Force:=True, OnDone:=AddressOf OnServersLoaded, SkipRateWait:=True)
    End Sub
    ''' <summary>
    ''' 常规模式设置变更后调用：卡片是否出现、显示哪一份数据都要重排。
    ''' </summary>
    Public Sub ReloadForOfflineSettingChange()
        ApplyHomeLayout()
        ApplyOfflineUi()
    End Sub

    ''' <summary>
    ''' 离线模式下把公告与机器人卡片隐藏。服务器卡片取决于设置：
    ''' 默认仍然显示，因为用户自己添加的服务器在离线时也能探测。
    ''' 公开给外部在切换模式后调用。
    ''' </summary>
    Public Sub ApplyOfflineUi()
        Dim Offline As Boolean = DstBot.IsOffline
        Dim Vis As Visibility = If(Offline, Visibility.Collapsed, Visibility.Visible)
        If CardDstServers IsNot Nothing Then
            CardDstServers.Visibility = If(Offline AndAlso Not ShowServersInOffline, Visibility.Collapsed, Visibility.Visible)
        End If
        If CardDstReport IsNot Nothing Then CardDstReport.Visibility = Vis
        If CardDstBot IsNot Nothing Then CardDstBot.Visibility = Vis
        If Not Offline Then
            '回到在线模式时补一次数据
            RefreshBotCard()
            RefreshReport()
            RefreshServerList()
        Else
            '离线时只画自定义服务器
            RefreshServerList()
        End If
    End Sub

    ''' <summary>离线模式下是否仍然展示服务器列表卡片（只显示用户自己添加的服务器）。</summary>
    Public ReadOnly Property ShowServersInOffline As Boolean
        Get
            Return Settings.Get(Of Boolean)("DstOfflineShowServers")
        End Get
    End Property
    ''' <summary>深浅色切换后重建卡片，让代码生成的控件用上新配色。</summary>
    Public Sub RefreshThemeColors()
        Try
            '自定义主题的图标按钮不跟随资源刷新，要重新传一次前景色
            If BtnDstAddServer IsNot Nothing Then BtnDstAddServer.Foreground = New SolidColorBrush(Color3)
            RefreshServerList()
            RefreshReport()
            RefreshBotCard()
        Catch ex As Exception
            Logger.Warn(ex, "重建主页配色失败")
        End Try
    End Sub
    ''' <summary>
    ''' 按设置里的顺序与开关重排三张帝释天卡片。
    ''' 服务器卡片在离线模式下由「显示服务器列表」开关单独控制，
    ''' 因此即使主页卡片设置里关掉了它，常规模式下勾选该开关也应当出现。
    ''' </summary>
    Private Sub ApplyHomeLayout()
        If PanDstCards Is Nothing Then Return
        PanDstCards.Children.Clear()
        Dim ShowServerCard As Boolean = DstBot.HomeShow("servers") OrElse (DstBot.IsOffline AndAlso ShowServersInOffline)
        For Each Key In DstBot.HomeOrder()
            Select Case Key
                Case "servers"
                    If ShowServerCard Then PanDstCards.Children.Add(CardDstServers)
                Case "report"
                    If DstBot.HomeShow("report") Then PanDstCards.Children.Add(CardDstReport)
                Case "bot"
                    If DstBot.HomeShow("bot") Then PanDstCards.Children.Add(CardDstBot)
            End Select
        Next
    End Sub

    Private Sub BtnDstReportAll_Click(sender As Object, e As EventArgs) Handles BtnDstReportAll.Click
        FrmMain.PageChange(FormMain.PageType.Group, FormMain.PageSubType.GroupReport)
    End Sub

    Private Sub RefreshReport()
        If LabDstReport Is Nothing Then Return
        Dim G As DstGroup = DstBot.SelectedGroup
        If G Is Nothing Then
            LabDstReportName.Text = ""
            LabDstReport.Text = If(DstBot.GroupNo = "", "还没有选择 QQ 群，去 设置 → 机器人 里选一个", "群公告需要先获取群列表")
            Return
        End If
        LabDstReportName.Text = G.GroupName & "（" & G.GroupId & "）" & If(G.ReportCount > 1, $" · 共 {G.ReportCount} 条", "")
        Dim Latest As DstReport = G.LatestReport
        LabDstReport.Text = If(Latest Is Nothing OrElse String.IsNullOrWhiteSpace(Latest.Content),
                               "群主还没有设置公告", Latest.Content)
    End Sub

#End Region

#Region "帝释天机器人"

    Private Sub RefreshBotCard()
        If LabBotService Is Nothing Then Return
        LabBotService.Text = DstBot.ServiceName
        LabBotGroup.Text = DstBot.GroupDisplay
        If DstBot.LoginWaiting Then
            LabBotBind.Text = "等待群里回复 OK……"
            BtnDstBotBind.Text = "等待确认"
            Return
        End If
        LabBotBind.Text = If(DstBot.IsQqBound, $"已绑定 QQ {DstBot.QqNumber}", "未绑定 QQ")
        BtnDstBotBind.Text = If(DstBot.IsQqBound, "解绑 QQ", "绑定 QQ")
    End Sub

    Private Sub BtnDstBotBind_Click(sender As Object, e As EventArgs) Handles BtnDstBotBind.Click
        If DstBot.IsQqBound Then
            BtnDstBotBind.Text = "正在解绑……"
            DstBot.UnbindQq(Sub() RefreshBotCard())
        Else
            BtnDstBotBind.Text = "等待确认"
            DstBot.ShowBindDialog(AddressOf RefreshBotCard)
        End If
    End Sub

    Private Sub BtnDstBotMore_Click(sender As Object, e As EventArgs) Handles BtnDstBotMore.Click
        FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupDst)
    End Sub

    ''' <summary>
    ''' 添加自定义服务器：先填地址，再填显示名（可留空，留空则用默认名）。
    ''' </summary>
    Private Sub BtnDstAddServer_Click(sender As Object, e As EventArgs) Handles BtnDstAddServer.Click
        Dim Ip As String = MyMsgBoxInput("添加服务器", "请填写服务器地址，例如 127.0.0.1 或 play.example.com:25565。", "",
                                         HintText:="主机名 或 主机名:端口")
        If String.IsNullOrWhiteSpace(Ip) Then Return
        Dim ErrText As String = DstCustomServer.ValidateAddress(Ip)
        If ErrText IsNot Nothing Then
            Hint(ErrText, HintType.Red)
            Return
        End If
        Dim Name As String = MyMsgBoxInput("添加服务器", "请填写显示名称（可留空）。", DstCustomServer.DefaultName,
                                            HintText:="显示名称")
        If Name Is Nothing Then Return '点了取消
        Dim Added As DstCustomServer.Entry = DstCustomServer.Add(Ip, Name)
        If Added Is Nothing Then
            Hint(If(DstCustomServer.ErrorText, "添加失败"), HintType.Red)
            Return
        End If
        RefreshServerList()
        Hint($"已添加服务器 {Added.Name}", HintType.Green)
    End Sub

    ''' <summary>点刷新：绕过缓存、跳过本地限流等待，立刻把请求发出去。</summary>
    Private Sub BtnDstRefresh_Click(sender As Object, e As EventArgs) Handles BtnDstRefresh.Click
        If Not BtnDstRefresh.IsEnabled Then Return
        BtnDstRefresh.IsEnabled = False
        BtnDstRefresh.Text = "刷新中……"
        LabDstUpdate.Text = "正在刷新……"
        If DstBot.IsOffline Then
            '离线时没有远程列表可拉，只重新探测自定义服务器
            For Each One In DstCustomServer.All()
                StartProbeShared(GetCustomServer(One))
            Next
            RefreshServerList()
            BtnDstRefresh.Text = "刷新"
            BtnDstRefresh.IsEnabled = True
            LabDstUpdate.Text = ""
            Return
        End If
        '只刷服务器列表：群列表不常变，多打一次就多占一份额度
        DstBot.RefreshServers(Force:=True, OnDone:=AddressOf OnServersRefreshed, SkipRateWait:=True)
    End Sub

    ''' <summary>延迟显示开关变化后重画列表。</summary>
    Public Shared Sub OnShowPingChanged(Value As Object)
        Try
            If FrmLaunchRight IsNot Nothing Then FrmLaunchRight.RefreshServerList()
        Catch ex As Exception
            Logger.Warn(ex, "刷新延迟显示失败")
        End Try
    End Sub
    Private Sub OnServersLoaded()
        RefreshServerList()
        ScheduleProbeRefresh()
        '启动刚结束时机身还在忙着加载页面、扫描版本，这时候测出来的延迟会虚高好几倍。
        '等界面稳定下来再测，用户看到的才是真实值。
        RunInThread(
        Sub()
            Thread.Sleep(3000)
            RunInUi(Sub() RefreshServerList())
        End Sub)
    End Sub

    Private Sub OnServersRefreshed()
        RefreshServerList()
        ScheduleProbeRefresh()
        If DstBot.ErrLast <> "" Then
            LabDstUpdate.Text = "刷新失败"
            Hint("刷新失败：" & DstBot.ErrLast, HintType.Red)
        ElseIf DstBot.UpdatedAt <> Date.MinValue Then
            LabDstUpdate.Text = "更新于 " & DstBot.UpdatedAt.ToString("HH:mm:ss")
            Hint("已刷新", HintType.Green, False)
        End If
        BtnDstRefresh.Text = "刷新"
        BtnDstRefresh.IsEnabled = True
    End Sub

#End Region

#Region "服务器列表"

    ''' <summary>
    ''' 自定义服务器的运行时对象缓存，Key 为 DstCustomServer.Entry.Id。
    ''' 必须缓存：探测结果（延迟、人数）属于运行时状态，不写进本地文件，
    ''' 但每次刷新都新建对象的话，刚探测到的结果就会丢，于是每次重画都重新探测一遍。
    ''' </summary>
    Private ReadOnly CustomRuntime As New Dictionary(Of String, DstServer)
    Private ReadOnly CustomRuntimeLock As New Object

    ''' <summary>
    ''' 取自定义服务器的运行时对象；没有就新建并登记。
    ''' 列表刷新时复用同一个对象，探测结果才能留住。
    ''' 自动重测计时器会在后台线程调用这里，所以必须加锁。
    ''' </summary>
    Private Function GetCustomServer(One As DstCustomServer.Entry) As DstServer
        SyncLock CustomRuntimeLock
            Dim Cached As DstServer = Nothing
            If CustomRuntime.TryGetValue(One.Id, Cached) Then
                '名称与地址可能被用户改过，每次都同步回来
                Cached.Nickname = One.Name
                Cached.Ip = One.Ip
                Return Cached
            End If
            Dim Created As DstServer = DstCustomServer.ToServer(One)
            CustomRuntime(One.Id) = Created
            Return Created
        End SyncLock
    End Function

    ''' <summary>
    ''' 重画服务器列表。两个来源在这里合并显示，但各自独立：
    ''' 机器人下发的列表（<see cref="DstBot.ServerList"/>）会随切群、切服务整批清空；
    ''' 自定义列表（<see cref="DstCustomServer"/>）是本地数据，任何刷新都不动它。
    ''' </summary>
    Private Sub RefreshServerList(Optional IsLoading As Boolean = False)
        If PanDstServers Is Nothing Then Return

        '来源一：机器人下发的服务器。取快照，避免刷新过程中被别的线程改掉。
        '离线模式下不显示远程列表——那批数据本来就拉不到，留着只会显示上一份的陈旧内容
        Dim RemoteList As New List(Of DstServer)
        If Not DstBot.IsOffline Then
            SyncLock DstBot.ServerList
                RemoteList = DstBot.ServerList.ToList()
            End SyncLock
        End If
        '来源二：用户自己添加的。每次新建对象，探测状态不落盘
        Dim CustomEntries As List(Of DstCustomServer.Entry) = DstCustomServer.All()
        '清掉已经被用户删掉的运行时对象，避免缓存无限增长
        Dim ValidIds = New HashSet(Of String)(CustomEntries.Select(Function(One) One.Id))
        SyncLock CustomRuntimeLock
            For Each Stale As String In CustomRuntime.Keys.Where(Function(K) Not ValidIds.Contains(K)).ToList()
                CustomRuntime.Remove(Stale)
            Next
        End SyncLock
        Dim CustomList As List(Of DstServer) = CustomEntries.Select(Function(One) GetCustomServer(One)).ToList()

        '在线优先，人数多的在前；自定义的排在机器人列表之后，方便区分
        RemoteList = RemoteList.OrderByDescending(Function(S) IsAlive(S)).ThenByDescending(Function(S) S.PlayerCount).ToList()
        CustomList = CustomList.OrderByDescending(Function(S) IsAlive(S)).ThenByDescending(Function(S) S.PlayerCount).ToList()

        PanDstServers.Children.Clear()
        SyncLock ChipBindLock
            ChipBinders.Clear() '旧芯片全部作废
        End SyncLock

        '远程列表还在加载、且没有任何可显示内容时，才显示加载动画。
        '已经有自定义服务器就先画出来，不必让用户干等远程接口
        If IsLoading AndAlso RemoteList.Count = 0 AndAlso CustomList.Count = 0 Then
            LabDstSummary.Text = "正在获取服务器列表……"
            LabDstUpdate.Text = ""
            PanDstServers.Children.Add(New MyLoading With {
                .Text = "正在获取服务器列表",
                .Margin = New Thickness(0, 10, 0, 0)})
            Return
        End If

        Dim TotalCount As Integer = RemoteList.Count + CustomList.Count
        Dim OnlineCount As Integer = RemoteList.Where(Function(S) IsAlive(S)).Count() +
                                     CustomList.Where(Function(S) IsAlive(S)).Count()

        If TotalCount = 0 Then
            '离线时远程列表本来就是空的，别把「获取失败」这种话甩给用户
            LabDstSummary.Text = If(DstBot.IsOffline,
                "还没有添加自定义服务器，点右边的 + 添加",
                If(DstBot.ErrLast = "", "暂未获取到服务器", "获取失败：" & DstBot.ErrLast))
        ElseIf CustomList.Any() Then
            LabDstSummary.Text = $"共 {TotalCount} 个服务器（{CustomList.Count} 个自定义） · {OnlineCount} 个在线"
        Else
            LabDstSummary.Text = $"共 {TotalCount} 个服务器 · {OnlineCount} 个在线"
        End If
        '只有远程列表成功刷新过才有“更新于”；自定义列表不参与这个时间
        LabDstUpdate.Text = If(DstBot.UpdatedAt = Date.MinValue, "", "更新于 " & DstBot.UpdatedAt.ToString("HH:mm:ss"))

        For Each Server In RemoteList
            PanDstServers.Children.Add(BuildServerRow(Server))
        Next
        For Each Server In CustomList
            PanDstServers.Children.Add(BuildServerRow(Server))
        Next
    End Sub
    Private Function BuildServerRow(Server As DstServer) As FrameworkElement
        Dim CanDownload As Boolean = Server.DownloadIndex <> ""
        '悬停底色：Indra 用偏亮的深蓝，PCL 沿用主题色相算出来的中性面。
        '原来的 HSL(22,30) 在深色下是接近背景的灰，压上去文字就糊了。
        Dim HoverBack As MyColor = If(Appearance = AppearanceStyle.Indra,
                                      New MyColor(30, 41, 59),
                                      New MyColor().FromHSL(ColorHue, 22, If(DstDarkMode, 30, 94)))

        Dim Card As New Border With {
            .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(12, 8, 12, 8),
            .CornerRadius = New CornerRadius(6),
            .Background = New SolidColorBrush(ServerBack(Server)),
            .Tag = Server.InstanceId
        }


        Dim Grid As New Grid
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        Grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        Grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto}) '行2：延迟芯片

        '在线状态点
        Dim Dot As New Ellipse With {
            .Width = 8, .Height = 8, .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center,
            .Fill = New SolidColorBrush(If(IsAlive(Server), New MyColor(52, 211, 153), New MyColor(120, 133, 150)))
        }
        Grid.Children.Add(Dot)

        '名称 + IP（IP 跟在名字右边）
        Dim Name As New TextBlock With {
            .Text = Server.Nickname, .FontSize = 14, .FontWeight = FontWeights.Bold,
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis
        }
        Dim IpLabel As New TextBlock With {
            .Text = Server.Ip, .FontSize = 11.5, .Opacity = 0.55,
            .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(10, 1, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = New SolidColorBrush(TextColorSub)
        }
        Dim NameRow As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .VerticalAlignment = VerticalAlignment.Center
        }
        NameRow.Children.Add(Name)
        NameRow.Children.Add(IpLabel)
        Grid.SetColumn(NameRow, 1)
        Grid.Children.Add(NameRow)

        '人数（稍后和延迟一起放进右列）
        Dim Count As New TextBlock With {
            .Text = If(Server.ProbeDone OrElse Server.PlayerCount > 0,
                       $"{Server.PlayerCount}/{Server.MaxPlayerCount}", "--/--"),
            .FontSize = 12.5,
            .HorizontalAlignment = HorizontalAlignment.Right, .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(8, 0, 0, 0),
            .Foreground = New SolidColorBrush(TextColorMain)
        }

        'IP 与在线玩家
        '第二行只放玩家信息（IP 已经挪到名字右边）
        Dim PlayerText As String = GetPlayerText(Server)
        Dim Detail As New TextBlock With {
            .Text = PlayerText, .FontSize = 11.5,
            .Margin = New Thickness(0, 2, 0, 0), .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = New SolidColorBrush(TextColorSub)
        }
        Grid.SetRow(Detail, 1)
        Grid.SetColumn(Detail, 1)
        Grid.SetColumnSpan(Detail, 2)
        Grid.Children.Add(Detail)

        '右列顶部：延迟（探测中显示转圈动画；点一下单独重测）
        Dim Chip As New Border With {
            .Background = Brushes.Transparent,
            .CornerRadius = New CornerRadius(4),
            .Padding = New Thickness(0),
            .Margin = New Thickness(0),
            .HorizontalAlignment = HorizontalAlignment.Right,
            .VerticalAlignment = VerticalAlignment.Center,
            .Cursor = Cursors.Hand,
            .Tag = Server
        }
        Dim ChipHost As New Grid
        Dim Spinner As New DstSpinner With {.Width = 12, .Height = 12, .Visibility = Visibility.Collapsed}
        Dim ChipText As New TextBlock With {
            .FontSize = 11, .VerticalAlignment = VerticalAlignment.Center,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .Foreground = New SolidColorBrush(TextColorSub)
        }
        ChipHost.Children.Add(Spinner)
        ChipHost.Children.Add(ChipText)
        Chip.Child = ChipHost
        '右列：延迟在人数左边
        Dim RightStack As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .VerticalAlignment = VerticalAlignment.Center
        }
        '延迟芯片受 DstShowPing 控制：有人觉得挡害，可以在右键菜单里关掉
        If Settings.Get(Of Boolean)("DstShowPing") Then RightStack.Children.Add(Chip)
        RightStack.Children.Add(Count)
        Grid.SetColumn(RightStack, 2)
        Grid.Children.Add(RightStack)
        '延迟芯片点击：单独重测这一台
        AddHandler Chip.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                e.Handled = True '别让卡片自己的下载点击触发
                Dim S As DstServer = CType(Chip.Tag, DstServer)
                StartProbe(S, AddressOf RefreshServerList)
            End Sub

        '右下角：能装整合包才标一绿色的“可安装”
        If CanDownload Then
            Dim Tip As New TextBlock With {
                .Text = "可安装", .FontSize = 11.5,
                .HorizontalAlignment = HorizontalAlignment.Right,
                .VerticalAlignment = VerticalAlignment.Center,
                .Margin = New Thickness(10, 2, 0, 0),
                .Foreground = New SolidColorBrush(New MyColor(52, 211, 153))
            }
            Grid.SetRow(Tip, 1)
            Grid.SetColumn(Tip, 2)
            Grid.Children.Add(Tip)
        End If

        Card.Child = Grid

        '悬停高亮对所有卡片生效
        AddHandler Card.MouseEnter, Sub() Card.Background = New SolidColorBrush(HoverBack)
        AddHandler Card.MouseLeave, Sub() Card.Background = New SolidColorBrush(ServerBack(Server))
        Card.ToolTip = "右键操作"
        '右键菜单：安装 / 另存为 / 复制 IP / 刷新
        Card.ContextMenu = BuildServerMenu(Server, CanDownload)

        '把芯片状态接进探测状态机（探测中 → 转圈；完成 → 毫秒数）
        BindChip(Server, Chip, ChipText, Spinner, Count, Detail, Dot, Card)

        Return Card
    End Function

#Region "服务器本地探测（SLPing）"
    '自动重测计时器
    Private Shared ProbeTimerLock As New Object
    Private Shared ProbeTimer As System.Threading.Timer

    ''' <summary>服务器列表加载完成后启动自动重测：每 5 分钟一轮。</summary>
    Private Sub ScheduleProbeRefresh()
        SyncLock ProbeTimerLock
            If ProbeTimer IsNot Nothing Then Return '已经排了
            ProbeTimer = New System.Threading.Timer(
            Sub(state)
                Try
                    '两个来源都要重测：机器人下发的按面板在线状态筛选；自定义的没有面板信息，只要没在探测就测
                    Dim Targets As New List(Of DstServer)
                    SyncLock DstBot.ServerList
                        '不能只挑 IsOnline：外部服务器的这个字段恒为 false，永远进不了重测队列。
                        '本地 SLPing 是自己发 UDP 包，不占用接口额度，整批重测没有代价
                        Targets.AddRange(DstBot.ServerList.ToList())
                    End SyncLock
                    For Each One In DstCustomServer.All()
                        Targets.Add(GetCustomServer(One))
                    Next
                    '关掉延迟显示后不必再测，省一轮无谓的 UDP 探测
                    If Not Settings.Get(Of Boolean)("DstShowPing") Then Return
                    For Each S In Targets
                        If Not S.Probing Then StartProbeShared(S)
                    Next
                Catch ex As Exception
                    Logger.Warn(ex, "自动重测服务器延迟失败")
                End Try
            End Sub, Nothing, 5 * 60 * 1000, 5 * 60 * 1000)
        End SyncLock
    End Sub

    ''' <summary>线程池上下文里的探测（芯片更新走共享登记表）。</summary>
    Private Sub StartProbeShared(S As DstServer)
        If S.Probing Then Return
        S.Probing = True
        NotifyChip(S)
        Dim HostPort = ParseHostPort(S.Ip)
        RunInThread(
        Sub()
            Dim Result As DstPing.PingResult
            If HostPort Is Nothing Then
                Result = New DstPing.PingResult With {.Error = "地址无效"}
            Else
                Result = DstPing.Probe(HostPort(0), HostPort(1), 5000)
            End If
            RunInUi(
            Sub()
                S.Probing = False
                If Result.PingMs >= 0 Then
                    S.PingMs = Result.PingMs
                    S.ProbeDone = True
                    S.ProbeFailed = False
                    If Result.OnlinePlayers >= 0 Then S.PlayerCount = Result.OnlinePlayers
                    If Result.MaxPlayers >= 0 Then S.MaxPlayerCount = Result.MaxPlayers
                    S.Players.Clear()
                    For Each P In Result.Players
                        S.Players.Add(P)
                    Next
                Else
                    S.ProbeFailed = True
                    S.PingMs = -1
                End If
                NotifyChip(S)
            End Sub)
        End Sub)
    End Sub


    ''' <summary>所有存活中的芯片绑定：服务器 InstanceId → 更新其显示的 Action。</summary>
    Private Shared ChipBindLock As New Object
    Private Shared ReadOnly ChipBinders As New Dictionary(Of String, Action)

    ''' <summary>
    ''' 把芯片的 UI 元素与服务器对象挂上：探测状态一变就重画芯片。
    ''' 页面重建时旧芯片会被 GC，这里只登记最新的。
    ''' </summary>
    Private Sub BindChip(Server As DstServer, Chip As Border, Text As TextBlock, Spinner As DstSpinner, Count As TextBlock, Detail As TextBlock, Dot As Ellipse, Card As Border)
        Dim Updater As Action = Sub() UpdateChip(Server, Chip, Text, Spinner, Count, Detail, Dot, Card)
        SyncLock ChipBindLock
            ChipBinders(Server.InstanceId) = Updater
        End SyncLock
        '首次立即画一次当前状态
        UpdateChip(Server, Chip, Text, Spinner, Count, Detail, Dot, Card)
        '未探测且未失败 → 自动排一次探测（首次获取）
        If Not Server.ProbeDone AndAlso Not Server.Probing AndAlso Not Server.ProbeFailed Then
            StartProbe(Server, Nothing)
        End If
    End Sub

    ''' <summary>按服务器当前状态重画一个芯片。</summary>
    Private Sub UpdateChip(S As DstServer, Chip As Border, Text As TextBlock, Spinner As DstSpinner, Count As TextBlock, Detail As TextBlock, Dot As Ellipse, Card As Border)
        '人数与玩家列表随探测结果一起更新
        UpdateServerTexts(S, Count, Detail, Dot, Card)
        If S.Probing Then
            Spinner.Start()
            Text.Text = ""
            Text.Visibility = Visibility.Collapsed
            Chip.ToolTip = "正在探测延迟……"
            Return
        End If
        Spinner.Stop()
        Text.Visibility = Visibility.Visible
        If S.ProbeFailed Then
            Text.Text = "不可达"
            Text.Foreground = New SolidColorBrush(New MyColor(248, 113, 113))
            Chip.ToolTip = "探测失败：服务器无响应或地址不可达。点击重试。"
        ElseIf S.PingMs >= 0 Then
            Text.Text = S.PingMs & " ms"
            Text.Foreground = New SolidColorBrush(PingColor(S.PingMs))
            Chip.ToolTip = $"本地探测延迟 {S.PingMs} 毫秒。点击重新探测。"
        Else
            Text.Text = "未探测"
            Text.Foreground = New SolidColorBrush(TextColorSub)
            Chip.ToolTip = "尚未探测。点击开始。"
        End If
    End Sub

    ''' <summary>
    ''' 服务器是否算“在线”：面板说在线，或本地 SLPing 探测通了。
    ''' 汇总行、状态点、卡片底色都用这一个判定，避免三处各说各话。
    ''' </summary>
    Private Function IsAlive(S As DstServer) As Boolean
        Return S.IsOnline OrElse S.PingMs >= 0
    End Function

    ''' <summary>
    ''' 卡片第二行的玩家/状态文本。
    ''' <para>
    ''' 是否“开着”必须用 <see cref="IsAlive"/> 判定，不能只看 <see cref="DstServer.IsOnline"/>：
    ''' 对于外部服务器（例如 Hypixel、各类第三方服），面板接口只统计在线人数，
    ''' 并不返回服务器是否启动，isOnline 恒为 false。只看这个字段会把明明 54ms 能连上的
    ''' 服务器标成“未启动”。本地 SLPing 通了，就说明它是开着的。
    ''' </para>
    ''' </summary>
    Private Function GetPlayerText(S As DstServer) As String
        If S.Players.Count > 0 Then
            Dim Text As String = String.Join("、", S.Players.Take(5))
            If S.Players.Count > 5 Then Text &= $" 等 {S.Players.Count} 人"
            Return Text
        End If
        'ping 不通时不要额外造一个“无法连接”状态：服务器真没开时也一样连不上，
        '芯片那边已经区分了「不可达」与「未探测」，这里保持原来的“未启动”即可
        If IsAlive(S) Then Return "暂无玩家"
        Return "未启动"
    End Function

    ''' <summary>
    ''' 按当前探测结果重算汇总行。两个来源都算进去，否则自定义服务器探测完不会更新台数。
    ''' </summary>
    Private Sub RefreshServerSummary()
        If LabDstSummary Is Nothing Then Return
        Dim RemoteList As New List(Of DstServer)
        SyncLock DstBot.ServerList
            RemoteList = DstBot.ServerList.ToList()
        End SyncLock
        Dim CustomList As List(Of DstServer) = DstCustomServer.All().
            Select(Function(One) GetCustomServer(One)).ToList()
        Dim Total As Integer = RemoteList.Count + CustomList.Count
        If Total = 0 Then Return
        Dim OnlineCount As Integer = RemoteList.Where(Function(S) IsAlive(S)).Count() +
                                     CustomList.Where(Function(S) IsAlive(S)).Count()
        LabDstSummary.Text = If(CustomList.Any(),
            $"共 {Total} 个服务器（{CustomList.Count} 个自定义） · {OnlineCount} 个在线",
            $"共 {Total} 个服务器 · {OnlineCount} 个在线")
    End Sub
    ''' <summary>把探测到的人数与玩家列表写进卡片的两行文本。</summary>
    Private Sub UpdateServerTexts(S As DstServer, Count As TextBlock, Detail As TextBlock, Dot As Ellipse, Card As Border)
        '人数
        If S.ProbeDone AndAlso S.PlayerCount >= 0 Then
            Count.Text = $"{S.PlayerCount}/{S.MaxPlayerCount}"
        ElseIf S.PlayerCount > 0 OrElse S.MaxPlayerCount > 0 Then
            Count.Text = $"{S.PlayerCount}/{S.MaxPlayerCount}"
        Else
            Count.Text = "--/--"
        End If
        'IP 与玩家
        Detail.Text = GetPlayerText(S)
        '在线状态点：面板没探测到也可能本地 ping 得通（面板只试一次就放弃）
        Dim Alive As Boolean = IsAlive(S)
        Dot.Fill = New SolidColorBrush(If(Alive, New MyColor(52, 211, 153), New MyColor(120, 133, 150)))
        '卡片背景跟着活/停切换（若当前不是悬停态才动它，避免盖掉悬停色）
        If Not Card.IsMouseOver Then Card.Background = New SolidColorBrush(ServerBack(S))
        '探测结果会改变在线台数，汇总行跟着更新
        RefreshServerSummary()
    End Sub

    ''' <summary>
    ''' 服务器卡片的右键菜单。菜单项用 PCL 自己的 MyMenuItem，跟“皮肤 → 另存为”那套一致。
    ''' </summary>
    Private Function BuildServerMenu(Server As DstServer, CanDownload As Boolean) As ContextMenu
        Dim Menu As New ContextMenu
        Dim MInstall As New MyMenuItem With {.Header = "安装", .IsEnabled = CanDownload}
        Dim MSaveAs As New MyMenuItem With {.Header = "另存为", .IsEnabled = CanDownload}
        Dim MCopyIp As New MyMenuItem With {.Header = "复制 IP"}
        Dim MRefresh As New MyMenuItem With {.Header = "刷新"}
        Dim MPing As New MyMenuItem With {
            .Header = If(Settings.Get(Of Boolean)("DstShowPing"), "隐藏延迟", "显示延迟")}
        AddHandler MInstall.Click, Sub() DownloadInstancePack(Server)
        AddHandler MSaveAs.Click, Sub() SaveInstancePackAs(Server)
        AddHandler MCopyIp.Click, Sub() CopyServerIp(Server)
        AddHandler MRefresh.Click, Sub() StartProbe(Server, AddressOf RefreshServerList)
        Menu.Items.Add(MInstall)
        Menu.Items.Add(MSaveAs)
        Menu.Items.Add(MCopyIp)
        Menu.Items.Add(MRefresh)
        AddHandler MPing.Click, Sub()
            Settings.Set("DstShowPing", Not Settings.Get(Of Boolean)("DstShowPing"))
            RefreshServerList()
        End Sub
        Menu.Items.Add(MPing)
        '自定义服务器多一个「删除」，机器人下发的服务器不能删
        If Server.IsCustom Then
            Menu.Items.Add(New Separator)
            Dim MRemove As New MyMenuItem With {.Header = "删除这个服务器"}
            AddHandler MRemove.Click, Sub() RemoveCustomServer(Server)
            Menu.Items.Add(MRemove)
        End If
        Return Menu
    End Function

    ''' <summary>删除一台自定义服务器。只影响本地列表，不动机器人下发的数据。</summary>
    Private Sub RemoveCustomServer(Server As DstServer)
        If Not Server.IsCustom Then Return
        If MyMsgBox($"确定要删除自定义服务器「{Server.Nickname}」吗？", "删除服务器", "删除", "取消", IsWarn:=True) <> 1 Then Return
        DstCustomServer.Remove(Server.InstanceId)
        '运行时缓存也要一起清掉，否则列表里还留着这一项的探测结果
        SyncLock CustomRuntimeLock
            CustomRuntime.Remove(Server.InstanceId)
        End SyncLock
        RefreshServerList()
        Hint("已删除该服务器", HintType.Green)
    End Sub

    ''' <summary>另存为：选中路径后把整合包直接存到那儿，不安装。</summary>
    Private Sub SaveInstancePackAs(Server As DstServer)
        If Server.DownloadIndex = "" Then
            Hint("这个服务器没有可下载的整合包", HintType.Blue)
            Return
        End If
        Dim FileName As String = Server.Nickname
        For Each Bad As Char In IO.Path.GetInvalidFileNameChars()
            FileName = FileName.Replace(Bad, "_"c)
        Next
        FileName &= ".zip"
        Dim Target As String = Dialogs.SaveFile("选择保存位置", FileName, filter:={("zip", "整合包")})
        If String.IsNullOrWhiteSpace(Target) Then Return
        RunInThread(
        Sub()
            Try
                DstBot.DownloadToFile(DstUpdate.PathDownload & "?index=" & Uri.EscapeDataString(Server.DownloadIndex), Target)
                RunInUi(Sub() Hint($"已保存到 {Target}", HintType.Green))
            Catch ex As Exception
                ex.ThrowIfCanceled()
                Logger.Warn(ex, "另存为整合包失败")
                RunInUi(Sub() Hint("保存失败：" & FriendlyError(ex.Message), HintType.Red))
            End Try
        End Sub)
    End Sub

    ''' <summary>复制服务器 IP 到剪贴板。</summary>
    Private Sub CopyServerIp(Server As DstServer)
        If Server.Ip = "" Then Return
        Try
            My.Computer.Clipboard.SetText(Server.Ip)
            Hint($"已复制 {Server.Ip}", HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "复制服务器 IP 失败")
            Hint("复制失败：" & ex.Message, HintType.Red)
        End Try
    End Sub
    ''' <summary>
    ''' 卡片底色：活着（面板说在线，或本地探测通了）用主底色，否则用副底色。
    ''' 必须每次实时算——建卡时还没探测，面板也可能误报"未启动"。
    ''' </summary>
    Private Function ServerBack(S As DstServer) As MyColor
        If IsAlive(S) Then Return BackColorMain
        Return BackColorSub
    End Function
    ''' <summary>延迟配色：低于 100ms 为绿，100~199ms 为黄，200~399ms 为橙，400ms 及以上为红。</summary>
    Private Function PingColor(Ms As Integer) As MyColor
        If Ms < 100 Then Return New MyColor(52, 211, 153)     ' 绿：100ms 以内都很健康
        If Ms < 200 Then Return New MyColor(250, 204, 21)     ' 黄
        If Ms < 400 Then Return New MyColor(251, 146, 60)     ' 橙
        Return New MyColor(248, 113, 113)                     ' 红
    End Function

    ''' <summary>
    ''' 对一台服务器发起本地 SLPing，结束后更新数据与芯片。
    ''' OnDone 在 UI 线程回调（可传 Nothing）。
    ''' </summary>
    Private Sub StartProbe(S As DstServer, Optional OnDone As Action = Nothing)
        If S.Probing Then Return
        S.Probing = True
        S.ProbeFailed = False
        NotifyChip(S)
        Dim HostPort = ParseHostPort(S.Ip)
        RunInThread(
        Sub()
            Dim Result As DstPing.PingResult
            If HostPort Is Nothing Then
                Result = New DstPing.PingResult With {.Error = "地址无效"}
            Else
                Result = DstPing.Probe(HostPort(0), HostPort(1), 5000)
            End If
            RunInUi(
            Sub()
                S.Probing = False
                If Result.PingMs >= 0 Then
                    S.PingMs = Result.PingMs
                    S.ProbeDone = True
                    S.ProbeFailed = False
                    If Result.OnlinePlayers >= 0 Then S.PlayerCount = Result.OnlinePlayers
                    If Result.MaxPlayers >= 0 Then S.MaxPlayerCount = Result.MaxPlayers
                    S.Players.Clear()
                    For Each P In Result.Players
                        S.Players.Add(P)
                    Next
                Else
                    '探测失败：面板说在线但 ping 不通，标记失败但保留面板数据
                    S.ProbeFailed = True
                    S.PingMs = -1
                End If
                NotifyChip(S)
                If OnDone IsNot Nothing Then OnDone()
            End Sub)
        End Sub)
    End Sub

    ''' <summary>把 "host:port" 拆成两段；不带端口默认 25565。</summary>
    Private Function ParseHostPort(Ip As String) As String()
        If Ip Is Nothing OrElse Ip.Trim() = "" Then Return Nothing
        Dim Parts = Ip.Trim().Split(":"c)
        If Parts.Length = 1 Then Return {Parts(0), 25565}
        Dim Port As Integer
        If Not Integer.TryParse(Parts(1), Port) OrElse Port < 1 OrElse Port > 65535 Then Return Nothing
        Return {Parts(0), Port}
    End Function

    ''' <summary>
    ''' 通知某台服务器的芯片重画。
    ''' 服务器可能已经被用户删掉、卡片也已经销毁，此时登记表里没有它，直接忽略。
    ''' </summary>
    Private Sub NotifyChip(S As DstServer)
        If S Is Nothing Then Return
        Dim Updater As Action = Nothing
        SyncLock ChipBindLock
            ChipBinders.TryGetValue(S.InstanceId, Updater)
        End SyncLock
        If Updater IsNot Nothing Then RunInUi(Updater)
    End Sub

#End Region
    ''' <summary>按下载索引拉取整合包，走 PCL 的标准下载 + 安装 Loader 链。</summary>
    Private Sub DownloadInstancePack(Server As DstServer, Optional Button As MyButton = Nothing)
        If Button IsNot Nothing Then Button.IsEnabled = False
        Dim LoaderName As String = $"安装整合包 {Server.Nickname}"
        RunInThread(
        Sub()
            Try
                Dim TempFile As String = PathTemp & "DstPack\" & Server.DownloadIndex & ".tmp"
                Dim LocalFile As String = PathTemp & "DstPack\" & Server.DownloadIndex & ".zip"
                Dim IsFinished As Boolean = False

                '下载任务：带进度，能被任务管理页捕获
                Dim DownloadTask As New LoaderTask(Of Integer, String)("下载整合包 " & Server.Nickname,
                Sub(Task As LoaderTask(Of Integer, String))
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(TempFile))
                    Task.Progress = 0
                    Dim Suggested As String = DstBot.DownloadToFile(
                        DstUpdate.PathDownload & "?index=" & Uri.EscapeDataString(Server.DownloadIndex),
                        TempFile,
                        Sub(Received As Long, Total As Long)
                            If Total > 0 Then Task.Progress = Math.Min(0.999, Received / Total)
                        End Sub)
                    Task.Progress = 1
                    If Not FileUtils.Exists(TempFile) Then Throw New Exception("下载后未找到文件")
                    Dim Size As Long = New IO.FileInfo(TempFile).Length
                    Logger.Info($"整合包已下载：{Size} 字节，服务器文件名：{Suggested}")
                    If Size = 0 Then Throw New Exception("下载到的文件为空")
                    '按文件头确认是压缩包
                    Dim Kind As String = DstUpdate.SniffPackage(TempFile)
                    If Kind <> "zip" AndAlso Kind <> "rar" Then
                        Throw New Exception($"下载到的不是压缩包（{Size} 字节，类型：{Kind}）")
                    End If
                    '统一改名为 .zip，安装器要靠扩展名
                    If FileUtils.Exists(LocalFile) Then FileUtils.Delete(LocalFile)
                    IO.File.Move(TempFile, LocalFile)
                    Task.Output = LocalFile
                End Sub) With {.ProgressWeight = 10, .Block = True}

                '安装任务
                Dim InstallTask As New LoaderTask(Of Integer, Integer)("准备安装整合包 " & Server.Nickname,
                Sub()
                    ModpackInstall(LocalFile, Server.Nickname)
                End Sub) With {.ProgressWeight = 1}

                Dim Combo As New LoaderCombo(Of String)(LoaderName,
                    New List(Of LoaderBase) From {DownloadTask, InstallTask}) With {
                    .OnStateChanged = Sub(MyLoader As LoaderBase)
                                           If MyLoader.State = LoadState.Finished Then IsFinished = True
                                       End Sub
                }
                Combo.Start()
                RunInUi(
                Sub()
                    LoaderTaskbarAdd(Combo)
                    FrmMain.BtnExtraDownload.ShowRefresh()
                    FrmMain.BtnExtraDownload.Ribble()
                    '切到任务管理，让用户看到进度
                    FrmMain.PageChange(FormMain.PageType.TaskManager)
                    If Button IsNot Nothing Then Button.IsEnabled = True
                End Sub)
            Catch ex As Exception
                ex.ThrowIfCanceled()
                Logger.Warn(ex, "下载实例整合包失败")
                RunInUi(
                Sub()
                    If Button IsNot Nothing Then Button.IsEnabled = True
                    Hint("下载失败：" & FriendlyError(ex.Message), HintType.Red)
                End Sub)
            End Try
        End Sub)
    End Sub

#End Region

#Region "主页"

    ''' <summary>
    ''' 刷新主页。
    ''' </summary>
    Private Sub Refresh() Handles Me.Loaded
        RunInThread(
        Sub()
            Try
                SyncLock RefreshLock
                    RefreshReal()
                End SyncLock
            Catch ex As Exception
                Logger.Error(ex, "加载 PCL 主页自定义信息失败", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
            End Try
        End Sub)
    End Sub
    Private Sub RefreshReal()
        Dim Content As String = Nothing, Url As String = Nothing
        Select Case Settings.Get(Of Integer)("UiCustomType")
            Case 1
                '加载本地文件
                Logger.Info("主页自定义数据来源：本地文件")
                Content = FileUtils.TryReadAsString(Paths.Base & "PCL\Custom.xaml")
            Case 2
                '联网下载
                Url = Settings.Get(Of String)("UiCustomNet")
            Case 3
                '预设
                Select Case Settings.Get(Of Integer)("UiCustomPreset")
                    Case 0
                        Logger.Info("主页预设：你知道吗")
                        Content = "
                            <local:MyCard Title=""你知道吗？"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{hint}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 1
                        Logger.Info("主页预设：回声洞")
                        Content = "
                            <local:MyCard Title=""回声洞"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{cave}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 2
                        Logger.Info("主页预设：Minecraft 新闻")
                        Url = "https://mcnews.meloong.com"
                    Case 3
                        Logger.Info("主页预设：简单主页")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/MFn233/Custom.xaml"
                    Case 4
                        Logger.Info("主页预设：每日整合包推荐")
                        Url = "https://pclsub.sodamc.com/"
                    Case 5
                        Logger.Info("主页预设：Minecraft 皮肤推荐")
                        Url = "https://forgepixel.com/pcl_sub_file"
                    Case 6
                        Logger.Info("主页预设：OpenBMCLAPI 仪表盘 Lite")
                        Url = "https://pcl-bmcl.milu.ink/"
                    Case 7
                        Logger.Info("主页预设：主页市场")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/p/Homepages/Homepage.Market/Custom.xaml"
                    Case 8
                        Logger.Info("主页预设：更新日志")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/Joker2184/UpdateHomepage.xaml"
                    Case 9
                        Logger.Info("主页预设：PCL 新功能说明书")
                        Url = "https://raw.gitcode.com/WForst-Breeze/WhatsNewPCL/raw/main/Custom.xaml"
                    Case 10
                        Logger.Info("主页预设：OpenMCIM Dashboard")
                        Url = "https://files.mcimirror.top/PCL"
                    Case 11
                        Logger.Info("主页预设：杂志主页")
                        Url = "https://gh-proxy.com/https://github.com/Neclyon/Magazine-Homepage-PCL/raw/main/output/Custom.xaml"
                    Case 12
                        Logger.Info("主页预设：PCL GitHub 仪表盘")
                        Url = "https://ddf.pcl-community.top/Custom.xaml"
                    Case 13
                        Logger.Info("主页预设：PCL 更新摘要")
                        Url = "https://raw.gitcode.com/ENC_Euphony/PCL-AI-Summary-HomePage/raw/master/Custom.xaml"
                    Case 14
                        Logger.Info("主页预设：今日新闻热点")
                        Url = "https://pcl.wyc-w.top/index.xaml"
                    Case 15
                        Logger.Info("主页预设：Minecraft 芝士站")
                        Url = "https://mkss-services.awaeric.xyz"
                    Case 16
                        Logger.Info("主页预设：整合包推荐引擎")
                        Url = "https://qawsedrftgyhujiko.fun/pcl2/Custom.xaml"
                    Case 17
                        Logger.Info("主页预设：Bangumi 番剧主页")
                        Url = "https://bangumi.p.kaphia.top"
                    Case 18
                        Logger.Info("主页预设：Bilibili 热门")
                        Url = "https://bilibili.p.kaphia.top"
                    Case 19
                        Logger.Info("主页预设：Music 云音乐热门")
                        Url = "https://cloudmusic.p.kaphia.top"
                    Case 20
                        Logger.Info("主页预设：模组推荐")
                        Url = "https://g-fish.dpdns.org/download/ModPage.xaml"
                    Case 21
                        Logger.Info("主页预设：每日游戏推荐")
                        Url = "https://pcl.xn--9iq209asnop94b.top/download/GamePage.xaml"
                    Case 22
                        Logger.Info("主页预设：Wallpaper 每日壁纸")
                        Url = "https://wallpaper.p.kaphia.top"
                    Case 23
                        Logger.Info("主页预设：Weather 天气预报")
                        Url = "https://weather-p.kaphia.top/{variable:PCLDailyWeatherPage:choose}.xaml"
                    Case 24
                        Logger.Info("主页预设：新一代狗屁王大杂烩主页")
                        Url = "https://end.g-fish.dpdns.org/Custom.xaml"
                End Select
        End Select
        '联网下载
        If Not String.IsNullOrWhiteSpace(Url) Then
            '替换自定义变量与设置
            Url = ArgumentReplace(Url, AddressOf WebUtility.HtmlEncode)
            If Url = Settings.Get(Of String)("CacheSavedPageUrl") AndAlso FileUtils.Exists(PathTemp & "Cache\Custom.xaml") Then
                '缓存可用
                Logger.Info("主页自定义数据来源：联网缓存文件")
                Content = FileUtils.TryReadAsString(PathTemp & "Cache\Custom.xaml")
                '后台更新缓存
                OnlineLoader.Start((Url, False))
            Else
                '缓存不可用
                Logger.Info("主页自定义数据来源：联网全新下载")
                Hint("正在加载主页……")
                RunInUiWait(Sub() LoadContent(Nothing)) '在加载结束前清空页面
                Settings.Set("CacheSavedPageVersion", "")
                OnlineLoader.Start((Url, True)) '下载完成后将会再次触发更新
                Return
            End If
        End If
        '同步到 UI
        RunInUi(Sub() LoadContent(Content))
    End Sub
    Private RefreshLock As New Object

    '联网获取主页文件
    Private OnlineLoader As New LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer)("下载主页", AddressOf OnlineLoaderSub) With {.ReloadTimeout = 10 * 60 * 1000}
    Private Sub OnlineLoaderSub(Task As LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer))
        Dim Address As String = Task.Input.Address '#3721 中连续触发两次导致内容变化
        Dim ShouldRefresh As Boolean = Task.Input.ShouldRefresh
        Try
            '获取版本校验地址
            Dim VersionAddress As String
            If Address.Contains(".xaml") Then
                VersionAddress = Address.Replace(".xaml", ".xaml.ini")
            Else
                VersionAddress = Address.BeforeFirst("?")
                If Not VersionAddress.EndsWithF("/") Then VersionAddress += "/"
                VersionAddress += "version"
                If Address.Contains("?") Then VersionAddress += "?" & Address.AfterFirst("?")
            End If
            '校验版本
            Dim Version As String = ""
            Try
                Version = NetRequestByClientRetry(VersionAddress)
                If Version.Length > 1000 Then Throw New Exception($"获取的主页版本过长（{Version.Length} 字符）")
                Dim CurrentVersion As String = Settings.Get(Of String)("CacheSavedPageVersion")
                If Version <> "" AndAlso CurrentVersion <> "" AndAlso Version = CurrentVersion Then
                    Logger.Info($"当前缓存的主页已为最新，当前版本：{Version}，检查源：{VersionAddress}")
                    Return
                End If
                Logger.Info($"需要下载联网主页，当前版本：{Version}，检查源：{VersionAddress}")
            Catch exx As Exception
                Logger.Warn(exx, $"联网获取主页版本失败")
                Logger.Info($"无法检查联网主页版本，将直接下载，检查源：{VersionAddress}")
            End Try
            '实际下载
            Dim FileContent As String = NetRequestByClientRetry(Address)
            Logger.Info($"已联网下载主页，内容长度：{FileContent.Length}，来源：{Address}")
            Settings.Set("CacheSavedPageUrl", Address)
            Settings.Set("CacheSavedPageVersion", Version)
            FileUtils.Write(PathTemp & "Cache\Custom.xaml", FileContent)
            '若内容变更则要求刷新
            If LoadedContentHash <> FileContent.GetStableHashCode() AndAlso ShouldRefresh Then Refresh()
        Catch ex As Exception
            Logger.Error(ex, $"下载主页失败（{Address}）", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
        End Try
    End Sub

    ''' <summary>
    ''' 立即强制刷新主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Public Sub ForceRefresh() Implements IRefreshable.Refresh
        Logger.Info("要求强制刷新主页")
        ClearCache()
        '实际的刷新
        If FrmMain.PageCurrent.Page = FormMain.PageType.Launch Then
            PanBack.ScrollToHome()
            Refresh()
        Else
            FrmMain.PageChange(FormMain.PageType.Launch)
        End If
    End Sub

    ''' <summary>
    ''' 清空主页缓存信息。
    ''' </summary>
    Private Sub ClearCache()
        LoadedContentHash = Nothing
        OnlineLoader.Input = ("", True)
        Settings.Set("CacheSavedPageUrl", "")
        Settings.Set("CacheSavedPageVersion", "")
        Logger.Info("已清空主页缓存")
    End Sub

    ''' <summary>
    ''' 从文本内容中加载主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Private Sub LoadContent(Content As String)
        Try
            SyncLock LoadContentLock
                '如果加载目标内容一致则不加载
                Dim Hash = If(Content, "").GetStableHashCode()
                If Hash = LoadedContentHash Then Return
                LoadedContentHash = Hash
                '实际加载内容
                PanCustom.Children.Clear()
                If String.IsNullOrWhiteSpace(Content) Then
                    Logger.Info($"实例化：清空主页 UI，来源为空")
                    Return
                End If
                Dim LoadStartTime As Date = Date.Now
                '修改时应同时修改 PageOtherHelpDetail.Init
                Content = ArgumentReplace(Content, AddressOf StringUtils.XmlEscape)
                Do While Content.Contains("xmlns")
                    Content = Content.RegexReplace("xmlns[^""']*(""|')[^""']*(""|')", "").Replace("xmlns", "") '禁止声明命名空间
                Loop
                Content = "<StackPanel xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:sys=""clr-namespace:System;assembly=mscorlib"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" xmlns:core=""clr-namespace:MeloongCore;assembly=MeloongCore"" xmlns:corewpf=""clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf"">" & Content & "</StackPanel>"
                Logger.Info($"实例化：加载主页 UI 开始，最终内容长度：{Content.Count}")
                PanCustom.Children.Add(GetObjectFromXML(Content))
                '加载计时
                Dim LoadCostTime = (Date.Now - LoadStartTime).Milliseconds
                Logger.Info($"实例化：加载主页 UI 完成，耗时 {LoadCostTime}ms")
                If LoadCostTime > 3000 Then Hint($"主页加载过于缓慢（花费了 {Math.Round(LoadCostTime / 1000, 1)} 秒），请向主页作者反馈此问题，或暂时停止使用该主页")
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, $"加载失败的主页内容：{vbCrLf}{Content}")
            OnLoadContentFailed(ex)
        End Try
    End Sub
    ''' <summary>
    ''' 加载主页失败时调用。
    ''' </summary>
    Private Sub OnLoadContentFailed(ex As Exception)
        If ModeDebug OrElse Settings.Get(Of Integer)("UiCustomType") = 1 Then
            Logger.Warn(ex, "加载主页失败")
            If MyMsgBox(If(TypeOf ex Is UnauthorizedAccessException, ex.Message, $"主页内容编写有误，请根据下列错误信息进行检查：{vbCrLf}{ex.GetDisplay(False)}"),
                        "加载主页失败", "重试", "取消") = 1 Then ForceRefresh()
        Else
            Logger.Error(ex, "加载主页失败", LogBehavior.Toast)
        End If
    End Sub
    ''' <summary>
    ''' 捕获主页在 Measure 和 Arrange 阶段抛出的异常。
    ''' </summary>
    Private Sub DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Implements IDispatcherUnhandledException.DispatcherUnhandledException
        If TypeOf e.Exception IsNot Markup.XamlParseException Then Return
        e.Handled = True
        LoadContent(Nothing)
        OnLoadContentFailed(e.Exception)
    End Sub

    Private LoadedContentHash As ULong? = Nothing
    Private LoadContentLock As New Object

#End Region

End Class
