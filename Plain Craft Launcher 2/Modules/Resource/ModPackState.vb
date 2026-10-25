Imports System.IO
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' 资源包 / 光影包在游戏里是否启用。
'''
''' 磁盘上的 .zip 只是「文件在那儿」，游戏实际启用哪些，由版本目录下的 options.txt 决定：
''' <code>
''' resourcePacks:["vanilla","file/某个资源包.zip"]
''' incompatibleResourcePacks:[]
''' </code>
''' 只有出现在 resourcePacks 里的包才会被游戏加载，顺序就是覆盖顺序（靠后的盖住靠前的）。
''' 把包停用、或包格式与游戏不符时，游戏会把它从这里移除，转手记到 incompatibleResourcePacks。
'''
''' 这里只做 options.txt 的读写，不碰 WPF，可以在后台线程调用。
''' </summary>
Public Module ModPackState

    ''' <summary>
    ''' 读一份 options.txt 里的启用列表。
    ''' 文件不存在、没有这一项、或者内容不是合法 JSON 时，返回空列表。
    ''' </summary>
    ''' <param name="OptionsFile">options.txt 的完整路径。</param>
    ''' <param name="Key">resourcePacks 或 incompatibleResourcePacks。</param>
    Public Function GetEnabledPacks(OptionsFile As String, Optional Key As String = "resourcePacks") As List(Of String)
        Dim Result As New List(Of String)
        If String.IsNullOrWhiteSpace(OptionsFile) Then Return Result
        Dim Raw As String = ""
        Try
            If Not FileUtils.Exists(OptionsFile) Then Return Result
            '直接读文件，不走 ReadIni 的缓存：游戏运行时也会改这个文件，
            '缓存里的值可能已经过期，而我们需要的正是最新的那份
            For Each Line In FileUtils.ReadAsLines(OptionsFile, True)
                Dim Index As Integer = Line.IndexOf(":"c)
                If Index > 0 AndAlso String.Equals(Line.Substring(0, Index), Key, StringComparison.Ordinal) Then
                    Raw = Line.Substring(Index + 1).Trim()
                    Exit For
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"读取 {Key} 失败：{OptionsFile}")
            Return Result
        End Try
        If Raw = "" Then Return Result
        '选项值本身是一段 JSON 数组，例如 ["vanilla","file/a.zip"]
        Try
            Dim Arr = JArray.Parse(Raw)
            For Each Item In Arr
                Dim Value = TryCast(Item, JValue)
                If Value IsNot Nothing AndAlso Value.Type = JTokenType.[String] Then Result.Add(Value.Value(Of String)())
            Next
        Catch ex As Exception
            '不是合法 JSON 就按逗号切，至少别把整个功能带崩
            Logger.Warn(ex, $"解析 {Key} 失败，将按逗号切分：{Raw}")
            For Each Part In Raw.TrimStart("["c).TrimEnd("]"c).Split(","c)
                Dim Value = Part.Trim().Trim(""""c)
                If Value <> "" Then Result.Add(Value)
            Next
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 把一个资源包写进启用列表（已存在则不动）。
    ''' </summary>
    ''' <param name="OptionsFile">options.txt 的完整路径。</param>
    ''' <param name="Entry">要启用的条目，例如 file/某个资源包.zip。</param>
    ''' <param name="Front">True 时插到最前面（盖住其他资源包），False 时追加到末尾。</param>
    Public Function EnablePack(OptionsFile As String, Entry As String, Optional Front As Boolean = False) As Boolean
        If String.IsNullOrWhiteSpace(OptionsFile) OrElse String.IsNullOrWhiteSpace(Entry) Then Return False
        Try
            Dim Enabled As List(Of String) = GetEnabledPacks(OptionsFile)
            If Enabled.Any(Function(E) String.Equals(E, Entry, StringComparison.OrdinalIgnoreCase)) Then Return True
            If Enabled.Count = 0 Then Enabled.Add("vanilla") '空列表会让原版资源也一起关掉，补上
            If Front Then
                Enabled.Insert(0, Entry)
            Else
                Enabled.Add(Entry)
            End If
            Return WritePacks(OptionsFile, "resourcePacks", Enabled)
        Catch ex As Exception
            Logger.Warn(ex, $"写入资源包启用状态失败：{Entry}")
            Return False
        End Try
    End Function

    ''' <summary>把资源包从启用列表里移除。找不到也算成功。</summary>
    Public Function DisablePack(OptionsFile As String, Entry As String) As Boolean
        If String.IsNullOrWhiteSpace(OptionsFile) OrElse String.IsNullOrWhiteSpace(Entry) Then Return False
        Try
            Dim Enabled As List(Of String) = GetEnabledPacks(OptionsFile)
            Dim Removed As Integer = Enabled.RemoveAll(Function(E) String.Equals(E, Entry, StringComparison.OrdinalIgnoreCase))
            If Removed = 0 Then Return True
            Return WritePacks(OptionsFile, "resourcePacks", Enabled)
        Catch ex As Exception
            Logger.Warn(ex, $"移除资源包启用状态失败：{Entry}")
            Return False
        End Try
    End Function

    ''' <summary>整体改写启用列表（保持给定顺序）。</summary>
    Public Function SetEnabledPacks(OptionsFile As String, Values As List(Of String)) As Boolean
        If String.IsNullOrWhiteSpace(OptionsFile) Then Return False
        Return WritePacks(OptionsFile, "resourcePacks", Values)
    End Function

    ''' <summary>
    ''' 判断一个资源包当前是否被游戏启用。
    ''' </summary>
    ''' <param name="OptionsFile">options.txt 的完整路径。</param>
    ''' <param name="PackFileName">资源包文件名，例如 某包.zip；文件夹形态传文件夹名。</param>
    Public Function IsPackEnabled(OptionsFile As String, PackFileName As String) As Boolean
        If String.IsNullOrWhiteSpace(PackFileName) Then Return False
        Dim Enabled As List(Of String) = GetEnabledPacks(OptionsFile)
        Return Enabled.Any(
            Function(E)
                '条目形如 file/某包.zip 或 某包.zip，两种都认
                Dim Name = E
                If Name.StartsWith("file/", StringComparison.OrdinalIgnoreCase) Then Name = Name.Substring(5)
                Return String.Equals(Name, PackFileName, StringComparison.OrdinalIgnoreCase)
            End Function)
    End Function

    ''' <summary>
    ''' 把一个键的值写回 options.txt。
    ''' 只改目标那一行，其余内容原样保留（包括未知的键和注释），避免把玩家的设置写丢。
    ''' </summary>
    Private Function WritePacks(OptionsFile As String, Key As String, Values As List(Of String)) As Boolean
        Dim NewValue As String = "[" & String.Join(",", Values.Select(Function(V) """" & V.Replace(""""c, """""") & """")) & "]"
        Dim Lines As New List(Of String)
        Dim Replaced As Boolean = False
        Try
            If FileUtils.Exists(OptionsFile) Then Lines.AddRange(FileUtils.ReadAsLines(OptionsFile, True))
        Catch ex As Exception
            Logger.Warn(ex, $"读取 options.txt 失败：{OptionsFile}")
            Return False
        End Try
        For I = 0 To Lines.Count - 1
            Dim Index As Integer = Lines(I).IndexOf(":"c)
            If Index > 0 AndAlso String.Equals(Lines(I).Substring(0, Index), Key, StringComparison.Ordinal) Then
                Lines(I) = Key & ":" & NewValue
                Replaced = True
            End If
        Next
        If Not Replaced Then Lines.Add(Key & ":" & NewValue)
        Try
            FileUtils.Write(OptionsFile, String.Join(vbCrLf, Lines) & vbCrLf)
            'options.txt 也在 PCL 的 ini 缓存里，改完要让它失效，否则启动时按旧值覆盖回去
            IniClearCache(OptionsFile)
            Logger.Info($"已将 {Key} 写回 {OptionsFile}：{NewValue}")
            Return True
        Catch ex As Exception
            Logger.Warn(ex, $"写入 options.txt 失败：{OptionsFile}")
            Return False
        End Try
    End Function

End Module
