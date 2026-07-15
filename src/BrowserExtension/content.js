// Content script (chỉ chạy khi người dùng đã bật "Nút tải nổi" và cấp quyền trong popup).
// Rê chuột vào 1 link khớp danh sách đuôi file, hoặc vào 1 thẻ <video>, sẽ hiện 1 nút nổi nhỏ
// góc trên-phải của phần tử đó - bấm vào để gửi link sang FileListDownloader, giống nút nổi
// quen thuộc của IDM khi rê chuột qua video/link tải trên trang.

(function () {
  if (window.__fldFloaterInjected) return;
  window.__fldFloaterInjected = true;

  const DEFAULT_EXT_WHITELIST =
    "rar,zip,7z,tar,gz,exe,msi,iso,apk,dmg,pkg,pdf,mp4,mkv,avi,mov,wmv,mp3,flac";

  let enabled = true;
  let extList = DEFAULT_EXT_WHITELIST.split(",");

  chrome.storage.local.get({ floaterEnabled: true, extWhitelist: DEFAULT_EXT_WHITELIST }, (items) => {
    enabled = items.floaterEnabled;
    extList = items.extWhitelist.split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);
  });

  chrome.storage.onChanged.addListener((changes) => {
    if (changes.floaterEnabled) enabled = changes.floaterEnabled.newValue;
    if (changes.extWhitelist) extList = changes.extWhitelist.newValue.split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);
    if (!enabled) hideBadge();
  });

  function extOf(url) {
    try {
      const clean = url.split("?")[0].split("#")[0];
      const last = clean.split("/").pop() || "";
      const dot = last.lastIndexOf(".");
      return dot >= 0 ? last.substring(dot + 1).toLowerCase() : "";
    } catch (e) {
      return "";
    }
  }

  // ---- 1 nút nổi dùng chung, di chuyển vị trí tuỳ theo phần tử đang hover ----
  let badge = null;
  let currentUrl = null;
  let currentFilename = "";
  let hideTimer = null;

  function ensureBadge() {
    if (badge) return badge;
    badge = document.createElement("div");
    badge.textContent = "⬇";
    badge.title = "Tải bằng FileListDownloader";
    Object.assign(badge.style, {
      position: "fixed",
      zIndex: "2147483647",
      width: "26px",
      height: "26px",
      lineHeight: "26px",
      textAlign: "center",
      borderRadius: "50%",
      background: "#1a7f37",
      color: "#fff",
      fontSize: "14px",
      cursor: "pointer",
      boxShadow: "0 1px 4px rgba(0,0,0,.4)",
      userSelect: "none",
      display: "none",
      fontFamily: "Arial, sans-serif"
    });
    badge.addEventListener("mouseenter", () => clearTimeout(hideTimer));
    badge.addEventListener("mouseleave", scheduleHide);
    badge.addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (!currentUrl) return;
      chrome.runtime.sendMessage({
        action: "downloadUrl",
        url: currentUrl,
        filename: currentFilename,
        referer: location.href
      });
      badge.textContent = "✓";
      setTimeout(() => { if (badge) badge.textContent = "⬇"; }, 900);
    });
    document.documentElement.appendChild(badge);
    return badge;
  }

  function showBadgeFor(target, url, filename) {
    if (!enabled) return;
    currentUrl = url;
    currentFilename = filename || "";
    const b = ensureBadge();
    const rect = target.getBoundingClientRect();
    b.style.left = Math.max(0, rect.right - 30) + "px";
    b.style.top = Math.max(0, rect.top + 4) + "px";
    b.style.display = "flex";
    b.style.alignItems = "center";
    b.style.justifyContent = "center";
    clearTimeout(hideTimer);
  }

  function scheduleHide() {
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hideBadge, 400);
  }

  function hideBadge() {
    if (badge) badge.style.display = "none";
    currentUrl = null;
  }

  // ---- Theo dõi hover trên toàn trang (event delegation, không gắn listener riêng từng thẻ) ----
  document.addEventListener("mouseover", (e) => {
    if (!enabled) return;
    const a = e.target.closest && e.target.closest("a[href]");
    if (a) {
      const ext = extOf(a.href);
      if (extList.includes(ext)) {
        const name = a.href.split("?")[0].split("#")[0].split("/").pop();
        showBadgeFor(a, a.href, name);
        return;
      }
    }

    const v = e.target.closest && e.target.closest("video");
    if (v) {
      const src = v.currentSrc || (v.querySelector("source") && v.querySelector("source").src);
      if (src && src.indexOf("blob:") !== 0) {
        showBadgeFor(v, src, "video." + (extOf(src) || "mp4"));
        return;
      }
    }
  }, true);

  document.addEventListener("mouseout", (e) => {
    const stillOverBadge = badge && e.relatedTarget === badge;
    if (!stillOverBadge) scheduleHide();
  }, true);
})();
