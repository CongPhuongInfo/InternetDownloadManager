const DEFAULT_PORT = 39215;
const DEFAULT_MIN_SIZE_KB = 512;
const DEFAULT_EXT_WHITELIST =
  "rar,zip,7z,tar,gz,exe,msi,iso,apk,dmg,pkg,pdf,mp4,mkv,avi,mov,wmv,mp3,flac";

function load() {
  chrome.storage.local.get(
    {
      port: DEFAULT_PORT,
      autoCapture: false,
      token: "",
      minSizeKB: DEFAULT_MIN_SIZE_KB,
      extWhitelist: DEFAULT_EXT_WHITELIST
    },
    (items) => {
      document.getElementById("port").value = items.port;
      document.getElementById("autoCapture").checked = items.autoCapture;
      document.getElementById("token").value = items.token;
      document.getElementById("minSizeKB").value = items.minSizeKB;
      document.getElementById("extList").value = items.extWhitelist;
    }
  );
}

function setStatus(el, text, cls) {
  el.textContent = text;
  el.className = cls;
}

function save() {
  const port = parseInt(document.getElementById("port").value, 10) || DEFAULT_PORT;
  const autoCapture = document.getElementById("autoCapture").checked;
  const token = document.getElementById("token").value.trim();
  const minSizeKB = parseInt(document.getElementById("minSizeKB").value, 10) || 0;
  const extWhitelist = document.getElementById("extList").value.trim();

  chrome.storage.local.set({ port, autoCapture, token, minSizeKB, extWhitelist }, () => {
    setStatus(document.getElementById("status"), "Đã lưu.", "ok");
    chrome.runtime.sendMessage({ action: "refreshBadge" });
  });
}

async function ping() {
  const port = parseInt(document.getElementById("port").value, 10) || DEFAULT_PORT;
  const token = document.getElementById("token").value.trim();
  const statusEl = document.getElementById("status");
  try {
    const resp = await fetch(`http://127.0.0.1:${port}/ping`, {
      headers: { "X-FLD-Token": token }
    });
    if (!resp.ok) throw new Error("bad status");
    const data = await resp.json();
    if (data.paired) {
      setStatus(statusEl, "Đã kết nối và khớp mã.", "ok");
    } else {
      setStatus(statusEl, "Thấy ứng dụng đang chạy nhưng SAI mã kết nối - kiểm tra lại mã ở hộp thoại Cài đặt.", "warn");
    }
  } catch (e) {
    setStatus(statusEl, 'Không kết nối được. Hãy mở FileListDownloader và bật "Nhận link từ trình duyệt".', "bad");
  }
}

function scanCurrentTab() {
  const el = document.getElementById("scanStatus");
  el.textContent = "Đang quét...";
  chrome.runtime.sendMessage({ action: "scanCurrentTab" }, () => {
    el.textContent = "Đã gửi yêu cầu quét - xem thông báo góc màn hình và danh sách tải trong ứng dụng.";
  });
}

function loadFloaterStatus() {
  chrome.runtime.sendMessage({ action: "getFloaterStatus" }, (resp) => {
    const cb = document.getElementById("floaterToggle");
    cb.checked = !!(resp && resp.enabled);
  });
}

function onFloaterToggle(e) {
  const cb = e.target;
  const hint = document.getElementById("floaterHint");
  if (cb.checked) {
    chrome.runtime.sendMessage({ action: "enableFloater" }, (resp) => {
      if (!resp || !resp.ok) {
        cb.checked = false;
        hint.textContent = "Bạn đã từ chối cấp quyền, nên chưa bật được nút tải nổi.";
        hint.className = "hint bad";
      } else {
        hint.textContent = "Đã bật - rê chuột vào link/video trên trang để thấy nút tải nổi.";
        hint.className = "hint ok";
      }
    });
  } else {
    chrome.runtime.sendMessage({ action: "disableFloater" }, () => {
      hint.textContent = "Đã tắt nút tải nổi.";
      hint.className = "hint";
    });
  }
}

document.addEventListener("DOMContentLoaded", () => {
  load();
  loadFloaterStatus();
  document.getElementById("btnSave").addEventListener("click", save);
  document.getElementById("btnPing").addEventListener("click", ping);
  document.getElementById("btnScan").addEventListener("click", scanCurrentTab);
  document.getElementById("floaterToggle").addEventListener("change", onFloaterToggle);
});
