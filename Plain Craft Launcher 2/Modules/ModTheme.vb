' 主题与外观：配色计算、外观风格（PCL / PCL Inder）、以及切换后重启的提示。
' 从 ModSecret 拆出来：那是放凭据的基础设施模块，不该同时装着一整套主题引擎。
' 成员仍是 Module 级（全程序可见），调用点不需要任何改动。
Friend Module ModTheme

    ''' <summary>
    ''' 高对比度主题的编号。
    ''' <para>
    ''' 它不是「某个色相」的变体，而是一整套写死的黑白黄：纯黑底、纯白字、纯黄强调。
    ''' 所以它在配色计算里走独立分支，不受深浅模式与外观风格影响。
    ''' </para>
    ''' </summary>
    Public Const ThemeHighContrast As Integer = 15

    ''' <summary>
    ''' 定制主题的编号。
    ''' <para>
    ''' 它和「自定义」（色相滑块那个）不是一回事：
    ''' 自定义是用色相 / 饱和度 / 亮度算出一套，
    ''' 定制是由用户逐个指定 9 个主题色，不再算。
    ''' </para>
    ''' </summary>
    Public Const ThemeCustom As Integer = 16

    ''' <summary>当前主题是不是定制主题。</summary>
    Public ReadOnly Property IsCustomTheme As Boolean
        Get
            Return ThemeNow = ThemeCustom
        End Get
    End Property

    ''' <summary>当前主题是不是高对比度。</summary>
    Public ReadOnly Property IsHighContrast As Boolean
        Get
            Return ThemeNow = ThemeHighContrast
        End Get
    End Property

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public Color9 As New MyColor(245, 248, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    '代码里动态创建控件用的语义色（深浅色模式各一套）
    Public BackColorMain As New MyColor(38, 49, 63)
    Public BackColorSub As New MyColor(30, 40, 54)
    Public TextColorMain As New MyColor(232, 238, 247)
    Public TextColorSub As New MyColor(147, 161, 181)
    Public TitleTextColor As New MyColor(255, 255, 255)
    '聊天气泡用色
    Public BubbleOtherBack As New MyColor(31, 41, 56)
    Public BubbleOtherText As New MyColor(232, 238, 247)
    Public BubbleMineBack As New MyColor(37, 99, 235)
    Public BubbleMineText As New MyColor(255, 255, 255)
    Public BubbleMetaText As New MyColor(190, 205, 225)

    Public ThemeNow As Integer = -1
    ''' <summary>当前是否处于深色模式。界面里需要按深浅取色时用它。</summary>
    Public ReadOnly Property DstDarkMode As Boolean
        Get
            Return Settings.Get(Of Boolean)("DstDarkMode")
        End Get
    End Property
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    '标题栏标签页（深色标题栏下的选中/未选中状态）
    Public TabSelectedBack As New MyColor(255, 35, 43, 54)
    Public TabTextIdle As New MyColor(255, 147, 160, 174)

    ''' <summary>界面外观风格。两套是完整的整套外观，不是颜色覆盖。</summary>
    Public Enum AppearanceStyle
        ''' <summary>PCL 原版：跟随「浅色/深色」设置，顶部横排导航。</summary>
        Pcl = 0
        ''' <summary>PCL Indra：写死的现代简约深色，左侧竖排导航。</summary>
        Indra = 1
    End Enum


    ''' <summary>当前外观风格。</summary>
    Public ReadOnly Property Appearance As AppearanceStyle
        Get
            Try
                Return If(Settings.Get(Of Integer)("DstAppearance") = 1, AppearanceStyle.Indra, AppearanceStyle.Pcl)
            Catch ex As Exception
                Return AppearanceStyle.Pcl
            End Try
        End Get
    End Property

    ''' <summary>
    ''' 切换外观。
    ''' <para>
    ''' 外观会改变窗口骨架与整套控件模板，运行中逐个控件补推样式既慢又不稳，
    ''' 所以直接按"改设置 → 重启 → 新进程读取配置"的路径走。
    ''' 重启本身只要一两秒，比让用户自己关掉再点开省事。
    ''' </para>
    ''' </summary>
    Public Sub SetAppearance(Value As AppearanceStyle)
        '设置项是单选按钮自动写入的，点下去时 DstAppearance 已经是新值，
        '所以不能拿 Appearance 判断"变没变"——它跟界面实际状态不是一回事。
        '只有 AppliedAppearance 才代表屏幕上的真实外观。
        Settings.Set("DstAppearance", CInt(Value))
        If FrmMain IsNot Nothing AndAlso Value = FrmMain.AppliedAppearance Then
            '选回当前正在用的外观，什么都不用做
            ConfigUtils.SaveAll()
            Return
        End If
        ConfigUtils.SaveAll()
        RunInUi(
        Sub()
            ShowRestartHint("外观已切换为 " & If(Value = AppearanceStyle.Indra, "PCL Inder", "PCL") & "，需要重启启动器才能完全生效。")
        End Sub)
    End Sub


    ''' <summary>
    ''' 自定义主题的色调 / 饱和度 / 色相渐变 / 亮度发生变化时，重新算一遍配色。
    ''' <para>
    ''' 必须传 Force：<see cref="ThemeRefresh"/> 会按主题编号做短接，
    ''' 而拖动滑块时主题编号一直是「自定义」，不 Force 就会被直接拦掉、界面没反应。
    ''' </para>
    ''' <para>
    ''' 也只在当前主题确实是「自定义」时才做事：这四个设置项在别的主题下
    ''' 同样会被写入，跟着重算既浪费，还会把「滑稽彩」的随机色打乱。
    ''' </para>
    ''' </summary>
    Public Sub RefreshCustomThemePalette(NewValue As Object)
        If ThemeNow <> 14 AndAlso ThemeNow <> ThemeCustom Then Return
        ThemeRefresh(Force:=True)
    End Sub

    ''' <summary>
    ''' 读取用户为「自定义」主题单独指定的颜色。返回 Nothing 表示这一项没被覆盖。
    ''' <para>设置里存成 0xRRGGBB 的十六进制字符串，空串表示不覆盖、沿用算法算出来的颜色。</para>
    ''' </summary>
    Public Function GetCustomColor(Index As Integer) As MyColor
        Dim Text As String = Settings.Get(Of String)("UiLauncherCustomColor" & Index)
        If String.IsNullOrWhiteSpace(Text) Then Return Nothing
        Try
            Text = Text.Trim()
            If Text.StartsWith("#"c) Then Text = Text.Substring(1)
            If Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) Then Text = Text.Substring(2)
            If Text.Length <> 6 Then Return Nothing
            Dim Value As Integer = Convert.ToInt32(Text, 16)
            Return New MyColor(255, (Value >> 16) And &HFF, (Value >> 8) And &HFF, Value And &HFF)
        Catch ex As Exception
            Logger.Warn(ex, $"解析自定义主题色 {Index} 失败：{Text}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 定制主题：9 个主题色全部取自用户设置，未指定的那一档用默认值兜底。
    ''' <para>
    ''' 这里不按色相计算，也不再派生灰阶与语义色，用户看到的就是他选的那 9 个颜色。
    ''' 派生色（卡片、边框、输入框）按主题色 1 的明暗自动挑深或浅，保证文字可读。
    ''' </para>
    ''' </summary>
    Private Sub ApplyCustomThemePalette()
        Color1 = If(GetCustomColor(1), New MyColor(52, 61, 74))
        Color2 = If(GetCustomColor(2), New MyColor(11, 91, 203))
        Color3 = If(GetCustomColor(3), New MyColor(19, 112, 243))
        Color4 = If(GetCustomColor(4), New MyColor(72, 144, 245))
        Color5 = If(GetCustomColor(5), New MyColor(150, 192, 249))
        Color6 = If(GetCustomColor(6), New MyColor(213, 230, 253))
        Color7 = If(GetCustomColor(7), New MyColor(222, 236, 253))
        Color8 = If(GetCustomColor(8), New MyColor(234, 242, 254))
        Color9 = If(GetCustomColor(9), New MyColor(245, 248, 254))

        '背景由 Color9 决定深浅：用户把 9 号色设成暗色，整套界面就跟着走暗色
        Dim IsDarkBackground As Boolean = (Color9.R + Color9.G + Color9.B) / 3 < 128
        ColorBg0 = Color9
        ColorBg1 = New MyColor(190, Color9)

        If IsDarkBackground Then
            SetColorBrush("ColorBrushCard", New MyColor(28, Color9))
            SetColorBrush("ColorBrushCardHover", New MyColor(40, Color9))
            SetColorBrush("ColorBrushRail", New MyColor(20, Color9))
            SetColorBrush("ColorBrushBorder", New MyColor(48, Color9))
            SetColorBrush("ColorBrushBorderLight", New MyColor(64, Color9))
            SetColorBrush("ColorBrushCardBorder", New MyColor(40, Color9))
            SetColorBrush("ColorBrushInput", New MyColor(24, Color9))
            SetColorBrush("ColorBrushBackgroundTransparentSidebar", New MyColor(235, Color9))
            SetColorBrush("ColorBrushGray1", New MyColor(240, 240, 240))
            SetColorBrush("ColorBrushGray2", New MyColor(190, 190, 190))
            SetColorBrush("ColorBrushGray3", New MyColor(150, 150, 150))
            SetColorBrush("ColorBrushGray4", New MyColor(120, 120, 120))
            SetColorBrush("ColorBrushGray5", New MyColor(90, 90, 90))
            SetColorBrush("ColorBrushGray6", New MyColor(60, 60, 60))
            SetColorBrush("ColorBrushGray7", New MyColor(40, 40, 40))
            SetColorBrush("ColorBrushGray8", New MyColor(30, 30, 30))
            SetColorBrush("ColorBrushTitleIcon", New MyColor(255, 255, 255))
            SetColorBrush("ColorBrushLaunchSub", New MyColor(Color.FromArgb(215, 255, 255, 255)))
            SetColorBrush("ColorBrushHalfWhite", New MyColor(Color.FromArgb(26, 255, 255, 255)))
            SetColorBrush("ColorBrushSemiTransparent", New MyColor(1, Color9))
            SetColorBrush("ColorBrushWindowBorder", New MyColor(31, 255, 255, 255))
            BackColorMain = New MyColor(22, Color9)
            BackColorSub = New MyColor(16, Color9)
            TextColorSub = New MyColor(150, 150, 150)
            BubbleOtherBack = New MyColor(28, Color9)
            BubbleMetaText = New MyColor(190, 190, 190)
        Else
            SetColorBrush("ColorBrushCard", New MyColor(255, 255, 255))
            SetColorBrush("ColorBrushCardHover", New MyColor(250, 250, 250))
            SetColorBrush("ColorBrushRail", New MyColor(245, 245, 245))
            SetColorBrush("ColorBrushBorder", New MyColor(224, 224, 224))
            SetColorBrush("ColorBrushBorderLight", New MyColor(240, 240, 240))
            SetColorBrush("ColorBrushCardBorder", New MyColor(235, 235, 235))
            SetColorBrush("ColorBrushInput", New MyColor(255, 255, 255))
            SetColorBrush("ColorBrushBackgroundTransparentSidebar", New MyColor(235, 245, 245, 245))
            SetColorBrush("ColorBrushGray1", New MyColor(64, 64, 64))
            SetColorBrush("ColorBrushGray2", New MyColor(115, 115, 115))
            SetColorBrush("ColorBrushGray3", New MyColor(140, 140, 140))
            SetColorBrush("ColorBrushGray4", New MyColor(166, 166, 166))
            SetColorBrush("ColorBrushGray5", New MyColor(204, 204, 204))
            SetColorBrush("ColorBrushGray6", New MyColor(235, 235, 235))
            SetColorBrush("ColorBrushGray7", New MyColor(240, 240, 240))
            SetColorBrush("ColorBrushGray8", New MyColor(245, 245, 245))
            SetColorBrush("ColorBrushTitleIcon", New MyColor(255, 255, 255))
            SetColorBrush("ColorBrushLaunchSub", New MyColor(Color.FromArgb(215, 255, 255, 255)))
            SetColorBrush("ColorBrushHalfWhite", New MyColor(Color.FromArgb(26, 255, 255, 255)))
            SetColorBrush("ColorBrushSemiTransparent", New MyColor(1, Color9))
            SetColorBrush("ColorBrushWindowBorder", New MyColor(51, 0, 0, 0))
            BackColorMain = New MyColor(245, 245, 245)
            BackColorSub = New MyColor(235, 235, 235)
            TextColorSub = New MyColor(140, 140, 140)
            BubbleOtherBack = New MyColor(245, 245, 245)
            BubbleMetaText = New MyColor(166, 166, 166)
        End If

        TextColorMain = Color1
        TitleTextColor = New MyColor(255, 255, 255)
        BubbleOtherText = Color1
        BubbleMineBack = Color3
        BubbleMineText = New MyColor(255, 255, 255)
    End Sub

    ''' <summary>
    ''' 把「定制」主题下用户指定的颜色套到 Color1~Color9 上。
    ''' <para>
    ''' 只在自定义主题下生效：别的主题是一整套算好的配色，
    ''' 单独改其中一个颜色只会让界面变得东一块西一块。
    ''' </para>
    ''' </summary>
    Private Sub ApplyCustomColors()
        '\xe5\x8f\xaa\xe6\x9c\x8d\xe5\x8a\xa1\xe3\x80\x8c\xe8\x87\xaa\xe5\xae\x9a\xe4\xb9\x89\xe3\x80\x8d\xef\xbc\x8814\xef\xbc\x89\xef\xbc\x9a\xe5\xae\x9a\xe5\x88\xb6\xe4\xb8\xbb\xe9\xa2\x98\xe8\xb5\xb0\xe8\x87\xaa\xe5\xb7\xb1\xe7\x9a\x84\xe5\x88\x86\xe6\x94\xaf\xef\xbc\x8c\xe4\xb8\x8d\xe8\x83\xbd\xe5\x86\x8d\xe8\xa2\xab\xe8\xa6\x86\xe7\x9b\x96\n        If ThemeNow <> 14 Then Return
        Dim Picked(9) As MyColor
        For I = 1 To 9
            Picked(I) = GetCustomColor(I)
        Next
        If Picked(1) IsNot Nothing Then Color1 = Picked(1)
        If Picked(2) IsNot Nothing Then Color2 = Picked(2)
        If Picked(3) IsNot Nothing Then Color3 = Picked(3)
        If Picked(4) IsNot Nothing Then Color4 = Picked(4)
        If Picked(5) IsNot Nothing Then Color5 = Picked(5)
        If Picked(6) IsNot Nothing Then Color6 = Picked(6)
        If Picked(7) IsNot Nothing Then Color7 = Picked(7)
        If Picked(8) IsNot Nothing Then Color8 = Picked(8)
        If Picked(9) IsNot Nothing Then Color9 = Picked(9)
    End Sub

    ''' <summary>
    ''' 弹出"需要重启"提示，用户确认后自动重启。
    ''' </summary>
    Public Sub ShowRestartHint(Message As String)
        If MyMsgBox(Message & vbCrLf & vbCrLf & "现在重启吗？", "需要重启", "立即重启", "稍后", IsWarn:=True) = 1 Then
            FormMain.RestartProgram()
        Else
            Hint("改动会在下次启动时生效", HintType.Blue, False)
        End If
    End Sub

    ''' <summary>
    ''' 就地更新一个颜色资源。
    ''' <para>
    ''' 关键点：不替换资源对象，而是改已有 SolidColorBrush 的 Color。
    ''' XAML 里用 StaticResource 引到的画刷是对象引用，一旦把资源换成新对象，
    ''' 那些引用还指着旧的，只能靠重建界面（重启）才更新。
    ''' 就地改色则所有引用同时生效，主题与外观才能真正热切换。
    ''' </para>
    ''' </summary>
    Private Sub SetColorBrush(Key As String, Value As MyColor)
        Try
            Dim Existing = TryCast(Application.Current.Resources(Key), SolidColorBrush)
            If Existing IsNot Nothing Then
                '冻结的画刷不能改，只能退回到替换；内置画刷都没冻结，正常走这一支
                If Existing.IsFrozen Then
                    '换成一个新的：需要该键支持动态刷新，这里退化为替换
                    Application.Current.Resources(Key) = New SolidColorBrush(CType(Value, Color))
                Else
                    Existing.Color = CType(Value, Color)
                End If
                Return
            End If
            Application.Current.Resources(Key) = New SolidColorBrush(CType(Value, Color))
        Catch ex As Exception
            '单个颜色失败不该中断整套外观刷新
            Logger.Warn(ex, $"更新颜色资源失败：{Key}")
        End Try
    End Sub

    ''' <summary>
    ''' 刷新主题配色与界面外观。
    ''' <param name="Force">
    ''' 强制重算。默认会按主题编号做短接（同一套主题不重复计算），
    ''' 但换皮肤这类“主题编号没变、覆盖层却变了”的场景必须强制走一遍，
    ''' 否则会被上面的短接直接拦掉，表现为“点了皮肤但界面没反应”。
    ''' </param>
    ''' </summary>
    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1, Optional Force As Boolean = False)
        Try
            If NewTheme < 0 Then NewTheme = Settings.Get(Of Integer)("UiLauncherTheme")
            '滑稽彩每 50ms 由主时钟重新调用，需要每次都重算随机色，不做短接
            If Not Force AndAlso ThemeNow = NewTheme AndAlso NewTheme >= 0 AndAlso ThemeDontClick <> 2 Then Return
            If NewTheme >= 0 Then
                ThemeNow = NewTheme
                ThemeApplyPalette(NewTheme)
            End If

            '深浅两套配色：1 = 主文字，2~4 = 强调色，5~8 = 由浅到深的面
            Dim Shift As Double = ColorLightAdjust * 0.4
            Dim Dark As Boolean = Settings.Get(Of Boolean)("DstDarkMode")
            '外观风格：PCL 用原本的深浅主题，Indra 用写死的现代简约深色配色。
            '两套是互斥的整套外观，不是颜色覆盖，所以在这里一次算清。
            '
            '高对比度排在最前面：它按定义就是「强行覆盖所有颜色」，
            '既不看深浅模式，也不看 CurrentAppearance，否则选了它却还是 Indra 的配色，
            '用户会以为功能坏了。
            If IsHighContrast Then
                ApplyHighContrastPalette()
            ElseIf IsCustomTheme Then
                '\xe5\xae\x9a\xe5\x88\xb6\xe4\xb8\xbb\xe9\xa2\x98\xef\xbc\x9a\xe4\xb9\x9d\xe4\xb8\xaa\xe9\xa2\x9c\xe8\x89\xb2\xe5\x85\xa8\xe7\x94\xb1\xe7\x94\xa8\xe6\x88\xb7\xe6\x8c\x87\xe5\xae\x9a\xef\xbc\x8c\xe4\xb8\x8d\xe5\x86\x8d\xe6\x8c\x89\xe8\x89\xb2\xe7\x9b\xb8\xe7\xae\x97
                ApplyCustomThemePalette()
            ElseIf Appearance = AppearanceStyle.Indra Then
                Color1 = New MyColor(237, 239, 243)
                Color2 = New MyColor(76, 141, 255)
                Color3 = New MyColor(108, 163, 255)
                Color4 = New MyColor(150, 190, 255)
                Color5 = New MyColor(28, 33, 39)
                Color6 = New MyColor(22, 26, 31)
                Color7 = New MyColor(17, 20, 24)
                Color8 = New MyColor(11, 13, 16)
                ColorBg0 = New MyColor(16, 19, 23)
                ColorBg1 = New MyColor(247, New MyColor(11, 13, 16))

                SetColorBrush("ColorBrushCard", New MyColor(20, 23, 28))
                SetColorBrush("ColorBrushCardHover", New MyColor(26, 30, 36))
                SetColorBrush("ColorBrushRail", New MyColor(20, 23, 28))
                SetColorBrush("ColorBrushBackgroundTransparentSidebar", New MyColor(246, New MyColor(13, 16, 19)))
                SetColorBrush("ColorBrushGray1", New MyColor(237, 239, 243))
                SetColorBrush("ColorBrushGray2", New MyColor(170, 178, 188))
                SetColorBrush("ColorBrushGray3", New MyColor(122, 131, 142))
                SetColorBrush("ColorBrushGray4", New MyColor(92, 100, 110))
                SetColorBrush("ColorBrushGray5", New MyColor(66, 73, 83))
                SetColorBrush("ColorBrushGray6", New MyColor(46, 52, 60))
                SetColorBrush("ColorBrushGray7", New MyColor(28, 32, 38))
                SetColorBrush("ColorBrushGray8", New MyColor(20, 23, 28))
                SetColorBrush("ColorBrushBorder", New MyColor(32, 36, 43))
                SetColorBrush("ColorBrushBorderLight", New MyColor(45, 50, 58))
                SetColorBrush("ColorBrushCardBorder", New MyColor(28, 32, 39))
                SetColorBrush("ColorBrushInput", New MyColor(15, 18, 22))
                SetColorBrush("ColorBrushTitleIcon", New MyColor(Color.FromRgb(255, 255, 255)))
                '启动按钮是实心蓝底，副标题用半透明白才读得清
                SetColorBrush("ColorBrushLaunchSub", New MyColor(Color.FromArgb(215, 255, 255, 255)))
                SetColorBrush("ColorBrushHalfWhite", New MyColor(Color.FromArgb(20, 255, 255, 255)))
                SetColorBrush("ColorBrushSemiTransparent", New MyColor(1, Color8))
                '窗口最外圈的轮廓：深色外观下用极淡的白，否则描边会和背景糊在一起看不出边界
                SetColorBrush("ColorBrushWindowBorder", New MyColor(31, 255, 255, 255))
                BackColorMain = New MyColor(17, 20, 24)
                BackColorSub = New MyColor(12, 14, 17)
                TextColorMain = Color1
                TextColorSub = New MyColor(122, 131, 142)
                TitleTextColor = New MyColor(255, 255, 255)
                BubbleOtherBack = New MyColor(20, 23, 28)
                BubbleOtherText = Color1
                BubbleMineBack = New MyColor(76, 141, 255)
                BubbleMineText = New MyColor(255, 255, 255)
                BubbleMetaText = New MyColor(170, 178, 188)
            ElseIf Dark Then
                Color1 = New MyColor().FromHSL(ColorHue, 20, 90)
                Color2 = New MyColor().FromHSL(ColorHue, 78, 66 + Shift)
                Color3 = New MyColor().FromHSL(ColorHue, 85, 60 + Shift)
                Color4 = New MyColor().FromHSL(ColorHue, 80, 74 + Shift)
                Color5 = New MyColor().FromHSL(ColorHue, 18, 32 + Shift)
                Color6 = New MyColor().FromHSL(ColorHue, 20, 24 + Shift)
                Color7 = New MyColor().FromHSL(ColorHue, 22, 19 + Shift)
                Color8 = New MyColor().FromHSL(ColorHue, 24, 14 + Shift)
                ColorBg0 = New MyColor().FromHSL(ColorHue, 18, 30)
                ColorBg1 = New MyColor(190, Color8)

                '卡片、弹窗、输入框用的深色面，跟着主题色相走
                SetColorBrush("ColorBrushCard", New MyColor().FromHSL(ColorHue, 22, 21))
                SetColorBrush("ColorBrushCardHover", New MyColor().FromHSL(ColorHue, 22, 27))
                SetColorBrush("ColorBrushRail", New MyColor().FromHSL(ColorHue, 20, 18))
                SetColorBrush("ColorBrushBorder", New MyColor().FromHSL(ColorHue, 12, 30))
                SetColorBrush("ColorBrushBorderLight", New MyColor().FromHSL(ColorHue, 12, 38))
                SetColorBrush("ColorBrushCardBorder", New MyColor().FromHSL(ColorHue, 12, 28))
                SetColorBrush("ColorBrushInput", New MyColor().FromHSL(ColorHue, 18, 20))
                SetColorBrush("ColorBrushBackgroundTransparentSidebar", New MyColor(240, Color8))
                SetColorBrush("ColorBrushGray1", New MyColor().FromHSL(ColorHue, 16, 92))
                SetColorBrush("ColorBrushGray2", New MyColor().FromHSL(ColorHue, 14, 76))
                SetColorBrush("ColorBrushGray3", New MyColor().FromHSL(ColorHue, 12, 62))
                SetColorBrush("ColorBrushGray4", New MyColor().FromHSL(ColorHue, 12, 48))
                SetColorBrush("ColorBrushGray5", New MyColor().FromHSL(ColorHue, 12, 34))
                SetColorBrush("ColorBrushGray6", New MyColor().FromHSL(ColorHue, 14, 26))
                SetColorBrush("ColorBrushGray7", New MyColor().FromHSL(ColorHue, 20, 22))
                SetColorBrush("ColorBrushGray8", New MyColor().FromHSL(ColorHue, 22, 16))
                SetColorBrush("ColorBrushTitleIcon", New MyColor(Color.FromRgb(255, 255, 255)))
                SetColorBrush("ColorBrushLaunchSub", New MyColor(Color.FromArgb(215, 255, 255, 255)))
                SetColorBrush("ColorBrushHalfWhite", New MyColor(Color.FromArgb(26, 255, 255, 255)))
                '控件默认底/悬停底：极淡的主题色，深浅两套都要给，否则浅色模式下会沿用深色值
                SetColorBrush("ColorBrushSemiTransparent", New MyColor(1, Color8))
                '窗口最外圈的轮廓：深色下用极淡的白，否则描边会和背景糊在一起看不出边界
                SetColorBrush("ColorBrushWindowBorder", New MyColor(31, 255, 255, 255))
                BackColorMain = New MyColor().FromHSL(ColorHue, 18, 24)
                BackColorSub = New MyColor().FromHSL(ColorHue, 16, 18)
                TextColorMain = Color1
                TextColorSub = New MyColor().FromHSL(ColorHue, 12, 62)
                TitleTextColor = New MyColor(255, 255, 255)
                BubbleOtherBack = New MyColor().FromHSL(ColorHue, 14, 22)
                BubbleOtherText = Color1
                BubbleMineBack = New MyColor().FromHSL(ColorHue, 85, 58)
                BubbleMineText = New MyColor(255, 255, 255)
                BubbleMetaText = New MyColor().FromHSL(ColorHue, 14, 74)
            Else
                '浅色：完全沿用 PCL 原版的配色算法（FromHSL2），保证观感与官方一致
                '
                '自定义主题是例外：FromHSL2 会按色相查表再压一次亮度，
                '内置主题的色相是挑过的，压完正好；用户自己调出来的色相没被挑过，
                '蓝色系会被压到 34% 亮度，饱和度拉满也是一片发闷的墨蓝。
                '自定义就直接用 HSL，让三个滑块如实反映到颜色上。
                Dim Custom As Boolean = ThemeNow = 14
                Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
                If Custom Then
                    Color2 = New MyColor().FromHSL(ColorHue, ColorSat, 45 + ColorLightAdjust)
                    Color3 = New MyColor().FromHSL(ColorHue, ColorSat, 55 + ColorLightAdjust)
                    Color4 = New MyColor().FromHSL(ColorHue, ColorSat, 68 + ColorLightAdjust)
                Else
                    Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
                    Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
                    Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
                End If
                Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
                Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
                Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
                Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
                ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
                ColorBg1 = New MyColor(190, Color7)

                '浅色一律用原版的固定灰阶，不跟着主题色相走
                SetColorBrush("ColorBrushCard", New MyColor(255, 255, 255))
                SetColorBrush("ColorBrushCardHover", New MyColor(250, 250, 250))
                SetColorBrush("ColorBrushRail", New MyColor(245, 245, 245))
                SetColorBrush("ColorBrushBorder", New MyColor(224, 224, 224))
                SetColorBrush("ColorBrushBorderLight", New MyColor(240, 240, 240))
                SetColorBrush("ColorBrushCardBorder", New MyColor(235, 235, 235))
                SetColorBrush("ColorBrushInput", New MyColor(255, 255, 255))
                SetColorBrush("ColorBrushBackgroundTransparentSidebar", New MyColor(235, 245, 245, 245))
                SetColorBrush("ColorBrushGray1", New MyColor(64, 64, 64))
                SetColorBrush("ColorBrushGray2", New MyColor(115, 115, 115))
                SetColorBrush("ColorBrushGray3", New MyColor(140, 140, 140))
                SetColorBrush("ColorBrushGray4", New MyColor(166, 166, 166))
                SetColorBrush("ColorBrushGray5", New MyColor(204, 204, 204))
                SetColorBrush("ColorBrushGray6", New MyColor(235, 235, 235))
                SetColorBrush("ColorBrushGray7", New MyColor(240, 240, 240))
                SetColorBrush("ColorBrushGray8", New MyColor(245, 245, 245))
                SetColorBrush("ColorBrushTitleIcon", New MyColor(Color.FromRgb(255, 255, 255)))
                SetColorBrush("ColorBrushLaunchSub", New MyColor(Color.FromArgb(215, 255, 255, 255)))
                '原版恒为白色 10%：白色叠在白卡片上看不见，换成黑色就会变成灰块
                SetColorBrush("ColorBrushHalfWhite", New MyColor(Color.FromArgb(26, 255, 255, 255)))
                '原版就是 1/255 的极淡蒙层，直接照搬
                SetColorBrush("ColorBrushSemiTransparent", New MyColor(1, Color8))
                '窗口最外圈的轮廓：浅色下用半透明黑，压在白底上是一条淡淡的灰边
                SetColorBrush("ColorBrushWindowBorder", New MyColor(51, 0, 0, 0))
                BackColorMain = New MyColor(245, 245, 245)
                BackColorSub = New MyColor(235, 235, 235)
                TextColorMain = Color1
                TextColorSub = New MyColor(140, 140, 140)
                TitleTextColor = New MyColor(255, 255, 255)
                BubbleOtherBack = New MyColor(245, 245, 245)
                BubbleOtherText = Color1
                BubbleMineBack = New MyColor().FromHSL2(ColorHue, ColorSat, 55)
                BubbleMineText = New MyColor(255, 255, 255)
                BubbleMetaText = New MyColor(166, 166, 166)
            End If
            '\xe8\x87\xaa\xe5\xae\x9a\xe4\xb9\x89\xe4\xb8\xbb\xe9\xa2\x98\xe4\xb8\x8b\xe7\x9a\x84\xe9\xa2\x9c\xe8\x89\xb2\xe8\xa6\x86\xe7\x9b\x96\xef\xbc\x9a\xe5\xbf\x85\xe9\xa1\xbb\xe6\x94\xbe\xe5\x9c\xa8\xe5\x86\x99\xe8\xb5\x84\xe6\xba\x90\xe4\xb9\x8b\xe5\x89\x8d\n            ApplyCustomColors()

            Application.Current.Resources("ColorObjectGray7") = CType(Application.Current.Resources("ColorBrushGray7"), SolidColorBrush).Color
            Application.Current.Resources("ColorObjectGray8") = CType(Application.Current.Resources("ColorBrushGray8"), SolidColorBrush).Color

            '原版就是 1/255 的极淡蒙层，深浅两套都用它，保持与官方一致
            ColorSemiTransparent = New MyColor(1, Color8)
            SetColorBrush("ColorBrush1", Color1)
            SetColorBrush("ColorBrush2", Color2)
            SetColorBrush("ColorBrush3", Color3)
            SetColorBrush("ColorBrush4", Color4)
            SetColorBrush("ColorBrush5", Color5)
            SetColorBrush("ColorBrush6", Color6)
            SetColorBrush("ColorBrush7", Color7)
            SetColorBrush("ColorBrush8", Color8)
            SetColorBrush("ColorBrush9", Color9)
            SetColorBrush("ColorBrushBg0", ColorBg0)
            SetColorBrush("ColorBrushBg1", ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObject9") = CType(Color9, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)


            '标题栏导航：文字一律用白色系，只有透明度区分选中与未选中。
            '之前 TabTextIdle 是个写死的灰蓝，在高对比度与深色主题下读不清；
            '标题栏背景本来就是深色渐变，白字在任何主题下都成立。
            TabTextIdle = New MyColor(190, 255, 255, 255)
            TabSelectedBack = If(IsHighContrast, New MyColor(60, 255, 255, 255), New MyColor(255, 35, 43, 54))

            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub

    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '这两个变量后面“主页面背景”也要用，必须声明在 If 之外
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            'Indra 外观恒为深色：它是一整套写死的外观，不跟随深浅设置
            Dim IsDarkMode As Boolean = Appearance = AppearanceStyle.Indra OrElse Settings.Get(Of Boolean)("DstDarkMode")

            '顶部条背景：高对比度与 Indra 外观都用纯色，PCL 用内置渐变
            If IsHighContrast Then
                '高对比度要的是边界清楚，渐变会被看成模糊，直接用纯黑
                FrmMain.PanTitle.Background = New SolidColorBrush(New MyColor(0, 0, 0))
                FrmMain.PanTitle.Background.Freeze()
            ElseIf Appearance = AppearanceStyle.Indra Then
                FrmMain.PanTitle.Background = New SolidColorBrush(New MyColor(11, 13, 16))
                FrmMain.PanTitle.Background.Freeze()
            Else
                Dim TitleLight As Integer = If(IsDarkMode, 20, 26)
                Dim TitleSat As Double = ColorSat * 0.5
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL(ColorHue + Deltas(0), TitleSat, TitleLight + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL(ColorHue + Deltas(1), TitleSat, TitleLight + 6 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL(ColorHue + Deltas(2), TitleSat, TitleLight + ColorLightAdjust)})
                FrmMain.PanTitle.Background = Brush
                FrmMain.PanTitle.Background.Freeze()
            End If
            '主页面背景：高对比度忽略「彩色背景」开关，彩色渐变会破坏对比度
            If IsHighContrast Then
                FrmMain.PanForm.Background = New MyColor(0, 0, 0)
            ElseIf Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                If IsDarkMode Then
                    Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL(ColorHue - 12, 22, 13)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL(ColorHue, 22, 16)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL(ColorHue + 12, 22, 13)})
                Else
                    '浅色：与 PCL 原版一致，用 FromHSL2 的低饱和度渐变，而不是压暗的低亮度
                    Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                End If
                FrmMain.PanForm.Background = Brush
            Else
                If IsDarkMode Then
                    '深色：内容区直接用最深层
                    FrmMain.PanForm.Background = New MyColor().FromHSL(ColorHue, 24, 12)
                Else
                    '浅色：原版用的就是固定灰白，不跟色相走
                    FrmMain.PanForm.Background = New MyColor(245, 245, 245)
                End If
            End If
            FrmMain.PanForm.Background.Freeze()
            '颜色刷新管不到非依赖属性的东西（图标、布局尺寸），需要它们跟随外观时，
            '在 ApplyAppearance 里主动推一次，不要指望 ThemeRefresh 顺手带上。
        End Sub)
    End Sub
    ''' <summary>
    ''' 主题 ID → 调色参数。
    ''' </summary>
    Private Sub ThemeApplyPalette(Id As Integer)
        ThemeDontClick = 0
        ColorHueTopbarDelta = 0
        ColorLightAdjust = 0
        Select Case Id
            Case 0 '龙猫蓝（默认，与 PCL 原版一致）
                'ColorLightAdjust 必须为 0，否则会把浅色主题整体压暗成偏暖的米色
                '顶栏不做色相偏移，保持原版那种纯蓝渐变
                ColorHue = 210 : ColorSat = 85
            Case 1 '甜柠青
                ColorHue = 168 : ColorSat = 78
            Case 2 '小草绿
                ColorHue = 128 : ColorSat = 62
            Case 3 '菠萝黄
                ColorHue = 45 : ColorSat = 88
            Case 4 '橡木棕
                ColorHue = 28 : ColorSat = 52
            Case 5 '玄素黑
                ColorHue = 220 : ColorSat = 0 : ColorLightAdjust = -16
            Case 6 '铁杆粉
                ColorHue = 339 : ColorSat = 82
            Case 7 '神秘紫
                ColorHue = 268 : ColorSat = 76
            Case 8 '秋仪金
                ColorHue = 41 : ColorSat = 62 : ColorLightAdjust = -6 : ColorHueTopbarDelta = 12
            Case 9 '活跃橙
                ColorHue = 22 : ColorSat = 92
            Case 10 '跳票红
                ColorHue = 358 : ColorSat = 84
            Case 11 '极客蓝
                ColorHue = 197 : ColorSat = 96 : ColorLightAdjust = -8
            Case 12 '滑稽彩，由主时钟 50ms 反复刷新
                ColorHue = RandomInteger(0, 359) : ColorSat = RandomInteger(70, 100)
                ThemeDontClick = 2
            Case 13 '欧皇彩
                ColorHue = RandomInteger(0, 359) : ColorSat = RandomInteger(55, 100)
            Case 14 '自定义
                '从设置项读，不读滑块控件：启动时设置页还没创建（FrmSetupUI 为 Nothing），
                '读控件会静默跳过，结果是自定义主题一重启就掉回默认蓝色。
                ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta")
                '亮度滑块是 0~40，中点是 20，代表「不做调整」。
                '直接把滑块值当偏移用，等于默认就把每个颜色都提亮 20，
                '饱和度拉满也发灰——必须减去中点才是不偏不倚的亮度。
                ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
            Case ThemeHighContrast
                '高对比度不用色相，整套颜色在 ApplyHighContrastPalette 里写死
                ColorHue = 0 : ColorSat = 0
            Case Else
                ColorHue = 210 : ColorSat = 85
        End Select
    End Sub
    ''' <summary>
    ''' 统一刷新主题的可用状态与当前配色。
    ''' EffectSetup 表示是否需要同步刷新设置页的外观（启动流程里为 False，避免过早访问设置页控件）。
    ''' 所有主题一律开放，因此这里只负责把控件状态与配色对齐。
    ''' </summary>
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        ApplyThemeAvailability()
        If EffectSetup AndAlso FrmSetupUI IsNot Nothing Then
            ThemeApplyPalette(Settings.Get(Of Integer)("UiLauncherTheme"))
        End If
    End Sub

    ''' <summary>各主题选项在 XAML 里定义的原始文字，用于还原被改写的显示名。</summary>
    Private ReadOnly _ThemeBaseText As New Dictionary(Of String, String)(StringComparer.Ordinal)

    ''' <summary>把所有主题选项标为可用，并把被改写的显示文字还原成 XAML 里定义的名字。</summary>
    Private Sub ApplyThemeAvailability()
        If FrmSetupUI Is Nothing OrElse FrmSetupUI.PanLauncherTheme Is Nothing Then Return
        For Each Control In FrmSetupUI.PanLauncherTheme.Children
            Dim Radio = TryCast(Control, MyRadioBox)
            If Radio Is Nothing Then Continue For
            Radio.IsEnabled = True
            Dim Key As String = If(Radio.Name, "")
            If Key = "" Then Continue For
            Dim BaseText As String = Nothing
            If _ThemeBaseText.TryGetValue(Key, BaseText) Then
                '已经记录过原文，直接还原（别处可能把文字改成了“真·滑稽彩”之类）
                If Radio.Text <> BaseText Then Radio.Text = BaseText
            Else
                _ThemeBaseText(Key) = Radio.Text
            End If
        Next
    End Sub

    ''' <summary>
    ''' 高对比度配色：纯黑打底、纯白文字、纯黄强调。
    ''' <para>
    ''' 这里把所有颜色键一次性写全，不依赖任何主题分支留下的值，
    ''' 也不参考深浅设置——它就是用来「强行覆盖」的。
    ''' </para>
    ''' <para>
    ''' 两点和常规主题不同，是为白字可读性专门处理的：
    ''' 主按钮的文字固定取 ColorBrushWhite，所以这里把它改成黑色，
    ''' 黄底黑字才读得清；启动按钮副标题同理。
    ''' </para>
    ''' </summary>
    Private Sub ApplyHighContrastPalette()
        Color1 = New MyColor(255, 255, 255)   '主文字：纯白
        Color2 = New MyColor(255, 215, 0)     '强调色（常态）：金黄
        Color3 = New MyColor(255, 255, 0)     '强调色（悬停）：纯黄
        Color4 = New MyColor(255, 255, 153)   '强调色（浅）
        Color5 = New MyColor(128, 128, 128)   '禁用态文字
        Color6 = New MyColor(0, 0, 0)
        Color7 = New MyColor(38, 38, 38)      '悬停面
        Color8 = New MyColor(0, 0, 0)         '主背景：纯黑
        ColorBg0 = New MyColor(0, 0, 0)
        ColorBg1 = New MyColor(0, 0, 0)

        SetColorBrush("ColorBrush1", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrush2", Color2)
        SetColorBrush("ColorBrush3", Color3)
        SetColorBrush("ColorBrush4", Color4)
        SetColorBrush("ColorBrush5", Color5)
        SetColorBrush("ColorBrush6", Color6)
        SetColorBrush("ColorBrush7", Color7)
        SetColorBrush("ColorBrush8", Color8)

        SetColorBrush("ColorBrushCard", New MyColor(0, 0, 0))
        SetColorBrush("ColorBrushCardHover", New MyColor(38, 38, 38))
        SetColorBrush("ColorBrushRail", New MyColor(0, 0, 0))
        SetColorBrush("ColorBrushBackgroundTransparentSidebar", New MyColor(255, 0, 0, 0))
        '灰阶只承担文字层级；边框一律纯白，这是高对比度的关键
        SetColorBrush("ColorBrushGray1", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrushGray2", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrushGray3", New MyColor(230, 230, 230))
        SetColorBrush("ColorBrushGray4", New MyColor(204, 204, 204))
        SetColorBrush("ColorBrushGray5", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrushGray6", New MyColor(51, 51, 51))
        SetColorBrush("ColorBrushGray7", New MyColor(38, 38, 38))
        SetColorBrush("ColorBrushGray8", New MyColor(26, 26, 26))
        SetColorBrush("ColorBrushBorder", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrushBorderLight", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrushCardBorder", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrushInput", New MyColor(0, 0, 0))
        SetColorBrush("ColorBrushWindowBorder", New MyColor(255, 255, 255))
        SetColorBrush("ColorBrushTitleIcon", New MyColor(255, 255, 255))
        '黄底按钮上的副标题：改成黑字
        SetColorBrush("ColorBrushLaunchSub", New MyColor(255, 0, 0, 0))
        SetColorBrush("ColorBrushHalfWhite", New MyColor(Color.FromArgb(51, 255, 255, 255)))
        SetColorBrush("ColorBrushSemiTransparent", New MyColor(1, Color8))
        SetColorBrush("ColorBrushWhite", New MyColor(0, 0, 0))

        BackColorMain = New MyColor(0, 0, 0)
        BackColorSub = New MyColor(0, 0, 0)
        TextColorMain = New MyColor(255, 255, 255)
        TextColorSub = New MyColor(255, 255, 255)
        '强调色底上的文字：这里强调色是纯黄，白字等于看不见，必须给黑
        TitleTextColor = New MyColor(0, 0, 0)
        BubbleOtherBack = New MyColor(0, 0, 0)
        BubbleOtherText = New MyColor(255, 255, 255)
        BubbleMineBack = New MyColor(255, 255, 0)
        BubbleMineText = New MyColor(0, 0, 0)
        BubbleMetaText = New MyColor(255, 255, 255)
    End Sub

    ''' <summary>主题是否可用。全部开放，但仍然走这套判断，方便以后按需收紧。</summary>
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        '0~14 是常规色相主题，15 是高对比度，16 是定制
        Return Id >= 0 AndAlso Id <= ThemeCustom
    End Function

    ''' <summary>
    ''' 尝试解锁主题。
    ''' 返回值表示“本次是否发生了新解锁”，调用方据此弹一次庆祝提示（如启动 99 次解锁铁杆粉、正版登录解锁跳票红）。
    ''' 所有主题默认可用，因此不存在“新解锁”这回事，恒返回 False；
    ''' 保留完整签名是为了不改动调用点。
    ''' </summary>
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function


End Module
