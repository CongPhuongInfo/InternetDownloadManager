Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading

''' <summary>
''' Đối số sự kiện khi nhận được 1 link tải từ extension trình duyệt.
''' </summary>
Public Class BrowserLinkReceivedEventArgs
    Inherits EventArgs

    Public Url As String
    Public SuggestedFileName As String
    Public Referer As String
    ''' <summary>"manual" (chuột phải vào link / nút tải nổi - NÊN hỏi xác nhận),
    ''' "auto" (tự động bắt tải của trình duyệt - không hỏi, giữ mượt),
    ''' "batch" (quét hàng loạt link trên trang - không hỏi từng cái một).</summary>
    Public Source As String

    Public Sub New(url As String, suggestedFileName As String, Optional referer As String = Nothing, Optional source As String = "manual")
        Me.Url = url
        Me.SuggestedFileName = suggestedFileName
        Me.Referer = referer
        Me.Source = If(String.IsNullOrEmpty(source), "manual", source)
    End Sub
End Class

''' <summary>
''' Máy chủ HTTP siêu nhẹ, chỉ lắng nghe trên 127.0.0.1 (không mở ra ngoài mạng), để extension
''' trên trình duyệt (Chrome/Edge) gửi URL link đang tải sang chương trình.
'''
''' Giao thức rất đơn giản (khỏi cần thư viện JSON ngoài vì build bằng vbc.exe thuần):
'''   GET  /ping       -> {"ok":true,"app":"FileListDownloader","paired":true|false}
'''   POST /add        -> body {"url":"...","filename":"...","referer":"..."} -> {"ok":true}
'''   POST /add-batch  -> body {"urls":[{"url":"...","filename":"...","referer":"..."}, ...]}
'''                        -> {"ok":true,"added":N}
''' Có bật CORS (Access-Control-Allow-Origin: *) để extension gọi fetch() trực tiếp được.
'''
''' BẢO MẬT: vì CORS mở cho mọi origin, BẤT KỲ trang web nào (không chỉ qua extension) đều có
''' thể tự gọi thẳng /add nếu không có gì chặn lại. Vì vậy /add và /add-batch bắt buộc header
''' "X-FLD-Token" khớp với ExpectedToken (sinh ngẫu nhiên, hiển thị trong hộp thoại Cài đặt để
''' người dùng dán 1 lần vào popup của extension). /ping thì không bắt buộc token (chỉ để biết
''' app có đang chạy hay không) nhưng có trả về "paired" để popup biết token đã đúng chưa.
''' </summary>
Public Class BrowserBridgeServer
    Implements IDisposable

    Public Event LinkReceived As EventHandler(Of BrowserLinkReceivedEventArgs)

    Private _listener As HttpListener
    Private _listenerThread As Thread
    Private _port As Integer
    Private _running As Boolean

    ''' <summary>Token bí mật mà extension phải gửi kèm (header X-FLD-Token) cho /add, /add-batch.
    ''' Nếu để trống thì KHÔNG kiểm tra (không khuyến khích - chỉ để tương thích ngược).</summary>
    Public Property ExpectedToken As String = ""

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return _running
        End Get
    End Property

    Public ReadOnly Property Port As Integer
        Get
            Return _port
        End Get
    End Property

    ''' <summary>Bắt đầu lắng nghe trên 127.0.0.1:port. Ném Exception nếu không mở được cổng.</summary>
    Public Sub StartListening(port As Integer)
        If _running Then Return

        _port = port
        _listener = New HttpListener()
        _listener.Prefixes.Add("http://127.0.0.1:" & port & "/")

        Try
            _listener.Start()
        Catch ex As Exception
            Throw New Exception("Không thể mở cổng " & port & ": " & ex.Message)
        End Try

        _running = True
        _listenerThread = New Thread(AddressOf ListenLoop)
        _listenerThread.IsBackground = True
        _listenerThread.Start()
    End Sub

    Public Sub StopListening()
        _running = False
        Try
            If _listener IsNot Nothing AndAlso _listener.IsListening Then _listener.Stop()
        Catch
        End Try
    End Sub

    Private Sub ListenLoop()
        While _running
            Try
                Dim ctx As HttpListenerContext = _listener.GetContext()
                ThreadPool.QueueUserWorkItem(AddressOf HandleContext, ctx)
            Catch ex As HttpListenerException
                Exit While ' listener vua bi Stop()
            Catch ex As ObjectDisposedException
                Exit While
            Catch ex As Exception
                ' Loi don le tren 1 request - bo qua, tiep tuc lang nghe request ke tiep.
            End Try
        End While
    End Sub

    Private Sub HandleContext(state As Object)
        Dim ctx As HttpListenerContext = CType(state, HttpListenerContext)
        Try
            Dim req As HttpListenerRequest = ctx.Request
            Dim resp As HttpListenerResponse = ctx.Response

            resp.Headers.Add("Access-Control-Allow-Origin", "*")
            resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-FLD-Token")

            If req.HttpMethod = "OPTIONS" Then
                resp.StatusCode = 204
                resp.OutputStream.Close()
                Return
            End If

            Dim providedToken As String = req.Headers("X-FLD-Token")
            Dim tokenOk As Boolean = String.IsNullOrEmpty(ExpectedToken) OrElse
                                      (Not String.IsNullOrEmpty(providedToken) AndAlso providedToken = ExpectedToken)

            If req.HttpMethod = "GET" AndAlso req.Url.AbsolutePath = "/ping" Then
                WriteText(resp, "{""ok"":true,""app"":""FileListDownloader"",""paired"":" &
                                 If(tokenOk, "true", "false") & "}", "application/json")
                Return
            End If

            If req.HttpMethod = "POST" AndAlso (req.Url.AbsolutePath = "/add" OrElse req.Url.AbsolutePath = "/add-batch") Then
                If Not tokenOk Then
                    resp.StatusCode = 401
                    WriteText(resp, "{""ok"":false,""error"":""invalid token""}", "application/json")
                    Return
                End If

                Dim body As String
                Using reader As New StreamReader(req.InputStream, req.ContentEncoding)
                    body = reader.ReadToEnd()
                End Using

                If req.Url.AbsolutePath = "/add" Then
                    Dim url As String = ExtractJsonStringField(body, "url")
                    Dim fname As String = ExtractJsonStringField(body, "filename")
                    Dim referer As String = ExtractJsonStringField(body, "referer")
                    Dim source As String = ExtractJsonStringField(body, "source")

                    If String.IsNullOrWhiteSpace(url) Then
                        resp.StatusCode = 400
                        WriteText(resp, "{""ok"":false,""error"":""missing url""}", "application/json")
                        Return
                    End If

                    RaiseEvent LinkReceived(Me, New BrowserLinkReceivedEventArgs(url.Trim(), fname, referer, source))
                    WriteText(resp, "{""ok"":true}", "application/json")
                Else
                    Dim arrInner As String = ExtractJsonArrayField(body, "urls")
                    Dim added As Integer = 0
                    If arrInner IsNot Nothing Then
                        For Each objStr As String In SplitJsonObjects(arrInner)
                            Dim url As String = ExtractJsonStringField(objStr, "url")
                            Dim fname As String = ExtractJsonStringField(objStr, "filename")
                            Dim referer As String = ExtractJsonStringField(objStr, "referer")
                            If Not String.IsNullOrWhiteSpace(url) Then
                                RaiseEvent LinkReceived(Me, New BrowserLinkReceivedEventArgs(url.Trim(), fname, referer, "batch"))
                                added += 1
                            End If
                        Next
                    End If
                    WriteText(resp, "{""ok"":true,""added"":" & added & "}", "application/json")
                End If
                Return
            End If

            resp.StatusCode = 404
            WriteText(resp, "{""ok"":false,""error"":""not found""}", "application/json")
        Catch ex As Exception
            Try
                ctx.Response.StatusCode = 500
                WriteText(ctx.Response, "{""ok"":false}", "application/json")
            Catch
            End Try
        End Try
    End Sub

    Private Sub WriteText(resp As HttpListenerResponse, text As String, contentType As String)
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(text)
        resp.ContentType = contentType & "; charset=utf-8"
        resp.ContentLength64 = bytes.Length
        resp.OutputStream.Write(bytes, 0, bytes.Length)
        resp.OutputStream.Close()
    End Sub

    ''' <summary>
    ''' Bóc 1 field string đơn giản từ JSON phẳng { "url": "...", "filename": "..." } mà không cần
    ''' thư viện JSON ngoài. Đủ dùng cho payload đơn giản do extension gửi lên; có xử lý escape
    ''' cơ bản (\", \\, \/, \n, \r, \t).
    ''' </summary>
    Private Function ExtractJsonStringField(json As String, fieldName As String) As String
        If String.IsNullOrEmpty(json) Then Return Nothing

        Dim key As String = """" & fieldName & """"
        Dim keyIdx As Integer = json.IndexOf(key, StringComparison.OrdinalIgnoreCase)
        If keyIdx < 0 Then Return Nothing

        Dim colonIdx As Integer = json.IndexOf(":"c, keyIdx + key.Length)
        If colonIdx < 0 Then Return Nothing

        Dim i As Integer = colonIdx + 1
        While i < json.Length AndAlso Char.IsWhiteSpace(json(i))
            i += 1
        End While
        If i >= json.Length OrElse json(i) <> """"c Then Return Nothing
        i += 1

        Dim sb As New StringBuilder()
        While i < json.Length AndAlso json(i) <> """"c
            If json(i) = "\"c AndAlso i + 1 < json.Length Then
                Dim nextCh As Char = json(i + 1)
                Select Case nextCh
                    Case """"c
                        sb.Append(""""c) : i += 2
                    Case "\"c
                        sb.Append("\"c) : i += 2
                    Case "/"c
                        sb.Append("/"c) : i += 2
                    Case "n"c
                        sb.Append(vbLf) : i += 2
                    Case "r"c
                        sb.Append(vbCr) : i += 2
                    Case "t"c
                        sb.Append(vbTab) : i += 2
                    Case Else
                        sb.Append(nextCh) : i += 2
                End Select
            Else
                sb.Append(json(i))
                i += 1
            End If
        End While

        Return sb.ToString()
    End Function

    ''' <summary>Lấy chuỗi thô bên trong [ ... ] của field mảng (vd "urls"), có theo dõi dấu ngoặc kép
    ''' để không bị lẫn nếu 1 giá trị string chứa ký tự [ hoặc ]. Trả về Nothing nếu không tìm thấy.</summary>
    Private Function ExtractJsonArrayField(json As String, fieldName As String) As String
        If String.IsNullOrEmpty(json) Then Return Nothing
        Dim key As String = """" & fieldName & """"
        Dim keyIdx As Integer = json.IndexOf(key, StringComparison.OrdinalIgnoreCase)
        If keyIdx < 0 Then Return Nothing
        Dim bracketIdx As Integer = json.IndexOf("["c, keyIdx)
        If bracketIdx < 0 Then Return Nothing

        Dim depth As Integer = 0
        Dim inStr As Boolean = False
        Dim i As Integer = bracketIdx
        While i < json.Length
            Dim c As Char = json(i)
            If inStr Then
                If c = "\"c Then
                    i += 1
                ElseIf c = """"c Then
                    inStr = False
                End If
            Else
                If c = """"c Then
                    inStr = True
                ElseIf c = "["c Then
                    depth += 1
                ElseIf c = "]"c Then
                    depth -= 1
                    If depth = 0 Then Return json.Substring(bracketIdx + 1, i - bracketIdx - 1)
                End If
            End If
            i += 1
        End While
        Return Nothing
    End Function

    ''' <summary>Cắt chuỗi bên trong 1 mảng JSON thành từng object con {..}, {..}, ... (theo dõi độ
    ''' sâu ngoặc nhọn + trạng thái trong/ngoài chuỗi để không cắt nhầm).</summary>
    Private Function SplitJsonObjects(arrayInner As String) As List(Of String)
        Dim result As New List(Of String)
        Dim depth As Integer = 0
        Dim inStr As Boolean = False
        Dim start As Integer = -1
        Dim i As Integer = 0
        While i < arrayInner.Length
            Dim c As Char = arrayInner(i)
            If inStr Then
                If c = "\"c Then
                    i += 1
                ElseIf c = """"c Then
                    inStr = False
                End If
            Else
                If c = """"c Then
                    inStr = True
                ElseIf c = "{"c Then
                    If depth = 0 Then start = i
                    depth += 1
                ElseIf c = "}"c Then
                    depth -= 1
                    If depth = 0 AndAlso start >= 0 Then
                        result.Add(arrayInner.Substring(start, i - start + 1))
                        start = -1
                    End If
                End If
            End If
            i += 1
        End While
        Return result
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        StopListening()
    End Sub

End Class
