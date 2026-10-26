''' <summary>
''' 联机设置页：选择优先使用的联机节点。
''' <para>
''' 这是 <see cref="FormMain.PageType.LinkSettings"/> 副页面，从联机主页进入，
''' 标题栏的返回箭头由 FormMain 统一提供，页内不再自己画一份。
''' </para>
''' <para>
''' 延迟优先 / 优先直连的切换仍在「设置 → 联机」，这里只做跳转入口，
''' 免得同一设置项在两处各画一个控件、互相看不到对方改了什么。
''' </para>
''' </summary>
Public Class PageLinkSettings

    Private Sub PageLinkSettings_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        AniControlEnabled += 1
        Try
            TextCustomNode.Text = Settings.Get(Of String)("LinkCustomPeer")
        Finally
            AniControlEnabled -= 1
        End Try
        RefreshNodeList()
    End Sub

    ''' <summary>跳到「设置 → 联机」看完整选项。</summary>
    Private Sub BtnOpenLinkSetup_Click(sender As Object, e As EventArgs) Handles BtnOpenLinkSetup.Click
        FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupLink)
    End Sub

    ''' <summary>刷新按钮：强制重新拉云端清单并测速。</summary>
    Private Sub BtnNodeRefresh_Click(sender As Object, e As EventArgs) Handles BtnNodeRefresh.Click
        Hint("正在刷新节点列表……", HintType.Blue, False)
        RefreshNodeList(Force:=True)
    End Sub

    ''' <summary>拉取节点清单并测速，填进下拉框。</summary>
    Private Sub RefreshNodeList(Optional Force As Boolean = False)
        If CmbLinkNode Is Nothing Then Return
        RunInThread(
        Sub()
            Dim Nodes As List(Of ModLinkNodes.LinkNode)
            Try
                Nodes = ModLinkNodes.Fetch(Force)
            Catch ex As Exception
                Logger.Warn(ex, "获取联机节点清单失败")
                Return
            End Try
            If Nodes Is Nothing OrElse Nodes.Count = 0 Then Return
            ModLinkNodes.ProbeLatency(Nodes, 3000)
            Nodes = Nodes.OrderBy(Function(N) If(N.Latency < 0, Integer.MaxValue, N.Latency)).ToList()
            RunInUi(
            Sub()
                AniControlEnabled += 1
                Try
                    CmbLinkNode.Items.Clear()
                    CmbLinkNode.Items.Add(New MyComboBoxItem With {.Content = "自动（测速选最快）", .Tag = ""})
                    For Each Node In Nodes
                        Dim Text As String = Node.Address & If(Node.Latency >= 0, $" ({Node.Latency} ms)", "")
                        CmbLinkNode.Items.Add(New MyComboBoxItem With {.Content = Text, .Tag = Node.Address})
                    Next
                    Dim Saved As String = Settings.Get(Of String)("LinkPreferredPeer")
                    Dim Target As MyComboBoxItem = Nothing
                    If Saved <> "" Then
                        For Each One As MyComboBoxItem In CmbLinkNode.Items
                            If CStr(One.Tag) = Saved Then Target = One : Exit For
                        Next
                    End If
                    If Target Is Nothing Then Target = CType(CmbLinkNode.Items(0), MyComboBoxItem)
                    CmbLinkNode.SelectedItem = Target
                Finally
                    AniControlEnabled -= 1
                End Try
            End Sub)
        End Sub)
    End Sub

    Private Sub CmbLinkNode_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmbLinkNode.SelectionChanged
        If AniControlEnabled > 0 Then Return
        Dim Item = TryCast(CmbLinkNode.SelectedItem, MyComboBoxItem)
        If Item Is Nothing Then Return
        Settings.Set("LinkPreferredPeer", CStr(Item.Tag))
    End Sub

    ''' <summary>
    ''' 自定义节点。写入的是「设置 → 联机」里同一个设置项，两处共享一份值。
    ''' </summary>
    Private Sub TextCustomNode_TextChanged(sender As Object, e As EventArgs) Handles TextCustomNode.TextChanged
        If AniControlEnabled > 0 Then Return
        Settings.Set("LinkCustomPeer", TextCustomNode.Text.Trim())
    End Sub

End Class
