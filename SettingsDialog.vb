Imports System
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Hộp thoại "Cài đặt" - số luồng/tệp, số tệp tải song song, thư mục tải mặc định,
''' và bật/tắt nhận link từ extension trình duyệt (kèm cổng kết nối).
''' </summary>
Public Class SettingsDialog
    Inherits Form

    Private nudSegments As NumericUpDown
    Private nudConcurrent As NumericUpDown
    Private txtDefaultFolder As TextBox
    Private WithEvents btnBrowseFolder As Button
    Private chkBrowserBridge As CheckBox
    Private nudBridgePort As NumericUpDown
    Private WithEvents btnOk As Button
    Private WithEvents btnCancel As Button
    Private folderDialog As FolderBrowserDialog

    Public Property Segments As Integer
    Public Property Concurrent As Integer
    Public Property DefaultDownloadFolder As String
    Public Property BrowserBridgeEnabled As Boolean
    Public Property BrowserBridgePort As Integer

    Public Sub New(segments As Integer, concurrent As Integer, defaultFolder As String, bridgeEnabled As Boolean, bridgePort As Integer)
        InitializeComponent()
        nudSegments.Value = segments
        nudConcurrent.Value = concurrent
        txtDefaultFolder.Text = defaultFolder
        chkBrowserBridge.Checked = bridgeEnabled
        nudBridgePort.Value = bridgePort
    End Sub

    Private Sub btnBrowseFolder_Click(sender As Object, e As EventArgs) Handles btnBrowseFolder.Click
        If folderDialog.ShowDialog() = DialogResult.OK Then
            txtDefaultFolder.Text = folderDialog.SelectedPath
        End If
    End Sub

    Private Sub btnOk_Click(sender As Object, e As EventArgs) Handles btnOk.Click
        Segments = CInt(nudSegments.Value)
        Concurrent = CInt(nudConcurrent.Value)
        DefaultDownloadFolder = If(String.IsNullOrWhiteSpace(txtDefaultFolder.Text),
                                    Path.Combine(Directory.GetCurrentDirectory(), "Download"),
                                    txtDefaultFolder.Text)
        BrowserBridgeEnabled = chkBrowserBridge.Checked
        BrowserBridgePort = CInt(nudBridgePort.Value)

        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub btnCancel_Click(sender As Object, e As EventArgs) Handles btnCancel.Click
        Me.DialogResult = DialogResult.Cancel
        Me.Close()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Cài đặt"
        Me.ClientSize = New Size(430, 300)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Font = New Font("Segoe UI", 9.0F)

        folderDialog = New FolderBrowserDialog()

        Dim lblSegments As New Label With {.Text = "Số luồng/tệp:", .Location = New Point(12, 18), .AutoSize = True}
        nudSegments = New NumericUpDown With {.Location = New Point(190, 15), .Width = 70, .Minimum = 1, .Maximum = 16}

        Dim lblConcurrent As New Label With {.Text = "Số tệp tải song song:", .Location = New Point(12, 50), .AutoSize = True}
        nudConcurrent = New NumericUpDown With {.Location = New Point(190, 47), .Width = 70, .Minimum = 1, .Maximum = 10}

        Dim lblFolder As New Label With {.Text = "Thư mục tải mặc định:", .Location = New Point(12, 84), .AutoSize = True}
        txtDefaultFolder = New TextBox With {.Location = New Point(12, 104), .Width = 310}
        btnBrowseFolder = New Button With {.Text = "Chọn...", .Location = New Point(328, 102), .Width = 90}

        Dim sep As New Label With {.Location = New Point(12, 140), .Size = New Size(406, 2), .BorderStyle = BorderStyle.Fixed3D}

        chkBrowserBridge = New CheckBox With {.Text = "Nhận link từ trình duyệt (extension)", .Location = New Point(12, 154), .AutoSize = True}
        Dim lblPort As New Label With {.Text = "Cổng:", .Location = New Point(30, 182), .AutoSize = True}
        nudBridgePort = New NumericUpDown With {.Location = New Point(80, 179), .Width = 90, .Minimum = 1024, .Maximum = 65535}
        Dim lblPortHint As New Label With {.Text = "(phải khớp với cổng cấu hình trong popup của extension trên trình duyệt)",
                                            .Location = New Point(12, 208), .AutoSize = True, .ForeColor = Color.Gray,
                                            .MaximumSize = New Size(406, 0)}

        btnOk = New Button With {.Text = "Lưu", .Location = New Point(238, 250), .Width = 90, .Height = 30}
        btnCancel = New Button With {.Text = "Huỷ", .Location = New Point(332, 250), .Width = 90, .Height = 30}

        Me.Controls.AddRange(New Control() {lblSegments, nudSegments, lblConcurrent, nudConcurrent,
                               lblFolder, txtDefaultFolder, btnBrowseFolder, sep,
                               chkBrowserBridge, lblPort, nudBridgePort, lblPortHint, btnOk, btnCancel})
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel
    End Sub

End Class
