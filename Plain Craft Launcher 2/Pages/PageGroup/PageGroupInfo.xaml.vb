Imports System.Windows.Media.Imaging
Imports System.Windows.Threading

''' <summary>
''' 群聊 → 查看消息。渲染最近消息，按 10 秒轮询增量同步，历史只在内存里累加。
''' </summary>
Public Class PageGroupInfo

    '已经收到的消息（跨多次刷新累加，不丢历史）
    Private ReadOnly AllMessages As New ObjectModel.ObservableCollection(Of DstBot.DstMessage)
    '真正绑定到界面的窗口：只保留最近几十条，渲染量与总条数无关
    Private ReadOnly Visible As New ObjectModel.ObservableCollection(Of DstBot.DstMessage)
    Private Const VisibleMax As Integer = 60
    '窗口起点在 AllMessages 里的下标
    Private ViewStart As Integer
    Private ReadOnly SeenKeys As New HashSet(Of String)
    Private SyncTimer As DispatcherTimer
    Private LastError As String = ""

    Private Sub PageGroupInfo_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '交给虚拟化面板：只有滚到可见范围的行才会真正渲染
        DstChatRow.Builder = AddressOf BuildMessageRow
        PanChat.ItemsSource = Visible
        '先把本地缓存铺出来，再联网增量同步
        LoadCache()
        Refresh(False)
        '每 10 秒同步一次
        If SyncTimer Is Nothing Then
            SyncTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(10)}
            AddHandler SyncTimer.Tick, Sub() Refresh(True)
        End If
        If Not SyncTimer.IsEnabled Then SyncTimer.Start()
    End Sub

    ''' <summary>从本地缓存恢复历史（跨启动保留）。</summary>
    Private Sub LoadCache()
        Dim G As DstGroup = DstBot.SelectedGroup
        If G Is Nothing Then Return
        If AllMessages.Count > 0 Then Return
        Dim CacheGroupId As String = G.GroupId
        '读文件 + 解析 JSON 都不该占 UI 线程
        RunInThread(
        Sub()
            Dim Cached As List(Of DstBot.DstMessage) = DstBot.LoadChatCache(CacheGroupId)
            RunInUi(
            Sub()
                '读盘期间可能已经切群了，这份缓存直接作废
                If DstBot.GroupNo <> CacheGroupId Then Return
                If AllMessages.Count > 0 Then Return
                For Each Item In Cached
                    If SeenKeys.Add(Item.Key) Then AllMessages.Add(Item)
                Next
                If AllMessages.Count > 0 Then
                    TrimHistory()
                    AppendMessages(True)
                End If
            End Sub)
        End Sub)
    End Sub

    Private Sub PageGroupInfo_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        If SyncTimer IsNot Nothing Then SyncTimer.Stop()
    End Sub

    ''' <summary>离线模式：整页只留一个上线按钮，其它内容全部隐藏。</summary>
    Public Sub ApplyOfflineUi()
        If PanOffline Is Nothing Then Return
        Dim Offline As Boolean = DstBot.IsOffline
        PanOffline.Visibility = If(Offline, Visibility.Visible, Visibility.Collapsed)
        CardChat.Visibility = If(Offline, Visibility.Collapsed, Visibility.Visible)
    End Sub

    Private Sub BtnGoOnlineChat_Click(sender As Object, e As EventArgs) Handles BtnGoOnlineChat.Click
        If BtnGoOnlineChat.IsEnabled = False Then Return
        BtnGoOnlineChat.IsEnabled = False
        BtnGoOnlineChat.Text = "正在连接"
        RunInThread(
        Sub()
            Dim Ok As Boolean = DstBot.GoOnline()
            RunInUi(
            Sub()
                BtnGoOnlineChat.IsEnabled = True
                BtnGoOnlineChat.Text = "上线"
                If Ok Then
                    Hint("已回到在线模式", HintType.Green, False)
                    ApplyOfflineUi()
                    If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshDependentViewsAfterModeChange()
                    Refresh(Silent:=False)
                Else
                    Hint("远程服务不可用", HintType.Red)
                End If
            End Sub)
        End Sub)
    End Sub
    ''' <summary>换群后清掉当前对话内容，重新按新群拉取。</summary>
    Public Sub ReloadForGroupChange()
        Try
            '把上一个群留下的所有状态清空：消息、可见窗口、去重集合、渲染计数、请求令牌
            IsFetching = False
            AllMessages.Clear()
            Visible.Clear()
            SeenKeys.Clear()
            ViewStart = 0
            RenderedCount = 0
            LastError = ""
            '界面上的消息控件也要清，否则会残留旧群的卡片
            If PanChat IsNot Nothing Then PanChat.Items.Clear()
            UpdateHeader()
            LoadCache()
            Refresh(Silent:=False)
        Catch ex As Exception
            Logger.Warn(ex, "换群后重载聊天失败")
        End Try
    End Sub
    Private Sub BtnChatRefresh_Click(sender As Object, e As EventArgs) Handles BtnChatRefresh.Click
        Refresh(False)
    End Sub

    '聊天记录同时最多保留这么多条（内存与控件都算），超出的从最旧的开始丢
    Private Const MaxMessages As Integer = 300
    '是否有一个请求在路上，避免定时器和手动刷新叠在一起
    Private IsFetching As Boolean

    '已经渲染进 PanChat 的条数，用于增量追加
    Private RenderedCount As Integer

    ''' <summary>拉一次消息。Silent 为 True 时用于自动轮询，不打断界面。</summary>
    Private Sub Refresh(Silent As Boolean)
        '注意：这里不能因为上一个请求还没回来就 return。
        '否则切群时旧请求占着 IsFetching，新群的第一次请求会被直接吞掉，
        '然后旧请求带着上一个群的消息回填——正是“串群残留”的来源。
        Dim G As DstGroup = DstBot.SelectedGroup
        If G Is Nothing Then
            LabChatTitle.Text = "还没有选择群，去 设置 → 机器人 里选一个"
            Return
        End If
        If Not DstBot.IsQqBound Then
            LabChatTitle.Text = "绑定 QQ 后才能查看群消息"
            Return
        End If
        If Not Silent Then LabChatTitle.Text = "正在获取群消息……"
        IsFetching = True
        '群号必须跟着这次请求一起传下去。用共享字段的话，
        '切群后新一轮请求会把字段改成新群号，旧请求回来照样能通过校验，于是串群。
        Dim ReqGroup As String = G.GroupId
        DstBot.FetchMessages(ReqGroup,
            Sub(List_, Err) OnMessages(ReqGroup, List_, Err))
    End Sub

    ''' <summary>
    ''' 一批消息回来了。GroupId 是发起请求时的群号；与当前群不一致就整包丢弃。
    ''' </summary>
    Private Sub OnMessages(GroupId As String, List_ As List(Of DstBot.DstMessage), Err As String)
        If GroupId <> DstBot.GroupNo Then Return
        IsFetching = False
        If Err <> "" Then
            LastError = Err
            LabChatTitle.Text = "获取失败：" & Err
            Return
        End If
        LastError = ""
        Dim Added As Integer = 0
        If List_ IsNot Nothing Then
            For Each Item In List_
                If SeenKeys.Add(Item.Key) Then
                    AllMessages.Add(Item)
                    Added += 1
                End If
            Next
        End If
        If Added > 0 Then
            TrimHistory()
            AppendMessages()
            '有新消息才落盘；写文件放后台线程，别卡 UI。
            '用请求时的群号，防止把 A 群的消息写进 B 群的缓存文件
            Dim Snapshot As New List(Of DstBot.DstMessage)(AllMessages)
            RunInThread(Sub() DstBot.SaveChatCache(GroupId, Snapshot))
        Else
            UpdateHeader()
        End If
    End Sub

    ''' <summary>超出上限时从最旧的开始丢；虚拟化面板会自动回收对应控件。</summary>
    Private Sub TrimHistory()
        If AllMessages.Count <= MaxMessages Then Return
        Dim RemoveCount As Integer = AllMessages.Count - MaxMessages
        For i = 1 To RemoveCount
            AllMessages.RemoveAt(0)
        Next
        '去重集合也跟着瘦身，避免长期运行越来越大
        If SeenKeys.Count > MaxMessages * 2 Then
            SeenKeys.Clear()
            For Each M In AllMessages
                SeenKeys.Add(M.Key)
            Next
        End If
    End Sub

    ''' <summary>把 AllMessages 末尾同步进可见窗口，界面只渲染最近的 VisibleMax 条。</summary>
    Private Sub AppendMessages(Optional Force As Boolean = False)
        Dim Follow As Boolean = Force OrElse IsNearBottom()
        '把窗口挪到最新的一段
        Dim NewStart As Integer = Math.Max(0, AllMessages.Count - VisibleMax)
        Dim OldCount As Integer = Visible.Count
        '起点变了（新增或丢弃）就整窗重灌，量很小
        If NewStart <> ViewStart OrElse Visible.Count <> AllMessages.Count - NewStart Then
            ViewStart = NewStart
            Visible.Clear()
            For i = ViewStart To AllMessages.Count - 1
                Visible.Add(AllMessages(i))
            Next
        ElseIf AllMessages.Count > OldCount Then
            For i = OldCount To AllMessages.Count - 1
                Visible.Add(AllMessages(i))
            Next
        End If
        UpdateHeader()
        If Follow Then
            Dispatcher.BeginInvoke(Sub() PanChatScroll.ScrollToEnd(), DispatcherPriority.Loaded)
        End If
    End Sub

    ''' <summary>往上翻到头时再多加载一页更早的消息。</summary>
    Private Sub LoadOlderPage()
        If ViewStart <= 0 Then
            Hint("没有更早的消息了", HintType.Blue)
            Return
        End If
        Dim NewStart As Integer = Math.Max(0, ViewStart - VisibleMax)
        Dim Prepended As Integer = ViewStart - NewStart
        ViewStart = NewStart
        For i = 1 To Prepended
            Visible.Insert(0, AllMessages(NewStart + Prepended - i))
        Next
    End Sub

    ''' <summary>滚到最顶部时自动往前补一页，历史仍旧可翻。</summary>
    Private Sub PanChatScroll_ScrollChanged(sender As Object, e As ScrollChangedEventArgs)
        If e.VerticalOffset <= 2 AndAlso e.ExtentHeightChange = 0 AndAlso ViewStart > 0 Then LoadOlderPage()
        '往回补页后要维持在原来的位置，否则会突然跳到最上面
        If e.ExtentHeightChange > 0 AndAlso e.VerticalOffset <= 2 Then
            PanChatScroll.ScrollToVerticalOffset(e.ExtentHeightChange)
        End If
    End Sub
    Private Function IsNearBottom() As Boolean
        Return PanChatScroll.VerticalOffset + PanChatScroll.ViewportHeight >= PanChatScroll.ExtentHeight - 60
    End Function

    Private Sub UpdateHeader()
        Dim G As DstGroup = DstBot.SelectedGroup
        Dim Name As String = If(G Is Nothing, "群", G.GroupName)
        LabChatTitle.Text = $"{Name} · 已加载 {AllMessages.Count} 条"
        LabChatQuota.Text = $"配额 {DstBot.ChatQuotaLeft}/16"
    End Sub


    '头像缓存：内存 + 磁盘（磁盘里是压好的 96px JPEG，几 KB 而已）
    Private Shared ReadOnly AvatarCache As New Dictionary(Of String, BitmapImage)
    Private Shared ReadOnly AvatarLoading As New HashSet(Of String)

    ''' <summary>把头像挂到指定 Border 上。缓存命中立即显示，否则后台下载后回填。</summary>
    Private Sub ApplyAvatar(Target As Border, Qq As String)
        If Target Is Nothing OrElse Qq = "" Then Return
        Target.Tag = Qq '标记当前属于谁，虚拟化回收后防止错位
        '1. 内存缓存
        SyncLock AvatarCache
            Dim Cached As BitmapImage = Nothing
            If AvatarCache.TryGetValue(Qq, Cached) Then
                Target.Background = New ImageBrush(Cached) With {.Stretch = Stretch.UniformToFill}
                Return
            End If
        End SyncLock
        '2. 磁盘缓存：本地小文件，解码很快，可以直接在 UI 线程做
        Dim FilePath As String = DstBot.AvatarCacheFile(Qq)
        If FilePath <> "" AndAlso FileUtils.Exists(FilePath) Then
            Dim Bmp As BitmapImage = LoadAvatarFile(FilePath)
            If Bmp IsNot Nothing Then
                CacheAvatar(Qq, Bmp)
                Target.Background = New ImageBrush(Bmp) With {.Stretch = Stretch.UniformToFill}
                Return
            End If
        End If
        '3. 都没有：后台抓取，避免卡住界面
        SyncLock AvatarLoading
            If Not AvatarLoading.Add(Qq) Then Return '已经在下了
        End SyncLock
        RunInThread(
        Sub()
            Try
                Dim Saved As String = DstBot.EnsureAvatarFile(Qq)
                If Saved = "" Then Return
                Dim Bmp As BitmapImage = LoadAvatarFile(Saved)
                If Bmp Is Nothing Then Return
                CacheAvatar(Qq, Bmp)
                RunInUi(
                Sub()
                    '控件可能已被虚拟化回收给别人了，比对一下
                    If Target.Tag IsNot Nothing AndAlso Target.Tag.ToString() = Qq Then
                        Target.Background = New ImageBrush(Bmp) With {.Stretch = Stretch.UniformToFill}
                    End If
                End Sub)
            Catch ex As Exception
                Logger.Info($"加载头像失败：{Qq}，{ex.Message}")
            Finally
                SyncLock AvatarLoading
                    AvatarLoading.Remove(Qq)
                End SyncLock
            End Try
        End Sub)
    End Sub

    Private Function LoadAvatarFile(FilePath As String) As BitmapImage
        Try
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.UriSource = New Uri(FilePath, UriKind.Absolute)
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.DecodePixelWidth = 68 '34px 显示，2 倍屏按 68 解码足够
            Bmp.EndInit()
            Bmp.Freeze()
            Return Bmp
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>头像缓存上限，超过后按最早写入的顺序淘汰，避免整体清空导致整屏重载。</summary>
    Private Const AvatarCacheMax As Integer = 200
    Private Shared ReadOnly AvatarCacheOrder As New Queue(Of String)

    Private Sub CacheAvatar(Qq As String, Bmp As BitmapImage)
        SyncLock AvatarCache
            If Not AvatarCache.ContainsKey(Qq) Then
                '已满：从最早的开始淘汰，一次腾出 1/4 的量，减少淘汰频率
                If AvatarCacheOrder.Count >= AvatarCacheMax Then
                    Dim DropCount As Integer = Math.Max(1, AvatarCacheMax \ 4)
                    For i = 1 To DropCount
                        If AvatarCacheOrder.Count = 0 Then Exit For
                        AvatarCache.Remove(AvatarCacheOrder.Dequeue())
                    Next
                End If
                AvatarCacheOrder.Enqueue(Qq)
            End If
            AvatarCache(Qq) = Bmp
        End SyncLock
    End Sub
    Private Function BuildMessageRow(Item As DstBot.DstMessage, IsMine As Boolean) As FrameworkElement
        Dim Row As New Grid With {.Margin = New Thickness(0, 0, 0, 10)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        '头像
        Dim Avatar As New Border With {
            .Width = 34, .Height = 34, .CornerRadius = New CornerRadius(17),
            .VerticalAlignment = VerticalAlignment.Top, .Margin = New Thickness(0, 2, 8, 0),
            .Background = New SolidColorBrush(BackColorSub)
        }
        ApplyAvatar(Avatar, Item.UserQq)

        '气泡
        Dim Bubble As New Border With {
            .CornerRadius = New CornerRadius(8),
            .Padding = New Thickness(11, 8, 11, 9),
            .MaxWidth = 430,
            .HorizontalAlignment = If(IsMine, HorizontalAlignment.Right, HorizontalAlignment.Left),
            .Background = New SolidColorBrush(If(IsMine, BubbleMineBack, BubbleOtherBack))
        }
        Dim Inner As New StackPanel
        Inner.Children.Add(New TextBlock With {
            .Text = Item.UserName & If(Item.TimeText = "", "", "　" & Item.TimeText),
            .FontSize = 11, .Opacity = 0.85, .Margin = New Thickness(0, 0, 0, 4),
            .Foreground = New SolidColorBrush(If(IsMine, BubbleMineText, BubbleMetaText)),
            .HorizontalAlignment = If(IsMine, HorizontalAlignment.Right, HorizontalAlignment.Left)
        })
        '正文颜色跟随气泡
        Dim Body As FrameworkElement = BuildContent(Item.Content)
        If TypeOf Body Is TextBlock Then
            CType(Body, TextBlock).Foreground = New SolidColorBrush(If(IsMine, BubbleMineText, BubbleOtherText))
        End If
        Inner.Children.Add(Body)
        Bubble.Child = Inner

        If IsMine Then
            Grid.SetColumn(Bubble, 1)
            Row.Children.Add(Bubble)
            Grid.SetColumn(Avatar, 2)
            Avatar.Margin = New Thickness(8, 2, 0, 0)
            Row.Children.Add(Avatar)
        Else
            Row.Children.Add(Avatar)
            Grid.SetColumn(Bubble, 1)
            Row.Children.Add(Bubble)
        End If
        Return Row
    End Function

    ''' <summary>把一条消息的 CQ 内容渲染成 文本 / 图片 / 视频 / 文件。</summary>
    Private Function BuildContent(Content As String) As FrameworkElement
        '整条是卡片 JSON（有的后端不加 CQ 包裹，甚至前面还带一段残片）
        Dim Card As FrameworkElement = TryBuildCard(Content)
        If Card IsNot Nothing Then Return Card
        Dim Segments = DstBot.ParseCq(Content)
        Dim Panel As New WrapPanel With {.MaxWidth = 400}
        If Segments.Count = 0 Then
            Panel.Children.Add(New TextBlock With {.Text = "", .FontSize = 13})
            Return Panel
        End If
        For Each Seg In Segments
            Select Case Seg.Kind
                Case "text"
                    If Seg.Text.Trim() <> "" Then
                        Panel.Children.Add(New TextBlock With {
                            .Text = Seg.Text, .FontSize = 13, .LineHeight = 20,
                            .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 0, 2, 2)
                        })
                    End If
                Case "at"
                    Panel.Children.Add(New TextBlock With {
                        .Text = Seg.Text, .FontSize = 13, .Margin = New Thickness(0, 0, 3, 2),
                        .Foreground = New SolidColorBrush(Color3)
                    })
                Case "face"
                    Panel.Children.Add(New TextBlock With {.Text = "[表情]", .FontSize = 13, .Opacity = 0.75, .Margin = New Thickness(0, 0, 3, 2)})
                Case "image"
                    Panel.Children.Add(BuildImageBlock(Seg.Url))
                Case "json"
                    Panel.Children.Add(BuildJsonCard(Seg.Text))
                Case "xml"
                    Panel.Children.Add(BuildXmlCard(Seg.Text))
                Case "video"
                    Panel.Children.Add(BuildPlaceholder("▶ 视频", BackColorSub))
                Case "file"
                    If DstBot.IsVideoFile(Seg.FileName) AndAlso Seg.Url.StartsWithF("http") Then
                        Panel.Children.Add(BuildVideoBlock(Seg.FileName, Seg.Url, Seg.FileSize))
                    ElseIf DstBot.IsImageFile(Seg.FileName) AndAlso Seg.Url.StartsWithF("http") Then
                        Panel.Children.Add(BuildImageBlock(Seg.Url))
                    Else
                        Panel.Children.Add(BuildPlaceholder("📄 " & If(Seg.FileName = "", "文件", Seg.FileName), BackColorSub))
                    End If
                Case "record"
                    Panel.Children.Add(BuildPlaceholder("🎤 语音", BackColorSub))
                Case Else
                    Panel.Children.Add(New TextBlock With {.Text = Seg.Text, .FontSize = 13, .Opacity = 0.7, .Margin = New Thickness(0, 0, 3, 2)})
            End Select
        Next
        Return Panel
    End Function

    Private Function BuildImageBlock(Url As String) As FrameworkElement
        If String.IsNullOrWhiteSpace(Url) Then Return BuildPlaceholder("[图片]", BackColorSub)
        Dim Img As New Image With {
            .MaxWidth = 220, .MaxHeight = 240, .Stretch = Stretch.Uniform,
            .Margin = New Thickness(0, 2, 3, 3), .Cursor = Cursors.Hand,
            .ToolTip = "点击放大预览"
        }
        Try
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.UriSource = New Uri(Url, UriKind.Absolute)
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.DecodePixelWidth = 440
            Bmp.EndInit()
            Img.Source = Bmp
        Catch ex As Exception
            Return BuildPlaceholder("[图片]", BackColorSub)
        End Try
        Dim Wrap As New Border With {
            .CornerRadius = New CornerRadius(6), .ClipToBounds = True,
            .Margin = New Thickness(0, 2, 3, 3), .Child = Img
        }
        '点击放大
        AddHandler Img.MouseLeftButtonUp, Sub(s As Object, e As MouseButtonEventArgs)
                                              e.Handled = True
                                              OpenPreview(Url)
                                          End Sub
        AttachImageMenu(Wrap, Url)
        Return Wrap
    End Function

    ''' <summary>给消息里的图片挂右键菜单：另存为 / 设置为背景。</summary>
    Private Sub AttachImageMenu(Target As FrameworkElement, Url As String)
        Dim Menu As New ContextMenu()
        Dim ItemSave As New MenuItem With {.Header = "另存为"}
        AddHandler ItemSave.Click, Sub() SaveImageAs(Url)
        Dim ItemBack As New MenuItem With {.Header = "设置为背景"}
        AddHandler ItemBack.Click, Sub() SetImageAsBackground(Url)
        Menu.Items.Add(ItemSave)
        Menu.Items.Add(ItemBack)
        Target.ContextMenu = Menu
    End Sub

    ''' <summary>从地址里猜扩展名；猜不到按 jpg 处理（WPF 解码不看扩展名）。</summary>
    Private Function GuessImageExtension(Url As String) As String
        Try
            Dim PathOnly As String = Url.Split("?"c)(0)
            Dim Dot As Integer = PathOnly.LastIndexOf("."c)
            If Dot >= 0 AndAlso PathOnly.Length - Dot <= 6 Then
                Dim Ext As String = PathOnly.Substring(Dot + 1).ToLowerInvariant()
                If Ext.Length >= 2 AndAlso Ext.All(Function(C) Char.IsLetterOrDigit(C)) Then Return Ext
            End If
        Catch
        End Try
        Return "jpg"
    End Function

    ''' <summary>另存为：直接走 HTTPS 取文件。</summary>
    Private Sub SaveImageAs(Url As String)
        Try
            Dim Dialog As New Microsoft.Win32.SaveFileDialog With {
                .Title = "保存图片",
                .FileName = "群图片-" & Date.Now.ToString("yyyyMMdd-HHmmss") & "." & GuessImageExtension(Url),
                .Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|所有文件|*.*"}
            If Dialog.ShowDialog() <> True Then Return
            Dim Target As String = Dialog.FileName
            RunInThread(
            Sub()
                Try
                    '不用多线程下载引擎：QQ 图片地址不一定支持分段请求，
                    '走分片反而会失败，这里直接一次性取回整个文件
                    NetDownloadByClient(Url, Target, SimulateBrowserHeaders:=True)
                    RunInUi(Sub() Hint("已保存：" & PathUtils.GetLastPart(Target), HintType.Green))
                Catch ex As Exception
                    Logger.Warn(ex, "保存群图片失败")
                    RunInUi(Sub() Hint("保存失败：" & ex.Message, HintType.Red))
                End Try
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "打开保存对话框失败")
        End Try
    End Sub

    ''' <summary>
    ''' 设置为背景：把图片存进背景图片文件夹，然后立即应用。
    ''' <para>
    ''' 不另做一套背景机制，仍复用设置页那套（PCLPictures + ImgBack）。
    ''' 只是这里不调随机刷新——「设置为背景」点哪张就该显示哪张。
    ''' </para>
    ''' </summary>
    Private Sub SetImageAsBackground(Url As String)
        RunInThread(
        Sub()
            Try
                Dim Folder As String = Paths.Base & "PCLPictures"
                DirectoryUtils.Create(Folder)
                '同名文件不覆盖：同一秒点两次也不至于互相踩
                Dim Target As String = Folder & "群图片-" & Date.Now.ToString("yyyyMMdd-HHmmss") & "." & GuessImageExtension(Url)
                '同上：单次 HTTPS 直接取回，避免分片下载在 CDN 上失败
                NetDownloadByClient(Url, Target, SimulateBrowserHeaders:=True)
                RunInUi(
                Sub()
                    Try
                        FrmMain.ImgBack.Background = New MyBitmap(Target)
                        FrmMain.ImgBack.Visibility = Visibility.Visible
                        FrmMain.UpdateBackgroundAndTitleBar()
                        '设置页那块的张数计数跟着走一遍，但不要重新随机选图
                        PageSetupUI.BackgroundRefresh(True, False)
                        Hint("已设置为背景", HintType.Green, False)
                    Catch ex As Exception
                        Logger.Warn(ex, "应用背景图片失败")
                        Hint("背景应用失败，图片可能不是标准格式", HintType.Red)
                    End Try
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "下载背景图片失败")
                RunInUi(Sub() Hint("设置为背景失败：" & ex.Message, HintType.Red))
            End Try
        End Sub)
    End Sub

    Private Sub OpenPreview(Url As String)
        Try
            Dim Win As New WindowImagePreview(Url)
            Win.ShowDialog()
        Catch ex As Exception
            Logger.Warn(ex, "打开图片预览失败")
            Hint("图片预览失败：" & ex.Message, HintType.Red)
        End Try
    End Sub


    Private Function BuildPlaceholder(Text As String, Back As MyColor) As FrameworkElement
        Return New Border With {
            .CornerRadius = New CornerRadius(4),
            .Background = New SolidColorBrush(Back),
            .Padding = New Thickness(8, 5, 8, 5),
            .Margin = New Thickness(0, 2, 3, 3),
            .Child = New TextBlock With {.Text = Text, .FontSize = 12.5, .Opacity = 0.85}
        }
    End Function

    ''' <summary>视频文件：显示可点击的播放块，点开独立窗口播放。</summary>
    Private Function BuildVideoBlock(FileName As String, Url As String, Optional ExpectedSize As Long = 0) As FrameworkElement
        Dim Shell As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Background = New SolidColorBrush(BackColorSub),
            .BorderBrush = New SolidColorBrush(Color3),
            .BorderThickness = New Thickness(1),
            .Margin = New Thickness(0, 3, 3, 3),
            .Padding = New Thickness(10, 8, 12, 8),
            .Cursor = Cursors.Hand,
            .ToolTip = "点击播放"
        }
        Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal}
        Row.Children.Add(New TextBlock With {
            .Text = "▶", .FontSize = 18, .Margin = New Thickness(2, 0, 10, 0),
            .VerticalAlignment = VerticalAlignment.Center,
            .Foreground = New SolidColorBrush(Color3)
        })
        Dim Texts As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Texts.Children.Add(New TextBlock With {
            .Text = If(FileName = "", "视频", FileName), .FontSize = 13,
            .MaxWidth = 240, .TextTrimming = TextTrimming.CharacterEllipsis
        })
        Texts.Children.Add(New TextBlock With {
            .Text = "视频 · 点击在启动器内播放", .FontSize = 11, .Opacity = 0.55, .Margin = New Thickness(0, 2, 0, 0)
        })
        Row.Children.Add(Texts)
        Shell.Child = Row
        AddHandler Shell.MouseLeftButtonUp, Sub(s As Object, e As MouseButtonEventArgs)
                                                e.Handled = True
                                                OpenVideo(FileName, Url, ExpectedSize)
                                            End Sub
        Return Shell
    End Function

    Private Sub OpenVideo(FileName As String, Url As String, Optional ExpectedSize As Long = 0)
        Try
            WindowVideoPreview.ShowVideo(Url, If(FileName = "", "视频", FileName), ExpectedSize)
        Catch ex As Exception
            Logger.Warn(ex, "打开视频窗口失败")
            Hint("视频播放失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#Region "JSON / XML 卡片"

    ''' <summary>判断这条内容是不是卡片 JSON，是就返回渲染好的卡片，否则返回 Nothing。</summary>
    Private Function TryBuildCard(Content As String) As FrameworkElement
        Dim Text As String = If(Content, "").Trim()
        If Text = "" Then Return Nothing
        Dim LooksLikeCard As Boolean =
            (Text.StartsWithF("{") AndAlso (Text.Contains("""meta""") OrElse Text.Contains("""app""") OrElse Text.Contains("""config"""))) OrElse
            Text.Contains("""config""") OrElse Text.Contains("""meta""")
        If Not LooksLikeCard Then Return Nothing
        Dim Card As FrameworkElement = BuildJsonCard(Text)
        Return Card
    End Function

    ''' <summary>尽最大努力把五花八门的卡片 JSON 解析出来。</summary>
    Private Function ParseCardJson(Text As String) As JObject
        '1. 原样
        Try
            Return JObject.Parse(Text)
        Catch ex As Exception
        End Try
        '2. 前面有残片（比如只有 "标题","config":{...} 而缺了开头的 {"prompt":），补个外壳
        Dim FirstBrace As Integer = Text.IndexOf("{"c)
        If FirstBrace > 0 Then
            Try
                Return JObject.Parse(Text.Substring(FirstBrace))
            Catch ex As Exception
            End Try
        End If
        '3. 补个 {"prompt":"... 的外壳（有的后端把开头截掉了）
        Dim Q As String = """"
        If Text.Contains(Q & "config" & Q) OrElse Text.Contains(Q & "meta" & Q) Then
            Try
                Return JObject.Parse("{" & Q & "prompt" & Q & ":" & Q & Text.TrimStart(","c))
            Catch ex As Exception
            End Try
        End If
        Return Nothing
    End Function

    ''' <summary>JSON 被截断时，用正则从原文里抠标题、描述、图标拼一张卡。</summary>
    Private Function BuildCardFromRaw(Text As String) As FrameworkElement
        Dim Title As String = RegexPick(Text, "title")
        Dim Desc As String = RegexPick(Text, "desc")
        Dim Icon As String = RegexPick(Text, "icon")
        If Icon = "" Then Icon = RegexPick(Text, "preview")
        If Title = "" AndAlso Desc = "" AndAlso Icon = "" Then Return Nothing
        If Title = "" Then Title = "卡片消息"
        If Icon <> "" AndAlso Not Icon.StartsWithF("http") Then Icon = "https://" & Icon
        Dim App As String = RegexPick(Text, "app")
        Return BuildCardShell(CardAppName(App), Title, Desc, Icon)
    End Function

    Private Function RegexPick(Text As String, Key As String) As String
        Dim M As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(Text, """" & Key & """\s*:\s*""([^""]*)""")
        If Not M.Success Then Return ""
        Return WebUtility.HtmlDecode(M.Groups(1).Value).Replace("\/", "/").Trim()
    End Function

    Private Function BuildJsonCard(RawData As String) As FrameworkElement
        Try
            Dim Text As String = RawData
            'data= 里常是 URL 编码过的 JSON
            If Text.StartsWithF("%7B") OrElse Text.Contains("%22") Then Text = Uri.UnescapeDataString(Text)
            Dim Json As JObject = ParseCardJson(Text)
            If Json Is Nothing Then
                'JSON 可能被截断了，退一步用正则从原文里抠字段
                Dim Raw As FrameworkElement = BuildCardFromRaw(Text)
                If Raw IsNot Nothing Then Return Raw
                Logger.Info("卡片 JSON 无法解析，退化为占位：" & Text.Substring(0, Math.Min(120, Text.Length)))
                Return BuildPlaceholder("[卡片消息]", BackColorSub)
            End If
            Dim App As String = If(Json("app"), "").ToString()
            Dim Prompt As String = If(Json("prompt"), "").ToString()
            Dim Meta As JObject = TryCast(Json("meta"), JObject)

            '挑第一个对象型的 meta 子项（news / detail_1 / contact / music …）
            Dim Detail As JObject = Nothing
            If Meta IsNot Nothing Then
                For Each Prop In Meta.Properties()
                    If Prop.Value.Type = JTokenType.Object Then
                        Detail = CType(Prop.Value, JObject)
                        Exit For
                    End If
                Next
            End If
            If Detail Is Nothing Then Detail = Meta

            Dim Title As String = CardPick(Detail, "title", "desc", "name")
            Dim Desc As String = CardPick(Detail, "desc", "summary", "tag")
            Dim Preview As String = CardPick(Detail, "preview", "icon", "avatar", "image", "thumb")
            If Title = "" Then Title = Prompt
            If Title = "" Then Title = "卡片消息"
            If Preview <> "" AndAlso Not Preview.StartsWithF("http") Then Preview = "https://" & Preview

            Return BuildCardShell(CardAppName(App), Title, Desc, Preview)
        Catch ex As Exception
            Logger.Info(ex, "解析 JSON 卡片失败")
            Return BuildPlaceholder("[卡片消息]", BackColorSub)
        End Try
    End Function

    Private Function BuildXmlCard(RawData As String) As FrameworkElement
        Try
            Dim Text As String = RawData
            If Text.StartsWithF("%3C") Then Text = Uri.UnescapeDataString(Text)
            '优先取 brief 属性作为摘要
            Dim M As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(Text, "brief\s*=\s*""([^""]*)""")
            Dim Title As String = If(M.Success, M.Groups(1).Value, "")
            Dim TitleM As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(Text, "<title[^>]*>([^<]+)</title>")
            If Title = "" AndAlso TitleM.Success Then Title = TitleM.Groups(1).Value
            If Title = "" Then Title = "卡片消息"
            Return BuildCardShell("卡片", Title, "", "")
        Catch ex As Exception
            Return BuildPlaceholder("[卡片消息]", BackColorSub)
        End Try
    End Function

    Private Function CardPick(Obj As JObject, ParamArray Keys As String()) As String
        If Obj Is Nothing Then Return ""
        For Each Key In Keys
            Dim V As JToken = Obj(Key)
            If V IsNot Nothing AndAlso V.Type = JTokenType.String Then
                Dim S As String = WebUtility.HtmlDecode(V.ToString()).Trim()
                If S <> "" Then Return S
            End If
        Next
        Return ""
    End Function

    Private Function CardAppName(App As String) As String
        If App = "" Then Return "卡片"
        If App.Contains("contact") Then Return "群名片"
        If App.Contains("structmsg") Then Return "分享"
        If App.Contains("miniapp") Then Return "小程序"
        If App.Contains("music") Then Return "音乐"
        If App.Contains("news") Then Return "图文"
        Return "卡片"
    End Function

    ''' <summary>统一的卡片外观：左缩略图 + 右标题/描述 + 左上角类型角标。</summary>
    Private Function BuildCardShell(AppName As String, Title As String, Desc As String, Preview As String) As FrameworkElement
        Dim Shell As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Background = New SolidColorBrush(BackColorMain),
            .BorderBrush = New SolidColorBrush(BackColorSub),
            .BorderThickness = New Thickness(1),
            .Margin = New Thickness(0, 3, 3, 3),
            .Width = 280,
            .Cursor = Cursors.Hand
        }
        Dim Grid As New Grid With {.Margin = New Thickness(10, 9, 10, 9)}
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        Grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})

        '缩略图或文字块
        Dim Thumb As New Border With {
            .Width = 48, .Height = 48, .CornerRadius = New CornerRadius(6),
            .Margin = New Thickness(0, 0, 10, 0), .ClipToBounds = True,
            .Background = New SolidColorBrush(Color2)
        }
        If Preview <> "" Then
            Try
                Dim Bmp As New BitmapImage()
                Bmp.BeginInit()
                Bmp.UriSource = New Uri(Preview, UriKind.Absolute)
                Bmp.CacheOption = BitmapCacheOption.OnLoad
                Bmp.DecodePixelWidth = 96
                Bmp.EndInit()
                Thumb.Background = New ImageBrush(Bmp) With {.Stretch = Stretch.UniformToFill}
            Catch ex As Exception
                Thumb.Child = New TextBlock With {
                    .Text = If(AppName = "", "卡", AppName.Substring(0, 1)), .FontSize = 20, .Foreground = Brushes.White,
                    .HorizontalAlignment = HorizontalAlignment.Center, .VerticalAlignment = VerticalAlignment.Center
                }
            End Try
        Else
            Thumb.Child = New TextBlock With {
                .Text = If(AppName = "", "卡", AppName.Substring(0, 1)), .FontSize = 20, .Foreground = Brushes.White,
                .HorizontalAlignment = HorizontalAlignment.Center, .VerticalAlignment = VerticalAlignment.Center
            }
        End If
        Grid.Children.Add(Thumb)

        '标题 + 描述
        Dim Texts As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Texts.Children.Add(New TextBlock With {
            .Text = Title, .FontSize = 13, .FontWeight = FontWeights.Bold,
            .TextTrimming = TextTrimming.CharacterEllipsis, .MaxWidth = 190
        })
        If Desc <> "" Then
            Texts.Children.Add(New TextBlock With {
                .Text = Desc, .FontSize = 11.5, .Opacity = 0.6, .Margin = New Thickness(0, 3, 0, 0),
                .TextWrapping = TextWrapping.Wrap, .MaxWidth = 190, .MaxHeight = 32,
                .TextTrimming = TextTrimming.CharacterEllipsis
            })
        End If
        Grid.SetColumn(Texts, 1)
        Grid.Children.Add(Texts)

        '角标
        Dim Badge As New TextBlock With {
            .Text = AppName, .FontSize = 10.5, .Opacity = 0.45, .Margin = New Thickness(0, 6, 0, 0)
        }
        Grid.SetRow(Badge, 1)
        Grid.SetColumnSpan(Badge, 2)
        Grid.Children.Add(Badge)

        '点一下把标题和描述弹出来（卡片里的链接不跟着走）
        AddHandler Shell.MouseLeftButtonUp, Sub(s As Object, e As MouseEventArgs)
                                                 MyMsgBox(Title & If(Desc = "", "", vbCrLf & Desc), AppName)
                                             End Sub
        Shell.Child = Grid
        Return Shell
    End Function

#End Region


End Class
