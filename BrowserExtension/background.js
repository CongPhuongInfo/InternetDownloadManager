// Service worker (Manifest V3). Gửi link tải sang FileListDownloader qua HTTP nội bộ
// (127.0.0.1:port) - app phải đang chạy và đã bật "Nhận link từ trình duyệt".

const DEFAULT_PORT = 39215;

function getSettings() {
  return new Promise((resolve) => {
    chrome.storage.local.get({ port: DEFAULT_PORT, autoCapture: false }, (items) => resolve(items));
  });
}

function appBaseUrl(port) {
  return `http://127.0.0.1:${port}`;
}

async function sendToApp(url, filename) {
  const { port } = await getSettings();
  try {
    const resp = await fetch(`${appBaseUrl(port)}/add`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ url, filename: filename || "" })
    });
    return resp.ok;
  } catch (e) {
    return false;
  }
}

function notifyFail() {
  chrome.notifications.create({
    type: "basic",
    iconUrl: "icon48.png",
    title: "Không gửi được link",
    message: "Không kết nối được tới FileListDownloader. Hãy mở chương trình và bật \"Nhận link từ trình duyệt\"."
  });
}

// ---- Menu chuột phải trên link: gửi thủ công 1 link ----
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "send-to-fld",
    title: "Tải bằng FileListDownloader",
    contexts: ["link"]
  });
});

chrome.contextMenus.onClicked.addListener(async (info) => {
  if (info.menuItemId === "send-to-fld" && info.linkUrl) {
    const ok = await sendToApp(info.linkUrl, "");
    if (!ok) notifyFail();
  }
});

// ---- Tự động bắt mọi lượt tải xuống của trình duyệt (tuỳ chọn, mặc định TẮT) ----
// Khi bật trong popup: mỗi khi Chrome/Edge chuẩn bị tải 1 tệp, huỷ tải mặc định của
// trình duyệt và gửi URL đó sang FileListDownloader để tải đa luồng thay thế.
chrome.downloads.onCreated.addListener(async (downloadItem) => {
  const { autoCapture } = await getSettings();
  if (!autoCapture) return;
  if (!downloadItem.url || downloadItem.url.indexOf("http") !== 0) return;

  const ok = await sendToApp(downloadItem.url, downloadItem.filename || "");
  if (ok) {
    try {
      await chrome.downloads.cancel(downloadItem.id);
      await chrome.downloads.erase({ id: downloadItem.id });
    } catch (e) {
      // Khong huy duoc thi thoi, de trinh duyet tu tai binh thuong.
    }
  }
});
