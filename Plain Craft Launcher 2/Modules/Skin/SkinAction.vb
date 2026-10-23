Imports System.Windows

''' <summary>
''' 皮肤可绑定的事件名。
''' <para>
''' 这里只放**语义稳定**的动作，不暴露控件本身：皮肤作者写的是
''' <c>"click": "LaunchGame"</c>，而不是去猜某个按钮叫什么名字。
''' PCL 内部按钮改名、页面重构都不影响这些名字。
''' </para>
''' <para>
''' 新增动作时只需在这里加常量，并在 <see cref="SkinEvents.Invoke"/> 里补一条分派。
''' </para>
''' </summary>
Public Module SkinAction

    '---- 启动页 ----
    Public Const LaunchGame As String = "LaunchGame"
    Public Const SelectVersion As String = "SelectVersion"
    Public Const OpenVersionSetup As String = "OpenVersionSetup"
    Public Const SelectAccount As String = "SelectAccount"
    Public Const GoOnline As String = "GoOnline"
    Public Const SwitchGroup As String = "SwitchGroup"

    '---- 导航 ----
    Public Const GoLaunch As String = "GoLaunch"
    Public Const GoDownload As String = "GoDownload"
    Public Const GoSetup As String = "GoSetup"
    Public Const GoMore As String = "GoMore"
    Public Const GoExtra As String = "GoExtra"
    Public Const GoBack As String = "GoBack"

    '---- 窗口 ----
    Public Const MinimizeWindow As String = "MinimizeWindow"
    Public Const CloseWindow As String = "CloseWindow"

    '---- 皮肤自身 ----
    Public Const ReloadSkin As String = "ReloadSkin"

    ''' <summary>全部动作名，用于文档与校验。</summary>
    Public ReadOnly Property All As String()
        Get
            Return {
                LaunchGame, SelectVersion, OpenVersionSetup, SelectAccount,
                GoOnline, SwitchGroup,
                GoLaunch, GoDownload, GoSetup, GoMore, GoExtra, GoBack,
                MinimizeWindow, CloseWindow, ReloadSkin
            }
        End Get
    End Property

    ''' <summary>名字是否是已知动作。</summary>
    Public Function IsKnown(Name As String) As Boolean
        If String.IsNullOrWhiteSpace(Name) Then Return False
        Return All.Contains(Name.Trim(), StringComparer.OrdinalIgnoreCase)
    End Function

End Module
