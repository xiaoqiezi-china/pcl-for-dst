Imports System.Net

''' <summary>
''' 启动器自动更新的数据源。
''' <para>
''' 这里**只认 GitHub Release**，与预设服务器（<see cref="DstBot"/>）没有任何关系：
''' 预设服务器是可替换的、由使用者挑选的第三方服务，让它来决定「装哪个启动器」风险太大。
''' </para>
''' <para>
''' 取版本、展开镜像、测速、算摘要这些通用逻辑在 <see cref="ReleaseFeed"/> 里，
''' 副程序（汉化工具等）用的是同一份实现；这里只负责「启动器自己的仓库是哪个」。
''' </para>
''' </summary>
Friend Module ModUpdateSource

    ''' <summary>仓库标识。发版用的 Release 就挂在这里。</summary>
    Private Const RepoOwner As String = "xiaoqiezi-china"
    Private Const RepoName As String = "pcl-for-dst"

    ''' <summary>启动器自己的 Release 数据源。</summary>
    Private ReadOnly Feed As New ReleaseFeed(RepoOwner, RepoName)

    ''' <summary>仓库 Releases 页，用于「手动下载」。</summary>
    Public ReadOnly Property ReleasesPage As String
        Get
            Return Feed.ReleasesPage
        End Get
    End Property

    ''' <summary>
    ''' 旧版本会把查询结果写进 PCL\UpdateCache.json，这份缓存已经废了。
    ''' 见到就删，免得用户以为启动器还在读它。
    ''' </summary>
    Public Sub CleanupLegacyCache()
        Static Done As Boolean = False
        If Done Then Return
        Done = True
        Try
            Dim Legacy As String = Paths.Base & "PCL\UpdateCache.json"
            If FileUtils.Exists(Legacy) Then
                FileUtils.Delete(Legacy)
                Logger.Info("已删除旧版更新缓存")
            End If
        Catch ex As Exception
            Logger.Info("删除旧版更新缓存失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>查询最新 Release（取 .exe 附件）。必须在工作线程调用。</summary>
    Public Function FetchLatest() As ReleaseInfo
        CleanupLegacyCache()
        Return Feed.FetchLatest(".exe")
    End Function

    ''' <summary>
    ''' 把一个 Release 附件地址展开成所有可用下载源：官方在前，镜像在后。
    ''' 交给 NetFile 依次尝试，前一个失败会自动换下一个。
    ''' </summary>
    Public Function BuildSources(Url As String) As String()
        Return Feed.BuildSources(Url)
    End Function

    ''' <summary>
    ''' 测一批下载源的速度，按从快到慢返回。测不出来的排最后，仍保留作兜底。
    ''' </summary>
    Public Function RankSourcesBySpeed(Urls As String(), Optional ProbeBytes As Integer = 256 * 1024, Optional TimeoutMs As Integer = 5000) As String()
        Return Feed.RankSourcesBySpeed(Urls, ProbeBytes, TimeoutMs)
    End Function

    ''' <summary>算文件的 SHA-256，返回小写十六进制。失败返回空串。</summary>
    Public Function GetFileSha256(FilePath As String) As String
        Return Feed.GetFileSha256(FilePath)
    End Function

End Module
