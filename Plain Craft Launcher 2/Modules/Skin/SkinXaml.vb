Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Markup

''' <summary>
''' 皮肤可用的插槽名。
''' <para>
''' 皮肤在 XAML 里给任意容器加 <c>PCL:Slot="名字"</c>，PCL 就会把对应的
''' **真实控件**搬进去。这是皮肤控制整体版式的唯一途径，也是它能接管界面的原因。
''' </para>
''' <para>
''' 关键设计：搬的是控件本身，不是复制品。因此插槽里跑的还是 PCL 原来的
''' 事件、绑定与状态，皮肤怎么排都不会让功能失效。
''' </para>
''' </summary>
Public Module SkinSlots

    ''' <summary>标题栏（含 Logo、页面切换按钮、窗口按钮、版本号）。</summary>
    Public Const SlotTitle As String = "Title"
    ''' <summary>主导航按钮组。</summary>
    Public Const SlotNav As String = "Nav"
    ''' <summary>页面内容区（启动/下载/设置等整块）。</summary>
    Public Const SlotContent As String = "Content"
    ''' <summary>右下角的扩展按钮（任务管理、返回顶部等）。</summary>
    Public Const SlotExtra As String = "Extra"
    ''' <summary>底部提示条区。</summary>
    Public Const SlotHint As String = "Hint"
    ''' <summary>底部进度条。</summary>
    Public Const SlotProgress As String = "Progress"

    ''' <summary>全部插槽名，用于文档与校验。</summary>
    Public ReadOnly Property AllNames As String()
        Get
            Return {SlotTitle, SlotNav, SlotContent, SlotExtra, SlotHint, SlotProgress}
        End Get
    End Property

    ''' <summary>
    ''' 在已加载的皮肤布局里找名为 <paramref name="Name"/> 的插槽。
    ''' 找不到返回 Nothing。
    ''' </summary>
    Public Function FindSlot(Root As DependencyObject, Name As String) As FrameworkElement
        If Root Is Nothing OrElse String.IsNullOrWhiteSpace(Name) Then Return Nothing
        Dim Wanted As String = Name.Trim()
        Dim Stack As New Stack(Of DependencyObject)
        Stack.Push(Root)
        While Stack.Count > 0
            Dim Node = Stack.Pop()
            Dim Fe = TryCast(Node, FrameworkElement)
            If Fe IsNot Nothing Then
                Dim Value As String = SkinXaml.GetSlot(Fe)
                If String.Equals(Value, Wanted, StringComparison.OrdinalIgnoreCase) Then Return Fe
            End If
            '必须先走**逻辑树**：这份 XAML 刚解析出来、还没进布局，
            '此时视觉树是空的（VisualTreeHelper 拿不到任何子节点），只有逻辑树能遍历。
            '等它挂到窗口上之后再查，才轮到视觉树。
            Dim Logical As IEnumerable = Nothing
            Try
                Logical = LogicalTreeHelper.GetChildren(Node)
            Catch
            End Try
            If Logical IsNot Nothing Then
                For Each Child In Logical
                    Dim ChildDo = TryCast(Child, DependencyObject)
                    If ChildDo IsNot Nothing Then Stack.Push(ChildDo)
                Next
            End If
            '逻辑树里可能漏掉模板展开出来的内容，补一遍视觉树
            Dim Count As Integer = 0
            Try
                Count = VisualTreeHelper.GetChildrenCount(Node)
            Catch
            End Try
            For Index = 0 To Count - 1
                Stack.Push(VisualTreeHelper.GetChild(Node, Index))
            Next
        End While
        Return Nothing
    End Function

End Module

''' <summary>
''' 皮肤的动态 XAML 界面：允许皮肤用一个 XAML 文件接管界面版式。
''' <para>
''' 为什么复用 <see cref="GetObjectFromXML"/> 而不是自己写 XamlReader：
''' 它已经带类型白名单（禁 WebBrowser / Frame / Window / Code / FactoryMethod 等），
''' 皮肤包是从外部导入的，直接调 XamlReader.Load 等于给了任意代码执行入口。
''' </para>
''' <para>
''' 事件沿用 <see cref="CustomEventService"/>，写法与 PCL 主页自定义完全一致，
''' 皮肤作者不需要学新东西：
''' <code>
''' &lt;local:MyButton Text="启动" EventType="启动游戏" /&gt;
''' </code>
''' </para>
''' </summary>
Public Module SkinXaml

    ''' <summary>插槽附加属性：皮肤在容器上写 <c>PCL:Slot="Content"</c> 声明插槽。</summary>
    Public ReadOnly SlotProperty As DependencyProperty =
        DependencyProperty.RegisterAttached("Slot", GetType(String), GetType(SkinXaml), New PropertyMetadata(Nothing))
    Public Sub SetSlot(d As DependencyObject, Value As String)
        d.SetValue(SlotProperty, Value)
    End Sub
    Public Function GetSlot(d As DependencyObject) As String
        Return TryCast(d.GetValue(SlotProperty), String)
    End Function

    ''' <summary>上一次加载的错误说明；Nothing 表示没有错误。</summary>
    Public Property ErrorText As String = Nothing

    ''' <summary>
    ''' 从皮肤目录加载一个 XAML 文件并解析成控件。
    ''' 文件不存在时返回 Nothing（不是错误，皮肤可以不提供）。
    ''' </summary>
    Public Function LoadFile(FilePath As String) As Object
        ErrorText = Nothing
        Try
            If String.IsNullOrWhiteSpace(FilePath) OrElse Not FileUtils.Exists(FilePath) Then Return Nothing
            Dim Text As String = FileUtils.TryReadAsString(FilePath)
            If String.IsNullOrWhiteSpace(Text) Then
                ErrorText = "文件为空"
                Return Nothing
            End If
            Return LoadString(Text)
        Catch ex As Exception
            Logger.Warn(ex, "加载皮肤 XAML 失败：" & FilePath)
            ErrorText = ex.Message
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 解析一段 XAML 文本。
    ''' <para>
    ''' 命名空间由这里统一注入，皮肤文件里不要自己写 xmlns，这与 PCL 主页自定义的约定一致。
    ''' 事件同样写短形式 <c>EventType="切换页面"</c>，由 GetObjectFromXML 补全前缀。
    ''' </para>
    ''' </summary>
    Public Function LoadString(Text As String) As Object
        ErrorText = Nothing
        Try
            Dim Body As String = Text.
                Replace("local:CustomEventService.EventType", "EventType").
                Replace("local:CustomEventService.EventData", "EventData").
                Replace("local:SkinXaml.Slot=", "PCL:Slot=")
            '去掉作者自写的 xmlns 与 XML 声明，避免与注入的重复
            Body = Regex.Replace(Body, "\s+xmlns(:[A-Za-z0-9_]+)?\s*=\s*""[^""]*""", "")
            Body = Regex.Replace(Body, "\s+xmlns(:[A-Za-z0-9_]+)?\s*=\s*'[^']*'", "")
            Body = Regex.Replace(Body, "<\?xml[^>]*\?>", "")
            '把 PCL: 前缀补成完整写法：GetObjectFromXML 只认完整命名空间前缀
            Body = Regex.Replace(Body, "(?<![A-Za-z0-9_.])PCL:Slot=", "local:SkinXaml.Slot=")

            '与 PCL 主页自定义一致的命名空间注入
            Dim Wrapped As String =
                "<StackPanel xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" " &
                "xmlns:sys=""clr-namespace:System;assembly=mscorlib"" " &
                "xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" " &
                "xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" " &
                "xmlns:core=""clr-namespace:MeloongCore;assembly=MeloongCore"" " &
                "xmlns:corewpf=""clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf"">" & Body & "</StackPanel>"

            Dim Result = GetObjectFromXML(Wrapped)
            If Result Is Nothing Then
                ErrorText = "解析结果为空"
                Return Nothing
            End If
            '根包装是个 StackPanel，剥掉它，把真正的内容还回去
            Dim Panel = TryCast(Result, StackPanel)
            If Panel IsNot Nothing Then
                If Panel.Children.Count = 0 Then
                    ErrorText = "布局内容为空"
                    Return Nothing
                ElseIf Panel.Children.Count = 1 Then
                    Dim Only = Panel.Children(0)
                    Panel.Children.Clear()
                    Return Only
                End If
            End If
            Return Result
        Catch ex As Exception
            Logger.Warn(ex, "解析皮肤 XAML 失败")
            ErrorText = FriendlyError(ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 把一个控件挂到容器里。容器已有内容时先清空，避免换皮肤残留。
    ''' </summary>
    Public Sub Mount(Host As Panel, Content As Object)
        If Host Is Nothing Then Return
        Host.Children.Clear()
        If Content Is Nothing Then Return
        Dim Element = TryCast(Content, UIElement)
        If Element IsNot Nothing Then
            Host.Children.Add(Element)
            Return
        End If
        Dim Items = TryCast(Content, IEnumerable)
        If Items IsNot Nothing Then
            For Each Item In Items
                Dim Child = TryCast(Item, UIElement)
                If Child IsNot Nothing Then Host.Children.Add(Child)
            Next
        End If
    End Sub

End Module
