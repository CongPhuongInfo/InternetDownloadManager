Imports System
Imports System.IO

''' <summary>
''' Mô tả một mục trong danh sách tải: URL nguồn và đường dẫn cục bộ tương ứng.
''' Lớp này tự bóc tách đường dẫn tương đối từ URL để engine tải có thể tái tạo
''' đúng cấu trúc thư mục khi lưu về máy.
'''
''' Ghi chú: dùng property kiểu tường minh (field riêng + Get) thay vì auto-property
''' ReadOnly, vì vbc.exe của .NET Framework 4.0 gốc (chưa vá lên 4.5) KHÔNG hỗ trợ
''' gán ReadOnly auto-property trong Sub New - tính năng đó chỉ có từ VB 11 trở lên.
''' </summary>
Public Class FileDownloadData

    Private _url As String
    Private _relativePath As String
    Private _fileName As String

    Public ReadOnly Property Url As String
        Get
            Return _url
        End Get
    End Property

    Public ReadOnly Property RelativePath As String
        Get
            Return _relativePath
        End Get
    End Property

    Public ReadOnly Property FileName As String
        Get
            Return _fileName
        End Get
    End Property

    ''' <summary>
    ''' Khởi tạo từ một dòng URL (vd: http://host/data/sub/anh.png).
    ''' Ném ra UriFormatException nếu dòng không phải URL hợp lệ - nơi gọi
    ''' nên bắt lỗi này để bỏ qua các dòng rỗng/sai định dạng trong tệp danh sách.
    ''' </summary>
    Public Sub New(url As String)
        Dim u As New Uri(url)
        _url = url
        _relativePath = u.LocalPath.Replace("/"c, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar)
        _fileName = Path.GetFileName(_relativePath)
    End Sub

    ''' <summary>
    ''' Ghép đường dẫn tương đối (rút ra từ URL) vào một thư mục gốc để
    ''' có đường dẫn lưu tệp trên máy, vd: rootFolder\data\sub\anh.png
    ''' Chỉ dùng cho tính năng "tải theo danh sách quét từ thư mục", nơi việc
    ''' giữ nguyên cấu trúc thư mục là có chủ đích.
    ''' </summary>
    Public Function GetLocalPath(rootFolder As String) As String
        Return Path.Combine(rootFolder, _relativePath)
    End Function

    ''' <summary>
    ''' Ghép rootFolder + CHỈ tên tệp (bỏ qua toàn bộ cấu trúc thư mục của URL gốc).
    ''' Dùng cho "Thêm URL" thủ công và link nhận từ trình duyệt: nhiều trang (MediaFire,
    ''' Google Drive, v.v.) chèn chuỗi token/hash rất dài vào phần path của URL tải trực
    ''' tiếp - nếu giữ nguyên làm thư mục con sẽ dễ vượt giới hạn 260 ký tự của Windows.
    ''' </summary>
    Public Function GetLocalPathFlat(rootFolder As String) As String
        Return Path.Combine(rootFolder, _fileName)
    End Function

    Public Overrides Function ToString() As String
        Return _url
    End Function

End Class
