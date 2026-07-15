// Service worker (Manifest V3). Gửi link tải sang FileListDownloader qua HTTP nội bộ
// (127.0.0.1:port) - app phải đang chạy và đã bật "Nhận link từ trình duyệt".

const DEFAULT_PORT = 39215;
const DEFAULT_MIN_SIZE_KB = 512;
const DEFAULT_EXT_WHITELIST =
  "rar,zip,7z,tar,gz,exe,msi,iso,apk,dmg,pkg,pdf,mp4,mkv,avi,mov,wmv,mp3,flac,m3u8,ts,m4a";

function getSettings() {
  return new Promise((resolve) => {
    chrome.storage.local.get(
      {
        port: DEFAULT_PORT,
        autoCapture: false,
        token: "",
        minSizeKB: DEFAULT_MIN_SIZE_KB,
        extWhitelist: DEFAULT_EXT_WHITELIST
      },
      (items) => resolve(items)
    );
  });
}

function appBaseUrl(port) {
  return `http://127.0.0.1:${port}`;
}

function extOf(urlOrName) {
  try {
    const noQuery = urlOrName.split("?")[0].split("#")[0];
    const last = noQuery.split("/").pop() || "";
    const dot = last.lastIndexOf(".");
    return dot >= 0 ? last.substring(dot + 1).toLowerCase() : "";
  } catch (e) {
    return "";
  }
}

// ---- Lấy Cookie của phiên trình duyệt cho 1 URL, dùng cho link cần đăng nhập/session ----
// Cần quyền "cookies" + quyền host cho origin đó (đã xin chung với nút tải nổi). Nếu chưa được
// cấp quyền hoặc origin không hợp lệ thì trả về chuỗi rỗng - im lặng bỏ qua, không phải lỗi thật
// vì phần lớn link (rar/zip/mp4 tĩnh...) vốn không cần cookie cũng tải được bình thường.
async function getCookieHeaderForUrl(url) {
  try {
    const cookies = await chrome.cookies.getAll({ url });
    if (!cookies || cookies.length === 0) return "";
    return cookies.map((c) => `${c.name}=${c.value}`).join("; ");
  } catch (e) {
    return "";
  }
}

// ---- Gửi 1 link ----
async function sendToApp(url, filename, referer, source) {
  const { port, token } = await getSettings();
  const cookie = await getCookieHeaderForUrl(url);
  try {
    const resp = await fetch(`${appBaseUrl(port)}/add`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-FLD-Token": token || "" },
      body: JSON.stringify({ url, filename: filename || "", referer: referer || "", source: source || "manual", cookie })
    });
    return resp.ok;
  } catch (e) {
    return false;
  }
}

// ---- Gửi nhiều link cùng lúc (quét link trên trang) ----
async function sendBatchToApp(items, referer) {
  const { port, token } = await getSettings();
  const withCookies = await Promise.all(
    items.map(async (it) => ({ url: it.url, filename: it.filename || "", referer: referer || "", cookie: await getCookieHeaderForUrl(it.url) }))
  );
  try {
    const resp = await fetch(`${appBaseUrl(port)}/add-batch`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-FLD-Token": token || "" },
      body: JSON.stringify({ urls: withCookies })
    });
    if (!resp.ok) return { ok: false, added: 0 };
    const data = await resp.json();
    return { ok: true, added: data.added || 0 };
  } catch (e) {
    return { ok: false, added: 0 };
  }
}

function notify(title, message) {
  chrome.notifications.create({
    type: "basic",
    iconUrl: "icon48.png",
    title,
    message
  });
}

const FLOATER_ORIGINS = ["http://*/*", "https://*/*"];
const FLOATER_SCRIPT_ID = "fld-floater";

async function hasFloaterPermission() {
  try {
    return await chrome.permissions.contains({ origins: FLOATER_ORIGINS });
  } catch (e) {
    return false;
  }
}

// ---- Bật nút tải nổi: xin quyền đọc nội dung trang (nếu chưa có), đăng ký content script cho
// các trang mở sau này, và tiêm luôn vào các tab đang mở sẵn để có hiệu lực ngay ----
async function enableFloater() {
  const granted = await chrome.permissions.request({ origins: FLOATER_ORIGINS });
  if (!granted) return false;

  try {
    await chrome.scripting.registerContentScripts([
      { id: FLOATER_SCRIPT_ID, matches: FLOATER_ORIGINS, js: ["content.js"], runAt: "document_idle" }
    ]);
  } catch (e) {
    // co the da dang ky tu truoc do (vd sau khi tat/bat lai) - bo qua loi trung id
  }

  try {
    const tabs = await chrome.tabs.query({ url: FLOATER_ORIGINS });
    for (const tab of tabs) {
      try {
        await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["content.js"] });
      } catch (e) {
        // mot so tab (chrome://, Chrome Web Store, ...) khong cho tiem - bo qua
      }
    }
  } catch (e) {}

  await chrome.storage.local.set({ floaterEnabled: true });
  return true;
}

async function disableFloater() {
  try {
    await chrome.scripting.unregisterContentScripts({ ids: [FLOATER_SCRIPT_ID] });
  } catch (e) {}
  await chrome.storage.local.set({ floaterEnabled: false });
  // Khong tu thu hoi quyen truy cap trang web o day de tranh phai xin lai moi lan bat/tat -
  // content.js tu kiem tra "floaterEnabled" qua storage.onChanged nen se tu an nut ngay.
}

(async () => {
  const granted = await hasFloaterPermission();
  const { floaterEnabled } = await chrome.storage.local.get({ floaterEnabled: false });
  if (granted && floaterEnabled) {
    try {
      await chrome.scripting.registerContentScripts([
        { id: FLOATER_SCRIPT_ID, matches: FLOATER_ORIGINS, js: ["content.js"], runAt: "document_idle" }
      ]);
    } catch (e) {}
  }
})();

// ---- Dò link media (video/audio/HLS) qua tầng network, KHÔNG chỉ đọc DOM <video>/<audio> ----
// Bắt được cả trường hợp JS giấu URL thật sau blob: hay MSE, vì request gốc tới CDN vẫn phải đi
// qua đây trước khi trình duyệt gói lại thành blob. Cần cùng quyền host đã xin cho nút tải nổi
// (FLOATER_ORIGINS) - webRequest chỉ "nhìn thấy" request trên các origin mà extension có quyền.
// Chỉ lưu TẠM trong bộ nhớ theo từng tab (mất khi tab đóng hoặc service worker khởi động lại) -
// không tự động gửi gì cả, người dùng phải mở popup và bấm "Gửi" cho từng link (giữ đúng nguyên
// tắc không "cướp" quyền chọn nguồn tải của người dùng).
const MEDIA_URL_RE = /\.(m3u8|mpd|mp4|webm|mp3|m4a|ts)(\?|#|$)/i;
const foundMediaByTab = new Map(); // tabId -> Map(url -> {url, contentType, filename})
const MAX_ENTRIES_PER_TAB = 60;

function extIsMediaLike(url, contentType) {
  if (contentType && /^(video|audio)\//i.test(contentType)) return true;
  if (contentType && /mpegurl|dash\+xml/i.test(contentType)) return true; // m3u8 / mpd content-type
  return MEDIA_URL_RE.test(url);
}

function registerFoundMedia(tabId, url, contentType) {
  if (tabId == null || tabId < 0) return;
  if (!foundMediaByTab.has(tabId)) foundMediaByTab.set(tabId, new Map());
  const bucket = foundMediaByTab.get(tabId);
  if (bucket.has(url)) return;
  if (bucket.size >= MAX_ENTRIES_PER_TAB) return;

  const clean = url.split("?")[0].split("#")[0];
  const filename = clean.split("/").pop() || "media";
  bucket.set(url, { url, contentType: contentType || "", filename });
}

try {
  chrome.webRequest.onHeadersReceived.addListener(
    (details) => {
      if (!details.responseHeaders) return;
      const ctHeader = details.responseHeaders.find((h) => h.name.toLowerCase() === "content-type");
      const contentType = ctHeader ? ctHeader.value : "";
      if (extIsMediaLike(details.url, contentType)) {
        registerFoundMedia(details.tabId, details.url, contentType);
      }
    },
    { urls: ["<all_urls>"], types: ["media", "xmlhttprequest", "other"] },
    ["responseHeaders"]
  );
} catch (e) {
  // Trinh duyet khong ho tro webRequest (hiem) hoac chua duoc cap quyen host - bo qua im lang,
  // tinh nang dan link thu cong / context menu / nut noi van hoat dong binh thuong.
}

chrome.tabs.onRemoved.addListener((tabId) => foundMediaByTab.delete(tabId));

// ---- Menu chuột phải trên link: gửi thủ công 1 link ----
// ---- Menu chuột phải trên trang: quét & gửi tất cả link tệp trên trang ----
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "send-to-fld",
    title: "Tải bằng FileListDownloader",
    contexts: ["link"]
  });
  chrome.contextMenus.create({
    id: "grab-page-links",
    title: "Quét & tải tất cả link tệp trên trang này",
    contexts: ["page"]
  });
  chrome.alarms.create("connCheck", { periodInMinutes: 0.5 });
  refreshBadge();
});

chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create("connCheck", { periodInMinutes: 0.5 });
  refreshBadge();
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === "send-to-fld" && info.linkUrl) {
    const ok = await sendToApp(info.linkUrl, "", info.pageUrl || "", "manual");
    if (!ok) notify("Không gửi được link", 'Không kết nối được tới FileListDownloader. Hãy mở chương trình, bật "Nhận link từ trình duyệt" và kiểm tra mã kết nối trong popup.');
    return;
  }
  if (info.menuItemId === "grab-page-links" && tab && tab.id) {
    await scanTabAndSend(tab);
  }
});

// ---- Quét toàn bộ thẻ <a href> khớp danh sách đuôi file cho phép, trong đúng tab đang mở ----
async function scanTabAndSend(tab) {
  const { extWhitelist } = await getSettings();
  const extList = extWhitelist.split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);

  let results;
  try {
    results = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: (allowedExts) => {
        const anchors = Array.from(document.querySelectorAll("a[href]"));
        const seen = new Set();
        const found = [];
        for (const a of anchors) {
          let href;
          try {
            href = new URL(a.href, document.baseURI).href;
          } catch (e) {
            continue;
          }
          if (seen.has(href)) continue;
          const clean = href.split("?")[0].split("#")[0];
          const last = clean.split("/").pop() || "";
          const dot = last.lastIndexOf(".");
          const ext = dot >= 0 ? last.substring(dot + 1).toLowerCase() : "";
          if (allowedExts.includes(ext)) {
            seen.add(href);
            found.push({ url: href, filename: last });
          }
        }
        return found;
      },
      args: [extList]
    });
  } catch (e) {
    notify("Không quét được trang", "Trình duyệt không cho phép quét trang này (vd trang nội bộ chrome://, cửa hàng Chrome Web Store).");
    return;
  }

  const found = (results && results[0] && results[0].result) || [];
  if (found.length === 0) {
    notify("Không tìm thấy link", "Không thấy link tệp nào khớp danh sách đuôi file đang cấu hình trên trang này.");
    return;
  }

  const { added, ok } = await sendBatchToApp(found, tab.url || "");
  if (!ok) {
    notify("Không gửi được", 'Không kết nối được tới FileListDownloader hoặc mã kết nối sai. Kiểm tra lại trong hộp thoại Cài đặt của ứng dụng.');
  } else {
    notify("Đã gửi " + added + " link", "Xem trong danh sách tải của FileListDownloader.");
  }
}

// ---- Tự động bắt lượt tải xuống của trình duyệt (tuỳ chọn, mặc định TẮT) ----
// Khi bật trong popup: mỗi khi Chrome/Edge chuẩn bị tải 1 tệp KHỚP danh sách đuôi file cho phép
// (và đủ lớn hơn ngưỡng cấu hình, nếu trình duyệt đã biết trước kích thước), huỷ tải mặc định
// của trình duyệt và gửi URL đó sang FileListDownloader để tải đa luồng thay thế. Các tệp nhỏ
// hoặc không thuộc danh sách đuôi file (ảnh, trang html, json, ...) vẫn để trình duyệt tự tải,
// tránh việc mọi cú click đều bị "cướp" sang app một cách khó chịu.
chrome.downloads.onCreated.addListener(async (downloadItem) => {
  const { autoCapture, minSizeKB, extWhitelist } = await getSettings();
  if (!autoCapture) return;
  if (!downloadItem.url || downloadItem.url.indexOf("http") !== 0) return;

  const ext = extOf(downloadItem.filename || downloadItem.url);
  const extList = extWhitelist.split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);
  if (extList.length > 0 && !extList.includes(ext)) return;

  const minBytes = (minSizeKB || 0) * 1024;
  if (typeof downloadItem.fileSize === "number" && downloadItem.fileSize >= 0 && downloadItem.fileSize < minBytes) return;

  const ok = await sendToApp(downloadItem.url, downloadItem.filename || "", downloadItem.referrer || "", "auto");
  if (ok) {
    try {
      await chrome.downloads.cancel(downloadItem.id);
      await chrome.downloads.erase({ id: downloadItem.id });
    } catch (e) {
      // Khong huy duoc thi thoi, de trinh duyet tu tai binh thuong.
    }
  }
});

// ---- Badge trên icon: xanh = đã kết nối & khớp mã, vàng = app chạy nhưng sai/thiếu mã, đỏ = không thấy app ----
async function refreshBadge() {
  const { port, token } = await getSettings();
  try {
    const resp = await fetch(`${appBaseUrl(port)}/ping`, { headers: { "X-FLD-Token": token || "" } });
    if (!resp.ok) throw new Error("bad status");
    const data = await resp.json();
    if (data.paired) {
      chrome.action.setBadgeText({ text: "" });
    } else {
      chrome.action.setBadgeText({ text: "!" });
      chrome.action.setBadgeBackgroundColor({ color: "#e69819" });
    }
  } catch (e) {
    chrome.action.setBadgeText({ text: "X" });
    chrome.action.setBadgeBackgroundColor({ color: "#c62828" });
  }
}

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "connCheck") refreshBadge();
});

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg && msg.action === "scanCurrentTab") {
    chrome.tabs.query({ active: true, currentWindow: true }, async (tabs) => {
      if (tabs && tabs[0]) await scanTabAndSend(tabs[0]);
      sendResponse({ done: true });
    });
    return true; // giữ kênh mở cho callback bất đồng bộ
  }
  if (msg && msg.action === "refreshBadge") {
    refreshBadge().then(() => sendResponse({ done: true }));
    return true;
  }
  if (msg && msg.action === "downloadUrl") {
    sendToApp(msg.url, msg.filename, msg.referer, "manual").then((ok) => {
      if (!ok) notify("Không gửi được link", 'Không kết nối được tới FileListDownloader hoặc sai mã kết nối.');
      sendResponse({ ok });
    });
    return true;
  }
  if (msg && msg.action === "enableFloater") {
    enableFloater().then((ok) => sendResponse({ ok }));
    return true;
  }
  if (msg && msg.action === "disableFloater") {
    disableFloater().then(() => sendResponse({ ok: true }));
    return true;
  }
  if (msg && msg.action === "getFoundMedia") {
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      const tab = tabs && tabs[0];
      const bucket = tab ? foundMediaByTab.get(tab.id) : null;
      const list = bucket ? Array.from(bucket.values()) : [];
      sendResponse({ list, pageUrl: tab ? tab.url : "" });
    });
    return true;
  }
  if (msg && msg.action === "sendFoundMedia") {
    chrome.tabs.query({ active: true, currentWindow: true }, async (tabs) => {
      const tab = tabs && tabs[0];
      const referer = (tab && tab.url) || "";
      const ok = await sendToApp(msg.url, msg.filename || "", referer, "manual");
      if (!ok) notify("Không gửi được link", 'Không kết nối được tới FileListDownloader hoặc sai mã kết nối.');
      sendResponse({ ok });
    });
    return true;
  }
  if (msg && msg.action === "getFloaterStatus") {
    (async () => {
      const granted = await hasFloaterPermission();
      const { floaterEnabled } = await chrome.storage.local.get({ floaterEnabled: false });
      sendResponse({ granted, enabled: granted && floaterEnabled });
    })();
    return true;
  }
});

refreshBadge();
