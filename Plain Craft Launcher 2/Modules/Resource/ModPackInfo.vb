Imports System.IO
Imports System.IO.Compression
Imports Newtonsoft.Json.Linq

''' <summary>
''' 资源包 / 数据包 / 光影包这类「一个 zip 或一个文件夹」的描述性信息读取。
''' <para>
''' 只读字节与文本，不碰任何 WPF 类型，所以既能在后台线程用，也能被多个页面共用。
''' 资源包、光影包、存档数据包三处原本各抄了一份「读图标」的实现，这里统一收口。
''' </para>
''' </summary>
Public Module ModPackInfo

    ''' <summary>数据包图标常用的文件名，按优先级排列。</summary>
    Public ReadOnly PackIconNames As String() = {"pack.png", "icon.png", "logo.png", "globe.png", "cover.png", "banner.png"}

    ''' <summary>数据包说明文件。</summary>
    Public Const PackMetaName As String = "pack.mcmeta"

    ''' <summary>
    ''' 读出数据包图标的原始字节；读不到返回 Nothing。
    ''' 压缩包读根目录里的候选文件名，文件夹同理。
    ''' </summary>
    Public Function ReadPackIconBytes(Item As ModPackFolder.PackFolderItem) As Byte()
        If Item Is Nothing Then Return Nothing
        Try
            If Item.IsFolder Then
                Dim Base As String = PathUtils.AddSlashSuffix(Item.PackPath)
                For Each FileName In PackIconNames
                    Dim IconPath As String = Base & FileName
                    If FileUtils.Exists(IconPath) Then Return File.ReadAllBytes(IconPath)
                Next
                '根目录下任意 png，不少整合包把图标起名叫别的
                For Each F In DirectoryUtils.EnumerateFiles(Base, False, "*.png")
                    Return File.ReadAllBytes(F)
                Next
                Return Nothing
            End If
            If Not FileUtils.Exists(Item.PackPath) Then Return Nothing
            Using Zip As ZipArchive = ZipFile.OpenRead(Item.PackPath)
                Dim Entry As ZipArchiveEntry = FindRootEntry(Zip, PackIconNames)
                If Entry Is Nothing Then Return Nothing
                Using Ms As New MemoryStream()
                    Using ZipStream As Stream = Entry.Open()
                        ZipStream.CopyTo(Ms)
                    End Using
                    Return Ms.ToArray()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取数据包图标失败：" & Item.PackPath)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 读 pack.mcmeta 里的说明文字。description 既可能是字符串，也可能是 {"text": "…"}，
    ''' 两种都认；读不到返回空字符串。
    ''' </summary>
    Public Function ReadPackDescription(Item As ModPackFolder.PackFolderItem) As String
        If Item Is Nothing Then Return ""
        Try
            Dim Meta As String = ReadPackText(Item, PackMetaName)
            If String.IsNullOrWhiteSpace(Meta) Then Return ""
            Dim Root As JObject = JObject.Parse(Meta)
            Dim Pack As JObject = TryCast(Root("pack"), JObject)
            If Pack Is Nothing Then Return ""
            Dim Desc As JToken = Pack("description")
            If Desc Is Nothing Then Return ""
            Select Case Desc.Type
                Case JTokenType.String
                    Return Desc.ToString().Trim()
                Case JTokenType.Object
                    Dim Text As JToken = Desc("text")
                    If Text IsNot Nothing Then Return Text.ToString().Trim()
            End Select
            Return ""
        Catch ex As Exception
            'pack.mcmeta 写错很常见，不该影响整个列表
            Logger.Info("读取数据包说明失败：" & Item.DisplayName & "：" & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 读包内某个文件的文本内容：压缩包按条目名找，文件夹按相对路径找。
    ''' 找不到或读失败返回空字符串。
    ''' </summary>
    Public Function ReadPackText(Item As ModPackFolder.PackFolderItem, EntryName As String) As String
        If Item Is Nothing OrElse EntryName = "" Then Return ""
        Try
            If Item.IsFolder Then
                Dim FilePath As String = PathUtils.AddSlashSuffix(Item.PackPath) & EntryName.Replace("/"c, ChrW(92))
                If Not FileUtils.Exists(FilePath) Then Return ""
                Return FileUtils.ReadAsString(FilePath)
            End If
            If Not FileUtils.Exists(Item.PackPath) Then Return ""
            Using Zip As ZipArchive = ZipFile.OpenRead(Item.PackPath)
                Dim Entry As ZipArchiveEntry = FindEntry(Zip, EntryName)
                If Entry Is Nothing Then Return ""
                Using ZipStream As Stream = Entry.Open()
                    Using Reader As New StreamReader(ZipStream)
                        Return Reader.ReadToEnd()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取包内文件失败：" & EntryName & " @ " & Item.PackPath)
            Return ""
        End Try
    End Function

    ''' <summary>在压缩包里按名称找条目，忽略大小写与分隔符差异。</summary>
    Public Function FindEntry(Zip As ZipArchive, EntryName As String) As ZipArchiveEntry
        If Zip Is Nothing OrElse EntryName = "" Then Return Nothing
        Dim Target As String = EntryName.Replace("/"c, ChrW(92))
        For Each Item In Zip.Entries
            If Item.FullName.Replace("/"c, ChrW(92)).Equals(Target, StringComparison.OrdinalIgnoreCase) Then Return Item
        Next
        Return Nothing
    End Function

    ''' <summary>按候选文件名依次在压缩包根目录里找条目。</summary>
    Public Function FindRootEntry(Zip As ZipArchive, CandidateNames As String()) As ZipArchiveEntry
        If Zip Is Nothing OrElse CandidateNames Is Nothing Then Return Nothing
        For Each FileName In CandidateNames
            For Each Item In Zip.Entries
                If Item.FullName.Equals(FileName, StringComparison.OrdinalIgnoreCase) Then Return Item
            Next
        Next
        '根目录下任意 png
        For Each Item In Zip.Entries
            If Item.FullName.IndexOf("/"c) < 0 AndAlso Item.FullName.IndexOf(ChrW(92)) < 0 AndAlso
               Item.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) Then Return Item
        Next
        Return Nothing
    End Function

End Module
