''' <summary>存档里的一个玩家，用于地图以谁为中心渲染。</summary>
Public Class MapPlayer
    ''' <summary>playerdata 文件路径。</summary>
    Public FilePath As String = ""
    ''' <summary>玩家 UUID（带短横线）。</summary>
    Public Uuid As String = ""
    ''' <summary>显示名。</summary>
    Public Name As String = ""
    ''' <summary>所在维度：overworld / nether / end。</summary>
    Public Dimension As String = "overworld"
    ''' <summary>X 坐标（方块）。</summary>
    Public X As Integer = 0
    ''' <summary>Z 坐标（方块）。</summary>
    Public Z As Integer = 0
    ''' <summary>是否主玩家。</summary>
    Public IsOwner As Boolean = False

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

''' <summary>
''' 存档的玩家数据读取：给地图页提供「选哪个玩家、在哪个维度、什么坐标」。
''' </summary>
Public Module DstMapPlayers

    ''' <summary>读 level.dat 里记录的主玩家 UUID。26.1 用 singleplayer_uuid，之前是 Player.UUID。</summary>
    Public Function OwnerUuidOf(SaveDir As String) As String
        Try
            Dim Root As NbtTag = DstNbt.ReadFile(PathUtils.AddSlashSuffix(SaveDir) & "level.dat")
            Return DstSavePaths.OwnerUuid(Root)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>把 UUID 的 IntArray 转成带短横线的字符串。</summary>
    Public Function UuidFromInts(Ints As Integer()) As String
        If Ints Is Nothing OrElse Ints.Length < 4 Then Return ""
        Dim Hex As New List(Of String)
        For Each I In Ints
            Hex.Add(I.ToString("x8"))
        Next
        Dim S As String = String.Join("", Hex)
        If S.Length <> 32 Then Return ""
        Return S.Substring(0, 8) & "-" & S.Substring(8, 4) & "-" & S.Substring(12, 4) & "-" &
               S.Substring(16, 4) & "-" & S.Substring(20, 12)
    End Function

    ''' <summary>
    ''' 扫描存档里的所有玩家，按「主玩家在前」排好序。
    ''' 26.1 的玩家数据在 players\data\，之前的在 playerdata\，由 DstSavePaths 统一处理。
    ''' 维度取 Dimension（1.16.2 后是字符串，之前是数字）。
    ''' </summary>
    Public Function Scan(SaveDir As String) As List(Of MapPlayer)
        Dim Result As New List(Of MapPlayer)
        Dim Base As String = PathUtils.AddSlashSuffix(SaveDir)
        Dim Folder As String = DstSavePaths.PlayerFolder(Base)
        If Not DirectoryUtils.Exists(Folder) Then Return Result
        Dim Owner As String = OwnerUuidOf(Base)
        Dim Files As New List(Of String)
        Try
            For Each F In DirectoryUtils.EnumerateFiles(Folder, False, "*.dat")
                '排除 .dat_old 这类备份
                If DstSavePaths.IsPlayerDataFile(F) Then Files.Add(F)
            Next
        Catch
        End Try
        For Each F In Files
            Dim P As New MapPlayer With {.FilePath = F, .Uuid = PathUtils.GetFileNameWithoutExtension(F)}
            Try
                Dim Root As NbtTag = DstNbt.ReadFile(F)
                '名字：有的版本存 Name，没有就用 UUID
                Dim NameTag As NbtTag = DstNbt.CompoundGet(Root, "Name")
                If NameTag IsNot Nothing AndAlso NameTag.TagType = DstNbt.TypeString Then
                    P.Name = DirectCast(NameTag.TagValue, String)
                End If
                '维度
                Dim DimTag As NbtTag = DstNbt.CompoundGet(Root, "Dimension")
                If DimTag Is Nothing Then DimTag = DstNbt.GetByPath(Root, "Data.Dimension")
                If DimTag IsNot Nothing Then
                    If DimTag.TagType = DstNbt.TypeString Then
                        P.Dimension = DimensionKeyOf(DirectCast(DimTag.TagValue, String))
                    Else
                        P.Dimension = DimensionKeyOfId(CInt(DimTag.TagValue))
                    End If
                End If
                '坐标
                Dim PosTag As NbtTag = DstNbt.CompoundGet(Root, "Pos")
                If PosTag Is Nothing Then PosTag = DstNbt.GetByPath(Root, "Data.Pos")
                If PosTag IsNot Nothing Then
                    Dim Items As List(Of NbtTag) = TryCast(PosTag.TagValue, List(Of NbtTag))
                    If Items IsNot Nothing AndAlso Items.Count >= 3 Then
                        P.X = CInt(Math.Floor(CDbl(Items(0).TagValue)))
                        P.Z = CInt(Math.Floor(CDbl(Items(2).TagValue)))
                    End If
                End If
            Catch ex As Exception
                Logger.Info("读取玩家数据失败：" & F & "：" & ex.Message)
            End Try
            If Owner <> "" AndAlso P.Uuid.Equals(Owner, StringComparison.OrdinalIgnoreCase) Then P.IsOwner = True
            If P.Name = "" Then P.Name = If(P.IsOwner, "主玩家", P.Uuid)
            If P.IsOwner Then P.Name &= "（主玩家）"
            Result.Add(P)
        Next
        '主玩家排最前，其余按名字
        Result.Sort(
            Function(A, B)
                If A.IsOwner <> B.IsOwner Then Return If(A.IsOwner, -1, 1)
                Return String.Compare(A.Name, B.Name, StringComparison.OrdinalIgnoreCase)
            End Function)
        Return Result
    End Function

    ''' <summary>资源位置形式的维度 → 内部键。</summary>
    Public Function DimensionKeyOf(Id As String) As String
        If Id Is Nothing Then Return "overworld"
        If Id.EndsWith("the_nether", StringComparison.OrdinalIgnoreCase) Then Return "nether"
        If Id.EndsWith("the_end", StringComparison.OrdinalIgnoreCase) Then Return "end"
        Return "overworld"
    End Function

    ''' <summary>数字形式的维度 → 内部键。</summary>
    Public Function DimensionKeyOfId(Id As Integer) As String
        Select Case Id
            Case -1
                Return "nether"
            Case 1
                Return "end"
            Case Else
                Return "overworld"
        End Select
    End Function

    ''' <summary>内部键 → 显示名。</summary>
    Public Function DimensionName(Key As String) As String
        Select Case Key
            Case "nether"
                Return "下界"
            Case "end"
                Return "末地"
            Case Else
                Return "主世界"
        End Select
    End Function

End Module
