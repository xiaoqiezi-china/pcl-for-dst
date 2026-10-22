''' <summary>
''' 版本设置 - 资源包管理页。
''' 列出版本隔离目录下 resourcepacks 文件夹里的 .zip 与文件夹形态资源包，支持启用 / 停用、打开位置与删除。
''' 结构与交互照抄 Mod 管理页（PageInstanceMod），列表行由代码动态生成。
''' </summary>
Public Class PageInstanceResourcePack
    Implements IRefreshable

#Region "初始化"

    Private Sub PageInstanceResourcePack_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        RefreshList()
    End Sub

    ''' <summary>当前版本隔离后的资源包文件夹，末尾带反斜杠。</summary>
    Private ReadOnly Property PackFolder As String
        Get
            If PageInstanceLeft.Instance Is Nothing Then Return ""
            Return PageInstanceLeft.Instance.PathIndie & "resourcepacks\"
        End Get
    End Property

    ''' <summary>刷新资源包列表。会被侧边栏的刷新按钮调用。</summary>
    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshList()
    End Sub

#End Region

#Region "列表"

    ''' <summary>列表中的一个资源包。文件夹形态的 IsFolder 为 True，且不参与启用 / 停用。</summary>
    Private Class PackItem
        ''' <summary>完整路径（文件路径或文件夹路径）。</summary>
        Public PackPath As String = ""
        ''' <summary>显示名：文件夹名，或去掉 .zip / .zip.disabled 的文件名。</summary>
        Public DisplayName As String = ""
        ''' <summary>是不是文件夹形态。</summary>
        Public IsFolder As Boolean = False
        ''' <summary>是否处于启用状态（文件夹形态恒为 True）。</summary>
        Public IsEnabled As Boolean = False
        ''' <summary>大小，字节。</summary>
        Public SizeBytes As Long = 0
        ''' <summary>是否被停用（只有 zip 会被停用）。</summary>
        Public ReadOnly Property IsDisabled As Boolean
            Get
                Return Not IsFolder AndAlso Not IsEnabled
            End Get
        End Property
    End Class

    '刷新序号，用于丢弃过期的后台扫描结果
    Private RefreshToken As Integer = 0
    '最近一次扫描到的完整列表，供搜索与筛选复用，避免每次输入都重新扫盘
    Private AllItems As New List(Of PackItem)
    Private CurrentFolder As String = ""

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
                If DirectoryUtils.Exists(Folder) Then
                    'zip：启用的是 .zip，停用的改名为 .zip.disabled
                    For Each FilePath In DirectoryUtils.EnumerateFiles(Folder, False)
                        Dim FileName As String = PathUtils.GetLastPart(FilePath)
                        Dim Item As New PackItem With {.PackPath = FilePath}
                        If FileName.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase) Then
                            Item.IsEnabled = False
                            Item.DisplayName = FileName.Substring(0, FileName.Length - ".zip.disabled".Length)
                        ElseIf FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
                            Item.IsEnabled = True
                            Item.DisplayName = PathUtils.GetFileNameWithoutExtension(FilePath)
                        Else
                            Continue For '不认识的扩展名，不当成资源包
                        End If
                        Item.SizeBytes = FileSizeOf(FilePath)
                        Items.Add(Item)
                    Next
                    '文件夹形态：游戏会直接读，不参与启用 / 停用
                    For Each DirPath In DirectoryUtils.EnumerateDirectories(Folder, False)
                        Dim Item As New PackItem With {
                            .PackPath = DirPath, .DisplayName = PathUtils.GetLastPart(DirPath),
                            .IsFolder = True, .IsEnabled = True}
                        Item.SizeBytes = FolderSizeOf(DirPath)
                        Items.Add(Item)
                    Next
                End If
            Catch ex As Exception
                Logger.Warn(ex, "读取资源包文件夹失败")
            End Try
            '启用中的排在前面，其余按名字排
            Items.Sort(
                Function(A As PackItem, B As PackItem)
                    If A.IsDisabled <> B.IsDisabled Then Return If(A.IsDisabled, 1, -1)
                    Return String.Compare(A.DisplayName, B.DisplayName, StringComparison.OrdinalIgnoreCase)
                End Function)
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
            Select Case Mode
                Case 1 : If Not Item.IsEnabled Then Continue For
                Case 2 : If Item.IsEnabled Then Continue For
            End Select
            Shown.Add(Item)
        Next

        If Shown.Count = 0 Then
            PanList.Children.Add(EmptyHint("没有符合条件的资源包"))
            Return
        End If
        For Each Item In Shown
            Dim Entry As PackItem = Item '防止闭包捕获同一个循环变量
            PanList.Children.Add(BuildPackRow(Entry))
        Next
        PanList.Children.Add(New TextBlock With {
            .Text = $"共 {Shown.Count} 个。启用中的资源包是 .zip，停用后改名成 .zip.disabled；文件夹形态的资源包由游戏直接读取，这里不能切换。",
            .FontSize = 11, .Opacity = 0.45, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 6, 0, 0)
        })
    End Sub

    Private Function FileSizeOf(FilePath As String) As Long
        Try
            Return FileUtils.GetInfo(FilePath).Length
        Catch ex As Exception
            Logger.Warn(ex, "读取资源包大小失败：" & FilePath)
            Return 0
        End Try
    End Function

    Private Function FolderSizeOf(DirPath As String) As Long
        Dim Total As Long = 0
        Try
            For Each FilePath In DirectoryUtils.EnumerateFiles(DirPath, True)
                Try
                    Total += FileUtils.GetInfo(FilePath).Length
                Catch
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, "统计资源包文件夹大小失败：" & DirPath)
        End Try
        Return Total
    End Function

#End Region

#Region "单个资源包项"

    ''' <summary>一个资源包行：图标 + 名字 + 状态与大小 + 右侧图标按钮。</summary>
    Private Function BuildPackRow(Entry As PackItem) As FrameworkElement
        Dim StateText As String = If(Entry.IsFolder, "文件夹", If(Entry.IsEnabled, "已启用", "已停用"))
        Dim SubText As String = StateText & " · " & StringUtils.FormatByteSize(Entry.SizeBytes)
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
        '停用的整行压暗，一眼能分出来
        Dim Row As FrameworkElement = BuildRow(PackIcon(Entry), Entry.DisplayName, SubText, "", Buttons)
        If Entry.IsDisabled Then Row.Opacity = 0.6
        Return Row
    End Function

    ''' <summary>
    ''' 切换资源包的启用状态。
    ''' Minecraft 只认扩展名：.zip 启用，.zip.disabled 停用，所以这里只改文件名。
    ''' </summary>
    Private Sub TogglePack(Entry As PackItem)
        If Entry.IsFolder Then
            Hint("文件夹形态的资源包不能在启动器里启用或停用。", HintType.Red)
            Return
        End If
        If Not FileUtils.Exists(Entry.PackPath) Then
            Hint("这个资源包文件已经不在了：" & Entry.DisplayName, HintType.Red)
            RefreshList()
            Return
        End If
        Dim NewPath As String
        If Entry.IsEnabled Then
            NewPath = Entry.PackPath & ".disabled"
        Else
            NewPath = Entry.PackPath.Substring(0, Entry.PackPath.Length - ".disabled".Length)
        End If
        If FileUtils.Exists(NewPath) Then
            Hint("目标文件已存在，改名会覆盖它，所以停下了：" & PathUtils.GetLastPart(NewPath), HintType.Red)
            Return
        End If
        Try
            FileUtils.Move(Entry.PackPath, NewPath)
        Catch ex As Exception
            Logger.Error(ex, "切换资源包状态失败", LogBehavior.Alert)
            Hint("改名失败：" & ex.Message, HintType.Red)
            Return
        End Try
        Hint(If(Entry.IsEnabled, "已停用", "已启用") & "：" & Entry.DisplayName, HintType.Green)
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
        Try
            If Entry.IsFolder Then
                DirectoryUtils.Delete(Entry.PackPath, True)
            Else
                FileUtils.Delete(Entry.PackPath, True)
            End If
        Catch ex As Exception
            Logger.Error(ex, "删除资源包失败", LogBehavior.Alert)
            Return
        End Try
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

    ''' <summary>
    ''' 资源包图标：zip 里读根目录的 pack.png，文件夹读 pack.png。
    ''' 读不到返回 Nothing，由 BuildRow 换成 NoIcon。
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
