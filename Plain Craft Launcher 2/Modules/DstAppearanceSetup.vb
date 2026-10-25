''' <summary>
''' 首次启动（含从老版本升级上来）时的外观选择提示。
''' <para>
''' 为什么需要它：老版本升级上来时，配置文件里没有 DstAppearance 这一项，
''' 会直接套用默认值，用户既不知道有第二套外观，也不知道能改。
''' 所以在"第一次拿到这个版本"时问一次，并在文案里说明之后可以去个性化里切换。
''' </para>
''' </summary>
Public Module DstAppearanceSetup

    ''' <summary>
    ''' 外观选择是否已经问过。
    ''' <para>
    ''' 用一次性标记，不跟版本号绑定：外观是「选一次就长期有效」的偏好，
    ''' 若拿版本号做标记，每发一个新版本都会再把所有人问一遍。
    ''' </para>
    ''' <para>
    ''' 老版本只写过 DstAppearanceAskedVersion，那里有值就说明问过，直接迁移过来。
    ''' </para>
    ''' </summary>
    Private ReadOnly Property HasAsked As Boolean
        Get
            If Settings.Get(Of Boolean)("DstAppearanceAsked") Then Return True
            Return Settings.Get(Of String)("DstAppearanceAskedVersion") <> ""
        End Get
    End Property

    ''' <summary>
    ''' 没问过就问一次。必须在 UI 线程调用。
    ''' </summary>
    Public Sub ShowIfNeeded()
        If HasAsked Then Return
        Try
            Dim Text As String =
                "选择一套界面外观：" & vbCrLf & vbCrLf &
                "· PCL：PCL 原版外观，顶部横排导航，跟随主题色与深浅设置。" & vbCrLf &
                "· PCL Inder：左侧竖排导航，深色简约配色。" & vbCrLf & vbCrLf &
                "之后随时可以在「设置 → 个性化 → 外观」里切换。"
            '按钮 1 = PCL Inder，按钮 2 = PCL；默认推荐 PCL（与原版一致）
            Dim Choice As Integer = MyMsgBox(Text, "选择界面外观", "PCL Inder", "PCL")
            Dim Value As Integer = If(Choice = 1, 1, 0)
            Settings.Set("DstAppearance", Value)
            Settings.Set("DstAppearanceAsked", True)
            '保留版本记录，方便排查「用户是在哪个版本上选的」
            Settings.Set("DstAppearanceAskedVersion", VersionBaseName)
            ConfigUtils.SaveAll()
            '外观要重启才能完整生效，这里先按选择应用一次骨架，避免下次启动前看着不对
            If FrmMain IsNot Nothing Then
                ThemeRefresh(Force:=True)
                FrmMain.ApplyAppearance()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "显示外观选择提示失败")
        End Try
    End Sub

End Module
