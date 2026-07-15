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
    Private txtToken As TextBox
    Private WithEvents btnCopyToken As Button
    Private WithEvents btnRegenToken As Button
    Private WithEvents btnOk As Button
    Private WithEvents btnCancel As Button
    Private folderDialog As FolderBrowserDialog

    Public Property Segments As Integer
    Public Property Concurrent As Integer
    Public Property DefaultDownloadFolder As String
    Public Property BrowserBridgeEnabled As Boolean
    Public Property BrowserBridgePort As Integer
    Public Property BrowserBridgeToken As String

    Public Sub New(segments As Integer, concurrent As Integer, defaultFolder As String, bridgeEnabled As Boolean, bridgePort As Integer, bridgeToken As String)
        InitializeComponent()
        nudSegments.Value = segments
        nudConcurrent.Value = concurrent
        txtDefaultFolder.Text = defaultFolder
        chkBrowserBridge.Checked = bridgeEnabled
        nudBridgePort.Value = bridgePort
        txtToken.Text = bridgeToken
    End Sub

    Private Sub btnBrowseFolder_Click(sender As Object, e As EventArgs) Handles btnBrowseFolder.Click
        If folderDialog.ShowDialog() = DialogResult.OK Then
            txtDefaultFolder.Text = folderDialog.SelectedPath
        End If
    End Sub

    Private Sub btnCopyToken_Click(sender As Object, e As EventArgs) Handles btnCopyToken.Click
        Try
            Clipboard.SetText(txtToken.Text)
            MessageBox.Show("Đã sao chép mã kết nối. Dán vào ô ""Mã kết nối"" trong popup của extension trên trình duyệt.")
        Catch ex As Exception
        End Try
    End Sub

    Private Sub btnRegenToken_Click(sender As Object, e As EventArgs) Handles btnRegenToken.Click
        Dim confirm As DialogResult = MessageBox.Show(
            "Tạo mã kết nối mới sẽ khiến extension hiện tại (nếu đã dán mã cũ) mất kết nối cho tới khi bạn dán mã mới vào popup. Tiếp tục?",
            "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If confirm = DialogResult.Yes Then
            txtToken.Text = Guid.NewGuid().ToString("N")
        End If
    End Sub

    Private Sub btnOk_Click(sender As Object, e As EventArgs) Handles btnOk.Click
        Segments = CInt(nudSegments.Value)
        Concurrent = CInt(nudConcurrent.Value)
        DefaultDownloadFolder = If(String.IsNullOrWhiteSpace(txtDefaultFolder.Text),
                                    BrowserDownloadPromptDialog.GetSystemDownloadsFolder(),
                                    txtDefaultFolder.Text)
        BrowserBridgeEnabled = chkBrowserBridge.Checked
        BrowserBridgePort = CInt(nudBridgePort.Value)
        BrowserBridgeToken = txtToken.Text

        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub btnCancel_Click(sender As Object, e As EventArgs) Handles btnCancel.Click
        Me.DialogResult = DialogResult.Cancel
        Me.Close()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Cài đặt"
        Me.ClientSize = New Size(430, 380)
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

        Dim lblToken As New Label With {.Text = "Mã kết nối (dán vào popup của extension):", .Location = New Point(12, 234), .AutoSize = True}
        txtToken = New TextBox With {.Location = New Point(12, 254), .Width = 230, .ReadOnly = True, .BackColor = Color.WhiteSmoke}
        btnCopyToken = New Button With {.Text = "Sao chép", .Location = New Point(248, 252), .Width = 80}
        btnRegenToken = New Button With {.Text = "Tạo mã mới", .Location = New Point(332, 252), .Width = 88}
        Dim lblTokenHint As New Label With {.Text = "Extension nào cũng có thể gọi được máy chủ này qua HTTP nội bộ - mã kết nối" &
                                             " để chỉ extension đã được bạn cấp mã mới thêm được link vào hàng đợi.",
                                             .Location = New Point(12, 280), .AutoSize = True, .ForeColor = Color.Gray,
                                             .MaximumSize = New Size(406, 0)}

        btnOk = New Button With {.Text = "Lưu", .Location = New Point(238, 330), .Width = 90, .Height = 30}
        btnCancel = New Button With {.Text = "Huỷ", .Location = New Point(332, 330), .Width = 90, .Height = 30}

        Me.Controls.AddRange(New Control() {lblSegments, nudSegments, lblConcurrent, nudConcurrent,
                               lblFolder, txtDefaultFolder, btnBrowseFolder, sep,
                               chkBrowserBridge, lblPort, nudBridgePort, lblPortHint,
                               lblToken, txtToken, btnCopyToken, btnRegenToken, lblTokenHint,
                               btnOk, btnCancel})
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel
    End Sub

End Class
