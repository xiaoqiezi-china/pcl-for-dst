Imports System.IO
Imports System.IO.Compression
Imports System.Windows.Media

''' <summary>
''' 皮肤管理器：扫描、加载、应用、导入、导出、删除皮肤包。
''' <para>
''' 与 PCL 现有主题系统的关系：ThemeRefresh 会按当前主题色算出一整套
''' ColorBrush* 资源；本模块在它之后运行，只覆盖皮肤显式声明的键。
''' 因此皮肤是“叠加层”：未声明的键仍完全遵循用户的主题色设置，
''' 不存在两套配色互相打架的问题。
''' </para>
''' </summary>
Public Module SkinManager

    ''' <summary>皮肤根目录。</summary>
    Public ReadOnly Property SkinRoot As String
        Get
            Return Paths.Base & "PCL\Skins\"
        End Get
    End Property

    ''' <summary>当前启用的皮肤 Id；空表示不启用皮肤包。</summary>
    Public ReadOnly Property CurrentId As String
        Get
            '用 Try 兜一层：设置项万一没注册、或配置文件损坏，读它会抛异常。
            '这个属性被 ThemeRefresh 调用，一旦抛出去整个主题刷新会中断，
            '表现就是“只换了一半颜色”并弹红字。换皮肤失败不该拖垮整个界面。
            Try
                Return Settings.Get(Of String)("DstSkinId").Trim()
            Catch ex As Exception
                Logger.Warn(ex, "读取当前皮肤失败")
                Return ""
            End Try
        End Get
    End Property

    ''' <summary>上一次操作的错误说明；Nothing 表示没有错误。</summary>
    Public Property ErrorText As String = Nothing

    Private CachedPacks As List(Of SkinPack) = Nothing
    Private ReadOnly CacheLock As New Object

    ''' <summary>扫描全部皮肤包，结果按名称排序并缓存。</summary>
    Public Function ListPacks(Optional Force As Boolean = False) As List(Of SkinPack)
        SyncLock CacheLock
            If CachedPacks IsNot Nothing AndAlso Not Force Then Return CachedPacks.ToList()
        End SyncLock
        Dim Result As New List(Of SkinPack)
        Try
            DirectoryUtils.Create(SkinRoot)
            For Each DirPath In Directory.GetDirectories(SkinRoot)
                Dim Pack = LoadFromFolder(DirPath)
                If Pack IsNot Nothing Then Result.Add(Pack)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描皮肤目录失败")
        End Try
        Result = Result.OrderBy(Function(P) P.Name).ToList()
        SyncLock CacheLock
            CachedPacks = Result
        End SyncLock
        Return Result.ToList()
    End Function

    ''' <summary>按 Id 取皮肤包；找不到返回 Nothing。</summary>
    Public Function FindById(Id As String) As SkinPack
        If String.IsNullOrWhiteSpace(Id) Then Return Nothing
        Return ListPacks().FirstOrDefault(Function(P) String.Equals(P.Id, Id, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>当前生效的皮肤包对象；未启用或找不到则为 Nothing。</summary>
    Public ReadOnly Property CurrentPack As SkinPack
        Get
            Return FindById(CurrentId)
        End Get
    End Property

    '-----------------
    ' 读取
    '-----------------

    ''' <summary>从一个目录读取皮肤包；skin.json 缺失或非法时返回 Nothing。</summary>
    Public Function LoadFromFolder(Folder As String) As SkinPack
        Try
            Dim JsonPath = IO.Path.Combine(Folder, "skin.json")
            If Not FileUtils.Exists(JsonPath) Then Return Nothing
            Dim Raw As String = FileUtils.TryReadAsString(JsonPath)
            If String.IsNullOrWhiteSpace(Raw) Then Return Nothing
            Dim Json As JObject = JObject.Parse(Raw)

            Dim Pack As New SkinPack With {
                .Id = IO.Path.GetFileName(Folder.TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)),
                .Name = Json.Value(Of String)("name"),
                .Author = Json.Value(Of String)("author"),
                .Version = Json.Value(Of String)("version"),
                .Description = Json.Value(Of String)("description"),
                .Base = Json.Value(Of String)("base"),
                .FolderPath = Folder
            }
            If String.IsNullOrWhiteSpace(Pack.Name) Then Pack.Name = Pack.Id
            If String.IsNullOrWhiteSpace(Pack.Version) Then Pack.Version = "1.0"
            If String.IsNullOrWhiteSpace(Pack.Base) Then Pack.Base = "auto"

            '颜色：只接受能解析成颜色的值，非法项直接忽略，别让一个错字毁掉整个皮肤
            Dim ColorsNode = Json("colors")
            If ColorsNode IsNot Nothing AndAlso ColorsNode.Type = JTokenType.Object Then
                For Each Prop As JProperty In CType(ColorsNode, JObject).Properties()
                    Dim Value As String = If(Prop.Value, "").ToString().Trim()
                    Dim Dummy As Color
                    If Value <> "" AndAlso TryParseColor(Value, Dummy) Then Pack.Colors(Prop.Name) = Value
                Next
            End If

            '度量：数字进 Metrics，字符串进 StringMetrics，互不干扰
            Dim MetricsNode = Json("metrics")
            If MetricsNode IsNot Nothing AndAlso MetricsNode.Type = JTokenType.Object Then
                For Each Prop As JProperty In CType(MetricsNode, JObject).Properties()
                    Dim MetricText As String = If(Prop.Value, "").ToString().Trim()
                    Dim D As Double
                    If Double.TryParse(MetricText, D) Then
                        Pack.Metrics(Prop.Name) = D
                    ElseIf MetricText <> "" Then
                        Pack.StringMetrics(Prop.Name) = MetricText.Trim(""""c)
                    End If
                Next
            End If

            '图标覆盖：键是语义名，值是几何路径
            Dim IconsNode = Json("icons")
            If IconsNode IsNot Nothing AndAlso IconsNode.Type = JTokenType.Object Then
                For Each Prop As JProperty In CType(IconsNode, JObject).Properties()
                    Dim Value As String = If(Prop.Value, "").ToString().Trim()
                    '只接受看起来像几何路径的值（以字母开头、含数字或逗号），
                    '避免把随便一段文本塞进 GeometryConverter 直接抛异常
                    If Value.Length > 8 AndAlso Char.IsLetter(Value(0)) Then Pack.Icons(Prop.Name) = Value
                Next
            End If

            '自定义界面控件树
            Dim UiNode = Json("customUi")
            If UiNode IsNot Nothing AndAlso UiNode.Type = JTokenType.Array Then
                Pack.CustomUi = CType(UiNode, JArray)
            End If

            '整窗布局文件
            Pack.LayoutFile = If(Json.Value(Of String)("layout"), "").Trim()

            '预览图
            For Each Candidate In {"preview.png", "preview.jpg", "preview.jpeg"}
                Dim P = IO.Path.Combine(Folder, Candidate)
                If FileUtils.Exists(P) Then Pack.PreviewPath = P : Exit For
            Next
            Return Pack
        Catch ex As Exception
            Logger.Warn(ex, "读取皮肤包失败：" & Folder)
            Return Nothing
        End Try
    End Function

    ''' <summary>把颜色字符串解析成 WPF 颜色；失败返回 False。</summary>
    Private Function TryParseColor(Value As String, ByRef Result As Color) As Boolean
        Try
            Result = CType(ColorConverter.ConvertFromString(Value), Color)
            Return True
        Catch
            Return False
        End Try
    End Function

    '-----------------
    ' 应用
    '-----------------

    ''' <summary>
    ''' 把当前皮肤的覆盖层应用到已经算好的主题资源上。
    ''' 必须在 ThemeRefresh 写完基础配色之后调用，否则会被它覆盖掉。
    ''' </summary>
    Public Sub ApplyOverlay()
        '先恢复默认度量，避免上一个皮肤的几何设置残留
        SkinMetrics.Reset()
        Dim Pack = CurrentPack
        Logger.Info($"应用皮肤：{CurrentId}（{Pack.Name}）")
        If Pack Is Nothing Then Return
        If Not Pack.AppliesNow Then Return
        Try
            For Each Pair In Pack.Colors
                Dim Parsed As Color
                If TryParseColor(Pair.Value, Parsed) Then
                    Application.Current.Resources(Pair.Key) = New SolidColorBrush(Parsed)
                End If
            Next
            For Each Pair In Pack.Metrics
                Select Case Pair.Key
                    '圆角
                    Case "RadiusCard" : SkinMetrics.RadiusCard = Pair.Value
                    Case "RadiusButton" : SkinMetrics.RadiusButton = Pair.Value
                    Case "RadiusInput" : SkinMetrics.RadiusInput = Pair.Value
                    Case "RadiusItem" : SkinMetrics.RadiusItem = Pair.Value
                    Case "RadiusHint" : SkinMetrics.RadiusHint = Pair.Value
                    '描边与阴影
                    Case "CardBorderThickness" : SkinMetrics.CardBorderThickness = Pair.Value
                    Case "CardShadowOpacity" : SkinMetrics.CardShadowOpacity = Pair.Value
                    '按钮
                    Case "ButtonBorderThickness" : SkinMetrics.ButtonBorderThickness = Pair.Value
                    Case "ButtonFilled" : SkinMetrics.ButtonFilled = Pair.Value > 0.5
                    '卡片内边距与标题
                    Case "CardPaddingLeft" : SkinMetrics.CardPaddingLeft = Pair.Value
                    Case "CardPaddingTop" : SkinMetrics.CardPaddingTop = Pair.Value
                    Case "CardPaddingRight" : SkinMetrics.CardPaddingRight = Pair.Value
                    Case "CardPaddingBottom" : SkinMetrics.CardPaddingBottom = Pair.Value
                    Case "CardTitleFontSize" : SkinMetrics.CardTitleFontSize = Pair.Value
                    '标题栏
                    Case "TitlebarSolid" : SkinMetrics.TitlebarSolid = Pair.Value > 0.5
                    '布局
                    Case "NavButtonSize" : SkinMetrics.NavButtonSize = Pair.Value
                Case "NavButtonRadius" : SkinMetrics.NavButtonRadius = Pair.Value
                Case "NavIconSize" : SkinMetrics.NavIconSize = Pair.Value
                Case "NavFontSize" : SkinMetrics.NavFontSize = Pair.Value
                Case "NavSpacing" : SkinMetrics.NavSpacing = Pair.Value
                    Case "TitlebarHeight" : SkinMetrics.TitlebarHeight = Pair.Value
                    Case "HideTitleLogo" : SkinMetrics.HideTitleLogo = Pair.Value > 0.5
                End Select
            Next

            '字符串型度量（颜色键名等）已在上面单独收集
            For Each Pair In Pack.StringMetrics
                Select Case Pair.Key
                    Case "CardBorderBrushKey" : SkinMetrics.CardBorderBrushKey = Pair.Value
                        Case "TitlebarColor" : SkinMetrics.TitlebarColor = Pair.Value
                    Case "ButtonFilledTextColor" : SkinMetrics.ButtonFilledTextColor = Pair.Value
                    Case "NavVertical" : SkinMetrics.NavVertical = Pair.Value.Trim().ToLower() = "true" OrElse Pair.Value.Trim() = "1"
                    Case "TitleText" : SkinMetrics.TitleText = Pair.Value
                End Select
            Next

            '图标覆盖
            SkinMetrics.Icons.Clear()
            For Each Pair In Pack.Icons
                SkinMetrics.Icons(Pair.Key) = Pair.Value
            Next
        Catch ex As Exception
            Logger.Warn(ex, "应用皮肤失败：" & Pack.Id)
        End Try
    End Sub

    ''' <summary>切换皮肤并立即刷新界面。Id 为空表示关闭皮肤包。</summary>
    Public Sub SwitchTo(Id As String)
        Settings.Set("DstSkinId", If(Id, "").Trim())
        'ThemeRefresh 内部会调 ApplyOverlay，颜色与度量都在那里更新。
        '必须传 Force：主题编号没变，不强制的话会被短接直接返回，皮肤根本不生效。
        ThemeRefresh(Force:=True)
        '已有控件不会自己重读度量（页面实例是复用的），主动推一遍
        RefreshExistingControls()
    End Sub

    ''' <summary>
    ''' 把最新的皮肤度量推给界面上已经存在的控件。
    ''' <para>
    ''' 不能靠重建页面：PCL 的页面实例是缓存的，PageChange 到当前页会直接 return
    ''' （PageChangeActual 第一行就是同名即返回）。所以只能主动遍历视觉树，
    ''' 把度量重新写进已建好的卡片与按钮。
    ''' </para>
    ''' </summary>
    Public Sub RefreshExistingControls()
        Try
            If FrmMain Is Nothing Then Return
            WalkVisualTree(FrmMain)
        Catch ex As Exception
            Logger.Warn(ex, "刷新已有控件的皮肤度量失败")
        End Try
    End Sub

    ''' <summary>递归遍历视觉树，把度量推给每个 MyCard / MyButton。</summary>
    Private Sub WalkVisualTree(Node As DependencyObject)
        If Node Is Nothing Then Return
        Try
            Select Case True
                Case TypeOf Node Is MyCard
                    CType(Node, MyCard).ApplySkinMetrics()
                Case TypeOf Node Is MyButton
                    CType(Node, MyButton).ApplySkinMetrics()
            End Select
        Catch ex As Exception
            '单个控件失败不该中断整棵树
            Logger.Warn(ex, "套用皮肤度量失败")
        End Try
        Try
            Dim Count As Integer = VisualTreeHelper.GetChildrenCount(Node)
            For i As Integer = 0 To Count - 1
                WalkVisualTree(VisualTreeHelper.GetChild(Node, i))
            Next
        Catch
            '不是 Visual / 已卸载，跳过
        End Try
    End Sub'-----------------
    ' 导入 / 导出
    '-----------------

    ''' <summary>
    ''' 新建皮肤用的默认布局。
    ''' <para>
    ''' 刻意做成"和 PCL 原界面等价"的最小骨架：标题栏在上、内容区在下。
    ''' 皮肤作者从这份开始改，改坏了也能一眼看出差在哪。
    ''' </para>
    ''' </summary>
    Private ReadOnly Property TemplateLayout As String
        Get
            Return <![CDATA[<!--
  皮肤布局：PCL 会把真实控件搬进这些插槽，所以你只需要安排它们的位置。
  可用插槽：
    Title     标题栏（Logo、页面切换、窗口按钮、版本号）
    Nav       导航按钮组
    Content   页面内容区（必需）
    Extra     右下角扩展按钮
    Hint      底部提示条
    Progress  底部进度条
  写法：在任意容器上加 PCL:Slot="插槽名"。
  不要在文件里写 xmlns，PCL 会自动注入。
-->
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="1*" />
    </Grid.RowDefinitions>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="68" />
        <ColumnDefinition Width="1*" />
    </Grid.ColumnDefinitions>

    <!-- 左栏：导航 -->
    <Border Grid.RowSpan="2" Background="{DynamicResource ColorBrushRail}">
        <Border PCL:Slot="Nav" VerticalAlignment="Top" />
    </Border>

    <!-- 标题栏 -->
    <Border Grid.Column="1" Grid.Row="0" PCL:Slot="Title" />

    <!-- 内容区 -->
    <Grid Grid.Column="1" Grid.Row="1">
        <Border PCL:Slot="Content" />
        <ContentControl PCL:Slot="Hint" VerticalAlignment="Bottom" HorizontalAlignment="Left" />
        <ContentControl PCL:Slot="Progress" VerticalAlignment="Bottom" HorizontalAlignment="Left" />
        <ContentControl PCL:Slot="Extra" VerticalAlignment="Bottom" HorizontalAlignment="Right" />
    </Grid>
</Grid>]]>.Value
        End Get
    End Property

    ''' <summary>从 zip 或 skin.json 导入皮肤。失败返回 Nothing 并写 ErrorText。</summary>
    Public Function Import(FilePath As String) As SkinPack
        ErrorText = Nothing
        Try
            If Not FileUtils.Exists(FilePath) Then
                ErrorText = "文件不存在"
                Return Nothing
            End If
            Dim Ext = IO.Path.GetExtension(FilePath).ToLower()
            DirectoryUtils.Create(SkinRoot)
            Dim Target As String

            If Ext = ".zip" Then
                '解压到临时目录，校验通过后再搬进皮肤目录，避免半成品留在那里
                Dim TempDir = PathTemp & "SkinImport_" & Guid.NewGuid().ToString("N")
                DirectoryUtils.Create(TempDir)
                Try
                    Using Archive As ZipArchive = ZipFile.OpenRead(FilePath)
                        For Each Entry As ZipArchiveEntry In Archive.Entries
                            If String.IsNullOrWhiteSpace(Entry.Name) Then Continue For
                            '只允许普通文件，防止 zip 里的 .. 跳出目标目录
                            Dim Dest = IO.Path.GetFullPath(IO.Path.Combine(TempDir, Entry.FullName))
                            If Not Dest.StartsWith(IO.Path.GetFullPath(TempDir), StringComparison.OrdinalIgnoreCase) Then Continue For
                            DirectoryUtils.Create(IO.Path.GetDirectoryName(Dest))
                            Entry.ExtractToFile(Dest, True)
                        Next
                    End Using
                    '允许 zip 里再套一层目录
                    Dim JsonPath = FindSkinJson(TempDir)
                    If JsonPath Is Nothing Then
                        ErrorText = "压缩包里没有找到 skin.json"
                        Return Nothing
                    End If
                    Dim Pack = LoadFromFolder(IO.Path.GetDirectoryName(JsonPath))
                    If Pack Is Nothing Then
                        ErrorText = "skin.json 格式不正确"
                        Return Nothing
                    End If
                    Target = IO.Path.Combine(SkinRoot, SafeFolderName(Pack.Name, Pack.Id))
                    If DirectoryUtils.Exists(Target) Then Target &= "_" & Date.Now.ToString("HHmmss")
                    CopyFolder(IO.Path.GetDirectoryName(JsonPath), Target)
                Finally
                    Try
                        Directory.Delete(TempDir, True)
                    Catch
                    End Try
                End Try
            ElseIf Ext = ".json" Then
                '直接给 skin.json：把整个所在目录当作皮肤包
                Dim Pack = LoadFromFolder(IO.Path.GetDirectoryName(FilePath))
                If Pack Is Nothing Then
                    ErrorText = "skin.json 格式不正确"
                    Return Nothing
                End If
                Target = IO.Path.Combine(SkinRoot, SafeFolderName(Pack.Name, Pack.Id))
                If DirectoryUtils.Exists(Target) Then Target &= "_" & Date.Now.ToString("HHmmss")
                CopyFolder(IO.Path.GetDirectoryName(FilePath), Target)
            Else
                ErrorText = "只支持 .zip 或 .json"
                Return Nothing
            End If

            ListPacks(True) '刷新缓存
            Dim Installed = LoadFromFolder(Target)
            If Installed Is Nothing Then
                ErrorText = "导入后无法读取，可能缺少必要文件"
                Return Nothing
            End If
            Return Installed
        Catch ex As Exception
            Logger.Warn(ex, "导入皮肤失败")
            ErrorText = FriendlyError(ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>在目录里递归找 skin.json（最多往下两层，够覆盖 zip 里套一层文件夹）。</summary>
    Private Function FindSkinJson(Root As String) As String
        Dim Direct = IO.Path.Combine(Root, "skin.json")
        If FileUtils.Exists(Direct) Then Return Direct
        Try
            For Each Sub1 In Directory.GetDirectories(Root)
                Dim One = IO.Path.Combine(Sub1, "skin.json")
                If FileUtils.Exists(One) Then Return One
                For Each Sub2 In Directory.GetDirectories(Sub1)
                    Dim Two = IO.Path.Combine(Sub2, "skin.json")
                    If FileUtils.Exists(Two) Then Return Two
                Next
            Next
        Catch
        End Try
        Return Nothing
    End Function

    ''' <summary>把皮肤导出成 zip 文件。</summary>
    Public Function Export(Pack As SkinPack, TargetZip As String) As Boolean
        ErrorText = Nothing
        Try
            If Pack Is Nothing OrElse Pack.FolderPath = "" Then
                ErrorText = "皮肤目录不可用"
                Return False
            End If
            If FileUtils.Exists(TargetZip) Then FileUtils.Delete(TargetZip)
            ZipFile.CreateFromDirectory(Pack.FolderPath, TargetZip, CompressionLevel.Optimal, False)
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "导出皮肤失败")
            ErrorText = FriendlyError(ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>删除一个皮肤包；内置皮肤不允许删除。</summary>
    Public Function Remove(Pack As SkinPack) As Boolean
        ErrorText = Nothing
        If Pack Is Nothing Then Return False
        If Pack.IsBuiltIn Then
            ErrorText = "内置皮肤不能删除"
            Return False
        End If
        Try
            If String.Equals(CurrentId, Pack.Id, StringComparison.OrdinalIgnoreCase) Then Settings.Set("DstSkinId", "")
            If DirectoryUtils.Exists(Pack.FolderPath) Then Directory.Delete(Pack.FolderPath, True)
            ListPacks(True)
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "删除皮肤失败")
            ErrorText = FriendlyError(ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>把名称转成安全的目录名。</summary>
    Private Function SafeFolderName(Name As String, Fallback As String) As String
        Dim Result As String = If(String.IsNullOrWhiteSpace(Name), Fallback, Name)
        For Each Bad As Char In IO.Path.GetInvalidFileNameChars()
            Result = Result.Replace(Bad, "_"c)
        Next
        Result = Result.Trim()
        If Result = "" Then Result = "skin"
        Return Result
    End Function

    ''' <summary>递归复制目录。</summary>
    Private Sub CopyFolder(Source As String, Target As String)
        DirectoryUtils.Create(Target)
        For Each FilePath In Directory.GetFiles(Source)
            FileUtils.Copy(FilePath, IO.Path.Combine(Target, IO.Path.GetFileName(FilePath)))
        Next
        For Each DirPath In Directory.GetDirectories(Source)
            CopyFolder(DirPath, IO.Path.Combine(Target, IO.Path.GetFileName(DirPath)))
        Next
    End Sub

    ''' <summary>打开皮肤根目录，方便作者直接放文件。</summary>
    Public Sub OpenFolder()
        Try
            DirectoryUtils.Create(SkinRoot)
            Process.Start("explorer.exe", SkinRoot)
        Catch ex As Exception
            Logger.Warn(ex, "打开皮肤目录失败")
            Hint("打开皮肤目录失败：" & FriendlyError(ex.Message), HintType.Red)
        End Try
    End Sub

    ''' <summary>在皮肤目录里生成一份可修改的模板。</summary>
    Public Function CreateTemplate() As Boolean
        ErrorText = Nothing
        Try
            Dim Target As String = IO.Path.Combine(SkinRoot, "my-skin")
            If DirectoryUtils.Exists(Target) Then Target &= "_" & Date.Now.ToString("HHmmss")
            DirectoryUtils.Create(Target)
            '模板把能改的项都列出来，作者照着自己改即可，不用去翻源码找字段名
            Dim Json As New JObject From {
                {"name", "我的皮肤"},
                {"author", "你的名字"},
                {"version", "1.0"},
                {"description", "在这里写皮肤说明"},
                {"base", "auto"},
                {"layout", "Layout.xaml"},
                {"colors", New JObject From {
                    {"ColorBrush1", "#333D4A"},
                    {"ColorBrush2", "#0B5BCB"},
                    {"ColorBrush3", "#1370F3"},
                    {"ColorBrush4", "#4890F5"},
                    {"ColorBrushCard", "#FFFFFF"},
                    {"ColorBrushCardHover", "#FAFAFA"},
                    {"ColorBrushBackgroundTransparentSidebar", "#EBF5F5F5"},
                    {"ColorBrushGray1", "#404040"},
                    {"ColorBrushGray2", "#737373"},
                    {"ColorBrushGray5", "#CCCCCC"},
                    {"ColorBrushGray6", "#EBEBEB"}}},
                {"metrics", New JObject From {
                    {"RadiusCard", 5},
                    {"RadiusButton", 3},
                    {"RadiusItem", 6},
                    {"RadiusHint", 3},
                    {"CardBorderThickness", 0},
                    {"CardBorderBrushKey", "ColorBrushGray6"},
                    {"CardShadowOpacity", 0.07},
                    {"CardPaddingLeft", 25},
                    {"CardPaddingTop", 40},
                    {"CardPaddingRight", 25},
                    {"CardPaddingBottom", 20},
                    {"CardTitleFontSize", 13},
                    {"ButtonFilled", 0},
                    {"ButtonBorderThickness", 1},
                    {"ButtonFilledTextColor", ""},
                    {"TitlebarSolid", 0},
                    {"TitlebarColor", ""},
                    {"TitlebarHeight", 48},
                    {"NavVertical", "false"},
                    {"NavButtonSize", 52},
                    {"NavButtonRadius", 12},
                    {"NavIconSize", 18},
                    {"NavFontSize", 10.5},
                    {"NavSpacing", 2}}},
                {"icons", New JObject From {
                    {"Launch", ""},
                    {"Download", ""},
                    {"Link", ""},
                    {"Setup", ""},
                    {"More", ""},
                    {"Extra", ""}}}
            }
            FileUtils.Write(IO.Path.Combine(Target, "skin.json"), Json.ToString(Formatting.Indented))
            '配一份能直接跑的布局：皮肤只有声明了 Title 与 Content 两个必需插槽才会生效，
            '模板按这个最小结构给出，作者改起来有参照，不会一开始就是空白
            FileUtils.Write(IO.Path.Combine(Target, "Layout.xaml"), TemplateLayout)
            ListPacks(True)
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "创建皮肤模板失败")
            ErrorText = FriendlyError(ex.Message)
            Return False
        End Try
    End Function

End Module
