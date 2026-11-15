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
    ''' CFPA 仓库的数据源：展开镜像、测速排序都走这一层，与本仓库其它副程序一致。
    ''' </summary>
    Private ReadOnly Feed As New ReleaseFeed(RepoOwner, RepoName)

    ''' <summary>
    ''' 下载源里「还没开始给数据」的等待上限。镜像挂起时不该让玩家干等十几秒。
    ''' </summary>
    Private Const StartTimeout As Integer = 5000

    ''' <summary>
    ''' 启动时自动准备内容的档位。
    ''' <para>
    ''' 数值是已经写进用户配置的历史值，**不能重排**：
    ''' 0/1/2 在旧版本里就是「关闭 / 仅汉化 / 汉化 + 拼音」，只是现在 2 的含义
    ''' 变成了「全部启用」。新增的「仅拼音」只能往后加到 3，否则老配置会被读错。
    ''' </para>
    ''' </summary>
    Public Enum HanhuaMode
        ''' <summary>什么都不装。</summary>
        None = 0
        ''' <summary>只装汉化资源包。</summary>
        HanhuaOnly = 1
        ''' <summary>汉化资源包 + 通用拼音搜索。</summary>
        All = 2
        ''' <summary>只装通用拼音搜索。</summary>
        PinyinOnly = 3
    End Enum

    ''' <summary>
    ''' 当前设置对应的档位。越界值一律按「无」处理。
    ''' <para>
    ''' 名字特意不叫 Mode：VB 的 Module 成员是全局可见的，而 PageInstanceMod 里
    ''' 有循环变量 ModE，与 Mode 只差大小写，会被后者抢走解析。
    ''' </para>
    ''' </summary>
    Public ReadOnly Property PrepareMode As HanhuaMode
        Get
            Dim Value As Integer = Settings.Get(Of Integer)("LaunchArgumentHanhua")
            If Value < HanhuaMode.None OrElse Value > HanhuaMode.PinyinOnly Then Return HanhuaMode.None
            Return CType(Value, HanhuaMode)
        End Get
    End Property
    ''' <summary>是否需要准备汉化资源包。</summary>
    Public ReadOnly Property NeedHanhua As Boolean
        Get
            Dim Current As HanhuaMode = PrepareMode
            Return Current = HanhuaMode.HanhuaOnly OrElse Current = HanhuaMode.All
        End Get
    End Property
    ''' <summary>是否需要准备通用拼音搜索模组。</summary>
    Public ReadOnly Property NeedPinyin As Boolean
        Get
            Dim Current As HanhuaMode = PrepareMode
            Return Current = HanhuaMode.PinyinOnly OrElse Current = HanhuaMode.All
        End Get
    End Property

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
    ''' 启动游戏前的自动准备：设置里开启「自动获取汉化」时才做。
    ''' <para>
    ''' 三种情况分开处理，目的都是**不阻塞启动**：
    ''' 已经装好并启用 → 什么都不做；
    ''' 文件已在但没启用 → 就地写 options.txt 启用（纯本地操作，很快），本次启动就生效；
    ''' 文件还没有 → 后台下载，下完自动启用，下次启动生效。
    ''' </para>
    ''' </summary>
    ''' <param name="Instance">即将启动的版本。</param>
    ''' <param name="WaitForExit">True 时阻塞到下载完成；False 时挂到任务栏（启动流程用 False）。</param>
    Public Function AutoFetch(Instance As McInstance, Optional WaitForExit As Boolean = False) As Boolean
        If Instance Is Nothing Then Return False
        '档位判断收口在 Mode / NeedHanhua 里，别在这里再写一遍整数比较
        If Not NeedHanhua Then Return False
        Dim PackVersion As String = GetPackVersion(Instance)
        If PackVersion = "" Then Return False
        Dim OutputPath As String = ResourcePackPath(Instance)
        '已经装好并启用了就不再折腾
        If FileUtils.Exists(OutputPath) AndAlso
           ModPackState.IsPackEnabled(Instance.PathIndie & "options.txt", PathUtils.GetLastPart(OutputPath)) Then
            Logger.Info($"汉化资源包已存在且已启用，跳过自动获取（{PackVersion}）")
            Return False
        End If
        '文件已经在本地，只差启用这一步：直接本地改 options.txt，不下载、不等待
        If FileUtils.Exists(OutputPath) Then
            '但要先确认它真的能用：以前镜像挂掉时会在目标路径留下半个文件，
            '那种文件启用之后只会让玩家看到一堆方块问号
            If IsValidPack(OutputPath) Then
                Logger.Info($"汉化资源包已存在但未启用，就地启用（{PackVersion}）")
                Return EnablePack(Instance)
            End If
            Logger.Warn($"本地汉化资源包已损坏，删除后重新下载：{OutputPath}")
            Try
                FileUtils.Delete(OutputPath)
            Catch ex As Exception
                Logger.Warn(ex, "删除损坏的汉化资源包失败")
                Return False
            End Try
        End If
        Logger.Info($"自动获取汉化资源包（{PackVersion}）")
        Return Start(Instance, WaitForExit)
    End Function

    ''' <summary>汉化资源包在当前版本里的目标路径，没有对应版本时返回空串。</summary>
    Public Function ResourcePackPath(Instance As McInstance) As String
        If Instance Is Nothing Then Return ""
        Dim PackVersion As String = GetPackVersion(Instance)
        If PackVersion = "" Then Return ""
        Return Instance.PathIndie & "resourcepacks\Minecraft-Mod-Language-Modpack-" & PackVersion & ".zip"
    End Function

    ''' <summary>
    ''' 把汉化资源包写进 options.txt 的启用列表（纯本地操作，不联网）。
    ''' 已经启用、或文件不在时返回 False。
    ''' </summary>
    Public Function EnablePack(Instance As McInstance) As Boolean
        If Instance Is Nothing Then Return False
        Dim OutputPath As String = ResourcePackPath(Instance)
        If OutputPath = "" OrElse Not FileUtils.Exists(OutputPath) Then Return False
        Try
            Dim OptionsFile As String = Instance.PathIndie & "options.txt"
            Dim Entry As String = "file/" & PathUtils.GetLastPart(OutputPath)
            '排在原版之后、其它资源包之前：汉化要盖住原版，但不该盖住玩家自己的材质包
            Dim Enabled As List(Of String) = ModPackState.GetEnabledPacks(OptionsFile)
            If Enabled.Any(Function(E) String.Equals(E, Entry, StringComparison.OrdinalIgnoreCase)) Then Return False
            If Enabled.Count = 0 Then
                ModPackState.EnablePack(OptionsFile, Entry)
            Else
                '插到 vanilla 后面
                Dim Index As Integer = Enabled.FindIndex(Function(E) String.Equals(E, "vanilla", StringComparison.OrdinalIgnoreCase))
                Enabled.Insert(If(Index < 0, 0, Index + 1), Entry)
                ModPackState.SetEnabledPacks(OptionsFile, Enabled)
            End If
            Logger.Info($"已在 options.txt 中启用汉化资源包：{Entry}")
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "启用汉化资源包失败")
            Return False
        End Try
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
        '目标路径与 AutoFetch 用的是同一份拼法，避免两处拼出不同文件名
        Dim OutputPath As String = ResourcePackPath(Instance)
        If OutputPath = "" Then Return False
        '测速要发网络请求，必须放在加载器的工作线程里做：
        'Start() 可能从 UI 线程（资源包页的「获取汉化」按钮）调用，
        '在这里直接测速会把界面卡住好几秒。
        '
        '用闭包而不是模块级字段来传这两个值：两个实例同时下载时，
        '模块级字段会被后一个覆盖，前一个就会把包写进别人的版本目录。
        Dim Url As String = GetAssetUrl(PackVersion)
        Dim Resolve As New LoaderTask(Of Integer, List(Of NetFile))("解析汉化资源包地址",
            Sub(Task)
                '展开镜像并测速：只给官方地址时，国内网络里 github 一个源挂了就彻底失败
                Dim Sources As String() = Feed.RankSourcesBySpeed(Feed.BuildSources(Url))
                Logger.Info($"获取汉化资源包，候选源 {Sources.Length} 个：{String.Join(" | ", Sources)}")
                '必须显式写进 Output：LoaderTask 的委托没有返回值，靠它把列表交给下载器
                '
                '校验只要「是个几十 KB 以上的文件」：各版本的包大小差了两个数量级
                '（26.1 的包只有 27 KB，1.16 的包 4.4 MB），写死 MinSize 必然误杀小包。
                '真正的完整性由下载完的结构校验兜住。
                Task.Output = New List(Of NetFile) From {
                    New NetFile(Sources, OutputPath, New FileChecker With {.MinSize = 8 * 1024}) With {.StartTimeout = StartTimeout}}
            End Sub) With {.ProgressWeight = 0.3}
        Dim Download As New LoaderDownload("下载汉化资源包", New List(Of NetFile)) With {.ProgressWeight = 1}
        '下载完先确认拿到的是个能读的资源包，再写进 options.txt
        Dim Activate As New LoaderTask(Of Integer, Integer)("启用汉化资源包",
            Sub()
                If Not IsValidPack(OutputPath) Then
                    '坏文件留着会让下次启动误以为「已装好」，必须删掉
                    Try
                        FileUtils.Delete(OutputPath)
                    Catch ex As Exception
                        Logger.Warn(ex, "删除无效的汉化资源包失败")
                    End Try
                    Throw New Exception("下载到的汉化资源包已损坏（可能被镜像替换），请稍后重试")
                End If
                '启用逻辑与 AutoFetch 共用一份实现，别再各写一遍
                EnablePack(Instance)
            End Sub) With {.ProgressWeight = 0.1}
        Dim Loaders As New List(Of LoaderBase) From {Resolve, Download, Activate}
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
            '只有调用方明确要求等待时才阻塞（例如玩家在设置里手动点「获取汉化」）
            Do While Combo.State = LoadState.Loading
                Thread.Sleep(100)
            Loop
            If Combo.State <> LoadState.Finished Then
                Throw New Exception("获取汉化资源包失败：" & If(Combo.Error Is Nothing, "已取消", Combo.Error.Message), Combo.Error)
            End If
        End If
        Return True
    End Function

    ''' <summary>
    ''' 确认下到的确实是个资源包：能当 zip 打开，且根目录有 pack.mcmeta。
    ''' <para>
    ''' 大小校验挡不住错误页与截断包——它们可能比阈值大，解压时才炸。
    ''' 在启用前验一次，比让玩家进游戏后看到一堆问号好得多。
    ''' </para>
    ''' </summary>
    Public Function IsValidPack(FilePath As String) As Boolean
        If Not FileUtils.Exists(FilePath) Then Return False
        Try
            Using Zip As IO.Compression.ZipArchive = IO.Compression.ZipFile.OpenRead(FilePath)
                '必须按名字精确找：FindRootEntry 在找不到时会退回「根目录下的任意 png」，
                '那会让只有图标、没有 pack.mcmeta 的包也通过校验
                Return ModPackInfo.FindEntry(Zip, ModPackInfo.PackMetaName) IsNot Nothing
            End Using
        Catch ex As IO.InvalidDataException
            Logger.Warn($"汉化资源包不是有效的压缩包：{FilePath}")
            Return False
        Catch ex As Exception
            Logger.Warn(ex, $"校验汉化资源包失败：{FilePath}")
            Return False
        End Try
    End Function

End Module
