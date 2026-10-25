''' <summary>
''' 版本设置 - 光影包管理页。
''' 列出版本隔离目录下 shaderpacks 文件夹里的 .zip 与文件夹形态光影包，支持启用 / 停用、打开位置与删除。
''' 结构与交互照抄 Mod 管理页（PageInstanceMod），列表行由代码动态生成。
''' </summary>
Public Class PageInstanceShaderPack
    Implements IRefreshable


#Region "初始化"

    '最近一次扫描到的完整列表，供搜索与筛选复用，避免每次输入都重新扫盘
    Private AllItems As New List(Of ShaderPackItem)
    Private CurrentFolder As String = ""
    '当前显示出来的条目（已筛选），全选只作用于它们
    Private ShownItems As New List(Of ShaderPackItem)
    '多选：保存被选中条目的完整路径
    Private SelectedPaths As New HashSet(Of String)

    Private Sub PageInstanceShaderPack_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '多选条用 PCL 自带的图标，和模组管理保持一致
        BtnSelectEnable.Logo = Logo.IconButtonCheck
        BtnSelectDisable.Logo = Logo.IconButtonStop
        BtnSelectDelete.Logo = Logo.IconButtonDelete
        BtnSelectCancel.Logo = Logo.IconButtonCross
        RefreshList()
    End Sub

    ''' <summary>当前版本隔离后的光影包文件夹，末尾带反斜杠。</summary>
    Private ReadOnly Property PackFolder As String
        Get
            If PageInstanceLeft.Instance Is Nothing Then Return ""
            Return PageInstanceLeft.Instance.PathIndie & "shaderpacks\"
        End Get
    End Property

    ''' <summary>刷新光影包列表。会被侧边栏的刷新按钮调用。</summary>
    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshList()
    End Sub

#End Region

#Region "列表"


    ''' <summary>
    ''' 列表中的一个光影包。
    ''' 路径 / 显示名 / 启用状态 / 大小都继承自 <see cref="ModPackFolder.PackFolderItem"/>，
    ''' 和资源包页共用同一套扫描与改名逻辑；这里只加光影包独有的字段。
    ''' </summary>
    Private Class ShaderPackItem
        Inherits ModPackFolder.PackFolderItem
        ''' <summary>从 shaders\shaders.properties 里读到的名字，读不到就是空字符串。</summary>
        Public PackTitle As String = ""
        ''' <summary>
        ''' 图标的原始字节，读不到时为 Nothing。
        ''' 这里只存字节，位图留到 UI 线程再解码，免得在工作线程里造 WPF 对象。
        ''' </summary>
        Public IconBytes As Byte() = Nothing
        ''' <summary>列表里对应的那一行控件，展开配置面板时用来定位插入位置。</summary>
        Public Row As FrameworkElement = Nothing
    End Class

    '刷新序号，用于丢弃过期的后台扫描结果
    Private RefreshToken As Integer = 0

    ''' <summary>重新扫描光影包文件夹，并刷新界面。</summary>
    Private Sub RefreshList()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        Dim Folder As String = PackFolder
        If Folder = "" Then
            PanList.Children.Add(EmptyHint("还没有选择版本，无法读取光影包。"))
            Return
        End If
        Logger.Info("已刷新光影包列表")
        RefreshToken += 1
        Dim Token As Integer = RefreshToken
        RunInThread(
        Sub()
            Dim Items As New List(Of ShaderPackItem)
            Try
                Items = ScanPacks(Folder)
            Catch ex As Exception
                Logger.Warn(ex, "读取光影包文件夹失败")
            End Try
            RunInUi(
            Sub()
                If Token <> RefreshToken Then Return '已经有更新的一次扫描了
                ShowList(Items, Folder)
            End Sub)
        End Sub)
    End Sub

    ''' <summary>
    ''' 扫描 shaderpacks 文件夹。
    ''' 磁盘约定与资源包页完全一致，交给 <see cref="ModPackFolder.ScanPackFolder"/> 统一处理；
    ''' 这里只补光影包独有的图标与 shaders.properties 里的名字。
    ''' </summary>
    Private Function ScanPacks(Folder As String) As List(Of ShaderPackItem)
        Dim Items As New List(Of ShaderPackItem)
        '光影包页的既有习惯是显示名保留 .zip，所以这里传 KeepZipSuffix:=True
        For Each Base In ModPackFolder.ScanPackFolder(Folder, "光影包", KeepZipSuffix:=True)
            Dim Item As New ShaderPackItem With {
                .PackPath = Base.PackPath, .DisplayName = Base.DisplayName,
                .IsFolder = Base.IsFolder, .IsEnabled = Base.IsEnabled,
                .SizeBytes = Base.SizeBytes}
            ReadPackInfo(Item)
            Items.Add(Item)
        Next
        Return Items
    End Function

    ''' <summary>
    ''' 读出一个光影包的图标与名字。
    ''' 图标：压缩包里先找 shaders/pack.png，再找根目录的 pack.png；文件夹同理。
    ''' 名字：shaders/shaders.properties 里写的那个名字，读不到就留空。
    ''' 只读字节，不在这里造位图。
    ''' </summary>
    Private Sub ReadPackInfo(Item As ShaderPackItem)
        Item.IconBytes = Nothing
        Item.PackTitle = ""
        Try
            If Item.IsFolder Then
                Dim Base As String = PathUtils.AddSlashSuffix(Item.PackPath)
                Item.IconBytes = ReadIconFile(Base & "shaders\pack.png")
                If Item.IconBytes Is Nothing Then Item.IconBytes = ReadIconFile(Base & "pack.png")
                Item.PackTitle = ReadTitleFromFile(Base & "shaders\shaders.properties")
                Return
            End If
            If Not FileUtils.Exists(Item.PackPath) Then Return
            Using Zip As ZipArchive = ZipFile.OpenRead(Item.PackPath)
                Item.IconBytes = ReadIconEntry(FindZipEntry(Zip, "shaders/pack.png"))
                If Item.IconBytes Is Nothing Then Item.IconBytes = ReadIconEntry(FindZipEntry(Zip, "pack.png"))
                Item.PackTitle = ReadTitleFromEntry(FindZipEntry(Zip, "shaders/shaders.properties"))
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取光影包信息失败：" & Item.PackPath)
            Item.IconBytes = Nothing
        End Try
    End Sub

    ''' <summary>在压缩包里按名称找条目，忽略大小写与分隔符差异。</summary>
    Private Function FindZipEntry(Zip As ZipArchive, EntryName As String) As ZipArchiveEntry
        Dim Target As String = EntryName.Replace("/"c, ChrW(92))
        For Each Item In Zip.Entries
            If Item.FullName.Replace("/"c, ChrW(92)).Equals(Target, StringComparison.OrdinalIgnoreCase) Then Return Item
        Next
        Return Nothing
    End Function

    ''' <summary>把扫描结果画成列表行。只能在 UI 线程调用。</summary>
    Private Sub ShowList(Items As List(Of ShaderPackItem), Folder As String)
        AllItems = If(Items, New List(Of ShaderPackItem))
        CurrentFolder = Folder
        ApplyFilter()
    End Sub

    ''' <summary>按搜索框与筛选标签过滤后重新绘制列表。</summary>
    Private Sub ApplyFilter()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        If AllItems.Count = 0 Then
            PanList.Children.Add(EmptyHint("还没有光影包。把 .zip 文件或光影包文件夹放进 " & CurrentFolder & " 即可。"))
            Return
        End If

        Dim Keyword As String = If(SearchBox Is Nothing, "", SearchBox.Text.Trim())
        Dim Mode As Integer = 0
        If BtnFilterEnabled IsNot Nothing AndAlso BtnFilterEnabled.Checked Then Mode = 1
        If BtnFilterDisabled IsNot Nothing AndAlso BtnFilterDisabled.Checked Then Mode = 2

        Dim Shown As New List(Of ShaderPackItem)
        For Each Item In AllItems
            If Keyword <> "" AndAlso
               Item.DisplayName.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Select Case Mode
                Case 1 : If Not Item.IsEnabled Then Continue For
                Case 2 : If Item.IsEnabled Then Continue For
            End Select
            Shown.Add(Item)
        Next

        ShownItems = Shown
        If Shown.Count = 0 Then
            PanList.Children.Add(EmptyHint("没有符合条件的光影包"))
            RefreshSelectBar()
            Return
        End If
        For Each Item In Shown
            Dim Entry As ShaderPackItem = Item '防止闭包捕获同一个循环变量
            '单个包出问题不应该让整个列表变空
            Try
                PanList.Children.Add(BuildPackRow(Entry))
            Catch ex As Exception
                Logger.Warn(ex, "绘制光影包条目失败：" & Entry.DisplayName)
            End Try
        Next
        PanList.Children.Add(New TextBlock With {
            .Text = $"共 {Shown.Count} 个。启用中的光影包是 .zip，停用后改名成 .zip.disabled；文件夹形态的光影包由游戏直接读取，这里不能切换。",
            .FontSize = 11, .Opacity = 0.45, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 6, 0, 0)
        })
        RefreshSelectBar()
    End Sub

#Region "多选"

    Private Sub ToggleSelect(Entry As ShaderPackItem)
        If Not SelectedPaths.Remove(Entry.PackPath) Then SelectedPaths.Add(Entry.PackPath)
        ApplyFilter()
    End Sub

    Private Sub ChangeAllSelected(ShouldSelect As Boolean)
        SelectedPaths.Clear()
        If ShouldSelect Then
            For Each Item In ShownItems
                SelectedPaths.Add(Item.PackPath)
            Next
        End If
        ApplyFilter()
    End Sub

    Private Sub RefreshSelectBar()
        If CardSelect Is Nothing Then Return
        SelectedPaths.RemoveWhere(Function(P) Not AllItems.Any(Function(I) I.PackPath = P))
        Dim Count As Integer = SelectedPaths.Count
        If Count = 0 Then
            CardSelect.Visibility = Visibility.Collapsed
            Return
        End If
        LabSelect.Text = $"已选择 {Count} 个文件"
        Dim CanToggle As Boolean = SelectedPaths.All(
            Function(P) AllItems.Any(Function(I) I.PackPath = P AndAlso Not I.IsFolder))
        BtnSelectEnable.IsEnabled = CanToggle
        BtnSelectDisable.IsEnabled = CanToggle
        If CardSelect.Visibility <> Visibility.Visible Then
            CardSelect.Visibility = Visibility.Visible
            CardSelect.Opacity = 1
        End If
    End Sub

    Private Sub BatchToggle(TargetEnabled As Boolean)
        Dim Targets = AllItems.Where(Function(I) SelectedPaths.Contains(I.PackPath) AndAlso Not I.IsFolder).ToList()
        If Targets.Count = 0 Then Return
        Dim Done As Integer = 0
        For Each Entry In Targets
            Dim Result = ModPackFolder.SetPackEnabled(Entry, TargetEnabled, "光影包")
            If Result.Ok Then Done += 1
        Next
        Hint($"已{If(TargetEnabled, "启用", "停用")} {Done} 个光影包", HintType.Green)
        SelectedPaths.Clear()
        RefreshList()
    End Sub

    Private Sub BatchDelete()
        Dim Targets = AllItems.Where(Function(I) SelectedPaths.Contains(I.PackPath)).ToList()
        If Targets.Count = 0 Then Return
        If MyMsgBox($"确定删除这 {Targets.Count} 个光影包吗？此操作不可撤销。", "删除光影包", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Dim Done As Integer = 0
        For Each Entry In Targets
            If ModPackFolder.DeletePackItem(Entry, "光影包") Then Done += 1
        Next
        Hint($"已删除 {Done} 个光影包", HintType.Green)
        SelectedPaths.Clear()
        RefreshList()
    End Sub

#End Region

#End Region

#Region "单个光影包项"

    ''' <summary>一个光影包行：图标 + 文件名 + 状态 / 大小 / 名字 + 右侧图标按钮。</summary>
    Private Function BuildPackRow(Entry As ShaderPackItem) As FrameworkElement
        '副标题：状态 · 大小 · shaders.properties 里的名字
        Dim Parts As New List(Of String)
        Parts.Add(If(Entry.IsFolder, "文件夹", If(Entry.IsEnabled, "已启用", "已停用")))
        If Entry.SizeBytes >= 0 Then Parts.Add(StringUtils.FormatByteSize(Entry.SizeBytes))
        If Entry.PackTitle <> "" Then Parts.Add(Entry.PackTitle)
        Dim Buttons As New List(Of FrameworkElement)
        '启用 / 停用：文件夹形态不显示
        If Not Entry.IsFolder Then
            Dim BtnToggle As New MyIconButton With {
                .Logo = If(Entry.IsEnabled, Logo.IconButtonStop, Logo.IconButtonCheck),
                .Width = 26, .Height = 26, .LogoScale = 0.8, .Margin = New Thickness(6, 0, 0, 0),
                .ToolTip = If(Entry.IsEnabled, "停用", "启用")}
            AddHandler BtnToggle.Click, Sub() TogglePack(Entry)
            Buttons.Add(BtnToggle)
        End If
        '编辑光影配置：读包里的 shaders.properties 与 lang，展开成可编辑的列表
        Dim BtnConfig As New MyIconButton With {
            .Logo = Logo.IconButtonSetup, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "编辑光影配置"}
        AddHandler BtnConfig.Click, Sub() ToggleConfigEditor(Entry)
        Buttons.Add(BtnConfig)
        '打开所在文件夹
        Dim BtnOpen As New MyIconButton With {
            .Logo = Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "打开所在文件夹"}
        AddHandler BtnOpen.Click, Sub() OpenPackFolder(Entry)
        Buttons.Add(BtnOpen)
        '删除
        Dim BtnDelete As New MyIconButton With {
            .Logo = Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "删除"}
        AddHandler BtnDelete.Click, Sub() DeletePack(Entry)
        Buttons.Add(BtnDelete)
        '直接复用模组管理的列表项控件
        Dim Item As New MyLocalModItem With {
            .Title = Entry.DisplayName,
            .Description = String.Join(" · ", Parts),
            .ItemIsEnabled = Entry.IsEnabled,
            .Checked = SelectedPaths.Contains(Entry.PackPath),
            .LogoSource = IconCachePath(Entry)
        }
        Dim IconButtons As New List(Of MyIconButton)
        For Each Btn In Buttons
            Dim IconBtn = TryCast(Btn, MyIconButton)
            If IconBtn IsNot Nothing Then IconButtons.Add(IconBtn)
        Next
        Item.Buttons = IconButtons
        AddHandler Item.Click, Sub() ToggleSelect(Entry)
        Return Item
    End Function

    ''' <summary>切换光影包的启用状态。改名逻辑与资源包页共用。</summary>
    Private Sub TogglePack(Entry As ShaderPackItem)
        Dim Result = ModPackFolder.SetPackEnabled(Entry, Not Entry.IsEnabled, "光影包")
        If Not Result.Ok Then
            Hint(Result.Reason, HintType.Red)
            RefreshList()
            Return
        End If
        Hint(If(Entry.IsEnabled, "已停用", "已启用") & "：" & Entry.DisplayName, HintType.Green)
        RefreshList()
    End Sub

    ''' <summary>打开这个光影包所在的文件夹。</summary>
    Private Sub OpenPackFolder(Entry As ShaderPackItem)
        Try
            OpenExplorer(PathUtils.RemoveLastPart(Entry.PackPath))
        Catch ex As Exception
            Logger.Error(ex, "打开光影包所在文件夹失败")
            Hint("打开文件夹失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    ''' <summary>删除光影包，删前先确认。</summary>
    Private Sub DeletePack(Entry As ShaderPackItem)
        If MyMsgBox("确定要删除光影包「" & Entry.DisplayName & "」吗？" & vbCrLf & "此操作不可撤销。",
                    "删除光影包", "删除", "取消", IsWarn:=True) <> 1 Then Return
        If Not ModPackFolder.DeletePackItem(Entry, "光影包") Then
            Hint("删除失败，详情见日志：" & Entry.DisplayName, HintType.Red)
            Return
        End If
        Logger.Info("已删除光影包：" & Entry.PackPath)
        Hint("已删除：" & Entry.DisplayName, HintType.Green)
        RefreshList()
    End Sub

#End Region

#Region "顶部按钮"

    ''' <summary>打开光影包文件夹，不存在就先建出来。</summary>
    Private Sub BtnOpenFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenFolder.Click
        Dim Folder As String = PackFolder
        If Folder = "" Then Return
        Try
            DirectoryUtils.Create(Folder)
            OpenExplorer(Folder)
        Catch ex As Exception
            Logger.Error(ex, "打开光影包文件夹失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs) Handles BtnRefresh.Click
        Hint("正在刷新……", Log:=False)
        RefreshList()
    End Sub

#End Region

#Region "光影配置编辑器"

    '当前展开的配置面板与它对应的行：同一时间只展开一个
    Private ConfigHost As Border = Nothing
    Private ConfigRow As FrameworkElement = Nothing
    '当前展开的配置，供「恢复默认」重建面板用
    Private ConfigCurrent As DstShaderConfig.ShaderConfig = Nothing

    ''' <summary>展开 / 收起某个光影包的配置编辑器。</summary>
    Private Sub ToggleConfigEditor(Entry As ShaderPackItem)
        If Entry Is Nothing OrElse Entry.Row Is Nothing Then Return
        '已经展开在同一个包上：收起来
        If ConfigHost IsNot Nothing AndAlso ConfigRow Is Entry.Row Then
            CloseConfigEditor()
            Return
        End If
        CloseConfigEditor()
        If PanList Is Nothing Then Return

        Dim Host As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Margin = New Thickness(0, -2, 0, 8),
            .Padding = New Thickness(12, 10, 12, 12),
            .Background = New SolidColorBrush(BackColorSub)
        }
        Host.Child = HintLine("正在读取光影包配置……")
        Dim Index As Integer = PanList.Children.IndexOf(Entry.Row)
        If Index < 0 Then Return
        PanList.Children.Insert(Index + 1, Host)
        ConfigHost = Host
        ConfigRow = Entry.Row

        Dim PackPath As String = Entry.PackPath
        '解析要读压缩包与着色器源码，放后台做
        RunInThread(
        Sub()
            Dim Cfg As DstShaderConfig.ShaderConfig = Nothing
            Try
                Cfg = DstShaderConfig.Load(PackPath)
            Catch ex As Exception
                Logger.Warn(ex, "读取光影配置失败：" & PackPath)
            End Try
            RunInUi(
            Sub()
                If Host IsNot ConfigHost Then Return '已经被关掉或换到别的包了
                ConfigCurrent = Cfg
                FillConfigPanel(Cfg)
            End Sub)
        End Sub)
    End Sub

    ''' <summary>收起配置面板。</summary>
    Private Sub CloseConfigEditor()
        If ConfigHost IsNot Nothing Then
            Try
                If PanList IsNot Nothing AndAlso PanList.Children.Contains(ConfigHost) Then PanList.Children.Remove(ConfigHost)
            Catch ex As Exception
                Logger.Warn(ex, "移除光影配置面板失败")
            End Try
        End If
        ConfigHost = Nothing
        ConfigRow = Nothing
        ConfigCurrent = Nothing
    End Sub

    ''' <summary>把解析结果画进面板。</summary>
    Private Sub FillConfigPanel(Cfg As DstShaderConfig.ShaderConfig)
        If ConfigHost Is Nothing Then Return
        If Cfg Is Nothing Then
            ConfigHost.Child = HintLine("读取失败，换个光影包试试")
            Return
        End If
        If Not Cfg.HasOptions Then
            ConfigHost.Child = HintLine(If(Cfg.LoadError <> "", Cfg.LoadError, "这个光影包没有提供可配置项"))
            Return
        End If

        Dim Root As New StackPanel

        '标题行：说明 + 保存 / 导出 / 恢复默认（全部用图标按钮）
        Dim Head As New Grid
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim TitleBox As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        TitleBox.Children.Add(New TextBlock With {
            .Text = "光影配置", .FontSize = 12.5, .FontWeight = FontWeights.Bold})
        Dim ConfigName As String = PathUtils.GetLastPart(Cfg.ConfigPath)
        TitleBox.Children.Add(New TextBlock With {
            .Text = $"{Cfg.Options.Count} 项 · 保存后写入 {ConfigName}",
            .FontSize = 11, .Opacity = 0.5, .Margin = New Thickness(0, 2, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis})
        Head.Children.Add(TitleBox)

        Dim Buttons As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim BtnSave As New MyIconButton With {
            .Logo = Logo.IconButtonSave, .Width = 28, .Height = 28, .LogoScale = 0.8,
            .ToolTip = "保存到游戏读取的配置位置"}
        AddHandler BtnSave.Click, Sub() SaveConfig(Cfg, "")
        Buttons.Children.Add(BtnSave)
        Dim BtnExport As New MyIconButton With {
            .Logo = Logo.IconButtonArrowDown, .Width = 28, .Height = 28, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "导出成文件"}
        AddHandler BtnExport.Click, Sub() ExportConfig(Cfg)
        Buttons.Children.Add(BtnExport)
        Dim BtnReset As New MyIconButton With {
            .Logo = Logo.IconButtonReset, .Width = 28, .Height = 28, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "全部恢复成默认值"}
        AddHandler BtnReset.Click, Sub() ResetConfig()
        Buttons.Children.Add(BtnReset)
        Grid.SetColumn(Buttons, 1)
        Head.Children.Add(Buttons)
        Root.Children.Add(Head)

        '选项区自带滚动条，免得把整页拉得太长
        Dim Scroll As New MyScrollViewer With {
            .Margin = New Thickness(0, 10, 0, 0), .MaxHeight = 380,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled}
        Dim Inner As New StackPanel
        Dim LastGroup As String = ""
        For Each Opt In Cfg.Options
            If Opt.GroupName <> LastGroup Then
                LastGroup = Opt.GroupName
                Dim GroupTitle As New TextBlock With {
                    .Text = LastGroup, .FontSize = 11.5, .FontWeight = FontWeights.Bold,
                    .Opacity = 0.75, .Margin = New Thickness(0, If(Inner.Children.Count = 0, 2, 14), 0, 6)}
                Inner.Children.Add(GroupTitle)
            End If
            Inner.Children.Add(BuildOptionRow(Opt))
        Next
        Scroll.Content = Inner
        Root.Children.Add(Scroll)

        Root.Children.Add(HintLine("改完点右上角的保存图标即可。改过的项会显示成强调色；恢复默认会把所有项还原成光影包自带的初始值。"))
        ConfigHost.Child = Root
    End Sub

    ''' <summary>画一行选项：左边名字，右边控件。</summary>
    Private Function BuildOptionRow(Opt As DstShaderConfig.ShaderOption) As FrameworkElement
        Dim Row As New Grid With {.Margin = New Thickness(0, 0, 0, 6)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(240)})

        Dim Lab As New TextBlock With {
            .Text = Opt.Label, .FontSize = 11.5, .VerticalAlignment = VerticalAlignment.Center,
            .TextTrimming = TextTrimming.CharacterEllipsis}
        ApplyOptionLabelStyle(Lab, Opt)
        Row.Children.Add(Lab)

        Dim Ctrl As New Grid With {.VerticalAlignment = VerticalAlignment.Center}
        Select Case Opt.Kind
            Case DstShaderConfig.OptionKind.Switch
                Dim Chk As New MyCheckBox With {
                    .Text = If(Opt.Value = "true", "开", "关"),
                    .Checked = (Opt.Value = "true"),
                    .HorizontalAlignment = HorizontalAlignment.Right}
                AddHandler Chk.Change,
                    Sub(sender As Object, user As Boolean)
                        Opt.Value = If(Chk.Checked, "true", "false")
                        Chk.Text = If(Chk.Checked, "开", "关")
                        Opt.IsCustomized = True
                        ApplyOptionLabelStyle(Lab, Opt)
                    End Sub
                Ctrl.Children.Add(Chk)
            Case DstShaderConfig.OptionKind.Choice
                Dim Btn As New MyButton With {
                    .Text = ChoiceDisplay(Opt, Opt.Value), .Height = 26, .MinWidth = 160,
                    .HorizontalAlignment = HorizontalAlignment.Right}
                AddHandler Btn.Click,
                    Sub()
                        If Opt.Choices.Count = 0 Then Return
                        Dim Items As New List(Of IMyRadio)
                        For Each Value In Opt.Choices
                            Dim Text As String = ChoiceDisplay(Opt, Value)
                            Dim Radio As New MyRadioBox With {.Text = Text}
                            If Value.Equals(Opt.Value, StringComparison.OrdinalIgnoreCase) Then Radio.Checked = True
                            Items.Add(Radio)
                        Next
                        Dim Picked As Integer? = MyMsgBoxSelect(Items, Opt.Label)
                        If Picked Is Nothing Then Return
                        Opt.Value = Opt.Choices(Picked.Value)
                        Opt.IsCustomized = True
                        Btn.Text = ChoiceDisplay(Opt, Opt.Value)
                        ApplyOptionLabelStyle(Lab, Opt)
                    End Sub
                Ctrl.Children.Add(Btn)
            Case Else
                If Opt.SliderMax > 0 Then
                    '能确认是 0~1 的浮点才给滑条，避免瞎猜范围把值夹掉
                    Dim Slider As New MySlider With {
                        .MaxValue = Opt.SliderMax, .HorizontalAlignment = HorizontalAlignment.Stretch}
                    Dim Percent As Double = 0
                    Double.TryParse(Opt.Value, Globalization.NumberStyles.Float,
                                    Globalization.CultureInfo.InvariantCulture, Percent)
                    Slider.Value = CInt(Math.Round(Math.Max(0, Math.Min(1, Percent)) * Opt.SliderMax))
                    AddHandler Slider.Change,
                        Sub(sender As Object, user As Boolean)
                            Dim Ratio As Double = Slider.Value / CDbl(Opt.SliderMax)
                            Opt.Value = Ratio.ToString("0.###", Globalization.CultureInfo.InvariantCulture)
                            Opt.IsCustomized = True
                            ApplyOptionLabelStyle(Lab, Opt)
                        End Sub
                    Ctrl.Children.Add(Slider)
                Else
                    Dim Box As New MyTextBox With {
                        .Text = Opt.Value, .Width = 140, .Height = 26,
                        .HorizontalAlignment = HorizontalAlignment.Right}
                    AddHandler Box.TextChanged,
                        Sub()
                            Dim Text As String = Box.Text.Trim()
                            If Text = "" Then Return
                            Opt.Value = Text
                            Opt.IsCustomized = True
                            ApplyOptionLabelStyle(Lab, Opt)
                        End Sub
                    Ctrl.Children.Add(Box)
                End If
        End Select
        Grid.SetColumn(Ctrl, 1)
        Row.Children.Add(Ctrl)
        Return Row
    End Function

    ''' <summary>改过的项用强调色标出来，一眼能看出哪些不是默认值。</summary>
    Private Sub ApplyOptionLabelStyle(Lab As TextBlock, Opt As DstShaderConfig.ShaderOption)
        If Opt.IsCustomized Then
            Lab.Foreground = New SolidColorBrush(Color2)
            Lab.ToolTip = "已改为 " & Opt.Value & "（默认 " & Opt.DefaultValue & "）"
        Else
            Lab.Foreground = New SolidColorBrush(Color1)
            Lab.ToolTip = "默认值 " & Opt.DefaultValue
        End If
    End Sub

    ''' <summary>枚举值优先用语言文件里的名字，没有就用原始值。</summary>
    Private Function ChoiceDisplay(Opt As DstShaderConfig.ShaderOption, Value As String) As String
        Dim Text As String = ""
        If Opt.ChoiceLabels.TryGetValue(Value, Text) AndAlso Not String.IsNullOrWhiteSpace(Text) Then Return Text
        Return Value
    End Function

    ''' <summary>保存到游戏读取的 txt；TargetPath 非空则写到指定位置。</summary>
    Private Sub SaveConfig(Cfg As DstShaderConfig.ShaderConfig, TargetPath As String)
        Dim Err As String = DstShaderConfig.Save(Cfg, TargetPath)
        If Err = "" Then
            Dim Where As String = If(String.IsNullOrWhiteSpace(TargetPath), Cfg.ConfigPath, TargetPath)
            Hint("已保存光影配置：" & PathUtils.GetLastPart(Where), HintType.Green)
        Else
            Hint(Err, HintType.Red)
        End If
    End Sub

    ''' <summary>导出成文件，让用户自己选位置。</summary>
    Private Sub ExportConfig(Cfg As DstShaderConfig.ShaderConfig)
        Dim Target As String = Dialogs.SaveFile("导出光影配置", Cfg.PackKey & ".txt",
                                                PathUtils.RemoveLastPart(Cfg.ConfigPath),
                                                {("txt", "光影配置文件")})
        If String.IsNullOrWhiteSpace(Target) Then Return
        SaveConfig(Cfg, Target)
    End Sub

    ''' <summary>所有项恢复成光影包自带的默认值，然后重画面板。</summary>
    Private Sub ResetConfig()
        If ConfigCurrent Is Nothing Then Return
        For Each Opt In ConfigCurrent.Options
            Opt.Value = Opt.DefaultValue
            Opt.IsCustomized = False
        Next
        FillConfigPanel(ConfigCurrent)
        Hint("已恢复默认值，记得点保存", HintType.Blue)
    End Sub

    Private Function HintLine(Text As String) As FrameworkElement
        Return New TextBlock With {
            .Text = Text, .FontSize = 11, .Opacity = 0.5, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 8, 0, 0)}
    End Function

#End Region

#Region "行与图标"

    ''' <summary>一行条目：图标 + 名称 + 说明 + 右侧按钮。读不到图标时换成 NoIcon。</summary>
    Private Function BuildRow(IconSource As ImageSource, Title As String, SubText As String,
                              Buttons As List(Of FrameworkElement)) As FrameworkElement
        Dim IconElement As FrameworkElement
        If IconSource Is Nothing Then
            IconElement = New MyImage With {.Stretch = Stretch.UniformToFill, .Source = PathImage & "Icons/NoIcon.png"}
        Else
            IconElement = New Image With {.Stretch = Stretch.UniformToFill, .Source = IconSource}
        End If
        Return BuildRowWith(IconElement, Title, SubText, Buttons)
    End Function

    ''' <summary>同一行，但图标元素由调用方提供。</summary>
    Private Function BuildRowWith(IconElement As FrameworkElement, Title As String, SubText As String,
                                  Buttons As List(Of FrameworkElement)) As FrameworkElement
        Dim Row As New Border With {
            .Height = 48, .CornerRadius = New CornerRadius(6), .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(10, 0, 10, 0), .Background = New SolidColorBrush(BackColorSub)
        }
        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim IconBox As New Border With {
            .Width = 32, .Height = 32, .CornerRadius = New CornerRadius(4),
            .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center,
            .Background = New SolidColorBrush(BackColorMain)
        }
        IconBox.Child = IconElement
        G.Children.Add(IconBox)
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim Info As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Dim TitleBlock As New TextBlock With {
            .Text = Title, .FontSize = 12.5, .FontWeight = FontWeights.Bold,
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        Info.Children.Add(TitleBlock)
        If SubText <> "" Then
            Info.Children.Add(New TextBlock With {
                .Text = SubText, .FontSize = 11, .Opacity = 0.5, .Margin = New Thickness(0, 2, 0, 0),
                .TextTrimming = TextTrimming.CharacterEllipsis
            })
        End If
        Grid.SetColumn(Info, 1)
        G.Children.Add(Info)

        Dim RightBox As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center
        }
        If Buttons IsNot Nothing Then
            For Each Btn As FrameworkElement In Buttons
                RightBox.Children.Add(Btn)
            Next
        End If
        Grid.SetColumn(RightBox, 2)
        G.Children.Add(RightBox)

        Row.Child = G
        Row.Tag = TitleBlock
        Return Row
    End Function

    ''' <summary>读出压缩包里某个条目的原始字节，条目不存在时返回 Nothing。</summary>
    Private Function ReadIconEntry(Entry As ZipArchiveEntry) As Byte()
        If Entry Is Nothing OrElse Entry.Length <= 0 Then Return Nothing
        Using Ms As New MemoryStream()
            Using ZipStream As Stream = Entry.Open()
                ZipStream.CopyTo(Ms)
            End Using
            Return Ms.ToArray()
        End Using
    End Function

    ''' <summary>读出本地图片文件的原始字节，文件不存在时返回 Nothing。</summary>
    Private Function ReadIconFile(IconPath As String) As Byte()
        If Not FileUtils.Exists(IconPath) Then Return Nothing
        Try
            Return File.ReadAllBytes(IconPath)
        Catch ex As Exception
            Logger.Warn(ex, "读取光影包图标失败：" & IconPath)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 列表行左侧的图标。
    ''' 光影包几乎都不带 pack.png（这点和资源包不一样），读不到时按包名生成一个带首字的色块，
    ''' 至少能让不同光影包在列表里互相区分，而不是清一色的占位图。
    ''' </summary>
    Private Function MakePackIcon(Entry As ShaderPackItem) As FrameworkElement
        Dim Source As ImageSource = MakeIcon(Entry.IconBytes)
        If Source IsNot Nothing Then
            Return New Image With {.Stretch = Stretch.UniformToFill, .Source = Source}
        End If
        Return BuildLetterIcon(Entry.DisplayName)
    End Function

    ''' <summary>用包名生成色块图标：色相由名字决定，同一包每次都一样。</summary>
    Private Function BuildLetterIcon(Name As String) As FrameworkElement
        Dim Text As String = "?"
        Dim Clean As String = If(Name, "").Trim()
        If Clean.Length > 0 Then Text = Clean.Substring(0, 1).ToUpper()
        '自己算个稳定色相，别用 String.GetHashCode（.NET 里不保证跨进程一致）
        '用 Long 累加再取模，Integer 在这里会因默认的溢出检查直接抛异常
        Dim Sum As Long = 0
        For Each C As Char In Clean
            Sum = (Sum * 31 + AscW(C)) Mod 2147483647L
        Next
        Dim Hue As Integer = CInt(Sum Mod 360L)
        Dim Box As New Border With {
            .CornerRadius = New CornerRadius(4),
            .Background = New SolidColorBrush(New MyColor().FromHSL(Hue, 52, 46))
        }
        Box.Child = New TextBlock With {
            .Text = Text, .FontSize = 16, .FontWeight = FontWeights.Bold,
            .Foreground = New SolidColorBrush(New MyColor(255, 255, 255)),
            .HorizontalAlignment = HorizontalAlignment.Center,
            .VerticalAlignment = VerticalAlignment.Center
        }
        Return Box
    End Function

    ''' <summary>光影包图标路径。图标在内存里，需要先写入缓存目录再交给列表项。</summary>
    Private Function IconCachePath(Entry As ShaderPackItem) As String
        Return MyLocalModItem.CacheIcon(Entry.PackPath, Entry.IconBytes)
    End Function

    ''' <summary>把图标的字节解码成位图。读不到就是 Nothing。</summary>
    Private Function MakeIcon(Data As Byte()) As ImageSource
        If Data Is Nothing OrElse Data.Length = 0 Then Return Nothing
        Try
            Using Ms As New MemoryStream(Data)
                Dim Bmp As New BitmapImage()
                Bmp.BeginInit()
                Bmp.CacheOption = BitmapCacheOption.OnLoad
                Bmp.StreamSource = Ms
                Bmp.EndInit()
                Return Bmp
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "解码光影包图标失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 从 shaders.properties 里读光影包自己的名字。
    ''' 这个文件的键名各家写法不统一，这里只认几个常见的名字键。
    ''' </summary>
    Private Function ParsePackTitle(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return ""
        For Each Line In Text.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
            Dim Row As String = Line.Trim()
            If Row = "" OrElse Row.StartsWith("#") OrElse Row.StartsWith("!") Then Continue For
            Dim Cut As Integer = Row.IndexOf("="c)
            If Cut < 0 Then Continue For
            Select Case Row.Substring(0, Cut).Trim().ToLower()
                Case "name", "pack.name", "packname", "shaderpack.name", "title"
                    Exit Select
                Case Else
                    Continue For
            End Select
            Dim Value As String = Row.Substring(Cut + 1).Trim()
            If Value.Length >= 2 Then
                Dim IsQuoted As Boolean = (Value(0) = """"c AndAlso Value(Value.Length - 1) = """"c) OrElse
                                          (Value(0) = "'"c AndAlso Value(Value.Length - 1) = "'"c)
                If IsQuoted Then Value = Value.Substring(1, Value.Length - 2).Trim()
            End If
            If Value <> "" Then Return Value
        Next
        Return ""
    End Function

    Private Function ReadTitleFromEntry(Entry As ZipArchiveEntry) As String
        If Entry Is Nothing OrElse Entry.Length <= 0 Then Return ""
        Using ZipStream As Stream = Entry.Open()
            Using Reader As New StreamReader(ZipStream)
                Return ParsePackTitle(Reader.ReadToEnd())
            End Using
        End Using
    End Function

    Private Function ReadTitleFromFile(PropPath As String) As String
        If Not FileUtils.Exists(PropPath) Then Return ""
        Try
            Return ParsePackTitle(FileUtils.ReadAsString(PropPath))
        Catch ex As Exception
            Logger.Warn(ex, "读取 shaders.properties 失败：" & PropPath)
            Return ""
        End Try
    End Function


    '搜索与筛选：只重绘列表，不重新扫盘
    '多选操作条
    Private Sub BtnSelectAll_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSelectAll.Click
        ChangeAllSelected(SelectedPaths.Count < ShownItems.Count)
    End Sub

    Private Sub BtnSelectEnable_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectEnable.Click
        BatchToggle(True)
    End Sub

    Private Sub BtnSelectDisable_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectDisable.Click
        BatchToggle(False)
    End Sub

    Private Sub BtnSelectDelete_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectDelete.Click
        BatchDelete()
    End Sub

    Private Sub BtnSelectCancel_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectCancel.Click
        ChangeAllSelected(False)
    End Sub

    Private Sub SearchBox_TextChanged(sender As Object, e As EventArgs) Handles SearchBox.TextChanged
        ApplyFilter()
    End Sub

    Private Sub Filter_Check(sender As Object, raiseByMouse As Boolean) Handles BtnFilterAll.Check, BtnFilterEnabled.Check, BtnFilterDisabled.Check
        Dim Btn = TryCast(sender, MyRadioButton)
        If Btn Is Nothing OrElse Not Btn.Checked Then Return
        ApplyFilter()
    End Sub

    Private Function EmptyHint(Text As String) As FrameworkElement
        Return New TextBlock With {
            .Text = Text, .FontSize = 11.5, .Opacity = 0.55, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 0, 0, 4)
        }
    End Function

#End Region

End Class
