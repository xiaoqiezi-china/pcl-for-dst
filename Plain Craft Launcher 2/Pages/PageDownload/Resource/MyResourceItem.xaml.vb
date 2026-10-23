Public Class MyResourceItem

#Region "基础属性"
    Public Uuid As Integer = GetUuid()

    '标题
    Public Property Title As String
        Get
            Return LabTitle.Text
        End Get
        Set(value As String)
            If LabTitle.Text = value Then Return
            LabTitle.Text = value
        End Set
    End Property

    '副标题
    Public Property SubTitle As String
        Get
            Return If(LabTitleRaw?.Text, "")
        End Get
        Set(value As String)
            If LabTitleRaw.Text = value Then Return
            LabTitleRaw.Text = value
            LabTitleRaw.Visibility = If(value = "", Visibility.Collapsed, Visibility.Visible)
        End Set
    End Property

    '描述
    Public Property Description As String
        Get
            Return LabInfo.Text
        End Get
        Set(value As String)
            If LabInfo.Text = value Then Return
            LabInfo.Text = value
        End Set
    End Property
    '指向时扩展描述
    Private Sub LabInfo_MouseEnter(sender As Object, e As MouseEventArgs) Handles LabInfo.MouseEnter
        ToolTipInfo.Content = LabInfo.Text
        LabInfo.ToolTip = If(IsTextTrimmed(LabInfo), ToolTipInfo, Nothing)
    End Sub

    'Tag
    Public WriteOnly Property Tags As List(Of String)
        Set(Tags As List(Of String))
            PanTags.Children.Clear()
            PanTags.Visibility = If(Tags.Any(), Visibility.Visible, Visibility.Collapsed)
            For Each TagText In Tags
                Dim BorderTag As New Border With {
                    .Background = New MyColor("#11000000"), .Padding = New Thickness(3, 1, 3, 1), .CornerRadius = New CornerRadius(3),
                    .Margin = New Thickness(0, 0, 3, 0), .SnapsToDevicePixels = True, .UseLayoutRounding = False}
                Dim LabTag As New TextBlock With {
                    .Text = TagText, .Foreground = New MyColor("#868686"), .FontSize = 11}
                BorderTag.Child = LabTag
                PanTags.Children.Add(BorderTag)
            Next
        End Set
    End Property

#End Region

#Region "点击"

    '触发点击事件
    Public Event Click(sender As Object, e As MouseButtonEventArgs)
    Private Sub Button_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.PreviewMouseLeftButtonUp
        If PanStar IsNot Nothing AndAlso PanStar.IsMouseOver Then Return '点的是收藏星，不跳详情
        If IsMouseDown Then
            RaiseEvent Click(sender, e)
            If e.Handled Then Return
            Logger.Info($"按下资源工程列表项：{LabTitle.Text}")
        End If
    End Sub
    Private Sub MyResourceItem_Click(sender As MyResourceItem, e As EventArgs) Handles Me.Click
        '记录当前展开的卡片标题（#2712）
        Dim Titles As New List(Of String)
        If FrmMain.PageCurrent.Page = FormMain.PageType.ResourceDetail Then
            For Each Card As MyCard In FrmDownloadResourceDetail.PanResults.Children
                If Card.Title <> "" AndAlso Not Card.IsSwapped Then Titles.Add(Card.Title)
            Next
            Logger.Info($"记录当前已展开的卡片：{String.Join("、", Titles)}")
        End If
        '打开详情页
        Dim TargetType As ResourceTypes
        Dim TargetInstance As String = Nothing
        Dim TargetLoader As ModLoaders = ModLoaders.None
        If FrmMain.PageCurrent.Page = FormMain.PageType.Download Then
            '从下载页进入
            Select Case FrmMain.PageCurrentSub
                Case FormMain.PageSubType.DownloadMod
                    TargetType = ResourceTypes.Mod
                    TargetInstance = FrmDownloadMod.Content.Loader.Input.GameVersion
                    TargetLoader = FrmDownloadMod.Content.Loader.Input.ModLoaders
                Case FormMain.PageSubType.DownloadPack
                    TargetType = ResourceTypes.ModPack
                    TargetInstance = FrmDownloadPack.Content.Loader.Input.GameVersion
                Case FormMain.PageSubType.DownloadDataPack
                    TargetType = ResourceTypes.DataPack
                    TargetInstance = FrmDownloadDataPack.Content.Loader.Input.GameVersion
                Case FormMain.PageSubType.DownloadResourcePack
                    TargetType = ResourceTypes.ResourcePack
                    TargetInstance = FrmDownloadResourcePack.Content.Loader.Input.GameVersion
                Case FormMain.PageSubType.DownloadShader
                    TargetType = ResourceTypes.Shader
                    TargetInstance = FrmDownloadShader.Content.Loader.Input.GameVersion
            End Select
        Else
            '从详情页进入（查看前置）
            TargetType = ResourceTypes.Any '允许任意类别
            TargetInstance = FrmMain.PageCurrent.Additional(2)
            TargetLoader = FrmMain.PageCurrent.Additional(3)
        End If
        FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.ResourceDetail,
                           .Additional = {sender.Tag, Titles, TargetInstance, TargetLoader, TargetType}})
    End Sub

    '收藏
    Public Event StarClick(sender As Object, e As MouseButtonEventArgs)
    Private _IsFavorite As Boolean = False
    ''' <summary>是否已收藏。设置时会带一个变黄的动画。</summary>
    Public Property IsFavorite As Boolean
        Get
            Return _IsFavorite
        End Get
        Set(value As Boolean)
            If _IsFavorite = value Then Return
            _IsFavorite = value
            RefreshStar(True)
        End Set
    End Property

    ''' <summary>刷新星星外观；WithAnimation 为 True 时做一个“变黄 + 弹一下”的动画。</summary>
    Public Sub RefreshStar(Optional WithAnimation As Boolean = False)
        If PathStar Is Nothing Then Return
        '收藏色分深浅两套：纯金 #FFCB2B 在白卡片上对比度只有 1.5，实心星几乎看不见，浅色下换成深琥珀
        Dim Yellow As MyColor = If(DstDarkMode, New MyColor("#FFCB2B"), New MyColor("#C97E00"))
        '每次都把缩放复位，否则动画增量会一轮轮累加，星星越点越大
        PathStar.RenderTransformOrigin = New Point(0.5, 0.5)
        PathStar.RenderTransform = New ScaleTransform(1, 1)
        AniStop("Star " & Uuid)
        If _IsFavorite Then
            If WithAnimation AndAlso AniControlEnabled = 0 Then
                'AaColor 的 MyColor 重载收的是「增量」而不是目标色：直接传目标色会在当前灰上叠加，
                '溢出截断成 (255,255,209) 这种近乎白色的淡黄，必须传 目标色 - 当前色
                Dim Delta As MyColor = Yellow - New MyColor(PathStar.Fill)
                AniStart({
                    AaColor(PathStar, Shapes.Shape.FillProperty, Delta, 220),
                    AaScaleTransform(PathStar, 0.25, 130,, New AniEaseOutFluent(AniEasePower.Strong)),
                    AaScaleTransform(PathStar, -0.25, 300,, New AniEaseOutFluent)
                }, "Star " & Uuid)
            Else
                PathStar.Fill = Yellow
            End If
            ToolTip = "已收藏"
        Else
            '未收藏的灰星同理：#A6A6A6 在白底上对比度只有 2.4，浅色下深一档才看得清轮廓
            Dim GrayName As String = If(DstDarkMode, "ColorBrushGray4", "ColorBrushGray3")
            If WithAnimation AndAlso AniControlEnabled = 0 Then
                AniStart(AaColor(PathStar, Shapes.Shape.FillProperty, GrayName, 160), "Star " & Uuid)
            Else
                PathStar.SetResourceReference(Shapes.Shape.FillProperty, GrayName)
            End If
            ToolTip = "收藏"
        End If
    End Sub

    Private Sub PanStar_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles PanStar.MouseLeftButtonUp
        e.Handled = True
        RaiseEvent StarClick(Me, e)
    End Sub

    '鼠标点击判定
    Private IsMouseDown As Boolean = False
    Private Sub Button_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.PreviewMouseLeftButtonDown
        If IsMouseOver AndAlso CanInteraction Then IsMouseDown = True
    End Sub
    Private Sub Button_MouseLeave(sender As Object, e As Object) Handles Me.MouseLeave, Me.PreviewMouseLeftButtonUp
        IsMouseDown = False
    End Sub

#End Region

#Region "后加载指向背景"

    Private _RectBack As Border = Nothing
    Public ReadOnly Property RectBack As Border
        Get
            If _RectBack Is Nothing Then
                Dim Rect As New Border With {
                    .Name = "RectBack",
                    .CornerRadius = New CornerRadius(3),
                    .RenderTransform = New ScaleTransform(0.8, 0.8),
                    .RenderTransformOrigin = New Point(0.5, 0.5),
                    .BorderThickness = New Thickness(GetWPFSize(1)),
                    .SnapsToDevicePixels = True,
                    .IsHitTestVisible = False,
                    .Opacity = 0
                }
                Rect.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
                Rect.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6")
                SetColumnSpan(Rect, 999)
                SetRowSpan(Rect, 999)
                Children.Insert(0, Rect)
                _RectBack = Rect
                '<!--<Border x:Name = "RectBack" CornerRadius="3" RenderTransformOrigin="0.5,0.5" SnapsToDevicePixels="True" 
                'IsHitTestVisible = "False" Opacity="0" BorderThickness="1" 
                'Grid.ColumnSpan = "4" Background="{DynamicResource ColorBrush7}" BorderBrush="{DynamicResource ColorBrush6}"/>-->
            End If
            Return _RectBack
        End Get
    End Property

#End Region

    Private StateLast As String
    ''' <summary>
    ''' 是否允许交互。目前仅用于 PageDownloadResourceDetail 的顶部栏展示：若关闭碰撞检测，则无法展开 Tooltip。
    ''' </summary>
    Public Property CanInteraction As Boolean = True
    Public Sub RefreshColor(sender As Object, e As EventArgs) Handles Me.MouseEnter, Me.MouseLeave, Me.MouseLeftButtonDown, Me.MouseLeftButtonUp
        If Not CanInteraction Then Return
        '判断当前颜色
        Dim StateNew As String, Time As Integer
        If IsMouseOver Then
            If IsMouseDown Then
                StateNew = "MouseDown"
                Time = 120
            Else
                StateNew = "MouseOver"
                Time = 120
            End If
        Else
            StateNew = "Idle"
            Time = 180
        End If
        If StateLast = StateNew Then Return
        StateLast = StateNew
        '触发颜色动画
        If IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画
            '有动画
            Dim Ani As New List(Of AniData)
            If IsMouseOver Then
                Ani.AddRange({
                    AaColor(RectBack, Border.BackgroundProperty, If(IsMouseDown, "ColorBrush6", "ColorBrushBg1"), Time),
                    AaOpacity(RectBack, 1 - RectBack.Opacity, Time,, New AniEaseOutFluent)
                })
                If IsMouseDown Then
                    Ani.Add(AaScaleTransform(RectBack, 0.996 - CType(RectBack.RenderTransform, ScaleTransform).ScaleX, Time * 1.2,, New AniEaseOutFluent))
                Else
                    Ani.Add(AaScaleTransform(RectBack, 1 - CType(RectBack.RenderTransform, ScaleTransform).ScaleX, Time * 1.2,, New AniEaseOutFluent))
                End If
            Else
                Ani.AddRange({
                    AaOpacity(RectBack, -RectBack.Opacity, Time),
                    AaColor(RectBack, Border.BackgroundProperty, If(IsMouseDown, "ColorBrush6", "ColorBrush7"), Time),
                    AaScaleTransform(RectBack, 0.996 - CType(RectBack.RenderTransform, ScaleTransform).ScaleX, Time,, New AniEaseOutFluent),
                    AaScaleTransform(RectBack, -0.196, 1,,, True)
                })
            End If
            AniStart(Ani, "MyResourceItem Color " & Uuid)
        Else
            '无动画
            AniStop("MyResourceItem Color " & Uuid)
            If _RectBack IsNot Nothing Then RectBack.Opacity = 0
        End If
    End Sub

End Class
