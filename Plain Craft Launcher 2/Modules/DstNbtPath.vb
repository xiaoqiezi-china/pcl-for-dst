''' <summary>
''' NBT 节点的路径步进工具。
''' 所有 NbtPathStep 列表都是「从根标签开始」的完整路径，方便同一份路径在
''' 播放器、物品栏面板、通用编辑器之间来回传递。
''' </summary>
Public Module DstNbtPath

    Public Function Root() As List(Of NbtPathStep)
        Return New List(Of NbtPathStep)
    End Function

    ''' <summary>新建一条路径：Start 为 Nothing 时从根开始。</summary>
    Public Function OfSteps(ParamArray Steps As NbtPathStep()) As List(Of NbtPathStep)
        Dim Result As New List(Of NbtPathStep)
        If Steps IsNot Nothing Then Result.AddRange(Steps)
        Return Result
    End Function

    ''' <summary>在已有路径后面接一个键名。</summary>
    Public Function Extend(Base As List(Of NbtPathStep), KeyName As String) As List(Of NbtPathStep)
        Dim Result As New List(Of NbtPathStep)
        If Base IsNot Nothing Then Result.AddRange(Base)
        Result.Add(NbtPathStep.ByKey(KeyName))
        Return Result
    End Function

    ''' <summary>在已有路径后面接一个列表下标。</summary>
    Public Function ExtendIndex(Base As List(Of NbtPathStep), Index As Integer) As List(Of NbtPathStep)
        Dim Result As New List(Of NbtPathStep)
        If Base IsNot Nothing Then Result.AddRange(Base)
        Result.Add(NbtPathStep.ByIndex(Index))
        Return Result
    End Function

    ''' <summary>把路径拼成 Item.tag.display.Name.Display 这样的可读文本。</summary>
    Public Function Describe(Steps As List(Of NbtPathStep)) As String
        If Steps Is Nothing OrElse Steps.Count = 0 Then Return "根标签"
        Dim Parts As New List(Of String)
        For Each PathStep In Steps
            If PathStep.IsIndex Then
                Parts.Add("[" & PathStep.Index & "]")
            Else
                Parts.Add(PathStep.Key)
            End If
        Next
        Return String.Join(".", Parts)
    End Function

    ''' <summary>路径末端的名字，用于列表里显示。</summary>
    Public Function LastName(Steps As List(Of NbtPathStep)) As String
        If Steps Is Nothing OrElse Steps.Count = 0 Then Return "根标签"
        Dim Last As NbtPathStep = Steps(Steps.Count - 1)
        Return If(Last.IsIndex, "[" & Last.Index & "]", Last.Key)
    End Function

    ''' <summary>复制一份路径。</summary>
    Public Function Copy(Steps As List(Of NbtPathStep)) As List(Of NbtPathStep)
        Dim Result As New List(Of NbtPathStep)
        If Steps IsNot Nothing Then Result.AddRange(Steps)
        Return Result
    End Function

    ''' <summary>判断两条路径是否相同。</summary>
    Public Function Same(A As List(Of NbtPathStep), B As List(Of NbtPathStep)) As Boolean
        If A Is Nothing AndAlso B Is Nothing Then Return True
        If A Is Nothing OrElse B Is Nothing Then Return False
        If A.Count <> B.Count Then Return False
        For i = 0 To A.Count - 1
            If A(i).IsIndex <> B(i).IsIndex Then Return False
            If A(i).IsIndex Then
                If A(i).Index <> B(i).Index Then Return False
            ElseIf A(i).Key <> B(i).Key Then
                Return False
            End If
        Next
        Return True
    End Function

    ''' <summary>判断 B 是否以 A 为前缀（A 为空时恒为 True）。</summary>
    Public Function HasPrefix(Target As List(Of NbtPathStep), Prefix As List(Of NbtPathStep)) As Boolean
        If Prefix Is Nothing OrElse Prefix.Count = 0 Then Return True
        If Target Is Nothing OrElse Target.Count < Prefix.Count Then Return False
        For i = 0 To Prefix.Count - 1
            If Target(i).IsIndex <> Prefix(i).IsIndex Then Return False
            If Target(i).IsIndex Then
                If Target(i).Index <> Prefix(i).Index Then Return False
            ElseIf Target(i).Key <> Prefix(i).Key Then
                Return False
            End If
        Next
        Return True
    End Function

    ''' <summary>去掉最后一节，用于「返回上一级」。</summary>
    Public Function ParentOf(Steps As List(Of NbtPathStep)) As List(Of NbtPathStep)
        Dim Result As New List(Of NbtPathStep)
        If Steps Is Nothing OrElse Steps.Count = 0 Then Return Result
        Result.AddRange(Steps.GetRange(0, Steps.Count - 1))
        Return Result
    End Function

End Module
