''' <summary>
''' QQ 页的收藏夹：五个分类子夹，列出收藏的资源（图标 / 标题 / 简介），点一下进详情页。
''' </summary>
Public Class PageGroupStar

    ''' <summary>当前分类，取值同 ResourceTypes。</summary>
    Private CurrentType As Integer = 1

    Public Sub New()
        InitializeComponent()
    End Sub

    ''' <summary>离线模式：隐藏云同步开关，换成一个「上线」按钮；收藏夹本地照常可用。</summary>
    Public Sub ApplyOfflineUi()
        Dim Offline As Boolean = DstBot.IsOffline
        CheckCloudSync.Visibility = If(Offline, Visibility.Collapsed, Visibility.Visible)
        BtnGoOnline.Visibility = If(Offline, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub BtnGoOnline_Click(sender As Object, e As EventArgs) Handles BtnGoOnline.Click
        If BtnGoOnline.IsEnabled = False Then Return
        BtnGoOnline.IsEnabled = False
        BtnGoOnline.Text = "连接中"
        RunInThread(
        Sub()
            Dim Ok As Boolean = DstBot.GoOnline()
            RunInUi(
            Sub()
                BtnGoOnline.IsEnabled = True
                BtnGoOnline.Text = "上线"
                If Ok Then
                    Hint("已回到在线模式", HintType.Green, False)
                    If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshDependentViewsAfterModeChange()
                Else
                    Hint("远程服务不可用，继续保持常规模式", HintType.Red)
                End If
                ApplyOfflineUi()
                RefreshList()
            End Sub)
        End Sub)
    End Sub
    ''' <summary>同步开关：只响应用户点击，程序里改 Checked 不会触发这里。</summary>
    Private Sub CheckCloudSync_Change(sender As Object, user As Boolean) Handles CheckCloudSync.Change
        If Not IsLoaded OrElse Not user Then Return
        ModFavorite.SyncToCloud = CheckCloudSync.Checked   '立即写进本地配置，下次启动还在
        If Not CheckCloudSync.Checked Then
            Hint("已关闭云同步，收藏只保存在本地", HintType.Blue, False)
            Return
        End If
        If Not DstBot.IsQqBound Then
            Hint("还没绑定 QQ，无法云同步", HintType.Red)
            CheckCloudSync.Checked = False
            ModFavorite.SyncToCloud = False
            Return
        End If
        MergeWithCloud(ShowResult:=True)
    End Sub

    ''' <summary>
    ''' 与云端合并一次。合并而不是覆盖：新设备本地是空的，合并后会把云端的收藏拉下来；
    ''' 两边都有内容时取并集，不会冲掉任何一边。
    ''' </summary>
    Private Sub MergeWithCloud(ShowResult As Boolean)
        If Not DstBot.IsQqBound Then Return
        CheckCloudSync.IsEnabled = False
        RunInThread(
        Sub()
            Dim Err As String = ModFavorite.PullFromCloud(Force:=ShowResult)
            RunInUi(
            Sub()
                CheckCloudSync.IsEnabled = True
                If Err Is Nothing Then
                    If ShowResult Then Hint("收藏夹已与云端合并", HintType.Green, False)
                Else
                    If ShowResult Then Hint("同步失败：" & Err, HintType.Red)
                End If
                RefreshList()
            End Sub)
        End Sub)
    End Sub

    Private Sub PageGroupStar_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '按 Tag 决定初始分类；默认 Mod
        Dim Tabs() As MyRadioButton = {TabMod, TabModPack, TabDataPack, TabResourcePack, TabShader}
        Dim Hit = Tabs.FirstOrDefault(Function(x) CInt(Val(x.Tag)) = CurrentType)
        If Hit IsNot Nothing Then Hit.SetChecked(True, False, False)
        CheckCloudSync.Checked = ModFavorite.SyncToCloud
        ApplyOfflineUi()
        RefreshList()
        '开了云同步就自动合并一次：新设备上第一次进来会把云端收藏拉下来
        If ModFavorite.SyncToCloud AndAlso DstBot.IsQqBound Then MergeWithCloud(ShowResult:=False)
    End Sub

    Private Sub Tab_Check(sender As MyRadioButton, user As Boolean) Handles TabMod.Check, TabModPack.Check,
            TabDataPack.Check, TabResourcePack.Check, TabShader.Check
        If Not user Then Return
        Dim NewType As Integer = CInt(Val(sender.Tag))
        If NewType = CurrentType Then Return
        CurrentType = NewType
        RefreshList()
    End Sub

    ''' <summary>重画当前分类的收藏列表。</summary>
    Public Sub RefreshList()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        Dim Items As List(Of FavoriteEntry) = ModFavorite.GetByType(CType(CurrentType, ResourceTypes))
        If Items.Count = 0 Then
            PanList.Children.Add(New TextBlock With {
                .Text = "这个分类还没有收藏。在下载页点资源卡片右上角的星星就能收藏。",
                .FontSize = 12.5, .Margin = New Thickness(2, 10, 0, 0),
                .TextWrapping = TextWrapping.Wrap,
                .Foreground = New SolidColorBrush(TextColorSub)})
            Return
        End If
        For Each Entry In Items
            PanList.Children.Add(BuildFavoriteRow(Entry))
        Next
    End Sub

    ''' <summary>一条收藏：图标 + 标题 + 简介，整行可点，点了进资源详情页。</summary>
    Private Function BuildFavoriteRow(Entry As FavoriteEntry) As FrameworkElement
        Dim Card As New Border With {
            .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(10, 8, 10, 8),
            .CornerRadius = New CornerRadius(6),
            .Background = New SolidColorBrush(BackColorMain),
            .Cursor = Cursors.Hand,
            .Tag = Entry
        }
        Dim Grid As New Grid
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        '图标
        Dim Logo As New Border With {
            .Width = 40, .Height = 40, .CornerRadius = New CornerRadius(6),
            .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center,
            .Background = New SolidColorBrush(BackColorSub)
        }
        Dim Img As New MyImage With {.Stretch = Stretch.UniformToFill}
        '优先用项目对象自己的 LogoUrl（与详情页完全同一条路径，含镜像兜底）；
        '老数据里 Entry.Logo 可能是空串，但 ProjectJson 里的 LogoUrl 是好的
        Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
        If Project IsNot Nothing Then
            Project.ApplyLogoToMyImage(Img)
        ElseIf Entry.Logo <> "" Then
            Img.Source = Entry.Logo
            Img.FallbackSource = DlSourceModGet(Entry.Logo)
        Else
            Img.Source = PathImage & "Icons/NoIcon.png"
        End If
        Logo.Child = Img
        Grid.Children.Add(Logo)

        '标题与简介
        Dim Text As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Text.Children.Add(New TextBlock With {
            .Text = If(Entry.Title = "", Entry.SubTitle, Entry.Title),
            .FontSize = 14, .FontWeight = FontWeights.Bold,
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = New SolidColorBrush(TextColorMain)})
        Text.Children.Add(New TextBlock With {
            .Text = If(Entry.Description = "", Entry.SubTitle, Entry.Description),
            .FontSize = 11.5, .Margin = New Thickness(0, 2, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = New SolidColorBrush(TextColorSub)})
        Grid.SetColumn(Text, 1)
        Grid.Children.Add(Text)

        '取消收藏
        Dim BtnRemove As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonCross, .ToolTip = "取消收藏",
            .Width = 24, .Height = 24, .LogoScale = 0.62,
            .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(8, 0, 2, 0)
        }
        AddHandler BtnRemove.Click,
            Sub()
                ModFavorite.Remove(Entry)
                RefreshList()
                Hint("已取消收藏", HintType.Blue, False)
            End Sub
        Grid.SetColumn(BtnRemove, 2)
        Grid.Children.Add(BtnRemove)

        Card.Child = Grid
        AddHandler Card.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                OpenDetail(Entry)
            End Sub
        Return Card
    End Function

    ''' <summary>还原项目对象后打开资源详情页（与下载页点卡片走同一条路）。</summary>
    Private Sub OpenDetail(Entry As FavoriteEntry)
        Dim Project As ResourceProject = ModFavorite.ToProject(Entry)
        If Project Is Nothing Then
            Hint("这个收藏的项目信息已失效，请重新搜索后收藏", HintType.Red)
            Return
        End If
        Try
            '参数顺序必须与下载页点卡片时一致：{项目, 已展开卡片标题列表, 目标版本, 目标加载器, 类别}
            'Additional(1) 必须是 List(Of String)，传 Nothing 会让详情页 UpdateFilterResult 空引用
            FrmMain.PageChange(New FormMain.PageStackData With {
                .Page = FormMain.PageType.ResourceDetail,
                .Additional = {Project, New List(Of String), "", ModLoaders.None, CType(Entry.ResourceType, ResourceTypes)}
            })
        Catch ex As Exception
            Logger.Warn(ex, "打开收藏的资源详情失败")
            Hint("打开失败：" & FriendlyError(ex.Message), HintType.Red)
        End Try
    End Sub

End Class
