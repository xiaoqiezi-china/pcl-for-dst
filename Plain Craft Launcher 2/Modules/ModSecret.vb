'由于包含加解密等安全信息，本文件中的部分代码已被删除
Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "DST"
    '注册表根，同时也是本启动器的内部标识。
    '统一用 "PCL" 是为了兼容原版 PCL 的既有设置（主题、账号、路径等开机即生效）；
    '旧版曾用过 "DSTLauncher"，启动时会自动搬一次，见 Application.MigrateLegacyRegistry。
    Public Const RegFolder As String = "PCL"
#Region "账号与 API 凭据"

    '凭据分两层：
    '  1. 编译期内置值（Builtin*）：闭源版来自 ModSecret.Local.vb，开源版为空，也可用环境变量给；
    '  2. 用户在「设置 → 其他 → 账号与 API」里填写的值：存在 PCL\Setup.ini，填了就以它为准。
    '这样同一套代码既能做内置凭据的闭源版，也能做让用户自备凭据的开源版。

#If DST_LOCAL Then
    '闭源版：凭据来自 ModSecret.Local.vb（该文件不进仓库）
    Public Const BuiltinOAuthClientId As String = ModSecretLocal.ClientId
    Public Const BuiltinCurseForgeKey As String = ModSecretLocal.CurseForgeKey
#Else
    '开源版：默认不带任何凭据，可由使用者在环境变量或设置里提供
    Public ReadOnly BuiltinOAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "")
    Public ReadOnly BuiltinCurseForgeKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
#End If

    ''' <summary>设置里填写的 ClientId；空串表示「用内置默认值」。</summary>
    Friend Function MsClientIdUserValue() As String
        Return If(Settings.Get(Of String)("DstOAuthClientId"), "").Trim()
    End Function

    ''' <summary>
    ''' 实际使用的微软登录 ClientId：用户填了就用用户的，没填就用内置的，都没有返回空串。
    ''' 空串时不应发起正版登录，调用方需引导用户去设置里填写。
    ''' </summary>
    Public Function MsClientIdResolve() As String
        Dim User As String = MsClientIdUserValue()
        If User <> "" Then Return User
        Return If(BuiltinOAuthClientId, "").Trim()
    End Function

    ''' <summary>设置里填写的 CurseForge Key；空串表示「用内置默认值」。</summary>
    Friend Function CurseForgeKeyUserValue() As String
        Return If(Settings.Get(Of String)("DstCurseForgeKey"), "").Trim()
    End Function

    ''' <summary>
    ''' 实际使用的 CurseForge API Key：用户填了就用用户的，没填就用内置的，都没有返回空串。
    ''' 空串时搜索社区资源会失败，调用方需引导用户去设置里填写。
    ''' </summary>
    Public Function CurseForgeKeyResolve() As String
        Dim User As String = CurseForgeKeyUserValue()
        If User <> "" Then Return User
        Return If(BuiltinCurseForgeKey, "").Trim()
    End Function

    ''' <summary>当前是否已经具备可用的 CurseForge Key。</summary>
    Public ReadOnly Property HasCurseForgeKey As Boolean
        Get
            Return CurseForgeKeyResolve() <> ""
        End Get
    End Property

    ''' <summary>当前是否已经具备可用的微软登录 ClientId。</summary>
    Public ReadOnly Property HasMsClientId As Boolean
        Get
            Return MsClientIdResolve() <> ""
        End Get
    End Property

#End Region
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "机器人预设服务"

    '预设服务地址与令牌。本地开发取 ModSecret.Local.vb，发布版留空由使用者自行配置
#If DST_LOCAL Then
    Public Const DstBotPresetApi As String = ModSecretLocal.PresetApi
    Public Const DstBotPresetToken As String = ModSecretLocal.PresetToken
#Else
    Public Const DstBotPresetApi As String = ""
    Public Const DstBotPresetToken As String = ""
#End If

#End Region

#Region "网络鉴权"

    Public Const RestrictMcDownload As Boolean = False
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeKeyResolve())
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
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

    ''' <summary>界面当前实际生效的深浅模式（不是设置项里的值）。</summary>
    Public Property AppliedDarkMode As Boolean = False

    ''' <summary>界面当前实际生效的主题编号（不是设置项里的值）。</summary>
    Public Property AppliedTheme As Integer = -1

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
    ''' 主题编号改变时的回调。
    ''' <para>
    ''' 整套配色都要重算，运行中刷新容易出现半新半旧的状态，
    ''' 统一走"改设置 → 重启"，反而更快也更稳。
    ''' </para>
    ''' </summary>
    Public Sub ThemeChangedNeedsRestart()
        ConfigUtils.SaveAll()
        '与界面实际生效的主题编号比较，选回原样就不必重启
        If Settings.Get(Of Integer)("UiLauncherTheme") = AppliedTheme Then Return
        RunInUi(Sub() ShowRestartHint("已切换主题配色。"))
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
                AppliedTheme = NewTheme
                ThemeApplyPalette(NewTheme)
            End If

            '深浅两套配色：1 = 主文字，2~4 = 强调色，5~8 = 由浅到深的面
            Dim Shift As Double = ColorLightAdjust * 0.4
            Dim Dark As Boolean = Settings.Get(Of Boolean)("DstDarkMode")
            AppliedDarkMode = Dark
            '外观风格：PCL 用原本的深浅主题，Indra 用写死的现代简约深色配色。
            '两套是互斥的整套外观，不是颜色覆盖，所以在这里一次算清。
            If Appearance = AppearanceStyle.Indra Then
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
                Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
                Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
                Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
                Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
                Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
                Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
                Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
                Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
                ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
                ColorBg1 = New MyColor(190, Color7)

                '浅色一律用原版的固定灰阶，不跟着主题色相走
                SetColorBrush("ColorBrushCard", New MyColor(255, 255, 255))
                SetColorBrush("ColorBrushCardHover", New MyColor(250, 250, 250))
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
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)


            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    ''' <summary>
    ''' 把 "#RRGGBB" / "#AARRGGBB" 解析成颜色。失败返回 False，不抛异常。
    ''' 皮肤文件里的颜色是用户手写的，必须容忍错字。
    ''' </summary>
    Public Function TryParseColorString(Value As String, ByRef Result As Color) As Boolean
        If String.IsNullOrWhiteSpace(Value) Then Return False
        Try
            Result = CType(ColorConverter.ConvertFromString(Value.Trim()), Color)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '这两个变量后面“主页面背景”也要用，必须声明在 If 之外
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            'Indra 外观恒为深色：它是一整套写死的外观，不跟随深浅设置
            Dim IsDarkMode As Boolean = Appearance = AppearanceStyle.Indra OrElse Settings.Get(Of Boolean)("DstDarkMode")

            '顶部条背景：Indra 外观用纯色，PCL 用内置渐变
            If Appearance = AppearanceStyle.Indra Then
                FrmMain.PanTitle.Background = New SolidColorBrush(New MyColor(11, 13, 16))
                FrmMain.PanTitle.Background.Freeze()
            Else
            '顶栏始终深色：里面的导航按钮、图标都按白字设计



            Dim TitleLight As Integer = If(IsDarkMode, 20, 26)
            Dim TitleSat As Double = ColorSat * 0.5
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL(ColorHue + Deltas(0), TitleSat, TitleLight + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL(ColorHue + Deltas(1), TitleSat, TitleLight + 6 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL(ColorHue + Deltas(2), TitleSat, TitleLight + ColorLightAdjust)})
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            End If
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
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
            '图标与布局都不是依赖属性，颜色刷新管不到它们，必须在这里主动推一次。
            '顺序有讲究：先套插槽布局（它决定导航横排还是竖排），
            '再推导航朝向与图标，最后建自定义控件。
            '已建好的控件不会自己重读度量：启动时控件的 Loaded 早于皮肤覆盖，
            '那时 ButtonFilled 还是默认值，按钮会画成描边。这里统一重推一次。
        End Sub)
    End Sub
    ''' <summary>
    ''' 定制版：主题 ID → 调色参数。开源版里这段映射原本被删掉了，这里补回。
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
                If FrmSetupUI IsNot Nothing Then
                    ColorHue = CInt(FrmSetupUI.SliderLauncherHue.Value)
                    ColorSat = CInt(FrmSetupUI.SliderLauncherSat.Value)
                    ColorHueTopbarDelta = CInt(FrmSetupUI.SliderLauncherDelta.Value)
                    ColorLightAdjust = CInt(FrmSetupUI.SliderLauncherLight.Value)
                End If
            Case Else
                ColorHue = 210 : ColorSat = 85
        End Select
    End Sub
    ''' <summary>
    ''' 统一刷新主题的可用状态与当前配色。
    ''' EffectSetup 表示是否需要同步刷新设置页的外观（启动流程里为 False，避免过早访问设置页控件）。
    ''' 定制版：所有主题一律开放，因此这里只负责把控件状态与配色对齐。
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

    ''' <summary>主题是否可用。定制版全部开放，但仍然走这套判断，方便以后按需收紧。</summary>
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        '0~14 都是合法主题编号，其余视为无效
        Return Id >= 0 AndAlso Id <= 14
    End Function

    ''' <summary>
    ''' 尝试解锁主题。
    ''' 返回值表示“本次是否发生了新解锁”，调用方据此弹一次庆祝提示（如启动 99 次解锁铁杆粉、正版登录解锁跳票红）。
    ''' 定制版所有主题默认可用，因此不存在“新解锁”这回事，恒返回 False；
    ''' 保留完整签名是为了让上述调用点的代码与上游保持一致，不必改动它们。
    ''' </summary>
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        DstUpdate.CheckLauncherUpdateByButton()
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    ''' <summary>
    ''' 由新版本进程带着 --update 调用：等旧进程退出后替换主程序，必要时拉起新版本。
    ''' NewFileName 可以是单个 exe（单程序更新），也可以是同目录下带依赖的整包。
    ''' </summary>
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Dim Bak As String = OldFileName & ".old"
        Try
            '1. 等旧进程退出
            Dim Old As Process = Nothing
            Try
                Old = Process.GetProcessById(ProcessId)
            Catch ex As Exception
            End Try
            If Old IsNot Nothing Then
                Try
                    Old.WaitForExit(60000)
                Catch ex As Exception
                End Try
            End If
            Thread.Sleep(300)

            Dim TargetDir As String = PathUtils.RemoveLastPart(OldFileName)
            Dim SourceDir As String = PathUtils.RemoveLastPart(NewFileName)
            Logger.Info($"开始替换启动器：{NewFileName} → {OldFileName}")

            '替换前确认两个路径都合法：目标必须是 exe，源文件必须存在且不是自己
            If Not OldFileName.EndsWithF(".exe", ignoreCase:=True) OrElse Not NewFileName.EndsWithF(".exe", ignoreCase:=True) Then
                Throw New Exception($"更新文件名不合法，已中止：{OldFileName} / {NewFileName}")
            End If
            If Not FileUtils.Exists(NewFileName) Then Throw New Exception($"新版本文件不存在，已中止：{NewFileName}")
            If String.Equals(IO.Path.GetFullPath(NewFileName), IO.Path.GetFullPath(OldFileName), StringComparison.OrdinalIgnoreCase) Then
                Throw New Exception("新旧文件是同一个文件，已中止替换")
            End If

            '2. 旧 exe 先改名让位，否则文件占用写不进去
            Try
                If FileUtils.Exists(Bak) Then FileUtils.Delete(Bak)
            Catch ex As Exception
            End Try
            If FileUtils.Exists(OldFileName) Then IO.File.Move(OldFileName, Bak)

            '3. 先拷到同目录的临时文件，校验通过后再整体替换，避免写到一半留下残缺的主程序
            Dim Staging As String = OldFileName & ".new"
            Try
                If FileUtils.Exists(Staging) Then FileUtils.Delete(Staging)
                IO.File.Copy(NewFileName, Staging, True)
                If New IO.FileInfo(Staging).Length <> New IO.FileInfo(NewFileName).Length Then
                    Throw New Exception("新文件拷贝不完整")
                End If
                IO.File.Move(Staging, OldFileName)
            Catch ex As Exception
                '临时文件失败时清掉，保持目标目录干净
                Try
                    If FileUtils.Exists(Staging) Then FileUtils.Delete(Staging)
                Catch ex2 As Exception
                End Try
                Throw
            End Try
            Logger.Info("主程序已替换")

            '配置文件跟着改名：.NET 只认 <exe名>.config，名字对不上就不会生效
            Dim SrcConfig As String = NewFileName & ".config"
            If FileUtils.Exists(SrcConfig) Then
                Try
                    IO.File.Copy(SrcConfig, OldFileName & ".config", True)
                    Logger.Info("配置文件已同步")
                Catch ex As Exception
                    Logger.Warn(ex, "同步配置文件失败")
                End Try
            End If

            '4. 新版本目录里若还带了别的依赖文件，一并覆盖（跳过用户数据与新主程序自身）
            Dim SkipDirs As String() = {"PCL", ".minecraft", "PCLData"}
            Dim Count As Integer = 0
            For Each FilePath In DirectoryUtils.EnumerateFiles(SourceDir, True)
                If String.Equals(FilePath, NewFileName, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim Rel As String = FilePath.Substring(SourceDir.Length).TrimStart("\"c, "/"c)
                Dim Top As String = Rel.Split("\"c, "/"c)(0)
                If SkipDirs.Contains(Top, StringComparer.OrdinalIgnoreCase) Then Continue For
                Dim Dest As String = IO.Path.Combine(TargetDir, Rel)
                Try
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(Dest))
                    IO.File.Copy(FilePath, Dest, True)
                    Count += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"覆盖文件失败：{Rel}")
                End Try
            Next
            If Count > 0 Then Logger.Info($"附带覆盖了 {Count} 个依赖文件")

            '5. 启动新版本、清理备份
            Try
                FileUtils.Delete(Bak)
            Catch ex As Exception
            End Try
            '只有替换确实成功后才会走到这里，此时才拉起新版本
            If TriggerRestart Then StartLauncherProcess(OldFileName)
        Catch ex As Exception
            Logger.Error(ex, "替换启动器文件失败")
            '失败时把旧文件还回去，别把程序弄坏
            Dim Restored As Boolean = False
            Try
                If FileUtils.Exists(Bak) Then
                    If FileUtils.Exists(OldFileName) Then FileUtils.Delete(OldFileName)
                    IO.File.Move(Bak, OldFileName)
                End If
                Restored = FileUtils.Exists(OldFileName)
            Catch ex2 As Exception
                Logger.Error(ex2, "回滚启动器文件失败")
            End Try
            '只有确认旧文件已经回来才重新启动，否则宁可不启动也不要拉起一个残缺的程序
            If TriggerRestart AndAlso Restored Then StartLauncherProcess(OldFileName)
        End Try
    End Sub
    ''' <summary>启动指定的启动器进程；失败只记日志，不再向外抛。</summary>
    Private Sub StartLauncherProcess(ExePath As String)
        Try
            '走 PCL 的统一入口，顺带处理长路径
            Dim P As Process = StartProcess(New ProcessStartInfo With {
                .FileName = ExePath,
                .WorkingDirectory = PathUtils.RemoveLastPart(ExePath),
                .UseShellExecute = True})
            P?.Dispose()
        Catch ex As Exception
            Logger.Warn(ex, $"拉起启动器失败：{ExePath}")
        End Try
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
