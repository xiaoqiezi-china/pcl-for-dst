''' <summary>
''' 版本设置 - 资源包管理页。
''' 列出版本隔离目录下 resourcepacks 文件夹里的 .zip 与文件夹形态资源包，支持启用 / 停用、打开位置与删除。
''' 结构与交互照抄 Mod 管理页（PageInstanceMod），列表行由代码动态生成。
''' </summary>
Public Class PageInstanceResourcePack
    Implements IRefreshable

#Region "初始化"

    Private Sub PageInstanceResourcePack_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '多选条用 PCL 自带的图标，和模组管理保持一致
        BtnSelectEnable.Logo = Logo.IconButtonCheck
        BtnSelectDisable.Logo = Logo.IconButtonStop
        BtnSelectDelete.Logo = Logo.IconButtonDelete
        BtnSelectCancel.Logo = Logo.IconButtonCross
        RefreshList()
    End Sub

    ''' <summary>当前版本隔离后的资源包文件夹，末尾带反斜杠。</summary>
    Private ReadOnly Property PackFolder As String
        Get
            If PageInstanceLeft.Instance Is Nothing Then Return ""
            Return PageInstanceLeft.Instance.PathIndie & "resourcepacks\"
        End Get
    End Property

    ''' <summary>
    ''' 当前版本真正决定资源包启停的 options.txt。
    ''' 游戏只认这里的 resourcePacks 列表，文件夹里有文件不代表被启用。
    ''' </summary>
    Private ReadOnly Property OptionsFile As String
        Get
            If PageInstanceLeft.Instance Is Nothing Then Return ""
            Return PageInstanceLeft.Instance.PathIndie & "options.txt"
        End Get
    End Property

    ''' <summary>刷新资源包列表。会被侧边栏的刷新按钮调用。</summary>
    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshList()
    End Sub

#End Region

#Region "列表"

    ''' <summary>
    ''' 列表中的一个资源包。
    ''' 字段与磁盘约定都来自 <see cref="ModPackFolder.PackFolderItem"/>，
    ''' 和光影包页共用同一套扫描与改名逻辑，这里只是给本页起个短名字。
    ''' </summary>
    Private Class PackItem
        Inherits ModPackFolder.PackFolderItem
    End Class

    '刷新序号，用于丢弃过期的后台扫描结果
    Private RefreshToken As Integer = 0
    '最近一次扫描到的完整列表，供搜索与筛选复用，避免每次输入都重新扫盘
    Private AllItems As New List(Of PackItem)
    Private CurrentFolder As String = ""
    '当前显示出来的条目（已筛选），全选只作用于它们
    Private ShownItems As New List(Of PackItem)
    '多选：保存被选中条目的完整路径
    Private SelectedPaths As New HashSet(Of String)

    ''' <summary>重新扫描资源包文件夹，并刷新界面。</summary>
    Private Sub RefreshList()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        Dim Folder As String = PackFolder
        If Folder = "" Then
            PanList.Children.Add(EmptyHint("还没有选择版本，无法读取资源包。"))
            Return
        End If
        Logger.Info("已刷新资源包列表")
        RefreshToken += 1
        Dim Token As Integer = RefreshToken
        RunInThread(
        Sub()
            Dim Items As New List(Of PackItem)
            Try
                '扫描规则与光影包页共用：.zip 启用、.zip.disabled 停用、文件夹不参与切换
                For Each Entry In ModPackFolder.ScanPackFolder(Folder, "资源包", OptionsFile:=OptionsFile)
                    Items.Add(New PackItem With {
                        .PackPath = Entry.PackPath, .DisplayName = Entry.DisplayName,
                        .IsFolder = Entry.IsFolder, .IsEnabled = Entry.IsEnabled,
                        .IsActive = Entry.IsActive,
                        .SizeBytes = Entry.SizeBytes})
                Next
            Catch ex As Exception
                Logger.Warn(ex, "读取资源包文件夹失败")
            End Try
            RunInUi(
            Sub()
                If Token <> RefreshToken Then Return '已经有更新的一次扫描了
                ShowList(Items, Folder)
            End Sub)
        End Sub)
    End Sub

    ''' <summary>把扫描结果画成列表行。只能在 UI 线程调用。</summary>
    Private Sub ShowList(Items As List(Of PackItem), Folder As String)
        AllItems = Items
        CurrentFolder = Folder
        ApplyFilter()
    End Sub

    ''' <summary>按搜索框与筛选标签过滤后重新绘制列表。</summary>
    Private Sub ApplyFilter()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        If AllItems.Count = 0 Then
            PanList.Children.Add(EmptyHint("还没有资源包。把 .zip 文件或资源包文件夹放进 " & CurrentFolder & " 即可。"))
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
               Item.DisplayName.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            '筛选依据是游戏是否真的加载了它，不是文件后缀
            Select Case Mode
                Case 1 : If Not Item.IsActive Then Continue For
                Case 2 : If Item.IsActive Then Continue For
            End Select
            Shown.Add(Item)
        Next

        ShownItems = Shown
        If Shown.Count = 0 Then
            PanList.Children.Add(EmptyHint("没有符合条件的资源包"))
            RefreshSelectBar()
            Return
        End If
        For Each Item In Shown
            Dim Entry As PackItem = Item '防止闭包捕获同一个循环变量
            PanList.Children.Add(BuildPackRow(Entry))
        Next
        PanList.Children.Add(New TextBlock With {
            .Text = $"共 {Shown.Count} 个",
            .FontSize = 11, .Opacity = 0.45, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 6, 0, 0)
        })
        RefreshSelectBar()
    End Sub

#Region "多选"

    ''' <summary>切换某条目的选中状态。</summary>
    Private Sub ToggleSelect(Entry As PackItem)
        If Not SelectedPaths.Remove(Entry.PackPath) Then SelectedPaths.Add(Entry.PackPath)
        ApplyFilter()
    End Sub

    ''' <summary>全选 / 取消全选当前显示出来的条目。</summary>
    Private Sub ChangeAllSelected(ShouldSelect As Boolean)
        SelectedPaths.Clear()
        If ShouldSelect Then
            For Each Item In ShownItems
                SelectedPaths.Add(Item.PackPath)
            Next
        End If
        ApplyFilter()
    End Sub

    ''' <summary>按选中数量刷新底部操作条。</summary>
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
        '启用 = 在 options.txt 里；停用 = 在文件夹里但不在 options.txt 里。
        '所以两个按钮各看各的：已经全启用时「启用」无事可做，反之亦然。
        Dim Selected As List(Of PackItem) = AllItems.Where(Function(I) SelectedPaths.Contains(I.PackPath)).ToList()
        BtnSelectEnable.IsEnabled = Selected.Any(Function(I) Not I.IsActive)
        BtnSelectDisable.IsEnabled = Selected.Any(Function(I) I.IsActive)
        If CardSelect.Visibility <> Visibility.Visible Then
            CardSelect.Visibility = Visibility.Visible
            CardSelect.Opacity = 0
            AniStart(AaOpacity(CardSelect, 1 - CardSelect.Opacity, 90), "SelectBar")
        End If
    End Sub

    ''' <summary>批量启用 / 停用选中的资源包。</summary>
    Private Sub BatchToggle(TargetEnabled As Boolean)
        Dim Targets = AllItems.Where(Function(I) SelectedPaths.Contains(I.PackPath) AndAlso I.IsActive <> TargetEnabled).ToList()
        If Targets.Count = 0 Then Return
        Dim Done As Integer = 0
        Dim Failed As New List(Of String)
        For Each Entry In Targets
            Dim Reason As String = SetPackActive(Entry, TargetEnabled)
            If Reason = "" Then
                Done += 1
            Else
                Failed.Add(Entry.DisplayName & "：" & Reason)
            End If
        Next
        Hint($"已{(If(TargetEnabled, "启用", "停用"))} {Done} 个资源包", HintType.Green)
        '失败原因汇总成一条，别刷屏
        If Failed.Count > 0 Then Hint($"{Failed.Count} 个没能切换：" & String.Join("；", Failed.Take(3)), HintType.Red)
        SelectedPaths.Clear()
        RefreshList()
    End Sub

    ''' <summary>
    ''' 启用或停用一个资源包，返回空字符串表示成功，否则是可以直接展示的原因。
    ''' <para>
    ''' 只改 options.txt 里的 resourcePacks 列表，不碰磁盘上的文件名：
    ''' 游戏认的是这份列表，文件一直留在 resourcepacks 文件夹里。
    ''' </para>
    ''' </summary>
    Private Function SetPackActive(Entry As PackItem, TargetActive As Boolean) As String
        If Entry Is Nothing Then Return "条目为空"
        '极老的版本可能留着 .zip.disabled 的文件，启用时先改回 .zip，否则游戏根本读不到它
        If TargetActive AndAlso Not Entry.IsFolder AndAlso Not Entry.IsEnabled Then
            Dim Result = ModPackFolder.SetPackEnabled(Entry, True, "资源包")
            If Not Result.Ok Then Return Result.Reason
            Entry.PackPath = Result.NewPath
        End If
        Dim PackName As String = PathUtils.GetLastPart(Entry.PackPath)
        If TargetActive Then
            If Not ModPackState.EnablePack(OptionsFile, "file/" & PackName) Then Return "写入失败"
        Else
            If Not ModPackState.DisablePack(OptionsFile, "file/" & PackName) Then Return "写入失败"
        End If
        Return ""
    End Function

    ''' <summary>批量删除选中的资源包。</summary>
    Private Sub BatchDelete()
        Dim Targets = AllItems.Where(Function(I) SelectedPaths.Contains(I.PackPath)).ToList()
        If Targets.Count = 0 Then Return
        If MyMsgBox($"确定删除这 {Targets.Count} 个资源包吗？此操作不可撤销。", "删除资源包", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Dim Done As Integer = 0
        For Each Entry In Targets
            If ModPackFolder.DeletePackItem(Entry, "资源包") Then Done += 1
        Next
        Hint($"已删除 {Done} 个资源包", HintType.Green)
        SelectedPaths.Clear()
        RefreshList()
    End Sub

#End Region


#End Region

#Region "单个资源包项"

    ''' <summary>一个资源包行：图标 + 名字 + 状态与大小 + 右侧图标按钮。</summary>
    Private Function BuildPackRow(Entry As PackItem) As FrameworkElement
        '「已启用」说的是游戏里真的加载了它；文件在但没勾选是「未启用」
        Dim StateText As String = If(Entry.IsActive, "已启用", "未启用")
        If Entry.IsFolder Then StateText = "文件夹"
        Dim SubText As String = StateText & " · " & StringUtils.FormatByteSize(Entry.SizeBytes)
        Dim Buttons As New List(Of FrameworkElement)
        '启用 / 停用：文件夹形态由游戏直接读取，没有这个概念
        If Not Entry.IsFolder Then
            Dim BtnToggle As New MyIconButton With {
                .Logo = If(Entry.IsActive, Logo.IconButtonStop, Logo.IconButtonCheck),
                .Width = 26, .Height = 26, .LogoScale = 0.8, .Margin = New Thickness(6, 0, 0, 0),
                .ToolTip = If(Entry.IsActive, "停用", "启用")}
            AddHandler BtnToggle.Click, Sub() TogglePack(Entry)
            Buttons.Add(BtnToggle)
        End If
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
        '直接复用模组管理的列表项控件：选中竖条、悬停与点击动画全部继承
        Dim Item As New MyLocalModItem With {
            .Title = Entry.DisplayName,
            .Description = SubText,
            .ItemIsEnabled = Entry.IsActive,
            .Checked = SelectedPaths.Contains(Entry.PackPath),
            .LogoSource = PackIconPath(Entry)
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

    ''' <summary>切换资源包的启用状态（只改 options.txt）。</summary>
    Private Sub TogglePack(Entry As PackItem)
        Dim TargetActive As Boolean = Not Entry.IsActive
        Dim Reason As String = SetPackActive(Entry, TargetActive)
        If Reason <> "" Then
            Hint(Reason, HintType.Red)
            RefreshList()
            Return
        End If
        Hint(If(TargetActive, "已启用", "已停用") & "：" & Entry.DisplayName, HintType.Green)
        RefreshList()
    End Sub

    ''' <summary>打开这个资源包所在的文件夹。</summary>
    Private Sub OpenPackFolder(Entry As PackItem)
        Try
            OpenExplorer(PathUtils.RemoveLastPart(Entry.PackPath))
        Catch ex As Exception
            Logger.Error(ex, "打开资源包所在文件夹失败")
            Hint("打开文件夹失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    ''' <summary>删除资源包，删前先确认。</summary>
    Private Sub DeletePack(Entry As PackItem)
        If MyMsgBox("确定要删除资源包「" & Entry.DisplayName & "」吗？" & vbCrLf & "此操作不可撤销。",
                    "删除资源包", "删除", "取消", IsWarn:=True) <> 1 Then Return
        If Not ModPackFolder.DeletePackItem(Entry, "资源包") Then
            Hint("删除失败，详情见日志：" & Entry.DisplayName, HintType.Red)
            Return
        End If
        Logger.Info("已删除资源包：" & Entry.PackPath)
        Hint("已删除：" & Entry.DisplayName, HintType.Green)
        RefreshList()
    End Sub

#End Region

#Region "顶部按钮"

    ''' <summary>打开资源包文件夹，不存在就先建出来。</summary>
    Private Sub BtnOpenFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenFolder.Click
        Dim Folder As String = PackFolder
        If Folder = "" Then Return
        Try
            DirectoryUtils.Create(Folder)
            OpenExplorer(Folder)
        Catch ex As Exception
            Logger.Error(ex, "打开资源包文件夹失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs) Handles BtnRefresh.Click
        Hint("正在刷新……", Log:=False)
        RefreshList()
    End Sub

    ''' <summary>下载 CFPA 的自动构建汉化资源包，放进当前版本的 resourcepacks。</summary>
    Private Sub BtnFetchHanhua_Click(sender As Object, e As EventArgs) Handles BtnFetchHanhua.Click
        If PageInstanceLeft.Instance Is Nothing Then Return
        ModHanhua.Start(PageInstanceLeft.Instance)
    End Sub

#End Region

#Region "行与图标"

    ''' <summary>一行条目：图标 + 名称 + 说明 + 右侧按钮。</summary>
    Private Function BuildRow(IconSource As ImageSource, Title As String, SubText As String, RightText As String,
                              Buttons As List(Of FrameworkElement)) As FrameworkElement
        Dim IconElement As FrameworkElement
        If IconSource Is Nothing Then
            IconElement = New MyImage With {.Stretch = Stretch.UniformToFill, .Source = PathImage & "Icons/NoIcon.png"}
        Else
            IconElement = New Image With {.Stretch = Stretch.UniformToFill, .Source = IconSource}
        End If
        Return BuildRowWith(IconElement, Title, SubText, RightText, Buttons)
    End Function

    ''' <summary>同一行，但图标元素由调用方提供。行的 Tag 指向标题的 TextBlock。</summary>
    Private Function BuildRowWith(IconElement As FrameworkElement, Title As String, SubText As String, RightText As String,
                                  Buttons As List(Of FrameworkElement)) As FrameworkElement
        Dim Row As New Border With {
            .Height = 48, .CornerRadius = New CornerRadius(6), .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(10, 0, 10, 0), .Background = New SolidColorBrush(BackColorSub)
        }
        Dim G As New Grid
        Dim FirstColumn As Integer = 0
        If IconElement IsNot Nothing Then
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Dim IconBox As New Border With {
                .Width = 32, .Height = 32, .CornerRadius = New CornerRadius(4),
                .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center,
                .Background = New SolidColorBrush(BackColorMain)
            }
            IconBox.Child = IconElement
            G.Children.Add(IconBox)
            FirstColumn = 1
        End If
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
        Grid.SetColumn(Info, FirstColumn)
        G.Children.Add(Info)

        Dim RightBox As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center
        }
        If RightText <> "" Then
            RightBox.Children.Add(New TextBlock With {
                .Text = RightText, .FontSize = 11, .Opacity = 0.45,
                .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 10, 0)
            })
        End If
        If Buttons IsNot Nothing Then
            For Each Btn As FrameworkElement In Buttons
                RightBox.Children.Add(Btn)
            Next
        End If
        Grid.SetColumn(RightBox, FirstColumn + 1)
        G.Children.Add(RightBox)

        Row.Child = G
        Row.Tag = TitleBlock
        Return Row
    End Function

    ''' <summary>资源包图标路径。图标在 zip 内，需要先导出到缓存目录再交给列表项。</summary>
    Private Function PackIconPath(Entry As PackItem) As String
        Try
            If Entry.IsFolder Then
                Dim IconFile As String = PathUtils.AddSlashSuffix(Entry.PackPath) & "pack.png"
                If FileUtils.Exists(IconFile) Then Return IconFile
                Return ""
            End If
            Using Zip As ZipArchive = ZipFile.OpenRead(Entry.PackPath)
                For Each ZipEntry In Zip.Entries
                    If ZipEntry.FullName.Equals("pack.png", StringComparison.OrdinalIgnoreCase) Then
                        Using Ms As New MemoryStream
                            ZipEntry.Open().CopyTo(Ms)
                            Return MyLocalModItem.CacheIcon(Entry.PackPath, Ms.ToArray)
                        End Using
                    End If
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取资源包图标失败：" & Entry.PackPath)
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' 资源包图标：zip 里读根目录的 pack.png，文件夹读 pack.png。
    ''' 读不到返回 Nothing，由列表项换成 NoIcon。
    ''' </summary>
    Private Function PackIcon(Entry As PackItem) As ImageSource
        Try
            If Entry.IsFolder Then
                Dim IconFile As String = PathUtils.AddSlashSuffix(Entry.PackPath) & "pack.png"
                If FileUtils.Exists(IconFile) Then Return LoadImageFile(IconFile)
                Return Nothing
            End If
            Using Zip As ZipArchive = ZipFile.OpenRead(Entry.PackPath)
                For Each ZipEntry In Zip.Entries
                    If ZipEntry.FullName.Equals("pack.png", StringComparison.OrdinalIgnoreCase) Then Return LoadImageEntry(ZipEntry)
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取资源包图标失败：" & Entry.PackPath)
        End Try
        Return Nothing
    End Function

    Private Function LoadImageEntry(ZipEntry As ZipArchiveEntry) As ImageSource
        Using Ms As New MemoryStream()
            Using Stream As Stream = ZipEntry.Open()
                Stream.CopyTo(Ms)
            End Using
            Ms.Position = 0
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.StreamSource = Ms
            Bmp.EndInit()
            Bmp.Freeze()
            Return Bmp
        End Using
    End Function

    Private Function LoadImageFile(IconPath As String) As ImageSource
        Using Fs As New FileStream(IconPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            Using Ms As New MemoryStream()
                Fs.CopyTo(Ms)
                Ms.Position = 0
                Dim Bmp As New BitmapImage()
                Bmp.BeginInit()
                Bmp.CacheOption = BitmapCacheOption.OnLoad
                Bmp.StreamSource = Ms
                Bmp.EndInit()
                Bmp.Freeze()
                Return Bmp
            End Using
        End Using
    End Function

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

    '搜索与筛选：只重绘列表，不重新扫盘
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
