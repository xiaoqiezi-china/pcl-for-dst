''' <summary>
''' 从 Mod 压缩包里读出它自己声明的内部标识（modid）。
''' 只读文件、不联网，供收藏夹安装去重等场景复用。
''' </summary>
Public Module ModFileIdentity

    ''' <summary>缓存：文件路径|大小|修改时间 → 该文件声明的全部 modid（小写，已去重）。</summary>
    Private ReadOnly Cache As New ConcurrentDictionary(Of String, List(Of String))

    ''' <summary>
    ''' 读取一个 Mod 文件内部声明的全部 modid。读取失败或不是 Mod 时返回空列表。
    ''' </summary>
    Public Function GetModIds(FilePath As String) As List(Of String)
        If String.IsNullOrEmpty(FilePath) Then Return New List(Of String)
        Dim Key As String = ""
        Try
            Dim Info = FileUtils.GetInfo(FilePath)
            If Not Info.Exists Then Return New List(Of String)
            Key = $"{FilePath}|{Info.Length}|{Info.LastWriteTimeUtc.Ticks}"
            Dim Cached As List(Of String) = Nothing
            If Cache.TryGetValue(Key, Cached) Then Return Cached
        Catch ex As Exception
            Logger.Warn(ex, $"读取 Mod 文件信息失败（{FilePath}）")
            Return New List(Of String)
        End Try
        Dim Result As New List(Of String)
        Try
            Using Archive As ZipArchive = ZipFile.OpenRead(FilePath)
                'Fabric / Quilt
                ReadJsonIds(Archive, "fabric.mod.json", "id", Result)
                ReadJsonIds(Archive, "quilt.mod.json", "id", Result)
                'Forge / NeoForge
                ReadTomlIds(Archive, "META-INF/mods.toml", Result)
                ReadTomlIds(Archive, "META-INF/neoforge.mods.toml", Result)
                '旧版 Forge
                ReadMcmodInfo(Archive, Result)
            End Using
        Catch ex As Exception
            Logger.Trace($"读取 Mod 内部标识失败（{FilePath}）：{ex.Message}")
        End Try
        Result = Result.Where(Function(s) Not String.IsNullOrWhiteSpace(s)).
                        Select(Function(s) s.Trim.Lower).Distinct.ToList
        Cache(Key) = Result
        Return Result
    End Function

    ''' <summary>读取一个 json 里的 id 字段，以及 provides 里声明的别名。</summary>
    Private Sub ReadJsonIds(Archive As ZipArchive, EntryName As String, IdField As String, Result As List(Of String))
        Dim Entry As ZipArchiveEntry = FindEntry(Archive, EntryName)
        If Entry Is Nothing Then Return
        Try
            Dim Json As JObject = Entry.Open().ReadString().DeserializeJson()
            Dim Id As String = Json(IdField)?.ToString()
            If Not String.IsNullOrWhiteSpace(Id) Then Result.Add(Id)
            Dim Provides = Json("provides")
            If Provides IsNot Nothing AndAlso Provides.Type = JTokenType.Array Then
                For Each One In Provides
                    Dim Text As String = One?.ToString()
                    If Not String.IsNullOrWhiteSpace(Text) Then Result.Add(Text)
                Next
            End If
        Catch ex As Exception
            Logger.Trace($"解析 {EntryName} 失败：{ex.Message}")
        End Try
    End Sub

    ''' <summary>读取 mods.toml 里的 modId 声明。</summary>
    Private Sub ReadTomlIds(Archive As ZipArchive, EntryName As String, Result As List(Of String))
        Dim Entry As ZipArchiveEntry = FindEntry(Archive, EntryName)
        If Entry Is Nothing Then Return
        Try
            Dim Text As String = Entry.Open().ReadString()
            Dim Pattern As String = "modId\s*=\s*[" & Chr(34) & "']([^" & Chr(34) & "']+)[" & Chr(34) & "']"
            For Each Found As System.Text.RegularExpressions.Match In System.Text.RegularExpressions.Regex.Matches(Text, Pattern)
                Result.Add(Found.Groups(1).Value)
            Next
        Catch ex As Exception
            Logger.Trace($"解析 {EntryName} 失败：{ex.Message}")
        End Try
    End Sub

    ''' <summary>读取旧版 Forge 的 mcmod.info。</summary>
    Private Sub ReadMcmodInfo(Archive As ZipArchive, Result As List(Of String))
        Dim Entry As ZipArchiveEntry = FindEntry(Archive, "mcmod.info")
        If Entry Is Nothing Then Return
        Try
            Dim Token As JToken = Entry.Open().ReadString().DeserializeJson()
            Dim Items As JToken = If(Token.Type = JTokenType.Array, Token, Token("modList"))
            If Items Is Nothing OrElse Items.Type <> JTokenType.Array Then Return
            For Each One In Items
                Dim Id As String = One("modid")?.ToString()
                If Not String.IsNullOrWhiteSpace(Id) Then Result.Add(Id)
            Next
        Catch ex As Exception
            Logger.Trace($"解析 mcmod.info 失败：{ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' 把一个 modid 归一化：只保留小写字母与数字。
    ''' 这样 roughly-enough-items 与 RoughlyEnoughItems 会被视为同一个标识。
    ''' </summary>
    Public Function NormalizeModId(Id As String) As String
        If String.IsNullOrEmpty(Id) Then Return ""
        Dim Builder As New StringBuilder(Id.Length)
        For Each c As Char In Id
            If (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "0"c AndAlso c <= "9"c) Then
                Builder.Append(c)
            ElseIf c >= "A"c AndAlso c <= "Z"c Then
                Builder.Append(Char.ToLowerInvariant(c))
            End If
        Next
        Return Builder.ToString()
    End Function

    ''' <summary>
    ''' 推测一个工程可能对应的 modid，用于和本地文件比对。
    ''' 不下载文件就读不到真正的 modid，这里用工程短名与原始名归一化后猜测。
    ''' </summary>
    Public Function GetProjectIds(Project As ResourceProject) As List(Of String)
        Dim Result As New List(Of String)
        If Project Is Nothing Then Return Result
        For Each Candidate In {Project.Slug, Project.RawName}
            Dim Normalized As String = NormalizeModId(Candidate)
            '太短的标识容易撞车（例如 modid 恰好是 "core"），只在足够有辨识度时才拿来比对
            If Normalized.Length >= 5 AndAlso Not Result.Contains(Normalized) Then Result.Add(Normalized)
        Next
        Return Result
    End Function
    ''' <summary>压缩包内查找条目，忽略大小写。</summary>
    Private Function FindEntry(Archive As ZipArchive, EntryName As String) As ZipArchiveEntry
        Dim Exact As ZipArchiveEntry = Archive.GetEntry(EntryName)
        If Exact IsNot Nothing Then Return Exact
        Return Archive.Entries.FirstOrDefault(Function(e) String.Equals(e.FullName, EntryName, StringComparison.OrdinalIgnoreCase))
    End Function

End Module
