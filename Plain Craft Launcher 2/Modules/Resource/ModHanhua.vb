Imports System.IO

''' <summary>
''' 「获取汉化」：下载 CFPA 的自动构建汉化资源包，装进当前版本的 resourcepacks。
'''
''' 直接取 GitHub Release 的 autobuild 滚动 tag。那个 tag 下每个版本各有一个资源包，
''' 仓库只在某个版本的翻译有改动时才重建它，所以各包的时间戳不一致是正常的，
''' 每个包本身都是最新的。
'''
''' 下载走 PCL 自己的 LoaderDownload（断点续传、多源、磁盘空间检查都是现成的），
''' 任务卡片走 LoaderCombo + LoaderTaskbarAdd，与安装整合包完全一致。
''' </summary>
Public Module ModHanhua

    Public Const RepoOwner As String = "CFPAOrg"
    Public Const RepoName As String = "Minecraft-Mod-Language-Package"
    ''' <summary>滚动发布 tag，里面的资源包随仓库更新。</summary>
    Private Const ReleaseTag As String = "autobuild"

    ''' <summary>
    ''' 把实例的 MC 版本与加载器映射到 CFPA 资源包对应的版本键。
    ''' 返回空字符串表示该版本没有汉化包。
    ''' </summary>
    Public Function GetPackVersion(Instance As McInstance) As String
        If Instance Is Nothing Then Return ""
        Dim Vanilla As String = ""
        Dim IsFabric As Boolean = False
        Try
            Vanilla = Instance.Version.VanillaName
            IsFabric = Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric)
        Catch ex As Exception
            Logger.Warn(ex, "识别实例版本失败，无法获取汉化")
            Return ""
        End Try
        If String.IsNullOrEmpty(Vanilla) OrElse Vanilla = "Unknown" Then Return ""
        '只取主次版本号，快照与预发布一律按大版本处理
        Dim Match = System.Text.RegularExpressions.Regex.Match(Vanilla, "^(\d+)\.(\d+)")
        If Not Match.Success Then Return ""
        Dim Major As Integer = CInt(Match.Groups(1).Value)
        Dim Minor As Integer = CInt(Match.Groups(2).Value)
        Dim Base As String
        Select Case Major
            Case 1
                Select Case Minor
                    Case 6 : Base = "1.6"
                    Case 10 : Base = "1.10.2"
                    Case 12 : Base = "1.12.2"
                    Case 16 : Base = "1.16"
                    Case 18 : Base = "1.18"
                    Case 19 : Base = "1.19"
                    Case 20 : Base = "1.20"
                    Case 21 : Base = "1.21"
                    Case Else : Return "" '1.7~1.9、1.11、1.13~1.15、1.17 没有对应资源包
                End Select
            Case 26 : Base = "26.1"
            Case Else : Return ""
        End Select
        'Fabric 有独立的包（模组版本不同），只有部分版本提供
        If IsFabric Then
            Select Case Base
                Case "1.16", "1.18", "1.20", "1.21", "26.1"
                    Return Base & "-fabric"
            End Select
        End If
        Return Base
    End Function

    ''' <summary>资源包在 Release 里的文件名。</summary>
    Private Function GetAssetName(PackVersion As String) As String
        '1.20-fabric → Minecraft-Mod-Language-Modpack-1-20-Fabric.zip
        Dim Parts = PackVersion.Split("-"c)
        Dim Name = Parts(0).Replace("."c, "-"c)
        If Parts.Length > 1 Then Name &= "-" & Parts(1)
        Return "Minecraft-Mod-Language-Modpack-" & Name & ".zip"
    End Function

    ''' <summary>资源包在 Release 里的下载地址。</summary>
    Private Function GetAssetUrl(PackVersion As String) As String
        Return $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{ReleaseTag}/{GetAssetName(PackVersion)}"
    End Function

    ''' <summary>
    ''' 启动游戏前的自动获取：设置里开启「自动获取汉化」时才做。
    ''' 已经在 options.txt 里启用、且文件还在，就直接跳过，不每次启动都下一遍。
    ''' </summary>
    ''' <param name="Instance">即将启动的版本。</param>
    ''' <param name="WaitForExit">True 时阻塞到下载完成（启动流程里用），False 时挂到任务栏。</param>
    Public Function AutoFetch(Instance As McInstance, Optional WaitForExit As Boolean = True) As Boolean
        If Instance Is Nothing Then Return False
        If Settings.Get(Of Integer)("LaunchArgumentHanhua") <> 1 Then Return False
        Dim PackVersion As String = GetPackVersion(Instance)
        If PackVersion = "" Then Return False
        Dim OutputPath As String = Instance.PathIndie & "resourcepacksMinecraft-Mod-Language-Modpack-" & PackVersion & ".zip"
        '已经装好并启用了就不再折腾
        If FileUtils.Exists(OutputPath) AndAlso
           ModPackState.IsPackEnabled(Instance.PathIndie & "options.txt", PathUtils.GetLastPart(OutputPath)) Then
            Logger.Info($"汉化资源包已存在且已启用，跳过自动获取（{PackVersion}）")
            Return False
        End If
        Logger.Info($"自动获取汉化资源包（{PackVersion}）")
        Return Start(Instance, WaitForExit)
    End Function

    ''' <summary>
    ''' 该版本是否有对应的汉化资源包。用于在设置页给出提示，不产生任何副作用。
    ''' </summary>
    Public Function HasPack(Instance As McInstance) As Boolean
        Return GetPackVersion(Instance) <> ""
    End Function

    ''' <summary>
    ''' 为指定实例下载并安装汉化资源包。
    ''' 默认立刻返回，实际下载在右下角的任务卡片里进行。
    ''' </summary>
    ''' <param name="WaitForExit">True 时阻塞到下载结束，用于启动流程里必须先装好再启动的场景。</param>
    Public Function Start(Instance As McInstance, Optional WaitForExit As Boolean = False) As Boolean
        If Instance Is Nothing Then Return False
        Dim PackVersion As String = GetPackVersion(Instance)
        If PackVersion = "" Then
            Hint("这个版本还没有对应的汉化资源包", HintType.Red)
            Return False
        End If
        Try
            If Not Instance.IsLoaded Then Instance.Load()
        Catch ex As Exception
            Logger.Warn(ex, "加载版本信息失败，无法获取汉化")
        End Try
        Dim PackFolder As String = Instance.PathIndie & "resourcepacks\"
        '覆盖同名资源包：反复获取时资源包列表里不会堆出一堆同名文件
        Dim OutputPath As String = PackFolder & "Minecraft-Mod-Language-Modpack-" & PackVersion & ".zip"
        Dim Url As String = GetAssetUrl(PackVersion)
        Logger.Info($"开始获取汉化（{PackVersion}）：{Url}")

        '与安装整合包一样，用 LoaderCombo 挂到任务管理器上
        Dim Download As New LoaderDownload("下载汉化资源包",
            New List(Of NetFile) From {
                New NetFile({Url}, OutputPath, New FileChecker With {.MinSize = 1024 * 100})}) With {.ProgressWeight = 1}
        '下载完顺手在 options.txt 里启用它，否则玩家还得自己进游戏勾一次
        Dim Activate As New LoaderTask(Of Integer, Integer)("启用汉化资源包",
            Sub()
                Dim OptionsFile As String = Instance.PathIndie & "options.txt"
                Dim Entry As String = "file/" & PathUtils.GetLastPart(OutputPath)
                '排在原版之后、其它资源包之前：汉化要盖住原版，但不该盖住玩家自己的材质包
                Dim Enabled As List(Of String) = ModPackState.GetEnabledPacks(OptionsFile)
                If Enabled.Any(Function(E) String.Equals(E, Entry, StringComparison.OrdinalIgnoreCase)) Then Return
                If Enabled.Count = 0 Then
                    ModPackState.EnablePack(OptionsFile, Entry)
                Else
                    '插到 vanilla 后面
                    Dim Index As Integer = Enabled.FindIndex(Function(E) String.Equals(E, "vanilla", StringComparison.OrdinalIgnoreCase))
                    Enabled.Insert(If(Index < 0, 0, Index + 1), Entry)
                    ModPackState.SetEnabledPacks(OptionsFile, Enabled)
                End If
                Logger.Info($"已在 options.txt 中启用汉化资源包：{Entry}")
            End Sub) With {.ProgressWeight = 0.1}
        Dim Loaders As New List(Of LoaderBase) From {Download, Activate}
        Dim Combo As New LoaderCombo(Of Integer)("获取汉化资源包：" & Instance.Name & " ", Loaders)
        Combo.OnStateChanged =
        Sub()
            Select Case Combo.State
                Case LoadState.Finished
                    Hint($"汉化资源包已就绪：{PathUtils.GetLastPart(OutputPath)}", HintType.Green)
                    '资源包列表里立刻能看到它
                    RunInUi(Sub()
                                If FrmInstanceResourcePack IsNot Nothing AndAlso
                                   FrmMain.PageCurrent = FormMain.PageType.InstanceSetup AndAlso
                                   FrmMain.PageCurrentSub = FormMain.PageSubType.InstanceResourcePack Then
                                    FrmInstanceResourcePack.Refresh()
                                End If
                            End Sub)
                Case LoadState.Failed
                    Hint("获取汉化失败：" & Combo.Error.GetDisplay(False), HintType.Red)
                Case LoadState.Canceled
                    Hint("已取消获取汉化", HintType.Blue)
                Case Else
                    Return
            End Select
        End Sub
        Combo.Start()
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        If WaitForExit Then
            '启动流程里必须先装好再往下走，否则游戏启动时读到的还是旧的 options.txt
            Do While Combo.State = LoadState.Loading
                Thread.Sleep(100)
            Loop
            If Combo.State <> LoadState.Finished Then
                Throw New Exception("获取汉化资源包失败：" & If(Combo.Error Is Nothing, "已取消", Combo.Error.Message), Combo.Error)
            End If
        End If
        Return True
    End Function

End Module
