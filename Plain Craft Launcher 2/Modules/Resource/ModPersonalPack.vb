Imports System.IO
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 个性化包（.dclart，本质是 zip）：把当前启动器的外观配置与素材打包成一个文件，
''' 换机器或分享给别人时一键还原。
'''
''' 包含四部分：
''' <list type="bullet">
''' <item>主题：主题编号、深浅模式、外观风格、自定义主题的四个滑块与 9 个定制色。</item>
''' <item>背景图片：PCL\Pictures 下的全部图片。</item>
''' <item>自带音乐：PCL\Musics 下的全部音频。</item>
''' <item>（主页文件不打包：它可能引用外部网址，跨机器还原意义不大）</item>
''' </list>
'''
''' 包里用 manifest.json 记录版本与主题设置，素材放在对应子目录，
''' 这样以后要加东西（比如字体、光标）只要多一个子目录，老包依然能读。
''' </summary>
Public Module ModPersonalPack

    ''' <summary>个性化包的扩展名。</summary>
    Public Const PackExtension As String = ".dclart"
    ''' <summary>包内清单文件名。</summary>
    Private Const ManifestName As String = "manifest.json"
    ''' <summary>清单格式版本。加字段时递增，读的时候按它做兼容。</summary>
    Private Const ManifestVersion As Integer = 1

    Private Const FolderPictures As String = "pictures/"
    Private Const FolderMusics As String = "musics/"

    ''' <summary>参与打包的主题相关设置项。</summary>
    Private ReadOnly ThemeKeys As String() = {
        "UiLauncherTheme", "DstDarkMode", "DstAppearance",
        "UiLauncherHue", "UiLauncherSat", "UiLauncherDelta", "UiLauncherLight",
        "UiLauncherCustomColor1", "UiLauncherCustomColor2", "UiLauncherCustomColor3",
        "UiLauncherCustomColor4", "UiLauncherCustomColor5", "UiLauncherCustomColor6",
        "UiLauncherCustomColor7", "UiLauncherCustomColor8", "UiLauncherCustomColor9"
    }

    ''' <summary>导出结果。</summary>
    Public Class ExportResult
        Public Ok As Boolean = False
        Public Reason As String = ""
        ''' <summary>实际写入的文件数（不含清单）。</summary>
        Public FileCount As Integer = 0
    End Class

    ''' <summary>
    ''' 导出个性化包。
    ''' </summary>
    ''' <param name="OutputPath">输出的 .dclart 路径。</param>
    Public Function Export(OutputPath As String) As ExportResult
        Dim R As New ExportResult
        Try
            Dim Sources As New Dictionary(Of String, String)
            Dim Pictures As String = Paths.Base & "PCL\Pictures\"
            Dim Musics As String = Paths.Base & "PCL\Musics\"

            '背景图片
            If DirectoryUtils.Exists(Pictures) Then
                For Each File In DirectoryUtils.EnumerateFiles(Pictures, True)
                    Dim Name As String = PathUtils.GetLastPart(File)
                    Sources(FolderPictures & Name) = File
                Next
            End If
            '自带音乐
            If DirectoryUtils.Exists(Musics) Then
                For Each File In DirectoryUtils.EnumerateFiles(Musics, True)
                    Dim Name As String = PathUtils.GetLastPart(File)
                    Sources(FolderMusics & Name) = File
                Next
            End If

            If Sources.Count = 0 Then
                R.Reason = "没有可打包的内容：背景图片和自带音乐都是空的。"
                Return R
            End If

            '清单：主题设置 + 内容索引
            Dim Theme As New JObject
            For Each Key In ThemeKeys
                Theme(Key) = ReadSettingRaw(Key)
            Next
            Dim Manifest As New JObject
            Manifest("format") = ManifestVersion
            Manifest("launcher") = VersionBaseName
            Manifest("exportedAt") = Date.Now.ToString("yyyy-MM-dd HH:mm:ss")
            Manifest("theme") = Theme
            Dim PicturesList As New JArray
            Dim MusicsList As New JArray
            For Each Pair In Sources
                If Pair.Key.StartsWith(FolderPictures, StringComparison.Ordinal) Then
                    PicturesList.Add(Pair.Key.Substring(FolderPictures.Length))
                ElseIf Pair.Key.StartsWith(FolderMusics, StringComparison.Ordinal) Then
                    MusicsList.Add(Pair.Key.Substring(FolderMusics.Length))
                End If
            Next
            Manifest("pictures") = PicturesList
            Manifest("musics") = MusicsList

            '清单本身也要进包：写到临时文件再交给打包函数
            Dim ManifestPath As String = PathTemp & "DclArtManifest.json"
            FileUtils.Write(ManifestPath, Manifest.ToString())
            Sources(ManifestName) = ManifestPath
            R.FileCount = Sources.Count - 1

            DirectoryUtils.Create(PathUtils.RemoveLastPart(Path.GetFullPath(OutputPath)))
            FileUtils.CreateZipFromFiles(OutputPath, Sources)
            FileUtils.Delete(ManifestPath)
            Logger.Info($"已导出个性化包：{OutputPath}（{R.FileCount} 个文件）")
            R.Ok = True
            Return R
        Catch ex As Exception
            Logger.Error(ex, "导出个性化包失败")
            R.Reason = "导出失败：" & ex.Message
            Return R
        End Try
    End Function

    ''' <summary>
    ''' 导入个性化包。会先解压到临时目录，校验清单后再覆盖到 PCL 目录。
    ''' <para>
    ''' 覆盖前会问一次：背景图片和音乐是整目录替换（否则旧图会和包里的混在一起），
    ''' 这个行为必须让用户知情。
    ''' </para>
    ''' </summary>
    Public Function Import(PackPath As String) As ExportResult
        Dim R As New ExportResult
        Dim Temp As String = PathTemp & "DclArtImport\"
        Try
            If Not FileUtils.Exists(PackPath) Then
                R.Reason = "文件不存在"
                Return R
            End If
            Try
                If DirectoryUtils.Exists(Temp) Then DirectoryUtils.Delete(Temp)
            Catch ex As Exception
                Logger.Warn(ex, "清理个性化包临时目录失败")
            End Try
            DirectoryUtils.Create(Temp)
            FileUtils.ExtractToDirectory(PackPath, Temp)

            Dim ManifestPath As String = Temp & ManifestName
            If Not FileUtils.Exists(ManifestPath) Then
                R.Reason = "这不是个性化包：缺少 manifest.json"
                Return R
            End If
            Dim Manifest = JObject.Parse(FileUtils.ReadAsString(ManifestPath))
            If Manifest("format") Is Nothing Then
                R.Reason = "个性化包格式不正确"
                Return R
            End If

            If MyMsgBox("导入会覆盖当前的背景图片与音乐（同名覆盖，不会删除包里没有的图片）。" & vbCrLf &
                        "主题设置也会一并应用。" & vbCrLf & vbCrLf & "确定导入吗？",
                        "导入个性化包", "导入", "取消") <> 1 Then
                R.Reason = "已取消"
                Return R
            End If

            '主题设置
            Dim Theme = TryCast(Manifest("theme"), JObject)
            If Theme IsNot Nothing Then
                For Each Prop In Theme.Properties()
                    If Not ThemeKeys.Contains(Prop.Name) Then Continue For
                    Try
                        ApplySetting(Prop.Name, Prop.Value)
                    Catch ex As Exception
                        Logger.Warn(ex, $"应用个性化包设置 {Prop.Name} 失败")
                    End Try
                Next
            End If

            '素材
            Dim Pictures As String = Paths.Base & "PCL\Pictures\"
            Dim Musics As String = Paths.Base & "PCL\Musics\"
            DirectoryUtils.Create(Pictures)
            DirectoryUtils.Create(Musics)
            R.FileCount = CopyFolderFiles(Temp & FolderPictures, Pictures) +
                          CopyFolderFiles(Temp & FolderMusics, Musics)

            Logger.Info($"已导入个性化包：{PackPath}（{R.FileCount} 个文件）")
            R.Ok = True
            Return R
        Catch ex As Exception
            Logger.Error(ex, "导入个性化包失败")
            R.Reason = "导入失败：" & ex.Message
            Return R
        Finally
            Try
                If DirectoryUtils.Exists(Temp) Then DirectoryUtils.Delete(Temp)
            Catch ex As Exception
                Logger.Warn(ex, "清理个性化包临时目录失败")
            End Try
        End Try
    End Function

    ''' <summary>把 From 下的文件复制到 To，返回复制的数量。</summary>
    Private Function CopyFolderFiles(From As String, [To] As String) As Integer
        If Not DirectoryUtils.Exists(From) Then Return 0
        Dim Count As Integer = 0
        For Each File In DirectoryUtils.EnumerateFiles(From, True)
            Try
                FileUtils.Copy(File, [To] & PathUtils.GetLastPart(File))
                Count += 1
            Catch ex As Exception
                Logger.Warn(ex, "复制个性化包素材失败：" & File)
            End Try
        Next
        Return Count
    End Function

    ''' <summary>按设置项原本的类型写回。</summary>
    Private Sub ApplySetting(Key As String, Token As JToken)
        Dim Entry = Settings.Entries(Key)
        Select Case Entry.Type
            Case GetType(Integer) : Settings.Set(Key, Token.Value(Of Integer)())
            Case GetType(Boolean) : Settings.Set(Key, Token.Value(Of Boolean)())
            Case GetType(Double) : Settings.Set(Key, Token.Value(Of Double)())
            Case Else : Settings.Set(Key, Token.ToString())
        End Select
    End Sub

    ''' <summary>
    ''' 读取一个设置项的原始值，保留它的类型（数字 / 布尔 / 字符串）。
    ''' </summary>
    Private Function ReadSettingRaw(Key As String) As JToken
        Try
            Dim Value As Object = Settings.Get(Of Object)(Key)
            If Value Is Nothing Then Return JValue.CreateNull()
            Select Case True
                Case TypeOf Value Is Integer : Return New JValue(CInt(Value))
                Case TypeOf Value Is Boolean : Return New JValue(CBool(Value))
                Case TypeOf Value Is Double : Return New JValue(CDbl(Value))
                Case Else : Return New JValue(CStr(Value))
            End Select
        Catch ex As Exception
            Logger.Warn(ex, $"读取设置 {Key} 失败")
            Return JValue.CreateNull()
        End Try
    End Function

End Module
