''' <summary>
''' 收藏夹安装的确认流程：后台分析计划 → 弹窗展示清单 → 用户确认后交给 <see cref="ModInstallRunner"/>。
''' </summary>
Public Module ModInstallDialog

    ''' <summary>
    ''' 分析并弹出安装确认。分析在工作线程进行，不阻塞界面。
    ''' </summary>
    Public Sub Show(References As List(Of ModFavoriteShare.ShareItem), Instance As McInstance)
        If References Is Nothing OrElse Not References.Any() OrElse Instance Is Nothing Then Return
        Hint("正在分析版本与前置……", HintType.Blue, False)
        RunInThread(
        Sub()
            Dim Plan As ModInstallPlanner.Plan
            Try
                '分析分两段：先扫本地 jar 读 modid（最慢），再逐个联网查项目与版本列表
                Dim Total As Integer = References.Count
                Dim Done As Integer = 0
                ModProgressHint.Show("正在读取本地模组…")
                Plan = ModInstallPlanner.BuildPlan(References, Instance,
                    Sub(ProjectId)
                        '每处理完一项推进一格；总数会随前置被发现而增长，所以用「已处理 / 至少总数」估算
                        Done += 1
                        Dim ShownTotal As Integer = Math.Max(Total, Done)
                        ModProgressHint.Show($"正在分析前置：{Done} / {ShownTotal}", If(ShownTotal = 0, 0, Done / ShownTotal))
                    End Sub,
                    Sub(Index, Count, FileName)
                        If Count = 0 Then Return
                        ModProgressHint.Show($"正在读取本地模组：{Index} / {Count}", Index / Count)
                    End Sub)
            Catch ex As Exception
                Logger.Error(ex, "分析收藏夹安装计划失败")
                RunInUi(Sub() Hint("分析失败：" & ex.Message, HintType.Red))
                Return
            Finally
                ModProgressHint.Hide()
            End Try
            RunInUi(
            Sub()
                Try
                    Dim Text As String = ModInstallPlanner.Describe(Plan, Instance)
                    If Not Plan.Ready.Any() Then
                        MyMsgBox(Text, "没有可安装的内容", "好")
                        Return
                    End If
                    If MyMsgBox(Text, "确认安装", "开始安装", "取消") = 1 Then
                        ModInstallRunner.Run(Plan, Instance)
                    End If
                Catch ex As Exception
                    Logger.Error(ex, "显示安装确认失败")
                    Hint("显示安装确认失败：" & ex.Message, HintType.Red)
                End Try
            End Sub)
        End Sub)
    End Sub

End Module
