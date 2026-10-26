Imports System.Reflection
Imports System.Windows.Threading

Public Class Application

    ''' <summary>
    ''' 把设置从旧注册表位置搬到 Software\PCL，仅当目标键里还没有数据时整块搬一次；
    ''' 必须连 Identify 一起搬：加密项的解密密钥是 "PCL" &amp; Identify，只搬密文会解不开。
    ''' </summary>
    Private Shared Sub MigrateLegacyRegistry()
        Try
            Using OldKey As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\DSTLauncher")
                If OldKey Is Nothing Then Return
                Dim Names As String() = OldKey.GetValueNames()
                If Names.Length = 0 Then Return
                Using NewKey As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\PCL")
                    If NewKey Is Nothing Then Return
                    Dim Count As Integer = 0
                    If NewKey.GetValue("SystemCount") Is Nothing Then
                        '目标键是空的：整块搬过去，连 Identify 一起（加密项的解密密钥要用到它）
                        For Each Name As String In Names
                            Try
                                Dim Value As Object = OldKey.GetValue(Name)
                                If Value Is Nothing Then Continue For
                                NewKey.SetValue(Name, Value, OldKey.GetValueKind(Name))
                                Count += 1
                            Catch ex As Exception
                                Logger.Warn(ex, $"迁移设置项失败：{Name}")
                            End Try
                        Next
                    Else
                        '目标键里已经有数据：只补搬 Dst* 设置，
                        '而且只补目标里没有的，不覆盖既有配置
                        For Each Name As String In Names
                            If Not Name.StartsWithF("Dst", True) Then Continue For
                            Try
                                If NewKey.GetValue(Name) IsNot Nothing Then Continue For
                                Dim Value As Object = OldKey.GetValue(Name)
                                If Value Is Nothing Then Continue For
                                NewKey.SetValue(Name, Value, OldKey.GetValueKind(Name))
                                Count += 1
                            Catch ex As Exception
                                Logger.Warn(ex, $"补搬 Dst 设置项失败：{Name}")
                            End Try
                        Next
                    End If
                    If Count > 0 Then Logger.Info($"已从 Software\DSTLauncher 迁移 {Count} 项设置到 Software\PCL")
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "迁移旧版注册表设置失败")
        End Try
    End Sub

    ''' <summary>
    ''' 把账号凭据从 Software\PCL 搬到 Software\PCL\DST（只搬一次）。
    ''' <para>
    ''' 判断依据不是「新键是否为空」，而是「旧键里那份密文我们解不解得开」：
    ''' 加密密钥是 "PCL" &amp; Identify，我们自己写的一直是 0000-0000-0000-0000，
    ''' 所以能解开就说明是 DST 写的，放心搬；解不开说明是官方版写的，绝对不要动它。
    ''' </para>
    ''' <para>
    ''' 这条判断很关键：之前两版共用同一个键，我们读不懂官方版的密文时会把值重置成默认值，
    ''' 等于把对方的登录状态抹掉。分开存之后就不会再发生。
    ''' </para>
    ''' </summary>
    Private Shared Sub MigrateAccountRegistry()
        Try
            Const OldPath As String = "Software\PCL"
            Const NewPath As String = "Software\PCL\DST"
            Using NewKey As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(NewPath)
                '新键已经有凭据了，说明搬过了，不再重复处理
                If NewKey IsNot Nothing AndAlso NewKey.GetValue("CacheMsV2OAuthRefresh") IsNot Nothing Then Return
            End Using
            Using OldKey As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(OldPath)
                If OldKey Is Nothing Then Return
                '旧键里没有正版凭据，无事可做
                Dim Raw As Object = OldKey.GetValue("CacheMsV2OAuthRefresh")
                If Raw Is Nothing OrElse CStr(Raw) = "" Then Return
                '密钥是 "PCL" & Identify，所以旧键里的 Identify 就是判断依据：
                '等于本地这个值，说明凭据是我们写的；不等就说明是官方版写的，绝不能动
                Dim OldIdentify As String = ""
                Try
                    Dim RawIdentify As Object = OldKey.GetValue("Identify")
                    If RawIdentify IsNot Nothing Then OldIdentify = CStr(RawIdentify).Trim()
                Catch
                End Try
                If OldIdentify <> "" AndAlso OldIdentify <> Identify Then
                    Logger.Info($"Software\PCL 的识别码是 {OldIdentify}，与本地不同，凭据来自其它 PCL 版本，保留原样不迁移")
                    Return
                End If

                '确实是我们的，搬到新键
                Using Target As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(NewPath)
                    If Target Is Nothing Then Return
                    Dim Count As Integer = 0
                    For Each Name As String In OldKey.GetValueNames()
                        Try
                            If Not IsAccountSetting(Name) Then Continue For
                            Dim Value As Object = OldKey.GetValue(Name)
                            If Value Is Nothing Then Continue For
                            Target.SetValue(Name, Value, OldKey.GetValueKind(Name))
                            Count += 1
                        Catch ex As Exception
                            Logger.Warn(ex, $"迁移账号凭据失败：{Name}")
                        End Try
                    Next
                    Logger.Info($"已把 {Count} 项账号凭据迁移到 Software\PCL\DST")
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "迁移账号凭据失败")
        End Try
    End Sub
    '开始
    Private Sub Application_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
        Try
            '核心初始化
            MeloongCore.Main.Init("PCL")
            MeloongCore.Wpf.Main.Init()
            If Not (e.Args.Contains("--update") OrElse e.Args.Contains("--gpu") OrElse e.Args.Contains("--memory")) Then '若是通过命令行调用，则不检查单例、不轮换日志（#9137）
                WaitingMutex(e)
                Logger.Instance = New PclLogger With {.logFolder = Paths.BaseThenName, .MinLevel = If(ModeDebug, LogLevel.Trace, LogLevel.Info)}
            End If
            '把旧版写在 Software\DSTLauncher 的设置搬到 Software\PCL（只搬一次）
            MigrateLegacyRegistry()
            '账号凭据单独存到 Software\PCL\DST，避免与官方版互相覆盖
            MigrateAccountRegistry()
            '释放本地配置：只在新装时写一份，之后更新启动器不会碰它
            ModConfigFile.EnsureExists()
            '先把上次成功的预设备份读回来，这样没网也有预设可选
            ModPresetSource.LoadFromBackup()
            '提升主线程优先级
            Thread.CurrentThread.Priority = ThreadPriority.Highest
            '执行开发版测试
            If BuildType = BuildTypes.Debug Then
                Try
                    ModDevelop.Start()
                Catch ex As Exception
                    Logger.Error(ex, "开发者模式测试出错", LogBehavior.Alert)
                End Try
            End If
            '检查 .NET Framework 版本
            Try
                If CInt(RegistryUtils.TryRead("HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 999999)) < 528040 Then OldEnvironmentAssert()
            Catch ex As Exception
                Logger.Warn(ex, "检查 .NET Framework 版本失败")
            End Try
            '修复 WPF 字体加载（#3467）
            Try
                Dim _unused As New FormattedText("", Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Fonts.SystemTypefaces.First, 96, New MyColor, DPI)
            Catch ex As UriFormatException
                Environment.SetEnvironmentVariable("windir", Environment.GetEnvironmentVariable("SystemRoot"), EnvironmentVariableTarget.User)
                Dim _unused As New FormattedText("", Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Fonts.SystemTypefaces.First, 96, New MyColor, DPI)
            End Try
            '检查参数调用
            If e.Args.Length > 0 Then
                If e.Args(0) = "--update" Then
                    '自动更新
                    UpdateReplace(e.Args(1), e.Args(2).Trim(""""), e.Args(3).Trim(""""), e.Args(4))
                    Environment.Exit(ProcessReturnValues.TaskDone)
                ElseIf e.Args(0) = "--gpu" Then
                    '调整显卡设置
                    Try
                        SetGPUPreference(e.Args(1).Trim(""""))
                        Environment.Exit(ProcessReturnValues.TaskDone)
                    Catch ex As Exception
                        Environment.Exit(ProcessReturnValues.Fail)
                    End Try
                ElseIf e.Args(0) = "--memory" Then
                    '内存优化
                    Dim Ram = My.Computer.Info.AvailablePhysicalMemory
                    Try
                        PageOtherTest.MemoryOptimizeInternal(False)
                    Catch ex As Exception
                        MsgBox(ex.Message, MsgBoxStyle.Critical, "内存优化失败")
                        Environment.Exit(-1)
                    End Try
                    If My.Computer.Info.AvailablePhysicalMemory < Ram Then '避免 ULong 相减出现负数
                        Environment.Exit(0)
                    Else
                        Environment.Exit((My.Computer.Info.AvailablePhysicalMemory - Ram) / 1024) '返回清理的内存量（K）
                    End If
                End If
            End If
            '初始化文件结构
            Try
                DirectoryUtils.Create(Paths.Base & "PCL\Pictures\")
                DirectoryUtils.Create(Paths.Base & "PCL\Musics\")
                CheckPermissionWithException(Paths.Base & "PCL\")
            Catch ex As Exception
                MsgBox($"PCL 没有对当前文件夹的权限（{Paths.Base}PCL\），请尝试：" & vbCrLf &
                  "1. 将 PCL 移动到其他文件夹" & If(PathUtils.GetDiskName(Paths.Base) = "C"c, "，例如 C 盘和桌面以外的其他位置。", "。") & vbCrLf &
                  "2. 删除当前目录中的 PCL 文件夹，然后再试。" & vbCrLf &
                  "3. 右键 PCL 选择属性，打开 兼容性 中的 以管理员身份运行此程序。",
                MsgBoxStyle.Critical, "运行环境错误")
                Environment.[Exit](ProcessReturnValues.Cancel)
            End Try
RetryCacheCheck:
            Try
                DirectoryUtils.Create(PathTemp)
                CheckPermissionWithException(PathTemp)
            Catch ex As Exception
                If PathUtils.IsSame(PathTemp, Path.GetTempPath() & "PCL\") Then
                    MyMsgBox("PCL 无法访问缓存文件夹，可能导致程序出错或无法正常使用！" & vbCrLf & vbCrLf & "错误原因：" & ex.GetDisplay(True), "缓存文件夹不可用")
                Else
                    MyMsgBox("手动设置的缓存文件夹不可用，PCL 将使用默认缓存文件夹。" & vbCrLf & vbCrLf & "错误原因：" & ex.GetDisplay(True), "缓存文件夹不可用")
                    Settings.Set("SystemSystemCache", "")
                    PathTemp = Path.GetTempPath() & "PCL\"
                    GoTo RetryCacheCheck
                End If
            End Try
            DirectoryUtils.Create(PathTemp & "Cache\")
            DirectoryUtils.Create(Paths.AppDataThenName)
            '设置 ToolTipService 默认值
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(GetType(DependencyObject), New FrameworkPropertyMetadata(100))
            '设置网络配置默认值
            ServicePointManager.Expect100Continue = False
            ServicePointManager.DefaultConnectionLimit = 10000
            ServicePointManager.UseNagleAlgorithm = False
            ServicePointManager.EnableDnsRoundRobin = True
            ServicePointManager.ReusePort = True
            If Environment.OSVersion.Version < New Version(6, 2) Then 'Windows 7 回退（#8519）
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 Or SecurityProtocolType.Tls Or SecurityProtocolType.Tls11 Or SecurityProtocolType.Tls12
            End If
            '等价于 exe.config 里的 useUnsafeHeaderParsing="true"
            '部分国内镜像站的响应头不合规，不放开会被判协议违规
            EnableUnsafeHeaderParsing()
            '设置初始窗口
            If Settings.Get(Of Boolean)("UiLauncherLogo") Then
                FrmStart = New SplashScreen("Images\icon.ico")
                FrmStart.Show(False, True)
            End If
            '基础信息
            Logger.Info($"程序版本：{VersionDisplay} ({VersionCode}{If(CommitHash = "", "", $"，#{CommitHash}")})")
            If BuildType = BuildTypes.Snapshot Then
                Logger.Info($"识别码：{Identify}{If(ThemeCheckOne(9), "，已解锁反馈主题", "，未解锁反馈主题")}")
            Else
                Logger.Info($"识别码：{Identify}")
            End If
            Logger.Info($"程序路径：{PathExe}")
            Logger.Info($"系统编码：{Encoding.Default.HeaderName} ({Encoding.Default.CodePage}, GBK={IsGBKEncoding})")
            Logger.Info($"管理员权限：{WindowsUtils.HasAdminRole()}")
            '检测异常环境
            If Paths.Base.Contains(Path.GetTempPath()) OrElse Paths.Base.Contains("AppData\Local\Temp\") Then
                MyMsgBox("请将 PCL 从压缩包中解压后再使用！" & vbCrLf & "如果不会解压，可以在网上寻找教程。", "需要解压！", "我知道了", IsWarn:=True, ForceWait:=True)
                FormMain.EndProgramForce(ProcessReturnValues.Cancel)
            End If
            If Not Environment.Is64BitOperatingSystem Then
                MyMsgBox("PCL 和新版 Minecraft 均不再支持 32 位系统，请重装为 64 位系统后再进行游戏！", "环境警告", "我知道了", IsWarn:=True, ForceWait:=True)
                FormMain.EndProgramForce(ProcessReturnValues.Cancel)
            End If
            '计时
            Logger.Info($"第一阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")
            ApplicationStartTick = GetTimeMs()
            AniControlEnabled += 1
        Catch ex As Exception
            Dim FilePath As String = Nothing
            Try
                FilePath = PathExe
            Catch
            End Try
            MsgBox(ex.GetDisplay(True) & vbCrLf & "PCL 所在路径：" & If(String.IsNullOrEmpty(FilePath), "获取失败", FilePath), MsgBoxStyle.Critical, "PCL 初始化错误")
            FormMain.EndProgramForce(ProcessReturnValues.Exception)
        End Try
    End Sub
    ''' <summary>
    ''' 要求程序以单例运行。
    ''' 如果已在运行，则将其窗口拖出来并播放提示音，然后结束当前进程。
    ''' </summary>
    Private Shared Sub WaitingMutex(e As StartupEventArgs)
        If BuildType = BuildTypes.Debug Then Return
        Try
            Dim MutexCreatedNew As Boolean
            PclMutex = New Mutex(True, "PCL_SingletonMutex", MutexCreatedNew)
            If MutexCreatedNew Then Return
            Logger.Warn("已有一个 PCL 实例正在运行")
            '等待已有的 PCL 退出
            If e.Args.Length > 0 AndAlso e.Args(0) = "--wait" Then
                Try
                    If PclMutex.WaitOne(10000) Then Return '等待至多 10 秒
                Catch Ignored As AbandonedMutexException
                    Return '已有实例异常退出，继续启动
                End Try
            End If
            '将已有的 PCL 窗口拖出来
            Dim WindowHwnd As IntPtr = FindWindow(Nothing, "Plain Craft Launcher　")
            If WindowHwnd = IntPtr.Zero Then WindowHwnd = FindWindow(Nothing, "Plain Craft Launcher 2　")
            If WindowHwnd <> IntPtr.Zero Then ShowWindowToTop(WindowHwnd)
            '播放提示音并退出
            Beep()
            Environment.[Exit](ProcessReturnValues.Cancel)
        Catch ex As Exception
            Logger.Warn(ex, "单例检查失败")
        End Try
    End Sub
    Private Shared PclMutex As Mutex

    '结束
    Private Sub Application_SessionEnding(sender As Object, e As SessionEndingCancelEventArgs) Handles Me.SessionEnding
        FrmMain?.EndProgram(False)
    End Sub

    '异常
    Private Sub Application_DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Handles Me.DispatcherUnhandledException
        On Error Resume Next
        '触发页面的 Dispatcher
        If FrmMain?.PageLeft IsNot Nothing AndAlso TypeOf FrmMain.PageLeft Is IDispatcherUnhandledException Then
            CType(FrmMain.PageLeft, IDispatcherUnhandledException).DispatcherUnhandledException(sender, e)
            If e.Handled Then Return
        End If
        If FrmMain?.PageRight IsNot Nothing AndAlso TypeOf FrmMain.PageRight Is IDispatcherUnhandledException Then
            CType(FrmMain.PageRight, IDispatcherUnhandledException).DispatcherUnhandledException(sender, e)
            If e.Handled Then Return
        End If
        '正常处理
        e.Handled = True
        If IsProgramEnding Then Return
        FeedbackInfo()
        Dim Detail As String = e.Exception.GetDisplay(True)
        If Detail.Contains("System.Windows.Threading.Dispatcher.Invoke") OrElse Detail.Contains("MS.Internal.AppModel.ITaskbarList.HrInit") OrElse Detail.Contains("未能加载文件或程序集") OrElse
           Detail.Contains(".NET Framework") Then ' “自动错误判断” 的结果分析
            OldEnvironmentAssert(e.Exception)
        Else
            Logger.Error(e.Exception, "程序出现未知错误", LogBehavior.AlertThenCrash)
        End If
    End Sub
    Private Shared Sub OldEnvironmentAssert(Optional ex As Exception = Nothing)
        OpenWebsite("https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/thank-you/net48-offline-installer")
        If ex Is Nothing Then
            Logger.Error($"你的 .NET Framework 版本过低或损坏，请下载并重新安装 .NET Framework 4.8！{vbCrLf}若安装失败，可以先卸载高版本的 .NET Framework 然后再试。", LogBehavior.AlertThenCrash)
        Else
            Logger.Error(ex, $"你的 .NET Framework 版本过低或损坏，请下载并重新安装 .NET Framework 4.8！{vbCrLf}若安装失败，可以先卸载高版本的 .NET Framework 然后再试。", LogBehavior.AlertThenCrash)
        End If
    End Sub

    '动态 DLL 加载
    ''' <summary>
    ''' 等价于 exe.config 里的 useUnsafeHeaderParsing="true"。
    ''' 部分国内镜像站返回的响应头不合规，不放开会被 HttpWebRequest 判为协议违规。
    ''' 用反射改内部设置，这样就不需要外置配置文件了。
    ''' </summary>
    Private Shared Sub EnableUnsafeHeaderParsing()
        Try
            Dim SettingsType As Type = Type.GetType("System.Net.Configuration.SettingsSection, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
            If SettingsType Is Nothing Then Return
            Dim SectionField As Reflection.FieldInfo = SettingsType.GetField("s_settingsSection", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Static)
            Dim Section = If(SectionField Is Nothing, Nothing, SectionField.GetValue(Nothing))
            If Section Is Nothing Then Return
            Dim UseUnsafe As Reflection.FieldInfo = Nothing
            '不同 .NET 版本字段名略有差异，两个都试
            Dim Cur As Type = Section.GetType()
            Do While Cur IsNot Nothing AndAlso UseUnsafe Is Nothing
                UseUnsafe = Cur.GetField("useUnsafeHeaderParsing", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
                Cur = Cur.BaseType
            Loop
            If UseUnsafe IsNot Nothing Then UseUnsafe.SetValue(Section, True)
        Catch ex As Exception
            Logger.Warn(ex, "开启宽松响应头解析失败")
        End Try
    End Sub

    Private Sub New() '这里必须尽早调用，且不能使用任何库，否则加载 MeloongCore 就会导致崩溃
        Static Prefixes As String() = {"NAudio", "Newtonsoft.Json", "Ookii.Dialogs.Wpf", "Imazen.WebP", "CacheCow.Common", "CacheCow.Client.FileStore", "CacheCow.Client", "ThrottleDebounce", "System.Net.Http.Formatting", "Microsoft.Win32.Registry", "PCLCS", "MeloongCore.Wpf", "MeloongCore"}
        Static LoadedAssemblies As New ConcurrentDictionary(Of String, Lazy(Of Assembly))(StringComparer.Ordinal) '缓存
        AddHandler AppDomain.CurrentDomain.AssemblyResolve,
        Function(sender As Object, Args As ResolveEventArgs) As Assembly
            Dim Prefix As String = Prefixes.FirstOrDefault(Function(p) Args.Name.StartsWith(p, StringComparison.Ordinal))
            If Prefix Is Nothing Then Return Nothing
            Dim LazyAssembly = LoadedAssemblies.GetOrAdd(Prefix, Function(p) New Lazy(Of Assembly)(
            Function()
                Dim LoadedAssembly = Assembly.Load(DirectCast(My.Resources.ResourceManager.GetObject(p), Byte()))
                If p = "Imazen.WebP" Then ExtractLibwebp() 'WebP 特判
                Return LoadedAssembly
            End Function, LazyThreadSafetyMode.ExecutionAndPublication))
            Return LazyAssembly.Value
        End Function
    End Sub
    Private Shared Sub ExtractLibwebp() '这个方法会调用 ModBase，进而调用 MeloongCore，所以不能放在 New 里
        SetDllDirectory(PathUtils.RemoveSlashSuffix(PathPure.Value))
        Try
            ExtractResources(PathPure.Value & "libwebp.dll", "libwebp64")
        Catch ex As Exception
            Logger.Warn(ex, "写入 libwebp.dll 失败") '防止同时加载多个图片时，同时写入文件导致文件占用，进而导致崩溃
        End Try
    End Sub
    Private Declare Function SetDllDirectory Lib "kernel32" Alias "SetDllDirectoryA" (lpPathName As String) As Boolean

    '控件模板事件
    Private Sub MyIconButton_Click(sender As Object, e As EventArgs)
        Select Case Settings.Get(Of McLoginType)("LoginType")
            Case McLoginType.Ms
                '微软
                Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
                MsJson.Remove(sender.Tag)
                Settings.Set("LoginMsJson", MsJson.ToString(Newtonsoft.Json.Formatting.None))
                If FrmLoginMs.ComboAccounts.SelectedItem Is sender.Parent Then FrmLoginMs.ComboAccounts.SelectedIndex = 0
                FrmLoginMs.ComboAccounts.Items.Remove(sender.Parent)
            Case McLoginType.Legacy
                '离线
                Dim Names As New List(Of String)
                Names.AddRange(Settings.Get(Of String)("LoginLegacyName").ToString.Split("¨"))
                Names.Remove(sender.Tag)
                Settings.Set("LoginLegacyName", Names.Join("¨"c))
                FrmLoginLegacy.ComboName.ItemsSource = Names
                FrmLoginLegacy.ComboName.Text = If(Names.Any, Names(0), "")
            Case Else
                '第三方
                Dim Token As String = Settings.Get(Of McLoginType)("LoginType").ToString()
                Dim Dict As New Dictionary(Of String, String)
                Dim Names As New List(Of String)
                Dim Passs As New List(Of String)
                If Not Settings.Get(Of String)("Login" & Token & "Email") = "" Then Names.AddRange(Settings.Get(Of String)("Login" & Token & "Email").Split("¨"))
                If Not Settings.Get(Of String)("Login" & Token & "Pass") = "" Then Passs.AddRange(Settings.Get(Of String)("Login" & Token & "Pass").Split("¨"))
                For i = 0 To Names.Count - 1
                    Dict.Add(Names(i), Passs(i))
                Next
                Dict.Remove(sender.Tag)
                Settings.Set("Login" & Token & "Email", Dict.Keys.Join("¨"c))
                Settings.Set("Login" & Token & "Pass", Dict.Values.Join("¨"c))
                Select Case Token
                    Case "Nide"
                        FrmLoginNide.ComboName.ItemsSource = Dict.Keys
                        FrmLoginNide.ComboName.Text = If(Dict.Keys.Any, Dict.Keys(0), "")
                        FrmLoginNide.TextPass.Password = If(Dict.Values.Any, Dict.Values(0), "")
                    Case "Auth"
                        FrmLoginAuth.ComboName.ItemsSource = Dict.Keys
                        FrmLoginAuth.ComboName.Text = If(Dict.Keys.Any, Dict.Keys(0), "")
                        FrmLoginAuth.TextPass.Password = If(Dict.Values.Any, Dict.Values(0), "")
                End Select
        End Select
    End Sub

End Class
