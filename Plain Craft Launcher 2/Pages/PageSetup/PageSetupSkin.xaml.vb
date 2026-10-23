Imports System.IO

''' <summary>
''' 皮肤包管理页：列出已安装皮肤、切换、导入、导出、删除、打开目录。
''' </summary>
Public Class PageSetupSkin
    Implements IRefreshable

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        RefreshList()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshList()
    End Sub

    ''' <summary>重建皮肤列表。</summary>
    Private Sub RefreshList()
        If PanList Is Nothing Then Return
        PanList.Children.Clear()
        Dim Packs = SkinManager.ListPacks(True)
        Dim CurrentId As String = SkinManager.CurrentId

        '第一项永远是“不使用皮肤包”，方便随时回到内置外观
        PanList.Children.Add(BuildRow(Nothing, CurrentId = ""))
        For Each Pack In Packs
            PanList.Children.Add(BuildRow(Pack, String.Equals(CurrentId, Pack.Id, StringComparison.OrdinalIgnoreCase)))
        Next
        If Packs.Count = 0 Then
            Dim Tip As New TextBlock With {
                .Text = "还没有安装任何皮肤包。点上面的「导入皮肤」载入一个 zip，或用「新建模板」自己做一个。",
                .TextWrapping = TextWrapping.Wrap,
                .FontSize = 12,
                .Opacity = 0.6,
                .Margin = New Thickness(0, 4, 0, 4)
            }
            PanList.Children.Add(Tip)
        End If
    End Sub

    ''' <summary>
    ''' 构造一行皮肤条目。Pack 为 Nothing 时表示“不使用皮肤包”。
    ''' 用 PCL 自己的 MyListItem，保持与本项目其他列表一致的观感与交互。
    ''' </summary>
    Private Function BuildRow(Pack As SkinPack, IsCurrent As Boolean) As FrameworkElement
        Dim Title As String = If(Pack Is Nothing, "不使用皮肤包（内置外观）", Pack.Name)
        Dim Desc As String
        If Pack Is Nothing Then
            Desc = "完全使用 PCL 主题色生成的外观。"
        Else
            Dim Bits As New List(Of String)
            If Pack.Author <> "" Then Bits.Add("作者：" & Pack.Author)
            If Pack.Version <> "" Then Bits.Add("v" & Pack.Version)
            Bits.Add($"覆盖 {Pack.Colors.Count} 个颜色")
            If Not Pack.AppliesNow Then Bits.Add("（当前深浅模式下不生效）")
            Desc = String.Join(" · ", Bits)
            If Pack.Description <> "" Then Desc &= vbCrLf & Pack.Description
        End If

        Dim Item As New MyListItem With {
            .Title = Title,
            .Info = Desc,
            .Type = MyListItem.CheckType.RadioBox,
            .Checked = IsCurrent,
            .Height = 48,
            .MinPaddingRight = 90
        }

        '操作按钮
        Dim Buttons As New List(Of MyIconButton)
        If Pack IsNot Nothing Then
            Dim BtnExport As New MyIconButton With {
                .Logo = Logo.IconButtonSave, .LogoScale = 0.8, .ToolTip = "导出为 zip",
                .Tag = Pack
            }
            AddHandler BtnExport.Click, Sub() ExportPack(CType(BtnExport.Tag, SkinPack))
            Buttons.Add(BtnExport)
            If Not Pack.IsBuiltIn Then
                Dim BtnDelete As New MyIconButton With {
                    .Logo = Logo.IconButtonCross, .LogoScale = 0.62, .ToolTip = "删除这个皮肤",
                    .Tag = Pack
                }
                AddHandler BtnDelete.Click, Sub() DeletePack(CType(BtnDelete.Tag, SkinPack))
                Buttons.Add(BtnDelete)
            End If
        End If
        Item.Buttons = Buttons

        '整行点击 = 切换到这个皮肤
        AddHandler Item.Click,
            Sub()
                SkinManager.SwitchTo(If(Pack Is Nothing, "", Pack.Id))
                RefreshList()
                Hint(If(Pack Is Nothing, "已恢复内置外观", "已应用皮肤：" & Pack.Name), HintType.Green, False)
            End Sub
        Return Item
    End Function

    Private Sub BtnImport_Click(sender As Object, e As EventArgs) Handles BtnImport.Click
        Dim Picked = Dialogs.SelectFile("选择皮肤包", False, filter:="皮肤包|zip;json").FirstOrDefault()
        If String.IsNullOrWhiteSpace(Picked) Then Return
        Dim Installed = SkinManager.Import(Picked)
        If Installed Is Nothing Then
            Hint("导入失败：" & If(SkinManager.ErrorText, "未知错误"), HintType.Red)
            Return
        End If
        RefreshList()
        Hint("已导入皮肤：" & Installed.Name, HintType.Green)
    End Sub

    Private Sub BtnCreateTemplate_Click(sender As Object, e As EventArgs) Handles BtnCreateTemplate.Click
        If Not SkinManager.CreateTemplate() Then
            Hint("创建模板失败：" & If(SkinManager.ErrorText, "未知错误"), HintType.Red)
            Return
        End If
        RefreshList()
        Hint("已生成模板，可在皮肤目录里修改", HintType.Green)
    End Sub

    Private Sub BtnOpenFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenFolder.Click
        SkinManager.OpenFolder()
    End Sub

    ''' <summary>导出皮肤为 zip。</summary>
    Private Sub ExportPack(Pack As SkinPack)
        If Pack Is Nothing Then Return
        Dim FileName As String = Pack.Name
        For Each Bad As Char In IO.Path.GetInvalidFileNameChars()
            FileName = FileName.Replace(Bad, "_"c)
        Next
        Dim Target = Dialogs.SaveFile("导出皮肤", FileName & ".zip", filter:={("zip", "皮肤包")})
        If String.IsNullOrWhiteSpace(Target) Then Return
        If SkinManager.Export(Pack, Target) Then
            Hint("已导出到 " & Target, HintType.Green)
        Else
            Hint("导出失败：" & If(SkinManager.ErrorText, "未知错误"), HintType.Red)
        End If
    End Sub

    ''' <summary>删除皮肤包（内置的不可删）。</summary>
    Private Sub DeletePack(Pack As SkinPack)
        If Pack Is Nothing Then Return
        If MyMsgBox($"确定要删除皮肤「{Pack.Name}」吗？" & vbCrLf & "这会同时删掉它所在的文件夹。",
                    "删除皮肤", "删除", "取消", IsWarn:=True) <> 1 Then Return
        If SkinManager.Remove(Pack) Then
            '如果删掉的是当前皮肤，界面要立刻回到内置外观
            SkinManager.SwitchTo(SkinManager.CurrentId)
            RefreshList()
            Hint("已删除皮肤", HintType.Green)
        Else
            Hint("删除失败：" & If(SkinManager.ErrorText, "未知错误"), HintType.Red)
        End If
    End Sub

End Class
