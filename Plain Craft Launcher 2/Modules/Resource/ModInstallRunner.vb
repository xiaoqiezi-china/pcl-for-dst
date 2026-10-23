''' <summary>
''' 执行 <see cref="ModInstallPlanner"/> 生成的安装计划。
''' 只负责把计划翻译成 PCL 现成的 <see cref="LoaderDownload"/> 与任务队列，不自己实现下载。
''' </summary>
Public Module ModInstallRunner

    ''' <summary>
    ''' 执行安装计划。返回是否启动了任务。
    ''' </summary>
    ''' <param name="Plan">要执行的计划。</param>
    ''' <param name="Instance">目标版本。</param>
    Public Function Run(Plan As ModInstallPlanner.Plan, Instance As McInstance) As Boolean
        If Plan Is Nothing OrElse Instance Is Nothing Then Return False
        Dim Items = Plan.Ready
        If Not Items.Any() Then Return False
        Try
            If Not Instance.IsLoaded Then Instance.Load()
            Dim ModFolder As String = Instance.PathIndie & "mods\"
            DirectoryUtils.Create(ModFolder)

            '构造下载信息。目标就是 mods 文件夹，与「从文件安装」走同一个落点，
            '这样 PCL 的重复文件检查、断点续传、镜像切换全部自动生效
            Dim FileList As New List(Of NetFile)
            Dim SkippedFiles As New List(Of String)
            For Each Item In Items
                Dim File As ResourceVersion = Item.Version
                If File Is Nothing OrElse Not File.DownloadAvailable Then
                    SkippedFiles.Add(Item.Display)
                    Continue For
                End If
                Dim FileName As String = File.FileName
                '~ 会导致 Mixin 加载失败，和「另存为」的处理保持一致
                FileName = FileName.Replace("~", "-")
                Dim Target As String = ModFolder & FileName
                '同名文件已存在时换个名字，避免覆盖掉用户自己的文件
                If FileUtils.Exists(Target) Then
                    Target = ModFolder & PathUtils.GetFileNameWithoutExtension(FileName) & "-" &
                             Date.Now.ToString("HHmmss") & "." & PathUtils.GetExtension(FileName)
                End If
                FileList.Add(File.ToNetFile(Target, ResourceVersion.DownloadReason.Dependency,
                                            Instance.Version.VanillaName, Instance.Version.ModLoaders))
            Next

            If Not FileList.Any() Then
                Hint("没有可以下载的文件", HintType.Red)
                Return False
            End If

            '构造加载器：下载 → 刷新列表
            Dim Loaders As New List(Of LoaderBase)
            Loaders.Add(New LoaderDownload("下载 Mod", FileList) With {.ProgressWeight = FileList.Count * 1.5})
            Loaders.Add(New LoaderTask(Of Integer, Integer)("刷新 Mod 列表",
                Sub()
                    RunInUi(Sub()
                                If FrmInstanceMod IsNot Nothing Then
                                    LoaderFolderRun(LocalResourceLoader, ModFolder, LoaderFolderRunType.ForceRun)
                                End If
                            End Sub)
                End Sub) With {.ProgressWeight = 0.1})

            Dim Loader As New LoaderCombo(Of Integer)("收藏夹安装：" & Instance.Name & " ", Loaders) With {
                .OnStateChanged =
                Sub(MyLoader)
                    Select Case MyLoader.State
                        Case LoadState.Finished
                            Dim Count As Integer = FileList.Count
                            Hint($"已安装 {Count} 个 Mod 到 {Instance.Name}！", HintType.Green)
                        Case LoadState.Failed
                            Hint("安装失败：" & MyLoader.Error.GetDisplay(False), HintType.Red)
                        Case LoadState.Canceled
                            Hint("安装已取消", HintType.Blue)
                        Case Else
                            Return
                    End Select
                End Sub}
            Loader.Start(1)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
            If SkippedFiles.Any() Then
                Logger.Warn($"以下 Mod 缺少可用下载文件，已跳过：{SkippedFiles.Join("，")}")
            End If
            Return True
        Catch ex As Exception
            Logger.Error(ex, "启动收藏夹安装失败", LogBehavior.Alert)
            Return False
        End Try
    End Function

End Module
