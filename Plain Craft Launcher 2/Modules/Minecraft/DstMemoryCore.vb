Imports System.Diagnostics
Imports System.Linq
Imports System.Runtime.InteropServices

''' <summary>
''' 一个进程的内存占用信息。
''' </summary>
Friend Class DstProcessMemory
    ''' <summary>进程 ID。</summary>
    Public Property Pid As Integer
    ''' <summary>进程名（不含 .exe）。</summary>
    Public Property Name As String = ""
    ''' <summary>工作集大小，即当前占用的物理内存。</summary>
    Public Property WorkingSetBytes As Long
    ''' <summary>提交大小（含换页到页面文件的私有内存）。</summary>
    Public Property PrivateBytes As Long
    ''' <summary>是否禁止结束。</summary>
    Public Property IsProtected As Boolean
    ''' <summary>禁止结束或需要提醒的原因；空串表示可以正常结束。</summary>
    Public Property Reason As String = ""
End Class

''' <summary>物理内存快照。</summary>
Friend Class DstMemorySnapshot
    Public Property TotalBytes As Long
    Public Property AvailableBytes As Long
    ''' <summary>已用百分比 0~100。</summary>
    Public Property LoadPercent As Integer
    Public ReadOnly Property UsedBytes As Long
        Get
            Return Math.Max(0, TotalBytes - AvailableBytes)
        End Get
    End Property
End Class

''' <summary>工作集回收的结果。</summary>
Friend Class DstTrimResult
    ''' <summary>回收前后可用物理内存的增量（可能为负：回收时别的程序也在吃内存）。</summary>
    Public Property FreedBytes As Long
    ''' <summary>成功回收的进程数。</summary>
    Public Property TrimmedCount As Integer
    ''' <summary>打不开、跳过的进程数（多半是无权限的系统进程）。</summary>
    Public Property SkippedCount As Integer
    Public Property ElapsedMs As Long
End Class

''' <summary>
''' 内存相关的底层能力：读取物理内存用量、枚举各进程占用、回收工作集。
''' <para>
''' 这一层**刻意不依赖 Logger、Settings 与任何界面**，只碰 BCL 与 Win32 API。
''' 好处是它可以脱离启动器单独编译运行，回收与枚举这类有真实副作用的操作
''' 才能在改动时被真正验证，而不是「编译过了就算完」。
''' </para>
''' <para>
''' 所谓「内存优化」就是把各进程的工作集页标记为可回收（EmptyWorkingSet）：
''' 物理内存占用会立刻下降，但那些页并没有消失，只是被换到了页面文件里，
''' 再次访问时要读回磁盘。所以它对「内存实在不够、想撑过这一下」有用，
''' 并不是真的让内存变多了，在机械硬盘上还可能卡顿。
''' </para>
''' </summary>
Friend Module DstMemoryCore

#Region "Win32"

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MEMORYSTATUSEX
        Public dwLength As UInteger
        Public dwMemoryLoad As UInteger
        Public ullTotalPhys As ULong
        Public ullAvailPhys As ULong
        Public ullTotalPageFile As ULong
        Public ullAvailPageFile As ULong
        Public ullTotalVirtual As ULong
        Public ullAvailVirtual As ULong
        Public ullAvailExtendedVirtual As ULong
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function GlobalMemoryStatusEx(ByRef lpBuffer As MEMORYSTATUSEX) As Boolean
    End Function

    <DllImport("psapi.dll", SetLastError:=True)>
    Private Function EmptyWorkingSet(hProcess As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function OpenProcess(dwDesiredAccess As UInteger, bInheritHandle As Boolean, dwProcessId As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function CloseHandle(hObject As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function ProcessIdToSessionId(dwProcessId As UInteger, ByRef pSessionId As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Function GetCurrentProcess() As IntPtr
    End Function

    Private Const PROCESS_SET_QUOTA As UInteger = &H100
    Private Const PROCESS_QUERY_INFORMATION As UInteger = &H400

#End Region

#Region "进程保护"

    ''' <summary>
    ''' 结束就会让系统出问题的进程名（小写，不含 .exe）。
    ''' <para>
    ''' 这些不是「最好不要动」，是「动了会蓝屏或直接注销」：
    ''' csrss / wininit / winlogon / lsass 被杀会触发蓝屏或强制注销，
    ''' smss / services 是会话与服务的根，svchost 承载大量系统服务。
    ''' </para>
    ''' </summary>
    Private ReadOnly CriticalNames As String() = {
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "fontdrvhost", "msmpeng", "nissrv", "securityhealthservice"
    }

    ''' <summary>
    ''' 判断某个进程能不能结束；能结束返回空串，否则返回原因。
    ''' <para>
    ''' 顺序有讲究：先挡自己（否则点一下启动器就没了），再挡系统核心，
    ''' 最后挡会话 0 的服务——服务进程一个都杀不得，而用户自己的程序
    ''' 都在登录会话里，按会话过滤既安全又能把列表收窄到「应用」。
    ''' </para>
    ''' </summary>
    Public Function GetProtectionReason(P As Process, Optional SelfPid As Integer = 0) As String
        If P Is Nothing Then Return "进程不存在"
        If SelfPid > 0 AndAlso P.Id = SelfPid Then Return "这是启动器自己"
        If P.Id <= 4 Then Return "系统核心进程"
        Dim Name As String = ""
        Try
            Name = P.ProcessName.ToLowerInvariant()
        Catch
            Return "无法读取进程信息"
        End Try
        If CriticalNames.Contains(Name) Then Return "结束它会导致系统不稳定"
        Dim Session As UInteger = 0
        If Not ProcessIdToSessionId(CUInt(P.Id), Session) Then Return "无法判断进程会话"
        If Session = 0 Then Return "系统服务进程"
        Return ""
    End Function

#End Region

#Region "读取"

    ''' <summary>取当前物理内存用量。失败时返回 Nothing。</summary>
    Public Function GetSnapshot() As DstMemorySnapshot
        Dim Data As New MEMORYSTATUSEX With {.dwLength = CUInt(Marshal.SizeOf(GetType(MEMORYSTATUSEX)))}
        If Not GlobalMemoryStatusEx(Data) Then Return Nothing
        Return New DstMemorySnapshot With {
            .TotalBytes = CLng(Data.ullTotalPhys),
            .AvailableBytes = CLng(Data.ullAvailPhys),
            .LoadPercent = CInt(Data.dwMemoryLoad)
        }
    End Function

    ''' <summary>
    ''' 取当前用户会话里占用工作集最多的若干进程。
    ''' <para>
    ''' 只看登录会话：会话 0 是服务，既不该被用户随手结束，列出来也只是噪声。
    ''' 读不到工作集的进程（受保护进程）直接跳过，不报错。
    ''' </para>
    ''' </summary>
    ''' <param name="MaxCount">最多返回多少条。</param>
    ''' <param name="MinBytes">工作集低于这个值的略过，避免列表被小进程淹没。</param>
    Public Function GetTopProcesses(Optional MaxCount As Integer = 12, Optional MinBytes As Long = 64L * 1024 * 1024) As List(Of DstProcessMemory)
        Dim Result As New List(Of DstProcessMemory)
        Dim SelfPid As Integer = Process.GetCurrentProcess().Id
        Dim SelfSession As UInteger = 0
        ProcessIdToSessionId(CUInt(SelfPid), SelfSession)

        Dim All As Process() = Nothing
        Try
            All = Process.GetProcesses()
        Catch
            Return Result
        End Try

        For Each P In All
            Try
                If P.Id <= 4 Then Continue For
                Dim Session As UInteger = 0
                If Not ProcessIdToSessionId(CUInt(P.Id), Session) Then Continue For
                If Session <> SelfSession Then Continue For
                Dim WorkingSet As Long = P.WorkingSet64
                If WorkingSet < MinBytes Then Continue For
                Dim Item As New DstProcessMemory With {
                    .Pid = P.Id,
                    .Name = p.ProcessName,
                    .WorkingSetBytes = WorkingSet,
                    .PrivateBytes = TryPrivateBytes(p),
                    .Reason = GetProtectionReason(p, SelfPid)
                }
                Item.IsProtected = (Item.Reason <> "")
                '已经带着原因的那些（系统服务等）不再重复判断
                Result.Add(Item)
            Catch
                '受保护进程读不到信息，跳过即可
            End Try
        Next

        Return Result.OrderByDescending(Function(X) X.WorkingSetBytes).Take(MaxCount).ToList()
    End Function

    Private Function TryPrivateBytes(P As Process) As Long
        Try
            Return P.PrivateMemorySize64
        Catch
            Return 0
        End Try
    End Function

#End Region

#Region "回收"

    ''' <summary>
    ''' 回收各进程的工作集。
    ''' <para>
    ''' 自己在内的所有进程都尝试一遍：能打开就回收，打不开（权限不足的
    ''' 系统进程）计入跳过数。没管理员权限时只能回收自己和同用户的部分进程，
    ''' 效果会明显打折，这一点在界面上要如实说明。
    ''' </para>
    ''' </summary>
    Public Function TrimWorkingSets() As DstTrimResult
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Dim Result As New DstTrimResult

        '先回收自己：这一条永远成功，也让「至少做了点什么」成立
        Try
            EmptyWorkingSet(GetCurrentProcess())
            Result.TrimmedCount += 1
        Catch
        End Try

        Dim Before As DstMemorySnapshot = GetSnapshot()

        Dim SelfPid As Integer = Process.GetCurrentProcess().Id
        For Each P In Process.GetProcesses()
            Try
                If P.Id = SelfPid OrElse P.Id <= 4 Then Continue For
                '结束会崩系统的那些不去碰，虽然回收工作集不等于结束进程，
                '但对这些进程动句柄没意义，也容易被安全软件拦
                If GetProtectionReason(P, SelfPid).Contains("系统") Then
                    Result.SkippedCount += 1
                    Continue For
                End If
                Dim Handle As IntPtr = OpenProcess(PROCESS_SET_QUOTA Or PROCESS_QUERY_INFORMATION, False, CUInt(P.Id))
                If Handle = IntPtr.Zero Then
                    Result.SkippedCount += 1
                    Continue For
                End If
                Try
                    If EmptyWorkingSet(Handle) Then
                        Result.TrimmedCount += 1
                    Else
                        Result.SkippedCount += 1
                    End If
                Finally
                    CloseHandle(Handle)
                End Try
            Catch
                Result.SkippedCount += 1
            End Try
        Next

        Dim After As DstMemorySnapshot = GetSnapshot()
        If Before IsNot Nothing AndAlso After IsNot Nothing Then
            Result.FreedBytes = After.AvailableBytes - Before.AvailableBytes
        End If

        Watch.Stop()
        Result.ElapsedMs = Watch.ElapsedMilliseconds
        Return Result
    End Function

#End Region

#Region "结束进程"

    ''' <summary>
    ''' 结束一个进程。成功返回空串，失败返回原因。
    ''' <para>
    ''' 这里再判一次保护规则：界面上的按钮可能因为列表刷新不及时还指着旧进程，
    ''' 或者进程名在两次读取之间被复用，只在界面上拦是不够的。
    ''' </para>
    ''' </summary>
    Public Function KillProcess(Pid As Integer) As String
        Dim P As Process = Nothing
        Try
            P = Process.GetProcessById(Pid)
        Catch
            Return "进程已经不存在了"
        End Try
        Dim Reason As String = GetProtectionReason(P, Process.GetCurrentProcess().Id)
        If Reason <> "" Then Return Reason
        Try
            P.Kill()
            Return ""
        Catch ex As InvalidOperationException
            Return "进程已经退出"
        Catch ex As System.ComponentModel.Win32Exception
            Return "权限不足（" & ex.NativeErrorCode.ToString() & "）"
        Catch ex As Exception
            Return ex.Message
        End Try
    End Function

#End Region

End Module
