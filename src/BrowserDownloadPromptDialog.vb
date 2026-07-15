Imports System
Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms

''' <summary>Kết quả người dùng chọn ở hộp thoại xác nhận tải xuống.</summary>
Public Enum DownloadPromptResult
    DownloadNow
    DownloadLater
    CancelPrompt
End Enum

''' <summary>
''' Hộp thoại xác nhận khi bắt được 1 link từ trình duyệt (chuột phải vào link, hoặc bấm nút tải
''' nổi) - giống hộp thoại "Download" quen thuộc của IDM: hiện tên tệp, dò dung lượng (chạy nền,
''' không chặn giao diện), nơi lưu (mặc định là đúng thư mục Downloads thật của Windows chứ không
''' phải thư mục con riêng của app), và 3 lựa chọn Tải sau / Tải xuống ngay / Huỷ.
''' CHỈ hiện cho link bắt THỦ CÔNG (Source="manual") - link tự động bắt hoặc quét hàng loạt vẫn
''' lặng lẽ thêm thẳng vào hàng đợi như trước, không làm phiền người dùng từng cái một.
''' </summary>
Public Class BrowserDownloadPromptDialog
    Inherits Form

    <DllImport("shell32.dll")>
    Private Shared Function SHGetKnownFolderPath(ByRef rfid As Guid, dwFlags As UInteger, hToken As IntPtr, ByRef pszPath As IntPtr) As Integer
    End Function

    Private ReadOnly _url As String
    Private ReadOnly _referer As String
    Private ReadOnly _fileName As String

    Private lblFileName As Label
    Private lblSize As Label
    Private txtSaveTo As TextBox
    Private WithEvents btnBrowse As Button
    Private WithEvents btnLater As Button
    Private WithEvents btnNow As Button
    Private WithEvents btnCancelPrompt As Button

    Private _chosenResult As DownloadPromptResult = DownloadPromptResult.CancelPrompt
    Private _saveFullPath As String

    Public ReadOnly Property ChosenResult As DownloadPromptResult
        Get
            Return _chosenResult
        End Get
    End Property

    Public ReadOnly Property SaveFullPath As String
        Get
            Return _saveFullPath
        End Get
    End Property

    Public Sub New(url As String, suggestedFileName As String, referer As String)
        _url = url
        _referer = referer
        _fileName = If(String.IsNullOrWhiteSpace(suggestedFileName), GuessFileNameFromUrl(url), suggestedFileName)

        InitializeComponent()
        lblFileName.Text = _fileName
        txtSaveTo.Text = Path.Combine(GetSystemDownloadsFolder(), MakeSafeFileName(_fileName))

        Dim probeThread As New Thread(AddressOf ProbeSizeThread)
        probeThread.IsBackground = True
        probeThread.Start()
    End Sub

    Private Shared Function GuessFileNameFromUrl(url As String) As String
        Try
            Dim u As New Uri(url)
            Dim name As String = Path.GetFileName(u.LocalPath)
            Return If(String.IsNullOrWhiteSpace(name), "tep-tai-xuong", name)
        Catch ex As Exception
            Return "tep-tai-xuong"
        End Try
    End Function

    Private Shared Function MakeSafeFileName(name As String) As String
        Dim result As String = name
        For Each c As Char In Path.GetInvalidFileNameChars()
            result = result.Replace(c, "_"c)
        Next
        Return result
    End Function

    ''' <summary>Lấy đúng thư mục Downloads thật của Windows (không phải Environment.SpecialFolder -
    ''' enum đó KHÔNG có mục Downloads trên .NET Framework). Phương án dự phòng nếu API lỗi:
    ''' %USERPROFILE%\Downloads.</summary>
    Private Shared Function GetSystemDownloadsFolder() As String
        Try
            Dim downloadsFolderId As New Guid("374DE290-123F-4565-9164-39C4925E467B")
            Dim pathPtr As IntPtr = IntPtr.Zero
            Dim hr As Integer = SHGetKnownFolderPath(downloadsFolderId, 0, IntPtr.Zero, pathPtr)
            If hr = 0 AndAlso pathPtr <> IntPtr.Zero Then
                Dim result As String = Marshal.PtrToStringUni(pathPtr)
                Marshal.FreeCoTaskMem(pathPtr)
                If Not String.IsNullOrEmpty(result) Then Return result
            End If
        Catch ex As Exception
            ' Roi xuong phuong an du phong ben duoi
        End Try
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
    End Function

    Private Sub ProbeSizeThread()
        Dim size As Long = FileDownloader.ProbeRemoteSize(_url, _referer)
        Try
            If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                Me.Invoke(New Action(Of Long)(AddressOf ApplyProbedSize), size)
            End If
        Catch ex As Exception
            ' Form co the da dong truoc khi do xong - bo qua an toan
        End Try
    End Sub

    Private Sub ApplyProbedSize(size As Long)
        lblSize.Text = If(size > 0, Form1.FormatBytes(size), "Không xác định trước được dung lượng")
    End Sub

    Private Sub btnBrowse_Click(sender As Object, e As EventArgs) Handles btnBrowse.Click
        Using dlg As New SaveFileDialog()
            Dim currentDir As String = Path.GetDirectoryName(txtSaveTo.Text)
            Dim currentName As String = Path.GetFileName(txtSaveTo.Text)
            If Directory.Exists(currentDir) Then dlg.InitialDirectory = currentDir
            dlg.FileName = currentName
            dlg.Filter = "Tất cả tệp|*.*"
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtSaveTo.Text = dlg.FileName
            End If
        End Using
    End Sub

    Private Sub btnLater_Click(sender As Object, e As EventArgs) Handles btnLater.Click
        Finish(DownloadPromptResult.DownloadLater)
    End Sub

    Private Sub btnNow_Click(sender As Object, e As EventArgs) Handles btnNow.Click
        Finish(DownloadPromptResult.DownloadNow)
    End Sub

    Private Sub btnCancelPrompt_Click(sender As Object, e As EventArgs) Handles btnCancelPrompt.Click
        Finish(DownloadPromptResult.CancelPrompt)
    End Sub

    Private Sub Finish(result As DownloadPromptResult)
        _chosenResult = result
        _saveFullPath = txtSaveTo.Text
        Me.DialogResult = If(result = DownloadPromptResult.CancelPrompt, DialogResult.Cancel, DialogResult.OK)
        Me.Close()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Tải xuống tệp mới"
        Me.ClientSize = New Size(460, 210)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.TopMost = True
        Me.Font = New Font("Segoe UI", 9.0F)

        Dim lblFileNameCaption As New Label With {.Text = "Tên tệp:", .Location = New Point(12, 16), .AutoSize = True}
        lblFileName = New Label With {.Location = New Point(110, 16), .AutoSize = False, .Size = New Size(338, 20), .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}

        Dim lblSizeCaption As New Label With {.Text = "Kích thước:", .Location = New Point(12, 44), .AutoSize = True}
        lblSize = New Label With {.Text = "Đang dò dung lượng...", .Location = New Point(110, 44), .AutoSize = False, .Size = New Size(338, 20), .ForeColor = Color.DimGray}

        Dim lblSaveToCaption As New Label With {.Text = "Lưu vào:", .Location = New Point(12, 76), .AutoSize = True}
        txtSaveTo = New TextBox With {.Location = New Point(12, 96), .Width = 340}
        btnBrowse = New Button With {.Text = "Chọn khác...", .Location = New Point(358, 94), .Width = 90}

        Dim sep As New Label With {.Location = New Point(12, 132), .Size = New Size(436, 2), .BorderStyle = BorderStyle.Fixed3D}

        btnLater = New Button With {.Text = "Tải sau", .Location = New Point(12, 150), .Width = 130, .Height = 32}
        btnNow = New Button With {.Text = "Tải xuống ngay", .Location = New Point(154, 150), .Width = 150, .Height = 32, .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}
        btnCancelPrompt = New Button With {.Text = "Huỷ", .Location = New Point(316, 150), .Width = 132, .Height = 32}

        Me.Controls.AddRange(New Control() {lblFileNameCaption, lblFileName, lblSizeCaption, lblSize,
                               lblSaveToCaption, txtSaveTo, btnBrowse, sep, btnLater, btnNow, btnCancelPrompt})
        Me.AcceptButton = btnNow
        Me.CancelButton = btnCancelPrompt
    End Sub

End Class
