Imports System.Windows.Media.Imaging

''' <summary>
''' 图片放大预览。滚轮缩放、按住拖动、双击复位、点击空白或 Esc 关闭。
''' </summary>
Public Class WindowImagePreview
    Inherits Window

    Private ReadOnly Img As New Image
    Private ReadOnly ScaleTr As New ScaleTransform(1, 1)
    Private ReadOnly TransTr As New TranslateTransform(0, 0)
    Private ReadOnly Hint As New TextBlock

    Private Dragging As Boolean
    Private DragStart As Point
    Private DragOrigin As Point
    Private Moved As Boolean

    Public Sub New(Url As String)
        '窗口本身
        WindowStyle = WindowStyle.None
        AllowsTransparency = True
        Background = New SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
        ShowInTaskbar = False
        ResizeMode = ResizeMode.NoResize
        WindowStartupLocation = WindowStartupLocation.CenterScreen
        Topmost = True
        If FrmMain IsNot Nothing Then Owner = FrmMain
        Width = Math.Min(SystemParameters.PrimaryScreenWidth * 0.86, 1400)
        Height = Math.Min(SystemParameters.PrimaryScreenHeight * 0.86, 900)

        '遮罩
        Dim Back As New Border With {
            .Background = New SolidColorBrush(Color.FromArgb(232, 8, 12, 18)),
            .CornerRadius = New CornerRadius(10)
        }

        '图片
        Img.Stretch = Stretch.Uniform
        Img.RenderTransformOrigin = New Point(0.5, 0.5)
        Dim TrGroup As New TransformGroup
        TrGroup.Children.Add(ScaleTr)
        TrGroup.Children.Add(TransTr)
        Img.RenderTransform = TrGroup
        Img.Cursor = Cursors.Hand
        Img.Margin = New Thickness(24, 24, 24, 44)

        '提示
        Hint.Text = "滚轮缩放 · 按住拖动 · 双击复位 · 点击空白或按 Esc 关闭"
        Hint.FontSize = 12
        Hint.Foreground = New SolidColorBrush(Color.FromArgb(170, 255, 255, 255))
        Hint.HorizontalAlignment = HorizontalAlignment.Center
        Hint.VerticalAlignment = VerticalAlignment.Bottom
        Hint.Margin = New Thickness(0, 0, 0, 16)
        Hint.IsHitTestVisible = False

        Dim Root As New Grid
        Root.Children.Add(Back)
        Root.Children.Add(Img)
        Root.Children.Add(Hint)
        Content = Root

        '加载图片
        Try
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.UriSource = New Uri(Url, UriKind.Absolute)
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.DecodePixelWidth = 1600
            Bmp.EndInit()
            Img.Source = Bmp
        Catch ex As Exception
            Hint.Text = "图片加载失败：" & ex.Message
        End Try

        '交互
        AddHandler Me.KeyDown, Sub(s As Object, e As KeyEventArgs)
                                   If e.Key = Key.Escape Then Close()
                               End Sub
        AddHandler Img.MouseWheel, Sub(s As Object, e As MouseWheelEventArgs)
                                       Dim Old As Double = ScaleTr.ScaleX
                                       Dim Factor As Double = If(e.Delta > 0, 1.15, 1 / 1.15)
                                       Dim NewScale As Double = Math.Max(1, Math.Min(6, Old * Factor))
                                       ScaleTr.ScaleX = NewScale
                                       ScaleTr.ScaleY = NewScale
                                       If NewScale <= 1.001 Then
                                           TransTr.X = 0
                                           TransTr.Y = 0
                                       End If
                                   End Sub
        AddHandler Img.MouseLeftButtonDown, Sub(s As Object, e As MouseButtonEventArgs)
                                                Dragging = True
                                                Moved = False
                                                DragStart = e.GetPosition(Me)
                                                DragOrigin = New Point(TransTr.X, TransTr.Y)
                                                Img.CaptureMouse()
                                            End Sub
        AddHandler Img.MouseMove, Sub(s As Object, e As MouseEventArgs)
                                      If Not Dragging Then Return
                                      Dim NowPoint As Point = e.GetPosition(Me)
                                      Dim Dx As Double = NowPoint.X - DragStart.X
                                      Dim Dy As Double = NowPoint.Y - DragStart.Y
                                      If Math.Abs(Dx) + Math.Abs(Dy) > 4 Then Moved = True
                                      If ScaleTr.ScaleX > 1.001 Then
                                          TransTr.X = DragOrigin.X + Dx
                                          TransTr.Y = DragOrigin.Y + Dy
                                      End If
                                  End Sub
        AddHandler Img.MouseLeftButtonUp, Sub(s As Object, e As MouseButtonEventArgs)
                                              If Dragging Then
                                                  Dragging = False
                                                  Img.ReleaseMouseCapture()
                                                  If Not Moved AndAlso ScaleTr.ScaleX <= 1.001 Then Close()
                                              End If
                                          End Sub
        AddHandler Img.MouseRightButtonUp, Sub(s As Object, e As MouseButtonEventArgs)
                                               Close()
                                           End Sub
        AddHandler Img.MouseDown, Sub(s As Object, e As MouseButtonEventArgs)
                                      If e.ClickCount = 2 Then
                                          '双击复位缩放
                                          ScaleTr.ScaleX = 1
                                          ScaleTr.ScaleY = 1
                                          TransTr.X = 0
                                          TransTr.Y = 0
                                      End If
                                  End Sub
        AddHandler Me.MouseLeftButtonUp, Sub(s As Object, e As MouseButtonEventArgs)
                                             If e.OriginalSource Is Back Then Close()
                                         End Sub
    End Sub

End Class
