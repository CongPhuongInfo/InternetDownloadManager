Imports System
Imports System.Collections.Generic
Imports System.Threading

''' <summary>
''' Điều phối cả HÀNG ĐỢI nhiều tệp: cho phép tối đa maxConcurrent tệp tải CÙNG LÚC (mỗi tệp lại
''' được FileDownloader chia thành segmentsPerFile luồng con) - đây là 2 tầng đa luồng giống IDM
''' (nhiều tệp song song + nhiều kết nối trong từng tệp).
''' Hỗ trợ:
'''  - Tạm dừng/Tiếp tục/Huỷ TOÀN BỘ hàng đợi (PauseAll/Resume/CancelAll).
'''  - Tạm dừng/Tiếp tục/Huỷ TỪNG dòng riêng lẻ (PauseItem/ResumeItem/CancelItem) mà không ảnh
'''    hưởng các tệp khác đang tải - y hệt việc right-click 1 dòng trong IDM.
'''  - Lưu/khôi phục trạng thái (kể cả vị trí từng segment) ra đĩa để tiếp tục đúng chỗ ngay cả
'''    sau khi tắt chương trình.
''' </summary>
Public Class DownloadQueueManager
    Implements IDisposable

    Public Event QueuePaused(remainingCount As Integer)
    Public Event AllCompleted(totalOk As Integer, totalFail As Integer, wasCancelled As Boolean)
    Public Event StateChanged()

    Private _items As List(Of DownloadItem)
    Private _statePath As String
    Private _maxConcurrent As Integer
    Private _segmentsPerFile As Integer
    Private _lockObj As New Object()
    Private _pauseAllRequested As Boolean
    Private _cancelAllRequested As Boolean
    Private _dispatcherThread As Thread
    Private _totalOk As Integer
    Private _totalFail As Integer
    Private _isRunning As Boolean

    Public ReadOnly Property IsBusy As Boolean
        Get
            Return _isRunning
        End Get
    End Property

    Public ReadOnly Property Items As List(Of DownloadItem)
        Get
            Return _items
        End Get
    End Property

    Public Sub New(maxConcurrent As Integer, segmentsPerFile As Integer)
        _maxConcurrent = Math.Max(1, maxConcurrent)
        _segmentsPerFile = Math.Max(1, segmentsPerFile)
    End Sub

    ''' <summary>Bắt đầu một phiên tải mới (hoặc tiếp tục danh sách items đã có sẵn trạng thái).</summary>
    Public Sub Start(items As List(Of DownloadItem), Optional statePath As String = Nothing)
        _items = items
        _statePath = statePath
        _totalOk = 0
        _totalFail = 0
        _pauseAllRequested = False
        _cancelAllRequested = False

        For Each it As DownloadItem In _items
            If it.Status <> DownloadStatus.Completed Then
                it.Status = DownloadStatus.Pending
                it.PauseRequested = False
                it.CancelRequested = False
            End If
        Next

        RunDispatcher()
    End Sub

    ''' <summary>Tiếp tục hàng đợi (dù đang tạm dừng trong phiên hiện tại, hay vừa nạp lại từ tệp trạng thái).</summary>
    Public Sub [Resume](items As List(Of DownloadItem), Optional statePath As String = Nothing)
        Start(items, statePath)
    End Sub

    ''' <summary>Yêu cầu tạm dừng TOÀN BỘ hàng đợi - các tệp đang tải sẽ dừng đúng vị trí đang dở.</summary>
    Public Sub PauseAll()
        _pauseAllRequested = True
        SyncLock _lockObj
            For Each it As DownloadItem In _items
                If it.Status = DownloadStatus.Downloading Then it.PauseRequested = True
                If it.Status = DownloadStatus.Pending Then it.Status = DownloadStatus.Paused
            Next
        End SyncLock
    End Sub

    ''' <summary>Huỷ hẳn TOÀN BỘ hàng đợi, không lưu lại trạng thái để tiếp tục.</summary>
    Public Sub CancelAll()
        _cancelAllRequested = True
        _pauseAllRequested = True
        SyncLock _lockObj
            For Each it As DownloadItem In _items
                it.CancelRequested = True
                it.PauseRequested = True
                If it.Status = DownloadStatus.Pending Then it.Status = DownloadStatus.Cancelled
            Next
        End SyncLock
    End Sub

    ''' <summary>Tạm dừng riêng 1 dòng - các tệp khác trong hàng đợi vẫn tiếp tục tải bình thường.</summary>
    Public Sub PauseItem(item As DownloadItem)
        If item.Status = DownloadStatus.Downloading Then
            item.PauseRequested = True
        ElseIf item.Status = DownloadStatus.Pending Then
            item.Status = DownloadStatus.Paused
        End If
    End Sub

    ''' <summary>Tiếp tục riêng 1 dòng đang Tạm dừng/Lỗi/Đã huỷ - chỉ có tác dụng khi hàng đợi đang chạy (IsBusy).</summary>
    Public Sub ResumeItem(item As DownloadItem)
        If item.Status <> DownloadStatus.Completed AndAlso item.Status <> DownloadStatus.Downloading Then
            item.Status = DownloadStatus.Pending
            item.PauseRequested = False
            item.CancelRequested = False
        End If
    End Sub

    ''' <summary>
    ''' Chèn thêm 1 tệp vào hàng đợi ĐANG CHẠY (vd: link mới nhận từ extension trình duyệt) -
    ''' dispatcher sẽ tự nhặt lên khi có chỗ trống, không cần dừng/khởi động lại hàng đợi.
    ''' Chỉ nên gọi khi IsBusy = True; nếu hàng đợi không chạy, Form1 tự tạo phiên mới thay vì gọi hàm này.
    ''' </summary>
    Public Sub AddItem(item As DownloadItem)
        SyncLock _lockObj
            If _items Is Nothing Then _items = New List(Of DownloadItem)
            item.Status = DownloadStatus.Pending
            item.PauseRequested = False
            item.CancelRequested = False
            _items.Add(item)
        End SyncLock
    End Sub

    ''' <summary>Huỷ riêng 1 dòng - loại khỏi hàng đợi (không tải nữa), các dòng khác không bị ảnh hưởng.</summary>
    Public Sub CancelItem(item As DownloadItem)
        item.CancelRequested = True
        item.PauseRequested = True
        If item.Status = DownloadStatus.Pending Then item.Status = DownloadStatus.Cancelled
    End Sub

    ''' <summary>
    ''' Xoá hẳn 1 mục khỏi danh sách quản lý (khác CancelItem - dòng này biến mất chứ không chỉ
    ''' chuyển trạng thái). Dùng SyncLock vì dispatcher có thể đang duyệt _items ở luồng nền cùng lúc.
    ''' Chỉ nên gọi sau khi đã CancelItem để đảm bảo luồng tải (nếu có) sớm dừng lại.
    ''' </summary>
    Public Sub RemoveItem(item As DownloadItem)
        SyncLock _lockObj
            If _items IsNot Nothing Then _items.Remove(item)
        End SyncLock
    End Sub

    Private Sub RunDispatcher()
        _isRunning = True
        _dispatcherThread = New Thread(AddressOf DispatcherLoop)
        _dispatcherThread.IsBackground = True
        _dispatcherThread.Start()
    End Sub

    Private Sub DispatcherLoop()
        Dim running As New List(Of Thread)

        Do
            Dim i As Integer = running.Count - 1
            Do While i >= 0
                If Not running(i).IsAlive Then running.RemoveAt(i)
                i -= 1
            Loop

            If Not _pauseAllRequested AndAlso Not _cancelAllRequested Then
                Do While running.Count < _maxConcurrent
                    Dim nextItem As DownloadItem = FindNextPending()
                    If nextItem Is Nothing Then Exit Do

                    nextItem.Status = DownloadStatus.Downloading
                    Dim capturedItem As DownloadItem = nextItem
                    Dim segCount As Integer = _segmentsPerFile
                    Dim t As New Thread(Sub() RunOneItem(capturedItem, segCount))
                    t.IsBackground = True
                    running.Add(t)
                    t.Start()
                Loop
            End If

            If running.Count = 0 Then
                If _pauseAllRequested OrElse _cancelAllRequested Then Exit Do
                If FindNextPending() Is Nothing Then Exit Do
            End If

            Thread.Sleep(150)
        Loop

        For Each t As Thread In running
            t.Join()
        Next

        _isRunning = False

        If _cancelAllRequested Then
            DeleteStateIfNeeded()
            RaiseEvent AllCompleted(_totalOk, _totalFail, True)
        ElseIf _pauseAllRequested Then
            SaveStateIfNeeded()
            Dim remaining As Integer = 0
            For Each it As DownloadItem In _items
                If it.Status <> DownloadStatus.Completed Then remaining += 1
            Next
            RaiseEvent QueuePaused(remaining)
        Else
            ' Truoc day goi DeleteStateIfNeeded() o day khien danh sach bien mat het sau khi tat/mo
            ' lai chuong trinh (ke ca cac tep da tai xong). Gio giu lai state de danh sach + trang
            ' thai (Hoan tat/Loi) van con sau khi khoi dong lai - giong lich su tai cua IDM.
            ' Chi "Huy tat ca" (CancelAll, nhanh o tren) hoac nut "Xoa danh sach" moi thuc su xoa.
            SaveStateIfNeeded()
            RaiseEvent AllCompleted(_totalOk, _totalFail, False)
        End If
    End Sub

    Private Function FindNextPending() As DownloadItem
        SyncLock _lockObj
            For Each it As DownloadItem In _items
                If it.Status = DownloadStatus.Pending Then Return it
            Next
        End SyncLock
        Return Nothing
    End Function

    Private Sub RunOneItem(item As DownloadItem, segCount As Integer)
        FileDownloader.Download(item, segCount)

        SyncLock _lockObj
            If item.Status = DownloadStatus.Completed Then
                _totalOk += 1
            ElseIf item.Status = DownloadStatus.Failed OrElse item.Status = DownloadStatus.Cancelled Then
                _totalFail += 1
            End If
        End SyncLock

        RaiseEvent StateChanged()
    End Sub

    ''' <summary>Lưu snapshot trạng thái hiện tại ra đĩa ngay lập tức (Form1 gọi định kỳ khi đang tải).</summary>
    Public Sub SaveStateNow()
        SaveStateIfNeeded()
    End Sub

    Private Sub SaveStateIfNeeded()
        If String.IsNullOrWhiteSpace(_statePath) Then Return
        Try
            DownloadQueueState.Save(_items, _statePath)
        Catch
        End Try
    End Sub

    Private Sub DeleteStateIfNeeded()
        If String.IsNullOrWhiteSpace(_statePath) Then Return
        DownloadQueueState.Delete(_statePath)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _cancelAllRequested = True
    End Sub

End Class
