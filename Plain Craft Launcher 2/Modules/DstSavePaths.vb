''' <summary>
''' 存档目录结构的路径解析。
''' 26.1（snapshot 6）之后 Mojang 重排了世界目录：
'''   旧：world\region\            world\DIM-1\region\        world\DIM1\region\
'''   新：world\dimensions\minecraft\overworld\region\        …\the_nether\region\   …\the_end\region\
'''   旧：world\playerdata\        world\advancements\        world\stats\
'''   新：world\players\data\      world\players\advancements\ world\players\stats\
''' 这里统一判断，新旧存档都能用。
''' </summary>
Public Module DstSavePaths

    ''' <summary>26.1 之后的维度目录名前缀。</summary>
    Private Const NewRoot As String = "dimensions\minecraft\"

    ''' <summary>
    ''' 某个维度的区域文件夹（以反斜杠结尾）。优先返回实际存在的那个。
    ''' Dimension 取 "overworld" / "nether" / "end"。
    ''' </summary>
    Public Function RegionFolder(SaveDir As String, Dimension As String) As String
        Dim Base As String = PathUtils.AddSlashSuffix(SaveDir)
        For Each Candidate In RegionFolderCandidates(Base, Dimension)
            If DirectoryUtils.Exists(Candidate) Then Return Candidate
        Next
        '都不存在时返回新结构路径，调用方据此判断「这个维度还没生成」
        Return RegionFolderCandidates(Base, Dimension)(0)
    End Function

    ''' <summary>某个维度的区域文件夹的所有可能位置，按优先级排列。</summary>
    Public Function RegionFolderCandidates(Base As String, Dimension As String) As List(Of String)
        Dim Result As New List(Of String)
        Select Case Dimension
            Case "nether"
                Result.Add(Base & NewRoot & "the_nether\region\")
                Result.Add(Base & "DIM-1\region\")
                '极少数整合包把下界放在自定义命名空间
                Result.Add(Base & "dimensions\minecraft\the_nether\region\")
            Case "end"
                Result.Add(Base & NewRoot & "the_end\region\")
                Result.Add(Base & "DIM1\region\")
            Case Else
                Result.Add(Base & NewRoot & "overworld\region\")
                Result.Add(Base & "region\")
        End Select
        Return Result
    End Function

    ''' <summary>所有维度实际存在的区域文件夹（用于「区域文件」页列出全部）。</summary>
    Public Function AllRegionFolders(SaveDir As String) As List(Of String)
        Dim Base As String = PathUtils.AddSlashSuffix(SaveDir)
        Dim Result As New List(Of String)
        For Each DimKey In {"overworld", "nether", "end"}
            Dim Folder As String = RegionFolder(Base, DimKey)
            If DirectoryUtils.Exists(Folder) AndAlso Not Result.Contains(Folder) Then Result.Add(Folder)
        Next
        '自定义维度：dimensions\<命名空间>\<维度名>\region\
        Try
            Dim DimRoot As String = Base & "dimensions\"
            If DirectoryUtils.Exists(DimRoot) Then
                For Each Ns In DirectoryUtils.EnumerateDirectories(DimRoot, False)
                    For Each DimDir In DirectoryUtils.EnumerateDirectories(Ns, False)
                        Dim RegionDir As String = PathUtils.AddSlashSuffix(DimDir) & "region\"
                        If DirectoryUtils.Exists(RegionDir) AndAlso Not Result.Contains(RegionDir) Then Result.Add(RegionDir)
                    Next
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "扫描自定义维度失败")
        End Try
        Return Result
    End Function

    ''' <summary>由区域文件夹反推维度名（用于界面显示）。</summary>
    Public Function DimensionNameOfFolder(Folder As String) As String
        If Folder Is Nothing Then Return "主世界"
        If Folder.Contains("the_nether") OrElse Folder.Contains("DIM-1") Then Return "下界"
        If Folder.Contains("the_end") OrElse Folder.Contains("DIM1\") Then Return "末地"
        If Folder.Contains("overworld") Then Return "主世界"
        '自定义维度：取 dimensions 后面那一段
        Dim Idx As Integer = Folder.IndexOf("dimensions\", StringComparison.OrdinalIgnoreCase)
        If Idx >= 0 Then
            Dim Rest As String = Folder.Substring(Idx + 11)
            Dim Parts As String() = Rest.Split("\"c)
            If Parts.Length >= 2 Then Return Parts(1)
        End If
        Return "主世界"
    End Function

    ''' <summary>
    ''' 玩家数据文件夹（以反斜杠结尾）。26.1 是 players\data\，之前是 playerdata\。
    ''' 都不存在时返回新结构路径。
    ''' </summary>
    Public Function PlayerFolder(SaveDir As String) As String
        Dim Base As String = PathUtils.AddSlashSuffix(SaveDir)
        Dim NewPath As String = Base & "players\data\"
        If DirectoryUtils.Exists(NewPath) Then Return NewPath
        Dim OldPath As String = Base & "playerdata\"
        If DirectoryUtils.Exists(OldPath) Then Return OldPath
        Return NewPath
    End Function

    ''' <summary>玩家数据文件的扩展名过滤：26.1 会留下 .dat_old 备份，要排除。</summary>
    Public Function IsPlayerDataFile(FilePath As String) As Boolean
        Return FilePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' 存档是不是 26.1 之后的新结构。
    ''' </summary>
    Public Function IsNewLayout(SaveDir As String) As Boolean
        Dim Base As String = PathUtils.AddSlashSuffix(SaveDir)
        Return DirectoryUtils.Exists(Base & "dimensions\") OrElse DirectoryUtils.Exists(Base & "players\")
    End Function

    ''' <summary>
    ''' level.dat 里主玩家的 UUID。
    ''' 26.1 用 singleplayer_uuid（IntArray）；之前是 Player.UUID（嵌套 compound）。
    ''' </summary>
    Public Function OwnerUuid(LevelRoot As NbtTag) As String
        If LevelRoot Is Nothing Then Return ""
        '新结构
        Dim NewTag As NbtTag = DstNbt.GetByPath(LevelRoot, "Data.singleplayer_uuid")
        If NewTag IsNot Nothing Then
            Dim Ints As Integer() = TryCast(NewTag.TagValue, Integer())
            If Ints IsNot Nothing Then Return DstMapPlayers.UuidFromInts(Ints)
        End If
        '旧结构
        Dim OldTag As NbtTag = DstNbt.GetByPath(LevelRoot, "Data.Player.UUID")
        If OldTag IsNot Nothing Then
            Dim Ints2 As Integer() = TryCast(OldTag.TagValue, Integer())
            If Ints2 IsNot Nothing Then Return DstMapPlayers.UuidFromInts(Ints2)
        End If
        Return ""
    End Function

    ''' <summary>
    ''' 难度的可读名。
    ''' 26.1 是 difficulty_settings.difficulty 字符串；之前是 Data.Difficulty 数字。
    ''' </summary>
    Public Function DifficultyOptions(LevelRoot As NbtTag) As List(Of KeyValuePair(Of String, String))
        Dim Result As New List(Of KeyValuePair(Of String, String))
        If DstNbt.GetByPath(LevelRoot, "Data.Difficulty") IsNot Nothing Then
            Result.Add(New KeyValuePair(Of String, String)("和平", "0"))
            Result.Add(New KeyValuePair(Of String, String)("简单", "1"))
            Result.Add(New KeyValuePair(Of String, String)("普通", "2"))
            Result.Add(New KeyValuePair(Of String, String)("困难", "3"))
        Else
            Result.Add(New KeyValuePair(Of String, String)("和平", "peaceful"))
            Result.Add(New KeyValuePair(Of String, String)("简单", "easy"))
            Result.Add(New KeyValuePair(Of String, String)("普通", "normal"))
            Result.Add(New KeyValuePair(Of String, String)("困难", "hard"))
        End If
        Return Result
    End Function

End Module
