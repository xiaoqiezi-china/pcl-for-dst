Imports System.IO.Compression
Imports System.Text.RegularExpressions

''' <summary>
''' OptiFine / Iris 光影包配置（shaders.properties + lang + shaderpacks\&lt;包名&gt;.txt）的读取与写回。
'''
''' 光影包的选项定义分散在三处：
''' 1. shaders.properties 的 screen.* / sliders / option.*.values，决定有哪些分组与控件形态；
''' 2. shaders/lang/*.lang 提供分组名、选项名、枚举值名；
''' 3. 着色器源码里的 const bool/int/float NAME = 值; 提供默认值。
''' 游戏运行时把用户改过的值写在 shaderpacks\&lt;包名&gt;.txt 里，格式就是每行 键=值。
''' 本模块只做文本层面的解析与合并，不去理解 GLSL。
''' </summary>
Public Module DstShaderConfig

    ''' <summary>选项的控件形态。</summary>
    Public Enum OptionKind
        ''' <summary>开关，值是 true / false。</summary>
        Switch = 0
        ''' <summary>数值，可以是滑条也可以是直接输入。</summary>
        Number = 1
        ''' <summary>枚举，值从固定的几个里挑。</summary>
        Choice = 2
    End Enum

    ''' <summary>光影包里的一个可配置项。</summary>
    Public Class ShaderOption
        ''' <summary>源码里的变量名，同时是 txt 里的键名。</summary>
        Public Name As String = ""
        ''' <summary>界面上显示的名字。</summary>
        Public Label As String = ""
        ''' <summary>所属分组的显示名。</summary>
        Public GroupName As String = ""
        Public Kind As OptionKind = OptionKind.Switch
        ''' <summary>枚举的可选值。</summary>
        Public Choices As New List(Of String)
        ''' <summary>枚举值的显示名，键为原始值。</summary>
        Public ChoiceLabels As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>当前值（来自 txt，没有则为源码默认值）。</summary>
        Public Value As String = ""
        ''' <summary>源码里的默认值，用于「恢复默认」。</summary>
        Public DefaultValue As String = ""
        ''' <summary>是否来自 txt（用户手动改过），用于界面提示。</summary>
        Public IsCustomized As Boolean = False
        ''' <summary>数值型是否为浮点。</summary>
        Public IsFloat As Boolean = True
        ''' <summary>
        ''' 数值型可用滑条时的上限；小于等于 0 表示不给滑条，直接输入。
        ''' 只有能确认取值范围时才给滑条，避免瞎猜范围把用户的值夹掉。
        ''' </summary>
        Public SliderMax As Integer = 0
    End Class

    ''' <summary>一个光影包解析出来的全部配置。</summary>
    Public Class ShaderConfig
        Public PackPath As String = ""
        ''' <summary>txt 配置的键名，即光影包去掉扩展名后的文件名。</summary>
        Public PackKey As String = ""
        ''' <summary>默认的 txt 位置。</summary>
        Public ConfigPath As String = ""
        Public Groups As New List(Of String)
        Public Options As New List(Of ShaderOption)
        ''' <summary>txt 里存在但不认识的键，保存时原样写回，避免弄丢别人的设置。</summary>
        Public Extra As New Dictionary(Of String, String)(StringComparer.Ordinal)
        ''' <summary>读取失败的原因；为空表示没有出错。</summary>
        Public LoadError As String = ""
        ''' <summary>是否读到了选项定义。</summary>
        Public ReadOnly Property HasOptions As Boolean
            Get
                Return Options.Count > 0
            End Get
        End Property
    End Class

    '限制扫描着色器源码的总字节数，避免个别包体积过大时卡太久
    Private Const MaxSourceBytes As Long = 16 * 1024 * 1024

    '着色器里声明选项的写法：const bool WAVY_PLANTS = true;
    Private ReadOnly OptionDeclareRegex As New Regex(
        "(?:^|\s)const\s+(bool|int|float|double)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([^;]+);",
        RegexOptions.Compiled Or RegexOptions.Multiline)

#Region "对外接口"

    ''' <summary>txt 配置的默认位置：与光影包同目录，名字取光影包文件名（去掉 .zip / .disabled）。</summary>
    Public Function DefaultConfigPath(PackPath As String) As String
        If String.IsNullOrWhiteSpace(PackPath) Then Return ""
        Dim Folder As String = PathUtils.RemoveLastPart(PackPath)
        Dim Name As String = PathUtils.GetLastPart(PackPath)
        If Name.EndsWithF(".disabled", True) Then Name = Name.Substring(0, Name.Length - ".disabled".Length)
        If Name.EndsWithF(".zip", True) Then Name = Name.Substring(0, Name.Length - 4)
        Return PathUtils.AddSlashSuffix(Folder) & Name & ".txt"
    End Function

    ''' <summary>
    ''' 解析一个光影包。PackPath 可以是 .zip、.zip.disabled 或文件夹。
    ''' 这一步会读压缩包与着色器源码，必须在工作线程调用。
    ''' </summary>
    Public Function Load(PackPath As String) As ShaderConfig
        Dim Cfg As New ShaderConfig With {.PackPath = PackPath}
        Try
            '停用的包名是 xxx.zip.disabled，先把 .disabled 去掉再取不带扩展名的名字
            Dim PackName As String = PathUtils.GetLastPart(PackPath)
            If PackName.EndsWithF(".disabled", True) Then PackName = PackName.Substring(0, PackName.Length - ".disabled".Length)
            Cfg.PackKey = PathUtils.GetFileNameWithoutExtension(PackName)
            Cfg.ConfigPath = DefaultConfigPath(PackPath)

            Dim Props As String = ReadPackText(PackPath, "shaders/shaders.properties")
            If String.IsNullOrWhiteSpace(Props) Then
                Cfg.LoadError = "这个光影包里没有 shaders/shaders.properties，无法识别配置项"
                Return Cfg
            End If

            Dim Lang As Dictionary(Of String, String) = ReadLang(PackPath)
            Dim Screens As New List(Of KeyValuePair(Of String, List(Of String)))
            Dim Sliders As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim EnumValues As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)

            ParseProperties(Props, Screens, Sliders, EnumValues)
            '源码里的默认值与类型
            Dim Declares As Dictionary(Of String, KeyValuePair(Of String, String)) = ScanOptionDeclares(PackPath)
            '已保存的值
            Dim Saved As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If Cfg.ConfigPath <> "" AndAlso FileUtils.Exists(Cfg.ConfigPath) Then
                ParseConfigText(FileUtils.ReadAsString(Cfg.ConfigPath), Saved)
            End If

            '按界面上的分组顺序建选项，同名的只保留第一次出现
            Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each Screen In Screens
                Dim GroupLabel As String = LangGet(Lang, "screen." & Screen.Key)
                If GroupLabel = "" Then GroupLabel = Screen.Key
                GroupLabel = LocalizeLabel(GroupLabel)
                If GroupLabel = "" Then Continue For
                Dim Added As Boolean = False
                For Each Token In Screen.Value
                    If Not IsOptionToken(Token) Then Continue For
                    If Seen.Contains(Token) Then Continue For
                    Seen.Add(Token)
                    Dim Opt As ShaderOption = BuildOption(Token, GroupLabel, Lang, Sliders, EnumValues, Declares, Saved)
                    If Opt IsNot Nothing Then
                        Cfg.Options.Add(Opt)
                        Added = True
                    End If
                Next
                If Added AndAlso Not Cfg.Groups.Contains(GroupLabel) Then Cfg.Groups.Add(GroupLabel)
            Next

            'lang 里声明了但没挂到任何分组上的选项，统一放进“其它”
            For Each Key In Lang.Keys
                If Not Key.StartsWithF("option.", True) Then Continue For
                Dim Name As String = Key.Substring("option.".Length).Trim()
                'option.X.comment / option.X.values 这类是选项的附加信息，不是选项本身
                If Name.Contains(".") Then Continue For
                If Name = "" OrElse Seen.Contains(Name) Then Continue For
                Seen.Add(Name)
                Dim Opt As ShaderOption = BuildOption(Name, "其它", Lang, Sliders, EnumValues, Declares, Saved)
                If Opt IsNot Nothing Then
                    Cfg.Options.Add(Opt)
                    If Not Cfg.Groups.Contains("其它") Then Cfg.Groups.Add("其它")
                End If
            Next

            'txt 里存在但不认识的键：原样留着
            For Each Pair In Saved
                If Not Seen.Contains(Pair.Key) Then Cfg.Extra(Pair.Key) = Pair.Value
            Next

            If Cfg.Options.Count = 0 Then Cfg.LoadError = "没有在这个光影包里找到可配置项"
        Catch ex As Exception
            Logger.Warn(ex, "解析光影包配置失败：" & PackPath)
            Cfg.LoadError = "解析失败：" & ex.Message
        End Try
        Return Cfg
    End Function

    ''' <summary>
    ''' 把配置写成 txt。TargetPath 为空时写回配置的默认位置。
    ''' 成功后返回空字符串，失败返回错误信息。
    ''' </summary>
    Public Function Save(Cfg As ShaderConfig, Optional TargetPath As String = "") As String
        If Cfg Is Nothing Then Return "没有可保存的配置"
        If String.IsNullOrWhiteSpace(TargetPath) Then TargetPath = Cfg.ConfigPath
        If String.IsNullOrWhiteSpace(TargetPath) Then Return "没有可写入的路径"
        Try
            Dim Sb As New Text.StringBuilder
            Sb.AppendLine("# 由 DPCL 写入 " & Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            Sb.AppendLine("# 该文件与 OptiFine / Iris 的 shaderpacks\<光影包名>.txt 是同一个东西，游戏会直接读取")
            For Each Opt In Cfg.Options
                Sb.AppendLine(Opt.Name & "=" & If(Opt.Value, ""))
            Next
            For Each Pair In Cfg.Extra
                Sb.AppendLine(Pair.Key & "=" & Pair.Value)
            Next
            DirectoryUtils.Create(PathUtils.RemoveLastPart(TargetPath))
            FileUtils.Write(TargetPath, Sb.ToString())
            Logger.Info($"已保存光影配置：{TargetPath}（{Cfg.Options.Count} 项）")
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "保存光影配置失败：" & TargetPath)
            Return "保存失败：" & ex.Message
        End Try
    End Function

#End Region

#Region "解析"

    ''' <summary>按选项原始值、类型与已保存值组装出一个选项对象。</summary>
    Private Function BuildOption(Name As String, GroupLabel As String, Lang As Dictionary(Of String, String),
                                 Sliders As HashSet(Of String), EnumValues As Dictionary(Of String, List(Of String)),
                                 Declares As Dictionary(Of String, KeyValuePair(Of String, String)),
                                 Saved As Dictionary(Of String, String)) As ShaderOption
        Dim Opt As New ShaderOption With {.Name = Name, .GroupName = GroupLabel}
        Opt.Label = LangGet(Lang, "option." & Name)
        If Opt.Label = "" Then Opt.Label = Name
        Opt.Label = LocalizeLabel(Opt.Label)

        '类型与默认值
        Dim TypeName As String = ""
        Dim DefaultText As String = ""
        Dim DeclInfo As KeyValuePair(Of String, String) = Nothing
        If Declares.TryGetValue(Name, DeclInfo) Then
            TypeName = DeclInfo.Key
            DefaultText = NormalizeValue(DeclInfo.Value)
        End If
        Opt.IsFloat = TypeName = "float" OrElse TypeName = "double"

        '枚举：properties 里显式给了 values，或者 lang 里给了 value.NAME.x
        Dim Values As List(Of String) = Nothing
        If EnumValues.TryGetValue(Name, Values) AndAlso Values.Count > 0 Then
            Opt.Kind = OptionKind.Choice
            Opt.Choices.AddRange(Values)
        Else
            Dim Found As New List(Of String)
            For Each Key In Lang.Keys
                If Not Key.StartsWithF("value." & Name & ".", True) Then Continue For
                Dim Raw As String = Key.Substring(("value." & Name & ".").Length)
                If Raw <> "" AndAlso Not Found.Contains(Raw) Then Found.Add(Raw)
            Next
            If Found.Count > 0 Then
                Opt.Kind = OptionKind.Choice
                Found.Sort(AddressOf CompareValueText)
                Opt.Choices.AddRange(Found)
            End If
        End If

        If Opt.Kind = OptionKind.Choice Then
            For Each Value In Opt.Choices
                Dim Label As String = LangGet(Lang, "value." & Name & "." & Value)
                If Label <> "" Then Opt.ChoiceLabels(Value) = LocalizeValue(Label)
            Next
        ElseIf Sliders.Contains(Name) OrElse Opt.IsFloat Then
            Opt.Kind = OptionKind.Number
            '只有确认是 0~1 的浮点才给百分比滑条，其余类型直接填数字
            If Opt.IsFloat AndAlso DefaultText <> "" Then
                Dim D As Double
                If Double.TryParse(DefaultText, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, D) AndAlso
                   D >= 0 AndAlso D <= 1 Then Opt.SliderMax = 100
            End If
        Else
            Opt.Kind = OptionKind.Switch
        End If

        '默认值：源码声明优先，其次按类型兜底
        Dim Fallback As String = ""
        If DefaultText <> "" Then
            Fallback = DefaultText
        ElseIf Opt.Kind = OptionKind.Choice AndAlso Opt.Choices.Count > 0 Then
            Fallback = Opt.Choices(0)
        ElseIf Opt.Kind = OptionKind.Switch Then
            Fallback = "true"
        Else
            Fallback = "0"
        End If
        If Opt.Kind = OptionKind.Switch Then Fallback = NormalizeSwitch(Fallback)
        Opt.DefaultValue = Fallback

        '当前值：txt 里有就用 txt 的
        Dim Current As String = ""
        Dim SavedText As String = ""
        If Saved.TryGetValue(Name, SavedText) AndAlso SavedText <> "" Then
            Current = SavedText
            If Opt.Kind = OptionKind.Switch Then Current = NormalizeSwitch(Current)
            '只有和默认值不一样才算“改过”，否则保存过一次就整页都是强调色
            Opt.IsCustomized = Not String.Equals(Current.Trim(), Fallback.Trim(), StringComparison.OrdinalIgnoreCase)
        Else
            Current = Fallback
        End If
        Opt.Value = Current
        Return Opt
    End Function

    ''' <summary>
    ''' 把英文标签换成中文。已经是中文的、或者对照表里没有的，原样返回。
    ''' </summary>
    Private Function LocalizeLabel(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return Text
        If DstShaderLangZh.HasChinese(Text) Then Return Text
        Dim Zh As String = DstShaderLangZh.Translate(Text)
        Return If(Zh <> "", Zh, Text)
    End Function

    ''' <summary>选项值（Off / High 之类）的中文化。</summary>
    Private Function LocalizeValue(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return Text
        If DstShaderLangZh.HasChinese(Text) Then Return Text
        Dim Zh As String = DstShaderLangZh.TranslateValue(Text)
        Return If(Zh <> "", Zh, Text)
    End Function

    ''' <summary>把开关的各种写法统一成 true / false。</summary>
    Private Function NormalizeSwitch(Text As String) As String
        Select Case If(Text, "").Trim().ToLower()
            Case "false", "0", "off", "no", "disabled"
                Return "false"
            Case Else
                Return "true"
        End Select
    End Function

    ''' <summary>枚举值排序：数字按数值排，其余按文本排。</summary>
    Private Function CompareValueText(A As String, B As String) As Integer
        Dim X As Double, Y As Double
        If Double.TryParse(A, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, X) AndAlso
           Double.TryParse(B, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, Y) Then
            Return X.CompareTo(Y)
        End If
        Return String.Compare(A, B, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>把源码里写的值整理一下：去掉多余空白与结尾注释。</summary>
    Private Function NormalizeValue(Value As String) As String
        If Value Is Nothing Then Return ""
        Dim S As String = Value.Trim()
        Dim Cut As Integer = S.IndexOf("//", StringComparison.Ordinal)
        If Cut >= 0 Then S = S.Substring(0, Cut).Trim()
        Return S
    End Function

    ''' <summary>判断 screen 里的一个词是不是选项名（排除 &lt;empty&gt;、[子界面] 这类占位符）。</summary>
    Private Function IsOptionToken(Token As String) As Boolean
        If String.IsNullOrWhiteSpace(Token) Then Return False
        Token = Token.Trim()
        If Token.StartsWith("<") OrElse Token.StartsWith("[") OrElse Token.StartsWith("!") Then Return False
        If Token = "profile" OrElse Token = "empty" Then Return False
        'info0 / info1 这类是说明文字，不是可以改的选项
        If Regex.IsMatch(Token, "^info[0-9]*$", RegexOptions.IgnoreCase) Then Return False
        Return Regex.IsMatch(Token, "^[A-Za-z_][A-Za-z0-9_.]*$")
    End Function

    ''' <summary>
    ''' 解析 shaders.properties 里与界面相关的部分。
    ''' 不理会 #if 之类的条件编译：那些只影响运行时行为，不影响有哪些选项。
    ''' </summary>
    Private Sub ParseProperties(Text As String, Screens As List(Of KeyValuePair(Of String, List(Of String))),
                                Sliders As HashSet(Of String), EnumValues As Dictionary(Of String, List(Of String)))
        For Each RawLine In Text.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.None)
            Dim Line As String = RawLine.Trim()
            If Line = "" OrElse Line.StartsWith("#") OrElse Line.StartsWith("!") Then Continue For
            Dim Cut As Integer = Line.IndexOf("="c)
            If Cut < 0 Then Continue For
            Dim Key As String = Line.Substring(0, Cut).Trim()
            Dim Value As String = Line.Substring(Cut + 1).Trim()
            If Key = "" Then Continue For

            If Key.Equals("sliders", StringComparison.OrdinalIgnoreCase) Then
                For Each Part In Value.Split({" "c, vbTab}, StringSplitOptions.RemoveEmptyEntries)
                    Sliders.Add(Part.Trim())
                Next
                Continue For
            End If

            If Key.StartsWithF("screen.", True) Then
                'screen.X=... 是布局；screen.X.columns 之类是属性，不是分组
                Dim Name As String = Key.Substring("screen.".Length)
                If Name.IndexOf("."c) >= 0 Then Continue For
                Dim Tokens As New List(Of String)
                For Each Part In Value.Split({" "c, vbTab}, StringSplitOptions.RemoveEmptyEntries)
                    Tokens.Add(Part.Trim())
                Next
                Screens.Add(New KeyValuePair(Of String, List(Of String))(Name, Tokens))
                Continue For
            End If

            If Key.StartsWithF("option.", True) AndAlso Key.EndsWithF(".values", True) Then
                Dim Name As String = Key.Substring("option.".Length, Key.Length - "option.".Length - ".values".Length)
                If Name = "" Then Continue For
                Dim List_ As New List(Of String)
                For Each Part In Value.Split({" "c, vbTab}, StringSplitOptions.RemoveEmptyEntries)
                    List_.Add(Part.Trim())
                Next
                EnumValues(Name) = List_
                Continue For
            End If
        Next
    End Sub

    ''' <summary>解析已保存的 txt：每行 键=值，# 开头为注释。</summary>
    Private Sub ParseConfigText(Text As String, Target As Dictionary(Of String, String))
        If String.IsNullOrWhiteSpace(Text) Then Return
        For Each RawLine In Text.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.None)
            Dim Line As String = RawLine.Trim()
            If Line = "" OrElse Line.StartsWith("#") Then Continue For
            Dim Cut As Integer = Line.IndexOf("="c)
            If Cut <= 0 Then Continue For
            Dim Key As String = Line.Substring(0, Cut).Trim()
            If Key <> "" Then Target(Key) = Line.Substring(Cut + 1).Trim()
        Next
    End Sub

    ''' <summary>读语言文件，优先中文，其次英文。</summary>
    Private Function ReadLang(PackPath As String) As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim Candidates As String() = {
            "shaders/lang/zh_cn.lang", "shaders/lang/zh_CN.lang",
            "shaders/lang/en_us.lang", "shaders/lang/en_US.lang",
            "shaders/lang/en_us.UTF-8.lang"}
        Dim Text As String = ""
        For Each Rel In Candidates
            Text = ReadPackText(PackPath, Rel)
            If Not String.IsNullOrWhiteSpace(Text) Then Exit For
        Next
        If String.IsNullOrWhiteSpace(Text) Then Return Result
        For Each RawLine In Text.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.None)
            Dim Line As String = RawLine.Trim()
            If Line = "" OrElse Line.StartsWith("#") Then Continue For
            Dim Cut As Integer = Line.IndexOf("="c)
            If Cut <= 0 Then Continue For
            Dim Key As String = Line.Substring(0, Cut).Trim()
            Dim Value As String = Line.Substring(Cut + 1).Trim()
            If Key <> "" Then Result(Key) = Value
        Next
        Return Result
    End Function

    Private Function LangGet(Lang As Dictionary(Of String, String), Key As String) As String
        Dim Value As String = ""
        If Lang.TryGetValue(Key, Value) Then Return CleanLabel(Value)
        Return ""
    End Function

    ''' <summary>
    ''' 去掉 Minecraft 的颜色代码（§ 加一个字符）以及首尾空白。
    ''' 光影包会在语言文件里直接写 §e、§r 这类标记，原样显示会是一串乱符号。
    ''' </summary>
    Public Function CleanLabel(Text As String) As String
        If String.IsNullOrEmpty(Text) Then Return ""
        Dim Sb As New Text.StringBuilder(Text.Length)
        Dim I As Integer = 0
        Do While I < Text.Length
            Dim C As Char = Text(I)
            If C = ChrW(167) AndAlso I + 1 < Text.Length Then '§
                I += 2
                Continue Do
            End If
            Sb.Append(C)
            I += 1
        Loop
        Return Sb.ToString().Trim()
    End Function

    ''' <summary>
    ''' 扫着色器源码，收集 const 声明的默认值与类型。
    ''' 找不到声明时返回空字典，不影响其它功能。
    ''' </summary>
    Private Function ScanOptionDeclares(PackPath As String) As Dictionary(Of String, KeyValuePair(Of String, String))
        Dim Result As New Dictionary(Of String, KeyValuePair(Of String, String))(StringComparer.OrdinalIgnoreCase)
        Dim Total As Long = 0
        Try
            For Each Item In EnumerateShaderSources(PackPath)
                If Total > MaxSourceBytes Then Exit For
                Dim Content As String = Item.Value
                If Content Is Nothing OrElse Content.Length = 0 Then Continue For
                Total += Content.Length
                For Each M As Match In OptionDeclareRegex.Matches(Content)
                    Dim Name As String = M.Groups(2).Value
                    If Result.ContainsKey(Name) Then Continue For '第一次出现的声明即视为默认值
                    Result(Name) = New KeyValuePair(Of String, String)(M.Groups(1).Value.ToLower(), M.Groups(3).Value.Trim())
                Next
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描光影包选项声明失败：" & PackPath)
        End Try
        Return Result
    End Function

    ''' <summary>枚举着色器源码文件，返回 相对路径 → 内容。</summary>
    Private Function EnumerateShaderSources(PackPath As String) As IEnumerable(Of KeyValuePair(Of String, String))
        Dim Result As New List(Of KeyValuePair(Of String, String))
        Dim Exts As String() = {".vsh", ".fsh", ".gsh", ".csh", ".glsl", ".inc", ".properties"}
        If DirectoryUtils.Exists(PackPath) Then
            For Each FilePath In DirectoryUtils.EnumerateFiles(PackPath, True)
                Dim Ext As String = PathUtils.GetExtension(FilePath).ToLower()
                If Not Exts.Contains(Ext) Then Continue For
                Try
                    Result.Add(New KeyValuePair(Of String, String)(FilePath, FileUtils.ReadAsString(FilePath)))
                Catch ex As Exception
                    Logger.Warn(ex, $"读取光影配置文件失败：{FilePath}")
                End Try
            Next
            Return Result
        End If
        If FileUtils.Exists(PackPath) Then
            Using Zip As ZipArchive = ZipFile.OpenRead(PackPath)
                For Each Entry In Zip.Entries
                    Dim Ext As String = PathUtils.GetExtension(Entry.FullName).ToLower()
                    If Not Exts.Contains(Ext) Then Continue For
                    If Entry.Length <= 0 OrElse Entry.Length > 4 * 1024 * 1024 Then Continue For
                    Try
                        Using ZipStream As Stream = Entry.Open()
                            Using Reader As New StreamReader(ZipStream)
                                Result.Add(New KeyValuePair(Of String, String)(Entry.FullName, Reader.ReadToEnd()))
                            End Using
                        End Using
                    Catch ex As Exception
                        Logger.Warn(ex, $"读取压缩包内的光影配置失败：{Entry.FullName}")
                    End Try
                Next
            End Using
        End If
        Return Result
    End Function

    ''' <summary>从光影包（压缩包或文件夹）里读一个文本文件，读不到返回空串。</summary>
    Public Function ReadPackText(PackPath As String, RelPath As String) As String
        Try
            If DirectoryUtils.Exists(PackPath) Then
                Dim Full As String = PathUtils.AddSlashSuffix(PackPath) & RelPath.Replace("/"c, ChrW(92))
                If Not FileUtils.Exists(Full) Then Return ""
                Return FileUtils.ReadAsString(Full)
            End If
            If Not FileUtils.Exists(PackPath) Then Return ""
            Using Zip As ZipArchive = ZipFile.OpenRead(PackPath)
                Dim Target As String = RelPath.Replace("/"c, ChrW(92))
                For Each Entry In Zip.Entries
                    If Not Entry.FullName.Replace("/"c, ChrW(92)).Equals(Target, StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Entry.Length <= 0 Then Return ""
                    Using ZipStream As Stream = Entry.Open()
                        Using Reader As New StreamReader(ZipStream)
                            Return Reader.ReadToEnd()
                        End Using
                    End Using
                Next
            End Using
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, $"读取光影包文件失败：{PackPath} / {RelPath}")
            Return ""
        End Try
    End Function

#End Region

End Module
