Imports System
Imports System.Collections.Generic
Imports System.Threading

Public Enum DownloadStatus
    Pending
    Downloading
    Paused
    Completed
    Failed
    Cancelled
End Enum

''' <summary>
''' Một đoạn (segment) trong tệp cần tải: [StartOffset..EndOffset] (theo byte, EndOffset bao gồm cả byte cuối).
''' EndOffset = -1 nghĩa là KHÔNG biết trước độ dài (server không trả Content-Length/không hỗ trợ Range) -
''' trường hợp này luôn chỉ có 1 segment duy nhất cho cả tệp, tải tuần tự tới khi hết luồng.
''' Downloaded dùng Interlocked để đọc/ghi an toàn giữa luồng tải nền và luồng UI đọc tiến độ.
''' </summary>
Public Class DownloadSegment

    Public StartOffset As Long
    Public EndOffset As Long
    Private _downloaded As Long

    Public Sub New(startOffset As Long, endOffset As Long, alreadyDownloaded As Long)
        Me.StartOffset = startOffset
        Me.EndOffset = endOffset
        _downloaded = alreadyDownloaded
    End Sub

    Public ReadOnly Property Downloaded As Long
        Get
            Return Interlocked.Read(_downloaded)
        End Get
    End Property

    Public Sub AddDownloaded(count As Long)
        Interlocked.Add(_downloaded, count)
    End Sub

    Public ReadOnly Property Length As Long
        Get
            If EndOffset < 0 Then Return -1
            Return EndOffset - StartOffset + 1L
        End Get
    End Property

    Public ReadOnly Property IsComplete As Boolean
        Get
            If EndOffset < 0 Then Return False
            Return Downloaded >= Length
        End Get
    End Property

End Class

''' <summary>
''' Một mục cần tải: dữ liệu URL/đường dẫn tương đối (FileDownloadData), đường dẫn cục bộ đích,
''' trạng thái hiện tại, và danh sách các segment (đoạn) đang/đã tải - hỗ trợ tải đa luồng cho
''' từng tệp (giống cơ chế tăng tốc của IDM) cũng như Tạm dừng/Tiếp tục đúng vị trí từng đoạn.
''' </summary>
Public Class DownloadItem

    Public Data As FileDownloadData
    Public LocalPath As String
    Public Status As DownloadStatus
    Public TotalBytes As Long          ' -1 = chưa biết trước kích thước
    Public SupportsRange As Boolean
    Public Segments As List(Of DownloadSegment)
    Public LastError As String

    ' Cờ điều khiển - do DownloadQueueManager / thao tác từng dòng trên lưới đặt,
    ' các luồng tải nền tự kiểm tra định kỳ để dừng đúng chỗ.
    Public PauseRequested As Boolean
    Public CancelRequested As Boolean

    ' Dùng để tính tốc độ tức thời - chỉ luồng UI (Form1) đọc/ghi nên không cần đồng bộ hoá.
    Public LastSampleBytes As Long
    Public LastSampleTicks As Long
    Public CurrentSpeedBps As Double

    Public Sub New()
        TotalBytes = -1L
        Status = DownloadStatus.Pending
        Segments = New List(Of DownloadSegment)
    End Sub

    ''' <summary>Tổng số byte đã tải được của tệp này (cộng dồn từ tất cả các segment).</summary>
    Public ReadOnly Property DownloadedBytes As Long
        Get
            Dim total As Long = 0
            For Each seg As DownloadSegment In Segments
                total += seg.Downloaded
            Next
            Return total
        End Get
    End Property

End Class
