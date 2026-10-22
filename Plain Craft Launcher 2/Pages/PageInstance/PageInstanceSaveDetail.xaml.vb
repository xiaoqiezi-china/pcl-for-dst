''' <summary>
''' 存档详情子页面。
''' 与「版本设置」一样，这是一个独立的副页面：标题栏会出现返回箭头，可以随时退回存档列表。
''' 用于查看与管理单个存档的数据包、区域文件与玩家数据。
''' </summary>
Public Class PageInstanceSaveDetail

    ''' <summary>当前查看的存档目录。</summary>
    Public Shared TargetSave As String = ""

    ''' <summary>当前存档的 level.dat 根标签，读取失败时为 Nothing。</summary>
    Private LevelRoot As NbtTag = Nothing

    ''' <summary>当前存档目录，保证以反斜杠结尾。</summary>
    Private ReadOnly Property CurDir As String
        Get
            If TargetSave = "" Then Return ""
            Return PathUtils.AddSlashSuffix(TargetSave)
        End Get
    End Property

#Region "页面刷新"

    ''' <summary>当前显示的分类：0 世界设置、1 数据包、2 区域文件、3 玩家数据。</summary>
    Private Section As Integer = 0

    Private Sub PageInstanceSaveDetail_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ApplySectionVisibility()
        RefreshContent()
    End Sub

    ''' <summary>切换左侧导航对应的分类。</summary>
    Public Sub SetSection(Id As Integer)
        If Id < 0 OrElse Id > 4 Then Return
        Section = Id
        ApplySectionVisibility()
        If IsLoaded AndAlso Id <> 4 Then RefreshContent()
    End Sub

    ''' <summary>只显示当前分类的卡片；存档的头顶信息（名字、路径、按钮）只在「世界设置」里出现。</summary>
    Private Sub ApplySectionVisibility()
        If CardLevel Is Nothing Then Return
        '地图管理是独立内容区，不跟其它卡片挤在同一个滚动区里
        Dim IsMap As Boolean = Section = 4
        If PanBack IsNot Nothing Then PanBack.Visibility = If(IsMap, Visibility.Collapsed, Visibility.Visible)
        If PanMap IsNot Nothing Then PanMap.Visibility = If(IsMap, Visibility.Visible, Visibility.Collapsed)
        If IsMap Then
            If CardHeader IsNot Nothing Then CardHeader.Visibility = Visibility.Collapsed
            If FrmInstanceSaveMap Is Nothing Then FrmInstanceSaveMap = New PageInstanceSaveMap
            If PanMap IsNot Nothing AndAlso FrmInstanceSaveMap.Parent Is Nothing Then
                PanMap.Children.Clear()
                PanMap.Children.Add(FrmInstanceSaveMap)
            End If
            Return
        End If
        If PanMap IsNot Nothing AndAlso FrmInstanceSaveMap IsNot Nothing AndAlso PanMap.Children.Contains(FrmInstanceSaveMap) Then
            PanMap.Children.Clear()
        End If
        CardLevel.Visibility = If(Section = 0, Visibility.Visible, Visibility.Collapsed)
        CardPack.Visibility = If(Section = 1, Visibility.Visible, Visibility.Collapsed)
        CardRegion.Visibility = If(Section = 2, Visibility.Visible, Visibility.Collapsed)
        CardPlayer.Visibility = If(Section = 3, Visibility.Visible, Visibility.Collapsed)
        If CardHeader Is Nothing Then Return
        CardHeader.Visibility = If(Section = 0, Visibility.Visible, Visibility.Collapsed)
        CardHeader.Margin = If(Section = 0, New Thickness(0, 15, 0, 15), New Thickness(0))
        If PanBack IsNot Nothing Then
            '没有头顶信息时，界面上沿要往下让一点，不要贴着标题栏
            PanBack.Margin = If(Section = 0, New Thickness(0), New Thickness(0, 15, 0, 0))
            PanBack.ScrollToHome()
        End If
    End Sub

    ''' <summary>重新读取当前存档的全部内容。</summary>
    Public Sub RefreshContent()
        If PanPack Is Nothing Then Return
        Dim SaveDir As String = CurDir
        If SaveDir = "" OrElse Not DirectoryUtils.Exists(SaveDir) Then
            LabName.Text = "存档不存在"
            LabPath.Text = SaveDir
            LabLevelName.Text = ""
            PanPack.Children.Clear()
            PanRegion.Children.Clear()
            PanPlayer.Children.Clear()
            PanLevel.Children.Clear()
            Return
        End If
        LabName.Text = PathUtils.GetLastPart(SaveDir)
        LabPath.Text = SaveDir
        'level.dat 中记录的世界名
        Dim Root As NbtTag = ReadLevelDat(SaveDir)
        LevelRoot = Root
        Dim LevelName As String = ""
        If Root IsNot Nothing Then
            Dim NameTag As NbtTag = DstNbt.GetByPath(Root, "Data.LevelName")
            If NameTag IsNot Nothing Then LevelName = CType(NameTag.TagValue, String)
        End If
        LabLevelName.Text = If(LevelName = "", "level.dat 中没有记录世界名", "level.dat 中的世界名：" & LevelName)
        '只重建当前显示的分类
        Select Case Section
            Case 0
                BuildLevel()
            Case 1
                BuildPacks(SaveDir)
            Case 2
                BuildRegions(SaveDir)
            Case 3
                BuildPlayers(SaveDir)
        End Select
    End Sub

#End Region

#Region "顶部按钮"

    Private Sub BtnBack_Click(sender As Object, e As EventArgs) Handles BtnBack.Click
        FrmMain.PageBack()
    End Sub

    Private Sub BtnOpen_Click(sender As Object, e As EventArgs) Handles BtnOpen.Click
        If TargetSave = "" Then Return
        OpenExplorer(TargetSave)
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs) Handles BtnRefresh.Click
        RefreshContent()
    End Sub

    ''' <summary>
    ''' 重命名世界。用的是 PCL 自己的重命名流程：弹出输入框 + 文件夹名校验。
    ''' </summary>
    Private Sub BtnRename_Click(sender As Object, e As EventArgs) Handles BtnRename.Click
        Dim SaveDir As String = CurDir
        If SaveDir = "" Then Return
        Dim OldName As String = PathUtils.GetLastPart(SaveDir)
        Dim Parent As String = PathUtils.AddSlashSuffix(PathUtils.RemoveLastPart(SaveDir))
        Dim NewName As String = MyMsgBoxInput("重命名世界",
            "修改存档文件夹的名字，同时更新 level.dat 中记录的世界名。", OldName,
            New ObjectModel.Collection(Of Validate) From {
                New ValidateFolderName(Parent, IgnoreList:=New List(Of String) From {OldName})
            })
        If String.IsNullOrWhiteSpace(NewName) Then Return
        If NewName = OldName Then Return
        '先改 level.dat，改不动就不动文件夹
        Dim LevelDat As String = SaveDir & "level.dat"
        If FileUtils.Exists(LevelDat) Then
            Dim Root As NbtTag = Nothing
            Try
                Root = DstNbt.ReadFile(LevelDat)
            Catch ex As Exception
                Hint("读取 level.dat 失败：" & ex.Message & vbCrLf & "已取消重命名。", HintType.Red)
                Return
            End Try
            Dim Data As NbtTag = DstNbt.CompoundGet(Root, "Data")
            If Not DstNbt.CompoundSetString(Data, "LevelName", NewName) Then
                Hint("level.dat 中没有找到世界名，只重命名文件夹。", HintType.Blue)
            ElseIf Not SaveLevelDat(SaveDir, Root) Then
                Hint("写入 level.dat 失败，已取消重命名。", HintType.Red)
                Return
            End If
        End If
        '再改文件夹名
        Dim NewDir As String = Parent & NewName
        Try
            DirectoryUtils.Move(SaveDir, NewDir)
        Catch ex As Exception
            Hint("重命名文件夹失败：" & ex.Message, HintType.Red)
            Return
        End Try
        TargetSave = NewDir
        FrmMain.PageNameRefresh() '标题栏里的存档名也要跟着变
        Hint("世界已重命名为 " & NewName, HintType.Green)
        RefreshContent()
    End Sub

#End Region

#Region "世界设置"

    ''' <summary>
    ''' level.dat 中的常用字段。标量与字符串可以直接改，改完写回文件。
    ''' </summary>
    Private Sub BuildLevel()
        PanLevel.Children.Clear()
        If LevelRoot Is Nothing Then
            PanLevel.Children.Add(EmptyHint("没有找到 level.dat，或者读取失败。"))
            Return
        End If
        If DstNbt.CompoundGet(LevelRoot, "Data") Is Nothing Then
            PanLevel.Children.Add(EmptyHint("level.dat 结构异常，没有 Data 标签。"))
            Return
        End If
        AddLevelRow("Data.LevelName", "世界名")
        AddLevelRow("Data.allowCommands", "允许作弊", "", "bool")
        '26.1 把 hardcore 挪进了 difficulty_settings
        If DstNbt.GetByPath(LevelRoot, "Data.hardcore") IsNot Nothing Then
            AddLevelRow("Data.hardcore", "极限模式", "", "bool")
        Else
            AddLevelRow("Data.difficulty_settings.hardcore", "极限模式", "", "bool")
        End If
        AddLevelRow("Data.GameType", "游戏模式", "", "combo", GameModeOptions())
        If DstNbt.GetByPath(LevelRoot, "Data.Difficulty") IsNot Nothing Then
            AddLevelRow("Data.Difficulty", "难度", "", "combo", DifficultyIdOptions())
        Else
            AddLevelRow("Data.difficulty_settings.difficulty", "难度", "", "combo", DifficultyNameOptions())
            AddLevelRow("Data.difficulty_settings.locked", "锁定难度", "", "bool")
        End If
        AddLevelRow("Data.Time", "世界时间（刻）", "1 秒 = 20 刻")
        AddLevelRow("Data.DayTime", "当天时间（刻）", "1 秒 = 20 刻")
        '出生点：26.1 是 Data.spawn.pos 列表 + spawn.dimension，之前是 SpawnX/Y/Z
        If DstNbt.GetByPath(LevelRoot, "Data.SpawnX") IsNot Nothing Then
            AddLevelRow("Data.SpawnX", "出生点 X")
            AddLevelRow("Data.SpawnY", "出生点 Y")
            AddLevelRow("Data.SpawnZ", "出生点 Z")
            AddLevelRow("Data.SpawnAngle", "出生朝向")
        Else
            AddSpawnRows()
        End If
        AddLevelRow("Data.initialized", "世界已初始化", "", "bool")
        AddLevelRow("Data.WasModded", "被 Mod 修改过", "", "bool")
        '只读字段，右侧带复制按钮
        AddLevelRow("Data.LastPlayed", "上次游玩", "", "readonly")
        AddLevelRow("Data.Version.Name", "游戏版本", "", "readonly")
        AddLevelRow("Data.DataVersion", "数据版本", "", "readonly")
        '世界种子：26.1 移到了 data\minecraft\world_gen_settings.dat
        If DstNbt.GetByPath(LevelRoot, "Data.WorldGenSettings.seed") IsNot Nothing Then
            AddLevelRow("Data.WorldGenSettings.seed", "世界种子", "", "readonly")
        Else
            AddLevelRow("Data.world_gen_settings.seed", "世界种子", "", "readonly")
        End If
    End Sub

    ''' <summary>
    ''' 26.1 的出生点写法：Data.spawn.pos 是三个 Double 的列表，Data.spawn.dimension 是维度。
    ''' 这三行不能直接走 AddLevelRow（那是按点号路径找 compound 子项），所以单独造。
    ''' </summary>
    Private Sub AddSpawnRows()
        Dim PosTag As NbtTag = DstNbt.GetByPath(LevelRoot, "Data.spawn.pos")
        If PosTag Is Nothing Then Return
        Dim Items As List(Of NbtTag) = TryCast(PosTag.TagValue, List(Of NbtTag))
        If Items Is Nothing OrElse Items.Count < 3 Then Return
        Dim Labels As String() = {"出生点 X", "出生点 Y", "出生点 Z"}
        For i = 0 To 2
            Dim Index As Integer = i
            Dim Value As String = Math.Round(CDbl(Items(i).TagValue), 2).ToString()
            Dim SubText As String = "Double · spawn.pos[" & i & "]"
            Dim Btn As New MyTextButton With {.Text = "编辑", .Height = 26, .MinWidth = 62}
            AddHandler Btn.Click,
                Sub()
                    Dim NewText As String = MyMsgBoxInput("修改" & Labels(Index),
                        "输入新的坐标值。", Value)
                    If NewText Is Nothing Then Return
                    Dim D As Double
                    If Not Double.TryParse(NewText.Trim(), D) Then
                        Hint("请输入数字", HintType.Red)
                        Return
                    End If
                    Items(Index).TagValue = D
                    If SaveLevelDat(CurDir, LevelRoot) Then
                        Hint("已保存", HintType.Blue)
                    Else
                        Hint("保存失败", HintType.Red)
                    End If
                End Sub
            PanLevel.Children.Add(BuildRow(Nothing, Labels(i), SubText, Value,
                                           New List(Of FrameworkElement) From {Btn}, False))
        Next
        Dim DimTag As NbtTag = DstNbt.GetByPath(LevelRoot, "Data.spawn.dimension")
        If DimTag IsNot Nothing Then
            Dim DimText As String = DstNbt.Describe(DimTag)
            Dim BtnCopy As New MyTextButton With {.Text = "复制", .Height = 26, .MinWidth = 62}
            AddHandler BtnCopy.Click, Sub() My.Computer.Clipboard.SetText(DimText)
            PanLevel.Children.Add(BuildRow(Nothing, "出生点维度", "String · spawn.dimension", DimText,
                                           New List(Of FrameworkElement) From {BtnCopy}, False))
        End If
    End Sub

    ''' <summary>世界设置中的一行。Kind 为 edit（数字/文字）、bool（开关）或 readonly（只读，可复制）。</summary>
    Private Function GameModeOptions() As List(Of KeyValuePair(Of String, String))
        Return New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("生存", "0"),
            New KeyValuePair(Of String, String)("创造", "1"),
            New KeyValuePair(Of String, String)("冒险", "2"),
            New KeyValuePair(Of String, String)("旁观", "3")}
    End Function

    Private Function DifficultyIdOptions() As List(Of KeyValuePair(Of String, String))
        Return New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("和平", "0"),
            New KeyValuePair(Of String, String)("简单", "1"),
            New KeyValuePair(Of String, String)("普通", "2"),
            New KeyValuePair(Of String, String)("困难", "3")}
    End Function

    Private Function DifficultyNameOptions() As List(Of KeyValuePair(Of String, String))
        Return New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("和平", "peaceful"),
            New KeyValuePair(Of String, String)("简单", "easy"),
            New KeyValuePair(Of String, String)("普通", "normal"),
            New KeyValuePair(Of String, String)("困难", "hard")}
    End Function

    Private Sub AddLevelRow(Path As String, Label As String, Optional Tip As String = "", Optional Kind As String = "edit",
                            Optional Options As List(Of KeyValuePair(Of String, String)) = Nothing)
        If LevelRoot Is Nothing Then Return
        Dim Tag As NbtTag = DstNbt.GetByPath(LevelRoot, Path)
        If Tag Is Nothing Then Return
        Dim SubText As String = DstNbt.TypeName(Tag.TagType) & " · " & Path.Replace("Data.", "")
        If Tip <> "" Then SubText &= " · " & Tip
        Dim Texts As String = DstNbt.Describe(Tag)
        If Path.EndsWith("LastPlayed", StringComparison.Ordinal) AndAlso Tag.TagType = DstNbt.TypeLong Then
            Try
                Dim Unix As Date = New Date(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                Texts = Unix.AddMilliseconds(CLng(Tag.TagValue)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            Catch
            End Try
        End If
        Dim Buttons As New List(Of FrameworkElement)
        Select Case Kind
            Case "bool"
                '开关类字段用勾选框
                Texts = ""
                Dim IsOn As Boolean = CLng(Tag.TagValue) <> 0
                Dim Chk As New MyCheckBox With {
                    .Text = If(IsOn, "开", "关"), .Checked = IsOn,
                    .Height = 20, .MinWidth = 52, .Margin = New Thickness(8, 0, 0, 0),
                    .VerticalAlignment = VerticalAlignment.Center
                }
                AddHandler Chk.Change,
                    Sub(sender As Object, user As Boolean)
                        If Not user Then Return
                        SetLevelBool(Path, Label, Chk.Checked)
                    End Sub
                Buttons.Add(Chk)
            Case "combo"
                Texts = ""
                Dim Combo As New MyComboBox With {
                    .Width = 132, .Height = 26, .FontSize = 12, .VerticalAlignment = VerticalAlignment.Center,
                    .Margin = New Thickness(8, 0, 0, 0)
                }
                Dim Cur As String = DstNbt.Describe(Tag)
                Dim Selected As Integer = -1
                Dim Index As Integer = 0
                For Each Opt In Options
                    Combo.Items.Add(New MyComboBoxItem With {.Content = Opt.Key, .Tag = Opt.Value})
                    If Opt.Value = Cur Then Selected = Index
                    Index += 1
                Next
                If Selected >= 0 Then Combo.SelectedIndex = Selected
                AddHandler Combo.SelectionChanged,
                    Sub(sender As Object, e As SelectionChangedEventArgs)
                        Dim Item = TryCast(Combo.SelectedItem, MyComboBoxItem)
                        If Item Is Nothing Then Return
                        If CStr(Item.Tag) = Cur Then Return
                        ApplyLevelText(Path, Label, CStr(Item.Tag))
                    End Sub
                Buttons.Add(Combo)
            Case "readonly"
                Dim BtnCopy As New MyTextButton With {
                    .Text = "复制", .Height = 24, .MinWidth = 54, .FontSize = 12,
                    .Padding = New Thickness(8, 0, 8, 0), .Margin = New Thickness(8, 0, 0, 0),
                    .VerticalAlignment = VerticalAlignment.Center
                }
                AddHandler BtnCopy.Click, Sub() CopyLevelValue(Path, Label)
                Buttons.Add(BtnCopy)
            Case Else
                Dim BtnEdit As New MyIconButton With {
                    .Logo = Logo.IconButtonEdit, .Width = 26, .Height = 26, .LogoScale = 0.8,
                    .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "修改这一项"
                }
                AddHandler BtnEdit.Click, Sub() EditLevelValue(Path, Label)
                Buttons.Add(BtnEdit)
        End Select
        PanLevel.Children.Add(BuildRow(Nothing, Label, SubText, Texts, Buttons, False))
    End Sub

    ''' <summary>开关类字段：0 关，1 开。</summary>
    Private Sub SetLevelBool(Path As String, Label As String, NewValue As Boolean)
        If LevelRoot Is Nothing Then Return
        Dim Tag As NbtTag = DstNbt.GetByPath(LevelRoot, Path)
        If Tag Is Nothing Then Return
        Tag.TagValue = If(NewValue, CSByte(1), CSByte(0))
        If Not SaveLevelDat(CurDir, LevelRoot) Then
            Hint("写入 level.dat 失败，已撤销这次修改。", HintType.Red)
            LevelRoot = ReadLevelDat(CurDir)
            RefreshContent()
            Return
        End If
        Hint(Label & If(NewValue, " 已开启", " 已关闭"), HintType.Green)
        RefreshContent()
    End Sub

    ''' <summary>把只读字段的原始值复制到剪贴板。</summary>
    Private Sub CopyLevelValue(Path As String, Label As String)
        If LevelRoot Is Nothing Then Return
        Dim Tag As NbtTag = DstNbt.GetByPath(LevelRoot, Path)
        If Tag Is Nothing Then Return
        Try
            My.Computer.Clipboard.SetText(DstNbt.Describe(Tag))
            Hint(Label & " 已复制", HintType.Green, False)
        Catch ex As Exception
            Logger.Warn(ex, "复制 " & Label & " 失败")
            Hint("复制失败，剪贴板被其它程序占用。", HintType.Red)
        End Try
    End Sub

    ''' <summary>修改 level.dat 里的一个字段。</summary>
    Private Sub EditLevelValue(Path As String, Label As String)
        If LevelRoot Is Nothing Then Return
        Dim Tag As NbtTag = DstNbt.GetByPath(LevelRoot, Path)
        If Tag Is Nothing Then Return
        Dim OldText As String = DstNbt.Describe(Tag)
        Dim Desc As String = ""
        Select Case Tag.TagType
            Case DstNbt.TypeByte
                Desc = "输入 0 到 127 之间的整数，开关类字段填 0 或 1。"
            Case DstNbt.TypeShort
                Desc = "输入 -32768 到 32767 之间的整数。"
            Case DstNbt.TypeInt
                Desc = "输入一个整数。"
            Case DstNbt.TypeLong
                Desc = "输入一个整数。"
            Case DstNbt.TypeFloat, DstNbt.TypeDouble
                Desc = "输入一个数字，可以带小数。"
            Case DstNbt.TypeString
                Desc = ""
            Case Else
                Hint("这种类型的字段不支持直接修改。", HintType.Red)
                Return
        End Select
        Dim IsNumber As Boolean = Tag.TagType <> DstNbt.TypeString
        Dim NewText As String = MyMsgBoxInput("修改" & Label, Desc, OldText,
            New ObjectModel.Collection(Of Validate) From {
                New ValidateFunc(Function(Text As String) If(IsNumber, ValidateNumberText(Text), ""))
            })
        If NewText Is Nothing Then Return
        NewText = NewText.Trim
        If NewText = OldText Then Return
        Dim NewValue As Object = Nothing
        Try
            Select Case Tag.TagType
                Case DstNbt.TypeByte
                    NewValue = Byte.Parse(NewText)
                Case DstNbt.TypeShort
                    NewValue = Short.Parse(NewText)
                Case DstNbt.TypeInt
                    NewValue = Integer.Parse(NewText)
                Case DstNbt.TypeLong
                    NewValue = Long.Parse(NewText)
                Case DstNbt.TypeFloat
                    NewValue = Single.Parse(NewText)
                Case DstNbt.TypeDouble
                    NewValue = Double.Parse(NewText)
                Case DstNbt.TypeString
                    NewValue = NewText
            End Select
        Catch ex As Exception
            Hint("输入的值无效：" & ex.Message, HintType.Red)
            Return
        End Try
        Tag.TagValue = NewValue
        If Not SaveLevelDat(CurDir, LevelRoot) Then
            Hint("写入 level.dat 失败，已撤销这次修改。", HintType.Red)
            LevelRoot = ReadLevelDat(CurDir)
            RefreshContent()
            Return
        End If
        Hint(Label & " 已修改", HintType.Green)
        If Path = "Data.LevelName" Then FrmMain.PageNameRefresh()
        RefreshContent()
    End Sub

    ''' <summary>把文本按标签类型写回 level.dat，供下拉框等直接调用。</summary>
    Private Function ApplyLevelText(Path As String, Label As String, NewText As String) As Boolean
        If LevelRoot Is Nothing Then Return False
        Dim Tag As NbtTag = DstNbt.GetByPath(LevelRoot, Path)
        If Tag Is Nothing Then Return False
        Dim NewValue As Object = Nothing
        Try
            Select Case Tag.TagType
                Case DstNbt.TypeByte
                    NewValue = Byte.Parse(NewText)
                Case DstNbt.TypeShort
                    NewValue = Short.Parse(NewText)
                Case DstNbt.TypeInt
                    NewValue = Integer.Parse(NewText)
                Case DstNbt.TypeLong
                    NewValue = Long.Parse(NewText)
                Case DstNbt.TypeFloat
                    NewValue = Single.Parse(NewText)
                Case DstNbt.TypeDouble
                    NewValue = Double.Parse(NewText)
                Case DstNbt.TypeString
                    NewValue = NewText
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            Hint("这个值无效：" & ex.Message, HintType.Red)
            RefreshContent()
            Return False
        End Try
        Tag.TagValue = NewValue
        If Not SaveLevelDat(CurDir, LevelRoot) Then
            Hint("写入 level.dat 失败，已撤销这次修改。", HintType.Red)
            LevelRoot = ReadLevelDat(CurDir)
            RefreshContent()
            Return False
        End If
        Hint(Label & " 已修改", HintType.Green)
        If Path = "Data.LevelName" Then FrmMain.PageNameRefresh()
        RefreshContent()
        Return True
    End Function

    Private Function ValidateNumberText(Text As String) As String
        Dim D As Double
        If Double.TryParse(Text.Trim, D) Then Return ""
        Return "请输入数字！"
    End Function

#End Region

#Region "数据包"

    ''' <summary>datapacks 目录下的一个条目。</summary>
    Public Class PackEntry
        ''' <summary>完整路径。</summary>
        Public Path As String = ""
        ''' <summary>数据包名（去掉 .zip / .disable 之后的文件名，或文件夹名）。</summary>
        Public Name As String = ""
        ''' <summary>是不是文件夹（开发状态的开发包）。</summary>
        Public IsFolder As Boolean = False
        ''' <summary>扩展名状态：0 启用（.zip）、1 停用（.zip.disable）、2 文件夹。</summary>
        Public State As Integer = 0
        ''' <summary>相对数据包文件夹的位置，用于提示。</summary>
        Public DisplayPath As String = ""
    End Class

    Private Const PackStateOn As Integer = 0
    Private Const PackStateOff As Integer = 1
    Private Const PackStateFolder As Integer = 2

    ''' <summary>
    ''' 扫描 datapacks 目录。
    ''' 启用中的包是 .zip；停用的包是 .zip.disable；文件夹是开发中的包，不能直接启用或停用。
    ''' </summary>
    Private Function ScanPacks(SaveDir As String) As List(Of PackEntry)
        Dim Result As New List(Of PackEntry)
        Dim Folder As String = SaveDir & "datapacks\"
        If Not DirectoryUtils.Exists(Folder) Then Return Result
        Try
            For Each F In DirectoryUtils.EnumerateFiles(Folder, False)
                Dim Lower As String = F.ToLower()
                Dim Entry As New PackEntry With {.Path = F, .DisplayPath = F.Substring(Folder.Length)}
                If Lower.EndsWith(".zip.disable") Then
                    Entry.Name = PathUtils.GetLastPart(F).Substring(0, PathUtils.GetLastPart(F).Length - 12)
                    Entry.State = PackStateOff
                    Result.Add(Entry)
                ElseIf Lower.EndsWith(".zip") Then
                    Entry.Name = PathUtils.GetFileNameWithoutExtension(F)
                    Entry.State = PackStateOn
                    Result.Add(Entry)
                End If
            Next
            For Each D In DirectoryUtils.EnumerateDirectories(Folder, False)
                '文件夹形态的数据包必须带 pack.mcmeta，否则 Minecraft 会直接忽略
                If Not FileUtils.Exists(PathUtils.AddSlashSuffix(D) & "pack.mcmeta") Then Continue For
                Result.Add(New PackEntry With {
                    .Path = D, .Name = PathUtils.GetLastPart(D), .IsFolder = True,
                    .State = PackStateFolder, .DisplayPath = D.Substring(Folder.Length)
                })
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取数据包目录失败")
        End Try
        Result.Sort(Function(A, B) String.Compare(A.Name, B.Name, StringComparison.OrdinalIgnoreCase))
        Return Result
    End Function

    Private Sub BuildPacks(SaveDir As String)
        PanPack.Children.Clear()
        Dim Packs As List(Of PackEntry) = ScanPacks(SaveDir)
        If Packs.Count = 0 Then
            PanPack.Children.Add(EmptyHint("没有数据包。把 .zip 文件或带 pack.mcmeta 的文件夹放进 datapacks 文件夹即可加载。"))
            Return
        End If
        For Each Entry In Packs
            PanPack.Children.Add(BuildPackRow(Entry, SaveDir))
        Next
        PanPack.Children.Add(New TextBlock With {
            .Text = "启用中的包是 .zip，停用后改成 .zip.disable。文件夹形态是开发中的包，游戏会直接读它，开关由游戏自己决定，这里不能切换。",
            .FontSize = 11, .Opacity = 0.45, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 6, 0, 0)
        })
    End Sub

    ''' <summary>一个数据包的条目。</summary>
    Private Function BuildPackRow(Entry As PackEntry, SaveDir As String) As FrameworkElement
        Dim StateText As String
        Select Case Entry.State
            Case PackStateOn
                StateText = "已启用"
            Case PackStateOff
                StateText = "已停用"
            Case Else
                StateText = "文件夹 · 开发中"
        End Select
        Dim SubText As String = PackDesc(Entry.Path)
        If SubText = "" Then SubText = StateText
        If SubText <> StateText Then SubText = StateText & " · " & SubText
        If Entry.State = PackStateFolder Then SubText &= " · 无法切换"

        Dim Buttons As New List(Of FrameworkElement)
        If Entry.State <> PackStateFolder Then
            Dim BtnToggle As New MyIconButton With {
                .Logo = If(Entry.State = PackStateOn, Logo.IconButtonStop, Logo.IconButtonCheck),
                .Width = 26, .Height = 26, .LogoScale = 0.8, .Margin = New Thickness(6, 0, 0, 0),
                .ToolTip = If(Entry.State = PackStateOn, "停用（改名成 .zip.disable）", "启用（改名成 .zip）")
            }
            AddHandler BtnToggle.Click, Sub() TogglePack(Entry, SaveDir)
            Buttons.Add(BtnToggle)
        End If

        Dim BtnOpen As New MyIconButton With {
            .Logo = Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "打开所在文件夹"
        }
        AddHandler BtnOpen.Click, Sub() OpenExplorer(PathUtils.RemoveLastPart(Entry.Path))
        Buttons.Add(BtnOpen)

        Dim BtnDelete As New MyIconButton With {
            .Logo = Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "删除数据包"
        }
        AddHandler BtnDelete.Click, Sub() DeleteItem(Entry.Path, "数据包")
        Buttons.Add(BtnDelete)

        '停用的包整体压暗一点，一眼能分出来
        Dim Row As FrameworkElement = BuildRow(PackIcon(Entry.Path), Entry.Name, SubText, "", Buttons)
        If Entry.State = PackStateOff Then Row.Opacity = 0.6
        Return Row
    End Function

    ''' <summary>
    ''' 切换数据包的启用状态。
    ''' Minecraft 靠文件后缀判断：.zip 启用、.zip.disable 停用，所以这里只改扩展名。
    ''' 文件夹形态是开发包，不支持切换。
    ''' </summary>
    Private Sub TogglePack(Entry As PackEntry, SaveDir As String)
        If Entry.IsFolder Then
            Hint("文件夹形态的数据包是开发中的包，不能在启动器里启用或停用。", HintType.Red)
            Return
        End If
        If Not FileUtils.Exists(Entry.Path) Then
            Hint("这个数据包文件已经不在了：" & Entry.DisplayPath, HintType.Red)
            RefreshContent()
            Return
        End If
        Dim NewPath As String
        If Entry.State = PackStateOn Then
            NewPath = Entry.Path & ".disable"
        Else
            '要把末尾的 .disable 去掉
            NewPath = Entry.Path.Substring(0, Entry.Path.Length - 8)
        End If
        If FileUtils.Exists(NewPath) Then
            Hint("目标文件已存在，改名会覆盖它，所以停下了：" & PathUtils.GetLastPart(NewPath), HintType.Red)
            Return
        End If
        Try
            FileUtils.Move(Entry.Path, NewPath)
        Catch ex As Exception
            Hint("改名失败：" & ex.Message, HintType.Red)
            Logger.Error(ex, "切换数据包状态失败")
            Return
        End Try
        Hint(If(Entry.State = PackStateOn, "已停用", "已启用") & "：" & Entry.Name, HintType.Green)
        RefreshContent()
    End Sub

    ''' <summary>数据包在列表中的名字（.zip / .zip.disable 都要去掉）。</summary>
    Private Function PackNameOf(Item As String) As String
        Dim FileName As String = PathUtils.GetLastPart(Item)
        If FileName.EndsWith(".zip.disable", StringComparison.OrdinalIgnoreCase) Then
            Return FileName.Substring(0, FileName.Length - 12)
        End If
        If FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
            Return PathUtils.GetFileNameWithoutExtension(Item)
        End If
        Return FileName
    End Function

#End Region

#Region "区域文件"

    Private Sub BuildRegions(SaveDir As String)
        PanRegion.Children.Clear()
        '26.1 后区域文件在 dimensions\minecraft\<维度>\region\，之前是根目录与 DIM-1/DIM1
        Dim Folders As List(Of String) = DstSavePaths.AllRegionFolders(SaveDir)
        Dim Total As Integer = 0
        For Each Folder In Folders
            Dim Files As New List(Of String)
            Try
                For Each F In DirectoryUtils.EnumerateFiles(Folder, False, "*.mca")
                    Files.Add(F)
                Next
            Catch
            End Try
            For Each F In Files
                Total += 1
                PanRegion.Children.Add(BuildRegionRow(F, Folder))
            Next
        Next
        If Total = 0 Then
            PanRegion.Children.Add(EmptyHint("没有区域文件。进入游戏加载地图后会自动生成。"))
        End If
    End Sub

    ''' <summary>文件图标（区域文件用）。</summary>
    Private Const GlyphFile As String = "M6 2 A2 2 0 0 0 4 4 V20 A2 2 0 0 0 6 22 H18 A2 2 0 0 0 20 20 V8 L14 2 Z M14 2 V8 H20"

    ''' <summary>一个区域文件的条目。</summary>
    Private Function BuildRegionRow(File As String, Folder As String) As FrameworkElement
        Dim SizeText As String = ""
        Try
            SizeText = FormatSize(FileUtils.GetInfo(File).Length)
        Catch
        End Try
        Dim Chunks As Integer = CountChunks(File)
        Dim SubText As String = DimNameOf(Folder) & " · " &
                                  If(Chunks < 0, "区块数未知", Chunks & " 个区块")
        If SizeText <> "" Then SubText &= " · " & SizeText
        Dim Buttons As New List(Of FrameworkElement)
        Dim BtnOpen As New MyIconButton With {
            .Logo = Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "打开所在文件夹"
        }
        AddHandler BtnOpen.Click, Sub() OpenExplorer(Folder)
        Buttons.Add(BtnOpen)
        Return BuildRow(Nothing, PathUtils.GetLastPart(File), SubText, "", Buttons, True, GlyphFile)
    End Function

    ''' <summary>读取区域文件的头部，统计区块数量。</summary>
    Private Function CountChunks(McaPath As String) As Integer
        Try
            Using Fs As New FileStream(McaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim Header(4095) As Byte
                Dim ReadCount As Integer = Fs.Read(Header, 0, 4096)
                If ReadCount < 4096 Then Return -1
                Dim Count As Integer = 0
                For i = 0 To 1023
                    If Header(i * 4) <> 0 OrElse Header(i * 4 + 1) <> 0 OrElse Header(i * 4 + 2) <> 0 Then Count += 1
                Next
                Return Count
            End Using
        Catch
            Return -1
        End Try
    End Function

    Private Function DimNameOf(Folder As String) As String
        Return DstSavePaths.DimensionNameOfFolder(Folder)
    End Function

#End Region

#Region "玩家数据"

    Private Sub BuildPlayers(SaveDir As String)
        PanPlayer.Children.Clear()
        '主玩家：26.1 用 Data.singleplayer_uuid，之前是 Data.Player.UUID
        Dim OwnerUuid As String = DstSavePaths.OwnerUuid(LevelRoot)
        '26.1 的玩家数据在 players\data\，之前的在 playerdata\
        Dim Folder As String = DstSavePaths.PlayerFolder(SaveDir)
        Dim Files As New List(Of String)
        Try
            If DirectoryUtils.Exists(Folder) Then
                For Each F In DirectoryUtils.EnumerateFiles(Folder, False, "*.dat")
                    '排除 .dat_old 备份
                    If DstSavePaths.IsPlayerDataFile(F) Then Files.Add(F)
                Next
            End If
        Catch
        End Try
        If Files.Count = 0 Then
            PanPlayer.Children.Add(EmptyHint("没有玩家数据。单人存档一般不会有，多人联机时才会生成。"))
            Return
        End If
        For Each F In Files
            PanPlayer.Children.Add(BuildPlayerRow(F, OwnerUuid))
        Next
    End Sub

    ''' <summary>玩家行 → 对应的数据文件。行的 Tag 留给标题文本用。</summary>
    Private ReadOnly PlayerFiles As New Dictionary(Of FrameworkElement, String)

    ''' <summary>玩家行 → 实际显示的名字（第一次拿到，后面不再乱改）。</summary>
    Private ReadOnly PlayerTitles As New Dictionary(Of FrameworkElement, String)

#Region "玩家数据编辑"

    ''' <summary>正在编辑的玩家数据文档。</summary>
    Private PlayerDoc As DstNbtDocument = Nothing
    ''' <summary>玩家编辑面板（装在一个 Border 里插到列表里）。</summary>
    Private PlayerCard As Border = Nothing
    ''' <summary>玩家编辑面板里当前显示的块：0 基本属性、1 物品栏、2 末影箱、3 原始 NBT。</summary>
    Private PlayerSection As Integer = 0
    ''' <summary>编辑面板里那块内容区。</summary>
    Private PlayerBody As StackPanel = Nothing
    ''' <summary>编辑面板当前对应的玩家文件。</summary>
    Private PlayerOpenFile As String = ""

    ''' <summary>打开某个玩家数据的编辑界面；同一个玩家再点一次就收起。</summary>
    Private Sub OpenPlayerEditor(File As String)
        '已经展开的这个玩家：收起来
        If PlayerCard IsNot Nothing AndAlso String.Equals(PlayerOpenFile, File, StringComparison.OrdinalIgnoreCase) Then
            If PanPlayer.Children.Contains(PlayerCard) Then PanPlayer.Children.Remove(PlayerCard)
            PlayerCard = Nothing
            PlayerBody = Nothing
            PlayerDoc = Nothing
            PlayerOpenFile = ""
            Return
        End If
        '换一个玩家：先把上一个收掉
        If PlayerCard IsNot Nothing AndAlso PanPlayer.Children.Contains(PlayerCard) Then PanPlayer.Children.Remove(PlayerCard)
        Dim Doc As DstNbtDocument = DstNbtDocument.Open(File)
        If Doc Is Nothing Then
            Hint("读取玩家数据失败", HintType.Red)
            Return
        End If
        PlayerDoc = Doc
        PlayerOpenFile = File
        PlayerSection = 0
        PlayerCard = New Border With {
            .CornerRadius = New CornerRadius(6), .Margin = New Thickness(0, 4, 0, 10),
            .Padding = New Thickness(12), .Background = New SolidColorBrush(BackColorSub)
        }
        Dim Panel As New StackPanel
        Dim Bar As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 0, 0, 10)}
        Dim BtnOverview As New MyButton With {.Text = "基本属性", .Height = 30, .MinWidth = 90}
        Dim BtnInv As New MyButton With {.Text = "物品栏", .Height = 30, .MinWidth = 80, .Margin = New Thickness(8, 0, 0, 0)}
        Dim BtnEnder As New MyButton With {.Text = "末影箱", .Height = 30, .MinWidth = 80, .Margin = New Thickness(8, 0, 0, 0)}
        Dim BtnRaw As New MyButton With {.Text = "原始 NBT", .Height = 30, .MinWidth = 90, .Margin = New Thickness(8, 0, 0, 0)}
        Bar.Children.Add(BtnOverview)
        Bar.Children.Add(BtnInv)
        Bar.Children.Add(BtnEnder)
        Bar.Children.Add(BtnRaw)
        Panel.Children.Add(Bar)
        PlayerBody = New StackPanel
        Panel.Children.Add(PlayerBody)
        AddHandler BtnOverview.Click, Sub() SwitchPlayerSection(0)
        AddHandler BtnInv.Click, Sub() SwitchPlayerSection(1)
        AddHandler BtnEnder.Click, Sub() SwitchPlayerSection(2)
        AddHandler BtnRaw.Click, Sub() SwitchPlayerSection(3)
        RedrawPlayerBody()
        PlayerCard.Child = Panel
        '插到被点的那一行后面
        For i = 0 To PanPlayer.Children.Count - 1
            Dim Row As FrameworkElement = TryCast(PanPlayer.Children(i), FrameworkElement)
            If Row Is Nothing Then Continue For
            Dim RowFile As String = ""
            If PlayerFiles.TryGetValue(Row, RowFile) AndAlso String.Equals(RowFile, File, StringComparison.OrdinalIgnoreCase) Then
                PanPlayer.Children.Insert(i + 1, PlayerCard)
                Return
            End If
        Next
        PanPlayer.Children.Add(PlayerCard)
    End Sub

    Private Sub SwitchPlayerSection(Id As Integer)
        If Id = PlayerSection Then Return
        PlayerSection = Id
        RedrawPlayerBody()
    End Sub

    Private Sub RedrawPlayerBody()
        If PlayerBody Is Nothing OrElse PlayerDoc Is Nothing Then Return
        PlayerBody.Children.Clear()
        Select Case PlayerSection
            Case 0
                BuildPlayerFields(PlayerBody, PlayerDoc)
            Case 1
                Dim ItemPanel As New DstItemPanel(PlayerDoc, "Inventory", PlayerBody,
                                                  Function() PlayerDoc.Save(),
                                                  Sub(Steps) ShowRawEditor(PlayerBody, PlayerDoc, Steps))
                ItemPanel.Refresh()
            Case 2
                Dim EnderPanel As New DstItemPanel(PlayerDoc, "EnderItems", PlayerBody,
                                                   Function() PlayerDoc.Save(),
                                                   Sub(Steps) ShowRawEditor(PlayerBody, PlayerDoc, Steps))
                EnderPanel.Refresh()
            Case 3
                Dim Viewer As New DstNbtViewer(PlayerDoc, PlayerBody, Function() PlayerDoc.Save())
                Viewer.ShowRoot()
        End Select
    End Sub

    Private Sub ShowRawEditor(Host As StackPanel, Doc As DstNbtDocument, Steps As List(Of NbtPathStep))
        Host.Children.Clear()
        Dim Viewer As New DstNbtViewer(Doc, Host, Function() Doc.Save())
        Viewer.ShowAt(Steps)
    End Sub

    ''' <summary>玩家最常改的几个属性。</summary>
    Private Sub BuildPlayerFields(Host As StackPanel, Doc As DstNbtDocument)
        AddPlayerField(Host, Doc, "Health", "生命值", "float")
        AddPlayerField(Host, Doc, "foodLevel", "饥饿值", "int")
        AddPlayerField(Host, Doc, "XpLevel", "经验等级", "int")
        AddPlayerField(Host, Doc, "XpTotal", "经验总量", "int")
        AddPlayerField(Host, Doc, "XpP", "经验进度", "float")
        AddPlayerField(Host, Doc, "Score", "得分", "int")
        AddPlayerField(Host, Doc, "SelectedItemSlot", "选中的快捷栏", "int")
        AddPlayerField(Host, Doc, "playerGameType", "游戏模式", "int")
        AddPlayerField(Host, Doc, "abilities.mayfly", "允许飞行", "bool")
        AddPlayerField(Host, Doc, "abilities.flying", "正在飞行", "bool")
        AddPlayerField(Host, Doc, "abilities.invulnerable", "无敌", "bool")
        AddPlayerField(Host, Doc, "abilities.instabuild", "瞬间破坏方块", "bool")
        AddPlayerField(Host, Doc, "abilities.mayBuild", "允许放置方块", "bool")
        AddPlayerField(Host, Doc, "abilities.flySpeed", "飞行速度", "float")
        AddPlayerField(Host, Doc, "abilities.walkSpeed", "移动速度", "float")
        AddPlayerField(Host, Doc, "Health", "生命值", "float")
        AddPlayerField(Host, Doc, "Dimension", "所在维度", "text")
        AddPlayerField(Host, Doc, "SpawnX", "重生点 X", "int")
        AddPlayerField(Host, Doc, "SpawnY", "重生点 Y", "int")
        AddPlayerField(Host, Doc, "SpawnZ", "重生点 Z", "int")
        AddPlayerField(Host, Doc, "OnGround", "站在地面上", "bool")
        AddPlayerField(Host, Doc, "FallDistance", "下落距离", "float")
        AddPlayerField(Host, Doc, "Fire", "着火刻数", "short")
        AddPlayerField(Host, Doc, "Air", "剩余氧气", "short")
        AddPlayerField(Host, Doc, "Invulnerable", "免疫伤害", "bool")
        If Host.Children.Count = 0 Then
            Host.Children.Add(New TextBlock With {
                .Text = "没找到可编辑的常用属性。", .FontSize = 11.5, .Opacity = 0.5
            })
        End If
    End Sub

    ''' <summary>一行可编辑的玩家属性。Kind 决定用什么控件。</summary>
    Private Sub AddPlayerField(Host As StackPanel, Doc As DstNbtDocument, Key As String, Label As String, Kind As String)
        Dim Tag As NbtTag = DstNbt.GetByPath(Doc.Root, Key)
        '平台值可能藏在根或 Data 下
        If Tag Is Nothing Then Tag = DstNbt.GetByPath(Doc.Root, "Data." & Key)
        If Tag Is Nothing Then Return
        Dim RealKey As String = Key
        Dim Row As New Border With {
            .Height = 40, .CornerRadius = New CornerRadius(5), .Margin = New Thickness(0, 0, 0, 5),
            .Padding = New Thickness(10, 0, 10, 0), .Background = New SolidColorBrush(BackColorMain)
        }
        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto, .MinWidth = 150})
        Dim Lab As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Lab.Children.Add(New TextBlock With {.Text = Label, .FontSize = 12.5, .FontWeight = FontWeights.Bold})
        Lab.Children.Add(New TextBlock With {
            .Text = RealKey & " · " & DstNbt.TypeName(Tag.TagType), .FontSize = 10, .Opacity = 0.45
        })
        G.Children.Add(Lab)
        Dim Ctrl As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center,
                                          .HorizontalAlignment = HorizontalAlignment.Right}
        Select Case Kind
            Case "bool"
                Dim IsOn As Boolean = CLng(Tag.TagValue) <> 0
                Dim Chk As New MyCheckBox With {.Text = If(IsOn, "开", "关"), .Checked = IsOn, .Height = 20, .MinWidth = 52}
                AddHandler Chk.Change,
                    Sub(sender As Object, user As Boolean)
                        If Not user Then Return
                        Tag.TagValue = If(Chk.Checked, CSByte(1), CSByte(0))
                        Chk.Text = If(Chk.Checked, "开", "关")
                        If Doc.Save() Then
                            Hint("已保存", HintType.Blue)
                        Else
                            Hint("保存失败：" & Doc.LastError, HintType.Red)
                        End If
                    End Sub
                Ctrl.Children.Add(Chk)
            Case Else
                Dim Box As New MyTextBox With {
                    .Text = DstNbt.Describe(Tag), .Width = 130, .Height = 26,
                    .VerticalAlignment = VerticalAlignment.Center
                }
                Dim Commit As Action =
                    Sub()
                        If Not DstNbt.SetText(Tag, Box.Text) Then
                            Hint("无法解析这个值", HintType.Red)
                            Return
                        End If
                        If Doc.Save() Then
                            Hint("已保存", HintType.Blue)
                        Else
                            Hint("保存失败：" & Doc.LastError, HintType.Red)
                        End If
                    End Sub
                Ctrl.Children.Add(Box)
                Dim Btn As New MyButton With {.Text = "保存", .Height = 26, .MinWidth = 54, .Margin = New Thickness(6, 0, 0, 0)}
                AddHandler Btn.Click, Sub() Commit()
                Ctrl.Children.Add(Btn)
        End Select
        Grid.SetColumn(Ctrl, 1)
        G.Children.Add(Ctrl)
        Row.Child = G
        Host.Children.Add(Row)
    End Sub

#End Region

    ''' <summary>一个玩家数据的条目。</summary>
    Private Function BuildPlayerRow(File As String, OwnerUuid As String) As FrameworkElement
        Dim PlayerName As String = ""
        Dim SubText As String = "读取失败"
        Try
            Dim Root As NbtTag = DstNbt.ReadFile(File)
            Dim NameTag As NbtTag = PlayerField(Root, "Name")
            If NameTag IsNot Nothing Then PlayerName = CType(NameTag.TagValue, String)
            Dim Parts As New List(Of String)
            Dim HealthTag As NbtTag = PlayerField(Root, "Health")
            If HealthTag IsNot Nothing Then Parts.Add("生命值 " & FormatDouble(CDbl(HealthTag.TagValue), 1))
            Dim DimTag As NbtTag = PlayerField(Root, "Dimension")
            If DimTag IsNot Nothing Then
                If DimTag.TagType = DstNbt.TypeString Then
                    Parts.Add(DimNameOfName(DirectCast(DimTag.TagValue, String)))
                Else
                    Parts.Add(DimNameOfId(CInt(DimTag.TagValue)))
                End If
            End If
            Dim XpTag As NbtTag = PlayerField(Root, "XpLevel")
            If XpTag IsNot Nothing Then Parts.Add("经验等级 " & CInt(XpTag.TagValue))
            Dim PosTag As NbtTag = PlayerField(Root, "Pos")
            If PosTag IsNot Nothing Then
                Dim PosItems As List(Of NbtTag) = TryCast(PosTag.TagValue, List(Of NbtTag))
                If PosItems IsNot Nothing AndAlso PosItems.Count >= 3 Then
                    Parts.Add("坐标 " & FormatDouble(CDbl(PosItems(0).TagValue), 1) & ", " &
                                     FormatDouble(CDbl(PosItems(1).TagValue), 1) & ", " &
                                     FormatDouble(CDbl(PosItems(2).TagValue), 1))
                End If
            End If
            SubText = If(Parts.Count = 0, "已读取", String.Join(" · ", Parts))
        Catch ex As Exception
            SubText = "读取失败：" & ex.Message
        End Try
        Dim Buttons As New List(Of FrameworkElement)
        Dim BtnOpen As New MyIconButton With {
            .Logo = Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "打开所在文件夹"
        }
        AddHandler BtnOpen.Click, Sub() OpenExplorer(PathUtils.RemoveLastPart(File))
        Buttons.Add(BtnOpen)
        Dim BtnDelete As New MyIconButton With {
            .Logo = Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "删除玩家数据"
        }
        AddHandler BtnDelete.Click, Sub() DeleteItem(File, "玩家数据")
        Buttons.Add(BtnDelete)
        Dim BtnEdit As New MyIconButton With {
            .Logo = "M732.64 64.32C688.576 21.216 613.696 21.216 569.6 64.32L120.128 499.52c-17.6 12.896-26.432 30.144-30.848 51.68L32 870.048c0 25.856 8.8 56 26.432 73.248 17.632 17.216 17.632 48.704 88.64 48.704h13.248l326.08-56c22.016-4.32 39.68-12.928 52.864-30.176l449.472-435.2c22.048-21.536 35.264-47.36 35.264-77.536 0-30.176-13.216-56-35.264-77.568l-256.096-251.2zM139.712 903.776l56-326.912 311.04-295.136 267.104 269.44-310.976 295.168-323.168 57.44zM844.576 467.84l-273.984-260.672 61.856-59.84c8.832-8.512 26.528-8.512 39.776 0l234.24 226.496c4.384 4.288 8.832 12.8 8.832 17.088s-4.416 8.544-8.864 12.8l-61.856 64.128z",
            .Width = 26, .Height = 26, .LogoScale = 0.8,
            .Margin = New Thickness(6, 0, 0, 0), .ToolTip = "编辑这个玩家的数据"
        }
        AddHandler BtnEdit.Click, Sub() OpenPlayerEditor(File)
        Buttons.Add(BtnEdit)
        If PlayerName = "" Then
            Dim FileUuid As String = PathUtils.GetFileNameWithoutExtension(File)
            If OwnerUuid <> "" AndAlso FileUuid.Equals(OwnerUuid, StringComparison.OrdinalIgnoreCase) Then
                PlayerName = "主玩家"
            Else
                PlayerName = FileUuid
            End If
        End If
        '头像先用内置 Steve，查到皮肤再换
        Dim Head As New Image With {.Stretch = Stretch.UniformToFill, .Source = SteveHead()}
        Dim Row As FrameworkElement = BuildRowWith(Head, PlayerName, SubText, "", Buttons)
        PlayerFiles(Row) = File
        PlayerTitles(Row) = PlayerName
        '行本身也点了就展开编辑面板
        AddHandler CType(Row, Border).MouseLeftButtonUp, Sub() OpenPlayerEditor(File)
        LoadPlayerHead(File, Head, Row)
        Return Row
    End Function

    ''' <summary>改玩家行的标题。原来带「（主玩家）」后缀时保留后缀。</summary>
    Private Sub SetPlayerTitle(Row As FrameworkElement, NewName As String)
        If Row Is Nothing OrElse NewName = "" Then Return
        Dim Panel As StackPanel = TryCast(Row.Tag, StackPanel)
        If Panel Is Nothing OrElse Panel.Children.Count = 0 Then Return
        Dim TitleBlock As TextBlock = TryCast(Panel.Children(0), TextBlock)
        If TitleBlock Is Nothing Then Return
        Dim Old As String = ""
        PlayerTitles.TryGetValue(Row, Old)
        Dim Suffix As String = If(Old.Contains("（主玩家）"), "（主玩家）", "")
        PlayerTitles(Row) = NewName & Suffix
        TitleBlock.Text = NewName & Suffix
    End Sub

    ''' <summary>玩家头像的本地缓存目录。</summary>
    Private ReadOnly Property HeadFolder As String
        Get
            Return PathTemp & "DstSaveHead\"
        End Get
    End Property

    Private Function HeadCacheFile(Uuid As String) As String
        Return HeadFolder & Uuid.ToLower() & ".png"
    End Function

    ''' <summary>
    ''' 默认头像：PCL 自带的正版头图。
    ''' 注意不要从 Images\Skins\Steve.png 里裁 8×8，那张皮肤图最上面八行是空的，
    ''' 裁出来是一块空白，看上去就像没加载出来。
    ''' </summary>
    Private Function SteveHead() As ImageSource
        Static Cached As ImageSource = Nothing
        If Cached IsNot Nothing Then Return Cached
        Try
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.UriSource = New Uri(PathImage & "Heads/00ll00.png", UriKind.Absolute)
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.EndInit()
            Bmp.Freeze()
            Cached = Bmp
            Return Cached
        Catch ex As Exception
            Logger.Warn(ex, "加载默认头像失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 用 UUID 去 Mojang 查玩家名，并把皮肤头像下载到本地。
    ''' 查不到就保留 Steve 头像，名字也保持原样。
    ''' </summary>
    Private Sub LoadPlayerHead(File As String, Head As Image, Row As FrameworkElement)
        Dim Uuid As String = PathUtils.GetFileNameWithoutExtension(File)
        If Not IsUuidText(Uuid) Then Return
        Dim Id As String = Uuid.Replace("-", "")
        RunInThread(
        Sub()
            '名字：Mojang 的会话服务器，不需要登录，通常一秒内返回
            Try
                Dim Json As String = NetRequestByClient("https://sessionserver.mojang.com/session/minecraft/profile/" & Id, Timeout:=8000, MakeLog:=False)
                If Not String.IsNullOrWhiteSpace(Json) Then
                    Dim Obj As JObject = JObject.Parse(Json)
                    Dim PlayerName As String = ""
                    If Obj("name") IsNot Nothing Then PlayerName = Obj("name").ToString()
                    If PlayerName <> "" Then
                        RunInUi(
                        Sub()
                            SetPlayerTitle(Row, PlayerName)
                        End Sub)
                    End If
                End If
            Catch ex As Exception
                Logger.Info("查询玩家名失败（" & Uuid & "）：" & ex.Message)
            End Try
            '头像：先看缓存；国内访问 mc-heads 常常超时，所以放到最后
            Dim Cached As String = HeadCacheFile(Uuid)
            Dim Got As String = ""
            Try
                If FileUtils.Exists(Cached) AndAlso New IO.FileInfo(Cached).Length > 100 Then Got = Cached
            Catch
            End Try
            If Got = "" Then
                Dim Urls As String() = {
                    "https://minotar.net/helm/" & Id & "/64.png",
                    "https://crafatar.com/avatars/" & Id & "?size=64&overlay",
                    "https://mc-heads.net/avatar/" & Id & "/64"
                }
                For Each Url In Urls
                    Try
                        DirectoryUtils.Create(HeadFolder)
                        NetDownloadByClient(Url, Cached)
                        If FileUtils.Exists(Cached) AndAlso New IO.FileInfo(Cached).Length > 100 Then
                            Got = Cached
                            Exit For
                        End If
                    Catch ex As Exception
                        Logger.Info("下载玩家头像失败（" & Url & "）：" & ex.Message)
                    End Try
                Next
            End If
            If Got = "" Then Return
            Dim HeadFile As String = Got
            RunInUi(
            Sub()
                Try
                    Dim Bmp As New BitmapImage()
                    Bmp.BeginInit()
                    Bmp.UriSource = New Uri(HeadFile, UriKind.Absolute)
                    Bmp.CacheOption = BitmapCacheOption.OnLoad
                    Bmp.DecodePixelWidth = 64
                    Bmp.EndInit()
                    Bmp.Freeze()
                    Head.Source = Bmp
                Catch ex As Exception
                    Logger.Warn(ex, "加载玩家头像失败")
                End Try
            End Sub)
        End Sub)
    End Sub

    Private Function IsUuidText(Text As String) As Boolean
        If Text.Length <> 36 Then Return False
        Dim Dashes As Integer = 0
        For Each C In Text
            If C = "-"c Then
                Dashes += 1
            ElseIf Not Uri.IsHexDigit(C) Then
                Return False
            End If
        Next
        Return Dashes = 4
    End Function

    ''' <summary>玩家数据里的字段有的在根上，有的在 Data 下。</summary>
    Private Function PlayerField(Root As NbtTag, Name As String) As NbtTag
        Dim Tag As NbtTag = DstNbt.CompoundGet(Root, Name)
        If Tag Is Nothing Then Tag = DstNbt.GetByPath(Root, "Data." & Name)
        Return Tag
    End Function

    '' 1.16.2 之后，玩家的维度字段是资源位置字符串
    Private Function DimNameOfName(Name As String) As String
        If Name.EndsWith("the_nether", StringComparison.OrdinalIgnoreCase) Then Return "下界"
        If Name.EndsWith("the_end", StringComparison.OrdinalIgnoreCase) Then Return "末地"
        If Name.EndsWith("overworld", StringComparison.OrdinalIgnoreCase) Then Return "主世界"
        Return Name
    End Function

    '' 把 UUID 的 IntArray 转成带短横线的字符串
    Private Function UuidFromInts(Ints As Integer()) As String
        If Ints Is Nothing OrElse Ints.Length < 4 Then Return ""
        Dim Hex As New List(Of String)
        For Each I In Ints
            Hex.Add(I.ToString("x8"))
        Next
        Dim S As String = String.Join("", Hex)
        If S.Length <> 32 Then Return ""
        Return S.Substring(0, 8) & "-" & S.Substring(8, 4) & "-" & S.Substring(12, 4) & "-" &
               S.Substring(16, 4) & "-" & S.Substring(20, 12)
    End Function

    Private Function DimNameOfId(Id As Integer) As String
        Select Case Id
            Case 0 : Return "主世界"
            Case -1 : Return "下界"
            Case 1 : Return "末地"
            Case Else : Return "维度 " & Id
        End Select
    End Function

#End Region

#Region "通用方法"

    Private Function ReadLevelDat(SaveDir As String) As NbtTag
        Dim LevelDat As String = SaveDir & "level.dat"
        If Not FileUtils.Exists(LevelDat) Then Return Nothing
        Try
            Return DstNbt.ReadFile(LevelDat)
        Catch ex As Exception
            Logger.Warn(ex, "读取 level.dat 失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 写入 level.dat。先写临时文件并回读校验，通过后再备份并覆盖，出错时原文件还在。
    ''' </summary>
    Private Function SaveLevelDat(SaveDir As String, Root As NbtTag) As Boolean
        Dim LevelDat As String = SaveDir & "level.dat"
        Dim Temp As String = LevelDat & ".dsttmp"
        Try
            DstNbt.WriteFile(Temp, Root, True)
            '回读校验
            Dim Check As NbtTag = DstNbt.ReadFile(Temp)
            Dim OrigName As NbtTag = DstNbt.GetByPath(Root, "Data.LevelName")
            Dim NewName As NbtTag = DstNbt.GetByPath(Check, "Data.LevelName")
            If OrigName Is Nothing OrElse NewName Is Nothing OrElse
               CType(OrigName.TagValue, String) <> CType(NewName.TagValue, String) Then
                If FileUtils.Exists(Temp) Then FileUtils.Delete(Temp)
                Return False
            End If
            '备份后覆盖
            Dim Bak As String = LevelDat & ".bak"
            If FileUtils.Exists(Bak) Then FileUtils.Delete(Bak)
            FileUtils.Copy(LevelDat, Bak)
            FileUtils.Delete(LevelDat)
            FileUtils.Move(Temp, LevelDat)
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "保存 level.dat 失败")
            Try
                If FileUtils.Exists(Temp) Then FileUtils.Delete(Temp)
            Catch
            End Try
            Return False
        End Try
    End Function

    Private Sub DeleteItem(Path As String, Kind As String)
        Dim ItemName As String = PathUtils.GetLastPart(Path)
        If MyMsgBox("确定要删除" & Kind & "「" & ItemName & "」吗？" & vbCrLf & "此操作不可撤销。",
                    "删除" & Kind, "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            If DirectoryUtils.Exists(Path) Then
                DirectoryUtils.Delete(Path, True)
            Else
                FileUtils.Delete(Path, True)
            End If
            Hint(Kind & "已删除", HintType.Green, False)
            RefreshContent()
        Catch ex As Exception
            Logger.Error(ex, "删除" & Kind & "失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Function EmptyHint(Text As String) As FrameworkElement
        Return New TextBlock With {
            .Text = Text, .FontSize = 11.5, .Opacity = 0.55, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 0, 0, 4)
        }
    End Function

    Private Shared Function FormatSize(Bytes As Long) As String
        If Bytes < 1024 Then Return Bytes & " B"
        If Bytes < 1024 * 1024 Then Return Math.Round(Bytes / 1024, 1) & " KB"
        Return Math.Round(Bytes / 1024 / 1024, 1) & " MB"
    End Function

    Private Shared Function FormatDouble(Value As Double, Digits As Integer) As String
        Return Math.Round(Value, Digits).ToString()
    End Function

    ''' <summary>一行条目，跟 Mod 管理一样：图标 + 名称 + 说明 + 右侧按钮。</summary>
    Private Function BuildRow(IconSource As ImageSource, Title As String, SubText As String, RightText As String,
                              Buttons As List(Of FrameworkElement), Optional ShowIcon As Boolean = True,
                              Optional IconGlyph As String = "") As FrameworkElement
        Dim IconElement As FrameworkElement = Nothing
        If ShowIcon Then
            If IconGlyph <> "" Then
                IconElement = GlyphIcon(IconGlyph, BackColorSub)
            ElseIf IconSource Is Nothing Then
                IconElement = New MyImage With {.Stretch = Stretch.UniformToFill, .Source = PathImage & "Icons/NoIcon.png"}
            Else
                IconElement = New Image With {.Stretch = Stretch.UniformToFill, .Source = IconSource}
            End If
        End If
        Return BuildRowWith(IconElement, Title, SubText, RightText, Buttons)
    End Function

    ''' <summary>
    ''' 同一行，但图标元素由调用方提供（玩家头像需要异步替换）。
    ''' 行的 Tag 会指向标题的 TextBlock，方便之后改名字。
    ''' </summary>
    Private Function BuildRowWith(IconElement As FrameworkElement, Title As String, SubText As String, RightText As String,
                                  Buttons As List(Of FrameworkElement)) As FrameworkElement
        Dim Row As New Border With {
            .Height = 48, .CornerRadius = New CornerRadius(6), .Margin = New Thickness(0, 0, 0, 6),
            .Padding = New Thickness(10, 0, 10, 0), .Background = New SolidColorBrush(BackColorSub)
        }
        Dim G As New Grid
        Dim FirstColumn As Integer = 0
        If IconElement IsNot Nothing Then
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Dim IconBox As New Border With {
                .Width = 32, .Height = 32, .CornerRadius = New CornerRadius(4),
                .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center,
                .Background = New SolidColorBrush(BackColorMain)
            }
            IconBox.Child = IconElement
            G.Children.Add(IconBox)
            FirstColumn = 1
        End If
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim Info As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Dim TitleBlock As New TextBlock With {
            .Text = Title, .FontSize = 12.5, .FontWeight = FontWeights.Bold,
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        Info.Children.Add(TitleBlock)
        If SubText <> "" Then
            Info.Children.Add(New TextBlock With {
                .Text = SubText, .FontSize = 11, .Opacity = 0.5, .Margin = New Thickness(0, 2, 0, 0),
                .TextTrimming = TextTrimming.CharacterEllipsis
            })
        End If
        Grid.SetColumn(Info, FirstColumn)
        G.Children.Add(Info)

        Dim RightBox As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center
        }
        If RightText <> "" Then
            RightBox.Children.Add(New TextBlock With {
                .Text = RightText, .FontSize = 11, .Opacity = 0.45,
                .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 10, 0)
            })
        End If
        If Buttons IsNot Nothing Then
            For Each Btn In Buttons
                RightBox.Children.Add(Btn)
            Next
        End If
        Grid.SetColumn(RightBox, FirstColumn + 1)
        G.Children.Add(RightBox)

        Row.Child = G
        Row.Tag = TitleBlock
        Return Row
    End Function

    ''' <summary>用矢量图形作图标（填充跟随主题色）。</summary>
    Private Function GlyphIcon(Data As String, Background As Color) As FrameworkElement
        Dim G As New Canvas With {
            .Width = 18, .Height = 18, .Margin = New Thickness(7),
            .VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Center
        }
        Dim Outline As New System.Windows.Shapes.Path With {
            .Data = Geometry.Parse(Data), .Stretch = Stretch.Uniform, .Width = 18, .Height = 18,
            .StrokeThickness = 1.7, .StrokeLineJoin = PenLineJoin.Round, .StrokeStartLineCap = PenLineCap.Round,
            .StrokeEndLineCap = PenLineCap.Round
        }
        Outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ColorBrush1")
        G.Children.Add(Outline)
        Return G
    End Function

    ''' <summary>数据包图标常用的文件名，按优先级排列。</summary>
    Private ReadOnly PackIconNames As String() = {"pack.png", "icon.png", "logo.png", "globe.png", "cover.png", "banner.png"}

    ''' <summary>
    ''' 数据包图标。优先 pack.png，其次是根目录里的其它 png
    ''' （不少整合包把图标叫 globe.png / icon.png，并不叫 pack.png）。
    ''' </summary>
    Private Function PackIcon(Item As String) As ImageSource
        Try
            If IsPackZip(Item) Then
                Using Zip As ZipArchive = ZipFile.OpenRead(Item)
                    Dim Entry As ZipArchiveEntry = FindZipImage(Zip)
                    If Entry Is Nothing Then Return Nothing
                    Return LoadImage(Entry)
                End Using
            Else
                Dim Folder As String = PathUtils.AddSlashSuffix(Item)
                For Each FileName In PackIconNames
                    If FileUtils.Exists(Folder & FileName) Then Return LoadImageFile(Folder & FileName)
                Next
                Try
                    For Each F In DirectoryUtils.EnumerateFiles(Folder, False, "*.png")
                        Return LoadImageFile(F)
                    Next
                Catch
                End Try
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取数据包图标失败：" & Item)
        End Try
        Return Nothing
    End Function

    ''' <summary>是不是压缩包形态的数据包（启用中的 .zip，或停用的 .zip.disable）。</summary>
    Private Function IsPackZip(Item As String) As Boolean
        If Item.EndsWith(".zip.disable", StringComparison.OrdinalIgnoreCase) Then Return True
        Return Item.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>在压缩包根目录里找一个图片当图标。</summary>
    Private Function FindZipImage(Zip As ZipArchive) As ZipArchiveEntry
        For Each FileName In PackIconNames
            For Each Item In Zip.Entries
                If Item.FullName.Equals(FileName, StringComparison.OrdinalIgnoreCase) Then Return Item
            Next
        Next
        '根目录下任意 png
        For Each Item2 In Zip.Entries
            If Item2.FullName.IndexOf("/"c) < 0 AndAlso Item2.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) Then Return Item2
        Next
        Return Nothing
    End Function

    Private Function LoadImage(Entry As ZipArchiveEntry) As ImageSource
        Using Ms As New MemoryStream()
            Using Stream As Stream = Entry.Open()
                Stream.CopyTo(Ms)
            End Using
            Ms.Position = 0
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.StreamSource = Ms
            Bmp.EndInit()
            Bmp.Freeze()
            Return Bmp
        End Using
    End Function

    Private Function LoadImageFile(Path As String) As ImageSource
        Using Fs As New FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read)
            Using Ms As New MemoryStream()
                Fs.CopyTo(Ms)
                Ms.Position = 0
                Dim Bmp As New BitmapImage()
                Bmp.BeginInit()
                Bmp.CacheOption = BitmapCacheOption.OnLoad
                Bmp.StreamSource = Ms
                Bmp.EndInit()
                Bmp.Freeze()
                Return Bmp
            End Using
        End Using
    End Function

    ''' <summary>数据包说明，取自 pack.mcmeta。</summary>
    Private Function PackDesc(Item As String) As String
        Try
            Dim Meta As String = ""
            If IsPackZip(Item) Then
                Using Zip As ZipArchive = ZipFile.OpenRead(Item)
                    Dim Entry As ZipArchiveEntry = Zip.GetEntry("pack.mcmeta")
                    If Entry Is Nothing Then Return ""
                    Using Stream As Stream = Entry.Open()
                        Using Reader As New StreamReader(Stream)
                            Meta = Reader.ReadToEnd()
                        End Using
                    End Using
                End Using
            Else
                Dim MetaPath As String = PathUtils.AddSlashSuffix(Item) & "pack.mcmeta"
                If Not FileUtils.Exists(MetaPath) Then Return ""
                Meta = FileUtils.ReadAsString(MetaPath)
            End If
            Dim Json As JObject = JObject.Parse(Meta)
            Dim Pack As JObject = Json("pack")
            If Pack Is Nothing Then Return ""
            Dim Desc As JToken = Pack("description")
            If Desc Is Nothing Then Return ""
            If Desc.Type = JTokenType.String Then Return Desc.ToString()
            Return ""
        Catch
            Return ""
        End Try
    End Function

#End Region

End Class
