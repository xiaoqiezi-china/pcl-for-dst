Imports System.Text.RegularExpressions

''' <summary>
''' 一份可以直接编辑的 NBT 文档。
''' 玩家数据、level.dat 都通过它来读改，由文档负责安全的写回与备份。
''' </summary>
Public Class DstNbtDocument
    ''' <summary>文件路径。</summary>
    Public ReadOnly Property FilePath As String
    ''' <summary>根标签。修改后调用 Save 才会写盘。</summary>
    Public Root As NbtTag
    ''' <summary>最后一次读取或保存时的错误，空串表示正常。</summary>
    Public LastError As String = ""

    Public Sub New(TargetFile As String, RootTag As NbtTag)
        FilePath = TargetFile
        Root = RootTag
    End Sub

    ''' <summary>读取一个 NBT 文件；失败时返回 Nothing 并记录错误。</summary>
    Public Shared Function Open(TargetFile As String) As DstNbtDocument
        Try
            Dim Tag As NbtTag = DstNbt.ReadFile(TargetFile)
            Return New DstNbtDocument(TargetFile, Tag)
        Catch ex As Exception
            Logger.Warn(ex, "读取 NBT 失败：" & TargetFile)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 安全写回：先写临时文件，重新读出来验证，再备份原文件、替换。
    ''' 任何一步失败都不会破坏原文件。
    ''' </summary>
    Public Function Save() As Boolean
        LastError = ""
        If Root Is Nothing Then
            LastError = "没有可写入的数据"
            Return False
        End If
        Dim Temp As String = FilePath & ".dsttmp"
        Dim Backup As String = FilePath & ".bak"
        Try
            DstNbt.WriteFile(Temp, Root, True)
        Catch ex As Exception
            LastError = "写入临时文件失败：" & ex.Message
            Logger.Error(ex, "写入临时 NBT 失败")
            Return False
        End Try
        '写出来的东西必须还能读回来，且结构没有崩
        Try
            Dim Check As NbtTag = DstNbt.ReadFile(Temp)
            If Check Is Nothing OrElse DstNbt.Children(Check).Count = 0 Then Throw New Exception("回读结果为空白")
        Catch ex As Exception
            LastError = "回读校验失败：" & ex.Message
            Logger.Error(ex, "NBT 回读校验失败")
            SafeDelete(Temp)
            Return False
        End Try
        Try
            SafeDelete(Backup)
            FileUtils.Copy(FilePath, Backup)
        Catch ex As Exception
            '备份失败不阻断裂写入，但记一笔
            Logger.Warn(ex, "备份 NBT 失败：" & FilePath)
        End Try
        Try
            FileUtils.Delete(FilePath)
            FileUtils.Move(Temp, FilePath)
        Catch ex As Exception
            LastError = "替换原文件失败：" & ex.Message
            Logger.Error(ex, "替换 NBT 文件失败")
            SafeDelete(Temp)
            Return False
        End Try
        Return True
    End Function

    Private Shared Sub SafeDelete(Target As String)
        Try
            If File.Exists(Target) Then File.Delete(Target)
        Catch
        End Try
    End Sub

    ''' <summary>按路径取标签。</summary>
    Public Function Find(Steps As List(Of NbtPathStep)) As NbtTag
        Return DstNbt.Resolve(Root, Steps)
    End Function

    ''' <summary>把整份文档导出为一段便于阅读的文本（等价于 SNBT 的简化版）。</summary>
    Public Function Dump(Optional MaxLines As Integer = 4000) As String
        Dim Lines As New List(Of String)
        DumpTag(Root, "", Lines, MaxLines)
        Return String.Join(Environment.NewLine, Lines)
    End Function

    Private Sub DumpTag(Tag As NbtTag, Indent As String, Lines As List(Of String), MaxLines As Integer)
        If Lines.Count >= MaxLines Then Return
        If Tag Is Nothing Then Return
        Select Case Tag.TagType
            Case DstNbt.TypeCompound
                Lines.Add(Indent & "{" & If(Tag.TagName = "", "", Tag.TagName & ": ") & "}")
                For Each Child In DstNbt.Children(Tag)
                    DumpTag(Child, Indent & "  ", Lines, MaxLines)
                Next
            Case DstNbt.TypeList
                Lines.Add(Indent & If(Tag.TagName = "", "", Tag.TagName & ": ") & "[列表 " & DstNbt.Children(Tag).Count & " 项]")
                For Each Child In DstNbt.Children(Tag)
                    DumpTag(Child, Indent & "  ", Lines, MaxLines)
                Next
            Case Else
                Lines.Add(Indent & If(Tag.TagName = "", "", Tag.TagName & ": ") & DstNbt.Describe(Tag))
        End Select
    End Sub

End Class

''' <summary>物品 ID 的输入校验，Minecraft 的命名空间要求。</summary>
Public Module DstItemValidate

    ''' <summary>合法的物品 ID：小写字母、数字、下划线、点、横线，可带命名空间。</summary>
    Public Function IsValidId(Id As String) As Boolean
        If String.IsNullOrWhiteSpace(Id) Then Return False
        Dim Text As String = Id.Trim().ToLower()
        If Not Text.Contains(":") Then Text = "minecraft:" & Text
        Return Regex.IsMatch(Text, "^[a-z0-9_.-]+:[a-z0-9_./-]+$")
    End Function

    ''' <summary>补全命名空间。</summary>
    Public Function NormalizeId(Id As String) As String
        Dim Text As String = If(Id, "").Trim().ToLower()
        If Text = "" Then Return ""
        If Not Text.Contains(":") Then Text = "minecraft:" & Text
        Return Text
    End Function

End Module
