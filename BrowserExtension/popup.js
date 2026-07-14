const DEFAULT_PORT = 39215;

function load() {
  chrome.storage.local.get({ port: DEFAULT_PORT, autoCapture: false }, (items) => {
    document.getElementById("port").value = items.port;
    document.getElementById("autoCapture").checked = items.autoCapture;
  });
}

function setStatus(text, ok) {
  const el = document.getElementById("status");
  el.textContent = text;
  el.className = ok ? "ok" : "bad";
}

function save() {
  const port = parseInt(document.getElementById("port").value, 10) || DEFAULT_PORT;
  const autoCapture = document.getElementById("autoCapture").checked;
  chrome.storage.local.set({ port, autoCapture }, () => {
    setStatus("Đã lưu.", true);
  });
}

async function ping() {
  const port = parseInt(document.getElementById("port").value, 10) || DEFAULT_PORT;
  try {
    const resp = await fetch(`http://127.0.0.1:${port}/ping`);
    if (resp.ok) {
      setStatus("Đã kết nối được với ứng dụng.", true);
    } else {
      setStatus("Ứng dụng phản hồi lỗi.", false);
    }
  } catch (e) {
    setStatus('Không kết nối được. Hãy mở FileListDownloader và bật "Nhận link từ trình duyệt".', false);
  }
}

document.addEventListener("DOMContentLoaded", () => {
  load();
  document.getElementById("btnSave").addEventListener("click", save);
  document.getElementById("btnPing").addEventListener("click", ping);
});
