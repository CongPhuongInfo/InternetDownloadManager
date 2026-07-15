Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Hộp thoại "Thêm URL" kiểu IDM - dán 1 hoặc nhiều liên kết, chọn thư mục lưu, tải ngay.
''' Đây là cách chính để đưa link vào hàng đợi mà KHÔNG cần tạo tệp danh sách .txt trước.
''' </summary>
Public Class AddUrlDialog
    Inherits Form

    Private txtUrls As TextBox
    Private txtFolder As TextBox
    Private WithEvents btnBrowse As Button
    Private WithEvents btnOk As Button
    Private WithEvents btnCancel As Button
    Private folderDialog As FolderBrowserDialog

    Public ReadOnly Property Urls As List(Of String)
        Get
            Dim result As New List(Of String)
            If txtUrls Is Nothing Then Return result
            Dim lines As String() = txtUrls.Text.Split(New String() {vbCrLf, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
            For Each line As String In lines
                Dim trimmed As String = line.Trim()
                If trimmed.Length > 0 Then result.Add(trimmed)
            Next
            Return result
        End Get
    End Property

    Public ReadOnly Property DestinationFolder As String
        Get
            Return txtFolder.Text
        End Get
    End Property

    Public Sub New(defaultFolder As String)
        InitializeComponent()
        txtFolder.Text = defaultFolder
    End Sub

    Private Sub btnBrowse_Click(sender As Object, e As EventArgs) Handles btnBrowse.Click
        If folderDialog.ShowDialog() = DialogResult.OK Then
            txtFolder.Text = folderDialog.SelectedPath
        End If
    End Sub

    Private Sub btnOk_Click(sender As Object, e As EventArgs) Handles btnOk.Click
        If Urls.Count = 0 Then
            MessageBox.Show("Vui lòng dán ít nhất 1 URL.")
            Return
        End If
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub btnCancel_Click(sender As Object, e As EventArgs) Handles btnCancel.Click
        Me.DialogResult = DialogResult.Cancel
        Me.Close()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Thêm URL để tải"
        Me.ClientSize = New Size(520, 300)
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MinimumSize = New Size(420, 260)
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Font = New Font("Segoe UI", 9.0F)

        folderDialog = New FolderBrowserDialog()

        Dim lblUrls As New Label With {.Text = "Dán URL cần tải (mỗi dòng 1 link):", .Location = New Point(12, 10), .AutoSize = True}
        txtUrls = New TextBox With {.Location = New Point(12, 30), .Size = New Size(496, 170),
                                     .Multiline = True, .ScrollBars = ScrollBars.Vertical,
                                     .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom}

        Dim lblFolder As New Label With {.Text = "Lưu vào thư mục:", .Location = New Point(12, 210), .AutoSize = True,
                                          .Anchor = AnchorStyles.Left Or AnchorStyles.Bottom}
        txtFolder = New TextBox With {.Location = New Point(12, 230), .Size = New Size(400, 23),
                                       .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom}
        btnBrowse = New Button With {.Text = "Chọn...", .Location = New Point(418, 228), .Size = New Size(90, 25),
                                      .Anchor = AnchorStyles.Right Or AnchorStyles.Bottom}

        btnOk = New Button With {.Text = "Tải xuống", .Location = New Point(326, 264), .Size = New Size(90, 28),
                                  .Anchor = AnchorStyles.Right Or AnchorStyles.Bottom}
        btnCancel = New Button With {.Text = "Huỷ", .Location = New Point(418, 264), .Size = New Size(90, 28),
                                      .Anchor = AnchorStyles.Right Or AnchorStyles.Bottom}

        Me.Controls.AddRange(New Control() {lblUrls, txtUrls, lblFolder, txtFolder, btnBrowse, btnOk, btnCancel})
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel
    End Sub

End Class
