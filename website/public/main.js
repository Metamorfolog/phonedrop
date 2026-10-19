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
    const assets = { win: find(/\.exe$/i), apk: find(/\.apk$/i) };
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
          body: JSON.stringify({ kind: data.get("kind"), message, email, website: data.get("website"), source: "web", lang }),
        });
        if (r.ok) {
          form.reset();
          say("Thank you! Your message has been sent.", "Díky! Zpráva je odeslaná.", true);
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
