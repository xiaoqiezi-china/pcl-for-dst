Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

''' <summary>
''' 皮肤的界面扩展：用 JSON 描述控件树，控件上的动作名交给 <see cref="SkinEvents"/> 执行。
''' <para>
''' 为什么不用 XAML：皮肤包是从外部导入的，XAML 能触发任意类型与资源引用，
''' 安全边界很难收紧。JSON 只允许一组固定控件类型与固定属性，
''' 表达力足够做布局，又不可能被用来执行任意代码。
''' </para>
''' </summary>
Public Module SkinControls

    ''' <summary>上一次构建的错误说明。</summary>
    Public Property ErrorText As String = Nothing

    ''' <summary>
    ''' 由 JSON 数组构建一组控件。每个元素形如：
    ''' <code>
    ''' { "type": "Button", "text": "下载游戏", "action": "LaunchGame",
    '''   "width": 200, "height": 40, "margin": "0,6,0,0",
    '''   "background": "#3B6BF0", "foreground": "#FFFFFF", "radius": 10,
    '''   "fontSize": 13, "bold": 1 }
    ''' </code>
    ''' 支持 type：StackPanel / Button / TextBlock / Border。
    ''' 容器可用 "children" 嵌套。
    ''' </summary>
    Public Function Build(Nodes As JArray) As List(Of FrameworkElement)
        ErrorText = Nothing
        Dim Result As New List(Of FrameworkElement)
        If Nodes Is Nothing Then Return Result
        For Each Node As JToken In Nodes
            Dim Element = BuildOne(Node)
            If Element IsNot Nothing Then Result.Add(Element)
        Next
        Return Result
    End Function

    Private Function BuildOne(Node As JToken) As FrameworkElement
        Try
            Dim Kind As String = If(Node("type"), "").ToString().Trim()
            Dim Element As FrameworkElement
            Select Case Kind.ToLowerInvariant()
                Case "stackpanel"
                    Dim Panel As New StackPanel
                    Panel.Orientation = If(If(Node("vertical"), "").ToString() = "0",
                                           Orientation.Horizontal, Orientation.Vertical)
                    Element = Panel
                Case "button"
                    Dim Btn As New MyButton With {.Height = 40, .MinWidth = 104}
                    Btn.Text = If(Node("text"), "").ToString()
                    Element = Btn
                Case "textblock"
                    Dim Lab As New TextBlock
                    Lab.Text = If(Node("text"), "").ToString()
                    Lab.TextWrapping = TextWrapping.Wrap
                    Element = Lab
                Case "border"
                    Element = New Border
                Case Else
                    '未知类型不报错、直接跳过：一个皮肤里有拼错的节点不该让整页空白
                    Return Nothing
            End Select

            ApplyCommon(Element, Node)

            '嵌套子节点
            Dim Children = Node("children")
            If Children IsNot Nothing AndAlso Children.Type = JTokenType.Array Then
                Dim Host As Panel = TryCast(Element, Panel)
                If Host Is Nothing AndAlso TypeOf Element Is Border Then
                    Dim Inner As New StackPanel
                    CType(Element, Border).Child = Inner
                    Host = Inner
                End If
                If Host IsNot Nothing Then
                    For Each Child As JToken In CType(Children, JArray)
                        Dim ChildElement = BuildOne(Child)
                        If ChildElement IsNot Nothing Then Host.Children.Add(ChildElement)
                    Next
                End If
            End If

            '动作绑定
            Dim ActionName As String = If(Node("action"), "").ToString().Trim()
            If ActionName <> "" Then
                If Not SkinAction.IsKnown(ActionName) Then
                    ErrorText = "未知动作：" & ActionName
                    Logger.Warn("皮肤里出现未知动作：" & ActionName)
                Else
                    AttachAction(Element, ActionName)
                End If
            End If

            Return Element
        Catch ex As Exception
            Logger.Warn(ex, "构建皮肤控件失败")
            ErrorText = ex.Message
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 把动作挂到控件上。
    ''' <para>
    ''' MyButton 自己消费了鼠标事件（内部 Click 走 MouseLeftButtonUp 并置 Handled），
    ''' 在外面挂冒泡或 Preview 都不可靠，所以按钮类型直接绑它自己的 Click；
    ''' 其它元素（TextBlock / Border / 容器）才用鼠标事件。
    ''' </para>
    ''' </summary>
    Private Sub AttachAction(Element As FrameworkElement, ActionName As String)
        If TypeOf Element Is MyButton Then
            Dim Btn As MyButton = CType(Element, MyButton)
            AddHandler Btn.Click, Sub(sender As Object, e As MouseButtonEventArgs)
                                      If Not SkinEvents.Invoke(ActionName) Then
                                          Hint($"皮肤动作失败：{SkinEvents.ErrorText}", HintType.Red)
                                      End If
                                  End Sub
            Return
        End If
        Dim Handler As New MouseButtonEventHandler(
            Sub(sender As Object, e As MouseButtonEventArgs)
                e.Handled = True
                If Not SkinEvents.Invoke(ActionName) Then
                    Hint($"皮肤动作失败：{SkinEvents.ErrorText}", HintType.Red)
                End If
            End Sub)
        Element.AddHandler(UIElement.MouseLeftButtonUpEvent, Handler, True)
        Element.IsHitTestVisible = True
        If TypeOf Element Is TextBlock OrElse TypeOf Element Is Border Then
            Element.Cursor = Cursors.Hand
        End If
    End Sub
    ''' <summary>套用通用外观属性。</summary>
    Private Sub ApplyCommon(Element As FrameworkElement, Node As JToken)
        Try
            Dim Width As Double, Height As Double
            If Double.TryParse(If(Node("width"), "").ToString(), Width) Then Element.Width = Width
            If Double.TryParse(If(Node("height"), "").ToString(), Height) Then Element.Height = Height

            Dim MarginText As String = If(Node("margin"), "").ToString().Trim()
            If MarginText <> "" Then
                Dim Parts = MarginText.Split(","c)
                If Parts.Length = 4 Then
                    Dim L, T, R, B As Double
                    If Double.TryParse(Parts(0), L) AndAlso Double.TryParse(Parts(1), T) AndAlso
                       Double.TryParse(Parts(2), R) AndAlso Double.TryParse(Parts(3), B) Then
                        Element.Margin = New Thickness(L, T, R, B)
                    End If
                End If
            End If

            Dim Align As String = If(Node("align"), "").ToString().Trim().ToLower()
            Select Case Align
                Case "left" : Element.HorizontalAlignment = HorizontalAlignment.Left
                Case "right" : Element.HorizontalAlignment = HorizontalAlignment.Right
                Case "stretch" : Element.HorizontalAlignment = HorizontalAlignment.Stretch
                Case "center" : Element.HorizontalAlignment = HorizontalAlignment.Center
            End Select

            '背景 / 前景 / 圆角
            Dim Bg As String = If(Node("background"), "").ToString().Trim()
            If Bg <> "" Then SetBrush(Element, Control.BackgroundProperty, Bg)
            Dim Fg As String = If(Node("foreground"), "").ToString().Trim()
            If Fg <> "" Then SetBrush(Element, Control.ForegroundProperty, Fg)

            Dim Radius As Double
            If Double.TryParse(If(Node("radius"), "").ToString(), Radius) Then
                If TypeOf Element Is Border Then
                    CType(Element, Border).CornerRadius = New CornerRadius(Radius)
                End If
            End If

            Dim FontSize As Double
            If Double.TryParse(If(Node("fontSize"), "").ToString(), FontSize) Then
                If TypeOf Element Is TextBlock Then CType(Element, TextBlock).FontSize = FontSize
                If TypeOf Element Is Control Then CType(Element, Control).FontSize = FontSize
            End If

            If If(Node("bold"), "").ToString() = "1" Then
                If TypeOf Element Is TextBlock Then CType(Element, TextBlock).FontWeight = FontWeights.Bold
            End If
        Catch ex As Exception
            Logger.Warn(ex, "套用皮肤控件样式失败")
        End Try
    End Sub

    ''' <summary>把颜色字符串写进依赖属性；失败就忽略，不让一个错字毁掉整个控件。</summary>
    Private Sub SetBrush(Element As FrameworkElement, Prop As DependencyProperty, Value As String)
        Dim Parsed As Color
        If ThemeParseColor(Value, Parsed) Then
            Element.SetValue(Prop, New SolidColorBrush(Parsed))
        End If
    End Sub

    ''' <summary>颜色解析的薄封装，复用主题模块里的实现。</summary>
    Private Function ThemeParseColor(Value As String, ByRef Result As Color) As Boolean
        Return TryParseColorString(Value, Result)
    End Function

End Module
