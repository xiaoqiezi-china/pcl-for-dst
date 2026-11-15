Imports System.IO
Imports System.IO.Compression
Imports Newtonsoft.Json.Linq

''' <summary>
''' 存档详情 - 数据包管理页。
''' <para>
''' 结构照抄资源包 / 光影包 / 模组管理页：搜索框、管理按钮、筛选标签、列表、多选操作条。
''' 之前的实现是一张卡片里堆出手搓的行，和 PCL 其它管理页对不上，这里统一过来。
''' </para>
''' <para>
''' 磁盘约定：<c>xxx.zip</c> 是启用，停用后改名成 <c>xxx.zip.disabled</c>；
''' 文件夹形态是开发中的包，游戏直接读，不参与启用 / 停用。
''' </para>
''' </summary>
Public Class PageInstanceSavePack
    Implements IRefreshable

#Region "初始化"

    '刷新序号，用于丢弃过期的后台扫描结果
    Private RefreshToken As Integer = 0
    '最近一次扫描到的完整列表，供搜索与筛选复用，避免每次输入都重新扫盘
    Private AllItems As New List(Of PackItem)
    Private CurrentFolder As String = ""
    '当前显示出来的条目（已筛选），全选只作用于它们
    Private ShownItems As New List(Of PackItem)
    '多选：保存被选中条目的完整路径
    Private SelectedPaths As New HashSet(Of String)

    Private Sub PageInstanceSavePack_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '多选条用 PCL 自带的图标，和其它管理页保持一致
        BtnSelectEnable.Logo = Logo.IconButtonCheck
        BtnSelectDisable.Logo = Logo.IconButtonStop
        BtnSelectDelete.Logo = Logo.IconButtonDelete
        BtnSelectCancel.Logo = Logo.IconButtonCross
        RefreshList()
    End Sub

    ''' <summary>当前存档的 datapacks 文件夹，末尾带反斜杠。</summary>
    Private ReadOnly Property PackFolder As String
        Get
            If PageInstanceSaveDetail.TargetSave = "" Then Return ""
            Return PathUtils.AddSlashSuffix(PageInstanceSaveDetail.TargetSave) & "datapacks" & ChrW(92)
        End Get
    End Property

    ''' <summary>刷新列表。会被左侧导航的刷新按钮调用。</summary>
    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshList()
    End Sub

#End Region

#Region "列表"

    ''' <summary>
    ''' 列表中的一个数据包。
    ''' 磁盘约定与扫描逻辑来自 <see cref="ModPackFolder"/>，和资源包 / 光影包页共用。
    ''' </summary>
    Private Class PackItem
        Inherits ModPackFolder.PackFolderItem
        ''' <summary>pack.mcmeta 里的说明，读不到是空字符串。</summary>
        Public PackDescription As String = ""
    End Class

    ''' <summary>重新扫描 datapacks 文件夹并刷新界面。</summary>
    Private Sub RefreshList()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        Dim Folder As String = PackFolder
        If Folder = "" Then
            PanList.Children.Add(EmptyHint("没有选择存档，无法读取数据包。"))
            Return
        End If
        Logger.Info("已刷新数据包列表")
        RefreshToken += 1
        Dim Token As Integer = RefreshToken
        RunInThread(
        Sub()
            Dim Items As New List(Of PackItem)
            Try
                '数据包的文件夹形态必须带 pack.mcmeta，否则游戏会直接忽略它
                For Each Entry In ModPackFolder.ScanPackFolder(Folder, "数据包", FolderMarkerFile:="pack.mcmeta")
                    Items.Add(New PackItem With {
                        .PackPath = Entry.PackPath, .DisplayName = Entry.DisplayName,
                        .IsFolder = Entry.IsFolder, .IsEnabled = Entry.IsEnabled,
                        .SizeBytes = Entry.SizeBytes,
                        .PackDescription = ModPackInfo.ReadPackDescription(Entry)})
                Next
            Catch ex As Exception
                Logger.Warn(ex, "读取数据包文件夹失败")
            End Try
            RunInUi(
            Sub()
                If Token <> RefreshToken Then Return '已经有更新的一次扫描了
                AllItems = Items
                CurrentFolder = Folder
                ApplyFilter()
            End Sub)
        End Sub)
    End Sub

    ''' <summary>按搜索框与筛选标签过滤后重新绘制列表。</summary>
    Private Sub ApplyFilter()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        If AllItems.Count = 0 Then
            PanList.Children.Add(EmptyHint("还没有数据包。把 .zip 文件或带 pack.mcmeta 的文件夹放进 datapacks 文件夹即可。"))
            RefreshSelectBar()
            Return
        End If

        Dim Keyword As String = If(SearchBox Is Nothing, "", SearchBox.Text.Trim())
        Dim Mode As Integer = 0
        If BtnFilterEnabled IsNot Nothing AndAlso BtnFilterEnabled.Checked Then Mode = 1
        If BtnFilterDisabled IsNot Nothing AndAlso BtnFilterDisabled.Checked Then Mode = 2
        If BtnFilterAll IsNot Nothing AndAlso BtnFilterAll.Checked Then Mode = 0

        Dim Shown As New List(Of PackItem)
        For Each Item In AllItems
            If Keyword <> "" AndAlso
               Item.DisplayName.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
               Item.PackDescription.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Select Case Mode
                Case 1 : If Not Item.IsEnabled Then Continue For
                Case 2 : If Item.IsEnabled Then Continue For
            End Select
            Shown.Add(Item)
        Next

        ShownItems = Shown
        If Shown.Count = 0 Then
            PanList.Children.Add(EmptyHint("没有符合条件的数据包"))
            RefreshSelectBar()
            Return
        End If
        For Each Item In Shown
            Dim Entry As PackItem = Item '防止闭包捕获同一个循环变量
            Try
                PanList.Children.Add(BuildPackRow(Entry))
            Catch ex As Exception
                Logger.Warn(ex, "绘制数据包条目失败：" & Entry.DisplayName)
            End Try
        Next
        PanList.Children.Add(New TextBlock With {
            .Text = $"共 {Shown.Count} 个",
            .FontSize = 11, .Opacity = 0.45, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 6, 0, 0)
        })
        RefreshSelectBar()
    End Sub

#Region "多选"

    Private Sub ToggleSelect(Entry As PackItem)
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
        '清掉已经不在列表里的选中项
        SelectedPaths.RemoveWhere(Function(P) Not AllItems.Any(Function(I) I.PackPath = P))
        Dim Count As Integer = SelectedPaths.Count
        If Count = 0 Then
            CardSelect.Visibility = Visibility.Collapsed
            Return
        End If
        LabSelect.Text = $"已选择 {Count} 个文件"
        '文件夹形态的包由游戏直接读，切换改名对它没有意义，所以只要选中有文件夹就禁用启停
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
        Dim Failed As New List(Of String)
        For Each Entry In Targets
            Dim Result = ModPackFolder.SetPackEnabled(Entry, TargetEnabled, "数据包")
            If Result.Ok Then
                Done += 1
            Else
                Failed.Add(Result.Reason)
            End If
        Next
        Hint($"已{If(TargetEnabled, "启用", "停用")} {Done} 个数据包", HintType.Green)
        '失败原因汇总成一条，别刷屏
        If Failed.Count > 0 Then Hint($"{Failed.Count} 个没能切换：" & String.Join("；", Failed.Take(3)), HintType.Red)
        SelectedPaths.Clear()
        RefreshList()
    End Sub

    Private Sub BatchDelete()
        Dim Targets = AllItems.Where(Function(I) SelectedPaths.Contains(I.PackPath)).ToList()
        If Targets.Count = 0 Then Return
        If MyMsgBox($"确定删除这 {Targets.Count} 个数据包吗？此操作不可撤销。", "删除数据包", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Dim Done As Integer = 0
        For Each Entry In Targets
            If ModPackFolder.DeletePackItem(Entry, "数据包") Then Done += 1
        Next
        Hint($"已删除 {Done} 个数据包", HintType.Green)
        SelectedPaths.Clear()
        RefreshList()
    End Sub

#End Region

#End Region

#Region "单个数据包项"

    ''' <summary>一个数据包行：图标 + 名字 + 状态与说明 + 右侧图标按钮。</summary>
    Private Function BuildPackRow(Entry As PackItem) As FrameworkElement
        Dim Parts As New List(Of String)
        Parts.Add(If(Entry.IsFolder, "文件夹", If(Entry.IsEnabled, "已启用", "已停用")))
        If Entry.SizeBytes >= 0 Then Parts.Add(StringUtils.FormatByteSize(Entry.SizeBytes))
        If Entry.PackDescription <> "" Then Parts.Add(Entry.PackDescription)

        Dim Buttons As New List(Of FrameworkElement)
        '启用 / 停用：文件夹形态由游戏直接读取，没有这个概念
        If Not Entry.IsFolder Then
            Dim BtnToggle As New MyIconButton With {
                .Logo = If(Entry.IsEnabled, Logo.IconButtonStop, Logo.IconButtonCheck),
                .Width = 26, .Height = 26, .LogoScale = 0.8, .Margin = New Thickness(6, 0, 0, 0),
                .ToolTip = If(Entry.IsEnabled, "停用", "启用")}
            AddHandler BtnToggle.Click, Sub() TogglePack(Entry)
            Buttons.Add(BtnToggle)
        End If
        Dim BtnOpen As New MyIconButton With {
            .Logo = Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "打开所在文件夹"}
        AddHandler BtnOpen.Click, Sub() OpenPackFolder(Entry)
        Buttons.Add(BtnOpen)
        Dim BtnDelete As New MyIconButton With {
            .Logo = Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "删除"}
        AddHandler BtnDelete.Click, Sub() DeletePack(Entry)
        Buttons.Add(BtnDelete)

        '直接复用模组管理的列表项控件：选中竖条、悬停与点击动画全部继承
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

    ''' <summary>切换数据包的启用状态。改名逻辑与资源包 / 光影包页共用。</summary>
    Private Sub TogglePack(Entry As PackItem)
        Dim Result = ModPackFolder.SetPackEnabled(Entry, Not Entry.IsEnabled, "数据包")
        If Not Result.Ok Then
            Hint(Result.Reason, HintType.Red)
            RefreshList()
            Return
        End If
        Hint(If(Entry.IsEnabled, "已停用", "已启用") & "：" & Entry.DisplayName, HintType.Green)
        RefreshList()
    End Sub

    Private Sub OpenPackFolder(Entry As PackItem)
        Try
            OpenExplorer(PathUtils.RemoveLastPart(Entry.PackPath))
        Catch ex As Exception
            Logger.Error(ex, "打开数据包所在文件夹失败")
            Hint("打开文件夹失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub DeletePack(Entry As PackItem)
        If MyMsgBox("确定要删除数据包「" & Entry.DisplayName & "」吗？" & vbCrLf & "此操作不可撤销。",
                    "删除数据包", "删除", "取消", IsWarn:=True) <> 1 Then Return
        If Not ModPackFolder.DeletePackItem(Entry, "数据包") Then
            Hint("删除失败，详情见日志：" & Entry.DisplayName, HintType.Red)
            Return
        End If
        Logger.Info("已删除数据包：" & Entry.PackPath)
        Hint("已删除：" & Entry.DisplayName, HintType.Green)
        RefreshList()
    End Sub

    ''' <summary>数据包图标。图标在压缩包里，需要先落盘才能交给列表项。</summary>
    Private Function IconCachePath(Entry As PackItem) As String
        Return MyLocalModItem.CacheIcon(Entry.PackPath, ModPackInfo.ReadPackIconBytes(Entry))
    End Function

#End Region

#Region "顶部按钮"

    Private Sub BtnOpenFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenFolder.Click
        Dim Folder As String = PackFolder
        If Folder = "" Then Return
        Try
            DirectoryUtils.Create(Folder)
            OpenExplorer(Folder)
        Catch ex As Exception
            Logger.Error(ex, "打开数据包文件夹失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs) Handles BtnRefresh.Click
        Hint("正在刷新……", Log:=False)
        RefreshList()
    End Sub

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

    '搜索与筛选：只重绘列表，不重新扫盘
    Private Sub SearchBox_TextChanged(sender As Object, e As EventArgs) Handles SearchBox.TextChanged
        ApplyFilter()
    End Sub

    Private Sub Filter_Check(sender As Object, raiseByMouse As Boolean) Handles BtnFilterAll.Check, BtnFilterEnabled.Check, BtnFilterDisabled.Check
        Dim Btn = TryCast(sender, MyRadioButton)
        If Btn Is Nothing OrElse Not Btn.Checked Then Return
        ApplyFilter()
    End Sub

#End Region

    Private Function EmptyHint(Text As String) As FrameworkElement
        Return New TextBlock With {
            .Text = Text, .FontSize = 11.5, .Opacity = 0.55, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 0, 0, 4)
        }
    End Function

End Class
