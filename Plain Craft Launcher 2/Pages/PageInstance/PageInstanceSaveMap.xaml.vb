''' <summary>
''' 存档的地图渲染页。
''' 初始只显示一张居中的配置卡片；点开始后卡片淡出，地图从中心向外一圈圈扩散渲染。
''' 渲染完成后左上角出现缩放 / 重置 / 保存三个图标按钮，中央悬浮一个重新配置按钮。
''' 水面按 50% 透明度绘制，透出下方方块。
''' </summary>
Public Class PageInstanceSaveMap

    ''' <summary>当前查看的存档目录（与存档详情共用）。</summary>
    Private ReadOnly Property SaveDir As String
        Get
            If PageInstanceSaveDetail.TargetSave = "" Then Return ""
            Return PathUtils.AddSlashSuffix(PageInstanceSaveDetail.TargetSave)
        End Get
    End Property

    ''' <summary>地图边长（方块数）。</summary>
    Private MapSize As Integer = 1
    ''' <summary>地图中心的方块坐标。</summary>
    Private CenterX As Integer = 0
    Private CenterZ As Integer = 0
    ''' <summary>当前选中的维度 ID（如 minecraft:overworld）。</summary>
    Private Dimension As String = "minecraft:overworld"
    ''' <summary>当前维度对应的区域文件夹。</summary>
    Private CurRegionFolder As String = ""
    ''' <summary>扫到的所有维度。</summary>
    Private Dimensions As New List(Of MapDimension)
    ''' <summary>下拉框加载期间不要触发选中逻辑。</summary>
    Private LoadingDimensions As Boolean = False

    ''' <summary>每个方块列的地表方块 ID，供鼠标悬停查询。渲染线程写，UI 线程读。</summary>
    Private SurfaceIds As String() = Nothing

    ''' <summary>每个像素的地表高度，用于算立体光照。</summary>
    Private HeightsMap As Short() = Nothing
    ''' <summary>每个像素的方块原始颜色（未经光照调制）。</summary>
    Private BaseColors As Integer() = Nothing

    ''' <summary>当前左键点选的方块名（不带命名空间），空串表示没选。</summary>
    Private PickedBlock As String = ""

    ''' <summary>调色板是否被用户关掉了。只在本页存活期间有效，重新进入页面自动恢复。</summary>
    Private PaletteHidden As Boolean = False

    ''' <summary>每格方块的 ARGB 颜色。渲染线程写，UI 线程读。</summary>
    Private Pixels As Integer() = Nothing
    ''' <summary>每个方块距离中心的区块半径，用于波浪式动画。</summary>
    Private RingOf As Integer() = Nothing

    ''' <summary>取消标志。</summary>
    Private Canceled As Boolean = False
    ''' <summary>是否正在渲染。</summary>
    Private Rendering As Boolean = False

    ''' <summary>把方块位置映射到地图像素的缩放倍率。</summary>
    Private ViewScale As Double = 1.0
    ''' <summary>是否已经成功画出过地图。</summary>
    Private HasResult As Boolean = False

    Private CurrentRing As Integer = 0
    Private MaxRing As Integer = 0
    Private LastFlush As Date = Nothing
    Private TotalChunks As Integer = 0
    Private DoneChunks As Integer = 0

    ''' <summary>一次性刷到画布的图片。</summary>
    Private MapImage As Image = Nothing
    ''' <summary>用来快速填像素的位图。</summary>
    Private WriteBitmap As WriteableBitmap = Nothing

    ''' <summary>存档里的所有玩家。</summary>
    Private Players As New List(Of MapPlayer)
    ''' <summary>下拉框加载期间不要触发选中逻辑。</summary>
    Private LoadingPlayers As Boolean = False

    ''' <summary>水的透明度：0.5 表示半透明，透出下面的方块。</summary>
    Private Const WaterAlpha As Integer = 128

    ''' <summary>上一次渲染的存档目录，用于判断是不是换了存档。</summary>
    Private LastSaveDir As String = ""

    ''' <summary>每画几个区块推一次图，让渲染过程能看到地图一圈圈长出来。</summary>
    Private Const ChunkPerFlush As Integer = 9

#Region "页面生命周期"

    Private Sub PageInstanceSaveMap_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '换了存档就要把上一次的地图、配置全部清掉重来
        '每次进入这个页面都把调色板恢复成显示状态
        PaletteHidden = False
        If PanPalette IsNot Nothing Then PanPalette.Visibility = Visibility.Visible
        If LastSaveDir <> SaveDir Then
            ResetForNewSave()
            LastSaveDir = SaveDir
        End If
        If HasResult Then Return
        If SaveDir = "" Then
            LabEmpty.Text = "没有选中的存档"
            BtnStart.IsEnabled = False
            Return
        End If
        '滑条的属性必须等模板应用之后再设，否则模板元素还没生成会抛异常
        SldRadius.MaxValue = 64
        SldRadius.Value = 8
        LoadDimensions()
        UpdateRadiusLabel()
        LoadPlayers()
        AddHandler MapRoot.SizeChanged, Sub() If HasResult Then ApplyTransform()
    End Sub

    ''' <summary>
    ''' 把页面恢复到「只显示配置卡」的初始状态。
    ''' 换存档时必须调，否则会看到上一个存档的地图和工具按钮。
    ''' </summary>
    Private Sub ResetForNewSave()
        Canceled = True
        Rendering = False
        HasResult = False
        Pixels = Nothing
        SurfaceIds = Nothing
        RingOf = Nothing
        MapImage = Nothing
        WriteBitmap = Nothing
        MapSize = 1
        ViewScale = 1.0
        _Pan = New Point(0, 0)
        _DragStart = Nothing
        _SelectStart = Nothing
        If PanCanvas IsNot Nothing Then
            PanCanvas.Children.Clear()
            PanCanvas.RenderTransform = Nothing
        End If
        If PanSelect IsNot Nothing Then PanSelect.Visibility = Visibility.Collapsed
        If PanTools IsNot Nothing Then PanTools.Visibility = Visibility.Collapsed
        If BtnReopen IsNot Nothing Then BtnReopen.Visibility = Visibility.Collapsed
        If PanProgress IsNot Nothing Then PanProgress.Visibility = Visibility.Collapsed
        If LabInfo IsNot Nothing Then LabInfo.Text = ""
        If LabEmpty IsNot Nothing Then
            LabEmpty.Visibility = Visibility.Visible
            LabEmpty.Text = "点下面的按钮渲染这个世界的地图。"
        End If
        '配置卡重新居中显示（并清掉可能残留的淡出动画）
        If CardSetup IsNot Nothing Then
            CardSetup.BeginAnimation(UIElement.OpacityProperty, Nothing)
            CardSetup.Visibility = Visibility.Visible
            CardSetup.Opacity = 1
            CardSetup.IsHitTestVisible = True
        End If
        If BtnStart IsNot Nothing Then BtnStart.IsEnabled = True
    End Sub

    ''' <summary>
    ''' 扫描存档里的所有维度，填进下拉框。
    ''' 整合包的维度可以有很多个，所以不能写死三个单选框。
    ''' </summary>
    Private Sub LoadDimensions()
        Dimensions = DstMapDimensions.Scan(SaveDir)
        LoadingDimensions = True
        CmbDimension.Items.Clear()
        For Each D In Dimensions
            CmbDimension.Items.Add(New MyComboBoxItem With {
                .Content = D.DisplayName & " · " & D.FileCount & " 个区域文件",
                .Tag = D
            })
        Next
        LoadingDimensions = False
        If CmbDimension.Items.Count > 0 Then
            '优先选主世界
            Dim Pick As Integer = 0
            For i = 0 To Dimensions.Count - 1
                If Dimensions(i).Key = "minecraft:overworld" Then
                    Pick = i
                    Exit For
                End If
            Next
            CmbDimension.SelectedIndex = Pick
            ApplyDimension(Dimensions(Pick))
        Else
            CurRegionFolder = ""
            LabStatus.Text = "这个存档还没有任何区域文件"
        End If
    End Sub

    ''' <summary>应用某个维度：记住区域文件夹。</summary>
    Private Sub ApplyDimension(D As MapDimension)
        If D Is Nothing Then Return
        Dimension = D.Key
        CurRegionFolder = D.RegionFolder
    End Sub

    ''' <summary>按维度 ID 选中下拉框里的那一项。</summary>
    Private Sub SelectDimensionById(Id As String)
        For i = 0 To CmbDimension.Items.Count - 1
            Dim Item As MyComboBoxItem = TryCast(CmbDimension.Items(i), MyComboBoxItem)
            If Item Is Nothing Then Continue For
            Dim D As MapDimension = TryCast(Item.Tag, MapDimension)
            If D Is Nothing Then Continue For
            If D.Key = Id Then
                CmbDimension.SelectedIndex = i
                Return
            End If
        Next
    End Sub

    ''' <summary>换维度：区域文件夹跟着换。</summary>
    Private Sub CmbDimension_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmbDimension.SelectionChanged
        If LoadingDimensions Then Return
        Dim Item As MyComboBoxItem = TryCast(CmbDimension.SelectedItem, MyComboBoxItem)
        If Item Is Nothing Then Return
        ApplyDimension(TryCast(Item.Tag, MapDimension))
    End Sub


    Private Sub UpdateRadiusLabel()
        If LabRadius Is Nothing OrElse SldRadius Is Nothing Then Return
        Dim R As Integer = SldRadius.Value
        LabRadius.Text = (R * 2 + 1) & " × " & (R * 2 + 1) & " 区块"
    End Sub

    ''' <summary>
    ''' 扫描存档里的玩家填进下拉框。
    ''' 默认选「不使用玩家」，中心点留在坐标原点。
    ''' </summary>
    Private Sub LoadPlayers()
        Players = DstMapPlayers.Scan(SaveDir)
        LoadingPlayers = True
        CmbPlayer.Items.Clear()
        CmbPlayer.Items.Add(New MyComboBoxItem With {.Content = "不使用玩家（以中心坐标为准）", .Tag = Nothing})
        For Each P In Players
            CmbPlayer.Items.Add(New MyComboBoxItem With {
                .Content = P.Name & " · " & DstMapPlayers.DimensionName(P.Dimension) & " " & P.X & ", " & P.Z,
                .Tag = P
            })
        Next
        CmbPlayer.SelectedIndex = 0
        LoadingPlayers = False
        BoxCenterX.Text = "0"
        BoxCenterZ.Text = "0"
    End Sub

#End Region

#Region "配置交互"

    Private Sub SldRadius_Change(sender As Object, user As Boolean) Handles SldRadius.Change
        UpdateRadiusLabel()
    End Sub

    ''' <summary>选了玩家：维度、坐标一起跟着走；选「不使用玩家」时回到原点。</summary>
    Private Sub CmbPlayer_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmbPlayer.SelectionChanged
        If LoadingPlayers Then Return
        Dim Item As MyComboBoxItem = TryCast(CmbPlayer.SelectedItem, MyComboBoxItem)
        If Item Is Nothing Then Return
        Dim P As MapPlayer = TryCast(Item.Tag, MapPlayer)
        If P Is Nothing Then
            BoxCenterX.Text = "0"
            BoxCenterZ.Text = "0"
            Return
        End If
        SelectDimensionById(DstMapDimensions.KeyToId(P.Dimension))
        BoxCenterX.Text = P.X.ToString()
        BoxCenterZ.Text = P.Z.ToString()
    End Sub

    Private Sub BtnStart_Click(sender As Object, e As EventArgs) Handles BtnStart.Click
        If SaveDir = "" Then
            Hint("没有选中的存档", HintType.Red)
            Return
        End If
        If CurRegionFolder = "" OrElse Not DirectoryUtils.Exists(CurRegionFolder) Then
            Hint("这个维度还没有区域文件，进游戏里走走就会生成。", HintType.Red)
            Return
        End If
        Integer.TryParse(BoxCenterX.Text.Trim(), CenterX)
        Integer.TryParse(BoxCenterZ.Text.Trim(), CenterZ)
        StartRender()
    End Sub

#End Region

#Region "工具按钮"

    Private Sub BtnZoomIn_Click(sender As Object, e As EventArgs) Handles BtnZoomIn.Click
        ZoomBy(1.4)
    End Sub

    Private Sub BtnZoomOut_Click(sender As Object, e As EventArgs) Handles BtnZoomOut.Click
        ZoomBy(1.0 / 1.4)
    End Sub

    Private Sub BtnResetView_Click(sender As Object, e As EventArgs) Handles BtnResetView.Click
        FitToFrame(True)
    End Sub

    ''' <summary>中央悬浮按钮：把配置卡片重新弹出来。</summary>
    Private Sub BtnReopen_Click(sender As Object, e As EventArgs) Handles BtnReopen.Click
        ShowSetupCard()
    End Sub

    ''' <summary>保存渲染图。</summary>
    Private Sub BtnExport_Click(sender As Object, e As EventArgs) Handles BtnExport.Click
        If Not HasResult OrElse WriteBitmap Is Nothing Then
            Hint("还没有渲染结果可以保存", HintType.Red)
            Return
        End If
        Dim WorldName As String = PathUtils.GetLastPart(PathUtils.RemoveSlashSuffix(SaveDir))
        Dim DefaultName As String = WorldName & "-" & DstMapDimensions.FriendlyName(Dimension) & "-" &
                                    Date.Now.ToString("yyyyMMdd-HHmmss") & ".png"
        Dim Target As String = Dialogs.SaveFile("保存渲染图", DefaultName, SaveDir, {("png", "PNG 图片")})
        If String.IsNullOrWhiteSpace(Target) Then Return
        Try
            Dim Encoder As New PngBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(WriteBitmap))
            Using Fs As New FileStream(Target, FileMode.Create, FileAccess.Write)
                Encoder.Save(Fs)
            End Using
            Hint("已保存：" & PathUtils.GetLastPart(Target), HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "保存渲染图失败")
            Hint("保存失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region

#Region "视图变换"

    Private _DragStart As Point? = Nothing
    Private _Pan As Point = New Point(0, 0)

    ''' <summary>左键按下时的位置，用来区分「点击选方块」和「拖动平移」。</summary>
    Private _ClickStart As Point? = Nothing

    Private Sub PanCanvas_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles PanCanvas.MouseLeftButtonDown
        If Not HasResult OrElse _SelectStart IsNot Nothing Then Return
        _DragStart = e.GetPosition(MapRoot)
        _ClickStart = _DragStart
        PanCanvas.CaptureMouse()
    End Sub

    ''' <summary>
    ''' 鼠标指向的方块信息：更新右上角浮层。
    ''' 把屏幕坐标换算回地图像素，再换算成世界方块坐标，顺便给出区块坐标。
    ''' </summary>
    Private Sub UpdatePickInfo(MousePos As Point)
        If Not HasResult OrElse SurfaceIds Is Nothing OrElse MapSize <= 1 Then
            If PanPick IsNot Nothing AndAlso PickedBlock = "" Then PanPick.Visibility = Visibility.Collapsed
            Return
        End If
        '屏幕 → 地图像素
        Dim Px As Integer = CInt(Math.Floor((MousePos.X - _Pan.X) / ViewScale))
        Dim Py As Integer = CInt(Math.Floor((MousePos.Y - _Pan.Y) / ViewScale))
        If Px < 0 OrElse Py < 0 OrElse Px >= MapSize OrElse Py >= MapSize Then
            '鼠标移出地图：没选中方块就收起面板，选中了则保留（方便去点改色按钮）
            If PickedBlock <> "" Then
                ShowPickedOnly()
            ElseIf PanPick IsNot Nothing Then
                PanPick.Visibility = Visibility.Collapsed
            End If
            Return
        End If
        '地图像素 → 世界方块坐标
        Dim BlockX As Integer = CenterX - MaxRing * 16 + Px
        Dim BlockZ As Integer = CenterZ - MaxRing * 16 + Py
        Dim Id As String = SurfaceIds(Py * MapSize + Px)
        Dim IdText As String = If(String.IsNullOrEmpty(Id), "（未读取）", DstNbt.StripNamespace(Id))
        Dim ChunkX As Integer = CInt(Math.Floor(BlockX / 16.0))
        Dim ChunkZ As Integer = CInt(Math.Floor(BlockZ / 16.0))
        If PanPick Is Nothing Then Return
        LabPickCoord.Text = "X " & BlockX & "   Z " & BlockZ
        LabPickBlock.Text = IdText & If(DstMapRender.HasUserColor(IdText), " · 已自定义", "")
        LabPickChunk.Text = "区块 " & ChunkX & ", " & ChunkZ & " · 区段 " & (ChunkX >> 5) & ", " & (ChunkZ >> 5)
        PanPick.Visibility = Visibility.Visible
        If PickedBlock <> "" Then
            PanPickEdit.Visibility = Visibility.Visible
            RefreshPickSwatch()
        Else
            PanPickEdit.Visibility = Visibility.Collapsed
        End If
    End Sub

    ''' <summary>
    ''' 鼠标离开地图但方块已选中时，面板改成只显示选中的那个方块。
    ''' 这样鼠标从地图移到面板上去点「改颜色」时，面板不会消失。
    ''' </summary>
    Private Sub ShowPickedOnly()
        If PanPick Is Nothing Then Return
        LabPickCoord.Text = PickedBlock
        LabPickBlock.Text = If(DstMapRender.HasUserColor(PickedBlock), "已自定义颜色", "使用默认颜色")
        LabPickChunk.Text = "再点一次该方块可取消选中"
        PanPick.Visibility = Visibility.Visible
        PanPickEdit.Visibility = Visibility.Visible
        RefreshPickSwatch()
    End Sub

    ''' <summary>刷新选中方块的色块。</summary>
    Private Sub RefreshPickSwatch()
        If SwatchPick Is Nothing OrElse PickedBlock = "" Then Return
        Dim PickRgb As Integer = DstMapRender.ColorOf(PickedBlock)
        SwatchPick.Background = New SolidColorBrush(Color.FromRgb((PickRgb >> 16) And 255, (PickRgb >> 8) And 255, PickRgb And 255))
        If LabPickHex IsNot Nothing Then LabPickHex.Text = "#" & PickRgb.ToString("X6")
        BuildPalette()
    End Sub

    ''' <summary>
    ''' 调色板用的颜色：30 色，常见地形、植被、建材、16 色里的代表色都有。
    ''' </summary>
    Private ReadOnly PaletteColors As Integer() = {
        &HFFFFFF, &HD9D9D9, &H8C8C8C, &H4D4D4D, &H1A1A1A,
        &H7CBD5B, &H4E8B2E, &H2F5C1C, &H8FA64A, &H3A5627,
        &H866043, &H5B3F26, &H9B7F4B, &HDBCFA3, &H877F7D,
        &H3F5EE8, &H2A5FD8, &H6BA39C, &HA0C4F0, &HF2FBFB,
        &H9A6B5B, &HB9631F, &H7F7F7F, &H63321E, &H150C1D,
        &HE06101, &HF1AF16, &H5EA919, &H64209C, &HA9309B
    }

    ''' <summary>把调色板色块铺到面板里。选中方块时才建一次。</summary>
    Private Sub BuildPalette()
        If PanPalette Is Nothing OrElse PickedBlock = "" Then Return
        '用户手动关掉了调色板就只留着色值和恢复按钮
        If PaletteHidden Then
            PanPalette.Visibility = Visibility.Collapsed
            Return
        End If
        PanPalette.Visibility = Visibility.Visible
        '已经有同样数量的色块就不重建
        If PanPalette.Children.Count = PaletteColors.Length Then Return
        PanPalette.Children.Clear()
        Dim Cur As Integer = DstMapRender.ColorOf(PickedBlock)
        For Each PalRgb In PaletteColors
            Dim Box As New Border With {
                .Width = 20, .Height = 20, .CornerRadius = New CornerRadius(3),
                .Margin = New Thickness(0, 0, 3, 3), .Cursor = Cursors.Hand,
                .Background = New SolidColorBrush(Color.FromRgb((PalRgb >> 16) And 255, (PalRgb >> 8) And 255, PalRgb And 255)),
                .BorderThickness = New Thickness(1)
            }
            '当前颜色那一格描白边
            Box.BorderBrush = New SolidColorBrush(If(PalRgb = Cur, Colors.White, Color.FromArgb(40, 255, 255, 255)))
            Box.ToolTip = "#" & PalRgb.ToString("X6")
            Dim Clr As Integer = PalRgb
            AddHandler Box.MouseLeftButtonUp,
                Sub()
                    If PickedBlock = "" Then Return
                    DstMapRender.SetUserColor(PickedBlock, Clr)
                    If HasResult Then StartRender()
                End Sub
            PanPalette.Children.Add(Box)
        Next
    End Sub

    ''' <summary>关掉调色板。只影响本次进入页面的显示，重新进页面会恢复。</summary>
    Private Sub BtnPaletteClose_Click(sender As Object, e As EventArgs) Handles BtnPaletteClose.Click
        PaletteHidden = True
        If PanPalette IsNot Nothing Then PanPalette.Visibility = Visibility.Collapsed
    End Sub
    ''' <summary>恢复选中方块的默认颜色。</summary>
    Private Sub BtnPickReset_Click(sender As Object, e As EventArgs) Handles BtnPickReset.Click
        If PickedBlock = "" Then Return
        If Not DstMapRender.HasUserColor(PickedBlock) Then
            Hint("这个方块用的就是默认颜色", HintType.Blue)
            Return
        End If
        DstMapRender.SetUserColor(PickedBlock, Nothing)
        Hint("已恢复 " & PickedBlock & " 的默认颜色", HintType.Green)
        If HasResult Then StartRender()
    End Sub

    ''' <summary>
    ''' 鼠标进入右上角面板：面板要一直显示着，不能被 PanCanvas 的 MouseLeave 收掉。
    ''' </summary>
    Private Sub PanPick_MouseEnter(sender As Object, e As MouseEventArgs)
        If PickedBlock <> "" Then ShowPickedOnly()
    End Sub

    Private Sub PanCanvas_MouseLeave(sender As Object, e As MouseEventArgs) Handles PanCanvas.MouseLeave
        '选中方块后不隐藏，否则鼠标去点「改颜色」时面板会消失
        If PickedBlock <> "" Then
            ShowPickedOnly()
        Else
            If PanPick IsNot Nothing Then PanPick.Visibility = Visibility.Collapsed
        End If
    End Sub

    Private Sub PanCanvas_MouseMove(sender As Object, e As MouseEventArgs) Handles PanCanvas.MouseMove
        '框选优先
        If _SelectStart IsNot Nothing Then
            Dim NowSel As Point = e.GetPosition(MapRoot)
            Canvas.SetLeft(RectSelect, Math.Min(_SelectStart.Value.X, NowSel.X))
            Canvas.SetTop(RectSelect, Math.Min(_SelectStart.Value.Y, NowSel.Y))
            RectSelect.Width = Math.Abs(NowSel.X - _SelectStart.Value.X)
            RectSelect.Height = Math.Abs(NowSel.Y - _SelectStart.Value.Y)
            Return
        End If
        If _DragStart Is Nothing Then
            UpdatePickInfo(e.GetPosition(MapRoot))
            Return
        End If
        Dim Now As Point = e.GetPosition(MapRoot)
        _Pan = New Point(_Pan.X + (Now.X - _DragStart.Value.X), _Pan.Y + (Now.Y - _DragStart.Value.Y))
        _DragStart = Now
        ApplyTransform()
        UpdatePickInfo(Now)
    End Sub

    Private Sub PanCanvas_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles PanCanvas.MouseLeftButtonUp
        Dim WasClick As Boolean = False
        If _ClickStart IsNot Nothing AndAlso _DragStart IsNot Nothing Then
            '位移小于 4 像素当成点击，不当作拖动
            WasClick = Math.Abs(_DragStart.Value.X - _ClickStart.Value.X) < 4 AndAlso
                       Math.Abs(_DragStart.Value.Y - _ClickStart.Value.Y) < 4
        End If
        _DragStart = Nothing
        _ClickStart = Nothing
        PanCanvas.ReleaseMouseCapture()
        If WasClick Then PickBlockAt(e.GetPosition(MapRoot))
    End Sub

    ''' <summary>
    ''' 左键点了一个方块：记住它，右上角浮层出现改色按钮。
    ''' 再点一次同一个方块则取消选中。
    ''' </summary>
    Private Sub PickBlockAt(MousePos As Point)
        If Not HasResult OrElse SurfaceIds Is Nothing OrElse MapSize <= 1 Then Return
        Dim Px As Integer = CInt(Math.Floor((MousePos.X - _Pan.X) / ViewScale))
        Dim Py As Integer = CInt(Math.Floor((MousePos.Y - _Pan.Y) / ViewScale))
        If Px < 0 OrElse Py < 0 OrElse Px >= MapSize OrElse Py >= MapSize Then
            PickedBlock = ""
            UpdatePickInfo(MousePos)
            Return
        End If
        Dim Id As String = SurfaceIds(Py * MapSize + Px)
        If String.IsNullOrEmpty(Id) Then
            PickedBlock = ""
            UpdatePickInfo(MousePos)
            Return
        End If
        Dim NewBlock As String = DstNbt.StripNamespace(Id)
        '同一方块再点一次 = 取消选中
        If PickedBlock = NewBlock Then PickedBlock = "" Else PickedBlock = NewBlock
        UpdatePickInfo(MousePos)
    End Sub




    Private Sub PanCanvas_MouseWheel(sender As Object, e As MouseWheelEventArgs) Handles PanCanvas.MouseWheel
        If Not HasResult Then Return
        ZoomBy(If(e.Delta > 0, 1.15, 1.0 / 1.15), e.GetPosition(MapRoot))
        e.Handled = True
    End Sub

    Private Sub ZoomBy(Factor As Double, Optional Anchor As Point? = Nothing)
        If Not HasResult Then Return
        Dim Old As Double = ViewScale
        ViewScale = Math.Max(0.02, Math.Min(40, ViewScale * Factor))
        If Anchor.HasValue Then
            _Pan = New Point(
                Anchor.Value.X - (Anchor.Value.X - _Pan.X) * (ViewScale / Old),
                Anchor.Value.Y - (Anchor.Value.Y - _Pan.Y) * (ViewScale / Old))
        End If
        ApplyTransform()
    End Sub

#End Region

#Region "右键框选补渲染"

    Private _SelectStart As Point? = Nothing
    Private SelectChunkFromX As Integer = 0
    Private SelectChunkFromZ As Integer = 0
    Private SelectChunkToX As Integer = 0
    Private SelectChunkToZ As Integer = 0

    Private Sub PanCanvas_MouseRightButtonDown(sender As Object, e As MouseButtonEventArgs) Handles PanCanvas.MouseRightButtonDown
        If Not HasResult OrElse Rendering Then Return
        _SelectStart = e.GetPosition(MapRoot)
        RectSelect.Width = 0
        RectSelect.Height = 0
        Canvas.SetLeft(RectSelect, _SelectStart.Value.X)
        Canvas.SetTop(RectSelect, _SelectStart.Value.Y)
        PanSelect.Visibility = Visibility.Visible
        PanCanvas.CaptureMouse()
        e.Handled = True
    End Sub

    Private Sub PanCanvas_MouseRightButtonUp(sender As Object, e As MouseButtonEventArgs) Handles PanCanvas.MouseRightButtonUp
        If _SelectStart Is Nothing Then Return
        PanCanvas.ReleaseMouseCapture()
        PanSelect.Visibility = Visibility.Collapsed
        Dim EndPoint As Point = e.GetPosition(MapRoot)
        Dim StartPoint As Point = _SelectStart.Value
        _SelectStart = Nothing
        If Not ScreenRectToChunks(StartPoint, EndPoint) Then
            Hint("框选范围太小，再拉大一点", HintType.Red)
            Return
        End If
        Dim W As Integer = SelectChunkToX - SelectChunkFromX + 1
        Dim H As Integer = SelectChunkToZ - SelectChunkFromZ + 1
        If W * H > 4096 Then
            Hint("一次最多补渲染 4096 个区块（当前 " & (W * H) & " 个）", HintType.Red)
            Return
        End If
        Dim Answer As Integer = MyMsgBox(
            "在 " & DstMapDimensions.FriendlyName(Dimension) & " 补渲染 " & W & " × " & H & " 个区块？" & vbCrLf &
            "区块范围 X " & SelectChunkFromX & " ~ " & SelectChunkToX & "，Z " & SelectChunkFromZ & " ~ " & SelectChunkToZ,
            "二次渲染", "开始", "取消")
        If Answer <> 1 Then Return
        StartPatchRender()
    End Sub

    ''' <summary>屏幕坐标 → 世界方块坐标。</summary>
    Private Function ScreenToBlock(P As Point) As Point
        Dim Bx As Double = (P.X - _Pan.X) / ViewScale + (CenterX - MaxRing * 16)
        Dim Bz As Double = (P.Y - _Pan.Y) / ViewScale + (CenterZ - MaxRing * 16)
        Return New Point(Bx, Bz)
    End Function

    Private Function ScreenRectToChunks(A As Point, B As Point) As Boolean
        Dim P1 As Point = ScreenToBlock(A)
        Dim P2 As Point = ScreenToBlock(B)
        Dim Cx1 As Integer = CInt(Math.Floor(Math.Min(P1.X, P2.X) / 16.0))
        Dim Cx2 As Integer = CInt(Math.Floor(Math.Max(P1.X, P2.X) / 16.0))
        Dim Cz1 As Integer = CInt(Math.Floor(Math.Min(P1.Y, P2.Y) / 16.0))
        Dim Cz2 As Integer = CInt(Math.Floor(Math.Max(P1.Y, P2.Y) / 16.0))
        If Cx1 = Cx2 AndAlso Cz1 = Cz2 Then Return False
        SelectChunkFromX = Cx1
        SelectChunkToX = Cx2
        SelectChunkFromZ = Cz1
        SelectChunkToZ = Cz2
        Return True
    End Function

#End Region

#Region "渲染流程"

    ''' <summary>卡片淡出 → 开始渲染。</summary>
    Private Sub StartRender()
        Dim R As Integer = SldRadius.Value
        MaxRing = R
        MapSize = (R * 2 + 1) * 16
        Pixels = New Integer(MapSize * MapSize - 1) {}
        SurfaceIds = New String(MapSize * MapSize - 1) {}
        BaseColors = New Integer(MapSize * MapSize - 1) {}
        HeightsMap = New Short(MapSize * MapSize - 1) {}
        For i = 0 To HeightsMap.Length - 1
            HeightsMap(i) = -999
        Next
        RingOf = New Integer(MapSize * MapSize - 1) {}
        For Zz As Integer = 0 To MapSize - 1
            For Xx As Integer = 0 To MapSize - 1
                Dim Bx As Integer = CenterX - R * 16 + Xx
                Dim Bz As Integer = CenterZ - R * 16 + Zz
                Dim Cx As Integer = Int(Math.Floor(Bx / 16.0)) - Int(Math.Floor(CenterX / 16.0))
                Dim Cz As Integer = Int(Math.Floor(Bz / 16.0)) - Int(Math.Floor(CenterZ / 16.0))
                RingOf(Zz * MapSize + Xx) = Math.Max(Math.Abs(Cx), Math.Abs(Cz))
            Next
        Next
        For i = 0 To Pixels.Length - 1
            '未读取到的位置用深灰，与真正的地表拉开一点距离，不要是一片透明
            Pixels(i) = &HFF2E2E30
        Next
        HasResult = True
        Canceled = False
        Rendering = True
        CurrentRing = 0
        DoneChunks = 0
        TotalChunks = (R * 2 + 1) * (R * 2 + 1)
        LastFlush = Date.MinValue

        '卡片淡出、地图区接管
        HideSetupCard()
        LabEmpty.Visibility = Visibility.Collapsed
        PanProgress.Visibility = Visibility.Visible
        LabStatus.Text = "正在读取区块…"
        BarFill.Width = 0
        LabInfo.Text = ""
        PanCanvas.Children.Clear()
        MapImage = Nothing
        WriteBitmap = Nothing
        PanCanvas.RenderTransform = Nothing
        _Pan = New Point(0, 0)

        Dim RegionFolder As String = If(CurRegionFolder <> "", CurRegionFolder, DstMapRender.RegionFolderOf(SaveDir, Dimension))
        Logger.Info("[地图] 开始渲染：目录=" & RegionFolder & " 中心=" & CenterX & "," & CenterZ & " 半径=" & R & " 尺寸=" & MapSize)
        RunInThread(Sub() RenderWorker(RegionFolder))
    End Sub

    ''' <summary>把配置卡片收起来（带淡出）。</summary>
    Private Sub HideSetupCard()
        PanTools.Visibility = Visibility.Collapsed
        BtnReopen.Visibility = Visibility.Collapsed
        CardSetup.IsHitTestVisible = False
        Dim Fade As New Animation.DoubleAnimation(1, 0, New Duration(TimeSpan.FromMilliseconds(180)))
        AddHandler Fade.Completed,
            Sub()
                CardSetup.Visibility = Visibility.Collapsed
                CardSetup.Opacity = 1
            End Sub
        CardSetup.BeginAnimation(UIElement.OpacityProperty, Fade)
    End Sub

    ''' <summary>把配置卡片重新弹出来。</summary>
    Private Sub ShowSetupCard()
        If Rendering Then Return
        PanTools.Visibility = Visibility.Collapsed
        BtnReopen.Visibility = Visibility.Collapsed
        CardSetup.Visibility = Visibility.Visible
        CardSetup.IsHitTestVisible = True
        CardSetup.Opacity = 0
        Dim Fade As New Animation.DoubleAnimation(0, 1, New Duration(TimeSpan.FromMilliseconds(200)))
        CardSetup.BeginAnimation(UIElement.OpacityProperty, Fade)
    End Sub

    ''' <summary>渲染完成后显示工具组。</summary>
    Private Sub ShowTools()
        PanProgress.Visibility = Visibility.Collapsed
        PanTools.Visibility = Visibility.Visible
        BtnReopen.Visibility = Visibility.Visible
        '工具组和中央按钮都淡入
        For Each El As FrameworkElement In {PanTools, BtnReopen}
            El.Opacity = 0
            El.BeginAnimation(UIElement.OpacityProperty,
                New Animation.DoubleAnimation(0, 1, New Duration(TimeSpan.FromMilliseconds(260))))
        Next
    End Sub

    Private Sub RenderWorker(RegionFolder As String)
        Try
            Dim CenterChunkX As Integer = Int(Math.Floor(CenterX / 16.0))
            Dim CenterChunkZ As Integer = Int(Math.Floor(CenterZ / 16.0))
            For Ring As Integer = 0 To MaxRing
                If Canceled Then Exit For
                CurrentRing = Ring
                Dim Targets As New List(Of Point)
                For Cz As Integer = -Ring To Ring
                    For Cx As Integer = -Ring To Ring
                        If Math.Max(Math.Abs(Cx), Math.Abs(Cz)) <> Ring Then Continue For
                        Targets.Add(New Point(Cx, Cz))
                    Next
                Next
                '这一圈里的区块按「先中心后边缘」排序，让扩散更自然
                Targets.Sort(Function(A, B) Math.Abs(A.X) + Math.Abs(A.Y) < Math.Abs(B.X) + Math.Abs(B.Y))
                For Each T In Targets
                    If Canceled Then Exit For
                    PaintedChunk(RegionFolder, CenterChunkX + CInt(T.X), CenterChunkZ + CInt(T.Y))
                    DoneChunks += 1
                    '每画几个区块就推一次，让地图边读边长
                    If DoneChunks Mod ChunkPerFlush = 0 Then FlushToUi()
                Next
                FlushToUi()
            Next
        Finally
            RunInUi(
            Sub()
                Rendering = False
                If Canceled Then
                    LabStatus.Text = "已取消"
                    PanProgress.Visibility = Visibility.Collapsed
                End If
                FinishAnimation()
            End Sub)
        End Try
    End Sub

    ''' <summary>补渲染：只读框选的那一片区块，读到的像素直接叠进现有地图。</summary>
    Private Sub StartPatchRender()
        If Not HasResult Then Return
        Canceled = False
        Rendering = True
        DoneChunks = 0
        TotalChunks = (SelectChunkToX - SelectChunkFromX + 1) * (SelectChunkToZ - SelectChunkFromZ + 1)
        PanProgress.Visibility = Visibility.Visible
        BarFill.Width = 0
        LabStatus.Text = "正在补渲染 " & TotalChunks & " 个区块…"
        PanTools.Visibility = Visibility.Collapsed
        BtnReopen.Visibility = Visibility.Collapsed
        Dim RegionFolder As String = If(CurRegionFolder <> "", CurRegionFolder, DstMapRender.RegionFolderOf(SaveDir, Dimension))
        RunInThread(
        Sub()
            Try
                For Cz As Integer = SelectChunkFromZ To SelectChunkToZ
                    If Canceled Then Exit For
                    For Cx As Integer = SelectChunkFromX To SelectChunkToX
                        If Canceled Then Exit For
                        PaintedChunk(RegionFolder, Cx, Cz)
                        DoneChunks += 1
                    Next
                Next
                FlushToUi()
            Finally
                RunInUi(
                Sub()
                    Rendering = False
                    PanProgress.Visibility = Visibility.Collapsed
                    PanTools.Visibility = Visibility.Visible
                    BtnReopen.Visibility = Visibility.Visible
                    If Not Canceled Then LabInfo.Text = MapSize & " × " & MapSize & " 格 · " & TotalChunks & " 区块"
                End Sub)
            End Try
        End Sub)
    End Sub

    ''' <summary>把一个区块的 256 列地表涂到像素表里；水面半透明。</summary>
    Private Sub PaintedChunk(RegionFolder As String, ChunkX As Integer, ChunkZ As Integer)
        Dim RegionX As Integer = ChunkX >> 5
        Dim RegionZ As Integer = ChunkZ >> 5
        Dim LocalX As Integer = ChunkX And 31
        Dim LocalZ As Integer = ChunkZ And 31
        Dim McaPath As String = RegionFolder & "r." & RegionX & "." & RegionZ & ".mca"
        If Not File.Exists(McaPath) Then
            Return
        End If
        Dim Slot As Integer = LocalZ * 32 + LocalX
        Dim ChunkRoot As NbtTag = Nothing
        Try
            ChunkRoot = DstMapRender.ReadChunkFromMca(McaPath, Slot)
        Catch ex As Exception
            Logger.Info("读取区块失败：" & ChunkX & "," & ChunkZ & "：" & ex.Message)
            Return
        End Try
        If ChunkRoot Is Nothing Then
            Return
        End If
        Dim Under As String() = Nothing
        Dim Heights As Short() = Nothing
        Dim Surface As String() = DstMapRender.ReadChunkSurface(ChunkRoot, Under, Heights)
        Dim Left As Integer = ChunkX * 16 - (CenterX - MaxRing * 16)
        Dim Top As Integer = ChunkZ * 16 - (CenterZ - MaxRing * 16)
        For Zz As Integer = 0 To 15
            Dim Py As Integer = Top + Zz
            If Py < 0 OrElse Py >= MapSize Then Continue For
            For Xx As Integer = 0 To 15
                Dim Px As Integer = Left + Xx
                If Px < 0 OrElse Px >= MapSize Then Continue For
                Dim Idx As Integer = Zz * 16 + Xx
                Dim Id As String = Surface(Idx)
                If Id = "" Then Continue For
                Dim Color As Integer = DstMapRender.ColorOf(Id)
                Dim Arg As Integer = &HFF
                '记下高度和原始色，光照在 ApplyHillshade 里统一算（需要邻域）
                HeightsMap(Py * MapSize + Px) = Heights(Idx)
                BaseColors(Py * MapSize + Px) = Color
                '水：统一 50% 透明度。不管水下有没有方块都保持半透明，
                '有海底就用海底颜色混一半，没有就用纯水色半透明画。
                If DstMapRender.IsWater(Id) Then
                    Arg = WaterAlpha
                    Dim Below As String = Under(Idx)
                    If Below <> "" Then
                        Color = Blend(Color, DstMapRender.ColorOf(Below), WaterAlpha)
                    End If
                End If
                Pixels(Py * MapSize + Px) = (Arg << 24) Or Color
                SurfaceIds(Py * MapSize + Px) = Id
            Next
        Next
    End Sub

    ''' <summary>
    ''' 给整张图算一遍立体光照，结果写回 Pixels。
    ''' 思路借鉴 Minecraft 的方块光照：每个像素根据它和左右上下邻居的高度差算一个坡度，
    ''' 再用一个从左上方照过来的方向光求明暗。这样树冠、山脊、河谷都会各自出层次，
    ''' 而不是单纯按海拔数值整体变亮变暗。
    ''' </summary>
    Private Sub ApplyHillshade()
        If HeightsMap Is Nothing OrElse BaseColors Is Nothing OrElse MapSize <= 1 Then Return
        '光源方向：从左上方来，高度角较高（俯视为主）
        Const LightX As Double = -0.55
        Const LightY As Double = -0.55
        Const LightZ As Double = 0.63
        '高度差到坡度的系数：相邻 1 格高度差 1 就相当于坡度 1
        Const SlopeScale As Double = 0.85
        '环境光占的比重：越大整体越平
        Const Ambient As Double = 0.5
        '光照后的明暗范围
        Const MinFactor As Double = 0.68
        Const MaxFactor As Double = 1.32
        For Py = 0 To MapSize - 1
            For Px = 0 To MapSize - 1
                Dim Idx As Integer = Py * MapSize + Px
                Dim BaseColor As Integer = BaseColors(Idx)
                If BaseColors(Idx) = 0 AndAlso SurfaceIds(Idx) = Nothing Then Continue For
                If String.IsNullOrEmpty(SurfaceIds(Idx)) Then Continue For
                Dim H As Integer = HeightsMap(Idx)
                If H <= -900 Then
                    '没有高度数据，直接用品色
                    Pixels(Idx) = (Pixels(Idx) And &HFF000000) Or BaseColor
                    Continue For
                End If
                '取四周邻居的高度差，算地形坡度
                Dim HL As Integer = HeightAt(Px - 1, Py, H)
                Dim HR As Integer = HeightAt(Px + 1, Py, H)
                Dim HU As Integer = HeightAt(Px, Py - 1, H)
                Dim HD As Integer = HeightAt(Px, Py + 1, H)
                Dim DX As Double = (HR - HL) * SlopeScale
                Dim DY As Double = (HD - HU) * SlopeScale
                '法线 = 归一化(-dX, -dY, 1)
                Dim Len As Double = Math.Sqrt(DX * DX + DY * DY + 1.0)
                Dim NX As Double = -DX / Len
                Dim NY As Double = -DY / Len
                Dim NZ As Double = 1.0 / Len
                '方向光点乘
                Dim Dot As Double = NX * LightX + NY * LightY + NZ * LightZ
                If Dot < 0 Then Dot = 0
                Dim F As Double = Ambient + (1.0 - Ambient) * Dot
                If F < MinFactor Then F = MinFactor
                If F > MaxFactor Then F = MaxFactor
                '海拔基调：高处略亮，低处略暗，叠在光照上
                F *= 1.0 + Math.Max(-0.18, Math.Min(0.18, (H - 63) / 96.0))
                Dim Arg As Integer = Pixels(Idx) And &HFF000000
                Pixels(Idx) = Arg Or ScaleColor(BaseColor, F)
            Next
        Next
    End Sub

    ''' <summary>取某个像素的高度，越界或没数据就用当前高度代替（相当于平地）。</summary>
    Private Function HeightAt(Px As Integer, Py As Integer, Fallback As Integer) As Integer
        If Px < 0 OrElse Py < 0 OrElse Px >= MapSize OrElse Py >= MapSize Then Return Fallback
        Dim H As Integer = HeightsMap(Py * MapSize + Px)
        Return If(H <= -900, Fallback, H)
    End Function

    ''' <summary>按系数调整颜色，各通道裁剪到 0..255。</summary>
    Private Function ScaleColor(BaseColor As Integer, F As Double) As Integer
        Dim R As Integer = CInt(((BaseColor >> 16) And &HFF) * F)
        Dim G As Integer = CInt(((BaseColor >> 8) And &HFF) * F)
        Dim B As Integer = CInt((BaseColor And &HFF) * F)
        If R > 255 Then R = 255
        If R < 0 Then R = 0
        If G > 255 Then G = 255
        If G < 0 Then G = 0
        If B > 255 Then B = 255
        If B < 0 Then B = 0
        Return (R << 16) Or (G << 8) Or B
    End Function
    ''' <summary>按 Alpha（0..255）把上层颜色叠到下层上。</summary>
    Private Function Blend(TopColor As Integer, BottomColor As Integer, Alpha As Integer) As Integer
        Dim A As Double = Alpha / 255.0
        Dim R As Integer = CInt(((TopColor >> 16) And &HFF) * A + ((BottomColor >> 16) And &HFF) * (1 - A))
        Dim G As Integer = CInt(((TopColor >> 8) And &HFF) * A + ((BottomColor >> 8) And &HFF) * (1 - A))
        Dim B As Integer = CInt((TopColor And &HFF) * A + (BottomColor And &HFF) * (1 - A))
        Return (R << 16) Or (G << 8) Or B
    End Function

    Private Sub FlushToUi()
        If (Date.Now - LastFlush).TotalMilliseconds < 16 Then Return
        LastFlush = Date.Now
        Dim Snapshot As Integer() = DirectCast(Pixels.Clone(), Integer())
        Dim CurRing As Integer = CurrentRing
        Dim Done As Integer = DoneChunks
        Dim Total As Integer = TotalChunks
        RunInUi(
        Sub()
            If Not HasResult Then Return
            EnsureBitmap()
            PushPixels(Snapshot, CurRing)
            Dim Pct As Double = If(Total = 0, 1.0, Done / Total)
            LabStatus.Text = "正在读取区块 " & Done & " / " & Total
            BarFill.Width = Math.Max(0, 360 * Pct)
            If MapImage.RenderTransform Is Nothing Then FitToFrame(False)
        End Sub)
    End Sub

    ''' <summary>把像素推进位图，未到当前圈的方块先不显示。</summary>
    Private Sub PushPixels(Snapshot As Integer(), CurRing As Integer)
        If WriteBitmap Is Nothing Then Return
        Dim Rect As New Int32Rect(0, 0, MapSize, MapSize)
        Dim Rows As Integer() = New Integer(Snapshot.Length - 1) {}
        For i = 0 To Snapshot.Length - 1
            If RingOf(i) > CurRing + 2 Then
                Rows(i) = &H00000000
            Else
                Rows(i) = Snapshot(i)
            End If
        Next
        WriteBitmap.WritePixels(Rect, Rows, MapSize * 4, 0)
    End Sub

    Private Sub EnsureBitmap()
        If WriteBitmap IsNot Nothing AndAlso WriteBitmap.PixelWidth = MapSize Then Return
        WriteBitmap = New WriteableBitmap(MapSize, MapSize, 96, 96, PixelFormats.Bgra32, Nothing)
        If MapImage Is Nothing Then
            MapImage = New Image With {.Stretch = Stretch.Fill, .Width = MapSize, .Height = MapSize}
            RenderOptions.SetBitmapScalingMode(MapImage, BitmapScalingMode.NearestNeighbor)
            PanCanvas.Children.Add(MapImage)
        End If
        MapImage.Source = WriteBitmap
        MapImage.Width = MapSize
        MapImage.Height = MapSize
    End Sub

    ''' <summary>渲染完成：缩放弹回 + 显示工具组。</summary>
    Private Sub FinishAnimation()
        If MapImage Is Nothing Then Return
        '整张图画完了，统一算一遍立体光照
        ApplyHillshade()
        If WriteBitmap IsNot Nothing AndAlso Pixels IsNot Nothing Then
            PushPixels(DirectCast(Pixels.Clone(), Integer()), MaxRing)
        End If
        FitToFrame(False)
        Dim BaseScale As Double = ViewScale
        Dim Scale As New ScaleTransform(BaseScale * 0.86, BaseScale * 0.86)
        Dim Move As New TranslateTransform(_Pan.X, _Pan.Y)
        Dim Trans As New TransformGroup
        Trans.Children.Add(Scale)
        Trans.Children.Add(Move)
        MapImage.RenderTransform = Trans
        MapImage.RenderTransformOrigin = New Point(0.5, 0.5)
        Dim Anim As New Animation.DoubleAnimation(BaseScale * 0.86, BaseScale,
                                                  New Duration(TimeSpan.FromSeconds(1))) With {
            .EasingFunction = New Animation.QuadraticEase With {.EasingMode = Animation.EasingMode.EaseOut}
        }
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim)
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim)
        Dim Timer As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1.05)}
        AddHandler Timer.Tick,
            Sub()
                Timer.Stop()
                ApplyTransform()
            End Sub
        Timer.Start()
        Dim Fade As New Animation.DoubleAnimation(0.7, 1.0, New Duration(TimeSpan.FromSeconds(0.9)))
        MapImage.BeginAnimation(UIElement.OpacityProperty, Fade)
        LabInfo.Text = MapSize & " × " & MapSize & " 格 · " & TotalChunks & " 区块"
        ShowTools()
    End Sub

    ''' <summary>把地图缩放平移成刚好铺满可视区。</summary>
    Private Sub FitToFrame(Animate As Boolean)
        If MapSize <= 0 Then Return
        Dim W As Double = MapRoot.ActualWidth
        Dim H As Double = MapRoot.ActualHeight
        If W < 10 OrElse H < 10 Then Return
        ViewScale = Math.Min(W / MapSize, H / MapSize) * 0.94
        _Pan = New Point((W - MapSize * ViewScale) / 2, (H - MapSize * ViewScale) / 2)
        ApplyTransform()
    End Sub

    Private Sub ApplyTransform()
        If MapImage Is Nothing Then Return
        Dim Trans As New TransformGroup
        Trans.Children.Add(New ScaleTransform(ViewScale, ViewScale))
        Trans.Children.Add(New TranslateTransform(_Pan.X, _Pan.Y))
        MapImage.RenderTransform = Trans
        MapImage.RenderTransformOrigin = New Point(0, 0)
    End Sub

    ''' <summary>公开的重绘入口，给侧边栏刷新用。</summary>
    Public Sub Refresh()
        If HasResult AndAlso Pixels IsNot Nothing Then
            PushPixels(DirectCast(Pixels.Clone(), Integer()), MaxRing)
        End If
    End Sub

#End Region

End Class
