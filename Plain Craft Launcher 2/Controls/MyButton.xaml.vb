Imports System.Windows.Markup

<ContentProperty("Inlines")>
Public Class MyButton

    '声明
    Public Event Click(sender As Object, e As MouseButtonEventArgs) '自定义事件

    '自定义属性
    Public Uuid As Integer = GetUuid()
    Public ReadOnly Property Inlines As InlineCollection
        Get
            Return LabText.Inlines
        End Get
    End Property
    Public Property Text As String
        Get
            Return GetValue(TextProperty)
        End Get
        Set(value As String)
            SetValue(TextProperty, value)
        End Set
    End Property '显示文本
    Public Shared ReadOnly TextProperty As DependencyProperty = DependencyProperty.Register("Text", GetType(String), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
    Sub(sender As DependencyObject, e As DependencyPropertyChangedEventArgs)
        If sender IsNot Nothing Then CType(sender, MyButton).LabText.Text = e.NewValue
    End Sub)))
    Public Property TextPadding As Thickness
        Get
            Return LabText.Padding
        End Get
        Set(value As Thickness)
            LabText.Padding = value
        End Set
    End Property
    Private _ColorType As ColorState = ColorState.Normal  '配色方案
    Public Property ColorType As ColorState
        Get
            Return _ColorType
        End Get
        Set(value As ColorState)
            _ColorType = value
            RefreshColor()
        End Set
    End Property
    Public Enum ColorState
        Normal = 0
        Highlight = 1
        Red = 2
    End Enum
    '属性穿透
    Public Shared Shadows ReadOnly PaddingProperty As DependencyProperty = DependencyProperty.Register("Padding", GetType(Thickness), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
        Sub(sender As MyButton, e As DependencyPropertyChangedEventArgs) If sender IsNot Nothing Then sender.PanFore.Padding = e.NewValue)))
    Public Overloads Property Padding As Thickness
        Get
            Return PanFore.Padding
        End Get
        Set(value As Thickness)
            PanFore.Padding = value
        End Set
    End Property
    Public Property RealRenderTransform As Transform
        Get
            Return PanFore.RenderTransform
        End Get
        Set(value As Transform)
            PanFore.RenderTransform = value
        End Set
    End Property

    '自定义事件
    Private Const AnimationColorIn As Integer = 100
    Private Const AnimationColorOut As Integer = 200
    Private Sub RefreshColor(Optional obj = Nothing, Optional e = Nothing) Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded, Me.IsEnabledChanged
        Try
            Dim BackRes As String
            Dim ForeRes As String
            If Not IsEnabled Then
                BackRes = "ColorBrushGray8"
                ForeRes = "ColorBrushGray4"
            Else
                Select Case ColorType
                    Case ColorState.Highlight
                        '主按钮：强调色实心，文字固定纯白（深浅色主题都成立）
                        BackRes = If(IsMouseOver, "ColorBrush3", "ColorBrush2")
                        ForeRes = "ColorBrushWhite"
                    Case ColorState.Red
                        BackRes = If(IsMouseOver, "ColorBrushRedLight", "ColorBrushRedDark")
                        ForeRes = "ColorBrushWhite"
                    Case Else
                        '次按钮：中性面 + 主文字色，保证对比度
                        BackRes = If(IsMouseOver, "ColorBrushGray6", "ColorBrushGray7")
                        ForeRes = "ColorBrush1"
                End Select
            End If
            If IsLoaded AndAlso AniControlEnabled = 0 AndAlso PanFore.Background IsNot Nothing Then
                AniStart({
                    AaColor(PanFore, Border.BackgroundProperty, BackRes, AnimationColorIn),
                    AaColor(PanFore, Border.BorderBrushProperty, ForeRes, AnimationColorIn)
                }, "MyButton Color " & Uuid)
            Else
                AniStop("MyButton Color " & Uuid)
                PanFore.SetResourceReference(Border.BackgroundProperty, BackRes)
                PanFore.SetResourceReference(Border.BorderBrushProperty, ForeRes)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新按钮颜色出错")
        End Try
    End Sub

    '鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
    Private IsMouseDown As Boolean = False
    Private Sub Button_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        Logger.Info($"按下按钮：{Text}")
        RaiseEvent Click(sender, e)
        RaiseCustomEvent() '自定义事件
    End Sub
    Private Sub Button_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        IsMouseDown = True
        Focus()
        AniStart({
                 AaScaleTransform(PanFore, 0.955 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 80,, New AniEaseOutFluent(AniEasePower.ExtraStrong)),
                 AaScaleTransform(PanFore, -0.01, 700,, New AniEaseOutFluent(AniEasePower.Middle))
                 }, "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseEnter() Handles Me.MouseEnter
        RefreshColor()
    End Sub
    Private Sub Button_MouseUp() Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart({
               AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 300, 10, New AniEaseOutFluent(AniEasePower.Middle))
           }, "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseLeave() Handles Me.MouseLeave
        RefreshColor()
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart(AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 800,, New AniEaseOutFluent(AniEasePower.Strong)), "MyButton Scale " & Uuid)
    End Sub

End Class
