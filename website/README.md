# PhoneDrop website

A single static page (`public/`) with no build step. Its only third-party request goes to the GitHub API for the
latest release, so the download links and sizes stay current. If that request fails, the links point at the
release the page was written for.

## Host it

Copy `public/` to any static web server; no build step or server-side code is needed. The feedback form posts JSON
to `/api/feedback` on the same host; without such an endpoint it just reports that sending failed.

## Edit it

- `public/index.html`: all the text, in English and Czech (`data-l="en"` / `data-l="cs"`). English is the default;
  the CZ/EN button switches and remembers the choice, and `?lang=cs` opens the Czech version.
- `public/style.css`: light and dark theme with the same colours as the apps. Dark follows the system unless the
  theme button (auto → light → dark) says otherwise. Images marked `only-light` / `only-dark` swap with the theme.
- `public/main.js`: the language and theme switch, the release links and the feedback form.
- `public/assets/screens/`: screenshots of the real apps (webp), per language and theme.
- `public/assets/og.png`: the preview image for link previews (1200×630), loaded from raw.githubusercontent.com,
  so it works on any domain.

To preview locally, run `python3 -m http.server -d website/public 8000` and open http://localhost:8000.
