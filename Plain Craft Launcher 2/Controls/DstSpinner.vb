Imports System.Windows.Media
Imports System.Windows.Media.Animation

''' <summary>
''' 自绘的转圈动画：一段圆弧绕中心旋转。用于"正在探测服务器"的视觉反馈。
''' 不依赖任何图片资源，纯 Path + RotateTransform。
''' </summary>
Public Class DstSpinner
    Inherits System.Windows.Controls.Grid

    Private ReadOnly Arc As New System.Windows.Shapes.Path With {
        .StrokeThickness = 2,
        .StrokeStartLineCap = PenLineCap.Round,
        .StrokeEndLineCap = PenLineCap.Round,
        .Stretch = Stretch.None
    }
    Private ReadOnly Rot As New RotateTransform(0)
    Private Running As Boolean = False

    Public Sub New()
        '圆心在 (6, 6)，半径 5，画一段 270° 的圆弧
        Dim Geometry As New StreamGeometry
        Using Ctx = Geometry.Open()
            Ctx.BeginFigure(New Point(6, 1), False, False)
            Ctx.ArcTo(New Point(6, 11), New Size(5, 5), 0, True, SweepDirection.Clockwise, True, False)
        End Using
        Geometry.Freeze()
        Arc.Data = Geometry
        Arc.RenderTransform = Rot
        Arc.RenderTransformOrigin = New Point(0.5, 0.5)
        '用主题前景色
        Arc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ColorBrush1")
        Children.Add(Arc)
        Start()
    End Sub

    ''' <summary>启动旋转动画（一圈 0.9 秒，无限循环）。</summary>
    Public Sub Start()
        If Running Then Return
        Running = True
        Visibility = Visibility.Visible
        Dim Anim As New DoubleAnimation With {
            .From = 0, .To = 360, .Duration = New Duration(TimeSpan.FromSeconds(0.9)),
            .RepeatBehavior = RepeatBehavior.Forever
        }
        Rot.BeginAnimation(RotateTransform.AngleProperty, Anim)
    End Sub

    ''' <summary>停止动画并隐藏。</summary>
    Public Sub [Stop]()
        Running = False
        Rot.BeginAnimation(RotateTransform.AngleProperty, Nothing)
        Visibility = Visibility.Collapsed
    End Sub

End Class
