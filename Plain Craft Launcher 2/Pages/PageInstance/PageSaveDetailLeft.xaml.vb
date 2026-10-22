''' <summary>
''' 存档详情的左侧导航。和版本设置一样，左边选分类，右边看内容。
''' </summary>
Public Class PageSaveDetailLeft
    Implements IRefreshable

    ''' <summary>当前选中的分类：0 世界设置、1 数据包、2 区域文件、3 玩家数据、4 地图管理。</summary>
    Public PageID As Integer = 0

    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemLevel.Check, ItemPack.Check, ItemRegion.Check, ItemPlayer.Check, ItemMap.Check
        '尚未初始化控件属性时 sender.Tag 为 Nothing
        If sender.Tag Is Nothing Then Return
        PageChange(Val(sender.Tag))
    End Sub

    ''' <summary>切换到某个分类。</summary>
    Public Sub PageChange(Id As Integer)
        PageID = Id
        If FrmInstanceSaveDetail IsNot Nothing Then FrmInstanceSaveDetail.SetSection(Id)
    End Sub

    ''' <summary>回到第一个分类（每次重新进入详情页时调用）。</summary>
    Public Sub ResetSection()
        PageID = 0
        If ItemLevel IsNot Nothing Then
            ItemLevel.Checked = True
            ItemPack.Checked = False
            ItemRegion.Checked = False
            ItemPlayer.Checked = False
            ItemMap.Checked = False
        End If
        If FrmInstanceSaveDetail IsNot Nothing Then FrmInstanceSaveDetail.SetSection(0)
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        If FrmInstanceSaveDetail IsNot Nothing Then FrmInstanceSaveDetail.RefreshContent()
    End Sub

End Class
