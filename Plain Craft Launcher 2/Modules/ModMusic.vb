Imports NAudio.Wave

Public Module ModMusic

#Region "播放列表"

    ''' <summary>
    ''' 接下来要播放的音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public MusicWaitingList As List(Of String) = Nothing
    ''' <summary>
    ''' 全部音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public MusicAllList As List(Of String) = Nothing
    ''' <summary>
    ''' 初始化音乐播放列表。
    ''' </summary>
    ''' <param name="ForceReload">强制全部重新载入列表。</param>
    ''' <param name="PreventFirst">在重载列表时避免让某项成为第一项。</param>
    Private Sub MusicListInit(ForceReload As Boolean, Optional PreventFirst As String = Nothing)
        If ForceReload Then MusicAllList = Nothing
        Try
            '初始化全部可用音乐列表
            If MusicAllList Is Nothing Then
                MusicAllList = New List(Of String)
                DirectoryUtils.Create(Paths.Base & "PCL\Musics\")
                For Each File In DirectoryUtils.EnumerateFiles(Paths.Base & "PCL\Musics\", True)
                    '文件夹可能会被加入 .ini 文件夹配置文件、一些乱七八糟的 .jpg 文件啥的
                    If {"ini", "jpg", "txt", "cfg", "lrc", "db", "png"}.Contains(PathUtils.GetExtension(File)) Then Continue For
                    MusicAllList.Add(File)
                Next
            End If
            '打乱顺序播放
            MusicWaitingList = If(Settings.Get(Of Boolean)("UiMusicRandom"), New List(Of String)(MusicAllList).Shuffle().ToList, New List(Of String)(MusicAllList))
            If PreventFirst IsNot Nothing AndAlso MusicWaitingList.FirstOrDefault = PreventFirst Then
                '若需要避免成为第一项的为第一项，则将它放在最后
                MusicWaitingList.RemoveAt(0)
                MusicWaitingList.Add(PreventFirst)
            End If
        Catch ex As Exception
            Logger.Error(ex, "初始化音乐列表失败")
        End Try
    End Sub
    ''' <summary>
    ''' 获取下一首播放的音乐路径并将其从列表中移除。
    ''' 如果没有，可能会返回 Nothing。
    ''' </summary>
    Private Function DequeueNextMusicAddress() As String
        Dim Result As String
        '初始化，确保存在音乐
        If MusicAllList Is Nothing OrElse Not MusicAllList.Any() OrElse Not MusicWaitingList.Any() Then MusicListInit(False)
        '出列下一个音乐，如果出列结束则生成新列表
        If MusicWaitingList.Any() Then
            Result = MusicWaitingList(0)
            MusicWaitingList.RemoveAt(0)
        Else
            Result = Nothing
        End If
        If Not MusicWaitingList.Any() Then MusicListInit(False, Result)
        Return Result
    End Function

#End Region

#Region "UI 控制"

    ''' <summary>
    ''' 当前曲目的显示名。在线音乐的文件名是纯数字 ID，要换成歌名。
    ''' </summary>
    Private Function MusicCurrentDisplayName() As String
        If String.IsNullOrEmpty(MusicCurrent) Then Return ""
        Try
            '在线音乐放在网易云缓存目录里，文件名就是歌曲 ID
            Dim Id As Long = DstNetease.NeIdFromCachePath(MusicCurrent)
            If Id > 0 Then
                Dim Title As String = DstNetease.NeSongTitle(Id)
                If Not String.IsNullOrEmpty(Title) Then Return Title
            End If
        Catch ex As Exception
            Logger.Warn(ex, "获取在线音乐名称失败")
        End Try
        Return PathUtils.GetFileNameWithoutExtension(MusicCurrent)
    End Function

    ''' <summary>
    ''' 刷新背景音乐按钮 UI 与设置页 UI。
    ''' </summary>
    Private Sub MusicRefreshUI()
        RunInUi(
        Sub()
            Try

                '只要当前有东西在播（本地背景音乐或在线音乐）就显示按钮。
                '在线音乐存在网易云缓存目录里，不会进 MusicAllList，
                '所以不能只用列表是否为空来判断。
                Dim HasCurrent As Boolean = Not String.IsNullOrEmpty(MusicCurrent) AndAlso
                                            MusicNAudio IsNot Nothing AndAlso
                                            MusicState <> MusicStates.Stop
                If Not MusicAllList.Any() AndAlso Not HasCurrent Then
                    '无背景音乐
                    FrmMain.BtnExtraMusic.Show = False
                Else
                    '有背景音乐
                    FrmMain.BtnExtraMusic.Show = True
                    Dim ToolTipText As String
                    If MusicState = MusicStates.Pause Then
                        FrmMain.BtnExtraMusic.Logo = Logo.IconPlay
                        FrmMain.BtnExtraMusic.LogoScale = 0.8
                        ToolTipText = "已暂停：" & MusicCurrentDisplayName()
                        If MusicAllList.Count > 1 Then
                            ToolTipText += vbCrLf & "左键恢复播放，右键播放下一曲。"
                        Else
                            ToolTipText += vbCrLf & "左键恢复播放，右键重新从头播放。"
                        End If
                    Else
                        FrmMain.BtnExtraMusic.Logo = Logo.IconMusic
                        FrmMain.BtnExtraMusic.LogoScale = 1
                        ToolTipText = "正在播放：" & MusicCurrentDisplayName()
                        If MusicAllList.Count > 1 Then
                            ToolTipText += vbCrLf & "左键暂停，右键播放下一曲。"
                        Else
                            ToolTipText += vbCrLf & "左键暂停，右键重新从头播放。"
                        End If
                    End If
                    FrmMain.BtnExtraMusic.ToolTip = ToolTipText
                End If

            Catch ex As Exception
                Logger.Error(ex, "刷新背景音乐 UI 失败")
            End Try
        End Sub)
    End Sub

    '滚轮调音量：每格 5%，范围 0~100
    Public Const MusicVolumeStep As Integer = 50
    ''' <summary>
    ''' 用滚轮调整背景音乐音量。Delta 是 MouseWheelEventArgs.Delta（一格 120）。
    ''' 调完在按钮旁边浮一个只显示百分比的提示，1.2 秒后自动收起。
    ''' </summary>
    Public Sub MusicWheelVolume(Delta As Integer)
        Try
            '一格 = 5%，按 0~1000 换算
            Dim StepValue As Integer = If(Delta > 0, MusicVolumeStep, -MusicVolumeStep)
            '向上滚一格 120，快速滚动会给出更大的值，按比例换算
            StepValue = CInt(StepValue * Math.Max(1, Math.Abs(Delta) / 120))
            Dim Current As Integer = Settings.Get(Of Integer)("UiMusicVolume")
            Dim NewValue As Integer = (Current + StepValue).Clamp(0, 1000)
            If NewValue <> Current Then
                Settings.Set("UiMusicVolume", NewValue)
                '播放线程每 100ms 轮询这个值，这里不用直接碰 NAudio
                Logger.Info($"滚轮调整音量：{Current} → {NewValue}")
            End If
            ShowMusicVolumeTip(NewValue)
        Catch ex As Exception
            Logger.Warn(ex, "滚轮调整音量失败")
        End Try
    End Sub

    ''' <summary>在音乐按钮旁浮出音量百分比，稍后自动收起。</summary>
    Private Sub ShowMusicVolumeTip(Volume As Integer)
        RunInUi(
        Sub()
            Try
                If FrmMain?.BtnExtraMusic Is Nothing Then Return
                FrmMain.BtnExtraMusic.ShowWheelTip(Math.Round(Volume / 10) & "%")
                AniStop("Music Volume Tip")
                AniStart(AaCode(Sub()
                                    If FrmMain?.BtnExtraMusic IsNot Nothing Then FrmMain.BtnExtraMusic.HideWheelTip()
                                End Sub, 1200), "Music Volume Tip")
            Catch ex As Exception
                Logger.Warn(ex, "显示音量提示失败")
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 让音乐在暂停、播放间切换，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlPause()
        If MusicNAudio Is Nothing Then
            Hint("音乐播放尚未开始！", HintType.Red)
        Else
            Select Case MusicState
                Case MusicStates.Pause
                    MusicResume()
                Case MusicStates.Play
                    MusicPause()
                Case Else
                    Logger.Warn("音乐目前为停止状态，已强制尝试开始播放")
                    MusicRefreshPlay(False)
            End Select
        End If
    End Sub

    ''' <summary>
    ''' 播放下一曲，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlNext()
        If MusicAllList.IsSingle Then
            MusicStartPlay(MusicCurrent)
            Hint("重新播放：" & PathUtils.GetLastPart(MusicCurrent), HintType.Green)
        Else
            Dim Address As String = DequeueNextMusicAddress()
            If Address Is Nothing Then
                Hint("没有可以播放的音乐！", HintType.Red)
            Else
                MusicStartPlay(Address)
                Hint("正在播放：" & PathUtils.GetLastPart(Address), HintType.Green)
            End If
        End If
        MusicRefreshUI()
    End Sub

#End Region

#Region "主状态控制"

    ''' <summary>
    ''' 获取当前的音乐播放状态。
    ''' </summary>
    Public ReadOnly Property MusicState As MusicStates
        Get
            If MusicNAudio Is Nothing Then Return MusicStates.Stop
            Select Case MusicNAudio.PlaybackState
                Case 0 'PlaybackState.Stopped
                    Return MusicStates.Stop
                Case 2 'PlaybackState.Paused
                    Return MusicStates.Pause
                Case Else
                    Return MusicStates.Play
            End Select
        End Get
    End Property
    Public Enum MusicStates
        [Stop]
        Play
        Pause
    End Enum

    '重载与开始

    ''' <summary>
    ''' 重载播放列表并尝试开始播放背景音乐。
    ''' </summary>
    ''' <param name="ShowHint">是否显示刷新提示。</param>
    Public Sub MusicRefreshPlay(ShowHint As Boolean, Optional IsFirstLoad As Boolean = False)
        Try

            MusicListInit(True)
            If Not MusicAllList.Any() Then
                If MusicNAudio Is Nothing Then
                    If ShowHint Then Hint("未检测到可用的背景音乐！", HintType.Red)
                Else
                    MusicNAudio = Nothing
                    If ShowHint Then Hint("背景音乐已清除！", HintType.Green)
                End If
            Else
                Dim Address As String = DequeueNextMusicAddress()
                If Address Is Nothing Then
                    If ShowHint Then Hint("没有可以播放的音乐！", HintType.Red)
                Else
                    Try
                        MusicStartPlay(Address, IsFirstLoad)
                        If ShowHint Then Hint("背景音乐已刷新：" & PathUtils.GetLastPart(Address), HintType.Green, False)
                    Catch
                    End Try
                End If
            End If
            MusicRefreshUI()

        Catch ex As Exception
            Logger.Error(ex, "刷新背景音乐播放失败")
        End Try
    End Sub
    ''' <summary>
    ''' 开始播放音乐。
    ''' </summary>
    Private Sub MusicStartPlay(Address As String, Optional IsFirstLoad As Boolean = False)
        If Address Is Nothing Then Return
        Logger.Info($"播放开始：{Address}")
        MusicCurrent = Address
        RunInNewThread(Sub() MusicLoop(IsFirstLoad), "Music", ThreadPriority.BelowNormal)
    End Sub

    '播放与暂停

    ''' <summary>
    ''' 暂停音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicPause() As Boolean
        If MusicState = MusicStates.Play Then
            RunInThread(
            Sub()
                Logger.Info("已暂停播放")
                MusicNAudio?.Pause()
                MusicRefreshUI()
            End Sub)
            Return True
        Else
            Logger.Info($"无需暂停播放，当前状态为 {MusicState}")
            Return False
        End If
    End Function
    ''' <summary>
    ''' 继续音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicResume() As Boolean
        If MusicState = MusicStates.Play OrElse Not MusicAllList.Any() Then
            Logger.Info($"无需继续播放，当前状态为 {MusicState}")
            Return False
        Else
            RunInThread(
            Sub()
                Logger.Info("已恢复播放")
                Try
                    MusicNAudio?.Play()
                Catch 'https://github.com/Meloong-Git/PCL/pull/5415#issuecomment-2751135223
                    MusicNAudio?.Stop()
                    MusicNAudio?.Play()
                End Try
                MusicRefreshUI()
            End Sub)
            Return True
        End If
    End Function

#End Region

    ''' <summary>
    ''' 当前正在播放的 WaveOutEvent。
    ''' 如果为它赋了类别，则会没有必要地加载 NAudio.dll。
    ''' </summary>
    Public MusicNAudio = Nothing
    ''' <summary>
    ''' 当前播放的音乐地址。
    ''' </summary>
    Private MusicCurrent As String = ""

    ''' <summary>当前播放位置，供播放器页面显示进度。</summary>
    Public MusicPosition As TimeSpan = TimeSpan.Zero
    ''' <summary>当前音乐总长度，供播放器页面显示进度。</summary>
    Public MusicDuration As TimeSpan = TimeSpan.Zero

    ''' <summary>当前播放的音乐文件路径（没有则为空串）。</summary>
    Public ReadOnly Property MusicCurrentAddress As String
        Get
            Return MusicCurrent
        End Get
    End Property

    ''' <summary>当前音频流，供拖进度条跳转用（没有则为 Nothing）。</summary>
    Public MusicReader As WaveStream = Nothing

    ''' <summary>按比例跳转播放位置，Ratio 取 0~1。</summary>
    Public Sub MusicSeekRatio(Ratio As Double)
        Dim Reader As WaveStream = MusicReader
        If Reader Is Nothing Then Return
        Try
            Dim Total As TimeSpan = Reader.TotalTime
            If Total.TotalMilliseconds <= 0 Then Return
            Dim Clamped As Double = Math.Max(0, Math.Min(1, Ratio))
            Reader.CurrentTime = TimeSpan.FromMilliseconds(Total.TotalMilliseconds * Clamped)
            MusicPosition = Reader.CurrentTime
        Catch ex As Exception
            Logger.Warn(ex, "跳转播放位置失败")
        End Try
    End Sub
    ''' <summary>指定文件立即播放，供播放器页面点列表用。</summary>
    Public Sub MusicPlayFile(Address As String)
        If String.IsNullOrWhiteSpace(Address) Then Return
        If MusicAllList Is Nothing OrElse Not MusicAllList.Any() Then MusicListInit(True)
        If MusicWaitingList IsNot Nothing Then MusicWaitingList.Remove(Address)
        MusicStartPlay(Address)
        MusicRefreshUI()
    End Sub

    ''' <summary>
    ''' 在 MusicUuid 不变的前提下，持续播放某地址的音乐，且在播放结束后随机播放下一曲。
    ''' </summary>
    Private Sub MusicLoop(Optional IsFirstLoad As Boolean = False)
        Dim CurrentWave As WaveOutEvent = Nothing
        Dim Reader As WaveStream = Nothing
        Try
            '开始播放
            CurrentWave = New WaveOutEvent()
            MusicNAudio = CurrentWave
            CurrentWave.DeviceNumber = -1
            Try
                Reader = New AudioFileReader(MusicCurrent)
            Catch ex As Exception
                Logger.Warn(ex, "使用 AudioFileReader 加载音频文件失败，换用 MediaFoundationReader 重试")
                Reader = New MediaFoundationReader(MusicCurrent)
            End Try
            MusicReader = Reader
            CurrentWave.Init(Reader)
            CurrentWave.Play()
            '第一次打开的暂停
            If IsFirstLoad AndAlso Not Settings.Get(Of Boolean)("UiMusicAuto") Then CurrentWave.Pause()
            MusicRefreshUI()
            '停止条件：播放完毕或变化
            Dim PreviousVolume = 0
            While CurrentWave.Equals(MusicNAudio) AndAlso Not CurrentWave.PlaybackState = PlaybackState.Stopped
                If Settings.Get(Of Integer)("UiMusicVolume") <> PreviousVolume Then
                    '更新音量
                    PreviousVolume = Settings.Get(Of Integer)("UiMusicVolume")
#Disable Warning BC40008 '类型或成员已过时
                    CurrentWave.Volume = PreviousVolume / 1000
#Enable Warning BC40008 '类型或成员已过时
                End If
                '更新进度条
                MusicPosition = Reader.CurrentTime
                MusicDuration = Reader.TotalTime
                Dim Percent = If(Reader.TotalTime.TotalMilliseconds <= 0, 0,
                                 Reader.CurrentTime.TotalMilliseconds / Reader.TotalTime.TotalMilliseconds)
                RunInUi(Sub() FrmMain.BtnExtraMusic.Progress = Percent)
                Thread.Sleep(100)
            End While
            '当前音乐已播放结束，继续下一曲
            If CurrentWave.PlaybackState = PlaybackState.Stopped AndAlso MusicAllList.Any Then MusicStartPlay(DequeueNextMusicAddress)
        Catch ex As Exception
            Logger.Warn(ex, $"播放音乐出现内部错误（{MusicCurrent}）")
            If TypeOf ex Is NAudio.MmException AndAlso ex.Message.Contains("AlreadyAllocated") Then
                Hint("你的音频设备正被其他程序占用。请在关闭占用的程序后重启 PCL，才能恢复音乐播放功能！", HintType.Red)
                Thread.Sleep(1000000000)
            End If
            If TypeOf ex Is NAudio.MmException AndAlso (ex.Message.Contains("NoDriver") OrElse ex.Message.Contains("BadDeviceId")) Then
                Hint("由于音频设备变更，音乐播放功能在重启 PCL 后才能恢复！", HintType.Red)
                Thread.Sleep(1000000000)
            End If
            If Not ({"wav", "mp3", "flac"}.Contains(PathUtils.GetExtension(MusicCurrent))) OrElse
                ex.Message.Contains("0xC00D36C4") Then '#5096：不支持给定的 URL 的字节流类型。 (异常来自 HRESULT:0xC00D36C4)
                Hint("播放音乐失败（" & PathUtils.GetLastPart(MusicCurrent) & "）：PCL 可能不支持此音乐格式，请将格式转换为 .wav、.mp3 或 .flac 后再试", HintType.Red)
            Else
                Logger.Error(ex, $"播放音乐失败（{PathUtils.GetLastPart(MusicCurrent)}）", LogBehavior.Toast)
            End If
            '将播放错误的音乐从列表中移除
            MusicAllList.Remove(MusicCurrent)
            MusicWaitingList.Remove(MusicCurrent)
            MusicRefreshUI()
            '等待 2 秒后继续播放
            Thread.Sleep(2000)
            If TypeOf ex Is FileNotFoundException Then
                MusicRefreshPlay(True, IsFirstLoad)
            Else
                MusicStartPlay(DequeueNextMusicAddress(), IsFirstLoad)
            End If
        Finally
            MusicReader = Nothing
            If CurrentWave IsNot Nothing Then CurrentWave.Dispose()
            If Reader IsNot Nothing Then Reader.Dispose()
            MusicRefreshUI()
        End Try
    End Sub

End Module
