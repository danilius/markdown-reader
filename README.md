# Markdown Reader

A single-file, offline Markdown reader with browser-style tabs and proper
Hebrew/English bidirectional rendering. It runs in Chrome (or Edge), so mixed
Hebrew–English text is rendered exactly as Chrome renders the plain file: the
document flows left-to-right, and Hebrew runs within each line are handled
inline by the browser's Unicode bidi algorithm.

There is also a native Windows app version in [`app/`](app/README.md) (WPF +
WebView2, same renderer) that adds live auto-reload on file changes, session
persistence, and single-instance tab opening.

## Use

Open **`MarkdownReader.html`** in Chrome or Edge (double-click it, or pin a
shortcut). Then:

- **Open files** — drag `.md` files anywhere into the window, click the 📂
  button, or press **Ctrl+O**. Multiple files open as multiple tabs.
- **Tabs** — click to switch, **×** or middle-click to close,
  **Ctrl+Tab / Ctrl+Shift+Tab** to cycle.
- **Reload** — the ↻ button in the file bar (or **Ctrl+R**) re-reads the file
  from disk without losing your scroll position — handy while editing the file
  elsewhere.

Everything is inlined; no internet connection or installation is needed.

Tip: for a cleaner app-like window without the browser chrome, create a
shortcut with the target:

    chrome.exe --app="file:///F:/Git Repos/Markdown reader/MarkdownReader.html"

## Rendering notes

- GitHub-flavored Markdown (tables, task lists, strikethrough) via
  [marked](https://github.com/markedjs/marked); HTML is sanitized with
  [DOMPurify](https://github.com/cure53/DOMPurify).
- Code blocks and inline code are forced LTR (correct for code inside RTL
  paragraphs).
- Light and dark theme follow the system setting.

## Development

- `src/reader.template.html` — the app source (edit this, not the built file).
- `marked.min.js`, `purify.min.js` — vendored libraries, inlined at build time.
- `build.ps1` — splices the libraries into the template and writes
  `MarkdownReader.html`.

Rebuild after editing the template:

    powershell -File build.ps1
