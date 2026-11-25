Public Class PageSelectRight

    '窗口基础
    Private Sub PageSelectRight_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.RunOnUpdated, MaxDepth:=1, ExtraPath:="versions\")
        PanBack.ScrollToHome()
        RefreshFromSnapshot()
    End Sub
    Private Sub LoaderInit() Handles Me.Initialized
        '版本列表改为异步加载：页面本身不再被加载环阻塞，进入页面就能看到已有的内容，
        '剩下的版本项随解析进度一条条长出来（见 RefreshFromSnapshot）。
        Load.State = McInstanceListLoader
        PanLoad.Visibility = Visibility.Collapsed
        PanEmpty.Visibility = Visibility.Collapsed
        AddHandler McInstanceListProgress, AddressOf McInstanceListProgressChanged
    End Sub
    Private Sub Load_Click(sender As Object, e As MouseButtonEventArgs) Handles Load.Click
        If McInstanceListLoader.State = LoadState.Failed Then
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        End If
    End Sub

    '窗口属性
    ''' <summary>
    ''' 是否显示隐藏的 Minecraft 版本。
    ''' </summary>
    Public ShowHidden As Boolean = False

#Region "结果 UI 化"

    '加载进度更新
    Private IsRenderQueued As Boolean = False
    ''' <summary>
    ''' 版本列表快照更新时被触发（在后台线程）。
    ''' 这里合并同一时间内的多次更新，避免一次性向 UI 队列塞入大量刷新请求。
    ''' </summary>
    Private Sub McInstanceListProgressChanged()
        Try
            If IsRenderQueued Then Return
            IsRenderQueued = True
            RunInUi(
            Sub()
                IsRenderQueued = False
                RefreshFromSnapshot()
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "刷新版本列表界面失败")
        End Try
    End Sub

    ''' <summary>
    ''' 按最新的版本列表快照刷新界面。加载过程中每次快照都会调用一次。
    ''' </summary>
    Public Sub RefreshFromSnapshot()
        Try
            If Not IsLoaded Then Return
            Dim Snapshot = McInstanceListSnapshot
            If Snapshot Is Nothing Then
                UpdateEmptyState(False, McInstanceListSnapshotIsFinal)
                Return
            End If
            RenderSnapshot(Snapshot, McInstanceListSnapshotIsFinal)
        Catch ex As Exception
            Logger.Error(ex, "将版本列表转换显示时失败")
        End Try
    End Sub

    ''' <summary>
    ''' 立即按当前快照重新显示，不做任何重新加载。用于切换隐藏版本显示等纯显示相关的操作。
    ''' </summary>
    Public Sub RefreshView()
        If McInstanceListSnapshot Is Nothing Then
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Return
        End If
        RefreshFromSnapshot()
    End Sub

    '已经显示出来的卡片
    Private Class RenderedCard
        Public Card As MyCard
        Public Stack As StackPanel
        Public Keys As New List(Of String)
        Public Spacer As FrameworkElement
    End Class
    Private ReadOnly RenderedCards As New Dictionary(Of McInstanceCardType, RenderedCard)
    '卡片固定的显示顺序
    Private Shared ReadOnly CardOrder As McInstanceCardType() = {
        McInstanceCardType.Star, McInstanceCardType.API, McInstanceCardType.OriginalLike,
        McInstanceCardType.Rubbish, McInstanceCardType.Fool, McInstanceCardType.Error, McInstanceCardType.Hidden}

    ''' <summary>
    ''' 把快照增量地画到界面上：只添加新出现的卡片与版本，已经显示出来的项目不会被重建，
    ''' 因此界面不会在加载过程中整体闪烁。
    ''' </summary>
    Private Sub RenderSnapshot(Snapshot As Dictionary(Of McInstanceCardType, List(Of McInstance)), IsFinal As Boolean)
        '已经显示过内容，或者还在加载中时，新出现的版本项需要缓动进入
        Dim AnimateEnter As Boolean = RenderedCards.Count > 0 OrElse Not IsFinal
        '整理出本次需要显示的卡片与版本
        Dim Desired As New Dictionary(Of McInstanceCardType, List(Of McInstance))
        For Each Card As KeyValuePair(Of McInstanceCardType, List(Of McInstance)) In Snapshot
            '确认是否为隐藏版本显示状态
            If Card.Key = McInstanceCardType.Hidden Xor ShowHidden Then Continue For
            Dim Instances = Card.Value.Where(Function(Instance) Not McInstanceLock.IsLocked(Instance)).ToList
            If Not Instances.Any Then Continue For
            Desired.Add(Card.Key, Instances)
        Next

        '移除已经不再需要的卡片
        For Each CardType As McInstanceCardType In RenderedCards.Keys.ToList
            If Desired.ContainsKey(CardType) Then Continue For
            RemoveCard(CardType)
        Next

        '逐张卡片增量更新
        For Each CardType As McInstanceCardType In CardOrder
            If Not Desired.ContainsKey(CardType) Then Continue For
            RenderCard(CardType, Desired(CardType), AnimateEnter)
        Next

        '保持卡片顺序
        EnsureCardOrder()

        '若只有一个卡片，则强制展开
        If IsFinal AndAlso Desired.Count = 1 Then
            Dim OnlyCard As RenderedCard = Nothing
            If RenderedCards.TryGetValue(Desired.Keys(0), OnlyCard) Then OnlyCard.Card.IsSwapped = False
        End If

        '判断应该显示哪一个页面
        UpdateEmptyState(RenderedCards.Count > 0, IsFinal)
    End Sub

    ''' <summary>
    ''' 更新某一张卡片：不存在就建立，存在就只同步有变化的版本项。
    ''' </summary>
    Private Sub RenderCard(CardType As McInstanceCardType, Instances As List(Of McInstance), AnimateEnter As Boolean)
        Dim Info As RenderedCard = Nothing
        If Not RenderedCards.TryGetValue(CardType, Info) Then
            '建立控件
            Dim NewCard As New MyCard With {.Margin = New Thickness(0, 0, 0, 15)}
            Dim NewStack As New StackPanel With {.Margin = New Thickness(20, MyCard.SwapedHeight, 18, 0), .VerticalAlignment = VerticalAlignment.Top, .RenderTransform = New TranslateTransform(0, 0)}
            NewCard.Children.Add(NewStack)
            NewCard.SwapControl = NewStack
            Info = New RenderedCard With {.Card = NewCard, .Stack = NewStack, .Spacer = New FrameworkElement With {.Height = 18}}
            '确定卡片是否展开：分类结果在加载完成前还会变化，先按默认规则折叠，最后再统一收尾
            If CardType = McInstanceCardType.Rubbish OrElse CardType = McInstanceCardType.Error OrElse CardType = McInstanceCardType.Fool Then
                NewCard.IsSwapped = True
            End If
            RenderedCards.Add(CardType, Info)
            PanMain.Children.Add(NewCard)
        End If
        Info.Card.Title = GetCardTitle(CardType, Instances)
        SyncCardItems(Info, Instances, AnimateEnter)
    End Sub

    ''' <summary>
    ''' 同步卡片内的版本项：新增的版本项会缓动进入，位置不对的会被移动到正确位置，消失的直接移除。
    ''' 版本项永远排在最前面，占位符在最后，所以下标与 Keys 一一对应。
    ''' </summary>
    Private Sub SyncCardItems(Info As RenderedCard, Instances As List(Of McInstance), AnimateEnter As Boolean)
        '移除已经不在本卡片中的版本
        Dim DesiredKeys As New List(Of String)
        For Each Instance As McInstance In Instances
            DesiredKeys.Add(Instance.PathVersion)
        Next
        For i = Info.Keys.Count - 1 To 0 Step -1
            If DesiredKeys.Contains(Info.Keys(i)) Then Continue For
            If i < Info.Stack.Children.Count Then Info.Stack.Children.RemoveAt(i)
            Info.Keys.RemoveAt(i)
        Next

        '按目标顺序摆放
        For i = 0 To Instances.Count - 1
            Dim Key As String = Instances(i).PathVersion
            If i < Info.Keys.Count AndAlso Info.Keys(i) = Key Then Continue For
            Dim OldIndex As Integer = Info.Keys.IndexOf(Key)
            If OldIndex >= 0 Then
                '已存在但位置不对，直接移动，避免同一张卡片里反复出现淡入淡出
                Dim Item As UIElement = Info.Stack.Children(OldIndex)
                Info.Stack.Children.RemoveAt(OldIndex)
                Info.Stack.Children.Insert(i, Item)
                Info.Keys.RemoveAt(OldIndex)
                Info.Keys.Insert(i, Key)
            Else
                '新出现的版本，插入并缓动进入
                Info.Stack.Children.Insert(i, CreateItemElement(Instances(i), AnimateEnter))
                Info.Keys.Insert(i, Key)
            End If
        Next

        '确保末尾的占位始终在最后
        If Info.Stack.Children.Count = 0 OrElse Not Object.ReferenceEquals(Info.Stack.Children(Info.Stack.Children.Count - 1), Info.Spacer) Then
            Info.Stack.Children.Remove(Info.Spacer)
            Info.Stack.Children.Add(Info.Spacer)
        End If
    End Sub

    ''' <summary>
    ''' 建立版本项控件。使用虚拟化包装，只有在滚动到可见区域时才会实际生成。
    ''' </summary>
    Private Function CreateItemElement(Instance As McInstance, AnimateEnter As Boolean) As FrameworkElement
        Dim Element As New MyVirtualizingElement(Of MyListItem)(
            Function()
                Dim Item As MyListItem = Instance.CreateListItem(AddressOf McInstanceListContent)
                '在项目真正出现的瞬间淡入，避免异步加载时列表里的内容生硬地跳出
                If AnimateEnter Then AnimateItemEnter(Item)
                Return Item
            End Function)
        Element.Height = 42
        Return Element
    End Function

    ''' <summary>
    ''' 版本项出现时的缓动进入动画。
    ''' </summary>
    Private Sub AnimateItemEnter(Item As FrameworkElement)
        Try
            If Item Is Nothing OrElse AniControlEnabled <> 0 Then Return
            Item.Opacity = 0
            AniStart({AaOpacity(Item, 1, 160, 0, New AniEaseOutFluent(AniEasePower.Weak))}, "Instance Item Enter " & GetUuid(), True)
        Catch ex As Exception
            Item.Opacity = 1
            Logger.Warn(ex, "版本项进入动画失败")
        End Try
    End Sub

    Private Sub RemoveCard(CardType As McInstanceCardType)
        Dim Info As RenderedCard = RenderedCards(CardType)
        RenderedCards.Remove(CardType)
        PanMain.Children.Remove(Info.Card)
    End Sub

    ''' <summary>
    ''' 让卡片在列表中保持固定的顺序。
    ''' </summary>
    Private Sub EnsureCardOrder()
        Dim Ordered As New List(Of MyCard)
        For Each CardType As McInstanceCardType In CardOrder
            Dim Info As RenderedCard = Nothing
            If RenderedCards.TryGetValue(CardType, Info) Then Ordered.Add(Info.Card)
        Next
        For i = 0 To Ordered.Count - 1
            Dim Target As MyCard = Ordered(i)
            If PanMain.Children.IndexOf(Target) = i Then Continue For
            PanMain.Children.Remove(Target)
            PanMain.Children.Insert(Math.Min(i, PanMain.Children.Count), Target)
        Next
    End Sub

    ''' <summary>
    ''' 更新空列表与加载中的提示。
    ''' </summary>
    Private Sub UpdateEmptyState(HasCard As Boolean, IsFinal As Boolean)
        Dim ShowLoading As Boolean = Not HasCard AndAlso Not IsFinal
        Dim ShowEmpty As Boolean = Not HasCard AndAlso IsFinal
        PanLoad.Visibility = If(ShowLoading, Visibility.Visible, Visibility.Collapsed)
        PanEmpty.Visibility = If(ShowEmpty, Visibility.Visible, Visibility.Collapsed)
        PanBack.Visibility = If(HasCard, Visibility.Visible, Visibility.Collapsed)
        If Not ShowEmpty Then Return
        If ShowHidden Then
            LabEmptyTitle.Text = "无隐藏版本"
            LabEmptyContent.Text = "没有版本被隐藏，你可以在版本设置的版本分类选项中隐藏版本。" & vbCrLf & "再次按下 F11 即可退出隐藏版本查看模式。"
            BtnEmptyDownload.Visibility = Visibility.Collapsed
        Else
            LabEmptyTitle.Text = "无可用版本"
            LabEmptyContent.Text = "未找到任何版本的游戏，请先下载任意版本的游戏。" & vbCrLf & "若有已存在的游戏，请在左边的列表中选择添加文件夹，选择 .minecraft 文件夹将其导入。"
            BtnEmptyDownload.Visibility = If(Settings.Get(Of Boolean)("UiHiddenPageDownload") AndAlso Not PageSetupUI.HiddenForceShow, Visibility.Collapsed, Visibility.Visible)
        End If
    End Sub

    ''' <summary>
    ''' 确认卡片名称。
    ''' </summary>
    Private Function GetCardTitle(CardType As McInstanceCardType, Instances As List(Of McInstance)) As String
        Dim CardName As String = ""
        Select Case CardType
            Case McInstanceCardType.OriginalLike
                CardName = "常规版本"
            Case McInstanceCardType.API
                Dim IsForgeExists As Boolean = False
                Dim IsNeoForgeExists As Boolean = False
                Dim IsFabricExists As Boolean = False
                Dim IsLiteExists As Boolean = False
                For Each Instance As McInstance In Instances
                    If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then IsFabricExists = True
                    If Instance.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) Then IsLiteExists = True
                    If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then IsForgeExists = True
                    If Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then IsNeoForgeExists = True
                Next
                If If(IsLiteExists, 1, 0) + If(IsForgeExists, 1, 0) + If(IsFabricExists, 1, 0) + If(IsNeoForgeExists, 1, 0) > 1 Then
                    CardName = "可安装 Mod"
                ElseIf IsForgeExists Then
                    CardName = "Forge 版本"
                ElseIf IsNeoForgeExists Then
                    CardName = "NeoForge 版本"
                ElseIf IsLiteExists Then
                    CardName = "LiteLoader 版本"
                Else
                    CardName = "Fabric 版本"
                End If
            Case McInstanceCardType.Error
                CardName = "错误的版本"
            Case McInstanceCardType.Hidden
                CardName = "隐藏的版本"
            Case McInstanceCardType.Rubbish
                CardName = "不常用版本"
            Case McInstanceCardType.Star
                CardName = "收藏夹"
            Case McInstanceCardType.Fool
                CardName = "愚人节版本"
            Case Else
                Throw New ArgumentException("未知的卡片种类（" & CardType & "）")
        End Select
        Return CardName & If(CardName = "收藏夹", "", " (" & Instances.Count & ")")
    End Function

    Public Shared Sub McInstanceListContent(sender As MyListItem, e As EventArgs)
        Dim Instance As McInstance = sender.Tag
        '注册点击事件
        AddHandler sender.Click, AddressOf Item_Click
        '图标按钮
        '收藏星标固定用主题强调色（Color2）：默认配色用的是 ColorBrush4，浅色主题下太淡，实心图标会糊成一片
        Dim BtnStar As New MyIconButton With {
            .LogoScale = 1.1,
            .Theme = MyIconButton.Themes.Custom,
            .Foreground = New SolidColorBrush(Color2)
        }
        If Instance.IsStar Then
            BtnStar.ToolTip = "取消收藏"
            BtnStar.Logo = Logo.IconButtonLikeFill
        Else
            BtnStar.ToolTip = "收藏"
            BtnStar.Logo = Logo.IconButtonLikeLine
        End If
        AddHandler BtnStar.Click,
        Sub()
            WriteIni(Instance.PathVersion & "PCL\Setup.ini", "IsStar", Not Instance.IsStar)
            '收藏状态变了只需要重排卡片，不必让整套版本缓存作废
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        End Sub
        Dim BtnDel As New MyIconButton With {.LogoScale = 1.1, .Logo = Logo.IconButtonDelete}
        BtnDel.ToolTip = "删除"
        AddHandler BtnDel.Click, Sub() DeleteInstance(sender, Instance)
        If Instance.State <> McInstance.McInstanceState.Error Then
            Dim BtnCont As New MyIconButton With {.LogoScale = 1.1, .Logo = Logo.IconButtonSetup}
            BtnCont.ToolTip = "设置"
            AddHandler BtnCont.Click,
            Sub()
                PageInstanceLeft.Instance = Instance
                FrmMain.PageChange(FormMain.PageType.InstanceSetup, 0)
            End Sub
            AddHandler sender.MouseRightButtonUp,
            Sub()
                PageInstanceLeft.Instance = Instance
                FrmMain.PageChange(FormMain.PageType.InstanceSetup, 0)
            End Sub
            sender.Buttons = {BtnStar, BtnDel, BtnCont}
        Else
            Dim BtnCont As New MyIconButton With {.LogoScale = 1.15, .Logo = Logo.IconButtonOpen}
            BtnCont.ToolTip = "打开文件夹"
            AddHandler BtnCont.Click, Sub() PageInstanceOverall.OpenInstanceFolder(Instance)
            AddHandler sender.MouseRightButtonUp, Sub() PageInstanceOverall.OpenInstanceFolder(Instance)
            sender.Buttons = {BtnStar, BtnDel, BtnCont}
        End If
        '整合包：扫描到新版本时常驻一个高亮按钮；没扫描到就跟着悬停按钮一起出现，
        '用来手动重新检查这一个包，平时不占地方。
        If Instance.ModpackSource IsNot Nothing Then
            Dim BtnUpdate As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonUpdate}
            If ModModpackUpdate.HasUpdate(Instance.PathVersion) Then
                'Custom 主题才会用 Foreground 着色，默认的 Color 主题会被主题色覆盖
                BtnUpdate.Theme = MyIconButton.Themes.Custom
                BtnUpdate.Foreground = New SolidColorBrush(Color3)
                BtnUpdate.ToolTip = "更新整合包"
                AddHandler BtnUpdate.Click, Sub() ModModpackUpdate.StartUpdate(Instance)
                sender.PersistentButtons = {BtnUpdate}
            Else
                BtnUpdate.ToolTip = "检查整合包更新"
                AddHandler BtnUpdate.Click, Sub() ModModpackUpdate.CheckOne(Instance)
                '排在悬停按钮组最左边，和原来的顺序保持一致
                Dim HoverButtons As New List(Of MyIconButton) From {BtnUpdate}
                HoverButtons.AddRange(sender.Buttons)
                sender.Buttons = HoverButtons
            End If
        End If
    End Sub

#End Region

#Region "页面事件"

    '点击选项
    Public Shared Sub Item_Click(sender As MyListItem, e As EventArgs)
        Dim Instance As McInstance = sender.Tag
        If McInstanceLock.IsLocked(Instance) Then
            Hint("该版本正在被修改，请稍后再选择！", HintType.Red)
            Return
        End If
        If New McInstance(Instance.PathVersion).Check Then
            '正常版本
            McInstanceSelected = Instance
            FrmMain.PageBack()
        Else
            '错误版本
            PageInstanceOverall.OpenInstanceFolder(Instance)
        End If
    End Sub

    Private Sub BtnDownload_Click(sender As Object, e As EventArgs) Handles BtnEmptyDownload.Click
        FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall)
    End Sub

    '修改此代码时，同时修改 PageInstanceOverall 中的代码
    Public Shared Sub DeleteInstance(Item As MyListItem, Instance As McInstance)
        Try
            If McInstanceLock.IsLocked(Instance) Then
                Hint("该版本正在被修改，请稍后再删除！", HintType.Red)
                Return
            End If
            Dim IsShiftPressed As Boolean = My.Computer.Keyboard.ShiftKeyDown
            Dim IsHintIndie As Boolean = Instance.State <> McInstance.McInstanceState.Error AndAlso Instance.PathIndie <> McFolderSelected
            Dim SavesFolder = DirectoryUtils.GetInfo(Instance.PathIndie & "saves\")
            Dim SaveEntries = If(IsHintIndie AndAlso SavesFolder.Exists, SavesFolder.EnumerateDirectories.OrderByDescending(Function(Save) Save.LastWriteTime).Select(Function(Save) $"{Save.Name}（上次修改：{StringUtils.FormatTimeSpan(Save.LastWriteTime - Date.Now, False)}）").ToList, New List(Of String))
            Select Case MyMsgBox($"你确定要{If(IsShiftPressed, "永久", "")}删除版本 {Instance.Name} 吗？" &
                        If(IsHintIndie, vbCrLf & "该版本对应的存档、资源包、Mod 等文件也将被一并删除！", "") &
                        If(SaveEntries.Any, vbCrLf & vbCrLf & "这会删除以下存档：" & vbCrLf & "· " & SaveEntries.Join(vbCrLf & "· "), ""),
                        "版本删除确认", , "取消",, True)
                Case 1
                    Instance.ResetSettingsCache()
                    DirectoryUtils.Delete(Instance.PathVersion, Not IsShiftPressed)
                    Hint("版本 " & Instance.Name & " 已删除！", HintType.Green)
                Case 2
                    Return
            End Select
            '删除后重新加载版本列表，被删掉的版本会随之从界面上消失
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        Catch ex As OperationCanceledException
            Logger.Warn(ex, $"删除版本 {Instance.Name} 被主动取消")
        Catch ex As Exception
            Logger.Error(ex, $"删除版本 {Instance.Name} 失败", LogBehavior.Alert)
        End Try
    End Sub

    Public Sub BtnEmptyDownload_Loaded() Handles BtnEmptyDownload.Loaded
        Dim NewVisibility = If((Settings.Get(Of Boolean)("UiHiddenPageDownload") AndAlso Not PageSetupUI.HiddenForceShow) OrElse ShowHidden, Visibility.Collapsed, Visibility.Visible)
        If BtnEmptyDownload.Visibility <> NewVisibility Then
            BtnEmptyDownload.Visibility = NewVisibility
            PanLoad.TriggerForceResize()
        End If
    End Sub

#End Region

End Class
