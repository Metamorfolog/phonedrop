// PhoneDrop website: language and theme switch, live download links from the latest GitHub release,
// and the feedback form.
(() => {
  "use strict";

  const REPO = "Metamorfolog/phonedrop";
  const root = document.documentElement;
  const $ = (sel, el = document) => el.querySelector(sel);
  const $$ = (sel, el = document) => Array.from(el.querySelectorAll(sel));

  const store = {
    get(key) { try { return localStorage.getItem(key); } catch { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* private mode */ } },
    remove(key) { try { localStorage.removeItem(key); } catch { /* private mode */ } },
  };

  // ---------- Language ----------
  const params = new URLSearchParams(location.search);
  let lang = params.get("lang") || store.get("lang") || "en";
  const setLang = value => {
    lang = value === "cs" ? "cs" : "en";
    root.dataset.lang = lang;
    root.lang = lang;
    $$("[data-ph-en]").forEach(el => { el.placeholder = el.dataset[lang === "cs" ? "phCs" : "phEn"]; });
  };
  $("#lang-toggle").addEventListener("click", () => {
    setLang(lang === "cs" ? "en" : "cs");
    store.set("lang", lang);
  });

  // ---------- Theme: auto → light → dark ----------
  const themes = ["auto", "light", "dark"];
  let theme = store.get("theme") || "auto";
  const setTheme = value => {
    theme = themes.includes(value) ? value : "auto";
    if (theme === "auto") delete root.dataset.theme; else root.dataset.theme = theme;
  };
  $("#theme-toggle").addEventListener("click", () => {
    setTheme(themes[(themes.indexOf(theme) + 1) % themes.length]);
    if (theme === "auto") store.remove("theme"); else store.set("theme", theme);
  });

  // ---------- Downloads ----------
  const mb = bytes => `${(bytes / 1048576).toFixed(1)} MB`;

  function applyRelease(release) {
    const find = re => (release.assets || []).find(a => re.test(a.name));
    const assets = { win: find(/-win-x64\.exe$/i) || find(/\.exe$/i), winsmall: find(/-small\.exe$/i), apk: find(/\.apk$/i) };
    for (const [key, asset] of Object.entries(assets)) {
      if (!asset) continue;
      $$(`.js-dl-${key}`).forEach(a => { a.href = asset.browser_download_url; });
      $$(`.js-size-${key}`).forEach(el => { el.textContent = mb(asset.size); });
    }
    if (release.tag_name) $$(".js-version").forEach(el => { el.textContent = release.tag_name; });
  }

  async function loadRelease() {
    const cacheKey = "release-cache";
    try {
      const cached = JSON.parse(sessionStorage.getItem(cacheKey) || "null");
      if (cached) return applyRelease(cached);
    } catch { /* ignore */ }
    try {
      const response = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers: { Accept: "application/vnd.github+json" } });
      if (!response.ok) return;
      const release = await response.json();
      const slim = { tag_name: release.tag_name, assets: (release.assets || []).map(a => ({ name: a.name, size: a.size, browser_download_url: a.browser_download_url })) };
      try { sessionStorage.setItem(cacheKey, JSON.stringify(slim)); } catch { /* ignore */ }
      applyRelease(slim);
    } catch { /* offline or rate-limited: the links in the page point at v1.0.0 */ }
  }

  // ---------- Feedback ----------
  function feedback() {
    const form = $("#feedback-form");
    if (!form) return;
    const status = $("#feedback-status");
    const say = (en, cs, ok) => {
      status.textContent = lang === "cs" ? cs : en;
      status.className = "feedback-status " + (ok ? "ok" : "err");
    };

    // Snímky obrazovky: zmenšení a převod v prohlížeči (nové plátno = bez EXIF a jiných metadat).
    const MAX_SHOTS = 3, MAX_BYTES = 2400000, MAX_SIDE = 2560;
    const shots = [];
    const shotsBox = $("#feedback-shots"), addBox = $("#feedback-add"), files = $("#feedback-files");
    const toBlob = (canvas, type, q) => new Promise(res => canvas.toBlob(res, type, q));
    const toBase64 = blob => new Promise((res, rej) => {
      const fr = new FileReader();
      fr.onload = () => res(String(fr.result));
      fr.onerror = rej;
      fr.readAsDataURL(blob);
    });
    async function prepare(file) {
      const bmp = await createImageBitmap(file);
      const k = Math.min(1, MAX_SIDE / Math.max(bmp.width, bmp.height));
      const canvas = document.createElement("canvas");
      canvas.width = Math.max(1, Math.round(bmp.width * k));
      canvas.height = Math.max(1, Math.round(bmp.height * k));
      const g = canvas.getContext("2d");
      let blob = null;
      if (file.type === "image/png") {
        g.drawImage(bmp, 0, 0, canvas.width, canvas.height);
        blob = await toBlob(canvas, "image/png");
        if (blob && blob.size > MAX_BYTES) blob = null;
      }
      if (!blob) {
        g.fillStyle = "#fff";
        g.fillRect(0, 0, canvas.width, canvas.height);
        g.drawImage(bmp, 0, 0, canvas.width, canvas.height);
        for (const q of [0.85, 0.72, 0.6]) {
          blob = await toBlob(canvas, "image/jpeg", q);
          if (blob && blob.size <= MAX_BYTES) break;
        }
      }
      bmp.close();
      if (!blob || blob.size > MAX_BYTES) throw new Error("too big");
      const url = await toBase64(blob);
      const type = blob.type === "image/png" ? "image/png" : "image/jpeg";
      return { name: "screenshot" + (type === "image/png" ? ".png" : ".jpg"), type, url, data: url.slice(url.indexOf(",") + 1) };
    }
    function drawShots() {
      shotsBox.querySelectorAll(".fb-shot").forEach(el => el.remove());
      for (const s of shots) {
        const box = document.createElement("div");
        box.className = "fb-shot";
        const img = document.createElement("img");
        img.src = s.url;
        img.alt = "";
        const x = document.createElement("button");
        x.type = "button";
        x.textContent = "✕";
        x.setAttribute("aria-label", lang === "cs" ? "Odebrat obrázek" : "Remove image");
        x.addEventListener("click", () => { shots.splice(shots.indexOf(s), 1); drawShots(); });
        box.append(img, x);
        shotsBox.insertBefore(box, addBox);
      }
      addBox.hidden = shots.length >= MAX_SHOTS;
    }
    async function addFiles(list) {
      for (const f of list) {
        if (!f.type.startsWith("image/")) continue;
        if (shots.length >= MAX_SHOTS) return say("Up to 3 images.", "Nejvýš 3 obrázky.", false);
        try {
          shots.push(await prepare(f));
          drawShots();
        } catch {
          say("That image can't be used.", "Tenhle obrázek nejde použít.", false);
        }
      }
    }
    if (shotsBox) {
      files.addEventListener("change", () => { addFiles([...files.files]); files.value = ""; });
      form.addEventListener("paste", e => {
        const list = [...(e.clipboardData?.files || [])].filter(f => f.type.startsWith("image/"));
        if (list.length) { e.preventDefault(); addFiles(list); }
      });
      shotsBox.addEventListener("dragover", e => { e.preventDefault(); shotsBox.classList.add("drag"); });
      shotsBox.addEventListener("dragleave", () => shotsBox.classList.remove("drag"));
      shotsBox.addEventListener("drop", e => { e.preventDefault(); shotsBox.classList.remove("drag"); addFiles([...e.dataTransfer.files]); });
    }
    form.addEventListener("submit", async e => {
      e.preventDefault();
      const data = new FormData(form);
      const message = String(data.get("message") || "").trim();
      const email = String(data.get("email") || "").trim();
      if (message.length < 3) return say("Please write a few words first.", "Napiš prosím aspoň pár slov.", false);
      if (email && !form.elements.email.checkValidity()) return say("That e-mail doesn't look right.", "Ten e-mail nevypadá správně.", false);
      const button = form.querySelector("button[type=submit]");
      button.disabled = true;
      say("Sending…", "Odesílám…", true);
      try {
        const r = await fetch("/api/feedback", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            kind: data.get("kind"), message, email, website: data.get("website"), source: "web", lang,
            attachments: shots.map(s => ({ name: s.name, type: s.type, data: s.data })),
          }),
        });
        if (r.ok) {
          form.reset();
          shots.length = 0;
          if (shotsBox) drawShots();
          say("Thank you! Your message has been sent.", "Díky! Zpráva je odeslaná.", true);
        } else if (r.status === 413 || (r.status === 400 && /attachment/.test(await r.text()))) {
          say("The server didn't accept an image. Try a different one.", "Server obrázek nepřijal. Zkus jiný.", false);
        } else if (r.status === 429) {
          say("Too many messages in a short time. Try again in a few minutes.", "Moc zpráv za krátkou dobu. Zkus to za pár minut.", false);
        } else {
          say("Sending failed. Please try again later.", "Odeslání se nepovedlo. Zkus to prosím později.", false);
        }
      } catch {
        say("Sending failed. Check your connection and try again.", "Odeslání se nepovedlo. Zkontroluj připojení a zkus to znovu.", false);
      } finally {
        button.disabled = false;
      }
    });
  }

  // ---------- Support ----------
  function support() {
    const button = $("#sol-copy");
    if (!button) return;
    const note = $("#sol-copied");
    button.addEventListener("click", async () => {
      let ok = false;
      try {
        await navigator.clipboard.writeText(button.dataset.copy);
        ok = true;
      } catch {
        // Bez schránky: označit adresu, ať jde zkopírovat ručně.
        const range = document.createRange();
        range.selectNodeContents($("#sol-address"));
        const selection = getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
      }
      note.textContent = ok
        ? (lang === "cs" ? "Zkopírováno" : "Copied")
        : (lang === "cs" ? "Adresa je označená, zkopíruj ji Ctrl+C" : "Address selected, press Ctrl+C");
      setTimeout(() => { note.textContent = ""; }, 2500);
    });
  }

  setLang(lang);
  setTheme(theme);
  feedback();
  support();
  loadRelease();
})();
