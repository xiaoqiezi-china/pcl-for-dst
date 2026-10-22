Imports System.Windows.Threading

''' <summary>
''' 百宝箱。播放器卡片是 PCL 背景音乐引擎（ModMusic / NAudio）外面包的一层界面。
''' 拖拽排序的事件挂在列表容器上而不是行上：行在重排时会被重建，
''' 挂在行上的话鼠标一动就丢事件，表现为“拖了一半就断”。
''' </summary>
Public Class PageOtherTest

    '图标路径（自己画的，坐标基准 24×24）
    Private Const IconNote As String =
        "M9,2 L9,16.4 A4.2,4.2 0 1 0 11,20 L11,7.2 L20,5.2 L20,14.4 A4.2,4.2 0 1 0 22,18 L22,2 Z"
    Private Const IconPrev As String = "M20,3 L6,12 L20,21 Z M5,3 L2,3 L2,21 L5,21 Z"
    Private Const IconNext As String = "M4,3 L18,12 L4,21 Z M19,3 L22,3 L22,21 L19,21 Z"
    Private Const IconPlay As String = "M6,3 L20,12 L6,21 Z"
    Private Const IconPause As String = "M6,3 L10,3 L10,21 L6,21 Z M14,3 L18,3 L18,21 L14,21 Z"

#Region "百宝箱遗留接口"

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub Jrrp()
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub RubbishClear()
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        If ShowHint Then Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        If ShowHint Then Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Function GetRandomCave() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function
    Public Shared Function GetRandomHint() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function
    Public Shared Function GetRandomPresetHint() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function

#End Region

#Region "播放器"

    Private Timer As DispatcherTimer
    Private IsLoad As Boolean = False
    Private Seeking As Boolean = False

    '拖拽状态
    Private DragIndex As Integer = -1
    Private DragId As Long = -1
    Private DragStartY As Double = 0
    Private DragActive As Boolean = False
    '每行高度（含间距），拖拽换算用
    Private Const RowHeight As Double = 34

    Private Sub PageOtherTest_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        BtnMusicSetup.Logo = Logo.IconButtonSetup
        InitVolume()
        PathMusicNote.Data = Geometry.Parse(IconNote)
        PathPrev.Data = Geometry.Parse(IconPrev)
        PathNext.Data = Geometry.Parse(IconNext)
        '拖拽事件挂在容器上，行被重建也不会中断
        AddHandler PanMusicList.PreviewMouseMove, AddressOf ListMouseMove
        AddHandler PanMusicList.PreviewMouseLeftButtonUp, AddressOf ListMouseUp
        If Timer Is Nothing Then
            Timer = New DispatcherTimer With {.Interval = New TimeSpan(0, 0, 0, 0, 250)}
            AddHandler Timer.Tick, AddressOf TickUpdate
        End If
        Timer.Start()
        If Not IsLoad Then
            IsLoad = True
            RefreshMusicList()
        End If
        TickUpdate(Nothing, Nothing)
    End Sub

    Private Sub PageOtherTest_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        If Timer IsNot Nothing Then Timer.Stop()
    End Sub

    ''' <summary>每 250ms 刷一次标题、进度、时间和播放按钮图标。</summary>
    Private Sub TickUpdate(sender As Object, e As EventArgs)
        Try
            Dim Cur As String = ModMusic.MusicCurrentAddress
            If Cur = "" Then
                LabTrack.Text = "未在播放"
            Else
                '网易云的缓存文件叫「1394165835.m4a」，要显示成歌名
                Dim CloudId As Long = DstNetease.NeIdFromCachePath(Cur)
                LabTrack.Text = If(CloudId > 0, DstNetease.NeSongTitle(CloudId),
                                   PathUtils.GetFileNameWithoutExtension(Cur))
            End If
            Dim Total As Double = ModMusic.MusicDuration.TotalSeconds
            Dim Pos As Double = ModMusic.MusicPosition.TotalSeconds
            If SeekHoldTicks > 0 Then
                SeekHoldTicks -= 1
            ElseIf Not Seeking Then
                SliderSeek.Value = If(Total <= 0, 0, Math.Min(1000, Pos / Total * 1000))
            End If
            LabTime.Text = FormatTime(Pos) & " / " & FormatTime(Total)
            Dim Playing As Boolean = ModMusic.MusicState = ModMusic.MusicStates.Play
            PathPlay.Data = Geometry.Parse(If(Playing, IconPause, IconPlay))
            BtnPlay.ToolTip = If(Playing, "暂停", "播放")
        Catch ex As Exception
            Logger.Warn(ex, "刷新播放器界面失败")
        End Try
    End Sub

    Private Shared Function FormatTime(Seconds As Double) As String
        If Seconds < 0 Then Seconds = 0
        Dim S As Integer = CInt(Seconds)
        Return $"{S \ 60}:{(S Mod 60).ToString("00")}"
    End Function

    ''' <summary>重画播放列表。</summary>
    Public Sub RefreshMusicList()
        If PanMusicList Is Nothing Then Return
        PanMusicList.Children.Clear()
        Dim All As List(Of String) = ModMusic.MusicAllList
        Dim Cloud As List(Of Long) = DstNetease.NeQueueSnapshot()
        Dim HasLocal As Boolean = All IsNot Nothing AndAlso All.Count > 0
        If Not HasLocal AndAlso Cloud.Count = 0 Then
            PanMusicList.Children.Add(New TextBlock With {
                .Text = "还没有歌曲。可以在播放器设置里搜索网易云添加，或把音频文件放进 PCL\Musics 文件夹。",
                .Opacity = 0.55, .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(6, 6, 0, 0)
            })
            Return
        End If
        If HasLocal Then
            For Each File In All.ToList()
                PanMusicList.Children.Add(BuildMusicRow(File))
            Next
        End If
        If Cloud.Count > 0 Then
            PanMusicList.Children.Add(BuildCloudHeader(Cloud.Count))
            For i = 0 To Cloud.Count - 1
                PanMusicList.Children.Add(BuildCloudRow(Cloud(i), i))
            Next
        End If
    End Sub

    ''' <summary>「网易云音乐（N 首）」标题行 + 一键清空。</summary>
    Private Function BuildCloudHeader(Count As Integer) As FrameworkElement
        '右边留出滚动条的宽度，否则“清空”按钮会压在滚动条上
        Dim G As New Grid With {.Margin = New Thickness(4, 8, 14, 4)}
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        G.Children.Add(New TextBlock With {
            .Text = $"网易云音乐（{Count} 首，按住拖动可排序）", .FontSize = 11, .Opacity = 0.45,
            .VerticalAlignment = VerticalAlignment.Center
        })
        Dim BtnProbe As New MyButton With {
            .Text = "检测", .Height = 24, .MinWidth = 56, .ToolTip = "逐首请求播放地址，能拿到才算可播"
        }
        Grid.SetColumn(BtnProbe, 1)
        G.Children.Add(BtnProbe)
        Dim BtnClear As New MyButton With {
            .Text = "清空", .Height = 24, .MinWidth = 60, .ColorType = MyButton.ColorState.Red,
            .Margin = New Thickness(6, 0, 0, 0)
        }
        Grid.SetColumn(BtnClear, 1)
        G.Children.Add(BtnClear)
        '两个按钮并排
        G.ColumnDefinitions.Clear()
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.SetColumn(BtnProbe, 1)
        Grid.SetColumn(BtnClear, 2)

        AddHandler BtnProbe.Click,
            Sub()
                If BtnProbe.IsEnabled = False Then Return
                If DstNetease.NeQueueSnapshot().Count = 0 Then Return
                BtnProbe.IsEnabled = False
                BtnProbe.Text = "检测中"
                Hint("正在逐首检测（4 并发）……", HintType.Blue, False)
                RunInThread(
                Sub()
                    DstNetease.NeProbeQueueStatus(4,
                        Sub(Done As Integer, All As Integer)
                            RunInUi(Sub() BtnProbe.Text = $"{Done}/{All}")
                        End Sub)
                    RunInUi(
                    Sub()
                        BtnProbe.IsEnabled = True
                        BtnProbe.Text = "检测"
                        Hint("检测完成", HintType.Green, False)
                        RefreshMusicList()
                    End Sub)
                End Sub)
            End Sub
        AddHandler BtnClear.Click,
            Sub()
                If DstNetease.NeQueueSnapshot().Count = 0 Then Return
                If MyMsgBox("要从播放列表里移除全部网易云歌曲吗？" & vbCrLf &
                            "（只清列表，不影响你的网易云歌单和本地缓存）",
                            "清空播放列表", "清空", "取消", IsWarn:=True) <> 1 Then Return
                DstNetease.NeQueueClear()
                RefreshMusicList()
                Hint("播放列表已清空", HintType.Blue, False)
            End Sub
        Return G
    End Function

    Private Function BuildMusicRow(File As String) As FrameworkElement
        Dim Row As New Border With {
            .Height = 32, .CornerRadius = New CornerRadius(4),
            .Margin = New Thickness(0, 0, 0, 2), .Padding = New Thickness(8, 0, 8, 0),
            .Background = Brushes.Transparent, .Cursor = Cursors.Hand, .ToolTip = File
        }
        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Title As New TextBlock With {
            .Text = PathUtils.GetFileNameWithoutExtension(File), .FontSize = 12.5,
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis
        }
        If File = ModMusic.MusicCurrentAddress Then
            Title.Foreground = New SolidColorBrush(BackColorMain)
            Title.FontWeight = FontWeights.Bold
        End If
        G.Children.Add(Title)
        Dim Info As New TextBlock With {
            .Text = PathUtils.GetExtension(File).ToUpper(), .FontSize = 10.5, .Opacity = 0.45,
            .VerticalAlignment = VerticalAlignment.Center
        }
        Grid.SetColumn(Info, 1)
        G.Children.Add(Info)
        Row.Child = G
        AddHandler Row.MouseEnter, Sub() Row.Background = New SolidColorBrush(BackColorSub)
        AddHandler Row.MouseLeave, Sub() Row.Background = Brushes.Transparent
        AddHandler Row.MouseLeftButtonUp,
            Sub()
                ModMusic.MusicPlayFile(File)
                RefreshMusicList()
                TickUpdate(Nothing, Nothing)
            End Sub
        Return Row
    End Function

    ''' <summary>网易云队列行：拖拽排序、右侧 × 删除、点击播放。</summary>
    Private Function BuildCloudRow(Id As Long, Index As Integer) As FrameworkElement
        Dim Row As New Border With {
            .Height = 32, .CornerRadius = New CornerRadius(4),
            .Margin = New Thickness(0, 0, 0, 2), .Padding = New Thickness(8, 0, 6, 0),
            .Cursor = Cursors.Hand, .ToolTip = "拖动可排序，点一下播放"
        }
        '正在拖的那一行：半透明 + 高亮，让人看得出拿起来了
        If DragActive AndAlso Id = DragId Then
            Row.Background = New SolidColorBrush(BackColorSub)
            Row.Opacity = 0.55
            Row.BorderThickness = New Thickness(2, 0, 0, 0)
            Row.BorderBrush = New SolidColorBrush(BackColorMain)
        Else
            Row.Background = Brushes.Transparent
            Row.BorderThickness = New Thickness(0)
        End If

        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        '序号，拖拽时给个参照
        Dim Num As New TextBlock With {
            .Text = (Index + 1).ToString(), .FontSize = 10.5, .Opacity = 0.35, .Width = 22,
            .VerticalAlignment = VerticalAlignment.Center
        }
        G.Children.Add(Num)

        '不可播的曲目：整行置灰，播放时会自动跳过
        Dim Skippable As Boolean = DstNetease.NeSongSkippable(Id)
        Dim Title As New TextBlock With {
            .Text = DstNetease.NeSongTitle(Id), .FontSize = 12.5,
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis
        }
        If Skippable Then Title.Opacity = 0.4
        Grid.SetColumn(Title, 1)
        G.Children.Add(Title)

        '标签：跳过 / VIP / 无版权
        Dim TagText As String = ""
        If DstNetease.NeSongStatus(Id) = "vip" Then
            TagText = "跳过"
        ElseIf DstNetease.NeSongStatus(Id) = "dead" Then
            TagText = "无版权"
        ElseIf DstNetease.NeSongIsPaid(Id) Then
            TagText = "VIP"
        End If
        If TagText <> "" Then
            Dim TagBox As New Border With {
                .CornerRadius = New CornerRadius(3), .Padding = New Thickness(5, 1, 5, 1),
                .Margin = New Thickness(0, 0, 6, 0), .VerticalAlignment = VerticalAlignment.Center,
                .Background = New SolidColorBrush(If(Skippable, New MyColor(90, 90, 90), New MyColor(200, 60, 60)))
            }
            TagBox.Child = New TextBlock With {
                .Text = TagText, .FontSize = 10, .Foreground = Brushes.White,
                .VerticalAlignment = VerticalAlignment.Center
            }
            Grid.SetColumn(TagBox, 2)
            G.Children.Add(TagBox)
            '把删除按钮挤到第 3 列
            G.ColumnDefinitions.Clear()
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Grid.SetColumn(Title, 1)
            Grid.SetColumn(TagBox, 2)
        End If

        Dim BtnDel As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonCross, .Width = 22, .Height = 22, .LogoScale = 0.55,
            .VerticalAlignment = VerticalAlignment.Center, .ToolTip = "从播放列表移除"
        }
        Grid.SetColumn(BtnDel, G.ColumnDefinitions.Count - 1)
        G.Children.Add(BtnDel)
        Row.Child = G
        Row.Tag = Index

        AddHandler BtnDel.Click,
            Sub()
                DstNetease.NeQueueRemove(Id)
                RefreshMusicList()
            End Sub
        AddHandler Row.MouseEnter,
            Sub()
                If Not (DragActive AndAlso Id = DragId) Then Row.Background = New SolidColorBrush(BackColorSub)
            End Sub
        AddHandler Row.MouseLeave,
            Sub()
                If Not (DragActive AndAlso Id = DragId) Then Row.Background = Brushes.Transparent
            End Sub
        '只记录按下，移动与松开都在列表容器上处理
        AddHandler Row.PreviewMouseLeftButtonDown,
            Sub(sender As Object, e As MouseButtonEventArgs)
                If BtnDel.IsMouseOver Then Return
                DragIndex = Index
                DragId = Id
                DragStartY = e.GetPosition(PanMusicList).Y
                DragActive = False
            End Sub
        Return Row
    End Function

    ''' <summary>列表上的拖动：按住上下移动就实时重排。</summary>
    Private Sub ListMouseMove(sender As Object, e As MouseEventArgs)
        If DragIndex < 0 OrElse e.LeftButton <> MouseButtonState.Pressed Then Return
        Dim Pos As Point = e.GetPosition(PanMusicList)
        Dim Delta As Double = Pos.Y - DragStartY
        If Not DragActive Then
            If Math.Abs(Delta) < 6 Then Return
            DragActive = True
            RefreshMusicList()   ' 把当前行标成“被拿起”的样子
            Return
        End If
        Dim Total As Integer = DstNetease.NeQueueSnapshot().Count
        If Total <= 1 Then Return
        Dim Target As Integer = Math.Max(0, Math.Min(Total - 1, DragIndex + CInt(Math.Round(Delta / RowHeight))))
        If Target = DragIndex Then Return
        DstNetease.NeQueueMove(DragIndex, Target)
        DragIndex = Target
        DragStartY = Pos.Y
        RefreshMusicList()
    End Sub

    ''' <summary>松手：拖动结束就只收尾，没拖动就当成点击播放。</summary>
    Private Sub ListMouseUp(sender As Object, e As MouseButtonEventArgs)
        If DragIndex < 0 Then Return
        Dim WasDrag As Boolean = DragActive
        Dim ClickedId As Long = DragId
        DragIndex = -1
        DragId = -1
        DragActive = False
        If WasDrag Then
            RefreshMusicList()
            Return
        End If
        If ClickedId <= 0 Then Return
        If DstNetease.NeSongSkippable(ClickedId) Then
            Hint("这首歌已确认无法播放（VIP 专享或已失效），播放时会自动跳过", HintType.Blue)
            Return
        End If
        PlayCloudSong(ClickedId)
    End Sub

    '每次播放请求发一个序号，只有最新一次的提示才有效。
    '否则连点两下时，先失败的那次会把“拿不到地址”的提示盖在已经播起来的画面上。
    Private PlayToken As Integer = 0

    Private Sub PlayCloudSong(Id As Long)
        DstNetease.NePlayingId = Id
        If Not DstNetease.NeIsLoggedIn Then
            Hint("先到播放器设置里登录网易云，否则拿不到播放地址", HintType.Red)
            Return
        End If
        PlayToken += 1
        Dim MyToken As Integer = PlayToken
        '同一首已经在播就不要再发一次请求
        Dim PlayingId As Long = DstNetease.NeIdFromCachePath(ModMusic.MusicCurrentAddress)
        If PlayingId = Id AndAlso ModMusic.MusicState <> ModMusic.MusicStates.Stop Then
            ModMusic.MusicResume()
            TickUpdate(Nothing, Nothing)
            Return
        End If
        Hint("正在获取播放地址……", HintType.Blue, False)
        RunInThread(
        Sub()
            Dim File As String = DstNetease.NeEnsureSongFile(Id)
            RunInUi(
            Sub()
                If MyToken <> PlayToken Then Return   '已经有更新的请求了，这次结果作废
                If File = "" Then
                    If DstNetease.NeSongStatus(Id) = "dead" Then
                        Hint("这首歌已下架或没有版权，已标记为跳过", HintType.Red)
                    Else
                        Hint("这首歌拿不到播放地址（可能是 VIP 专享且无试听）", HintType.Red)
                    End If
                    RefreshMusicList()
                Else
                    If DstNetease.NeLastUrlWasTrial Then
                        Hint("这首歌只能试听片段", HintType.Blue, False)
                    End If
                    ModMusic.MusicPlayFile(File)
                    RefreshMusicList()
                    TickUpdate(Nothing, Nothing)
                End If
            End Sub)
        End Sub)
    End Sub

    Private Sub BtnPlay_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles BtnPlay.MouseLeftButtonUp
        '点这个按钮后不要再触发列表的行点击逻辑
        e.Handled = True
        Select Case ModMusic.MusicState
            Case ModMusic.MusicStates.Play
                '正在播 → 暂停，绝不重新加载
                ModMusic.MusicPause()
            Case ModMusic.MusicStates.Pause
                '暂停中 → 继续播当前这首
                ModMusic.MusicResume()
            Case Else
                '完全停了才去找一首来播
                Dim Playing As Long = DstNetease.NeIdFromCachePath(ModMusic.MusicCurrentAddress)
                If Playing > 0 Then
                    PlayCloudSong(Playing)
                    Return
                End If
                Dim NextIndex As Integer = DstNetease.NeNextPlayableIndex(0)
                If NextIndex >= 0 Then
                    PlayCloudSong(DstNetease.NeQueueSnapshot()(NextIndex))
                ElseIf DstNetease.NeQueueSnapshot().Count > 0 Then
                    Hint("播放列表里没有可播放的歌曲（都是 VIP 专享或已失效）", HintType.Red)
                Else
                    Hint("播放列表是空的，先去设置里添加歌曲", HintType.Red)
                End If
        End Select
        TickUpdate(Nothing, Nothing)
    End Sub

    Private Sub BtnNext_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles BtnNext.MouseLeftButtonUp
        '先把网易云队列的下一首找出来；本地有歌时才走 PCL 自己的下一曲
        Dim CloudNext As Long = DstNetease.NePlayOffset(1)
        If CloudNext > 0 Then
            PlayCloudSong(CloudNext)
            Return
        End If
        If ModMusic.MusicAllList IsNot Nothing AndAlso ModMusic.MusicAllList.Any() Then
            ModMusic.MusicControlNext()
            RefreshMusicList()
            TickUpdate(Nothing, Nothing)
            Return
        End If
        Hint("没有下一首了", HintType.Blue, False)
    End Sub

    Private Sub BtnPrev_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles BtnPrev.MouseLeftButtonUp
        If ModMusic.MusicAllList Is Nothing OrElse Not ModMusic.MusicAllList.Any() Then
            Hint("还没有可播放的音乐", HintType.Red)
            Return
        End If
        If ModMusic.MusicPosition.TotalSeconds > 3 Then
            ModMusic.MusicSeekRatio(0)
            TickUpdate(Nothing, Nothing)
            Return
        End If
        Dim CloudPrev As Long = DstNetease.NePlayOffset(-1)
        If CloudPrev > 0 Then
            PlayCloudSong(CloudPrev)
            Return
        End If
        ModMusic.MusicPlayFile(ModMusic.MusicCurrentAddress)
        TickUpdate(Nothing, Nothing)
    End Sub

    '松手后要等播放器把 CurrentTime 真正更新上来，否则定时器会用旧位置把滑块拽回去
    Private SeekHoldTicks As Integer = 0

    Private Sub SliderSeek_Down(sender As Object, e As MouseButtonEventArgs) Handles SliderSeek.PreviewMouseLeftButtonDown
        '按下就进入定位状态，这样点轨道（不只是拖滑块）也能跳
        Seeking = True
        SeekHoldTicks = 0
    End Sub

    Private Sub SliderSeek_Change(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles SliderSeek.ValueChanged
        If Not Seeking Then Return
        ModMusic.MusicSeekRatio(SliderSeek.Value / 1000)
    End Sub

    Private Sub SliderSeek_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles SliderSeek.PreviewMouseLeftButtonUp
        ModMusic.MusicSeekRatio(SliderSeek.Value / 1000)
        '再挡两拍，避免播放器位置还没刷新时被定时器覆盖
        SeekHoldTicks = 2
        Seeking = False
    End Sub

    '==== 音量 ====

    Private Sub InitVolume()
        If SliderVolume Is Nothing Then Return
        AniControlEnabled += 1
        Try
            SliderVolume.Value = Settings.Get(Of Integer)("UiMusicVolume") / 10
            LabVolume.Text = CInt(SliderVolume.Value) & "%"
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Sub BtnVolume_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles BtnVolume.MouseLeftButtonUp
        PopupVolume.IsOpen = Not PopupVolume.IsOpen
    End Sub

    Private Sub SliderVolume_Change(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles SliderVolume.ValueChanged
        'XAML 载入阶段就会触发 ValueChanged，这时页面还没初始化完，整段都要兜住
        Try
            If SliderVolume Is Nothing OrElse LabVolume Is Nothing Then Return
            If Not IsLoad Then Return
            'PCL 的音量取值是 0..1000，滑块用 0..100
            Settings.Set("UiMusicVolume", CInt(SliderVolume.Value * 10))
            LabVolume.Text = CInt(SliderVolume.Value) & "%"
        Catch ex As Exception
            Logger.Warn(ex, "设置音量失败")
        End Try
    End Sub
    Private Sub BtnMusicSetup_Click(sender As Object, e As EventArgs) Handles BtnMusicSetup.Click
        FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.MusicSetup})
    End Sub

#End Region

End Class
