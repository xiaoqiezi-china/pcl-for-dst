''' <summary>
''' 收藏夹里的一行。
''' <para>
''' 信息层级与下载页的 <see cref="MyResourceItem"/> 对齐：标题、标签、描述，
''' 底下一行给出「版本 / 下载量 / 最近更新 / 来源」。
''' 但交互不同——下载页点一下进详情，收藏夹点一下是勾选，右键才进详情，
''' 所以不能直接复用那个控件，否则要么勾选坏掉，要么详情页乱跳。
''' </para>
''' <para>
''' 这里只管显示与勾选，具体条目数据由 <see cref="FavoriteListView"/> 填。
''' </para>
''' </summary>
Public Class MyFavoriteItem

    Public Uuid As Integer = GetUuid()

#Region "基础属性"

    Public Property Title As String
        Get
            Return LabTitle.Text
        End Get
        Set(value As String)
            LabTitle.Text = If(value, "")
        End Set
    End Property

    Public Property SubTitle As String
        Get
            Return If(LabSubtitle?.Text, "")
        End Get
        Set(value As String)
            If LabSubtitle Is Nothing Then Return
            LabSubtitle.Text = If(value, "")
            LabSubtitle.Visibility = If(String.IsNullOrEmpty(value), Visibility.Collapsed, Visibility.Visible)
        End Set
    End Property

    Public Property Description As String
        Get
            Return LabInfo.Text
        End Get
        Set(value As String)
            LabInfo.Text = If(value, "")
        End Set
    End Property

    ''' <summary>只有一行文字时（没有项目数据）把下边栏收起来，别留一条空白。</summary>
    Public Property ShowMeta As Boolean
        Get
            Return PanMeta.Visibility = Visibility.Visible
        End Get
        Set(value As Boolean)
            PanMeta.Visibility = If(value, Visibility.Visible, Visibility.Collapsed)
        End Set
    End Property

    Public WriteOnly Property VersionText As String
        Set(value As String)
            LabVersion.Text = If(value, "")
            PathVersion.Visibility = If(String.IsNullOrEmpty(value), Visibility.Collapsed, Visibility.Visible)
        End Set
    End Property

    Public WriteOnly Property DownloadText As String
        Set(value As String)
            LabDownload.Text = If(value, "")
            PathDownload.Visibility = If(String.IsNullOrEmpty(value), Visibility.Collapsed, Visibility.Visible)
        End Set
    End Property

    Public WriteOnly Property UpdateText As String
        Set(value As String)
            LabTime.Text = If(value, "")
            PathTime.Visibility = If(String.IsNullOrEmpty(value), Visibility.Collapsed, Visibility.Visible)
        End Set
    End Property

    Public WriteOnly Property SourceText As String
        Set(value As String)
            LabSource.Text = If(value, "")
            PathSource.Visibility = If(String.IsNullOrEmpty(value), Visibility.Collapsed, Visibility.Visible)
        End Set
    End Property

    Private _LogoSource As String = ""

    ''' <summary>图标路径。没有项目对象时（老收藏）用它兜底。</summary>
    Public Property LogoSource As String
        Get
            Return _LogoSource
        End Get
        Set(value As String)
            _LogoSource = If(value, "")
            PathLogo.Source = If(_LogoSource = "", PathImage & "Icons/NoIcon.png", _LogoSource)
        End Set
    End Property

    Public WriteOnly Property Tags As List(Of String)
        Set(value As List(Of String))
            PanTags.Children.Clear()
            If value Is Nothing OrElse Not value.Any() Then
                PanTags.Visibility = Visibility.Collapsed
                Return
            End If
            PanTags.Visibility = Visibility.Visible
            For Each TagText In value
                Dim BorderTag As New Border With {
                    .Background = New MyColor("#11000000"), .Padding = New Thickness(3, 1, 3, 1),
                    .CornerRadius = New CornerRadius(3), .Margin = New Thickness(0, 0, 3, 0),
                    .SnapsToDevicePixels = True, .UseLayoutRounding = False}
                BorderTag.Child = New TextBlock With {
                    .Text = TagText, .Foreground = New MyColor("#868686"), .FontSize = 11}
                PanTags.Children.Add(BorderTag)
            Next
        End Set
    End Property

#End Region

#Region "勾选"

    ''' <summary>触发点击。收藏夹里点一下就是勾选，不跳详情。</summary>
    Public Event Click(sender As Object, e As MouseButtonEventArgs)

    Private IsMouseDown As Boolean = False
    Private Sub Button_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.PreviewMouseLeftButtonDown
        If IsMouseOver Then IsMouseDown = True
    End Sub
    Private Sub Button_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.PreviewMouseLeftButtonUp
        If Not IsMouseDown Then Return
        IsMouseDown = False
        RaiseEvent Click(Me, e)
    End Sub

    Private _Checked As Boolean = False
    Public Property Checked As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            If value = _Checked Then Return
            _Checked = value
            RefreshChecked(True)
        End Set
    End Property

    Private _RectCheck As Border = Nothing
    ''' <summary>左侧勾选条。第一次用到时才创建，不勾选的行不会有这个元素。</summary>
    Private ReadOnly Property RectCheck As Border
        Get
            If _RectCheck Is Nothing Then
                _RectCheck = New Border With {
                    .Width = 5, .CornerRadius = New CornerRadius(2),
                    .HorizontalAlignment = HorizontalAlignment.Left,
                    .VerticalAlignment = VerticalAlignment.Center,
                    .Margin = New Thickness(-3, 6, 0, 6),
                    .IsHitTestVisible = False}
                _RectCheck.SetResourceReference(Border.BackgroundProperty, "ColorBrush3")
                SetRowSpan(_RectCheck, 10)
                Children.Add(_RectCheck)
            End If
            Return _RectCheck
        End Get
    End Property

    Private Sub RefreshChecked(WithAnimation As Boolean)
        Try
            Dim Bar As Border = RectCheck
            Dim Show As Boolean = _Checked
            Dim Time As Integer = If(WithAnimation AndAlso IsLoaded AndAlso AniControlEnabled = 0, 180, 0)
            If Time = 0 Then
                AniStop("MyFavoriteItem Checked " & Uuid)
                Bar.Height = If(Show, 32, 0)
                Bar.Opacity = If(Show, 1, 0)
            Else
                Dim Target As Double = If(Show, 32 - Bar.ActualHeight, -Bar.ActualHeight)
                AniStart({
                    AaHeight(Bar, Target, Time),
                    AaOpacity(Bar, If(Show, 1 - Bar.Opacity, -Bar.Opacity), Time)
                }, "MyFavoriteItem Checked " & Uuid)
            End If
            LabTitle.SetResourceReference(TextBlock.ForegroundProperty, If(Show, "ColorBrush2", "ColorBrush1"))
        Catch ex As Exception
            Logger.Warn(ex, "刷新收藏夹条目勾选状态失败")
        End Try
    End Sub

#End Region

#Region "悬停按钮"

    Public ButtonStack As FrameworkElement
    Private _Buttons As IEnumerable(Of MyIconButton)
    ''' <summary>右侧悬停时出现的图标按钮。</summary>
    Public Property Buttons As IEnumerable(Of MyIconButton)
        Get
            Return _Buttons
        End Get
        Set(value As IEnumerable(Of MyIconButton))
            _Buttons = value
            If ButtonStack IsNot Nothing Then
                Children.Remove(ButtonStack)
                ButtonStack = Nothing
            End If
            If value Is Nothing OrElse Not value.Any() Then Return
            ButtonStack = New StackPanel With {
                .Opacity = 0, .Margin = New Thickness(0, 0, 5, 0), .Orientation = Orientation.Horizontal,
                .HorizontalAlignment = HorizontalAlignment.Right, .VerticalAlignment = VerticalAlignment.Center,
                .SnapsToDevicePixels = False, .UseLayoutRounding = False}
            SetColumnSpan(ButtonStack, 10) : SetRowSpan(ButtonStack, 10)
            For Each Btn As MyIconButton In value
                If Double.IsNaN(Btn.Height) Then Btn.Height = 25
                If Double.IsNaN(Btn.Width) Then Btn.Width = 25
                CType(ButtonStack, StackPanel).Children.Add(Btn)
            Next
            Children.Add(ButtonStack)
            '没有按钮时右侧只留 4px 呼吸位；有按钮时按数量让出宽度，
            '否则文字会一直顶到按钮底下（不是裁掉，是直接压在一起，很难看）
            ColumnPaddingRight.Width = New GridLength(If(IsMouseOver, ButtonReserveWidth, 4))
        End Set
    End Property

    ''' <summary>悬停时右侧需要让出的宽度：按钮总宽 + 右边距。</summary>
    Private ReadOnly Property ButtonReserveWidth As Double
        Get
            If _Buttons Is Nothing Then Return 4
            Return 5 + _Buttons.Count * 25
        End Get
    End Property

#End Region

#Region "指向背景"

    Private _RectBack As Border = Nothing
    Public ReadOnly Property RectBack As Border
        Get
            If _RectBack Is Nothing Then
                Dim Rect As New Border With {
                    .Name = "RectBack", .CornerRadius = New CornerRadius(3),
                    .RenderTransform = New ScaleTransform(0.8, 0.8),
                    .RenderTransformOrigin = New Point(0.5, 0.5),
                    .BorderThickness = New Thickness(GetWPFSize(1)),
                    .SnapsToDevicePixels = True, .IsHitTestVisible = False, .Opacity = 0}
                Rect.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
                Rect.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6")
                SetColumnSpan(Rect, 999) : SetRowSpan(Rect, 999)
                Children.Insert(0, Rect)
                _RectBack = Rect
            End If
            Return _RectBack
        End Get
    End Property

#End Region

    Private StateLast As String
    Public Sub RefreshColor(sender As Object, e As EventArgs) Handles Me.MouseEnter, Me.MouseLeave
        '按钮跟着一起淡入淡出，鼠标移上去才看得见；
        '右侧同时让出相同宽度，文字才不会钻到按钮底下
        If ButtonStack IsNot Nothing Then
            Dim Target As Double = If(IsMouseOver, 1, 0)
            If Math.Abs(ButtonStack.Opacity - Target) > 0.01 Then
                AniStart(AaOpacity(ButtonStack, Target - ButtonStack.Opacity, 120), "MyFavoriteItem Btn " & Uuid)
            End If
            Dim PadTarget As Double = If(IsMouseOver, ButtonReserveWidth, 4)
            If Math.Abs(ColumnPaddingRight.Width.Value - PadTarget) > 0.5 Then
                AniStart(
                    AaDouble(Sub(i) ColumnPaddingRight.Width = New GridLength(Math.Max(4, ColumnPaddingRight.Width.Value + i)),
                             PadTarget - ColumnPaddingRight.Width.Value, 140),
                    "MyFavoriteItem Pad " & Uuid)
            End If
        End If

        Dim StateNew As String = If(IsMouseOver, "MouseOver", "Idle")
        If StateLast = StateNew Then Return
        StateLast = StateNew
        If IsLoaded AndAlso AniControlEnabled = 0 Then
            Dim Time As Integer = If(IsMouseOver, 120, 180)
            Dim Ani As New List(Of AniData)
            If IsMouseOver Then
                Ani.AddRange({
                    AaColor(RectBack, Border.BackgroundProperty, "ColorBrushBg1", Time),
                    AaOpacity(RectBack, 1 - RectBack.Opacity, Time, , New AniEaseOutFluent),
                    AaScaleTransform(RectBack, 1 - CType(RectBack.RenderTransform, ScaleTransform).ScaleX, Time * 1.2, , New AniEaseOutFluent)
                })
            Else
                Ani.AddRange({
                    AaOpacity(RectBack, -RectBack.Opacity, Time),
                    AaColor(RectBack, Border.BackgroundProperty, "ColorBrush7", Time),
                    AaScaleTransform(RectBack, 0.8 - CType(RectBack.RenderTransform, ScaleTransform).ScaleX, Time, , New AniEaseOutFluent)
                })
            End If
            AniStart(Ani, "MyFavoriteItem Color " & Uuid)
        Else
            AniStop("MyFavoriteItem Color " & Uuid)
            If _RectBack IsNot Nothing Then RectBack.Opacity = 0
        End If
    End Sub

    Public Sub New()
        InitializeComponent()
    End Sub

End Class
