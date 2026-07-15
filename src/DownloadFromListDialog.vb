Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Hộp thoại "Tải theo danh sách có sẵn" - chọn 1 tệp .txt (danh sách URL đã tạo trước), chọn
''' thư mục lưu, trả về danh sách DownloadItem để Form1 đưa thẳng vào hàng đợi tải chung.
''' </summary>
Public Class DownloadFromListDialog
    Inherits Form

    Private cboFileList As ComboBox
    Private WithEvents btnRefreshList As Button
    Private WithEvents btnOpenLocation As Button
    Private txtSubFolder As TextBox
    Private txtDownloadRoot As TextBox
    Private WithEvents btnBrowseRoot As Button
    Private WithEvents btnOk As Button
    Private WithEvents btnCancel As Button
    Private folderDialog As FolderBrowserDialog

    Private _items As List(Of DownloadItem)

    Public ReadOnly Property Items As List(Of DownloadItem)
        Get
            Return _items
        End Get
    End Property

    Public Sub New(defaultDownloadRoot As String)
        InitializeComponent()
        txtDownloadRoot.Text = defaultDownloadRoot
        RefreshFileListCombo()
    End Sub

    Private Sub RefreshFileListCombo()
        Dim dataFolder As String = Path.Combine(Directory.GetCurrentDirectory(), "data")
        cboFileList.Items.Clear()
        If Directory.Exists(dataFolder) Then
            Dim files As String() = Directory.GetFiles(dataFolder, "*.txt", SearchOption.AllDirectories)
            cboFileList.Items.AddRange(files)
        End If
    End Sub

    Private Sub btnRefreshList_Click(sender As Object, e As EventArgs) Handles btnRefreshList.Click
        RefreshFileListCombo()
    End Sub

    Private Sub btnOpenLocation_Click(sender As Object, e As EventArgs) Handles btnOpenLocation.Click
        Try
            Dim path As String = cboFileList.Text
            If Not String.IsNullOrWhiteSpace(path) AndAlso File.Exists(path) Then
                Process.Start("explorer.exe", "/select," & path)
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub btnBrowseRoot_Click(sender As Object, e As EventArgs) Handles btnBrowseRoot.Click
        If folderDialog.ShowDialog() = DialogResult.OK Then
            txtDownloadRoot.Text = folderDialog.SelectedPath
        End If
    End Sub

    Private Sub btnOk_Click(sender As Object, e As EventArgs) Handles btnOk.Click
        If String.IsNullOrWhiteSpace(cboFileList.Text) OrElse Not File.Exists(cboFileList.Text) Then
            MessageBox.Show("Vui lòng chọn một danh sách tệp hợp lệ.")
            Return
        End If

        Dim lines As String()
        Try
            lines = File.ReadAllLines(cboFileList.Text)
        Catch ex As Exception
            MessageBox.Show("Không đọc được danh sách: " & ex.Message)
            Return
        End Try

        Dim downloadRoot As String = If(String.IsNullOrWhiteSpace(txtDownloadRoot.Text),
                                         Path.Combine(Directory.GetCurrentDirectory(), "Download"),
                                         txtDownloadRoot.Text)
        Dim finalFolder As String = downloadRoot
        If Not String.IsNullOrWhiteSpace(txtSubFolder.Text) Then
            finalFolder = Path.Combine(downloadRoot, txtSubFolder.Text)
        End If

        Dim newItems As New List(Of DownloadItem)
        For Each line As String In lines
            If String.IsNullOrWhiteSpace(line) Then Continue For
            Try
                Dim data As New FileDownloadData(line.Trim())
                Dim it As New DownloadItem()
                it.Data = data
                it.LocalPath = data.GetLocalPath(finalFolder)
                newItems.Add(it)
            Catch ex As Exception
                ' Dong khong phai URL hop le - bo qua
            End Try
        Next

        If newItems.Count = 0 Then
            MessageBox.Show("Danh sách không có URL hợp lệ nào.")
            Return
        End If

        _items = newItems
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub btnCancel_Click(sender As Object, e As EventArgs) Handles btnCancel.Click
        Me.DialogResult = DialogResult.Cancel
        Me.Close()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Tải theo danh sách có sẵn"
        Me.ClientSize = New Size(566, 210)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Font = New Font("Segoe UI", 9.0F)

        folderDialog = New FolderBrowserDialog()

        Dim lblList As New Label With {.Text = "Danh sách:", .Location = New Point(12, 15), .AutoSize = True}
        cboFileList = New ComboBox With {.Location = New Point(130, 12), .Width = 300, .DropDownStyle = ComboBoxStyle.DropDown}
        btnRefreshList = New Button With {.Text = "Làm mới", .Location = New Point(436, 10), .Width = 55}
        btnOpenLocation = New Button With {.Text = "Mở vị trí", .Location = New Point(495, 10), .Width = 60}

        Dim lblSubFolder As New Label With {.Text = "Thư mục con (tuỳ chọn):", .Location = New Point(12, 50), .AutoSize = True}
        txtSubFolder = New TextBox With {.Location = New Point(170, 47), .Width = 386}

        Dim lblRoot As New Label With {.Text = "Thư mục tải về:", .Location = New Point(12, 85), .AutoSize = True}
        txtDownloadRoot = New TextBox With {.Location = New Point(130, 82), .Width = 336}
        btnBrowseRoot = New Button With {.Text = "Chọn...", .Location = New Point(472, 80), .Width = 84}

        Dim lblHint As New Label With {.Text = "Các tệp sẽ được tải và lưu theo đúng cấu trúc thư mục ghi trong URL.",
                                        .Location = New Point(12, 118), .AutoSize = True, .ForeColor = Color.Gray}

        btnOk = New Button With {.Text = "Tải xuống", .Location = New Point(372, 158), .Width = 90, .Height = 30}
        btnCancel = New Button With {.Text = "Huỷ", .Location = New Point(466, 158), .Width = 90, .Height = 30}

        Me.Controls.AddRange(New Control() {lblList, cboFileList, btnRefreshList, btnOpenLocation,
                               lblSubFolder, txtSubFolder, lblRoot, txtDownloadRoot, btnBrowseRoot,
                               lblHint, btnOk, btnCancel})
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel
    End Sub

End Class
