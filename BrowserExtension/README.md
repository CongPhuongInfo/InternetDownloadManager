# Extension "Gửi tới FileListDownloader"

Bắt liên kết tải xuống trên Chrome/Edge và gửi sang ứng dụng **FileListDownloader** (chạy trên
máy, cổng mặc định `39215`) để tải bằng cơ chế đa luồng thay vì trình duyệt tự tải.

## Cài đặt (chế độ Developer - chưa đăng ký lên Chrome Web Store)

1. Mở `chrome://extensions` (Chrome) hoặc `edge://extensions` (Edge).
2. Bật **Chế độ dành cho nhà phát triển / Developer mode** (góc trên phải).
3. Bấm **Load unpacked / Tải tiện ích đã giải nén**, chọn đúng thư mục `BrowserExtension` này.
4. Extension sẽ xuất hiện trên thanh công cụ.

## Cách dùng

1. Mở ứng dụng **FileListDownloader**, tick vào ô **"Nhận link từ trình duyệt"** (mặc định cổng `39215`).
2. Trên trình duyệt:
   - **Gửi thủ công**: chuột phải vào 1 liên kết bất kỳ → chọn **"Tải bằng FileListDownloader"**.
   - **Tự động bắt mọi lượt tải**: bấm icon extension → tick **"Tự động bắt mọi link tải xuống"**.
     Khi bật, mọi lượt tải trên trình duyệt sẽ bị huỷ và chuyển sang cho FileListDownloader tải
     đa luồng thay thế (tắt đi để trình duyệt tự tải như bình thường).
3. Bấm icon extension → **"Kiểm tra kết nối"** để chắc chắn extension gọi được tới ứng dụng.

## Vì sao cần đổi cổng?

Nếu cổng `39215` bị phần mềm khác chiếm dụng, đổi cổng trong ứng dụng (ô "Cổng" cạnh checkbox
"Nhận link từ trình duyệt") VÀ trong popup của extension (phải khớp nhau), rồi bấm "Lưu".

## Ghi chú kỹ thuật

- Extension chỉ gọi `http://127.0.0.1:<port>/...` (khai báo trong `host_permissions`), không gửi
  dữ liệu ra ngoài Internet.
- Giao thức rất đơn giản, không yêu cầu đăng ký Native Messaging Host - chỉ là 2 endpoint HTTP
  do chính ứng dụng VB.NET tự mở (`BrowserBridgeServer.vb`, dùng `System.Net.HttpListener`).
