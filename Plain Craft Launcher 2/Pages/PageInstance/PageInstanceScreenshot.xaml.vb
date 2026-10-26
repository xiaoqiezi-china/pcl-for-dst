Imports System.Windows.Media.Imaging
Imports System.Windows.Media

''' <summary>
''' 版本设置 → 截图管理。
''' 目录走 Instance.PathIndie，版本隔离开不开都能取到正确的 screenshots 位置。
''' 单击勾选，双击或右键打开全屏预览，与存档页的交互保持一致。
''' </summary>
Public Class PageInstanceScreenshot

    ''' <summary>当前版本应用隔离后的 Minecraft 根目录。</summary>
    Private ReadOnly Property ShotRoot As String
        Get
            Dim Inst As McInstance = CurInstance
            If Inst Is Nothing Then Return ""
            Return Inst.PathIndie & "screenshots\"
        End Get
    End Property

    ''' <summary>当前版本设置页面对的实例。</summary>
    Private ReadOnly Property CurInstance As McInstance
        Get
            If PageInstanceLeft.Instance IsNot Nothing Then Return PageInstanceLeft.Instance
            Return ModMinecraft.McInstanceSelected
        End Get
    End Property

    Private Sub PageInstanceScreenshot_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        BtnSelectDelete.Logo = Logo.IconButtonDelete
        BtnSelectCancel.Logo = Logo.IconButtonCross
        AddHandler BtnOpenShots.Click, Sub() OpenShotsFolder()
        AddHandler BtnRefreshShots.Click, Sub() RefreshShots()
        AddHandler BtnSelectAll.Click, Sub() ChangeAllSelected(True)
        AddHandler BtnSelectDelete.Click, Sub() DeleteSelected()
        AddHandler BtnSelectCancel.Click, Sub() ClearSelection()
        AddHandler SearchBox.TextChanged, Sub() ApplyFilter()
        '每次切回本页都重画一次：收藏状态可能在收藏夹里被改过，
        '只靠建行时读一次的话星星会一直是旧的
        AddHandler PageEnter,
            Sub()
                'Loaded 里已经刷过一次，紧接着的 PageEnter 不必再扫一遍
                If Date.Now - LastRefreshAt < TimeSpan.FromMilliseconds(300) Then Return
                RefreshShots()
            End Sub
        RefreshShots()
    End Sub

    '上次重画列表的时间，用来挡掉 Loaded 与 PageEnter 的重复刷新
    Private LastRefreshAt As Date = Date.MinValue

    Public Sub RefreshShots()
        If PanShots Is Nothing Then Return
        LastRefreshAt = Date.Now
        Dim Root As String = ShotRoot
        CurrentRoot = Root
        SelectedPaths.Clear()
        PanShots.Children.Clear()
        If Root = "" Then
            PanShots.Children.Add(EmptyHint("请先选择一个版本"))
            RefreshSelectBar()
            Return
        End If
        If Not DirectoryUtils.Exists(Root) Then
            PanShots.Children.Add(EmptyHint("这个版本还没有截图。进游戏按 F2 即可截图。"))
            RefreshSelectBar()
            Return
        End If
        Dim Files As List(Of String)
        Try
            Files = DirectoryUtils.EnumerateFiles(Root, False).
                Where(Function(F) IsImage(F)).ToList()
        Catch ex As Exception
            Logger.Warn(ex, "枚举截图目录失败")
            PanShots.Children.Add(EmptyHint("读取截图目录失败：" & ex.Message))
            RefreshSelectBar()
            Return
        End Try
        If Files.Count = 0 Then
            PanShots.Children.Add(EmptyHint("这个版本还没有截图。进游戏按 F2 即可截图。"))
            RefreshSelectBar()
            Return
        End If
        '按修改时间倒序，最新的排前面
        Files.Sort(Function(A, B)
                       Dim TA As Date = Date.MinValue, TB As Date = Date.MinValue
                       Try
                           TA = FileUtils.GetInfo(A).LastWriteTime
                       Catch ex As Exception
                           Logger.Warn(ex, $"读取截图修改时间失败：{A}")
                       End Try
                       Try
                           TB = FileUtils.GetInfo(B).LastWriteTime
                       Catch ex As Exception
                           Logger.Warn(ex, $"读取截图修改时间失败：{B}")
                       End Try
                       Return TB.CompareTo(TA)
                   End Function)
        AllShots = Files
        ApplyFilter()
    End Sub

    Private Shared Function IsImage(Path As String) As Boolean
        Dim Ext As String = IO.Path.GetExtension(Path).ToLowerInvariant()
        Return Ext = ".png" OrElse Ext = ".jpg" OrElse Ext = ".jpeg"
    End Function

    '最近一次扫描到的截图，供搜索与筛选复用
    Private AllShots As New List(Of String)
    Private CurrentRoot As String = ""
    '当前显示出来的截图，全选只作用于它们
    Private ShownShots As New List(Of String)
    '多选：保存被选中截图的完整路径
    Private ReadOnly SelectedPaths As New HashSet(Of String)

    ''' <summary>按搜索框与时间筛选重新绘制列表。</summary>
    Private Sub ApplyFilter()
        If PanShots Is Nothing Then Return
        PanShots.Children.Clear()
        If AllShots.Count = 0 Then Return

        Dim Keyword As String = If(SearchBox Is Nothing, "", SearchBox.Text.Trim())
        Dim Range As Integer = 0 '0=全部 1=今天 2=本周 3=本月
        If BtnFilterToday IsNot Nothing AndAlso BtnFilterToday.Checked Then Range = 1
        If BtnFilterWeek IsNot Nothing AndAlso BtnFilterWeek.Checked Then Range = 2
        If BtnFilterMonth IsNot Nothing AndAlso BtnFilterMonth.Checked Then Range = 3

        Dim Now As Date = Date.Now
        Dim Shown As New List(Of String)
        For Each Shot In AllShots
            Dim Name As String = PathUtils.GetLastPart(Shot)
            If Keyword <> "" AndAlso Name.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            If Range <> 0 Then
                Dim T As Date
                Try
                    T = FileUtils.GetInfo(Shot).LastWriteTime
                Catch ex As Exception
                    Continue For
                End Try
                Select Case Range
                    Case 1 : If T.Date <> Now.Date Then Continue For
                    Case 2 : If T < Now.Date.AddDays(-6) Then Continue For
                    Case 3 : If T < Now.Date.AddDays(-29) Then Continue For
                End Select
            End If
            Shown.Add(Shot)
        Next

        ShownShots = Shown
        If Shown.Count = 0 Then
            PanShots.Children.Add(EmptyHint("没有符合条件的截图"))
            RefreshSelectBar()
            Return
        End If
        For Each Shot In Shown
            PanShots.Children.Add(BuildShotRow(Shot))
        Next
        RefreshSelectBar()
    End Sub

    Private Function EmptyHint(Text As String) As FrameworkElement
        Return New TextBlock With {
            .Text = Text, .Margin = New Thickness(4, 8, 4, 8),
            .TextWrapping = TextWrapping.Wrap,
            .Foreground = New SolidColorBrush(Color.FromRgb(128, 128, 128))
        }
    End Function

#Region "多选"

    Private Sub ToggleSelect(Shot As String)
        If Not SelectedPaths.Remove(Shot) Then SelectedPaths.Add(Shot)
        '只改这一行的勾选状态：重建整个列表会重新解码每一张缩略图，点一下要卡好几秒
        SyncCheckedState()
    End Sub

    Private Sub ClearSelection()
        SelectedPaths.Clear()
        SyncCheckedState()
    End Sub

    Private Sub ChangeAllSelected(ShouldSelect As Boolean)
        SelectedPaths.Clear()
        If ShouldSelect Then
            For Each Shot In ShownShots
                SelectedPaths.Add(Shot)
            Next
        End If
        SyncCheckedState()
    End Sub

    ''' <summary>把选中状态同步到已有行，不重建控件。</summary>
    Private Sub SyncCheckedState()
        If PanShots Is Nothing Then Return
        For Each Row In PanShots.Children.OfType(Of MyLocalModItem)()
            Dim Shot As String = TryCast(Row.ToolTip, String)
            If Shot Is Nothing Then Continue For
            Row.Checked = SelectedPaths.Contains(Shot)
        Next
        RefreshSelectBar()
    End Sub

    Private Sub DeleteSelected()
        If SelectedPaths.Count = 0 Then Return
        If MyMsgBox($"确定要删除选中的 {SelectedPaths.Count} 张截图吗？" & vbCrLf &
                    "此操作不可撤销。", "删除截图", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Dim Failed As Integer = 0
        For Each Shot In SelectedPaths.ToList()
            Try
                FileUtils.Delete(Shot)
            Catch ex As Exception
                Failed += 1
                Logger.Warn(ex, $"删除截图失败：{Shot}")
            End Try
        Next
        SelectedPaths.Clear()
        RefreshShots()
        If Failed = 0 Then
            Hint("截图已删除", HintType.Green, False)
        Else
            Hint($"有 {Failed} 张截图删除失败，可能正被其他程序占用", HintType.Red)
        End If
    End Sub

    ''' <summary>底部多选条的显隐与计数。</summary>
    Private Sub RefreshSelectBar()
        If CardSelect Is Nothing Then Return
        Dim Count As Integer = SelectedPaths.Count
        If Count > 0 Then LabSelect.Text = $"已选择 {Count} 项"
        CardSelect.Visibility = If(Count > 0, Visibility.Visible, Visibility.Collapsed)
        CardList.Margin = New Thickness(0, 0, 0, If(Count > 0, 95, 15))
    End Sub

#End Region

#Region "行内操作"

    Private Function BuildShotRow(Shot As String) As FrameworkElement
        Dim Name As String = PathUtils.GetLastPart(Shot)
        Dim Item As New MyLocalModItem With {
            .Title = Name,
            .Description = "正在读取……",
            .ItemIsEnabled = True,
            .Checked = SelectedPaths.Contains(Shot),
            .LogoSource = "",
            .ToolTip = Shot
        }
        '缩略图放后台做：原图是 1920×1080 的 PNG，列表里全尺寸解码会拖住界面
        RunInThread(
        Sub()
            Dim Thumb As String = GetThumbnail(Shot)
            If Thumb <> "" Then RunInUi(Sub() Item.LogoSource = Thumb)
        End Sub)

        Dim IconButtons As New List(Of MyIconButton)
        Dim BtnEdit As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonEdit, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "用画图编辑"}
        Dim BtnInfo As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonInfo, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "截图信息"}
        Dim BtnOpen As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "打开所在文件夹"}
        Dim BtnDelete As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "删除截图"}
        Dim Favorited As Boolean = ModFavorite.IsScreenshotFavorite(Shot)
        Dim BtnFav As New MyIconButton With {
            .Logo = LogoStarPath, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Opacity = If(Favorited, 1.0, 0.5),
            .ToolTip = If(Favorited, "取消收藏", "收藏这张截图")}
        IconButtons.Add(BtnEdit)
        IconButtons.Add(BtnInfo)
        IconButtons.Add(BtnOpen)
        IconButtons.Add(BtnDelete)
        IconButtons.Add(BtnFav)
        Item.Buttons = IconButtons

        '尺寸与大小放后台算，别卡界面
        RunInThread(
        Sub()
            Dim Text As String = "—"
            Try
                Dim Info As New IO.FileInfo(Shot)
                Dim SizeText As String = FormatSize(Info.Length)
                Dim DimText As String = ""
                Try
                    Using Fs As IO.FileStream = IO.File.OpenRead(Shot)
                        Dim Frame As BitmapFrame = BitmapFrame.Create(Fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
                        DimText = $"{Frame.PixelWidth}×{Frame.PixelHeight}"
                    End Using
                Catch ex As Exception
                    Logger.Warn(ex, $"读取截图尺寸失败：{Shot}")
                End Try
                Text = If(DimText = "", SizeText, DimText & " · " & SizeText) &
                       " · " & Info.LastWriteTime.ToString("MM-dd HH:mm")
            Catch ex As Exception
                Logger.Warn(ex, $"读取截图信息失败：{Shot}")
            End Try
            RunInUi(Sub() Item.Description = Text)
        End Sub)

        AddHandler BtnEdit.Click, Sub() EditShot(Shot)
        AddHandler BtnInfo.Click, Sub() ShowShotInfo(Shot)
        AddHandler BtnOpen.Click, Sub() OpenExplorer(Shot)
        AddHandler BtnDelete.Click, Sub() DeleteShot(Shot)
        AddHandler BtnFav.Click, Sub() FavoriteShot(Shot, BtnFav)
        '左键：单击勾选，双击看图。
        'MyLocalModItem 的 Click 事件带 MouseButtonEventArgs，直接看 ClickCount 即可，
        '不必再挂 MouseDoubleClick——那个事件这个控件没有。
        AddHandler Item.Click,
            Sub(sender As Object, e As MouseButtonEventArgs)
                If e.ClickCount >= 2 Then
                    e.Handled = True
                    PreviewShot(Shot)
                    Return
                End If
                ToggleSelect(Shot)
            End Sub
        '右键进预览
        AddHandler Item.MouseRightButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                e.Handled = True
                PreviewShot(Shot)
            End Sub
        Return Item
    End Function

    ''' <summary>
    ''' 生成或复用缩略图，返回缓存路径。列表里显示原图会全尺寸解码，
    ''' 一屏几十张时能把界面卡住好几秒，所以统一降到 320 宽再显示。
    ''' </summary>
    Private Shared Function GetThumbnail(Shot As String) As String
        Try
            Dim Info As New IO.FileInfo(Shot)
            Dim Dir As String = PathTemp & "ShotThumbs\"
            DirectoryUtils.Create(Dir)
            Dim Name As String = Shot.GetStableHashCode().ConvertRadix(36) & ".png"
            Dim Target As String = Dir & Name
            '原图没变就直接用缓存
            If FileUtils.Exists(Target) Then
                Dim Cached As New IO.FileInfo(Target)
                If Cached.LastWriteTime >= Info.LastWriteTime Then Return Target
            End If
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.DecodePixelWidth = 320
            Bmp.UriSource = New Uri(Shot)
            Bmp.EndInit()
            Bmp.Freeze()
            Dim Encoder As New PngBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(Bmp))
            Using Fs As New IO.FileStream(Target, IO.FileMode.Create, IO.FileAccess.Write)
                Encoder.Save(Fs)
            End Using
            Return Target
        Catch ex As Exception
            Logger.Warn(ex, $"生成截图缩略图失败：{Shot}")
            Return ""
        End Try
    End Function

    Private Shared Function FormatSize(Bytes As Long) As String
        If Bytes < 1024 Then Return Bytes & " B"
        If Bytes < 1024 * 1024 Then Return Math.Round(Bytes / 1024, 1) & " KB"
        Return Math.Round(Bytes / 1024 / 1024, 1) & " MB"
    End Function

    ''' <summary>双击或右键：全屏预览。</summary>
    Private Sub PreviewShot(Shot As String)
        Try
            Dim Window As New WindowImagePreview(Shot)
            Window.ShowDialog()
        Catch ex As Exception
            Logger.Error(ex, "打开截图预览失败", LogBehavior.Alert)
        End Try
    End Sub

    ''' <summary>用系统自带的画图打开。</summary>
    Private Sub EditShot(Shot As String)
        Try
            StartProcess("mspaint.exe", """" & Shot & """")
        Catch ex As Exception
            Logger.Error(ex, "打开画图失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub DeleteShot(Shot As String)
        Dim Name As String = PathUtils.GetLastPart(Shot)
        If MyMsgBox("确定要删除截图「" & Name & "」吗？" & vbCrLf &
                    "此操作不可撤销。", "删除截图", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            FileUtils.Delete(Shot)
            Hint("截图已删除", HintType.Green, False)
            RefreshShots()
        Catch ex As Exception
            Logger.Error(ex, "删除截图失败", LogBehavior.Alert)
        End Try
    End Sub

    ''' <summary>截图信息：尺寸、大小、时间与完整路径。</summary>
    Private Sub ShowShotInfo(Shot As String)
        RunInThread(
        Sub()
            Dim Lines As New List(Of String)
            Try
                Dim Info As New IO.FileInfo(Shot)
                Lines.Add("文件名：" & Info.Name)
                Try
                    Using Fs As IO.FileStream = IO.File.OpenRead(Shot)
                        Dim Frame As BitmapFrame = BitmapFrame.Create(Fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
                        Lines.Add($"尺寸：{Frame.PixelWidth} × {Frame.PixelHeight}")
                    End Using
                Catch ex As Exception
                    Lines.Add("尺寸：读取失败")
                End Try
                Lines.Add("大小：" & FormatSize(Info.Length))
                Lines.Add("修改时间：" & Info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))
                Lines.Add("位置：" & Shot)
            Catch ex As Exception
                Logger.Warn(ex, $"读取截图信息失败：{Shot}")
            End Try
            RunInUi(Sub() MyMsgBox(String.Join(vbCrLf, Lines), "截图信息", "确定"))
        End Sub)
    End Sub

    ''' <summary>
    ''' 收藏 / 取消收藏。已收藏时再点一次就取消。
    ''' 取消只删收藏记录，不动已经复制出去的那份文件。
    ''' </summary>
    Private Sub FavoriteShot(Shot As String, Button As MyIconButton)
        Try
            '按原图路径找，不能用副本路径——副本名字每次都不一样
            Dim Existing As FavoriteEntry = ModFavorite.FindScreenshotFavorite(Shot)
            If Existing IsNot Nothing Then
                ModFavorite.Remove(Existing)
                '副本是收藏时复制出去的，取消收藏就该一起清掉，
                '否则下次收藏又会因为「目标已存在」而生成 (1)(2)
                Dim Copy As String = Existing.Key.Replace("screenshot:", "")
                If Copy <> "" AndAlso Copy <> Shot AndAlso FileUtils.Exists(Copy) Then
                    Try
                        FileUtils.Delete(Copy)
                    Catch ex As Exception
                        Logger.Warn(ex, $"删除收藏副本失败：{Copy}")
                    End Try
                End If
                UpdateFavButton(Button, False)
                Hint("已取消收藏", HintType.Blue, False)
                Return
            End If

            Dim Mode As Integer = Settings.Get(Of Integer)("DstScreenshotFavMode")
            Dim Target As String = Shot
            Dim Name As String = PathUtils.GetLastPart(Shot)
            Dim Description As String = ""
            Try
                Dim Info As New IO.FileInfo(Shot)
                Description = FormatSize(Info.Length) & " · " & Info.LastWriteTime.ToString("MM-dd HH:mm")
            Catch ex As Exception
                Logger.Warn(ex, "读取截图信息失败")
            End Try

            If Mode = 1 OrElse Mode = 2 Then
                Dim Folder As String = If(Mode = 1,
                    Paths.Base & "PCL\ScreenshotFavorites\",
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) & "\DPCL\")
                DirectoryUtils.Create(Folder)
                Target = UniquePath(Folder & Name)
                FileUtils.Copy(Shot, Target)
                Logger.Info($"截图已收藏并复制到：{Target}")
            End If

            If ModFavorite.AddScreenshot(Target, Name, Description, Shot) Then
                UpdateFavButton(Button, True)
                Hint(If(Mode = 0, "已收藏（仅记录路径）", "已收藏"), HintType.Green, False)
            Else
                Hint("这张截图已经收藏过了", HintType.Blue, False)
            End If
        Catch ex As Exception
            Logger.Error(ex, "收藏截图失败", LogBehavior.Alert)
        End Try
    End Sub

    ''' <summary>收藏状态变化后刷新星星外观。</summary>
    Private Shared Sub UpdateFavButton(Button As MyIconButton, Favorited As Boolean)
        If Button Is Nothing Then Return
        Button.Opacity = If(Favorited, 1.0, 0.5)
        Button.ToolTip = If(Favorited, "取消收藏", "收藏这张截图")
    End Sub

    ''' <summary>目标已存在时加 (1)、(2) 后缀，不覆盖已有文件。</summary>
    Private Shared Function UniquePath(Target As String) As String
        If Not FileUtils.Exists(Target) Then Return Target
        'RemoveLastPart 会连同末尾分隔符一起去掉，拼回去时必须自己补一个，
        '否则会拼成「...ScreenshotFavoritesxxx(1).png」，文件散落在 PCL 根目录
        Dim Folder As String = PathUtils.RemoveLastPart(Target) & ""
        Dim Base As String = PathUtils.GetFileNameWithoutExtension(Target)
        Dim Ext As String = IO.Path.GetExtension(Target)
        For i As Integer = 1 To 999
            Dim Candidate As String = Folder & Base & "(" & i & ")" & Ext
            If Not FileUtils.Exists(Candidate) Then Return Candidate
        Next
        Return Folder & Base & "(" & GetUuid() & ")" & Ext
    End Function

    Private Sub OpenShotsFolder()
        Dim Root As String = ShotRoot
        If Root = "" Then
            Hint("请先选择一个版本", HintType.Red)
            Return
        End If
        Try
            DirectoryUtils.Create(Root)
        Catch ex As Exception
            Logger.Warn(ex, "创建截图目录失败")
        End Try
        OpenExplorer(Root)
    End Sub

    Private Sub Filter_Check(sender As Object, raiseByMouse As Boolean) Handles BtnFilterAll.Check, BtnFilterToday.Check,
            BtnFilterWeek.Check, BtnFilterMonth.Check
        If Not raiseByMouse Then Return
        ApplyFilter()
    End Sub

#End Region

    ''' <summary>收藏按钮用的星形图标，与收藏夹同一套。</summary>
    Private Const LogoStarPath As String = "M325.3 810l139.5-73.3c14.8-7.8 31.5-11.9 48.2-11.9 16.7 0 33.4 4.1 48.2 11.9L700.7 810l-26.6-155.1c-5.8-33.6 5.4-67.9 29.8-91.7l112.7-109.8-155.8-22.6c-33.7-4.9-62.9-26.1-78-56.7L513 232.9l-69.8 141.3c-15.1 30.5-44.3 51.6-78 56.6l-155.8 22.6 112.7 109.8c24.5 23.8 35.6 58.1 29.8 91.7L325.3 810m434.2 95.7c-2.3 0-4.7-0.6-6.9-1.7L533.7 789.1c-6.5-3.4-13.6-5.1-20.7-5.1s-14.2 1.7-20.7 5.1L273.4 904c-2.3 1.1-4.6 1.7-6.9 1.7-8.7 0-16.3-7.8-14.7-17.4l41.8-243.4c2.5-14.4-2.3-29.1-12.8-39.3l-177-172.4c-8.8-8.5-4-23.5 8.2-25.3l244.7-35.5c14.5-2.1 27.1-11.2 33.6-24.3l109.4-221.5c2.7-5.5 8-8.3 13.4-8.3 5.3 0 10.6 2.8 13.3 8.3L635.7 348c6.5 13.1 19.1 22.2 33.6 24.3L914 407.8c12.2 1.8 17.1 16.8 8.3 25.3L745.1 605.6c-10.5 10.2-15.3 24.9-12.8 39.3l41.8 243.4c1.6 9.6-6 17.4-14.6 17.4z"

End Class