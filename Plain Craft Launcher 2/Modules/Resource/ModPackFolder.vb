''' <summary>
''' 「一个文件夹里堆着若干压缩包和子文件夹，启用与否靠改扩展名」这类资源包的公共存储层。
''' <para>
''' 资源包（resourcepacks）和光影包（shaderpacks）在磁盘上的约定完全一样：
''' 启用时是 <c>xxx.zip</c>，停用时改名成 <c>xxx.zip.disabled</c>；
''' 文件夹形态由游戏直接读取，不参与启用 / 停用。
''' </para>
''' <para>
''' 这里只做文件系统操作，不碰任何 WPF 类型，所以两个页面可以共用，
''' 也可以在后台线程里直接调用。
''' </para>
''' </summary>
Public Module ModPackFolder

    ''' <summary>停用状态下追加的扩展名后缀。</summary>
    Public Const PackDisabledSuffix As String = ".disabled"

    ''' <summary>扫描结果里的一条：可能是一个压缩包，也可能是一个文件夹。</summary>
    Public Class PackFolderItem
        ''' <summary>完整路径。</summary>
        Public PackPath As String = ""
        ''' <summary>显示名：去掉 <c>.zip</c> / <c>.zip.disabled</c> 后的文件名，或文件夹名。</summary>
        Public DisplayName As String = ""
        ''' <summary>是不是文件夹形态。</summary>
        Public IsFolder As Boolean = False
        ''' <summary>
        ''' 文件名是否是 <c>.zip</c>（而不是旧版本留下的 <c>.zip.disabled</c>）。
        ''' <para>
        ''' 这**不是**启用状态。游戏是否启用一个资源包，只看 options.txt 的 resourcePacks，
        ''' 也就是 <see cref="IsActive"/>。这个字段只在兼容旧文件时才用得上。
        ''' </para>
        ''' </summary>
        Public IsEnabled As Boolean = False
        ''' <summary>
        ''' 游戏是否真的启用了它。这是本页唯一关心的状态，读自 options.txt。
        ''' </summary>
        Public IsActive As Boolean = False
        ''' <summary>大小（字节）。-1 表示读取失败。</summary>
        Public SizeBytes As Long = -1

        ''' <summary>是否被停用。只有压缩包会被停用。</summary>
        Public ReadOnly Property IsDisabled As Boolean
            Get
                Return Not IsFolder AndAlso Not IsEnabled
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 切换启用状态的结果。<c>Ok</c> 为 False 时 <c>Reason</c> 是可以直接展示给用户的原因。
    ''' </summary>
    Public Class PackToggleResult
        Public Ok As Boolean = False
        Public NewPath As String = ""
        Public Reason As String = ""
    End Class

    ''' <summary>
    ''' 扫描一个资源包文件夹。文件夹不存在时返回空列表，不抛异常。
    ''' <para>纯文件操作，可在工作线程调用。</para>
    ''' </summary>
    ''' <param name="Folder">要扫描的文件夹，末尾带不带反斜杠都行。</param>
    ''' <param name="KindName">出现在日志里的名字，例如「资源包」。</param>
    ''' <param name="KeepZipSuffix">
    ''' 显示名里是否保留 <c>.zip</c>。资源包页去掉、光影包页保留，这是两页各自的既有显示习惯，
    ''' 统一会改变用户看到的文字，所以做成参数而不是硬编码一种。
    ''' </param>
    ''' <param name="OptionsFile">
    ''' 版本目录下的 options.txt。给了就顺便读出游戏真正启用的列表，填进 IsActive；
    ''' 留空则 IsActive 一律为 False（光影包没有这个概念）。
    ''' </param>
    Public Function ScanPackFolder(Folder As String, KindName As String, Optional KeepZipSuffix As Boolean = False,
                                   Optional OptionsFile As String = "") As List(Of PackFolderItem)
        Dim Items As New List(Of PackFolderItem)
        If String.IsNullOrWhiteSpace(Folder) OrElse Not DirectoryUtils.Exists(Folder) Then Return Items

        '压缩包形态：.zip 是启用，.zip.disabled 是停用
        For Each FilePath In DirectoryUtils.EnumerateFiles(Folder, False)
            Dim FileName As String = PathUtils.GetLastPart(FilePath)
            Dim Item As New PackFolderItem With {.PackPath = FilePath}
            If FileName.EndsWith(".zip" & PackDisabledSuffix, StringComparison.OrdinalIgnoreCase) Then
                Item.IsEnabled = False
                Item.DisplayName = FileName.Substring(0, FileName.Length - PackDisabledSuffix.Length)
            ElseIf FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
                Item.IsEnabled = True
                Item.DisplayName = If(KeepZipSuffix, FileName, PathUtils.GetFileNameWithoutExtension(FilePath))
            Else
                Continue For '不认识的扩展名，不当成资源包
            End If
            Item.SizeBytes = GetPackFileSize(FilePath, KindName)
            Items.Add(Item)
        Next

        '文件夹形态：游戏会直接读，不参与启用 / 停用
        For Each DirPath In DirectoryUtils.EnumerateDirectories(Folder, False)
            Dim Item As New PackFolderItem With {
                .PackPath = DirPath, .DisplayName = PathUtils.GetLastPart(DirPath),
                .IsFolder = True, .IsEnabled = True}
            Item.SizeBytes = GetPackFolderSize(DirPath, KindName)
            Items.Add(Item)
        Next

        '读出游戏真正启用的列表，标到每个条目上。
        '条目必须是「文件在磁盘上」且「名字在列表里」才算启用：
        'options.txt 里可能残留指向已删除文件的条目，那种不能算数。
        If Not String.IsNullOrWhiteSpace(OptionsFile) Then
            Dim Active As List(Of String) = ModPackState.GetEnabledPacks(OptionsFile)
            For Each Item In Items
                Dim Name As String = PathUtils.GetLastPart(Item.PackPath)
                Item.IsActive = Active.Any(
                    Function(E)
                        Dim Value = E
                        If Value.StartsWith("file/", StringComparison.OrdinalIgnoreCase) Then Value = Value.Substring(5)
                        Return String.Equals(Value, Name, StringComparison.OrdinalIgnoreCase)
                    End Function)
                If Item.IsActive Then
                    '文件不在了，这个条目是死项，界面上不该显示成已启用
                    If Not Item.IsFolder AndAlso Not FileUtils.Exists(Item.PackPath) Then Item.IsActive = False
                End If
            Next
        End If

        '启用中的排在前面，同状态按名字排
        Items.Sort(
            Function(A As PackFolderItem, B As PackFolderItem)
                If A.IsActive <> B.IsActive Then Return If(A.IsActive, -1, 1)
                Return String.Compare(A.DisplayName, B.DisplayName, StringComparison.OrdinalIgnoreCase)
            End Function)
        Return Items
    End Function

    ''' <summary>
    ''' 算出切换启用状态之后应该用的新路径。
    ''' 只是拼路径，不检查目标是否存在。
    ''' </summary>
    Public Function PackToggledPath(Item As PackFolderItem, TargetEnabled As Boolean) As String
        If Item Is Nothing Then Return ""
        If TargetEnabled Then
            If Not Item.PackPath.EndsWith(PackDisabledSuffix, StringComparison.OrdinalIgnoreCase) Then Return Item.PackPath
            Return Item.PackPath.Substring(0, Item.PackPath.Length - PackDisabledSuffix.Length)
        End If
        Return Item.PackPath & PackDisabledSuffix
    End Function

    ''' <summary>
    ''' 启用 / 停用一个资源包，只改文件名。
    ''' <para>不弹提示、不刷新界面：那是调用方的事，这里只负责把磁盘改对。</para>
    ''' </summary>
    Public Function SetPackEnabled(Item As PackFolderItem, TargetEnabled As Boolean, KindName As String) As PackToggleResult
        Dim R As New PackToggleResult
        If Item Is Nothing Then
            R.Reason = "条目为空"
            Return R
        End If
        If Item.IsFolder Then
            R.Reason = $"文件夹形态的{KindName}不能在启动器里启用或停用。"
            Return R
        End If
        If Item.IsEnabled = TargetEnabled Then
            '已经是目标状态，当成成功，免得调用方还要自己判一次
            R.Ok = True
            R.NewPath = Item.PackPath
            Return R
        End If
        If Not FileUtils.Exists(Item.PackPath) Then
            R.Reason = $"这个{KindName}文件已经不在了：{Item.DisplayName}"
            Return R
        End If

        Dim NewPath As String = PackToggledPath(Item, TargetEnabled)
        '目标已存在时改名会覆盖掉另一个包，宁可不做。
        '同名文件夹也算冲突：游戏对两种形态一视同仁，改名过去同样会打架。
        If Not String.Equals(NewPath, Item.PackPath, StringComparison.OrdinalIgnoreCase) AndAlso
           (FileUtils.Exists(NewPath) OrElse DirectoryUtils.Exists(NewPath)) Then
            R.Reason = "目标文件已存在，改名会覆盖它，所以停下了：" & PathUtils.GetLastPart(NewPath)
            Return R
        End If

        Try
            FileUtils.Move(Item.PackPath, NewPath)
        Catch ex As Exception
            Logger.Warn(ex, $"切换{KindName}状态失败：{Item.DisplayName}")
            R.Reason = "改名失败：" & ex.Message
            Return R
        End Try

        R.Ok = True
        R.NewPath = NewPath
        Return R
    End Function

    ''' <summary>删除一个资源包（文件夹形态连内容一起删）。失败会记日志。</summary>
    Public Function DeletePackItem(Item As PackFolderItem, KindName As String) As Boolean
        If Item Is Nothing Then Return False
        Try
            If Item.IsFolder Then
                DirectoryUtils.Delete(Item.PackPath, True)
            Else
                FileUtils.Delete(Item.PackPath)
            End If
            Return True
        Catch ex As Exception
            Logger.Warn(ex, $"删除{KindName}失败：{Item.DisplayName}")
            Return False
        End Try
    End Function

    ''' <summary>读单个文件的大小，失败返回 -1。</summary>
    Public Function GetPackFileSize(FilePath As String, KindName As String) As Long
        Try
            Return FileUtils.GetInfo(FilePath).Length
        Catch ex As Exception
            Logger.Warn(ex, $"读取{KindName}大小失败：{FilePath}")
            Return -1
        End Try
    End Function

    ''' <summary>递归统计一个文件夹的总大小，失败返回 -1。</summary>
    Public Function GetPackFolderSize(DirPath As String, KindName As String) As Long
        Try
            Dim Total As Long = 0
            For Each FilePath In DirectoryUtils.EnumerateFiles(DirPath, True)
                Try
                    Total += FileUtils.GetInfo(FilePath).Length
                Catch ex As Exception
                    '单个文件读不到不该让整次统计失败，但要知道是哪个
                    Logger.Warn(ex, $"统计{KindName}大小时跳过文件：{FilePath}")
                End Try
            Next
            Return Total
        Catch ex As Exception
            Logger.Warn(ex, $"统计{KindName}文件夹大小失败：{DirPath}")
            Return -1
        End Try
    End Function

End Module
