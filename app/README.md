# Markdown Reader (C# desktop app)

A native Windows version of the single-file HTML reader in the parent folder.
It is a WPF shell hosting WebView2, and it reuses the exact same renderer
(marked + DOMPurify, the same GitHub-like CSS, the same tab UI and
Hebrew/English bidi behavior) — the native side adds what a plain browser
page can't do:

- **Live auto-reload** — every open file is watched with a `FileSystemWatcher`;
  when it changes on disk the tab re-renders automatically, keeping your
  scroll position. Works with editors that save via temp-file-and-rename.
- **Session persistence** — open tabs, the active tab, per-tab scroll
  positions, and the window size/position are saved to
  `%APPDATA%\MarkdownReader\session.json` and restored on the next launch.
- **Single instance** — launching the app again (e.g. double-clicking another
  `.md` file) opens the file as a new tab in the existing window instead of
  starting a second copy.
- **Command-line files** — `MarkdownReader.exe file1.md file2.md` opens tabs,
  so the app can be used with *Open with…* / file association.
- **Relative links** — clicking a link to another local `.md` file opens it in
  a new tab; `http(s)`/`mailto` links open in your default browser.
- **TOC sidebar** — a collapsible table of contents built from the document's
  headings, with click-to-jump and the current section highlighted while you
  scroll. Toggle it with the ☰ button or **Ctrl+B**, and resize it by
  dragging its edge (double-click the edge to reset). Both visibility and
  width are remembered across sessions. It hides itself for documents with
  fewer than two headings.
- **Find in document** — **Ctrl+F** opens a find bar with live highlighting
  (CSS Custom Highlight API — no DOM mutation), match counter, and
  Enter / Shift+Enter to walk matches.
- **Export to PDF** — **Ctrl+P** or the ⤓ PDF button prints the current
  document to a PDF of your choosing; a print stylesheet strips the app
  chrome so only the rendered document is exported.

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| Ctrl+O | Open file(s) |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+R | Reload current tab from disk |
| Ctrl+W (or middle-click a tab) | Close tab |
| Ctrl+F | Find in document |
| Ctrl+B | Show/hide the TOC sidebar |
| Ctrl+P | Export current document as PDF |

Drag & drop of `.md` files works anywhere in the window.

## Build & run

Requires the .NET 9 SDK (Windows) and the Edge WebView2 runtime
(preinstalled on Windows 11).

    dotnet build app/MarkdownReader.csproj
    app/bin/Debug/net9.0-windows/MarkdownReader.exe [files...]

For a self-contained release build:

    dotnet publish app/MarkdownReader.csproj -c Release

## File association

To make `.md` files open in the reader, right-click a `.md` file →
*Open with* → *Choose another app* → browse to `MarkdownReader.exe` and tick
*Always*. Thanks to the single-instance pipe, each file opens as a tab in the
running window.

## Layout

- `MainWindow.xaml.cs` — WebView2 host: open/reload/watch files, session
  save/restore, drag & drop (native side, so real file paths are available),
  single-instance activation.
- `FileWatchService.cs` — debounced per-file watchers with retry-on-locked
  reads.
- `Session.cs` — the `session.json` model and store.
- `App.xaml.cs` — single-instance mutex + named-pipe forwarding of file
  arguments.
- `wwwroot/index.html` — the renderer (adapted from `../src/reader.template.html`);
  talks to the host over `chrome.webview.postMessage`.
- `wwwroot/marked.min.js`, `wwwroot/purify.min.js` — copies of the vendored
  libraries in the parent folder.
