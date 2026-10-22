''' <summary>
''' 播放器设置。二级页，从百宝箱的播放器卡片右上角进入。
''' 开关直接用 SettingService 绑定 PCL 原有的背景音乐设置项。
''' </summary>
Public Class PageMusicSetup

    Private Sub PageMusicSetup_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        InitVolume()
        RefreshCount()
    End Sub

    ''' <summary>音量：读的是 PCL 自己的 UiMusicVolume（0..1000），界面用 0..100。</summary>
    Private Sub InitVolume()
        If SliderMusicVolume Is Nothing Then Return
        AniControlEnabled += 1
        Try
            SliderMusicVolume.Value = Settings.Get(Of Integer)("UiMusicVolume") / 10
            If LabSetupVolume IsNot Nothing Then LabSetupVolume.Text = SliderMusicVolume.Value & "%"
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Sub SliderMusicVolume_Change(sender As Object, user As Boolean) Handles SliderMusicVolume.Change
        Try
            If Not user Then Return
            If SliderMusicVolume Is Nothing Then Return
            Settings.Set("UiMusicVolume", CInt(SliderMusicVolume.Value * 10))
            If LabSetupVolume IsNot Nothing Then LabSetupVolume.Text = SliderMusicVolume.Value & "%"
        Catch ex As Exception
            Logger.Warn(ex, "保存音量失败")
        End Try
    End Sub

#Region "网易云"

    Private Sub PageMusicSetup_NeLoaded() Handles Me.Loaded
        DstNetease.NeEnsureLoaded()
        RefreshNeUi()
        '每次进页面都刷一次账号信息，顺便更新 VIP 状态（开通后不必重登）
        If DstNetease.NeIsLoggedIn Then
            RunInThread(
            Sub()
                DstNetease.NeRefreshAccount()
                RunInUi(Sub() RefreshNeUi())
            End Sub)
        End If
    End Sub

    Private Sub RefreshNeUi()
        If LabNeState Is Nothing Then Return
        If DstNetease.NeIsLoggedIn Then
            LabNeState.Text = $"已登录：{If(DstNetease.NeNickname = "", "UID " & DstNetease.NeUserId, DstNetease.NeNickname)}"
            BtnNeLogin.IsEnabled = False
            BtnNeLogout.IsEnabled = True
            BtnNeRefresh.IsEnabled = True
        Else
            LabNeState.Text = "未登录"
            BtnNeLogin.IsEnabled = True
            BtnNeLogout.IsEnabled = False
            BtnNeRefresh.IsEnabled = False
        End If
        RefreshNePlaylistList()
        RefreshNeQuality()
    End Sub

    ''' <summary>音质下拉与 VIP 提示。</summary>
    Private Sub RefreshNeQuality()
        If LabNeVip Is Nothing Then Return
        If Not DstNetease.NeIsLoggedIn Then
            LabNeVip.Text = "登录后可选择更高音质"
        ElseIf DstNetease.NeIsVip Then
            LabNeVip.Text = "已检测到 VIP，极高 / 无损基本都能拿到"
        Else
            LabNeVip.Text = "未检测到 VIP：多数曲目仍能拿到 320K，个别曲目会被限制"
        End If
        If ComboNeQuality IsNot Nothing AndAlso ComboNeQuality.Items.Count > 0 Then
            Dim Want As String = DstNetease.NeMusicLevel
            For i = 0 To ComboNeQuality.Items.Count - 1
                Dim Tag As String = ""
                Try
                    Tag = CType(ComboNeQuality.Items(i), MyComboBoxItem).Tag?.ToString()
                Catch ex As Exception
                End Try
                If Tag = Want Then
                    ComboNeQuality.SelectedIndex = i
                    Exit For
                End If
            Next
        End If
    End Sub

    Private Sub RefreshNePlaylistList()
        If PanNePlaylists Is Nothing Then Return
        PanNePlaylists.Children.Clear()
        Dim List_ As List(Of DstNetease.NeteasePlaylist) = DstNetease.NeGetPlaylistsSnapshot()
        If List_.Count = 0 Then
            PanNePlaylists.Children.Add(New TextBlock With {
                .Text = "登录后会在这里列出你的歌单。", .FontSize = 12, .Opacity = 0.5,
                .Margin = New Thickness(0, 2, 0, 0)
            })
            Return
        End If
        For Each Item In List_
            Dim Row As New Border With {
                .Height = 34, .CornerRadius = New CornerRadius(4), .Margin = New Thickness(0, 0, 0, 4),
                .Padding = New Thickness(9, 0, 9, 0), .Cursor = Cursors.Hand,
                .Background = New SolidColorBrush(BackColorSub), .Tag = Item.Id
            }
            Dim G As New Grid
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Dim Count As Integer = DstNetease.NeParseTrackIds(Item.TrackIds).Count
            G.Children.Add(New TextBlock With {
                .Text = Item.Name, .FontSize = 12.5, .VerticalAlignment = VerticalAlignment.Center,
                .TextTrimming = TextTrimming.CharacterEllipsis
            })
            Dim Info As New TextBlock With {
                .Text = If(Count > 0, $"已存 {Count} 首 / 共 {Item.TrackCount} 首", $"{Item.TrackCount} 首"),
                .FontSize = 11, .Opacity = 0.5, .VerticalAlignment = VerticalAlignment.Center
            }
            Grid.SetColumn(Info, 1)
            G.Children.Add(Info)
            Row.Child = G
            Row.ToolTip = "点击把这个歌单的歌曲加入播放列表"
            AddHandler Row.MouseEnter, Sub() Row.Background = New SolidColorBrush(BackColorMain)
            AddHandler Row.MouseLeave, Sub() Row.Background = New SolidColorBrush(BackColorSub)
            AddHandler Row.MouseLeftButtonUp,
                Sub()
                    If Not DstNetease.NeIsLoggedIn Then
                        Hint("先登录网易云", HintType.Red)
                        Return
                    End If
                    Dim Id As Long = CType(Row.Tag, Long)
                    RunInThread(
                    Sub()
                        '之前只把 ID 记进歌单本身，没有进播放队列，所以点了像没反应
                        Dim Err As String = DstNetease.NeAddPlaylistToQueue(Id)
                        Dim Total As Integer = DstNetease.NeQueueSnapshot().Count
                        RunInUi(
                        Sub()
                            If Err = "" Then
                                Hint($"已加入播放列表，当前共 {Total} 首", HintType.Green, False)
                                If FrmOtherTest IsNot Nothing Then FrmOtherTest.RefreshMusicList()
                            Else
                                Hint(Err, HintType.Red)
                            End If
                            RefreshNePlaylistList()
                        End Sub)
                    End Sub)
                End Sub
            PanNePlaylists.Children.Add(Row)
        Next
    End Sub

    Private Sub BtnNeLogin_Click(sender As Object, e As EventArgs) Handles BtnNeLogin.Click
        BtnNeLogin.IsEnabled = False
        LabNeState.Text = "已打开登录窗口，请在浏览器里完成登录……"
        DstNetease.StartLogin(
            Sub(Err As String)
                If Err = "" Then
                    Hint("网易云登录成功", HintType.Green)
                    RunInThread(
                    Sub()
                        DstNetease.NeFetchMyPlaylists()
                        RunInUi(Sub() RefreshNeUi())
                    End Sub)
                Else
                    Hint(Err, HintType.Red)
                End If
                RefreshNeUi()
            End Sub)
    End Sub

    ''' <summary>一键把自己全部歌单导入播放列表。</summary>
    Private Sub BtnNeImportAll_Click(sender As Object, e As EventArgs) Handles BtnNeImportAll.Click
        If BtnNeImportAll.IsEnabled = False Then Return
        If Not DstNetease.NeIsLoggedIn Then
            Hint("先登录网易云", HintType.Red)
            Return
        End If
        BtnNeImportAll.IsEnabled = False
        BtnNeImportAll.Text = "导入中"
        Hint("正在导入你的歌单，曲目多的话要等一会儿……", HintType.Blue)
        RunInThread(
        Sub()
            Dim Err As String = DstNetease.NeImportAllPlaylistsToQueue()
            RunInUi(
            Sub()
                BtnNeImportAll.IsEnabled = True
                BtnNeImportAll.Text = "一键导入全部歌单"
                If Err = "" Then
                    Hint($"已导入，播放列表共 {DstNetease.NeQueueSnapshot().Count} 首", HintType.Green)
                    If FrmOtherTest IsNot Nothing Then FrmOtherTest.RefreshMusicList()
                Else
                    Hint(Err, HintType.Red)
                End If
                RefreshNeUi()
            End Sub)
        End Sub)
    End Sub

    ''' <summary>补全显示为「歌曲 ID」的元数据。</summary>
    Private Sub BtnNeFixNames_Click(sender As Object, e As EventArgs) Handles BtnNeFixNames.Click
        If BtnNeFixNames.IsEnabled = False Then Return
        BtnNeFixNames.IsEnabled = False
        BtnNeFixNames.Text = "刷新中"
        RunInThread(
        Sub()
            Dim Err As String = DstNetease.NeFetchSongDetails(Force:=True)
            RunInUi(
            Sub()
                BtnNeFixNames.IsEnabled = True
                BtnNeFixNames.Text = "刷新歌曲信息"
                If Err = "" Then Hint("歌曲信息已更新", HintType.Green, False) Else Hint(Err, HintType.Red)
                If FrmOtherTest IsNot Nothing Then FrmOtherTest.RefreshMusicList()
            End Sub)
        End Sub)
    End Sub
    Private Sub BtnNeGrab_Click(sender As Object, e As EventArgs) Handles BtnNeGrab.Click
        If BtnNeGrab.IsEnabled = False Then Return
        BtnNeGrab.IsEnabled = False
        BtnNeGrab.Text = "读取中"
        Dim Port As Integer = DstNetease.NeLoginPort
        RunInThread(
        Sub()
            Dim Err As String = DstNetease.NeGrabNow(Port)
            RunInUi(
            Sub()
                BtnNeGrab.IsEnabled = True
                BtnNeGrab.Text = "读取登录状态"
                If Err <> "" Then
                    Hint(Err, HintType.Red)
                Else
                    Hint("已读取网易云会话", HintType.Green, False)
                    RunInThread(
                    Sub()
                        DstNetease.NeFetchMyPlaylists()
                        RunInUi(Sub() RefreshNeUi())
                    End Sub)
                End If
                RefreshNeUi()
            End Sub)
        End Sub)
    End Sub
    '==== 搜索 ====

    Private Sub BtnNeSearchSong_Click(sender As Object, e As EventArgs) Handles BtnNeSearchSong.Click
        DoNeSearch(False)
    End Sub

    Private Sub BtnNeSearchList_Click(sender As Object, e As EventArgs) Handles BtnNeSearchList.Click
        DoNeSearch(True)
    End Sub

    Private Sub DoNeSearch(IsPlaylist As Boolean)
        Dim Keyword As String = If(TextNeSearch.Text, "").Trim()
        If Keyword = "" Then
            Hint("先输入要搜索的内容", HintType.Red)
            Return
        End If
        If Not DstNetease.NeIsLoggedIn Then
            Hint("先登录网易云", HintType.Red)
            Return
        End If
        BtnNeSearchSong.IsEnabled = False
        BtnNeSearchList.IsEnabled = False
        PanNeSearchResult.Children.Clear()
        PanNeSearchResult.Children.Add(New TextBlock With {
            .Text = "搜索中……", .FontSize = 12, .Opacity = 0.55, .Margin = New Thickness(0, 2, 0, 0)
        })
        RunInThread(
        Sub()
            Dim Hits As List(Of DstNetease.NeSearchHit) =
                If(IsPlaylist, DstNetease.NeSearchPlaylists(Keyword), DstNetease.NeSearchSongs(Keyword))
            RunInUi(
            Sub()
                BtnNeSearchSong.IsEnabled = True
                BtnNeSearchList.IsEnabled = True
                BuildNeSearchResult(Hits, IsPlaylist)
            End Sub)
        End Sub)
    End Sub

    Private Sub BuildNeSearchResult(Hits As List(Of DstNetease.NeSearchHit), IsPlaylist As Boolean)
        PanNeSearchResult.Children.Clear()
        If Hits Is Nothing OrElse Hits.Count = 0 Then
            PanNeSearchResult.Children.Add(New TextBlock With {
                .Text = "没有搜到结果", .FontSize = 12, .Opacity = 0.55, .Margin = New Thickness(0, 2, 0, 0)
            })
            Return
        End If
        For Each HitRaw In Hits
            'VB 的 For Each 变量是共享的，lambda 里必须用局部副本，否则所有按钮都指向最后一项
            Dim Hit As DstNetease.NeSearchHit = HitRaw
            Dim Row As New Border With {
                .Height = 36, .CornerRadius = New CornerRadius(4), .Margin = New Thickness(0, 0, 0, 4),
                .Padding = New Thickness(9, 0, 9, 0), .Background = New SolidColorBrush(BackColorSub),
                .Tag = Hit
            }
            Dim G As New Grid
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Dim Info As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
            Info.Children.Add(New TextBlock With {
                .Text = Hit.Name, .FontSize = 12.5, .TextTrimming = TextTrimming.CharacterEllipsis
            })
            Info.Children.Add(New TextBlock With {
                .Text = Hit.Info, .FontSize = 10.5, .Opacity = 0.5, .TextTrimming = TextTrimming.CharacterEllipsis
            })
            G.Children.Add(Info)
            Dim BtnAdd As New MyButton With {
                .Text = If(IsPlaylist, "加入列表", "加入列表"), .Height = 26, .MinWidth = 80,
                .ColorType = MyButton.ColorState.Highlight
            }
            Grid.SetColumn(BtnAdd, 1)
            G.Children.Add(BtnAdd)
            Row.Child = G
            AddHandler BtnAdd.Click,
                Sub()
                    If BtnAdd.IsEnabled = False Then Return
                    BtnAdd.IsEnabled = False
                    BtnAdd.Text = "添加中"
                    RunInThread(
                    Sub()
                        Dim Err As String = If(Hit.IsPlaylist,
                                               DstNetease.NeAddPlaylistToQueue(Hit.Id),
                                               DstNetease.NeAddSong(Hit.Id, Hit.Name, Hit.Info))
                        RunInUi(
                        Sub()
                            BtnAdd.IsEnabled = True
                            BtnAdd.Text = "加入列表"
                            If Err = "" Then
                                Hint("已加入播放列表", HintType.Green, False)
                                If FrmOtherTest IsNot Nothing Then FrmOtherTest.RefreshMusicList()
                            Else
                                Hint(Err, HintType.Red)
                            End If
                        End Sub)
                    End Sub)
                End Sub
            PanNeSearchResult.Children.Add(Row)
        Next
    End Sub
    Private Sub BtnNeLogout_Click(sender As Object, e As EventArgs) Handles BtnNeLogout.Click
        DstNetease.CancelLogin()
        DstNetease.NeLogout()
        Hint("已退出网易云", HintType.Blue, False)
        RefreshNeUi()
    End Sub

    Private Sub BtnNeRefresh_Click(sender As Object, e As EventArgs) Handles BtnNeRefresh.Click
        BtnNeRefresh.IsEnabled = False
        RunInThread(
        Sub()
            Dim Err As String = DstNetease.NeFetchMyPlaylists()
            RunInUi(
            Sub()
                BtnNeRefresh.IsEnabled = True
                If Err <> "" Then Hint(Err, HintType.Red) Else Hint("歌单已更新", HintType.Green, False)
                RefreshNeUi()
            End Sub)
        End Sub)
    End Sub

#End Region
    Private Sub RefreshCount()
        Dim Count As Integer = 0
        Try
            Count = ModMusic.MusicAllList.Count
        Catch
        End Try
        LabMusicCount.Text = If(Count = 0, "当前没有音乐", $"当前共 {Count} 首")
    End Sub

    ''' <summary>两个「游戏启动后」选项互斥，与 PCL 原逻辑一致。</summary>
    Private Sub CheckMusicStart_Change() Handles CheckMusicStart.Change
        If AniControlEnabled <> 0 Then Return
        If CheckMusicStart.Checked Then CheckMusicStop.Checked = False
    End Sub

    Private Sub CheckMusicStop_Change() Handles CheckMusicStop.Change
        If AniControlEnabled <> 0 Then Return
        If CheckMusicStop.Checked Then CheckMusicStart.Checked = False
    End Sub

    Private Sub BtnMusicOpen_Click(sender As Object, e As EventArgs) Handles BtnMusicOpen.Click
        DirectoryUtils.Create(Paths.Base & "PCL\Musics\")
        OpenExplorer(Paths.Base & "PCL\Musics\")
    End Sub

    Private Sub BtnMusicRefresh_Click(sender As Object, e As EventArgs) Handles BtnMusicRefresh.Click
        ModMusic.MusicRefreshPlay(True)
        RefreshCount()
        If FrmOtherTest IsNot Nothing Then FrmOtherTest.RefreshMusicList()
    End Sub

    Private Sub BtnMusicClear_Click(sender As Object, e As EventArgs) Handles BtnMusicClear.Click
        If MyMsgBox("即将删除背景音乐文件夹中的所有文件。" & vbCrLf & "此操作不可撤销，是否确定？", "警告",, "取消", IsWarn:=True) = 1 Then
            RunInThread(
            Sub()
                Hint("正在删除背景音乐……")
                '停止播放并清空列表
                MusicNAudio = Nothing
                MusicWaitingList = New List(Of String)
                MusicAllList = New List(Of String)
                Thread.Sleep(200)
                Try
                    DirectoryUtils.Delete(Paths.Base & "PCL\Musics")
                    Hint("背景音乐已删除！", HintType.Green)
                Catch ex As Exception
                    Logger.Error(ex, "删除背景音乐失败", LogBehavior.Alert)
                End Try
                Try
                    DirectoryUtils.Create(Paths.Base & "PCL\Musics\")
                    RunInUi(
                    Sub()
                        ModMusic.MusicRefreshPlay(False)
                        RefreshCount()
                        If FrmOtherTest IsNot Nothing Then FrmOtherTest.RefreshMusicList()
                    End Sub)
                Catch ex As Exception
                    Logger.Error(ex, "重建背景音乐文件夹失败", LogBehavior.Alert)
                End Try
            End Sub)
        End If
    End Sub

End Class
