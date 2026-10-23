Imports System.Windows.Markup

''' <summary>
''' 左侧导航栏按钮：图标在上、文字在下，选中态为浅色圆角块 + 强调色。
''' </summary>
Public Class MyRailButton

    Public Uuid As Integer = GetUuid()
    Public Event Check(sender As Object, raiseByMouse As Boolean)

    Public Property Logo As String
        Get
            Return ShapeLogo.Data.ToString
        End Get
        Set(value As String)
            ShapeLogo.Data = (New GeometryConverter).ConvertFromString(value)
        End Set
    End Property

    Public Property Text As String
        Get
            Return LabText.Text
        End Get
        Set(value As String)
            LabText.Text = value
            System.Windows.Automation.AutomationProperties.SetName(Me, value)
            '文字直接显示在图标下方，不再依赖 ToolTip
            ToolTip = Nothing
            ToolTipService.SetIsEnabled(Me, False)
        End Set
    End Property

    Private _Checked As Boolean = False
    Public Property Checked As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            SetChecked(value, False, False)
        End Set
    End Property

    ''' <summary>
    ''' 设置选中状态。同一父容器内保持单选。
    ''' </summary>
    ''' <param name="value">新的选中状态。</param>
    ''' <param name="raiseByMouse">是否由用户点击引发。</param>
    ''' <param name="anime">是否执行动画（当前直接切换颜色，保留参数以兼容调用方）。</param>
    Public Sub SetChecked(value As Boolean, raiseByMouse As Boolean, Optional anime As Boolean = False)
        Try
            If _Checked = value Then
                RefreshColor()
                Return
            End If
            If value Then
                Dim Pan = TryCast(Parent, Panel)
                If Pan IsNot Nothing Then
                    For Each Child As UIElement In Pan.Children
                        Dim Other = TryCast(Child, MyRailButton)
                        If Other IsNot Nothing AndAlso Other IsNot Me Then Other.SetChecked(False, False)
                    Next
                End If
            End If
            _Checked = value
            RefreshColor()
            If value Then RaiseEvent Check(Me, raiseByMouse)
        Catch ex As Exception
            Logger.Warn(ex, "设置导航按钮选中状态出错")
        End Try
    End Sub

    Private Sub RefreshColor() Handles Me.Loaded, Me.MouseEnter, Me.MouseLeave, Me.IsEnabledChanged
        Try
            If _Checked Then
                '选中：强调色淡底 + 强调色图标
                Background = New SolidColorBrush(Color.FromArgb(44, AccentColor().R, AccentColor().G, AccentColor().B))
                ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrush3")
            ElseIf IsMouseOver Then
                SetResourceReference(BackgroundProperty, "ColorBrushGray7")
                ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrush1")
            Else
                Background = Brushes.Transparent
                ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrushGray3")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新导航按钮颜色出错")
        End Try
    End Sub

    Private Function AccentColor() As Color
        Dim Brush = TryCast(Application.Current.TryFindResource("ColorBrush2"), SolidColorBrush)
        Return If(Brush IsNot Nothing, Brush.Color, Color.FromRgb(&H4C, &H8D, &HFF))
    End Function

    Private Sub RailButton_MouseUp() Handles Me.MouseLeftButtonUp
        If Not IsEnabled Then Return
        If _Checked Then Return
        Logger.Info($"切换导航：{Text}")
        SetChecked(True, True)
    End Sub

End Class
