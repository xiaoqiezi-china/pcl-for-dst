''' <summary>
''' 版本设置 → 存档管理。
''' 目录一律走 Instance.PathIndie，这样版本隔离开不开都能取到正确的 saves 位置。
''' 点进某个存档会打开「存档详情」副页面，和点击「版本设置」进入的方式一样。
''' </summary>
Public Class PageInstanceSaves

    ''' <summary>当前版本应用隔离后的 Minecraft 根目录。</summary>
    Private ReadOnly Property SaveRoot As String
        Get
            Dim Inst As McInstance = CurInstance
            If Inst Is Nothing Then Return ""
            Return Inst.PathIndie & "saves\"
        End Get
    End Property

    ''' <summary>当前版本设置页面对的实例。</summary>
    Private ReadOnly Property CurInstance As McInstance
        Get
            'Instance 是 PageInstanceLeft 的共享成员，直接走类型访问
            If PageInstanceLeft.Instance IsNot Nothing Then Return PageInstanceLeft.Instance
            Return ModMinecraft.McInstanceSelected
        End Get
    End Property

    Private Sub PageInstanceSaves_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '多选条用 PCL 自带的图标，和模组管理保持一致
        BtnSelectDelete.Logo = Logo.IconButtonDelete
        BtnSelectCancel.Logo = Logo.IconButtonCross
        RefreshSaves()
    End Sub

    Public Sub RefreshSaves()
        If PanSaves Is Nothing Then Return
        Dim Root As String = SaveRoot
        CurrentRoot = Root
        PanSaves.Children.Clear()
        If Root = "" Then
            PanSaves.Children.Add(EmptyHint("请先选择一个版本"))
            Return
        End If
        If Not DirectoryUtils.Exists(Root) Then
            PanSaves.Children.Add(EmptyHint("这个版本还没有存档。进游戏新建一个世界，或者把别人的存档文件夹放进上面那个目录。"))
            Return
        End If
        Dim Dirs As List(Of String)
        Try
            Dirs = DirectoryUtils.EnumerateDirectories(Root).ToList()
        Catch ex As Exception
            Logger.Warn(ex, "枚举存档目录失败")
            PanSaves.Children.Add(EmptyHint("读取存档目录失败：" & ex.Message))
            Return
        End Try
        If Dirs.Count = 0 Then
            PanSaves.Children.Add(EmptyHint("这个版本还没有存档"))
            Return
        End If
        '按修改时间倒序，最近玩过的排前面
        Dirs.Sort(Function(A, B)
                      Dim TA As Date = Date.MinValue, TB As Date = Date.MinValue
                      Try
                          TA = DirectoryUtils.GetInfo(A).LastWriteTime
                      Catch ex As Exception
                          Logger.Warn(ex, $"读取存档修改时间失败：{A}")
                      End Try
                      Try
                          TB = DirectoryUtils.GetInfo(B).LastWriteTime
                      Catch ex As Exception
                          Logger.Warn(ex, $"读取存档修改时间失败：{B}")
                      End Try
                      Return TB.CompareTo(TA)
                  End Function)
        AllSaves = Dirs
        ApplyFilter()
    End Sub

    '最近一次扫描到的存档列表，供搜索与筛选复用
    Private AllSaves As New List(Of String)
    Private CurrentRoot As String = ""
    '当前显示出来的存档，全选只作用于它们
    Private ShownSaves As New List(Of String)
    '多选：保存被选中存档的完整路径
    Private SelectedPaths As New HashSet(Of String)

    ''' <summary>按搜索框与筛选标签过滤后重新绘制列表。</summary>
    Private Sub ApplyFilter()
        If PanSaves Is Nothing Then Return
        PanSaves.Children.Clear()
        If AllSaves.Count = 0 Then Return

        Dim Keyword As String = If(SearchBox Is Nothing, "", SearchBox.Text.Trim())
        Dim WithIcon As Integer = 0 '0=全部 1=有图标 2=无图标
        If BtnFilterEnabled IsNot Nothing AndAlso BtnFilterEnabled.Checked Then WithIcon = 1
        If BtnFilterDisabled IsNot Nothing AndAlso BtnFilterDisabled.Checked Then WithIcon = 2

        Dim Shown As New List(Of String)
        For Each SaveDir In AllSaves
            Dim Name As String = PathUtils.GetLastPart(SaveDir)
            If Keyword <> "" AndAlso
               Name.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            If WithIcon <> 0 Then
                Dim HasIcon As Boolean = FileUtils.Exists(SaveDir & "\icon.png")
                If WithIcon = 1 AndAlso Not HasIcon Then Continue For
                If WithIcon = 2 AndAlso HasIcon Then Continue For
            End If
            Shown.Add(SaveDir)
        Next

        ShownSaves = Shown
        If Shown.Count = 0 Then
            PanSaves.Children.Add(EmptyHint("没有符合条件的存档"))
            RefreshSelectBar()
            Return
        End If
        For Each SaveDir In Shown
            PanSaves.Children.Add(BuildSaveRow(SaveDir))
        Next
        RefreshSelectBar()
    End Sub

#Region "多选"

    Private Sub ToggleSelect(SaveDir As String)
        If Not SelectedPaths.Remove(SaveDir) Then SelectedPaths.Add(SaveDir)
        ApplyFilter()
    End Sub

    Private Sub ChangeAllSelected(ShouldSelect As Boolean)
        SelectedPaths.Clear()
        If ShouldSelect Then
            For Each S In ShownSaves
                SelectedPaths.Add(S)
            Next
        End If
        ApplyFilter()
    End Sub

    Private Sub RefreshSelectBar()
        If CardSelect Is Nothing Then Return
        SelectedPaths.RemoveWhere(Function(P) Not AllSaves.Any(Function(S) S = P))
        Dim Count As Integer = SelectedPaths.Count
        If Count = 0 Then
            CardSelect.Visibility = Visibility.Collapsed
            Return
        End If
        LabSelect.Text = $"已选择 {Count} 个存档"
        If CardSelect.Visibility <> Visibility.Visible Then
            CardSelect.Visibility = Visibility.Visible
            CardSelect.Opacity = 1
        End If
    End Sub

    Private Sub BatchDelete()
        Dim Targets = AllSaves.Where(Function(S) SelectedPaths.Contains(S)).ToList()
        If Targets.Count = 0 Then Return
        If MyMsgBox($"确定删除这 {Targets.Count} 个存档吗？删除后无法恢复。", "删除存档", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Dim Done As Integer = 0
        For Each SaveDir In Targets
            Try
                DirectoryUtils.Delete(SaveDir, True)
                Done += 1
            Catch ex As Exception
                Logger.Warn(ex, "删除存档失败：" & SaveDir)
            End Try
        Next
        Hint($"已删除 {Done} 个存档", HintType.Green)
        SelectedPaths.Clear()
        RefreshSaves()
    End Sub

#End Region

    '多选操作条
    Private Sub BtnSelectAll_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSelectAll.Click
        ChangeAllSelected(SelectedPaths.Count < ShownSaves.Count)
    End Sub

    Private Sub BtnSelectDelete_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectDelete.Click
        BatchDelete()
    End Sub

    Private Sub BtnSelectCancel_Click(sender As Object, e As RouteEventArgs) Handles BtnSelectCancel.Click
        ChangeAllSelected(False)
    End Sub

    '搜索与筛选：只重绘列表，不重新扫盘
    Private Sub SearchBox_TextChanged(sender As Object, e As EventArgs) Handles SearchBox.TextChanged
        ApplyFilter()
    End Sub

    Private Sub Filter_Check(sender As Object, raiseByMouse As Boolean) Handles BtnFilterAll.Check, BtnFilterEnabled.Check, BtnFilterDisabled.Check
        Dim Btn = TryCast(sender, MyRadioButton)
        If Btn Is Nothing OrElse Not Btn.Checked Then Return
        ApplyFilter()
    End Sub

    Private Function EmptyHint(Text As String) As FrameworkElement
        Return New TextBlock With {
            .Text = Text, .FontSize = 12, .Opacity = 0.55, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(6, 8, 0, 0)
        }
    End Function

    ''' <summary>
    ''' 打开存档详情子页面。和点击「版本设置」进入的方式完全一致：标题栏会出现返回箭头。
    ''' </summary>
    Private Sub OpenDetail(SaveDir As String)
        PageInstanceSaveDetail.TargetSave = SaveDir
        FrmMain.PageChange(FormMain.PageType.InstanceSaveDetail)
    End Sub

    ''' <summary>一行存档：图标 + 名称 + 修改时间 / 大小。</summary>
    Private Function BuildSaveRow(SaveDir As String) As FrameworkElement
        Dim Name As String = PathUtils.GetLastPart(SaveDir)
        '直接复用模组管理的列表项控件：选中竖条、悬停与点击动画全部继承
        Dim Item As New MyLocalModItem With {
            .Title = Name,
            .Description = "正在读取……",
            .ItemIsEnabled = True,
            .Checked = SelectedPaths.Contains(SaveDir),
            .LogoSource = If(FileUtils.Exists(SaveDir & "\icon.png"), SaveDir & "\icon.png", ""),
            .ToolTip = SaveDir
        }
        Dim IconButtons As New List(Of MyIconButton)
        Dim BtnDetail As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonInfo, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "存档信息"
        }
        Dim BtnOpen As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "打开文件夹"
        }
        Dim BtnDelete As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "删除存档"
        }
        IconButtons.Add(BtnDetail)
        IconButtons.Add(BtnOpen)
        IconButtons.Add(BtnDelete)
        Item.Buttons = IconButtons

        '大小与文件数放后台算，别卡界面
        RunInThread(
        Sub()
            Dim Size As Long = 0
            Dim Files As Integer = 0
            Try
                For Each F In DirectoryUtils.EnumerateFiles(SaveDir, True)
                    Try
                        Size += FileUtils.GetInfo(F).Length
                        Files += 1
                    Catch ex As Exception
                        Logger.Warn(ex, $"统计存档大小时跳过文件：{F}")
                    End Try
                Next
            Catch ex As Exception
                Logger.Warn(ex, $"统计存档大小失败：{SaveDir}")
            End Try
            Dim Text As String = $"{FormatSize(Size)} · {Files} 个文件"
            RunInUi(Sub() Item.Description = Text)
        End Sub)

        AddHandler BtnOpen.Click, Sub() OpenExplorer(SaveDir)
        AddHandler BtnDetail.Click, Sub() ShowSaveInfo(SaveDir)
        AddHandler BtnDelete.Click,
            Sub()
                If MyMsgBox("确定要删除存档「" & Name & "」吗？" & vbCrLf &
                            "此操作不可撤销。", "删除存档", "删除", "取消", IsWarn:=True) <> 1 Then Return
                Try
                    DirectoryUtils.Delete(SaveDir, True)
                    Hint("存档已删除", HintType.Green, False)
                    RefreshSaves()
                Catch ex As Exception
                    Logger.Error(ex, "删除存档失败", LogBehavior.Alert)
                End Try
            End Sub
        '左键只负责勾选，与模组管理、收藏夹一致，避免点一下就被详情页抢走
        AddHandler Item.Click, Sub() ToggleSelect(SaveDir)
        '右键进详情
        AddHandler Item.MouseRightButtonUp, Sub() OpenDetail(SaveDir)
        Return Item
    End Function

    Private Shared Function FormatSize(Bytes As Long) As String
        If Bytes < 1024 Then Return Bytes & " B"
        If Bytes < 1024 * 1024 Then Return Math.Round(Bytes / 1024, 1) & " KB"
        Return Math.Round(Bytes / 1024 / 1024, 1) & " MB"
    End Function

    ''' <summary>
    ''' 存档信息：大小、文件数、创建与修改时间，以及 level.dat 里记的世界名与游戏版本。
    ''' </summary>
    Private Sub ShowSaveInfo(SaveDir As String)
        RunInThread(
        Sub()
            Dim Size As Long = 0
            Dim Files As Integer = 0
            Dim Subs As Integer = 0
            Try
                For Each F In DirectoryUtils.EnumerateFiles(SaveDir, True)
                    Try
                        Size += FileUtils.GetInfo(F).Length
                        Files += 1
                    Catch ex As Exception
                        Logger.Warn(ex, $"统计存档大小时跳过文件：{F}")
                    End Try
                Next
                For Each D In DirectoryUtils.EnumerateDirectories(SaveDir, True)
                    Subs += 1
                Next
            Catch ex As Exception
                Logger.Warn(ex, "统计存档大小失败")
            End Try
            Dim Created As String = "未知"
            Dim Modified As String = "未知"
            Try
                Dim Info = DirectoryUtils.GetInfo(SaveDir)
                Created = Info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
                Modified = Info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
            Catch ex As Exception
                Logger.Warn(ex, $"读取存档创建时间失败：{SaveDir}")
            End Try
            Dim WorldName As String = ""
            Dim GameVersion As String = ""
            Dim DataVersion As String = ""
            Try
                Dim Root As NbtTag = DstNbt.ReadFile(PathUtils.AddSlashSuffix(SaveDir) & "level.dat")
                Dim Data As NbtTag = DstNbt.CompoundGet(Root, "Data")
                If Data IsNot Nothing Then
                    Dim NameTag As NbtTag = DstNbt.CompoundGet(Data, "LevelName")
                    If NameTag IsNot Nothing Then WorldName = CType(NameTag.TagValue, String)
                    Dim IdTag As NbtTag = DstNbt.CompoundGet(Data, "DataVersion")
                    If IdTag IsNot Nothing Then DataVersion = IdTag.TagValue.ToString
                End If
                Dim VerName As NbtTag = DstNbt.GetByPath(Root, "Data.Version.Name")
                If VerName IsNot Nothing Then GameVersion = CType(VerName.TagValue, String)
            Catch ex As Exception
                Logger.Warn(ex, "读取存档 level.dat 失败")
            End Try
            Dim Sb As New Text.StringBuilder
            Sb.AppendLine("文件夹：" & PathUtils.GetLastPart(SaveDir))
            If WorldName <> "" Then Sb.AppendLine("世界名：" & WorldName)
            Sb.AppendLine("大小：" & FormatSize(Size) & "（" & Files & " 个文件，" & Subs & " 个文件夹）")
            Sb.AppendLine("创建时间：" & Created)
            Sb.AppendLine("修改时间：" & Modified)
            If GameVersion <> "" Then Sb.AppendLine("游戏版本：" & GameVersion & If(DataVersion = "", "", "（数据版本 " & DataVersion & "）"))
            Sb.AppendLine()
            Sb.AppendLine(SaveDir)
            Dim Text As String = Sb.ToString
            RunInUi(Sub() MyMsgBox(Text, "存档信息", "知道了"))
        End Sub)
    End Sub

    ''' <summary>右键菜单，用 PCL 自己的 MyMenuItem。</summary>
    Private Sub ShowSaveMenu(Target As FrameworkElement, SaveDir As String)
        Dim Menu As New ContextMenu With {.FontSize = 12}
        Dim ItemInfo As New MyMenuItem With {.Header = "存档信息"}
        AddHandler ItemInfo.Click, Sub() ShowSaveInfo(SaveDir)
        Menu.Items.Add(ItemInfo)

        Dim ItemDetail As New MyMenuItem With {.Header = "详细管理"}
        AddHandler ItemDetail.Click, Sub() OpenDetail(SaveDir)
        Menu.Items.Add(ItemDetail)

        Dim ItemOpen As New MyMenuItem With {.Header = "打开文件夹"}
        AddHandler ItemOpen.Click, Sub() OpenExplorer(SaveDir)
        Menu.Items.Add(ItemOpen)

        Dim ItemCopy As New MyMenuItem With {.Header = "复制路径"}
        AddHandler ItemCopy.Click,
            Sub()
                Try
                    My.Computer.Clipboard.SetText(SaveDir)
                    Hint("路径已复制", HintType.Green, False)
                Catch ex As Exception
                    Logger.Warn(ex, "复制路径失败")
                End Try
            End Sub
        Menu.Items.Add(ItemCopy)

        Dim ItemDelete As New MyMenuItem With {.Header = "删除存档"}
        AddHandler ItemDelete.Click,
            Sub()
                If MyMsgBox("确定要删除存档「" & PathUtils.GetLastPart(SaveDir) & "」吗？" & vbCrLf &
                            "此操作不可撤销。", "删除存档", "删除", "取消", IsWarn:=True) <> 1 Then Return
                Try
                    DirectoryUtils.Delete(SaveDir, True)
                    Hint("存档已删除", HintType.Green, False)
                    RefreshSaves()
                Catch ex As Exception
                    Logger.Error(ex, "删除存档失败", LogBehavior.Alert)
                End Try
            End Sub
        Menu.Items.Add(ItemDelete)

        Menu.PlacementTarget = Target
        Menu.IsOpen = True
    End Sub

    Private Sub BtnOpenSaves_Click(sender As Object, e As EventArgs) Handles BtnOpenSaves.Click
        Dim Root As String = SaveRoot
        If Root = "" Then Return
        DirectoryUtils.Create(Root)
        OpenExplorer(Root)
    End Sub

    Private Sub BtnRefreshSaves_Click(sender As Object, e As EventArgs) Handles BtnRefreshSaves.Click
        RefreshSaves()
    End Sub

    ''' <summary>看一眼当前版本的隔离策略，避免用户把存档放错地方。</summary>
    Private Sub BtnIndieHint_Click(sender As Object, e As EventArgs) Handles BtnIndieHint.Click
        Dim Inst As McInstance = CurInstance
        If Inst Is Nothing Then Return
        Dim Indie As Boolean = Inst.PathIndie <> McFolderSelected
        MyMsgBox("版本：" & Inst.Name & vbCrLf & vbCrLf &
                 "隔离状态：" & If(Indie, "已开启（存档在版本文件夹内）", "未开启（存档在全局 .minecraft 内）") & vbCrLf & vbCrLf &
                 "存档目录：" & vbCrLf & SaveRoot, "版本隔离", "知道了")
    End Sub

End Class
