''' <summary>存档里的一个维度。</summary>
Public Class MapDimension
    ''' <summary>内部键：overworld / nether / end，或自定义维度的资源位置。</summary>
    Public Key As String = "overworld"
    ''' <summary>显示名。</summary>
    Public DisplayName As String = "主世界"
    ''' <summary>区域文件夹（以反斜杠结尾）。</summary>
    Public RegionFolder As String = ""
    ''' <summary>区域文件数量，用于显示。</summary>
    Public FileCount As Integer = 0

    Public Overrides Function ToString() As String
        Return DisplayName
    End Function
End Class

''' <summary>
''' 扫描存档里的所有维度。26.1 之后维度都在 dimensions\&lt;命名空间&gt;\&lt;维度名&gt;\region\，
''' 整合包可以加任意多个自定义维度，所以不能写死三个。
''' </summary>
Public Module DstMapDimensions

    ''' <summary>已知维度的友好名。</summary>
    Private ReadOnly KnownNames As New Dictionary(Of String, String) From {
        {"minecraft:overworld", "主世界"},
        {"minecraft:the_nether", "下界"},
        {"minecraft:the_end", "末地"}
    }

    ''' <summary>
    ''' 扫描存档，返回所有有区域文件的维度。
    ''' 新结构（26.1+）走 dimensions\*，旧结构走根目录 / DIM-1 / DIM1。
    ''' </summary>
    Public Function Scan(SaveDir As String) As List(Of MapDimension)
        Dim Result As New List(Of MapDimension)
        Dim Base As String = PathUtils.AddSlashSuffix(SaveDir)
        Dim Seen As New HashSet(Of String)

        '新结构：dimensions\<命名空间>\<维度名>\region\
        Dim DimRoot As String = Base & "dimensions\"
        If DirectoryUtils.Exists(DimRoot) Then
            Try
                For Each NsDir In DirectoryUtils.EnumerateDirectories(DimRoot, False)
                    Dim Ns As String = PathUtils.GetLastPart(NsDir)
                    For Each DimDir In DirectoryUtils.EnumerateDirectories(NsDir, False)
                        Dim DimName As String = PathUtils.GetLastPart(DimDir)
                        Dim RegionDir As String = PathUtils.AddSlashSuffix(DimDir) & "region\"
                        If Not DirectoryUtils.Exists(RegionDir) Then Continue For
                        Dim Id As String = Ns & ":" & DimName
                        If Not Seen.Add(Id) Then Continue For
                        Dim Count As Integer = CountMca(RegionDir)
                        If Count = 0 Then Continue For
                        Result.Add(New MapDimension With {
                            .Key = Id,
                            .DisplayName = FriendlyName(Id),
                            .RegionFolder = RegionDir,
                            .FileCount = Count
                        })
                    Next
                Next
            Catch ex As Exception
                Logger.Warn(ex, "扫描 dimensions 目录失败")
            End Try
        End If

        '旧结构：根目录 region\、DIM-1\region\、DIM1\region\
        Dim Legacy As New List(Of Tuple(Of String, String))
        Legacy.Add(Tuple.Create("minecraft:overworld", Base & "region\"))
        Legacy.Add(Tuple.Create("minecraft:the_nether", Base & "DIM-1\region\"))
        Legacy.Add(Tuple.Create("minecraft:the_end", Base & "DIM1\region\"))
        For Each Item In Legacy
            If Not DirectoryUtils.Exists(Item.Item2) Then Continue For
            If Not Seen.Add(Item.Item1) Then Continue For
            Dim Count As Integer = CountMca(Item.Item2)
            If Count = 0 Then Continue For
            Result.Add(New MapDimension With {
                .Key = Item.Item1,
                .DisplayName = FriendlyName(Item.Item1),
                .RegionFolder = Item.Item2,
                .FileCount = Count
            })
        Next

        '主世界排最前，其余按名字
        Result.Sort(
            Function(A, B)
                If A.Key = "minecraft:overworld" Then Return -1
                If B.Key = "minecraft:overworld" Then Return 1
                Return String.Compare(A.DisplayName, B.DisplayName, StringComparison.OrdinalIgnoreCase)
            End Function)
        Return Result
    End Function

    ''' <summary>统计区域文件数。</summary>
    Private Function CountMca(Folder As String) As Integer
        Try
            Dim N As Integer = 0
            For Each F In DirectoryUtils.EnumerateFiles(Folder, False, "*.mca")
                N += 1
            Next
            Return N
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>维度 ID → 显示名。未知维度用维度名部分。</summary>
    Public Function FriendlyName(Id As String) As String
        Dim Known As String = ""
        If KnownNames.TryGetValue(Id, Known) Then Return Known
        '自定义维度：取冒号后面那段，把下划线换成空格
        Dim Idx As Integer = Id.IndexOf(":"c)
        Dim Part As String = If(Idx >= 0, Id.Substring(Idx + 1), Id)
        Return Part.Replace("_", " ")
    End Function

    ''' <summary>
    ''' 兼容旧的三选一：把内部键映射成维度 ID。
    ''' </summary>
    Public Function KeyToId(Key As String) As String
        Select Case Key
            Case "nether"
                Return "minecraft:the_nether"
            Case "end"
                Return "minecraft:the_end"
            Case Else
                Return "minecraft:overworld"
        End Select
    End Function

End Module
