''' <summary>
''' 启动页左侧的「QQ」登录形态：显示绑定的 QQ 头像与昵称。
''' 头像走本地压缩缓存，不再每次直连腾讯。
''' </summary>
Public Class PageLoginQq

    Public Sub Reload(Optional KeepInput As Boolean = True)
        If Not DstBot.IsQqBound Then
            ShowEmpty()
            Return
        End If
        '先用已有信息占位，再向服务器要一次最新资料
        ShowInfo()
        DstBot.FetchUserInfo(AddressOf ShowInfo)
    End Sub

    Private Sub ShowEmpty()
        LabQqName.Text = "未绑定 QQ"
        LabQqHint.Text = "点击下方「绑定 QQ」开始"
        PanQqAvatar.Background = New SolidColorBrush(New MyColor(42, 54, 70))
    End Sub

    Private Sub ShowInfo()
        If Not DstBot.IsQqBound Then
            ShowEmpty()
            Return
        End If
        Dim Qq As String = If(DstBot.UserInfoQq <> "", DstBot.UserInfoQq, DstBot.QqNumber)
        LabQqName.Text = If(DstBot.UserInfoName <> "", DstBot.UserInfoName, If(Qq = "", "QQ", "QQ " & Qq))
        LabQqHint.Text = If(Qq = "", "已绑定", "QQ " & Qq)

        ApplyAvatar(Qq)
    End Sub

    ''' <summary>头像：命中本地缓存立即显示，否则后台抓取后回填。</summary>
    Private Sub ApplyAvatar(Qq As String)
        If Qq = "" Then Return
        Dim FilePath As String = DstBot.AvatarCacheFile(Qq)
        If FilePath <> "" AndAlso File.Exists(FilePath) Then
            Dim Cached As Windows.Media.Imaging.BitmapImage = LoadLocal(FilePath)
            If Cached IsNot Nothing Then
                PanQqAvatar.Background = New ImageBrush(Cached) With {.Stretch = Stretch.UniformToFill}
                Return
            End If
        End If
        RunInThread(
        Sub()
            Try
                Dim Saved As String = DstBot.EnsureAvatarFile(Qq)
                If Saved = "" Then Return
                Dim Bmp As Windows.Media.Imaging.BitmapImage = LoadLocal(Saved)
                If Bmp Is Nothing Then Return
                RunInUi(
                Sub()
                    PanQqAvatar.Background = New ImageBrush(Bmp) With {.Stretch = Stretch.UniformToFill}
                End Sub)
            Catch ex As Exception
                Logger.Info(ex, "加载 QQ 头像失败")
            End Try
        End Sub)
    End Sub

    Private Function LoadLocal(FilePath As String) As Windows.Media.Imaging.BitmapImage
        Try
            Dim Bmp As New Windows.Media.Imaging.BitmapImage()
            Bmp.BeginInit()
            Bmp.UriSource = New Uri(FilePath, UriKind.Absolute)
            Bmp.CacheOption = Windows.Media.Imaging.BitmapCacheOption.OnLoad
            Bmp.DecodePixelWidth = 176
            Bmp.EndInit()
            Bmp.Freeze()
            Return Bmp
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

End Class