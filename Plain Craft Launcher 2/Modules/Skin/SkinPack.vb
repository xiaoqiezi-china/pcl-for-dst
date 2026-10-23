Imports System.Windows.Media

''' <summary>
''' 一个皮肤包，对应 <c>PCL\Skins\&lt;Id&gt;\skin.json</c>。
''' <para>
''' 设计上只允许改外观，不允许插手页面逻辑：
''' 颜色覆盖 PCL 已有的 ColorBrush* / ColorObject* 资源键，
''' 几何量写进 <see cref="SkinMetrics"/>。这样换肤永远不会影响功能，
''' 也不会和上游的页面代码产生冲突。
''' </para>
''' </summary>
Public Class SkinPack

    ''' <summary>目录名，同时作为唯一标识。</summary>
    Public Property Id As String = ""
    ''' <summary>显示名称。</summary>
    Public Property Name As String = ""
    Public Property Author As String = ""
    Public Property Version As String = "1.0"
    Public Property Description As String = ""
    ''' <summary>配色基底：dark / light / auto。</summary>
    Public Property Base As String = "auto"

    ''' <summary>颜色覆盖：资源键 → 颜色字符串。</summary>
    Public ReadOnly Property Colors As New Dictionary(Of String, String)
    ''' <summary>几何度量覆盖。</summary>
    Public ReadOnly Property Metrics As New Dictionary(Of String, Double)
    ''' <summary>图标覆盖：语义名 → 几何路径。</summary>
    Public ReadOnly Property Icons As New Dictionary(Of String, String)
    ''' <summary>
    ''' 字符串型度量（颜色资源键名、标题栏纯色等）。
    ''' 单独一张表，是因为 <see cref="Metrics"/> 只装数字，混在一起会让解析逻辑变复杂。
    ''' </summary>
    Public ReadOnly Property StringMetrics As New Dictionary(Of String, String)

    ''' <summary>
    ''' 自定义界面：由 JSON 描述的一组控件，可以绑定动作。
    ''' 为空表示这个皮肤只改外观、不插入额外控件。
    ''' </summary>
    Public Property CustomUi As JArray = Nothing

    ''' <summary>皮肤目录的完整路径。</summary>
    Public Property FolderPath As String = ""
    ''' <summary>预览图路径，没有则为空。</summary>
    Public Property PreviewPath As String = ""
    ''' <summary>是否为内置皮肤。</summary>
    Public Property IsBuiltIn As Boolean = False

    ''' <summary>
    ''' 这个皮肤是否应当在当前深浅设置下启用。
    ''' auto 恒启用；dark 只在深色模式启用；light 只在浅色模式启用。
    ''' </summary>
    Public ReadOnly Property AppliesNow As Boolean
        Get
            Dim Dark As Boolean = Settings.Get(Of Boolean)("DstDarkMode")
            Select Case Base.Trim().ToLower()
                Case "dark" : Return Dark
                Case "light" : Return Not Dark
                Case Else : Return True
            End Select
        End Get
    End Property

End Class
