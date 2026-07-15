Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text

''' <summary>
''' Lưu và khôi phục danh sách DownloadItem (kèm trạng thái VÀ vị trí từng segment) ra một tệp
''' văn bản đơn giản, để phiên tải dở có thể được TIẾP TỤC đúng chỗ ngay cả sau khi người dùng
''' đóng chương trình - kể cả khi tệp đang ở giữa chừng của tải đa luồng (multi-segment).
''' Định dạng mỗi dòng: TrangThai|DuongDanCucBo|TongDung Luong|DuLieuSegment|Url
''' DuLieuSegment: "-" nếu chưa có, hoặc "start:end:downloaded;start:end:downloaded;..."
''' </summary>
Public Class DownloadQueueState

    Private Const SEP As Char = "|"c
    Private Const SEG_SEP As Char = ";"c
    Private Const SEG_FIELD_SEP As Char = ":"c

    Public Shared Sub Save(items As List(Of DownloadItem), statePath As String)
        Dim dir As String = Path.GetDirectoryName(statePath)
        If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then
            Directory.CreateDirectory(dir)
        End If

        Dim sb As New StringBuilder()
        For Each it As DownloadItem In items
            sb.Append(it.Status.ToString()).Append(SEP)
            sb.Append(it.LocalPath).Append(SEP)
            sb.Append(it.TotalBytes.ToString(CultureInfo.InvariantCulture)).Append(SEP)
            sb.Append(SerializeSegments(it)).Append(SEP)
            sb.Append(it.Data.Url).AppendLine()
        Next

        File.WriteAllText(statePath, sb.ToString(), Encoding.UTF8)
    End Sub

    Private Shared Function SerializeSegments(it As DownloadItem) As String
        If it.Segments Is Nothing OrElse it.Segments.Count = 0 Then Return "-"

        Dim parts As New List(Of String)
        For Each seg As DownloadSegment In it.Segments
            parts.Add(seg.StartOffset.ToString(CultureInfo.InvariantCulture) & SEG_FIELD_SEP &
                      seg.EndOffset.ToString(CultureInfo.InvariantCulture) & SEG_FIELD_SEP &
                      seg.Downloaded.ToString(CultureInfo.InvariantCulture))
        Next
        Return String.Join(SEG_SEP.ToString(), parts.ToArray())
    End Function

    ''' <summary>
    ''' Đọc lại danh sách đã lưu. Các mục Completed vẫn giữ nguyên trạng thái (để bỏ qua khi tải lại),
    ''' các mục khác đều được đưa về Pending kèm segment đã lưu để FileDownloader tiếp tục đúng vị trí
    ''' (không phải tải lại từ đầu từng đoạn đã dở dang).
    ''' </summary>
    Public Shared Function Load(statePath As String) As List(Of DownloadItem)
        Dim result As New List(Of DownloadItem)
        If Not File.Exists(statePath) Then Return result

        Dim lines As String() = File.ReadAllLines(statePath, Encoding.UTF8)
        For Each line As String In lines
            If String.IsNullOrWhiteSpace(line) Then Continue For

            Dim parts As String() = line.Split(SEP)
            If parts.Length < 5 Then Continue For

            Try
                Dim rawStatus As String = parts(0)
                Dim localPath As String = parts(1)
                Dim totalBytesStr As String = parts(2)
                Dim segData As String = parts(3)
                Dim url As String = String.Join(SEP.ToString(), parts, 4, parts.Length - 4)

                Dim data As New FileDownloadData(url)
                Dim item As New DownloadItem()
                item.Data = data
                item.LocalPath = localPath

                Dim totalBytes As Long
                Long.TryParse(totalBytesStr, NumberStyles.Integer, CultureInfo.InvariantCulture, totalBytes)
                item.TotalBytes = totalBytes

                If rawStatus = DownloadStatus.Completed.ToString() Then
                    item.Status = DownloadStatus.Completed
                Else
                    item.Status = DownloadStatus.Pending
                    item.Segments = DeserializeSegments(segData)
                End If

                result.Add(item)
            Catch
                ' Bỏ qua dòng lỗi định dạng, không làm hỏng toàn bộ danh sách
            End Try
        Next

        Return result
    End Function

    Private Shared Function DeserializeSegments(segData As String) As List(Of DownloadSegment)
        Dim result As New List(Of DownloadSegment)
        If String.IsNullOrEmpty(segData) OrElse segData = "-" Then Return result

        Dim segs As String() = segData.Split(SEG_SEP)
        For Each s As String In segs
            Dim f As String() = s.Split(SEG_FIELD_SEP)
            If f.Length <> 3 Then Continue For

            Dim startOff, endOff, downloaded As Long
            If Long.TryParse(f(0), NumberStyles.Integer, CultureInfo.InvariantCulture, startOff) AndAlso
               Long.TryParse(f(1), NumberStyles.Integer, CultureInfo.InvariantCulture, endOff) AndAlso
               Long.TryParse(f(2), NumberStyles.Integer, CultureInfo.InvariantCulture, downloaded) Then
                result.Add(New DownloadSegment(startOff, endOff, downloaded))
            End If
        Next

        Return result
    End Function

    Public Shared Function Exists(statePath As String) As Boolean
        Return File.Exists(statePath)
    End Function

    Public Shared Sub Delete(statePath As String)
        Try
            If File.Exists(statePath) Then File.Delete(statePath)
        Catch
        End Try
    End Sub

End Class
