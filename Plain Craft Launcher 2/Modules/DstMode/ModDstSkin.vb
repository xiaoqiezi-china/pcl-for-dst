Imports System.IO

''' <summary>
''' DST-Mode 的皮肤上传（见 docs/dstmode-skin-api.md）。
''' <para>
''' 皮肤以 QQ 号为键、与玩家档案解耦：重复上传是覆盖，解绑不删皮肤。
''' 上传成功后服务端在玩家进服时通过 hasJoined 下发 textures，
''' 所以**已经在游戏里时上传不会立刻变**，需要重新进服。
''' </para>
''' <para>
''' 走原始字节那条路（Content-Type: application/octet-stream）：
''' JSON + base64 的请求体受服务端全局 1 MB 解析器限制，base64 还会膨胀 4/3，
''' 实际只够 700 KB 以内的图，没必要给自己留这个坑。
''' </para>
''' </summary>
Public Class ModDstSkin

    ''' <summary>一次操作的结果；失败时 Message 是给玩家看的文案。</summary>
    Public Class SkinResult
        Public Ok As Boolean = False
        Public Message As String = ""
        ''' <summary>服务端识别出的模型：classic / slim。失败时为空。</summary>
        Public Model As String = ""
    End Class

    ''' <summary>
    ''' 上传一张皮肤。需要已登录（有可用 accessToken）。
    ''' <para>必须在工作线程调用：里面会发网络请求。</para>
    ''' </summary>
    Public Shared Function Upload(FilePath As String) As SkinResult
        Dim Cred As ModDstMode.DstModeCredential = ModDstMode.Current
        If Cred Is Nothing OrElse Not Cred.IsUsable Then Return Fail("请先在启动页完成 QQ 登录")
        If Not DstModeCore.IsValidBase(Cred.AuthBase) Then Return Fail("登录服务地址无效，请重新登录")

        '本地先过一遍尺寸与体积：服务端也会拒，但没必要把几 MB 传上去再被打回
        Dim LocalError As String = ValidateFile(FilePath)
        If LocalError <> "" Then Return Fail(LocalError)

        Dim Bytes As Byte()
        Try
            Bytes = FileUtils.ReadAsBytes(FilePath)
        Catch ex As Exception
            Logger.Warn(ex, "读取皮肤文件失败")
            Return Fail("读取皮肤文件失败，请换一张试试")
        End Try

        Dim Body As String
        Try
            Logger.Info($"正在上传皮肤：{PathUtils.GetLastPart(FilePath)}（{Bytes.Length} 字节）")
            Body = NetRequestByClient(DstModeCore.SkinUploadUrl(Cred.AuthBase), HttpMethod.Post,
                Content:=Bytes, ContentType:="application/octet-stream",
                Headers:={{"Authorization", "Bearer " & Cred.AccessToken}},
                Timeout:=45000, RequireJson:=True, MakeLog:=False)
        Catch ex As Exception
            Logger.Warn(ex, "上传皮肤失败")
            Return Fail(ErrorText(ex))
        End Try

        '成功响应是 {"ok":true,"qq":"…","name":"…","model":"classic"}
        Dim Model As String = DstModeCore.JsonString(Body, "model")
        Logger.Info($"皮肤上传成功，服务端识别为 {If(Model, "未知")} 模型")
        Return New SkinResult With {
            .Ok = True,
            .Model = Model,
            .Message = If(Model = "slim", "已使用爱丽克丝（细手臂）模型", "已使用史蒂夫（经典）模型")
        }
    End Function

    ''' <summary>
    ''' 删除自己上传的皮肤，回到服务端默认（没有上传时可能是 QQ 头像生成的那张）。
    ''' <para>必须在工作线程调用。</para>
    ''' </summary>
    Public Shared Function ResetSkin() As SkinResult
        Dim Cred As ModDstMode.DstModeCredential = ModDstMode.Current
        If Cred Is Nothing OrElse Not Cred.IsUsable Then Return Fail("请先在启动页完成 QQ 登录")
        If Not DstModeCore.IsValidBase(Cred.AuthBase) Then Return Fail("登录服务地址无效，请重新登录")

        Try
            Logger.Info("正在恢复默认皮肤")
            NetRequestByClient(DstModeCore.SkinMeUrl(Cred.AuthBase), HttpMethod.Delete,
                Headers:={{"Authorization", "Bearer " & Cred.AccessToken}},
                Timeout:=30000, RequireJson:=True, MakeLog:=False)
        Catch ex As Exception
            Logger.Warn(ex, "恢复默认皮肤失败")
            Return Fail(ErrorText(ex))
        End Try
        Return New SkinResult With {.Ok = True, .Message = "已恢复默认皮肤"}
    End Function

    ''' <summary>
    ''' 本地预校验：尺寸必须正好 64×64，体积不超过 2 MB。
    ''' 返回空串表示通过，否则是给玩家看的失败原因。
    ''' </summary>
    Public Shared Function ValidateFile(FilePath As String) As String
        If String.IsNullOrEmpty(FilePath) OrElse Not FileUtils.Exists(FilePath) Then Return "找不到这个文件"
        Try
            If FileUtils.GetInfo(FilePath).Length > DstModeCore.SkinMaxBytes Then
                Return $"皮肤文件不能超过 2 MB（这张 {StringUtils.FormatByteSize(FileUtils.GetInfo(FilePath).Length)}）"
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取皮肤文件信息失败")
            Return "读取皮肤文件失败，请换一张试试"
        End Try
        Try
            'MyBitmap 不实现 IDisposable（它会在字段里长期持有位图），这里读完尺寸就够
            Dim Image As New MyBitmap(FilePath)
            Dim Width As Integer = Image.Pic.Width, Height As Integer = Image.Pic.Height
            If Not DstModeCore.IsValidSkinSize(Width, Height) Then
                '不做缩放：64×64 是像素画，缩放会直接毁掉它
                Return $"皮肤必须是 {DstModeCore.SkinSize}×{DstModeCore.SkinSize} 像素（这张是 {DstModeCore.SkinSizeText(Width, Height)}）"
            End If
        Catch ex As Exception
            Logger.Info($"皮肤图片无法解码：{ex.Message}")
            Return "这不是一张能识别的图片"
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' 把 HTTP 异常翻译成给玩家看的一句话；具体规则在 DstModeCore.FriendlySkinError 里，
    ''' 那边是纯逻辑，可以用 vbc 单独编译测试。
    ''' </summary>
    Private Shared Function ErrorText(ex As Exception) As String
        Dim HttpEx As HttpRequestCodeException = TryCast(ex, HttpRequestCodeException)
        If HttpEx Is Nothing Then Return "上传失败，请稍后再试"
        Return DstModeCore.FriendlySkinError(CInt(HttpEx.StatusCode), If(HttpEx.Response, ""))
    End Function

    Private Shared Function Fail(Message As String) As SkinResult
        Return New SkinResult With {.Ok = False, .Message = Message}
    End Function

End Class
