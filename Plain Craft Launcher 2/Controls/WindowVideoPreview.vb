''' <summary>
''' 视频播放窗口。单例（同时只开一个），下载进度可见；不开 AllowsTransparency 以免 MediaElement 黑屏。
''' </summary>
Public Class WindowVideoPreview
    Inherits Window

    Private Shared _Current As WindowVideoPreview

    Private ReadOnly Media As New MediaElement
    Private ReadOnly LabTip As New TextBlock
    Private ReadOnly PbProgress As New ProgressBar
    Private ReadOnly BtnBrowser As New MyButton
    Private ReadOnly BtnLocal As New MyButton
    Private LocalPathNow As String = ""
    Private ReadOnly Url As String
    Private ReadOnly FileName As String
    Private ReadOnly ExpectedSize As Long
    Private PlayingNow As Boolean
    Private Client As Net.WebClient

    ''' <summary>打开（或激活已有的）视频窗口。</summary>
    Public Shared Sub ShowVideo(Url As String, FileName As String, Optional ExpectedSize As Long = 0)
        If _Current IsNot Nothing AndAlso _Current.IsVisible Then
            _Current.Activate()
            Return
        End If
        Dim Win As New WindowVideoPreview(Url, FileName, ExpectedSize)
        _Current = Win
        Win.Show()
    End Sub

    Public Sub New(Url As String, FileName As String, Optional ExpectedSize As Long = 0)
        Me.Url = Url
        Me.ExpectedSize = ExpectedSize
        Me.FileName = If(FileName = "", "视频", FileName)
        Title = Me.FileName
        WindowStyle = WindowStyle.None
        AllowsTransparency = False
        Background = New SolidColorBrush(Color.FromRgb(10, 14, 20))
        ResizeMode = ResizeMode.CanResize
        ShowInTaskbar = False
        WindowStartupLocation = WindowStartupLocation.CenterScreen
        Width = 720
        Height = 460
        If FrmMain IsNot Nothing Then Owner = FrmMain

        Dim Root As New Grid
        Root.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(36)})
        Root.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})

        '顶栏
        Dim Bar As New Border With {
            .Background = New SolidColorBrush(Color.FromRgb(20, 27, 38)),
            .Padding = New Thickness(12, 0, 0, 0)
        }
        Dim BarGrid As New Grid
        BarGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        BarGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim LabName As New TextBlock With {
            .Text = Me.FileName & "　·　点击画面暂停/继续，空格切换，Esc 关闭",
            .FontSize = 12, .VerticalAlignment = VerticalAlignment.Center,
            .Foreground = New SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        BarGrid.Children.Add(LabName)
        Dim BtnClose As New MyButton With {.Text = "关闭", .Width = 68, .Height = 26, .Margin = New Thickness(0, 5, 8, 5)}
        AddHandler BtnClose.Click, Sub() Close()
        Grid.SetColumn(BtnClose, 1)
        BarGrid.Children.Add(BtnClose)
        Bar.Child = BarGrid
        AddHandler Bar.MouseLeftButtonDown, Sub(s As Object, e As MouseButtonEventArgs)
                                                If e.GetPosition(Bar).X < Bar.ActualWidth - 80 Then DragMove()
                                            End Sub
        Root.Children.Add(Bar)

        '播放器
        Media.Stretch = Stretch.Uniform
        Media.LoadedBehavior = MediaState.Manual
        Media.UnloadedBehavior = MediaState.Manual
        Media.Volume = 0.8
        Grid.SetRow(Media, 1)
        Root.Children.Add(Media)

        '提示 + 进度
        Dim TipPanel As New StackPanel With {
            .HorizontalAlignment = HorizontalAlignment.Center,
            .VerticalAlignment = VerticalAlignment.Center,
            .IsHitTestVisible = False
        }
        LabTip.Text = "正在准备视频……"
        LabTip.FontSize = 13
        LabTip.TextWrapping = TextWrapping.Wrap
        LabTip.TextAlignment = TextAlignment.Center
        LabTip.Foreground = New SolidColorBrush(Color.FromArgb(210, 255, 255, 255))
        LabTip.Margin = New Thickness(24, 0, 24, 0)
        TipPanel.Children.Add(LabTip)
        PbProgress.Width = 260
        PbProgress.Height = 4
        PbProgress.Margin = New Thickness(0, 10, 0, 0)
        PbProgress.Minimum = 0
        PbProgress.Maximum = 100
        PbProgress.Foreground = New SolidColorBrush(Color.FromRgb(96, 165, 250))
        PbProgress.Background = New SolidColorBrush(Color.FromRgb(38, 49, 63))
        PbProgress.BorderThickness = New Thickness(0)
        PbProgress.Visibility = Visibility.Collapsed
        TipPanel.Children.Add(PbProgress)
        Grid.SetRow(TipPanel, 1)
        Root.Children.Add(TipPanel)

        Dim BtnRow As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Center,
            .VerticalAlignment = VerticalAlignment.Bottom,
            .Margin = New Thickness(0, 0, 0, 18)
        }
        BtnBrowser.Text = "用浏览器打开"
        BtnBrowser.Height = 30
        BtnBrowser.MinWidth = 116
        BtnBrowser.Margin = New Thickness(8, 0, 0, 0)
        BtnBrowser.Visibility = Visibility.Collapsed
        AddHandler BtnBrowser.Click,
            Sub()
                Try
                    OpenWebsite(Me.Url)
                Catch ex As Exception
                    Hint("打开失败：" & ex.Message, HintType.Red)
                End Try
            End Sub
        BtnLocal.Text = "用系统播放器打开"
        BtnLocal.Height = 30
        BtnLocal.MinWidth = 132
        BtnLocal.Visibility = Visibility.Collapsed
        AddHandler BtnLocal.Click, Sub() OpenBySystem()
        BtnRow.Children.Add(BtnLocal)
        BtnRow.Children.Add(BtnBrowser)
        Grid.SetRow(BtnRow, 1)
        Root.Children.Add(BtnRow)

        Content = Root

        '播放器事件
        AddHandler Media.MediaOpened,
            Sub()
                LabTip.Visibility = Visibility.Collapsed
                PbProgress.Visibility = Visibility.Collapsed
                Media.Play()
                PlayingNow = True
            End Sub
        AddHandler Media.MediaEnded,
            Sub()
                PlayingNow = False
                LabTip.Visibility = Visibility.Visible
                LabTip.Text = "播放结束，点击画面重播"
            End Sub
        AddHandler Media.MediaFailed,
            Sub(s As Object, e As ExceptionRoutedEventArgs)
                ShowError("无法播放（系统缺少解码器，或文件损坏）")
            End Sub
        AddHandler Media.MouseLeftButtonUp,
            Sub()
                If Not PlayingNow AndAlso LabTip.IsVisible AndAlso LabTip.Text.StartsWithF("播放结束") Then
                    Media.Position = TimeSpan.Zero
                    Media.Play()
                    PlayingNow = True
                    LabTip.Visibility = Visibility.Collapsed
                    Return
                End If
                If PlayingNow Then
                    Media.Pause()
                    PlayingNow = False
                    LabTip.Visibility = Visibility.Visible
                    LabTip.Text = "已暂停"
                Else
                    Media.Play()
                    PlayingNow = True
                    LabTip.Visibility = Visibility.Collapsed
                End If
            End Sub
        AddHandler Me.KeyDown, Sub(s As Object, e As KeyEventArgs)
                                   If e.Key = Key.Escape Then Close()
                                   If e.Key = Key.Space Then
                                       If PlayingNow Then
                                           Media.Pause()
                                           PlayingNow = False
                                           LabTip.Visibility = Visibility.Visible
                                           LabTip.Text = "已暂停"
                                       Else
                                           Media.Play()
                                           PlayingNow = True
                                           LabTip.Visibility = Visibility.Collapsed
                                       End If
                                   End If
                               End Sub
        AddHandler Me.Closed,
            Sub()
                Try
                    Client?.CancelAsync()
                Catch ex As Exception
                    Logger.Warn(ex, "取消视频请求失败")
                End Try
                Try
                    Media.Stop()
                    Media.Close()
                    Media.Source = Nothing
                Catch ex As Exception
                    Logger.Warn(ex, "关闭视频播放器失败")
                End Try
                If _Current Is Me Then _Current = Nothing
            End Sub
        AddHandler Me.Loaded, Sub() Me.Activate()

        StartLoad()
    End Sub

    Private Sub StartLoad()
        Dim Ext As String = PathUtils.GetExtension(FileName).ToLower()
        If Ext = "" Then Ext = "mp4"
        Dim LocalPath As String = PathTemp & "DstVideo\" & Url.GetStableHashCode().ToString() & "." & Ext
        '缓存要能通过校验才复用：之前被中断过的半截文件会一直卡在加载
        If FileUtils.Exists(LocalPath) Then
            Dim CachedSize As Long = 0
            Try
                CachedSize = New IO.FileInfo(LocalPath).Length
            Catch ex As Exception
                Logger.Warn(ex, $"读取视频缓存大小失败：{LocalPath}")
            End Try
            If CachedSize > 0 AndAlso (ExpectedSize <= 0 OrElse CachedSize = ExpectedSize) AndAlso LooksLikeVideo(LocalPath) Then
                PlayLocal(LocalPath)
                Return
            End If
            Logger.Info($"视频缓存无效（{CachedSize} 字节 / 期望 {ExpectedSize} 字节），重新下载")
            Try
                FileUtils.Delete(LocalPath)
            Catch ex As Exception
                Logger.Warn(ex, $"删除视频缓存失败：{LocalPath}")
            End Try
        End If
        LabTip.Text = "正在下载视频……"
        PbProgress.Visibility = Visibility.Visible
        PbProgress.Value = 0
        RunInThread(
        Sub()
            Try
                DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalPath))
                Client = New Net.WebClient()
                AddHandler Client.DownloadProgressChanged,
                    Sub(s As Object, e As Net.DownloadProgressChangedEventArgs)
                        Dim Got As Long = e.BytesReceived
                        Dim Total As Long = If(ExpectedSize > 0, ExpectedSize, e.TotalBytesToReceive)
                        RunInUi(
                        Sub()
                            If Total > 0 Then
                                PbProgress.Value = Math.Min(100, Got * 100.0 / Total)
                                LabTip.Text = $"正在下载视频…… {Math.Floor(Got * 100.0 / Total)}%（{FormatSize(Got)} / {FormatSize(Total)}）"
                            Else
                                LabTip.Text = $"正在下载视频…… {FormatSize(Got)}"
                            End If
                        End Sub)
                    End Sub
                Client.DownloadFileTaskAsync(New Uri(Url), LocalPath).GetAwaiter().GetResult()
                If Not FileUtils.Exists(LocalPath) Then Throw New Exception("下载后未找到文件")
                Dim RealSize As Long = New IO.FileInfo(LocalPath).Length
                If ExpectedSize > 0 AndAlso RealSize <> ExpectedSize Then
                    Throw New Exception($"下载不完整（{FormatSize(RealSize)} / {FormatSize(ExpectedSize)}）")
                End If
                If Not LooksLikeVideo(LocalPath) Then Throw New Exception("下载到的内容不是视频")
                RunInUi(Sub() PlayLocal(LocalPath))
            Catch ex As Exception
                Logger.Warn(ex, "视频下载失败")
                RunInUi(Sub() ShowError("视频下载失败：" & FriendlyError(ex.Message)))
            Finally
                Client = Nothing
            End Try
        End Sub)
    End Sub

    ''' <summary>粗略校验：MP4/MOV 第 4-7 字节应该是 ftyp。</summary>
    Private Function LooksLikeVideo(LocalPath As String) As Boolean
        Try
            Using Fs As New IO.FileStream(LocalPath, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                Dim Head(11) As Byte
                If Fs.Read(Head, 0, 12) < 12 Then Return False
                Dim Tag As String = Text.Encoding.ASCII.GetString(Head, 4, 4)
                Return Tag = "ftyp" OrElse Tag = "moov" OrElse Tag = "mdat" OrElse Tag = "RIFF"
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub OpenBySystem()
        Try
            If LocalPathNow = "" OrElse Not FileUtils.Exists(LocalPathNow) Then
                Hint("还没有下载好的视频文件", HintType.Red)
                Return
            End If
            Process.Start(New ProcessStartInfo(LocalPathNow) With {.UseShellExecute = True})
        Catch ex As Exception
            Logger.Warn(ex, "用系统播放器打开失败")
            Hint("打开失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Function FormatSize(Bytes As Long) As String
        If Bytes < 1024 Then Return Bytes & " B"
        If Bytes < 1024 * 1024 Then Return (Bytes / 1024).ToString("F1") & " KB"
        Return (Bytes / 1024 / 1024).ToString("F2") & " MB"
    End Function

    Private Sub PlayLocal(LocalPath As String)
        Try
            Dim Size As Long = 0
            Try
                Size = New IO.FileInfo(LocalPath).Length
            Catch ex As Exception
                Logger.Warn(ex, $"读取视频大小失败：{LocalPath}")
            End Try
            Logger.Info($"视频缓存文件：{LocalPath}，{Size} 字节")
            If Size = 0 Then
                ShowError("视频文件为空，下载可能失败")
                Return
            End If
            LocalPathNow = LocalPath
            Media.Source = New Uri(LocalPath, UriKind.Absolute)
            'Manual 模式下不主动 Play，有些机器永远不触发 MediaOpened
            Media.Play()
            LabTip.Visibility = Visibility.Visible
            LabTip.Text = "正在加载……"
            PbProgress.Visibility = Visibility.Collapsed
            StartWatchdog(LocalPath, Size)
        Catch ex As Exception
            ShowError("加载失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>8 秒还没打开就认为卡住，把文件信息与位置亮出来，别让人干等。</summary>
    Private Watchdog As System.Windows.Threading.DispatcherTimer
    Private Sub StartWatchdog(LocalPath As String, Size As Long)
        If Watchdog IsNot Nothing Then Watchdog.Stop()
        Watchdog = New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(8)}
        AddHandler Watchdog.Tick,
            Sub()
                Watchdog.Stop()
                If PlayingNow Then Return
                If LabTip.Text.StartsWithF("正在加载") Then
                    Logger.Warn($"视频加载超时：{LocalPath}（{Size} 字节）")
                    ShowError($"加载超时（文件 {FormatSize(Size)}，{PathUtils.GetLastPart(LocalPath)}）{vbCrLf}可能是编码不被系统支持，可点下方按钮用浏览器打开")
                End If
            End Sub
        Watchdog.Start()
    End Sub

    Private Sub ShowError(Message As String)
        LabTip.Visibility = Visibility.Visible
        LabTip.Text = Message
        PbProgress.Visibility = Visibility.Collapsed
        BtnBrowser.Visibility = Visibility.Visible
    End Sub

End Class
