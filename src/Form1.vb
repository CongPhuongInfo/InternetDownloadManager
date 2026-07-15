Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Màn hình chính kiểu Internet Download Manager: menu + toolbar ở trên, LƯỚI danh sách tải
''' chiếm gần trọn cửa sổ, thanh trạng thái tổng ở dưới. Việc "tạo danh sách URL từ thư mục" và
''' "tải theo danh sách .txt có sẵn" (vốn là 2 khối lớn trên form ở bản trước) giờ nằm gọn trong
''' menu Tệp dưới dạng hộp thoại riêng - đúng tinh thần IDM: cửa sổ chính LÀ trình quản lý tải,
''' các thao tác phụ chỉ là hộp thoại phụ trợ.
''' Cách thêm tải chính, giống IDM nhất: nút "Thêm URL" (dán link, tải ngay) và nhận link tự động
''' từ extension trình duyệt qua BrowserBridgeServer.
''' Giao diện dựng bằng code (không dùng Designer.vb) để build được bằng vbc.exe.
''' </summary>
Public Class Form1
    Inherits Form

    ' ==== Khung chính ====
    Private menuStrip As MenuStrip
    Private toolStrip As ToolStrip
    Private toolBtnAddUrl As ToolStripButton
    Private toolBtnPauseAll As ToolStripButton
    Private toolBtnResumeAll As ToolStripButton
    Private toolBtnCancelAll As ToolStripButton
    Private miPauseAll As ToolStripMenuItem
    Private miResumeAll As ToolStripMenuItem
    Private miCancelAll As ToolStripMenuItem

    Private WithEvents lvGrid As ListView
    Private ctxGrid As ContextMenuStrip

    Private lblOverallStatus As Label
    Private progressOverall As ProgressBar
    Private lblBridgeStatusBar As Label

    Private WithEvents uiTimer As Timer

    Private _trayIcon As NotifyIcon
    Private _bridgeServer As BrowserBridgeServer

    ' ==== Cột trong lưới ====
    Private Const COL_NAME As Integer = 0
    Private Const COL_SIZE As Integer = 1
    Private Const COL_PROGRESS As Integer = 2
    Private Const COL_SPEED As Integer = 3
    Private Const COL_ETA As Integer = 4
    Private Const COL_STATUS As Integer = 5

    ' ==== Trạng thái tải ====
    Private _queueManager As DownloadQueueManager
    Private _items As List(Of DownloadItem)
    Private _saveTickCounter As Integer

    ' ==== Cài đặt (nạp/lưu ra temp\settings.txt) ====
    Private _defaultDownloadFolder As String
    Private _segments As Integer
    Private _concurrent As Integer
    Private _bridgeEnabled As Boolean
    Private _bridgePort As Integer
    Private _bridgeToken As String

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        LoadSettings()

        If _bridgeEnabled Then TryStartBridge(_bridgePort, True)

        Dim statePath As String = GetQueueStatePath()
        If DownloadQueueState.Exists(statePath) Then
            Try
                Dim loaded As List(Of DownloadItem) = DownloadQueueState.Load(statePath)
                If loaded.Count > 0 Then
                    _items = loaded
                    BuildGridRows(_items)
                    UpdateAllRows()
                    lblOverallStatus.Text = "Đã nạp phiên tải trước - còn " & CountPending(_items) & " tệp chưa xong. Bấm ""Tiếp tục"" để tải tiếp."
                End If
            Catch ex As Exception
            End Try
        End If

        SetRunningButtonsState(False)
    End Sub

    Private Sub Form1_Resize(sender As Object, e As EventArgs) Handles MyBase.Resize
        If Me.WindowState = FormWindowState.Minimized Then
            Me.Hide()
            _trayIcon.Visible = True
            _trayIcon.ShowBalloonTip(1500, "Đang chạy nền", "Chương trình vẫn tải tệp trong khay hệ thống.", ToolTipIcon.Info)
        End If
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        _trayIcon.Visible = False
        StopBridgeServer()
    End Sub

    ' ========================================================================
    '  CÀI ĐẶT (lưu/nạp temp\settings.txt)
    ' ========================================================================

    Private Function GetSettingsPath() As String
        Return Path.Combine(Directory.GetCurrentDirectory(), "temp", "settings.txt")
    End Function

    Private Sub LoadSettings()
        _defaultDownloadFolder = Path.Combine(Directory.GetCurrentDirectory(), "Download")
        _segments = 4
        _concurrent = 3
        _bridgePort = 39215
        _bridgeEnabled = False
        _bridgeToken = Guid.NewGuid().ToString("N")

        Dim settingsPath As String = GetSettingsPath()
        If Not File.Exists(settingsPath) Then Return

        Try
            For Each line As String In File.ReadAllLines(settingsPath)
                Dim idx As Integer = line.IndexOf("="c)
                If idx <= 0 Then Continue For
                Dim key As String = line.Substring(0, idx).Trim()
                Dim value As String = line.Substring(idx + 1).Trim()

                Select Case key
                    Case "Segments"
                        Dim v As Integer
                        If Integer.TryParse(value, v) Then _segments = v
                    Case "Concurrent"
                        Dim v As Integer
                        If Integer.TryParse(value, v) Then _concurrent = v
                    Case "DownloadFolder"
                        If value.Length > 0 Then _defaultDownloadFolder = value
                    Case "BridgeEnabled"
                        Dim v As Boolean
                        If Boolean.TryParse(value, v) Then _bridgeEnabled = v
                    Case "BridgePort"
                        Dim v As Integer
                        If Integer.TryParse(value, v) Then _bridgePort = v
                    Case "BridgeToken"
                        If value.Length > 0 Then _bridgeToken = value
                End Select
            Next
        Catch ex As Exception
        End Try
    End Sub

    Private Sub SaveSettings()
        Try
            Dim dir As String = Path.GetDirectoryName(GetSettingsPath())
            If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)

            Dim lines As New List(Of String)
            lines.Add("Segments=" & _segments)
            lines.Add("Concurrent=" & _concurrent)
            lines.Add("DownloadFolder=" & _defaultDownloadFolder)
            lines.Add("BridgeEnabled=" & _bridgeEnabled.ToString())
            lines.Add("BridgePort=" & _bridgePort)
            lines.Add("BridgeToken=" & _bridgeToken)
            File.WriteAllLines(GetSettingsPath(), lines.ToArray())
        Catch ex As Exception
        End Try
    End Sub

    Private Sub ShowSettingsDialog()
        Using dlg As New SettingsDialog(_segments, _concurrent, _defaultDownloadFolder, _bridgeEnabled, _bridgePort, _bridgeToken)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _segments = dlg.Segments
                _concurrent = dlg.Concurrent
                _defaultDownloadFolder = dlg.DefaultDownloadFolder
                _bridgeToken = dlg.BrowserBridgeToken

                If dlg.BrowserBridgeEnabled Then
                    TryStartBridge(dlg.BrowserBridgePort, False)
                Else
                    StopBridgeServer()
                    _bridgeEnabled = False
                    _bridgePort = dlg.BrowserBridgePort
                    lblBridgeStatusBar.Text = "Trình duyệt: đã tắt"
                    lblBridgeStatusBar.ForeColor = Color.Gray
                End If

                SaveSettings()
            End If
        End Using
    End Sub

    ' ========================================================================
    '  HÀNG ĐỢI TẢI - THÊM / TẠM DỪNG / TIẾP TỤC / HUỶ (áp dụng chung, không phân theo "dự án")
    ' ========================================================================

    Private Function GetQueueStatePath() As String
        Return Path.Combine(Directory.GetCurrentDirectory(), "temp", "queue.txt")
    End Function

    Private Function CountPending(items As List(Of DownloadItem)) As Integer
        Dim n As Integer = 0
        For Each it As DownloadItem In items
            If it.Status <> DownloadStatus.Completed Then n += 1
        Next
        Return n
    End Function

    Private Function HasResumableItems() As Boolean
        If _items Is Nothing Then Return False
        For Each it As DownloadItem In _items
            If it.Status <> DownloadStatus.Completed Then Return True
        Next
        Return False
    End Function

    ''' <summary>Điểm vào DUY NHẤT để đưa tệp mới vào hàng đợi - dùng chung cho Thêm URL, tải theo danh sách, và link từ trình duyệt.</summary>
    Private Sub AddItemsToQueue(newItems As List(Of DownloadItem))
        If newItems Is Nothing OrElse newItems.Count = 0 Then Return

        If _items Is Nothing Then _items = New List(Of DownloadItem)

        If _queueManager IsNot Nothing AndAlso _queueManager.IsBusy Then
            For Each it As DownloadItem In newItems
                _queueManager.AddItem(it) ' _queueManager._items VÀ Form1._items là CÙNG 1 List - AddItem đã thêm vào rồi, không Add lại lần nữa
                AddGridRow(it)
            Next
            lblOverallStatus.Text = "Đã thêm " & newItems.Count & " tệp vào hàng đợi."
        Else
            _items.AddRange(newItems)
            BuildGridRows(_items)
            SetRunningButtonsState(True)
            lblOverallStatus.Text = "Đang chuẩn bị tải..."
            progressOverall.Value = 0

            Dim statePath As String = GetQueueStatePath()
            _queueManager = New DownloadQueueManager(_segments, _concurrent)
            WireQueueManagerEvents()
            _queueManager.Start(_items, statePath)
            uiTimer.Enabled = True
        End If
    End Sub

    ''' <summary>"Tải sau": thêm tệp vào danh sách ở trạng thái Tạm dừng, không tự chạy - người dùng
    ''' tự bấm "Tiếp tục" (từng dòng hoặc "Tiếp tục tất cả") khi muốn tải.</summary>
    Private Sub AddItemDeferredToQueue(item As DownloadItem)
        If _items Is Nothing Then _items = New List(Of DownloadItem)

        If _queueManager IsNot Nothing AndAlso _queueManager.IsBusy Then
            _queueManager.AddItemDeferred(item)
            AddGridRow(item)
        Else
            item.Status = DownloadStatus.Paused
            _items.Add(item)
            BuildGridRows(_items)
        End If

        SetRunningButtonsState(_queueManager IsNot Nothing AndAlso _queueManager.IsBusy)
        lblOverallStatus.Text = "Đã thêm """ & item.Data.FileName & """ - chờ tải (bấm ""Tiếp tục"" khi sẵn sàng)."
    End Sub

    Private Sub PauseAllDownloads()
        If _queueManager IsNot Nothing Then
            toolBtnPauseAll.Enabled = False
            miPauseAll.Enabled = False
            lblOverallStatus.Text = "Đang tạm dừng, chờ các tệp đang tải dừng đúng chỗ..."
            _queueManager.PauseAll()
        End If
    End Sub

    Private Sub ResumeAllDownloads()
        If _items Is Nothing OrElse _items.Count = 0 Then
            MessageBox.Show("Không có tệp nào để tiếp tục.")
            Return
        End If
        If _queueManager IsNot Nothing AndAlso _queueManager.IsBusy Then Return

        SetRunningButtonsState(True)
        Dim statePath As String = GetQueueStatePath()
        _queueManager = New DownloadQueueManager(_segments, _concurrent)
        WireQueueManagerEvents()
        _queueManager.Resume(_items, statePath)
        uiTimer.Enabled = True
    End Sub

    Private Sub CancelAllDownloads()
        If _queueManager IsNot Nothing AndAlso _queueManager.IsBusy Then
            _queueManager.CancelAll()
        Else
            If _items IsNot Nothing AndAlso _items.Count > 0 Then
                Dim confirm As DialogResult = MessageBox.Show("Xoá toàn bộ danh sách tải hiện tại?", "Xác nhận",
                                                                MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                If confirm <> DialogResult.Yes Then Return
            End If

            DownloadQueueState.Delete(GetQueueStatePath())
            lvGrid.Items.Clear()
            _items = Nothing
            uiTimer.Enabled = False
            SetRunningButtonsState(False)
            lblOverallStatus.Text = "Đã xoá danh sách tải."
            progressOverall.Value = 0
        End If
    End Sub

    Private Sub SetRunningButtonsState(isRunning As Boolean)
        toolBtnPauseAll.Enabled = isRunning
        miPauseAll.Enabled = isRunning

        Dim canResume As Boolean = Not isRunning AndAlso HasResumableItems()
        toolBtnResumeAll.Enabled = canResume
        miResumeAll.Enabled = canResume

        Dim canCancel As Boolean = isRunning OrElse (_items IsNot Nothing AndAlso _items.Count > 0)
        toolBtnCancelAll.Enabled = canCancel
        miCancelAll.Enabled = canCancel
    End Sub

    Private Sub WireQueueManagerEvents()
        AddHandler _queueManager.QueuePaused, AddressOf OnQueuePaused
        AddHandler _queueManager.AllCompleted, AddressOf OnAllCompleted
        AddHandler _queueManager.StateChanged, AddressOf OnStateChanged
    End Sub

    ' ------------------------------------------------------------------
    '  Sự kiện từ DownloadQueueManager
    ' ------------------------------------------------------------------

    Private Sub OnQueuePaused(remainingCount As Integer)
        If InvokeRequired Then
            BeginInvoke(New Action(Of Integer)(AddressOf OnQueuePaused), remainingCount)
            Return
        End If

        uiTimer.Enabled = False
        UpdateAllRows()
        SetRunningButtonsState(False)
        lblOverallStatus.Text = "Đã tạm dừng. Còn " & remainingCount & " tệp chưa tải xong - bấm ""Tiếp tục"" khi sẵn sàng."
    End Sub

    Private Sub OnAllCompleted(totalOk As Integer, totalFail As Integer, wasCancelled As Boolean)
        If InvokeRequired Then
            BeginInvoke(New Action(Of Integer, Integer, Boolean)(AddressOf OnAllCompleted), totalOk, totalFail, wasCancelled)
            Return
        End If

        uiTimer.Enabled = False
        UpdateAllRows()
        SetRunningButtonsState(False)

        If wasCancelled Then
            lblOverallStatus.Text = "Đã huỷ. " & totalOk & " tệp đã tải xong trước đó, " & totalFail & " lỗi/dang dở."
        Else
            lblOverallStatus.Text = "Hoàn tất: " & totalOk & " thành công, " & totalFail & " lỗi."
            MessageBox.Show("Đã tải xong." & vbNewLine & totalOk & " thành công, " & totalFail & " lỗi.")
        End If
    End Sub

    Private Sub OnStateChanged()
        Try
            If _queueManager IsNot Nothing Then _queueManager.SaveStateNow()
        Catch
        End Try
    End Sub

    ' ========================================================================
    '  CÁC HỘP THOẠI (menu Tệp)
    ' ========================================================================

    Private Sub ShowAddUrlDialog()
        Using dlg As New AddUrlDialog(_defaultDownloadFolder)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Dim newItems As New List(Of DownloadItem)
                For Each url As String In dlg.Urls
                    Try
                        Dim data As New FileDownloadData(url)
                        Dim it As New DownloadItem()
                        it.Data = data
                        it.LocalPath = data.GetLocalPathFlat(dlg.DestinationFolder)
                        newItems.Add(it)
                    Catch ex As Exception
                        ' URL khong hop le - bo qua dong nay
                    End Try
                Next

                If newItems.Count = 0 Then
                    MessageBox.Show("Không có URL hợp lệ nào.")
                    Return
                End If

                AddItemsToQueue(newItems)
            End If
        End Using
    End Sub

    Private Sub ShowCreateListDialog()
        Using dlg As New CreateListDialog()
            dlg.ShowDialog(Me)
        End Using
    End Sub

    Private Sub ShowDownloadFromListDialog()
        Using dlg As New DownloadFromListDialog(_defaultDownloadFolder)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                AddItemsToQueue(dlg.Items)
            End If
        End Using
    End Sub

    ' ========================================================================
    '  NHẬN LINK TỪ TRÌNH DUYỆT (BrowserBridgeServer)
    ' ========================================================================

    Private Sub TryStartBridge(port As Integer, silent As Boolean)
        StopBridgeServer()
        Try
            _bridgeServer = New BrowserBridgeServer()
            _bridgeServer.ExpectedToken = _bridgeToken
            AddHandler _bridgeServer.LinkReceived, AddressOf OnBrowserLinkReceived
            _bridgeServer.StartListening(port)
            _bridgeEnabled = True
            _bridgePort = port
            lblBridgeStatusBar.Text = "Trình duyệt: đang lắng nghe cổng " & port
            lblBridgeStatusBar.ForeColor = Color.SeaGreen
        Catch ex As Exception
            _bridgeEnabled = False
            lblBridgeStatusBar.Text = "Trình duyệt: đã tắt (lỗi mở cổng " & port & ")"
            lblBridgeStatusBar.ForeColor = Color.IndianRed
            If Not silent Then MessageBox.Show("Không bật được nhận link từ trình duyệt: " & ex.Message)
        End Try
    End Sub

    Private Sub StopBridgeServer()
        If _bridgeServer IsNot Nothing Then
            _bridgeServer.StopListening()
            _bridgeServer = Nothing
        End If
    End Sub

    ''' <summary>Bắn ra từ luồng nền của BrowserBridgeServer - phải marshal về luồng UI trước khi đụng control.</summary>
    Private Sub OnBrowserLinkReceived(sender As Object, e As BrowserLinkReceivedEventArgs)
        If InvokeRequired Then
            BeginInvoke(New EventHandler(Of BrowserLinkReceivedEventArgs)(AddressOf OnBrowserLinkReceived), sender, e)
            Return
        End If

        Try
            If e.Source = "manual" Then
                HandleManualBrowserLink(e)
            Else
                Dim data As New FileDownloadData(e.Url)
                Dim folder As String = Path.Combine(_defaultDownloadFolder, "TuTrinhDuyet")

                Dim it As New DownloadItem()
                it.Data = data
                it.LocalPath = data.GetLocalPathFlat(folder)
                it.Referer = e.Referer

                Dim newItems As New List(Of DownloadItem)
                newItems.Add(it)
                AddItemsToQueue(newItems)

                If Not Me.Visible OrElse Me.WindowState = FormWindowState.Minimized Then
                    _trayIcon.ShowBalloonTip(1500, "Đã nhận link mới", data.FileName, ToolTipIcon.Info)
                End If
            End If
        Catch ex As Exception
            ' URL khong hop le gui tu extension - bo qua, khong lam gian doan cac tai khac
        End Try
    End Sub

    ''' <summary>Link bắt THỦ CÔNG (chuột phải vào link / nút tải nổi trên trình duyệt) - hiện hộp
    ''' thoại xác nhận kiểu IDM (tên tệp/kích thước/nơi lưu + Tải sau/Tải xuống ngay/Huỷ) thay vì
    ''' tự động thêm thẳng vào hàng đợi, để người dùng biết chính xác sắp tải gì trước khi tải.</summary>
    Private Sub HandleManualBrowserLink(e As BrowserLinkReceivedEventArgs)
        ' Neu form dang an/thu nho, dua len truoc de nguoi dung thay hop thoai xac nhan hien ra
        If Not Me.Visible Then Me.Show()
        If Me.WindowState = FormWindowState.Minimized Then Me.WindowState = FormWindowState.Normal
        Me.Activate()

        Using dlg As New BrowserDownloadPromptDialog(e.Url, e.SuggestedFileName, e.Referer)
            Dim dr As DialogResult = dlg.ShowDialog(Me)
            If dr <> DialogResult.OK Then Return ' Huỷ hoặc đóng bằng nút X - bỏ qua, không thêm gì cả

            Dim data As New FileDownloadData(e.Url)
            Dim it As New DownloadItem()
            it.Data = data
            it.LocalPath = dlg.SaveFullPath
            it.Referer = e.Referer

            If dlg.ChosenResult = DownloadPromptResult.DownloadNow Then
                AddItemsToQueue(New List(Of DownloadItem) From {it})
            ElseIf dlg.ChosenResult = DownloadPromptResult.DownloadLater Then
                AddItemDeferredToQueue(it)
            End If
        End Using
    End Sub

    ' ========================================================================
    '  LƯỚI DANH SÁCH TẢI
    ' ========================================================================

    Private Sub BuildGridRows(items As List(Of DownloadItem))
        lvGrid.BeginUpdate()
        lvGrid.Items.Clear()
        For Each it As DownloadItem In items
            AddGridRow(it)
        Next
        lvGrid.EndUpdate()
    End Sub

    Private Sub AddGridRow(it As DownloadItem)
        Dim lvi As New ListViewItem(it.Data.FileName)
        lvi.Tag = it
        lvi.SubItems.Add(FormatBytes(it.TotalBytes))   ' COL_SIZE
        lvi.SubItems.Add("")                            ' COL_PROGRESS (vẽ tay)
        lvi.SubItems.Add("")                            ' COL_SPEED
        lvi.SubItems.Add("")                            ' COL_ETA
        lvi.SubItems.Add(StatusText(it.Status))         ' COL_STATUS
        lvGrid.Items.Add(lvi)
    End Sub

    Private Sub uiTimer_Tick(sender As Object, e As EventArgs) Handles uiTimer.Tick
        UpdateAllRows()

        _saveTickCounter += 1
        If _saveTickCounter >= 4 Then ' luu tien do ra dia moi ~2 giay khi dang tai
            _saveTickCounter = 0
            Try
                If _queueManager IsNot Nothing Then _queueManager.SaveStateNow()
            Catch
            End Try
        End If
    End Sub

    Private Sub UpdateAllRows()
        If lvGrid.Items.Count = 0 Then Return

        Dim nowTicks As Long = Environment.TickCount
        Dim totalDownloaded As Long = 0
        Dim totalKnownSize As Long = 0
        Dim totalSpeed As Double = 0
        Dim doneCount As Integer = 0
        Dim failCount As Integer = 0

        For Each lvi As ListViewItem In lvGrid.Items
            Dim it As DownloadItem = CType(lvi.Tag, DownloadItem)
            Dim downloaded As Long = it.DownloadedBytes

            If it.Status = DownloadStatus.Downloading Then
                If it.LastSampleTicks <> 0 Then
                    Dim dt As Double = (nowTicks - it.LastSampleTicks) / 1000.0R
                    If dt > 0 Then
                        Dim deltaBytes As Long = downloaded - it.LastSampleBytes
                        Dim instSpeed As Double = deltaBytes / dt
                        If instSpeed < 0 Then instSpeed = 0
                        If it.CurrentSpeedBps <= 0 Then
                            it.CurrentSpeedBps = instSpeed
                        Else
                            it.CurrentSpeedBps = (it.CurrentSpeedBps * 0.6R) + (instSpeed * 0.4R)
                        End If
                    End If
                End If
            Else
                it.CurrentSpeedBps = 0
            End If

            it.LastSampleBytes = downloaded
            it.LastSampleTicks = nowTicks

            UpdateRow(lvi, it, downloaded)

            totalDownloaded += downloaded
            If it.TotalBytes > 0 Then totalKnownSize += it.TotalBytes
            If it.Status = DownloadStatus.Downloading Then totalSpeed += it.CurrentSpeedBps
            If it.Status = DownloadStatus.Completed Then doneCount += 1
            If it.Status = DownloadStatus.Failed OrElse it.Status = DownloadStatus.Cancelled Then failCount += 1
        Next

        lvGrid.Invalidate()
        UpdateStatusBar(totalDownloaded, totalKnownSize, totalSpeed, doneCount, failCount)
    End Sub

    Private Sub UpdateRow(lvi As ListViewItem, it As DownloadItem, downloaded As Long)
        lvi.SubItems(COL_SIZE).Text = If(it.TotalBytes > 0, FormatBytes(it.TotalBytes), "—")

        If it.Status = DownloadStatus.Downloading Then
            lvi.SubItems(COL_SPEED).Text = FormatSpeed(it.CurrentSpeedBps)
            If it.TotalBytes > 0 AndAlso it.CurrentSpeedBps > 0 Then
                Dim remain As Long = it.TotalBytes - downloaded
                lvi.SubItems(COL_ETA).Text = FormatEta(Math.Max(0L, remain) / it.CurrentSpeedBps)
            Else
                lvi.SubItems(COL_ETA).Text = "—"
            End If
        Else
            lvi.SubItems(COL_SPEED).Text = ""
            lvi.SubItems(COL_ETA).Text = ""
        End If

        lvi.SubItems(COL_STATUS).Text = StatusText(it.Status)
        lvi.SubItems(COL_PROGRESS).Text = ComputePercent(it) & "%"
    End Sub

    Private Sub UpdateStatusBar(totalDownloaded As Long, totalKnownSize As Long, totalSpeed As Double, doneCount As Integer, failCount As Integer)
        Dim totalCount As Integer = lvGrid.Items.Count

        Dim overallPct As Integer
        If totalKnownSize > 0 Then
            overallPct = CInt(Math.Min(100L, (totalDownloaded * 100L) \ totalKnownSize))
        Else
            overallPct = PercentOf(doneCount, Math.Max(totalCount, 1))
        End If
        progressOverall.Value = Math.Max(0, Math.Min(100, overallPct))

        Dim etaText As String = "—"
        If totalSpeed > 0 AndAlso totalKnownSize > 0 Then
            Dim remain As Long = totalKnownSize - totalDownloaded
            If remain > 0 Then etaText = FormatEta(remain / totalSpeed)
        End If

        Dim failText As String = ""
        If failCount > 0 Then failText = " (" & failCount & " lỗi)"

        Dim sizeText As String = ""
        If totalKnownSize > 0 Then sizeText = " / " & FormatBytes(totalKnownSize)

        lblOverallStatus.Text = doneCount & "/" & totalCount & " tệp xong" & failText &
            "  •  Tốc độ: " & FormatSpeed(totalSpeed) &
            "  •  Đã tải: " & FormatBytes(totalDownloaded) & sizeText &
            "  •  Còn lại: " & etaText
    End Sub

    ' ------------------------------------------------------------------
    '  Định dạng hiển thị
    ' ------------------------------------------------------------------

    Friend Shared Function FormatBytes(bytes As Long) As String
        If bytes < 0 Then Return "—"
        If bytes >= 1024L * 1024L * 1024L Then
            Return String.Format("{0:0.00} GB", bytes / 1024.0R / 1024.0R / 1024.0R)
        ElseIf bytes >= 1024L * 1024L Then
            Return String.Format("{0:0.00} MB", bytes / 1024.0R / 1024.0R)
        ElseIf bytes >= 1024L Then
            Return String.Format("{0:0.0} KB", bytes / 1024.0R)
        Else
            Return bytes & " B"
        End If
    End Function

    Private Shared Function FormatSpeed(bytesPerSec As Double) As String
        If bytesPerSec <= 0 Then Return "0 KB/s"
        If bytesPerSec >= 1024.0R * 1024.0R Then
            Return String.Format("{0:0.00} MB/s", bytesPerSec / 1024.0R / 1024.0R)
        Else
            Return String.Format("{0:0.0} KB/s", bytesPerSec / 1024.0R)
        End If
    End Function

    Private Shared Function FormatEta(seconds As Double) As String
        If Double.IsNaN(seconds) OrElse Double.IsInfinity(seconds) OrElse seconds < 0 Then Return "—"
        If seconds > 359999 Then Return "—"

        Dim total As Integer = CInt(Math.Ceiling(seconds))
        Dim h As Integer = total \ 3600
        Dim m As Integer = (total Mod 3600) \ 60
        Dim s As Integer = total Mod 60

        If h > 0 Then
            Return String.Format("{0}:{1:00}:{2:00}", h, m, s)
        Else
            Return String.Format("{0:00}:{1:00}", m, s)
        End If
    End Function

    Private Shared Function StatusText(status As DownloadStatus) As String
        Select Case status
            Case DownloadStatus.Pending : Return "Đang chờ"
            Case DownloadStatus.Downloading : Return "Đang tải"
            Case DownloadStatus.Paused : Return "Tạm dừng"
            Case DownloadStatus.Completed : Return "Hoàn tất"
            Case DownloadStatus.Failed : Return "Lỗi"
            Case DownloadStatus.Cancelled : Return "Đã huỷ"
            Case Else : Return ""
        End Select
    End Function

    Private Shared Function ProgressColorFor(status As DownloadStatus) As Color
        Select Case status
            Case DownloadStatus.Downloading : Return Color.SteelBlue
            Case DownloadStatus.Completed : Return Color.SeaGreen
            Case DownloadStatus.Failed : Return Color.IndianRed
            Case DownloadStatus.Cancelled : Return Color.Gray
            Case DownloadStatus.Paused : Return Color.DarkOrange
            Case Else : Return Color.LightGray
        End Select
    End Function

    Private Shared Function ComputePercent(it As DownloadItem) As Integer
        If it.Status = DownloadStatus.Completed Then Return 100
        If it.TotalBytes > 0 Then
            Dim pct As Integer = CInt(Math.Min(100L, (it.DownloadedBytes * 100L) \ it.TotalBytes))
            Return Math.Max(0, pct)
        End If
        Return 0
    End Function

    Private Shared Function PercentOf(value As Integer, total As Integer) As Integer
        If total <= 0 Then Return 0
        Return CInt(Math.Truncate((value / CDbl(total)) * 100.0R))
    End Function

    ' ------------------------------------------------------------------
    '  Vẽ tay cột "Tiến độ" thành thanh progress bar mini trong lưới (kiểu IDM)
    ' ------------------------------------------------------------------

    Private Sub lvGrid_DrawColumnHeader(sender As Object, e As DrawListViewColumnHeaderEventArgs) Handles lvGrid.DrawColumnHeader
        e.DrawDefault = True
    End Sub

    Private Sub lvGrid_DrawItem(sender As Object, e As DrawListViewItemEventArgs) Handles lvGrid.DrawItem
        ' Bat buoc phai co handler nay khi OwnerDraw=True, viec ve thuc su nam o DrawSubItem.
    End Sub

    Private Sub lvGrid_DrawSubItem(sender As Object, e As DrawListViewSubItemEventArgs) Handles lvGrid.DrawSubItem
        If e.ColumnIndex <> COL_PROGRESS Then
            e.DrawDefault = True
            Return
        End If

        Dim it As DownloadItem = CType(e.Item.Tag, DownloadItem)
        Dim pct As Integer = ComputePercent(it)

        Dim backColor As Color = If(e.Item.Selected, SystemColors.Highlight, Color.White)
        Using backBrush As New SolidBrush(backColor)
            e.Graphics.FillRectangle(backBrush, e.Bounds)
        End Using

        Dim barRect As New Rectangle(e.Bounds.X + 2, e.Bounds.Y + 3, e.Bounds.Width - 4, e.Bounds.Height - 6)
        e.Graphics.DrawRectangle(Pens.Gray, barRect)

        Dim fillWidth As Integer = CInt((barRect.Width - 2) * (pct / 100.0R))
        If fillWidth > 0 Then
            Dim fillRect As New Rectangle(barRect.X + 1, barRect.Y + 1, fillWidth, barRect.Height - 2)
            Using fillBrush As New SolidBrush(ProgressColorFor(it.Status))
                e.Graphics.FillRectangle(fillBrush, fillRect)
            End Using
        End If

        Dim text As String = pct & "%"
        Dim textSize As SizeF = e.Graphics.MeasureString(text, e.Item.ListView.Font)
        Dim textLoc As New PointF(barRect.X + (barRect.Width - textSize.Width) / 2.0F,
                                   barRect.Y + (barRect.Height - textSize.Height) / 2.0F)
        e.Graphics.DrawString(text, e.Item.ListView.Font, Brushes.Black, textLoc)
    End Sub

    ' ------------------------------------------------------------------
    '  Menu chuột phải trên từng dòng (Tạm dừng / Tiếp tục / Huỷ riêng từng tệp)
    ' ------------------------------------------------------------------

    Private Sub ctxPause_Click(sender As Object, e As EventArgs)
        For Each lvi As ListViewItem In lvGrid.SelectedItems
            Dim it As DownloadItem = CType(lvi.Tag, DownloadItem)
            If _queueManager IsNot Nothing Then _queueManager.PauseItem(it)
        Next
    End Sub

    Private Sub ctxResume_Click(sender As Object, e As EventArgs)
        If _queueManager Is Nothing OrElse Not _queueManager.IsBusy Then
            ResumeAllDownloads()
            Return
        End If

        For Each lvi As ListViewItem In lvGrid.SelectedItems
            Dim it As DownloadItem = CType(lvi.Tag, DownloadItem)
            _queueManager.ResumeItem(it)
        Next
    End Sub

    Private Sub ctxCancel_Click(sender As Object, e As EventArgs)
        For Each lvi As ListViewItem In lvGrid.SelectedItems
            Dim it As DownloadItem = CType(lvi.Tag, DownloadItem)
            If _queueManager IsNot Nothing Then
                _queueManager.CancelItem(it)
            Else
                it.Status = DownloadStatus.Cancelled
            End If
        Next
        UpdateAllRows()
    End Sub

    Private Sub ctxOpenFolder_Click(sender As Object, e As EventArgs)
        If lvGrid.SelectedItems.Count = 0 Then Return
        Try
            Dim it As DownloadItem = CType(lvGrid.SelectedItems(0).Tag, DownloadItem)
            If File.Exists(it.LocalPath) Then
                Process.Start("explorer.exe", "/select," & it.LocalPath)
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub ctxCopyUrl_Click(sender As Object, e As EventArgs)
        If lvGrid.SelectedItems.Count = 0 Then Return
        Dim urls As New List(Of String)
        For Each lvi As ListViewItem In lvGrid.SelectedItems
            Dim it As DownloadItem = CType(lvi.Tag, DownloadItem)
            urls.Add(it.Data.Url)
        Next
        Try
            Clipboard.SetText(String.Join(Environment.NewLine, urls.ToArray()))
        Catch ex As Exception
        End Try
    End Sub

    Private Sub ctxViewError_Click(sender As Object, e As EventArgs)
        If lvGrid.SelectedItems.Count = 0 Then Return
        Dim it As DownloadItem = CType(lvGrid.SelectedItems(0).Tag, DownloadItem)
        Dim msg As String = If(String.IsNullOrEmpty(it.LastError), "(Chưa có lỗi nào được ghi nhận cho tệp này.)", it.LastError)
        MessageBox.Show(msg, "Chi tiết lỗi - " & it.Data.FileName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

    ''' <summary>Xoá tệp đã tải xuống trên đĩa (giữ nguyên dòng trong danh sách, đưa về trạng thái
    ''' Chờ tải để có thể tải lại từ đầu). Bỏ qua các dòng đang tải dở để tránh xoá tệp đang được ghi.</summary>
    Private Sub ctxDeleteFile_Click(sender As Object, e As EventArgs)
        If lvGrid.SelectedItems.Count = 0 Then Return

        Dim targets As New List(Of DownloadItem)
        Dim skippedDownloading As Integer = 0
        For Each lvi As ListViewItem In lvGrid.SelectedItems
            Dim it As DownloadItem = CType(lvi.Tag, DownloadItem)
            If it.Status = DownloadStatus.Downloading Then
                skippedDownloading += 1
            Else
                targets.Add(it)
            End If
        Next

        If targets.Count = 0 Then
            MessageBox.Show("Không thể xoá tệp đang tải dở - hãy tạm dừng trước.")
            Return
        End If

        Dim confirm As DialogResult = MessageBox.Show(
            "Xoá " & targets.Count & " tệp đã tải xuống khỏi đĩa? (Dòng vẫn còn trong danh sách để tải lại nếu muốn.)",
            "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If confirm <> DialogResult.Yes Then Return

        Dim failedNames As New List(Of String)
        For Each it As DownloadItem In targets
            Try
                If File.Exists(it.LocalPath) Then File.Delete(it.LocalPath)
                it.Segments = New List(Of DownloadSegment)
                it.TotalBytes = -1
                it.Status = DownloadStatus.Pending
            Catch ex As Exception
                failedNames.Add(it.Data.FileName & " (" & ex.Message & ")")
            End Try
        Next

        UpdateAllRows()
        Try
            If _queueManager IsNot Nothing Then _queueManager.SaveStateNow()
        Catch
        End Try

        If skippedDownloading > 0 OrElse failedNames.Count > 0 Then
            Dim msg As String = ""
            If skippedDownloading > 0 Then msg &= skippedDownloading & " tệp đang tải dở đã bị bỏ qua." & Environment.NewLine
            If failedNames.Count > 0 Then msg &= "Không xoá được: " & String.Join(", ", failedNames.ToArray())
            MessageBox.Show(msg)
        End If
    End Sub

    ''' <summary>Xoá hẳn dòng khỏi danh sách (khác Huỷ - dòng biến mất hoàn toàn, không chỉ đổi trạng
    ''' thái). Tệp đã tải trên đĩa (nếu có) KHÔNG bị xoá, chỉ gỡ khỏi hàng đợi/lưới.</summary>
    Private Sub ctxRemoveFromList_Click(sender As Object, e As EventArgs)
        If lvGrid.SelectedItems.Count = 0 Then Return

        Dim confirm As DialogResult = MessageBox.Show(
            "Xoá " & lvGrid.SelectedItems.Count & " dòng khỏi danh sách? (Tệp đã tải trên đĩa, nếu có, sẽ KHÔNG bị xoá.)",
            "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If confirm <> DialogResult.Yes Then Return

        Dim rows As New List(Of ListViewItem)
        For Each lvi As ListViewItem In lvGrid.SelectedItems
            rows.Add(lvi)
        Next

        For Each lvi As ListViewItem In rows
            Dim it As DownloadItem = CType(lvi.Tag, DownloadItem)
            If _queueManager IsNot Nothing Then
                _queueManager.CancelItem(it)
                _queueManager.RemoveItem(it)
            ElseIf _items IsNot Nothing Then
                _items.Remove(it)
            End If
            lvGrid.Items.Remove(lvi)
        Next

        UpdateAllRows()
        Try
            If _queueManager IsNot Nothing Then _queueManager.SaveStateNow()
        Catch
        End Try
    End Sub

    ' ------------------------------------------------------------------
    '  Khay hệ thống (system tray)
    ' ------------------------------------------------------------------

    Private Sub TrayShow_Click(sender As Object, e As EventArgs)
        Me.Show()
        Me.WindowState = FormWindowState.Normal
        Me.Activate()
        _trayIcon.Visible = False
    End Sub

    Private Sub TrayExit_Click(sender As Object, e As EventArgs)
        _trayIcon.Visible = False
        Application.Exit()
    End Sub

    ' ========================================================================
    '  DỰNG GIAO DIỆN BẰNG CODE (thay cho Form1.Designer.vb)
    ' ========================================================================

    Private Sub InitializeComponent()
        Me.Text = "FileListDownloader - 2CongLC (kiểu IDM)"
        Me.ClientSize = New Size(980, 600)
        Me.MinimumSize = New Size(760, 420)
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MaximizeBox = True
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.Font = New Font("Segoe UI", 9.0F)

        ' ---- Lưới danh sách tải (nội dung chính, chiếm hết phần còn lại) ----
        lvGrid = New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True,
                                     .GridLines = True, .OwnerDraw = True, .MultiSelect = True, .HideSelection = False}
        lvGrid.Columns.Add("Tên tệp", 320)
        lvGrid.Columns.Add("Kích thước", 90)
        lvGrid.Columns.Add("Tiến độ", 150)
        lvGrid.Columns.Add("Tốc độ", 100)
        lvGrid.Columns.Add("Còn lại", 90)
        lvGrid.Columns.Add("Trạng thái", 100)

        ctxGrid = New ContextMenuStrip()
        Dim miPause As New ToolStripMenuItem("Tạm dừng")
        AddHandler miPause.Click, AddressOf ctxPause_Click
        Dim miResume As New ToolStripMenuItem("Tiếp tục")
        AddHandler miResume.Click, AddressOf ctxResume_Click
        Dim miCancel As New ToolStripMenuItem("Huỷ (xoá khỏi hàng đợi)")
        AddHandler miCancel.Click, AddressOf ctxCancel_Click
        Dim miOpenFolder As New ToolStripMenuItem("Mở thư mục chứa")
        AddHandler miOpenFolder.Click, AddressOf ctxOpenFolder_Click
        Dim miCopyUrl As New ToolStripMenuItem("Sao chép URL")
        AddHandler miCopyUrl.Click, AddressOf ctxCopyUrl_Click
        Dim miViewError As New ToolStripMenuItem("Xem lỗi")
        AddHandler miViewError.Click, AddressOf ctxViewError_Click
        Dim miDeleteFile As New ToolStripMenuItem("Xoá tệp đã tải xuống")
        AddHandler miDeleteFile.Click, AddressOf ctxDeleteFile_Click
        Dim miRemoveFromList As New ToolStripMenuItem("Xoá khỏi danh sách")
        AddHandler miRemoveFromList.Click, AddressOf ctxRemoveFromList_Click
        ctxGrid.Items.AddRange(New ToolStripItem() {miPause, miResume, miCancel, New ToolStripSeparator(), miOpenFolder, miCopyUrl, miViewError, New ToolStripSeparator(), miDeleteFile, miRemoveFromList})
        lvGrid.ContextMenuStrip = ctxGrid

        ' ---- Thanh trạng thái dưới cùng ----
        Dim bottomPanel As New Panel With {.Dock = DockStyle.Bottom, .Height = 56, .Padding = New Padding(8, 4, 8, 4)}

        Dim statusRow As New Panel With {.Dock = DockStyle.Top, .Height = 22}
        lblOverallStatus = New Label With {.Text = "Sẵn sàng.", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft}
        lblBridgeStatusBar = New Label With {.Text = "Trình duyệt: đã tắt", .Dock = DockStyle.Right, .Width = 320,
                                              .TextAlign = ContentAlignment.MiddleRight, .ForeColor = Color.Gray}
        statusRow.Controls.Add(lblOverallStatus)
        statusRow.Controls.Add(lblBridgeStatusBar)

        progressOverall = New ProgressBar With {.Dock = DockStyle.Top, .Height = 16, .Minimum = 0, .Maximum = 100}

        bottomPanel.Controls.Add(progressOverall)
        bottomPanel.Controls.Add(statusRow)

        ' ---- Toolbar ----
        toolStrip = New ToolStrip With {.Dock = DockStyle.Top, .GripStyle = ToolStripGripStyle.Hidden}

        toolBtnAddUrl = New ToolStripButton With {.Text = "Thêm URL", .DisplayStyle = ToolStripItemDisplayStyle.Text}
        AddHandler toolBtnAddUrl.Click, Sub(s, e) ShowAddUrlDialog()

        toolBtnPauseAll = New ToolStripButton With {.Text = "Tạm dừng tất cả", .DisplayStyle = ToolStripItemDisplayStyle.Text, .Enabled = False}
        AddHandler toolBtnPauseAll.Click, Sub(s, e) PauseAllDownloads()

        toolBtnResumeAll = New ToolStripButton With {.Text = "Tiếp tục", .DisplayStyle = ToolStripItemDisplayStyle.Text, .Enabled = False}
        AddHandler toolBtnResumeAll.Click, Sub(s, e) ResumeAllDownloads()

        toolBtnCancelAll = New ToolStripButton With {.Text = "Huỷ tất cả", .DisplayStyle = ToolStripItemDisplayStyle.Text, .Enabled = False}
        AddHandler toolBtnCancelAll.Click, Sub(s, e) CancelAllDownloads()

        toolStrip.Items.AddRange(New ToolStripItem() {toolBtnAddUrl, New ToolStripSeparator(),
                                   toolBtnPauseAll, toolBtnResumeAll, toolBtnCancelAll})

        ' ---- Menu ----
        menuStrip = New MenuStrip()

        Dim menuFile As New ToolStripMenuItem("Tệp")
        Dim itAddUrl As New ToolStripMenuItem("Thêm URL để tải...")
        AddHandler itAddUrl.Click, Sub(s, e) ShowAddUrlDialog()
        Dim itCreateList As New ToolStripMenuItem("Tạo danh sách liên kết từ thư mục...")
        AddHandler itCreateList.Click, Sub(s, e) ShowCreateListDialog()
        Dim itDownloadList As New ToolStripMenuItem("Tải theo danh sách có sẵn...")
        AddHandler itDownloadList.Click, Sub(s, e) ShowDownloadFromListDialog()
        Dim itExit As New ToolStripMenuItem("Thoát")
        AddHandler itExit.Click, Sub(s, e) Me.Close()
        menuFile.DropDownItems.AddRange(New ToolStripItem() {itAddUrl, New ToolStripSeparator(),
                                          itCreateList, itDownloadList, New ToolStripSeparator(), itExit})

        Dim menuDownloads As New ToolStripMenuItem("Tải xuống")
        miPauseAll = New ToolStripMenuItem("Tạm dừng tất cả") With {.Enabled = False}
        AddHandler miPauseAll.Click, Sub(s, e) PauseAllDownloads()
        miResumeAll = New ToolStripMenuItem("Tiếp tục") With {.Enabled = False}
        AddHandler miResumeAll.Click, Sub(s, e) ResumeAllDownloads()
        miCancelAll = New ToolStripMenuItem("Huỷ tất cả") With {.Enabled = False}
        AddHandler miCancelAll.Click, Sub(s, e) CancelAllDownloads()
        menuDownloads.DropDownItems.AddRange(New ToolStripItem() {miPauseAll, miResumeAll, miCancelAll})

        Dim menuOptions As New ToolStripMenuItem("Tuỳ chọn")
        Dim itSettings As New ToolStripMenuItem("Cài đặt...")
        AddHandler itSettings.Click, Sub(s, e) ShowSettingsDialog()
        menuOptions.DropDownItems.Add(itSettings)

        Dim menuHelp As New ToolStripMenuItem("Trợ giúp")
        Dim itAbout As New ToolStripMenuItem("Giới thiệu")
        AddHandler itAbout.Click, Sub(s, e) MessageBox.Show(
            "FileListDownloader (kiểu IDM) - 2CongLC" & vbNewLine & vbNewLine &
            "Tạo danh sách liên kết, tải đa luồng nhiều tệp cùng lúc, nhận link trực tiếp từ trình duyệt.",
            "Giới thiệu")
        menuHelp.DropDownItems.Add(itAbout)

        menuStrip.Items.AddRange(New ToolStripItem() {menuFile, menuDownloads, menuOptions, menuHelp})
        Me.MainMenuStrip = menuStrip

        ' ---- Ghép layout: thêm Fill trước, rồi Bottom, rồi Top (ToolStrip trước, MenuStrip sau
        ' cùng để nằm trên cùng - control Dock cùng cạnh thêm SAU sẽ nằm sát cạnh đó hơn) ----
        Me.Controls.Add(lvGrid)
        Me.Controls.Add(bottomPanel)
        Me.Controls.Add(toolStrip)
        Me.Controls.Add(menuStrip)

        ' ---- Timer cập nhật lưới ----
        uiTimer = New Timer With {.Interval = 500, .Enabled = False}

        ' ---- Khay hệ thống ----
        _trayIcon = New NotifyIcon()
        _trayIcon.Icon = SystemIcons.Application
        _trayIcon.Text = "FileListDownloader - 2CongLC"
        _trayIcon.Visible = False

        Dim trayMenu As New ContextMenuStrip()
        Dim miShow As New ToolStripMenuItem("Hiện cửa sổ")
        AddHandler miShow.Click, AddressOf TrayShow_Click
        Dim miTrayExit As New ToolStripMenuItem("Thoát")
        AddHandler miTrayExit.Click, AddressOf TrayExit_Click
        trayMenu.Items.AddRange(New ToolStripItem() {miShow, miTrayExit})
        _trayIcon.ContextMenuStrip = trayMenu
        AddHandler _trayIcon.DoubleClick, AddressOf TrayShow_Click
    End Sub

End Class
