Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading

''' <summary>
''' Tải MỘT stream HLS (playlist .m3u8), dùng cho các trang stream nội dung KHÔNG mã hoá
''' (không có #EXT-X-KEY với METHOD khác NONE) - phần lớn trang stream nhỏ/CDN nội bộ không phải
''' Big Tech dùng kiểu này. Nếu phát hiện có mã hoá AES-128 (hoặc DRM), dừng lại và báo lỗi rõ ràng
''' thay vì cố tải ra 1 tệp hỏng không phát được - việc giải mã DRM/AES nằm ngoài phạm vi hỗ trợ.
'''
''' Quy trình:
'''  1) Tải văn bản playlist gốc. Nếu là MASTER playlist (chứa nhiều #EXT-X-STREAM-INF, mỗi cái
'''     trỏ tới 1 playlist con ứng với 1 chất lượng) -> chọn variant có BANDWIDTH cao nhất, tải
'''     tiếp playlist con đó (lặp lại nếu playlist con lại là master - hiếm nhưng có thể xảy ra).
'''  2) Từ MEDIA playlist cuối cùng, lấy danh sách URL các đoạn .ts theo đúng thứ tự (bỏ qua dòng
'''     bắt đầu bằng "#", resolve URL tương đối theo đúng playlist chứa nó).
'''  3) Tải tuần tự từng đoạn .ts, ghi nối tiếp (append) vào 1 tệp .ts đích duy nhất - vì MPEG-TS
'''     cho phép nối thẳng nhiều đoạn với nhau mà không cần mux (khác DASH .m4s tách audio/video
'''     riêng, trường hợp đó cần ffmpeg ngoài, không tự ghép tay được).
''' Hỗ trợ Tạm dừng/Tiếp tục đúng đoạn dở (item.HlsSegmentsDone) và Huỷ, giống FileDownloader.
''' </summary>
Public Class HlsDownloader

    Private Const BUFFER_SIZE As Integer = 65536
    Private Const DEFAULT_TIMEOUT As Integer = 30000
    Private Const MAX_PLAYLIST_REDIRECTS As Integer = 4 ' chong vong lap neu master tro vao chinh no

    Shared Sub New()
        Try
            ' Giong het FileDownloader - vbc.exe 4.0 goc khong bat san TLS 1.1/1.2.
            ServicePointManager.SecurityProtocol = CType(192 Or 768 Or 3072, SecurityProtocolType)
        Catch
        End Try
    End Sub

    ''' <summary>Nhận diện nhanh 1 URL có khả năng là playlist HLS hay không, dựa vào đuôi tệp
    ''' (đủ dùng cho phần lớn trường hợp thực tế; server không chuẩn hoá nhiều khi trả .m3u8 mà
    ''' không có đuôi rõ ràng, trường hợp đó người dùng cần tick thủ công loại "HLS" khi Thêm URL).</summary>
    Public Shared Function LooksLikeHls(url As String) As Boolean
        Try
            Dim clean As String = url.Split("?"c)(0).Split("#"c)(0)
            Return clean.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Tải 1 DownloadItem đã đánh dấu IsHlsStream = True. Trả về khi Hoàn tất / Tạm dừng /
    ''' Lỗi / Huỷ (xem item.Status). Chữ ký giống FileDownloader.Download để DownloadQueueManager
    ''' gọi qua lớp trung gian mà không cần biết loại item.</summary>
    Public Shared Sub Download(item As DownloadItem)
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

        If item.HlsSegmentUrls Is Nothing Then
            If Not PreparePlaylist(item) Then
                item.Status = DownloadStatus.Failed
                Return ' PreparePlaylist da tu dat LastError
            End If
        End If

        If item.HlsSegmentUrls Is Nothing OrElse item.HlsSegmentUrls.Count = 0 Then
            item.Status = DownloadStatus.Failed
            item.LastError = "Playlist không có đoạn (.ts) nào."
            Return
        End If

        item.Status = DownloadStatus.Downloading

        Dim startIndex As Integer = CInt(item.HlsSegmentsDone)
        If startIndex >= item.HlsSegmentUrls.Count Then
            item.Status = DownloadStatus.Completed
            Return
        End If

        Try
            ' Mo tep o che do Append neu tiep tuc dang do, hoac tao moi tu dau neu bat dau lai.
            Dim mode As FileMode = If(startIndex > 0 AndAlso File.Exists(item.LocalPath), FileMode.Append, FileMode.Create)
            Using fs As New FileStream(item.LocalPath, mode, FileAccess.Write, FileShare.Read)
                For i As Integer = startIndex To item.HlsSegmentUrls.Count - 1
                    If item.CancelRequested Then
                        item.Status = DownloadStatus.Cancelled
                        Return
                    End If
                    If item.PauseRequested Then
                        item.Status = DownloadStatus.Paused
                        Return
                    End If

                    Dim segUrl As String = item.HlsSegmentUrls(i)
                    Dim ok As Boolean = DownloadOneSegment(segUrl, item.Referer, item.Cookie, fs, item)
                    If Not ok Then
                        If Not item.PauseRequested AndAlso Not item.CancelRequested Then
                            item.Status = DownloadStatus.Failed
                            If String.IsNullOrEmpty(item.LastError) Then item.LastError = "Lỗi tải đoạn " & (i + 1) & "/" & item.HlsSegmentUrls.Count
                        End If
                        Return
                    End If

                    item.AddHlsSegmentDone()
                Next
            End Using

            item.Status = DownloadStatus.Completed
            item.TotalBytes = item.DownloadedBytes ' chi biet chinh xac tong dung luong sau khi tai xong het

        Catch ex As Exception
            item.LastError = ex.Message
            item.Status = DownloadStatus.Failed
        End Try
    End Sub

    ''' <summary>Tải 1 đoạn .ts và ghi thẳng vào FileStream đích (append). Trả về False nếu lỗi mạng
    ''' (item.LastError sẽ được set) - Pause/Cancel giữa chừng 1 đoạn cũng trả về False nhưng KHÔNG
    ''' set LastError (không phải lỗi thật, chỉ dừng theo yêu cầu).</summary>
    Private Shared Function DownloadOneSegment(url As String, referer As String, cookie As String, fs As FileStream, item As DownloadItem) As Boolean
        Try
            Dim req As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
            req.Method = "GET"
            req.Timeout = DEFAULT_TIMEOUT
            req.ReadWriteTimeout = DEFAULT_TIMEOUT
            req.UserAgent = "FileListDownloader/2CongLC"
            If Not String.IsNullOrEmpty(referer) Then req.Referer = referer
            If Not String.IsNullOrEmpty(cookie) Then req.Headers.Add(HttpRequestHeader.Cookie, cookie)

            Using resp As HttpWebResponse = CType(req.GetResponse(), HttpWebResponse)
                Using respStream As Stream = resp.GetResponseStream()
                    Dim buffer(BUFFER_SIZE - 1) As Byte
                    Dim read As Integer = respStream.Read(buffer, 0, buffer.Length)
                    While read > 0
                        fs.Write(buffer, 0, read)
                        item.AddHlsBytesDownloaded(CLng(read))

                        If item.CancelRequested OrElse item.PauseRequested Then Return False

                        read = respStream.Read(buffer, 0, buffer.Length)
                    End While
                End Using
            End Using
            Return True
        Catch ex As Exception
            If item.PauseRequested OrElse item.CancelRequested Then Return False
            item.LastError = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>Tải + parse playlist (theo dõi cả trường hợp master trỏ tới media playlist), điền
    ''' item.HlsSegmentUrls. Trả về False (kèm item.LastError) nếu lỗi mạng, playlist rỗng, hoặc
    ''' phát hiện nội dung có mã hoá (không hỗ trợ giải mã).</summary>
    Private Shared Function PreparePlaylist(item As DownloadItem) As Boolean
        Dim currentUrl As String = item.Data.Url
        Dim hop As Integer = 0

        Do
            hop += 1
            If hop > MAX_PLAYLIST_REDIRECTS Then
                item.LastError = "Playlist lồng nhau quá sâu (có thể bị lỗi vòng lặp)."
                Return False
            End If

            Dim text As String
            Try
                text = FetchText(currentUrl, item.Referer, item.Cookie)
            Catch ex As Exception
                item.LastError = "Không tải được playlist: " & ex.Message
                Return False
            End Try

            If text.IndexOf("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               text.IndexOf("METHOD=NONE", StringComparison.OrdinalIgnoreCase) < 0 Then
                item.LastError = "Stream có mã hoá (#EXT-X-KEY) - không hỗ trợ giải mã, chỉ tải được stream không mã hoá."
                Return False
            End If

            Dim bestVariantUrl As String = FindBestVariantUrl(text, currentUrl)
            If bestVariantUrl IsNot Nothing Then
                ' Day la MASTER playlist - lap lai vong voi playlist con (chat luong cao nhat).
                currentUrl = bestVariantUrl
                Continue Do
            End If

            ' Day la MEDIA playlist (playlist cuoi cung chua chinh cac doan .ts).
            Dim segs As List(Of String) = ExtractSegmentUrls(text, currentUrl)
            item.HlsSegmentUrls = segs
            Return True
        Loop
    End Function

    ''' <summary>Tải nội dung text (dùng cho playlist - luôn nhỏ, không cần Range/segment).</summary>
    Private Shared Function FetchText(url As String, referer As String, cookie As String) As String
        Dim req As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
        req.Method = "GET"
        req.Timeout = DEFAULT_TIMEOUT
        req.UserAgent = "FileListDownloader/2CongLC"
        If Not String.IsNullOrEmpty(referer) Then req.Referer = referer
        If Not String.IsNullOrEmpty(cookie) Then req.Headers.Add(HttpRequestHeader.Cookie, cookie)

        Using resp As HttpWebResponse = CType(req.GetResponse(), HttpWebResponse)
            Using reader As New StreamReader(resp.GetResponseStream(), Encoding.UTF8)
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    ''' <summary>Nếu playlistText là MASTER playlist, tìm dòng #EXT-X-STREAM-INF có BANDWIDTH cao
    ''' nhất và trả về URL (đã resolve tuyệt đối) của playlist con tương ứng. Trả về Nothing nếu
    ''' đây không phải master playlist (tức không có dòng #EXT-X-STREAM-INF nào).</summary>
    Private Shared Function FindBestVariantUrl(playlistText As String, playlistUrl As String) As String
        Dim lines() As String = playlistText.Replace(vbCr, "").Split(vbLf.ToCharArray())
        Dim bestBandwidth As Long = -1
        Dim bestUrl As String = Nothing

        For i As Integer = 0 To lines.Length - 1
            Dim line As String = lines(i).Trim()
            If line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) Then
                Dim bw As Long = 0
                Dim idx As Integer = line.IndexOf("BANDWIDTH=", StringComparison.OrdinalIgnoreCase)
                If idx >= 0 Then
                    Dim start As Integer = idx + "BANDWIDTH=".Length
                    Dim endIdx As Integer = start
                    While endIdx < line.Length AndAlso Char.IsDigit(line(endIdx))
                        endIdx += 1
                    End While
                    Long.TryParse(line.Substring(start, endIdx - start), bw)
                End If

                ' Dong URL cua variant nay la dong khong-rong tiep theo sau dong #EXT-X-STREAM-INF.
                Dim j As Integer = i + 1
                While j < lines.Length AndAlso String.IsNullOrWhiteSpace(lines(j))
                    j += 1
                End While
                If j < lines.Length AndAlso Not lines(j).TrimStart().StartsWith("#") Then
                    If bw >= bestBandwidth Then
                        bestBandwidth = bw
                        bestUrl = ResolveUrl(playlistUrl, lines(j).Trim())
                    End If
                End If
            End If
        Next

        Return bestUrl
    End Function

    ''' <summary>Lấy danh sách URL các đoạn .ts từ 1 MEDIA playlist (bỏ mọi dòng bắt đầu bằng "#",
    ''' resolve URL tương đối theo đúng URL của playlist chứa nó).</summary>
    Private Shared Function ExtractSegmentUrls(playlistText As String, playlistUrl As String) As List(Of String)
        Dim result As New List(Of String)
        Dim lines() As String = playlistText.Replace(vbCr, "").Split(vbLf.ToCharArray())
        For Each raw As String In lines
            Dim line As String = raw.Trim()
            If line.Length = 0 OrElse line.StartsWith("#") Then Continue For
            result.Add(ResolveUrl(playlistUrl, line))
        Next
        Return result
    End Function

    ''' <summary>Resolve 1 URL (có thể tương đối) theo URL playlist chứa nó, giống hệt cách trình
    ''' duyệt/HLS player xử lý đường dẫn tương đối trong file .m3u8.</summary>
    Private Shared Function ResolveUrl(baseUrl As String, maybeRelative As String) As String
        Try
            Dim baseUri As New Uri(baseUrl)
            Dim combined As New Uri(baseUri, maybeRelative)
            Return combined.AbsoluteUri
        Catch
            Return maybeRelative
        End Try
    End Function

End Class
