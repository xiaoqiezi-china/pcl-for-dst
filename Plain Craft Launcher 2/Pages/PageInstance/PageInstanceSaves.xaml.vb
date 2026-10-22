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
        RefreshSaves()
    End Sub

    ''' <summary>重新扫描存档目录。</summary>
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
                      Catch
                      End Try
                      Try
                          TB = DirectoryUtils.GetInfo(B).LastWriteTime
                      Catch
                      End Try
                      Return TB.CompareTo(TA)
                  End Function)
        AllSaves = Dirs
        ApplyFilter()
    End Sub

    '最近一次扫描到的存档列表，供搜索与筛选复用
    Private AllSaves As New List(Of String)
    Private CurrentRoot As String = ""

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

        If Shown.Count = 0 Then
            PanSaves.Children.Add(EmptyHint("没有符合条件的存档"))
            Return
        End If
        For Each SaveDir In Shown
            PanSaves.Children.Add(BuildSaveRow(SaveDir))
        Next
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
        Dim Row As New Border With {
            .Height = 56, .CornerRadius = New CornerRadius(6),
            .Margin = New Thickness(0, 0, 0, 6), .Padding = New Thickness(10, 0, 10, 0),
            .Background = New SolidColorBrush(BackColorSub), .Cursor = Cursors.Hand, .Tag = SaveDir,
            .ToolTip = SaveDir
        }
        Dim G As New Grid
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        '图标：有 icon.png 就用，没有给个默认方块
        Dim Icon As New Border With {
            .Width = 40, .Height = 40, .CornerRadius = New CornerRadius(4),
            .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center,
            .Background = New SolidColorBrush(BackColorMain)
        }
        Dim IconPath As String = SaveDir & "\icon.png"
        Dim Img As New MyImage With {.Stretch = Stretch.UniformToFill}
        If FileUtils.Exists(IconPath) Then
            Img.Source = IconPath
        Else
            Img.Source = PathImage & "Icons/NoIcon.png"
        End If
        Icon.Child = Img
        G.Children.Add(Icon)

        Dim Info As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Info.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 13, .FontWeight = FontWeights.Bold,
            .TextTrimming = TextTrimming.CharacterEllipsis
        })
        Dim Sub1 As New TextBlock With {
            .Text = "正在读取……", .FontSize = 11, .Opacity = 0.5, .Margin = New Thickness(0, 2, 0, 0)
        }
        Info.Children.Add(Sub1)
        Grid.SetColumn(Info, 1)
        G.Children.Add(Info)

        '右侧操作：跟 Mod 管理一样的图标按钮组
        Dim BtnBox As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center
        }
        Dim BtnDetail As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonInfo, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "存档信息"
        }
        Dim BtnOpen As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonOpen, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "打开文件夹"
        }
        Dim BtnDelete As New MyIconButton With {
            .Logo = PCL.Logo.IconButtonDelete, .Width = 26, .Height = 26, .LogoScale = 0.8, .ToolTip = "删除存档"
        }
        BtnBox.Children.Add(BtnDetail)
        BtnBox.Children.Add(BtnOpen)
        BtnBox.Children.Add(BtnDelete)
        Grid.SetColumn(BtnBox, 2)
        G.Children.Add(BtnBox)
        Row.Child = G

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
                    Catch
                    End Try
                Next
            Catch
            End Try
            Dim Text As String = $"{FormatSize(Size)} · {Files} 个文件"
            RunInUi(Sub() Sub1.Text = Text)
        End Sub)

        AddHandler BtnOpen.Click, Sub() OpenExplorer(SaveDir)
        AddHandler BtnDetail.Click, Sub() ShowSaveInfo(SaveDir)
        AddHandler BtnDelete.Click,
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
        AddHandler Row.MouseLeftButtonUp, Sub() OpenDetail(SaveDir)
        AddHandler Row.MouseRightButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                e.Handled = True
                ShowSaveMenu(Row, SaveDir)
            End Sub
        AddHandler Row.MouseEnter, Sub() Row.Background = New SolidColorBrush(BackColorMain)
        AddHandler Row.MouseLeave, Sub() Row.Background = New SolidColorBrush(BackColorSub)
        Return Row
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
                    Catch
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
            Catch
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
