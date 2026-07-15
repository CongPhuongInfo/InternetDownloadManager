Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Threading

''' <summary>
''' Chịu trách nhiệm tải MỘT tệp (DownloadItem), tự động:
'''  1) "Dò" server bằng một request Range nhỏ để biết tổng dung lượng và server có hỗ trợ
'''     Range request hay không (Accept-Ranges).
'''  2) Nếu hỗ trợ và biết trước dung lượng: cấp phát sẵn tệp đích đúng kích thước rồi chia
'''     thành N đoạn (segment), tải song song mỗi đoạn trên 1 luồng riêng - đây chính là cách
'''     IDM tăng tốc độ tải cho từng tệp bằng nhiều kết nối cùng lúc.
'''  3) Nếu KHÔNG hỗ trợ Range (hoặc không biết trước dung lượng): rơi về tải 1 luồng duy nhất,
'''     vẫn nối tiếp được từ chỗ dở dang nếu server hỗ trợ resume một phần.
''' Hỗ trợ Tạm dừng/Huỷ thông qua 2 cờ PauseRequested/CancelRequested trên DownloadItem -
''' các luồng con tự kiểm tra định kỳ và dừng đúng vị trí (không mất dữ liệu đã tải).
''' </summary>
Public Class FileDownloader

    Private Const BUFFER_SIZE As Integer = 65536
    Private Const DEFAULT_TIMEOUT As Integer = 30000
    Private Const MIN_SEGMENT_SIZE As Long = 1024L * 1024L ' 1 MB - tệp nhỏ hơn mức này sẽ không chia nhiều đoạn

    ''' <summary>
    ''' .NET Framework 4.0 gốc mặc định chỉ bật SSL3/TLS 1.0 cho HttpWebRequest, trong khi hầu hết
    ''' server hiện nay (MediaFire, Google Drive, ...) yêu cầu TLS 1.1/1.2 - nếu không bật, request
    ''' bị từ chối bắt tay ngay với lỗi "Could not create SSL/TLS secure channel". Vì enum
    ''' SecurityProtocolType.Tls11/Tls12 chỉ có từ .NET 4.5 trở lên (không có sẵn khi biên dịch
    ''' bằng vbc.exe 4.0), phải ép giá trị số trực tiếp: Tls=192, Tls11=768, Tls12=3072.
    ''' Static constructor chạy đúng 1 lần, trước request đầu tiên của lớp này.
    ''' </summary>
    Shared Sub New()
        Try
            ServicePointManager.SecurityProtocol = CType(192 Or 768 Or 3072, SecurityProtocolType)
        Catch
            ' He dieu hanh qua cu, khong ho tro - de nguyen mac dinh, cac request se tu bao loi nhu cu.
        End Try
    End Sub

    ''' <summary>
    ''' Tải một DownloadItem, tối đa segmentCount kết nối song song (thực tế có thể ít hơn nếu
    ''' tệp nhỏ hoặc server không hỗ trợ Range). Chạy đồng bộ trên luồng do DownloadQueueManager
    ''' tạo ra, trả về khi tệp đã Hoàn tất / Tạm dừng / Lỗi / Huỷ (xem item.Status khi hàm trả về).
    ''' </summary>
    Public Shared Sub Download(item As DownloadItem, segmentCount As Integer)
        Try
            Dim dir As String = Path.GetDirectoryName(item.LocalPath)
            If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then
                Directory.CreateDirectory(dir)
            End If
        Catch ex As Exception
            item.Status = DownloadStatus.Failed
            item.LastError = ex.Message
            Return
        End Try

        If item.Segments Is Nothing OrElse item.Segments.Count = 0 Then
            PrepareSegments(item, segmentCount)
        End If

        If item.Segments Is Nothing OrElse item.Segments.Count = 0 Then
            item.Status = DownloadStatus.Failed
            item.LastError = "Không thể xác định phạm vi tải."
            Return
        End If

        item.Status = DownloadStatus.Downloading

        Dim threads As New List(Of Thread)
        For Each seg As DownloadSegment In item.Segments
            If seg.IsComplete Then Continue For
            Dim segLocal As DownloadSegment = seg
            Dim t As New Thread(Sub() DownloadSegmentWorker(item, segLocal))
            t.IsBackground = True
            threads.Add(t)
        Next

        For Each t As Thread In threads
            t.Start()
        Next
        For Each t As Thread In threads
            t.Join()
        Next

        If item.Status = DownloadStatus.Downloading Then
            Dim allDone As Boolean = True
            For Each seg As DownloadSegment In item.Segments
                If Not seg.IsComplete Then allDone = False
            Next

            If allDone Then
                item.Status = DownloadStatus.Completed
            ElseIf item.CancelRequested Then
                item.Status = DownloadStatus.Cancelled
            ElseIf item.PauseRequested Then
                item.Status = DownloadStatus.Paused
            Else
                item.Status = DownloadStatus.Failed
            End If
        End If
    End Sub

    ''' <summary>Dò nhanh dung lượng tệp từ xa (không tải, chỉ hỏi header) - dùng cho hộp thoại xác
    ''' nhận trước khi tải. Trả về -1 nếu không xác định được (lỗi mạng, server không trả kích thước...).</summary>
    Public Shared Function ProbeRemoteSize(url As String, referer As String) As Long
        Try
            Dim req As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
            req.Method = "GET"
            req.Timeout = 15000
            req.UserAgent = "FileListDownloader/2CongLC"
            If Not String.IsNullOrEmpty(referer) Then req.Referer = referer
            req.AddRange(0, 0)

            Using resp As HttpWebResponse = CType(req.GetResponse(), HttpWebResponse)
                If resp.StatusCode = HttpStatusCode.PartialContent Then
                    Dim cr As String = resp.Headers("Content-Range")
                    If Not String.IsNullOrEmpty(cr) Then
                        Dim slashIdx As Integer = cr.IndexOf("/"c)
                        If slashIdx >= 0 Then
                            Dim totalStr As String = cr.Substring(slashIdx + 1).Trim()
                            Dim parsedTotal As Long
                            If Long.TryParse(totalStr, parsedTotal) Then Return parsedTotal
                        End If
                    End If
                    Return -1L
                Else
                    Return resp.ContentLength
                End If
            End Using
        Catch ex As Exception
            Return -1L
        End Try
    End Function

    ''' <summary>Dò dung lượng + hỗ trợ Range của server, rồi chia (hoặc không chia) thành các segment.</summary>
    Private Shared Sub PrepareSegments(item As DownloadItem, segmentCount As Integer)
        item.Segments = New List(Of DownloadSegment)
        Dim totalBytes As Long = -1
        Dim supportsRange As Boolean = False

        Try
            Dim probe As HttpWebRequest = CType(WebRequest.Create(item.Data.Url), HttpWebRequest)
            probe.Method = "GET"
            probe.Timeout = DEFAULT_TIMEOUT
            probe.UserAgent = "FileListDownloader/2CongLC"
            If Not String.IsNullOrEmpty(item.Referer) Then probe.Referer = item.Referer
            probe.AddRange(0, 0)

            Using resp As HttpWebResponse = CType(probe.GetResponse(), HttpWebResponse)
                If resp.StatusCode = HttpStatusCode.PartialContent Then
                    supportsRange = True
                    Dim cr As String = resp.Headers("Content-Range")
                    If Not String.IsNullOrEmpty(cr) Then
                        Dim slashIdx As Integer = cr.IndexOf("/"c)
                        If slashIdx >= 0 Then
                            Dim totalStr As String = cr.Substring(slashIdx + 1).Trim()
                            Dim parsedTotal As Long
                            If Long.TryParse(totalStr, parsedTotal) Then totalBytes = parsedTotal
                        End If
                    End If
                Else
                    totalBytes = resp.ContentLength
                End If
            End Using
        Catch ex As Exception
            ' Neu "do" that bai, van co the thu tai kieu 1-luong o duoi (totalBytes se la -1).
        End Try

        item.TotalBytes = totalBytes
        item.SupportsRange = supportsRange

        If supportsRange AndAlso totalBytes > 0 Then
            Dim effectiveSegCount As Integer = segmentCount
            Dim maxSegBySize As Integer = CInt(Math.Max(1L, totalBytes \ MIN_SEGMENT_SIZE))
            If effectiveSegCount > maxSegBySize Then effectiveSegCount = maxSegBySize
            If effectiveSegCount < 1 Then effectiveSegCount = 1

            Dim chunk As Long = totalBytes \ effectiveSegCount
            Dim startOff As Long = 0
            For i As Integer = 0 To effectiveSegCount - 1
                Dim endOff As Long
                If i = effectiveSegCount - 1 Then
                    endOff = totalBytes - 1
                Else
                    endOff = startOff + chunk - 1
                End If
                item.Segments.Add(New DownloadSegment(startOff, endOff, 0))
                startOff = endOff + 1
            Next

            Try
                Using fs As New FileStream(item.LocalPath, FileMode.Create, FileAccess.Write)
                    fs.SetLength(totalBytes)
                End Using
            Catch ex As Exception
                item.Segments.Clear()
                item.LastError = ex.Message
            End Try
        Else
            ' Khong ho tro Range hoac khong biet truoc kich thuoc -> tai 1 luong duy nhat,
            ' van tiep tuc duoc bang cach noi vao tep da co tren dia (giong ban goc).
            Dim existing As Long = 0
            If File.Exists(item.LocalPath) Then existing = New FileInfo(item.LocalPath).Length
            item.Segments.Add(New DownloadSegment(0, -1, existing))
        End If
    End Sub

    ''' <summary>Tải một segment - chạy trên luồng riêng, ghi trực tiếp vào đúng vị trí (offset) của tệp đích.</summary>
    Private Shared Sub DownloadSegmentWorker(item As DownloadItem, seg As DownloadSegment)
        Try
            Dim rangeStart As Long = seg.StartOffset + seg.Downloaded

            Dim req As HttpWebRequest = CType(WebRequest.Create(item.Data.Url), HttpWebRequest)
            req.Method = "GET"
            req.Timeout = DEFAULT_TIMEOUT
            req.ReadWriteTimeout = DEFAULT_TIMEOUT
            req.UserAgent = "FileListDownloader/2CongLC"
            If Not String.IsNullOrEmpty(item.Referer) Then req.Referer = item.Referer

            ' HttpWebRequest.AddRange chỉ có overload kiểu Integer (32-bit) trên .NET Framework 4.x,
            ' nên với tệp/đoạn vượt quá 2GB sẽ không set Range chính xác được - giữ đúng giới hạn
            ' đã ghi chú trong bản gốc thay vì giả vờ hỗ trợ.
            Dim canUseRange As Boolean = (rangeStart <= Integer.MaxValue) AndAlso
                                          (seg.EndOffset < 0 OrElse seg.EndOffset <= Integer.MaxValue)

            If canUseRange Then
                If seg.EndOffset >= 0 Then
                    req.AddRange(CInt(rangeStart), CInt(seg.EndOffset))
                ElseIf rangeStart > 0 Then
                    req.AddRange(CInt(rangeStart))
                End If
            End If

            Using resp As HttpWebResponse = CType(req.GetResponse(), HttpWebResponse)
                Using respStream As Stream = resp.GetResponseStream()
                    Using fs As New FileStream(item.LocalPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)
                        fs.Seek(rangeStart, SeekOrigin.Begin)

                        Dim buffer(BUFFER_SIZE - 1) As Byte
                        Dim read As Integer = respStream.Read(buffer, 0, buffer.Length)

                        While read > 0
                            fs.Write(buffer, 0, read)
                            seg.AddDownloaded(CLng(read))

                            If item.CancelRequested OrElse item.PauseRequested Then Exit While
                            If seg.EndOffset >= 0 AndAlso seg.IsComplete Then Exit While

                            read = respStream.Read(buffer, 0, buffer.Length)
                        End While
                    End Using
                End Using
            End Using

            If seg.EndOffset < 0 AndAlso Not item.CancelRequested AndAlso Not item.PauseRequested Then
                ' Truong hop khong biet truoc kich thuoc va da doc het luong (read=0)
                ' -> danh dau segment nay la hoan tat, dong thoi cap nhat lai TotalBytes de hien thi 100%.
                seg.EndOffset = seg.StartOffset + seg.Downloaded - 1L
                item.TotalBytes = seg.Downloaded
            End If

        Catch ex As WebException When IsRangeNotSatisfiable(ex)
            ' Server bao 416 - nghia la doan nay tren dia da du/dung kich thuoc roi.
            If seg.EndOffset >= 0 Then
                seg.EndOffset = seg.StartOffset + seg.Downloaded - 1L
            End If

        Catch ex As Exception
            If Not item.PauseRequested AndAlso Not item.CancelRequested Then
                item.LastError = ex.Message
                item.Status = DownloadStatus.Failed
            End If
        End Try
    End Sub

    Private Shared Function IsRangeNotSatisfiable(ex As WebException) As Boolean
        Dim resp As HttpWebResponse = TryCast(ex.Response, HttpWebResponse)
        Return resp IsNot Nothing AndAlso resp.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable
    End Function

End Class
