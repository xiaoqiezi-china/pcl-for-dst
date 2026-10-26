''' <summary>
''' 联机模块（EasyTier）启动失败的识别。
''' <para>
''' EasyTier 启动时要建一块 Wintun 虚拟网卡，并把自己写进防火墙白名单，
''' 这两步都要求管理员权限。权限不够时它只往日志里丢一行英文加一个错误码
''' （0x80070005 = 拒绝访问），用户看不出该怎么办。
''' </para>
''' <para>
''' 这里把「是不是权限不够」的判断和对应说明单独抽出来：
''' 判断部分是纯字符串匹配，不依赖 WPF，可以脱离启动器直接测。
''' </para>
''' </summary>
Public Module ModLinkDiagnostics

    ''' <summary>
    ''' 日志里是否出现了「权限不足，没建成网卡 / 没写进防火墙白名单」。
    ''' </summary>
    Public Function IsPermissionDenied(Detail As String) As Boolean
        If String.IsNullOrEmpty(Detail) Then Return False
        'EasyTier 写防火墙白名单失败时带上这一串
        If Has(Detail, "firewall allowlist") Then Return True
        '建虚拟网卡失败：不同版本措辞不完全一致，两种都认
        If Has(Detail, "Failed to create adapter") Then Return True
        If Has(Detail, "tun device error") Then Return True
        'Windows 的错误码：拒绝访问
        If Has(Detail, "0x80070005") Then Return True
        Return False
    End Function

    ''' <summary>
    ''' 给用户的说明。原因放前面，处理办法放后面。
    ''' </summary>
    Public Function PermissionAdvice() As String
        Return $"联机模块启动失败：权限不足。" & vbCrLf & vbCrLf &
               $"EasyTier 需要创建虚拟网卡，并把自己加入防火墙白名单，这两步都要求管理员权限。" & vbCrLf & vbCrLf &
               $"请关闭启动器，右键选择「以管理员身份运行」后重试。" & vbCrLf &
               $"如果已经以管理员身份运行仍然失败，多半是安全软件拦截，可以临时关闭后重试。"
    End Function

    ''' <summary>
    ''' 忽略大小写的子串判断。
    ''' 不直接用扩展方法，是为了让本模块只依赖 BCL，能单独编译做测试。
    ''' </summary>
    Private Function Has(Text As String, Part As String) As Boolean
        Return Text.IndexOf(Part, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

End Module
