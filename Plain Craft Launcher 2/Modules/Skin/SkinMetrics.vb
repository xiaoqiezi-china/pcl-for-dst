Imports System.Windows.Media

''' <summary>
''' 皮肤度量：外观参数与图标覆盖。
''' <para>
''' 颜色可以直接靠 DynamicResource 联动（WPF 依赖属性），但**几何量**不行：
''' CornerRadius、Thickness、Geometry 都不会跟着资源键走，
''' 只能由控件在构造或载入时主动读一次。所以这些量集中在这里，
''' 皮肤切换时由 <see cref="SkinManager"/> 写入，再推给已有控件。
''' </para>
''' <para>
''' 这里只放"能靠改属性实现"的外观：
''' 圆角、描边、阴影、内边距、字号、导航朝向。
''' 结构性的东西（窗口骨架、页面布局）交给皮肤 XAML 的插槽，不走这里。
''' </para>
''' </summary>
Public Module SkinMetrics

    '-----------------
    ' 圆角
    '-----------------

    ''' <summary>卡片圆角。</summary>
    Public Property RadiusCard As Double = 5
    ''' <summary>按钮圆角。</summary>
    Public Property RadiusButton As Double = 3
    ''' <summary>输入框、下拉框圆角。</summary>
    Public Property RadiusInput As Double = 3
    ''' <summary>列表项圆角。</summary>
    Public Property RadiusItem As Double = 6
    ''' <summary>提示条圆角。</summary>
    Public Property RadiusHint As Double = 3

    '-----------------
    ' 描边与阴影
    '-----------------

    ''' <summary>卡片描边厚度。</summary>
    Public Property CardBorderThickness As Double = 0
    ''' <summary>卡片描边颜色键（资源键名）。为空则用默认灰。</summary>
    Public Property CardBorderBrushKey As String = ""

    ''' <summary>
    ''' 卡片阴影强度。
    ''' <para>
    ''' 用可空类型是为了区分"皮肤没设置"与"皮肤明确要求 0（无阴影）"。
    ''' 若用 Double，0 会被当成未设置而回退到默认值，皮肤就永远关不掉阴影。
    ''' </para>
    ''' </summary>
    Public Property CardShadowOpacity As Double? = Nothing

    '-----------------
    ' 按钮
    '-----------------

    ''' <summary>按钮描边宽度。</summary>
    Public Property ButtonBorderThickness As Double = 1
    ''' <summary>
    ''' 按钮是否为实心填充。
    ''' <para>用可空类型区分"没设置"与"明确要求描边"。</para>
    ''' </summary>
    Public Property ButtonFilled As Boolean? = Nothing
    ''' <summary>实心按钮的文字颜色（仅实心模式生效）。空则自动按深浅色选。</summary>
    Public Property ButtonFilledTextColor As String = ""

    '-----------------
    ' 卡片排版
    '-----------------

    ''' <summary>卡片内容区左内边距。</summary>
    Public Property CardPaddingLeft As Double = 25
    ''' <summary>卡片标题左边距（基准值，标题文字实际位置比它再小 10）。</summary>
    Public Property CardPaddingTop As Double = 40
    ''' <summary>卡片内容区右内边距。</summary>
    Public Property CardPaddingRight As Double = 25
    ''' <summary>卡片内容区底部内边距。</summary>
    Public Property CardPaddingBottom As Double = 20
    ''' <summary>卡片标题字号。</summary>
    Public Property CardTitleFontSize As Double = 13

    '-----------------
    ' 标题栏
    '-----------------

    ''' <summary>标题栏是否使用纯色（True）而不是内置渐变。</summary>
    Public Property TitlebarSolid As Boolean = False
    ''' <summary>标题栏纯色（仅在 TitlebarSolid 时生效）。</summary>
    Public Property TitlebarColor As String = ""
    ''' <summary>标题栏高度。</summary>
    Public Property TitlebarHeight As Double = 48
    ''' <summary>是否隐藏标题栏中间的图形 Logo。</summary>
    Public Property HideTitleLogo As Boolean = False
    ''' <summary>标题栏显示的文字。空表示沿用 PCL 自己的 Logo。</summary>
    Public Property TitleText As String = ""

    '-----------------
    ' 导航
    '-----------------

    ''' <summary>
    ''' 导航是否为竖排（图标在上、文字在下）。
    ''' <para>
    ''' 只改这一个开关：皮肤把导航槽摆在左栏时置 True，导航按钮就会
    ''' 按竖排几何排布。按钮还是原来那批按钮，事件与选中逻辑完全不经过第二套代码。
    ''' </para>
    ''' </summary>
    Public Property NavVertical As Boolean = False
    ''' <summary>竖排导航按钮的边长。</summary>
    Public Property NavButtonSize As Double = 52
    ''' <summary>竖排导航按钮圆角。</summary>
    Public Property NavButtonRadius As Double = 12
    ''' <summary>竖排导航图标尺寸。</summary>
    Public Property NavIconSize As Double = 18
    ''' <summary>竖排导航文字字号。</summary>
    Public Property NavFontSize As Double = 10.5
    ''' <summary>竖排导航按钮之间的间距。</summary>
    Public Property NavSpacing As Double = 2

    '-----------------
    ' 图标
    '-----------------

    ''' <summary>
    ''' 图标覆盖：键是语义名（如 "Launch"、"Download"），值是要用于 Logo 的几何路径。
    ''' <para>
    ''' PCL 的 Logo 是普通 CLR 属性、不是依赖属性，无法用 DynamicResource 换，
    ''' 所以皮肤只能通过这张表按名字替换。控件载入时查一次，查不到就用页面原本的图标。
    ''' </para>
    ''' </summary>
    Public ReadOnly Property Icons As New Dictionary(Of String, String)

    ''' <summary>取指定语义名的图标；没有覆盖则返回 Nothing。</summary>
    Public Function GetIcon(Name As String) As String
        If String.IsNullOrWhiteSpace(Name) Then Return Nothing
        Dim Result As String = Nothing
        If Icons.TryGetValue(Name, Result) Then Return Result
        Return Nothing
    End Function

    ''' <summary>把所有度量与图标恢复成内置默认值。</summary>
    Public Sub Reset()
        RadiusCard = 5
        RadiusButton = 3
        RadiusInput = 3
        RadiusItem = 6
        RadiusHint = 3
        CardBorderThickness = 0
        CardBorderBrushKey = ""
        CardShadowOpacity = Nothing
        ButtonBorderThickness = 1
        ButtonFilled = Nothing
        ButtonFilledTextColor = ""
        CardPaddingLeft = 25
        CardPaddingTop = 40
        CardPaddingRight = 25
        CardPaddingBottom = 20
        CardTitleFontSize = 13
        TitlebarSolid = False
        TitlebarColor = ""
        TitlebarHeight = 48
        HideTitleLogo = False
        TitleText = ""
        NavVertical = False
        NavButtonSize = 52
        NavButtonRadius = 12
        NavIconSize = 18
        NavFontSize = 10.5
        NavSpacing = 2
        Icons.Clear()
    End Sub

End Module
