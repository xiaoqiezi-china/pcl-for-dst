''' <summary>
''' 设置 - 服务器与机器人。
''' <para>
''' 页面按「源状态」「云信息」「服务器设置」「本地数据」四块组织：
''' 预设与自定义二选一，选中预设后先看清它的地址与群号再使用。
''' </para>
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
        RefreshPresetList()
    End Sub

    Private Sub RefreshAll()
        '模式单选：预设 / 自定义，由 DstBot.UseCustom 决定
        AniControlEnabled += 1
        Try
            RadioDstCustom.Checked = DstBot.UseCustom
            RadioDstPreset.Checked = Not DstBot.UseCustom
        Finally
            AniControlEnabled -= 1
        End Try
        PanDstPreset.Visibility = If(DstBot.UseCustom, Visibility.Collapsed, Visibility.Visible)
        PanDstCustomInput.Visibility = If(DstBot.UseCustom, Visibility.Visible, Visibility.Collapsed)
        TextDstApi.Text = Settings.Get(Of String)("DstBotApiUrl")
        TextDstToken.Text = Settings.Get(Of String)("DstBotToken")
        RefreshSourceState()
        RefreshState()
        RefreshGroupList()
        RefreshCacheInfo()
        DstBot.FetchGroups(OnDone:=AddressOf RefreshGroupList)
    End Sub

    '==== 源状态 ====

    ''' <summary>
    ''' 更新与预设分别走哪个源。
    ''' GitHub 是主源；Gitee 作为国内备用，地址配置后才会亮起。
    ''' </summary>
    Private Sub RefreshSourceState()
        If LabGithub Is Nothing Then Return
        'GitHub 恒为主源
        DotGithub.Fill = New SolidColorBrush(New MyColor(76, 175, 80))
        LabGithub.Text = "GitHub　已就绪，用于检查更新与拉取预设"
        'Gitee 目前没有配置地址，显示为未启用
        Dim GiteeReady As Boolean = ModPresetSource.GiteeUrl <> ""
        DotGitee.Fill = New SolidColorBrush(If(GiteeReady, New MyColor(76, 175, 80), New MyColor(158, 158, 158)))
        LabGitee.Text = If(GiteeReady, "Gitee　已就绪", "Gitee　未配置")
    End Sub

    '==== 云信息 ====

    Private Sub RefreshState()
        If LabDstStatus Is Nothing Then Return
        '常规模式只影响首页展示，不影响这里看服务状态：
        '用户要在这里配置服务器，隐藏掉这些反而没法判断连没连上
        Dim Offline As Boolean = DstBot.IsOffline
        LabCloudOffline.Visibility = If(Offline, Visibility.Visible, Visibility.Collapsed)
        '这句提示要写清楚「离线只是首页行为」，否则和下面的连接信息读起来自相矛盾
        LabCloudOffline.Text = "启动器处于常规模式，首页只显示本地内容；下面的连接状态仍然有效，配置好服务后取消勾选即可使用。"
        PanCloudOnline.Visibility = Visibility.Visible
        BtnDstTest.Visibility = Visibility.Visible
        BtnDstJoinGroup.Visibility = Visibility.Visible
        BtnDstQq.Visibility = Visibility.Visible
        LabCloudLatency.Text = DstBot.LatencyText
        LabDstGroup.Text = DstBot.GroupDisplay
        LabDstStatus.Text = DstBot.StatusText & If(DstBot.ErrLast = "", "", "（" & DstBot.ErrLast & "）")
        If DstBot.IsQqBound Then
            LabDstQq.Text = $"已绑定 QQ {DstBot.QqNumber}（key {DstBot.UserKeyMasked}）"
        Else
            LabDstQq.Text = "未绑定 QQ"
        End If
        BtnDstQq.Text = If(DstBot.IsQqBound, "解除绑定", "绑定 QQ")
    End Sub

    '==== 服务器设置：预设 ====

    Private Const PresetTagPrefix As String = "preset:"

    Private Sub RefreshPresetList()
        If CmbDstPreset Is Nothing Then Return
        LabDstPresetState.Text = "正在获取预设列表……"
        RunInThread(
        Sub()
            Dim Ok As Boolean = ModPresetSource.Fetch()
            If Not Ok Then ModPresetSource.LoadFromBackup()
            RunInUi(Sub() ApplyPresetList())
        End Sub)
    End Sub

    Private Sub ApplyPresetList()
        If CmbDstPreset Is Nothing Then Return
        AniControlEnabled += 1
        Try
            CmbDstPreset.Items.Clear()
            For Each Item In ModPresetSource.Items
                CmbDstPreset.Items.Add(New MyComboBoxItem With {
                    .Content = Item.Name,
                    .Tag = PresetTagPrefix & Item.Id})
            Next
            Dim CurrentId As String = ModConfigFile.Read(ModConfigFile.SectionCloud, "PresetId")
            If CurrentId = "" Then CurrentId = BuiltinPresetId
            Dim Target As MyComboBoxItem = Nothing
            For Each One As MyComboBoxItem In CmbDstPreset.Items
                If CStr(One.Tag) = PresetTagPrefix & CurrentId Then Target = One : Exit For
            Next
            If Target Is Nothing AndAlso CmbDstPreset.Items.Count > 0 Then Target = CType(CmbDstPreset.Items(0), MyComboBoxItem)
            If Target IsNot Nothing Then CmbDstPreset.SelectedItem = Target
        Finally
            AniControlEnabled -= 1
        End Try
        LabDstPresetState.Text = ModPresetSource.LastStatus
        RefreshPresetInfo()
    End Sub

    ''' <summary>把当前选中预设的地址、群号、说明铺开，让用户点之前先看清。</summary>
    Private Sub RefreshPresetInfo()
        If LabDstPresetUrl Is Nothing Then Return
        Dim Picked As ModPresetSource.Preset = PickedPreset()
        If Picked Is Nothing Then
            LabDstPresetUrl.Text = "—"
            LabDstPresetQq.Text = "—"
            LabDstPresetNote.Text = "远程预设列表不可用，可切到「自定义」手动填写地址。"
            Return
        End If
        LabDstPresetUrl.Text = If(Picked.Api = "", "—", Picked.Api)
        LabDstPresetQq.Text = If(Picked.QqGroup = "", "—", Picked.QqGroup)
        LabDstPresetNote.Text = Picked.Note
    End Sub

    Private Function PickedPreset() As ModPresetSource.Preset
        If CmbDstPreset Is Nothing Then Return Nothing
        Dim Item = TryCast(CmbDstPreset.SelectedItem, MyComboBoxItem)
        If Item Is Nothing Then Return Nothing
        Return ModPresetSource.FindById(CStr(Item.Tag).Substring(PresetTagPrefix.Length))
    End Function

    Private Sub CmbDstPreset_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmbDstPreset.SelectionChanged
        If AniControlEnabled > 0 Then Return
        RefreshPresetInfo()
        Dim Picked As ModPresetSource.Preset = PickedPreset()
        If Picked Is Nothing Then Return
        RefreshPresetInfo()
        ModConfigFile.Write(ModConfigFile.SectionCloud, "PresetId", Picked.Id)
        ModConfigFile.Write(ModConfigFile.SectionCloud, "ApiBase", Picked.Api)
        DstBot.ApplyPreset(Picked.Api, Picked.ApiKey)
        RefreshState()
        Hint($"已切换到「{Picked.Name}」", HintType.Blue)
    End Sub

    Private Sub BtnDstPresetRefresh_Click(sender As Object, e As EventArgs) Handles BtnDstPresetRefresh.Click
        RefreshPresetList()
    End Sub

    '==== 服务器设置：模式切换 ====

    Private Sub RadioDstPreset_Change(sender As Object, e As RouteEventArgs) Handles RadioDstPreset.Changed
        If AniControlEnabled > 0 OrElse Not RadioDstPreset.Checked Then Return
        PanDstPreset.Visibility = Visibility.Visible
        PanDstCustomInput.Visibility = Visibility.Collapsed
        Settings.Set("DstBotUseCustom", False)
        '切回预设时把地址恢复成当前选中的那个
        Dim Picked As ModPresetSource.Preset = PickedPreset()
        If Picked IsNot Nothing Then
            ModConfigFile.Write(ModConfigFile.SectionCloud, "ApiBase", Picked.Api)
            DstBot.ApplyPreset(Picked.Api, Picked.ApiKey)
        End If
        RefreshState()
    End Sub

    Private Sub RadioDstCustom_Change(sender As Object, e As RouteEventArgs) Handles RadioDstCustom.Changed
        If AniControlEnabled > 0 OrElse Not RadioDstCustom.Checked Then Return
        PanDstPreset.Visibility = Visibility.Collapsed
        PanDstCustomInput.Visibility = Visibility.Visible
        Settings.Set("DstBotUseCustom", True)
        RefreshState()
    End Sub

    Private Sub BtnDstSave_Click(sender As Object, e As EventArgs) Handles BtnDstSave.Click
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
        ModConfigFile.Write(ModConfigFile.SectionCloud, "ApiBase", Api.TrimEnd("/"c))
        ModConfigFile.Write(ModConfigFile.SectionCloud, "ApiKey", ApiToken)
        '切换服务后清空缓存，避免显示上一个服务的数据
        SyncLock DstBot.ServerList
            DstBot.ServerList.Clear()
        End SyncLock
        DstBot.UpdatedAt = Date.MinValue
        LabDstSaveTip.Text = "已保存，正在重新连接……"
        RefreshState()
        DstBot.FetchGroups(Force:=True, OnDone:=AddressOf RefreshGroupList)
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

    ''==== 群列表 ====

    Private Sub BtnDstGroupRefresh_Click(sender As Object, e As EventArgs) Handles BtnDstGroupRefresh.Click
        DstBot.FetchGroups(Force:=True, OnDone:=AddressOf RefreshGroupList)
    End Sub

    Private Sub RefreshGroupList()
        If PanDstGroups Is Nothing Then Return
        Dim List As List(Of DstGroup)
        SyncLock DstBot.Groups
            List = DstBot.Groups.ToList()
        End SyncLock
        '离线模式也照样显示群列：只要配了服务就有意义
        PanDstGroupsArea.Visibility = If(DstBot.HasService, Visibility.Visible, Visibility.Collapsed)
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
    End Sub

    Private Function BuildGroupRow(Item As DstGroup, IsSelected As Boolean) As FrameworkElement
        Dim Row As New Border With {
            .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(12, 8, 12, 8),
            .CornerRadius = New CornerRadius(6),
            .Cursor = Cursors.Hand,
            .Tag = Item.GroupId,
            .Background = New SolidColorBrush(If(IsSelected, Color2, BackColorSub))
        }
        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        '选中行底色变深，标题也必须跟着换成浅色，否则深底深字看不清
        Dim Title As New TextBlock With {
            .Text = Item.GroupName, .FontSize = 13.5, .FontWeight = FontWeights.Bold,
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = New SolidColorBrush(If(IsSelected, New MyColor(255, 255, 255), TextColorMain))
        }
        G.Children.Add(Title)
        Dim Id As New TextBlock With {
            .Text = If(IsSelected, "使用中 · ", "") & Item.GroupId, .FontSize = 12,
            .VerticalAlignment = VerticalAlignment.Center,
            .Foreground = New SolidColorBrush(If(IsSelected, New MyColor(255, 255, 255), TextColorSub))
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
                SyncLock DstBot.ServerList
                    DstBot.ServerList.Clear()
                End SyncLock
                DstBot.UpdatedAt = Date.MinValue
                Hint("已切换群：" & DstBot.GroupDisplay, HintType.Green)
                RefreshGroupList()
                RefreshState()
                If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshGroupDependentViews()
            End Sub
        Return Row
    End Function

    '==== 本地数据 ====

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

    '==== 启动器更新 ====

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

End Class
