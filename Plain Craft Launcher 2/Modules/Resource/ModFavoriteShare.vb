''' <summary>
''' 收藏夹分享码。只记录「平台 + 项目 ID」，压成最短的文本后再做 Base64。
''' <para>
''' 文本格式：<c>P</c> + 若干条目，条目之间无分隔符，靠字段自身的长度规则切分：
''' 第 1 个字符是平台（<c>c</c> = CurseForge，<c>m</c> = Modrinth），
''' CurseForge 的项目 ID 是纯数字，用 <c>.</c> 结尾；
''' Modrinth 的项目 ID 是 8 位小写字母数字，直接连写。
''' 这样一长串里没有冗余的分隔符，Base64 之后也最短。
''' </para>
''' </summary>
Public Module ModFavoriteShare

    Private Const FormatVersion As String = "1"
    Private Const Prefix As String = "PCLDST:"

    ''' <summary>分享码里的一条记录。</summary>
    Public Class ShareItem
        Public Platform As ResourcePlatforms
        ''' <summary>平台侧的项目 ID。CurseForge 为数字串，Modrinth 为 8 位短 ID。</summary>
        Public ProjectId As String = ""
    End Class

    ''' <summary>
    ''' 把若干条记录编码成分享文本。没有有效记录时返回空字符串。
    ''' </summary>
    Public Function Encode(Items As IEnumerable(Of ShareItem)) As String
        Dim Builder As New StringBuilder
        For Each Item In Items
            If Item Is Nothing OrElse String.IsNullOrWhiteSpace(Item.ProjectId) Then Continue For
            Select Case Item.Platform
                Case ResourcePlatforms.CurseForge
                    '纯数字，用点号收尾，便于解析
                    If Item.ProjectId.Any(Function(c) c < "0"c OrElse c > "9"c) Then Continue For
                    Builder.Append("c").Append(Item.ProjectId).Append("."c)
                Case ResourcePlatforms.Modrinth
                    '8 位小写字母数字，长度固定
                    If Item.ProjectId.Length <> 8 Then Continue For
                    If Not Item.ProjectId.All(Function(c) IsBase62(c)) Then Continue For
                    Builder.Append("m").Append(Item.ProjectId)
                Case Else
                    Continue For
            End Select
        Next
        If Builder.Length = 0 Then Return ""
        Return Prefix & FormatVersion & Convert.ToBase64String(Encoding.UTF8.GetBytes(Builder.ToString()))
    End Function

    ''' <summary>
    ''' 解析分享文本。无法识别的部分会被跳过，不会抛异常。
    ''' </summary>
    Public Function Decode(Text As String) As List(Of ShareItem)
        Dim Result As New List(Of ShareItem)
        If String.IsNullOrWhiteSpace(Text) Then Return Result
        Text = Text.Trim
        '去掉前缀（也容忍用户只贴了 Base64 主体）
        If Text.StartsWithF(Prefix) Then Text = Text.Substring(Prefix.Length)
        '第一个字符是格式版本号
        If Text.Length < 1 Then Return Result
        If Text(0) <> FormatVersion(0) Then Return Result
        Text = Text.Substring(1)
        Dim Raw As String
        Try
            Raw = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(Text)))
        Catch ex As Exception
            Logger.Trace($"分享码 Base64 解析失败：{ex.Message}")
            Return Result
        End Try
        '逐条解析
        Dim Index As Integer = 0
        While Index < Raw.Length
            Dim PlatformChar As Char = Raw(Index)
            Index += 1
            Select Case PlatformChar
                Case "c"c
                    Dim EndIndex As Integer = Raw.IndexOf("."c, Index)
                    If EndIndex < 0 Then Exit While '残缺，直接结束
                    Dim Id As String = Raw.Substring(Index, EndIndex - Index)
                    Index = EndIndex + 1
                    If Id.Length > 0 AndAlso Id.All(Function(c) c >= "0"c AndAlso c <= "9"c) Then
                        Result.Add(New ShareItem With {.Platform = ResourcePlatforms.CurseForge, .ProjectId = Id})
                    End If
                Case "m"c
                    If Index + 8 > Raw.Length Then Exit While
                    Dim Id As String = Raw.Substring(Index, 8)
                    Index += 8
                    If Id.All(Function(c) IsBase62(c)) Then
                        Result.Add(New ShareItem With {.Platform = ResourcePlatforms.Modrinth, .ProjectId = Id})
                    End If
                Case Else
                    Exit While '遇到不认识的字符就停下，避免错位后解析出一堆垃圾
            End Select
        End While
        Return Result
    End Function

    ''' <summary>补上被裁掉的 Base64 填充符。</summary>
    Private Function IsBase62(c As Char) As Boolean
        Return (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "0"c AndAlso c <= "9"c)
    End Function

    Private Function PadBase64(Text As String) As String
        Select Case Text.Length Mod 4
            Case 2 : Return Text & "=="
            Case 3 : Return Text & "="
            Case Else : Return Text
        End Select
    End Function

End Module
