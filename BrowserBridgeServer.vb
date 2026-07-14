Imports System
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

    Public Sub New(url As String, suggestedFileName As String)
        Me.Url = url
        Me.SuggestedFileName = suggestedFileName
    End Sub
End Class

''' <summary>
''' Máy chủ HTTP siêu nhẹ, chỉ lắng nghe trên 127.0.0.1 (không mở ra ngoài mạng), để extension
''' trên trình duyệt (Chrome/Edge) gửi URL link đang tải sang chương trình.
'''
''' Giao thức rất đơn giản (khỏi cần thư viện JSON ngoài vì build bằng vbc.exe thuần):
'''   GET  /ping  -> {"ok":true,"app":"FileListDownloader"}   (để extension kiểm tra app có đang chạy)
'''   POST /add   -> body JSON {"url":"...","filename":"..."} -> {"ok":true}
''' Có bật CORS (Access-Control-Allow-Origin: *) để extension gọi fetch() trực tiếp được.
''' </summary>
Public Class BrowserBridgeServer
    Implements IDisposable

    Public Event LinkReceived As EventHandler(Of BrowserLinkReceivedEventArgs)

    Private _listener As HttpListener
    Private _listenerThread As Thread
    Private _port As Integer
    Private _running As Boolean

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
            resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type")

            If req.HttpMethod = "OPTIONS" Then
                resp.StatusCode = 204
                resp.OutputStream.Close()
                Return
            End If

            If req.HttpMethod = "GET" AndAlso req.Url.AbsolutePath = "/ping" Then
                WriteText(resp, "{""ok"":true,""app"":""FileListDownloader""}", "application/json")
                Return
            End If

            If req.HttpMethod = "POST" AndAlso req.Url.AbsolutePath = "/add" Then
                Dim body As String
                Using reader As New StreamReader(req.InputStream, req.ContentEncoding)
                    body = reader.ReadToEnd()
                End Using

                Dim url As String = ExtractJsonStringField(body, "url")
                Dim fname As String = ExtractJsonStringField(body, "filename")

                If String.IsNullOrWhiteSpace(url) Then
                    resp.StatusCode = 400
                    WriteText(resp, "{""ok"":false,""error"":""missing url""}", "application/json")
                    Return
                End If

                RaiseEvent LinkReceived(Me, New BrowserLinkReceivedEventArgs(url.Trim(), fname))
                WriteText(resp, "{""ok"":true}", "application/json")
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

    Public Sub Dispose() Implements IDisposable.Dispose
        StopListening()
    End Sub

End Class
