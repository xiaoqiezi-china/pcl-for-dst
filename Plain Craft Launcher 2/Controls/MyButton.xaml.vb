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

    ''' <summary>
    ''' 按皮肤度量设置圆角与描边粗细。
    ''' 在构造时调用一次；切换皮肤后页面会重建，因此不需要监听变化。
    ''' </summary>
    Private SkinApplied As Boolean = False

    ''' <summary>
    ''' 按皮肤度量设置圆角、描边粗细，并套用实心/描边外观。
    ''' 控件载入时调用一次；切换皮肤后由 SkinManager 遍历视觉树再推一次。
    ''' </summary>
    Public Sub ApplySkinMetrics()
        PanFore.CornerRadius = New CornerRadius(SkinMetrics.RadiusButton)
        PanFore.BorderThickness = New Thickness(SkinMetrics.ButtonBorderThickness)
        ApplyFilledStyle()
    End Sub

    ''' <summary>
    ''' 按皮肤设置决定按钮是实心填充还是描边。
    ''' 每次 RefreshColor 都要重算：ColorType 可能在控件载入之后才被赋值，
    ''' 而实心模式下不同 ColorType 的填充色并不相同。
    ''' </summary>
    Private Sub ApplyFilledStyle()
        '可空：Nothing 表示皮肤没设置，此时保持内置的描边观感
        If SkinMetrics.ButtonFilled.GetValueOrDefault(False) = False Then
            '描边模式：恢复 XAML 里的默认底色。文字颜色绑定在 BorderBrush 上，无需处理。
            PanFore.Background = New SolidColorBrush(Color.FromArgb(26, 255, 255, 255))
            BindingOperations.ClearBinding(LabText, TextBlock.ForegroundProperty)
            LabText.SetBinding(TextBlock.ForegroundProperty,
                New Data.Binding("BorderBrush") With {.Source = PanFore, .Mode = Data.BindingMode.OneWay})
            Return
        End If
        '实心模式：用填充色承载状态，文字一律反白（或跟随主题前景）
        BindingOperations.ClearBinding(LabText, TextBlock.ForegroundProperty)
        Select Case ColorType
            Case ColorState.Highlight
                PanFore.Background = New SolidColorBrush(Color2)
                LabText.Foreground = New SolidColorBrush(Color.FromRgb(255, 255, 255))
            Case ColorState.Red
                PanFore.Background = New SolidColorBrush(Color.FromRgb(229, 72, 77))
                LabText.Foreground = New SolidColorBrush(Color.FromRgb(255, 255, 255))
            Case Else
                PanFore.Background = New SolidColorBrush(If(DstDarkMode,
                    Color.FromRgb(42, 42, 50), Color.FromRgb(245, 245, 245)))
                LabText.Foreground = New SolidColorBrush(Color1)
        End Select
    End Sub

    '自定义事件
    Private Const AnimationColorIn As Integer = 100
    Private Const AnimationColorOut As Integer = 200
    Private Sub RefreshColor(Optional obj = Nothing, Optional e = Nothing) Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded, Me.IsEnabledChanged
        '载入时套用一次皮肤度量（圆角/描边）。之后页面重建才会再读，符合“切皮肤后整页刷新”的约定
        If Not SkinApplied Then
            SkinApplied = True
            ApplySkinMetrics()
        End If
        Try
            ApplyFilledStyle()
            If IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画

                If IsEnabled Then
                    Select Case ColorType
                        Case ColorState.Normal
                            If IsMouseOver Then
                                '指向（Main 3）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush3", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '普通（Main 1）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush1", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                        Case ColorState.Highlight
                            If IsMouseOver Then
                                '指向（Main 3）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush3", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '高亮（Main 2）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush2", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                        Case ColorState.Red
                            If IsMouseOver Then
                                '红色指向
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrushRedLight", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '红色
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrushRedDark", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                    End Select
                Else
                    '不可用（Gray 4）
                    AniStart({AaColor(PanFore, Border.BorderBrushProperty, ColorGray4 - PanFore.BorderBrush, AnimationColorOut)}, "MyButton Color " & Uuid)
                End If
            Else

                AniStop("MyButton Color " & Uuid)
                If IsEnabled Then
                    Select Case ColorType
                        Case ColorState.Normal
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush3")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush1")
                            End If
                        Case ColorState.Highlight
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush3")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush2")
                            End If
                        Case ColorState.Red
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedLight")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedDark")
                            End If
                    End Select
                Else
                    PanFore.BorderBrush = ColorGray4
                End If

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
        AniStart(AaColor(PanFore, BackgroundProperty, If(_ColorType = ColorState.Red, "ColorBrushRedBack", "ColorBrush7"), AnimationColorIn), "MyButton Background " & Uuid)
    End Sub
    Private Sub Button_MouseUp() Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart({
               AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 300, 10, New AniEaseOutFluent(AniEasePower.Middle))
           }, "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseLeave() Handles Me.MouseLeave
        AniStart(AaColor(PanFore, BackgroundProperty, "ColorBrushHalfWhite", AnimationColorOut), "MyButton Background " & Uuid)
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart(AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 800,, New AniEaseOutFluent(AniEasePower.Strong)), "MyButton Scale " & Uuid)
    End Sub

End Class
