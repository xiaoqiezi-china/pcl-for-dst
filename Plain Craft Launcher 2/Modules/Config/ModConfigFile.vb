Imports System.Text

''' <summary>
''' 启动器的本地配置文件（PCL\config.ini）。
''' <para>
''' 和 ReadIni / WriteIni 那一套的区别：这个文件是用户可见、可手工编辑的，
''' 而且要在启动器更新之后依然保留，所以它不进注册表、不参与版本覆盖。
''' </para>
''' <para>
''' 凭据的优先级是：设置里手填 &gt; 这个文件 &gt; 编译期内置。
''' 这样闭源发布版可以带一份初始数据，开源版留空即可。
''' </para>
''' </summary>
Friend Module ModConfigFile

    ''' <summary>配置文件的简写名，实际路径为 PCL\config.ini。</summary>
    Public Const IniFileName As String = "config.ini"

    Public Const SectionCredential As String = "Credential"
    Public Const SectionCloud As String = "Cloud"

    ''' <summary>读过的键缓存，键为 节/键。</summary>
    Private ReadOnly Cache As New ConcurrentDictionary(Of String, String)

    ''' <summary>配置文件完整路径。</summary>
    Public ReadOnly Property IniFullPath As String
        Get
            Return Paths.Base & "PCL\" & IniFileName
        End Get
    End Property

    ''' <summary>读取一个配置项；文件或键不存在时返回空串。</summary>
    Public Function Read(Section As String, Key As String) As String
        Dim CacheKey As String = Section & "/" & Key
        Dim Cached As String = Nothing
        If Cache.TryGetValue(CacheKey, Cached) Then Return Cached
        Dim Value As String = ""
        Try
            If FileUtils.Exists(IniFullPath) Then Value = ParseValue(FileUtils.ReadAsString(IniFullPath), Section, Key)
        Catch ex As Exception
            Logger.Warn(ex, $"读取 {IniFileName} 失败（{Section}/{Key}）")
        End Try
        Cache(CacheKey) = Value
        Return Value
    End Function

    ''' <summary>清空缓存，供文件被外部改动后调用。</summary>
    Public Sub ClearCache()
        Cache.Clear()
    End Sub

    ''' <summary>
    ''' 文件不存在时释放一份初始配置。已存在则什么都不做，绝不覆盖用户改动。
    ''' </summary>
    Public Sub EnsureExists()
        Try
            If FileUtils.Exists(IniFullPath) Then Return
            DirectoryUtils.Create(Paths.Base & "PCL\")
            Dim Sb As New StringBuilder
            Sb.AppendLine("; PCL 本地配置")
            Sb.AppendLine("; 改完重启启动器生效。更新启动器不会覆盖这个文件。")
            Sb.AppendLine()
            Sb.AppendLine("[" & SectionCredential & "]")
            Sb.AppendLine("; 微软登录的 ClientId，留空则用启动器内置值")
            Sb.AppendLine("ClientId=")
            Sb.AppendLine("; CurseForge 的 API Key，留空则用启动器内置值")
            Sb.AppendLine("CurseForgeKey=")
            Sb.AppendLine()
            Sb.AppendLine("[" & SectionCloud & "]")
            Sb.AppendLine("; 服务地址与凭证，留空则用启动器内置值")
            Sb.AppendLine("ApiBase=")
            Sb.AppendLine("ApiKey=")
            Sb.AppendLine("; 当前选中的预设 id，切换预设时由启动器维护")
            Sb.AppendLine("PresetId=")
            Dim Initial As String = Sb.ToString()
            '闭源发布版会带一份初始数据，由这里填进去；开源版这段是空操作
            Initial = MergeBuiltinDefaults(Initial)
            FileUtils.WriteAtomic(IniFullPath, Initial)
            Cache.Clear()
            Logger.Info($"已释放默认配置：{IniFullPath}")
        Catch ex As Exception
            Logger.Warn(ex, $"释放默认配置失败：{IniFullPath}")
        End Try
    End Sub

    ''' <summary>把编译期内置值填进初始配置文本；开源版没有内置值，原样返回。</summary>
    Private Function MergeBuiltinDefaults(Text As String) As String
        Dim Result As String = Text
        Result = ReplaceValue(Result, SectionCredential, "ClientId", BuiltinOAuthClientId)
        Result = ReplaceValue(Result, SectionCredential, "CurseForgeKey", BuiltinCurseForgeKey)
        Result = ReplaceValue(Result, SectionCloud, "ApiBase", DstBotPresetApi)
        Result = ReplaceValue(Result, SectionCloud, "ApiKey", DstBotPresetToken)
        Result = ReplaceValue(Result, SectionCloud, "PresetId", BuiltinPresetId)
        Return Result
    End Function

    ''' <summary>把某一行 Key= 补上值；值为空时不动。</summary>
    Private Function ReplaceValue(Text As String, Section As String, Key As String, Value As String) As String
        If String.IsNullOrWhiteSpace(Value) Then Return Text
        Dim Lines As String() = Text.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
        Dim InSection As Boolean = False
        For i = 0 To Lines.Length - 1
            Dim Line As String = Lines(i).Trim()
            If Line.StartsWithF("[") Then
                InSection = Line.Equals($"[{Section}]", StringComparison.OrdinalIgnoreCase)
            ElseIf InSection Then
                Dim Pos As Integer = Line.IndexOf("="c)
                If Pos > 0 AndAlso Line.Substring(0, Pos).Trim().Equals(Key, StringComparison.OrdinalIgnoreCase) Then
                    Lines(i) = Key & "=" & Sanitize(Value)
                    Return String.Join(vbCrLf, Lines)
                End If
            End If
        Next
        Return Text
    End Function

    ''' <summary>从 ini 文本里取出某个节下某个键的值；只认第一个等号。</summary>
    Private Function ParseValue(Text As String, Section As String, Key As String) As String
        If String.IsNullOrEmpty(Text) Then Return ""
        Dim InSection As Boolean = False
        For Each RawLine In Text.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim Line As String = RawLine.Trim()
            If Line.Length = 0 OrElse Line.StartsWithF(";") OrElse Line.StartsWithF("#") Then Continue For
            If Line.StartsWithF("[") Then
                InSection = Line.Equals($"[{Section}]", StringComparison.OrdinalIgnoreCase)
                Continue For
            End If
            If Not InSection Then Continue For
            Dim Pos As Integer = Line.IndexOf("="c)
            If Pos <= 0 Then Continue For
            If Line.Substring(0, Pos).Trim().Equals(Key, StringComparison.OrdinalIgnoreCase) Then
                Return Line.Substring(Pos + 1).Trim()
            End If
        Next
        Return ""
    End Function

    ''' <summary>写入一行配置；只改目标行，保留注释与其它内容。</summary>
    Public Function Write(Section As String, Key As String, Value As String) As Boolean
        Try
            Dim Text As String = If(FileUtils.Exists(IniFullPath), FileUtils.ReadAsString(IniFullPath), "")
            Dim Lines As New List(Of String)()
            If Not String.IsNullOrEmpty(Text) Then Lines.AddRange(Text.Replace(vbCrLf, vbLf).Split(CChar(vbLf)))
            Dim InSection As Boolean = False
            Dim Done As Boolean = False
            For i = 0 To Lines.Count - 1
                Dim Line As String = Lines(i).Trim()
                If Line.StartsWithF("[") Then
                    InSection = Line.Equals($"[{Section}]", StringComparison.OrdinalIgnoreCase)
                    Continue For
                End If
                If Not InSection Then Continue For
                Dim Pos As Integer = Line.IndexOf("="c)
                If Pos > 0 AndAlso Line.Substring(0, Pos).Trim().Equals(Key, StringComparison.OrdinalIgnoreCase) Then
                    Lines(i) = Key & "=" & Sanitize(Value)
                    Done = True
                    Exit For
                End If
            Next
            If Not Done Then
                If Not Lines.Any(Function(L) L.Trim().Equals($"[{Section}]", StringComparison.OrdinalIgnoreCase)) Then
                    If Lines.Count > 0 Then Lines.Add("")
                    Lines.Add("[" & Section & "]")
                End If
                Lines.Add(Key & "=" & Sanitize(Value))
            End If
            DirectoryUtils.Create(Paths.Base & "PCL\")
            FileUtils.WriteAtomic(IniFullPath, String.Join(vbCrLf, Lines))
            Cache(Section & "/" & Key) = Value
            Return True
        Catch ex As Exception
            Logger.Warn(ex, $"写入 {IniFileName} 失败（{Section}/{Key}）")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 净化写入 ini 的文本：去掉换行与其它控制字符。
    ''' 这些值可能来自远程，留换行就有机会在文件里伪造出额外的键。
    ''' </summary>
    Private Function Sanitize(Value As String) As String
        If String.IsNullOrEmpty(Value) Then Return ""
        Dim Sb As New StringBuilder
        For Each C As Char In Value
            If Not Char.IsControl(C) Then Sb.Append(C)
        Next
        Return Sb.ToString().Trim()
    End Function

End Module
