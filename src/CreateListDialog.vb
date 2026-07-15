Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Hộp thoại "Tạo danh sách liên kết từ thư mục" - quét 1 thư mục theo mẫu tệp, sinh URL,
''' lưu ra tệp .txt trong thư mục data\ để dùng với "Tải theo danh sách có sẵn".
''' </summary>
Public Class CreateListDialog
    Inherits Form

    Private txtSourceFolder As TextBox
    Private WithEvents btnBrowseSource As Button
    Private txtPattern As TextBox
    Private txtBaseUrl As TextBox
    Private txtListName As TextBox
    Private WithEvents btnGenerate As Button
    Private WithEvents btnClose As Button
    Private folderDialog As FolderBrowserDialog

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub btnBrowseSource_Click(sender As Object, e As EventArgs) Handles btnBrowseSource.Click
        If folderDialog.ShowDialog() = DialogResult.OK Then
            txtSourceFolder.Text = folderDialog.SelectedPath
        End If
    End Sub

    Private Sub btnGenerate_Click(sender As Object, e As EventArgs) Handles btnGenerate.Click
        If String.IsNullOrWhiteSpace(txtSourceFolder.Text) Then
            MessageBox.Show("Vui lòng chọn thư mục nguồn.")
            Return
        End If
        If Not Directory.Exists(txtSourceFolder.Text) Then
            MessageBox.Show("Thư mục nguồn không tồn tại.")
            Return
        End If

        Dim pattern As String = txtPattern.Text
        If String.IsNullOrWhiteSpace(pattern) Then pattern = "*.*"
        If Not pattern.StartsWith("*.") Then
            MessageBox.Show("Mẫu tệp phải bắt đầu bằng *. (vd: *.png, *.*)")
            Return
        End If

        Try
            Dim urls As List(Of String) = FileListBuilder.BuildUrlList(txtSourceFolder.Text, pattern, txtBaseUrl.Text)
            If urls.Count = 0 Then
                MessageBox.Show("Không tìm thấy tệp nào khớp với mẫu đã chọn.")
                Return
            End If

            Dim dataFolder As String = Path.Combine(Directory.GetCurrentDirectory(), "data")
            Dim savedPath As String = FileListBuilder.SaveList(urls, dataFolder, txtListName.Text)

            MessageBox.Show("Đã tạo danh sách gồm " & urls.Count & " tệp." & vbNewLine & "Lưu tại: " & savedPath)
        Catch ex As Exception
            MessageBox.Show("Lỗi khi tạo danh sách: " & ex.Message)
        End Try
    End Sub

    Private Sub btnClose_Click(sender As Object, e As EventArgs) Handles btnClose.Click
        Me.Close()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Tạo danh sách liên kết từ thư mục"
        Me.ClientSize = New Size(566, 200)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Font = New Font("Segoe UI", 9.0F)

        folderDialog = New FolderBrowserDialog()

        Dim lblSrc As New Label With {.Text = "Thư mục nguồn:", .Location = New Point(12, 15), .AutoSize = True}
        txtSourceFolder = New TextBox With {.Location = New Point(130, 12), .Width = 320}
        btnBrowseSource = New Button With {.Text = "Chọn...", .Location = New Point(456, 10), .Width = 90}

        Dim lblPattern As New Label With {.Text = "Mẫu tệp:", .Location = New Point(12, 48), .AutoSize = True}
        txtPattern = New TextBox With {.Location = New Point(130, 45), .Width = 100, .Text = "*.*"}

        Dim lblBaseUrl As New Label With {.Text = "URL gốc (tuỳ chọn):", .Location = New Point(12, 81), .AutoSize = True}
        txtBaseUrl = New TextBox With {.Location = New Point(130, 78), .Width = 416}

        Dim lblListName As New Label With {.Text = "Tên tệp danh sách:", .Location = New Point(12, 114), .AutoSize = True}
        txtListName = New TextBox With {.Location = New Point(130, 111), .Width = 300}

        btnGenerate = New Button With {.Text = "Tạo danh sách", .Location = New Point(130, 150), .Width = 150, .Height = 30}
        btnClose = New Button With {.Text = "Đóng", .Location = New Point(456, 150), .Width = 90, .Height = 30}

        Me.Controls.AddRange(New Control() {lblSrc, txtSourceFolder, btnBrowseSource, lblPattern, txtPattern,
                               lblBaseUrl, txtBaseUrl, lblListName, txtListName, btnGenerate, btnClose})
        Me.CancelButton = btnClose
    End Sub

End Class
