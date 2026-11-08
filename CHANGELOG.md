# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased] — 2026-11-08 — Native Linux (x64) port

The application now runs **natively on Linux x64 without Wine**, while Windows
behavior and the `.nup` output format are unchanged. Engine logic
(`NupFormat`, `NupReader`, `NupEngine`) is functionally identical.

### Added
- `src_core/` — platform-neutral .NET 10 library (`NupExtractorCore`):
  the container engine, `AppUpdateManager`, the shared `IpcRouter`, the
  `IPlatformShell` abstraction, `StartupState`, and the embedded `static/`
  UI assets served by both desktop shells.
- `src_linux/` — native Linux desktop shell built on **Photino.NET 4.0 +
  WebKitGTK** (chosen over Avalonia so the existing HTML/CSS/JS UI and the
  exact WebView2 IPC wire contract are reused unchanged):
  - `PhotinoShell.cs` — custom `app://` scheme resource serving, native GTK
    open/folder/save dialogs (replacing `Microsoft.Win32` pickers),
    freedesktop `.desktop` + `xdg-mime` integration (replacing the HKCU
    ProgId/Explorer context-menu registration), WebKit-safe folder opening.
  - `ChromeShim.cs` — injects a `window.chrome.webview` compatibility
    bridge onto Photino's `window.external` messaging at serve time;
    `static/app.js` itself is untouched.
  - `Program.cs` — same CLI contract as the Windows app:
    `--extract-here`, `--extract-to-folder`, `--test-inspect`,
    `--test-extract`, and opening a `.nup` passed as argument.
- `build_linux.sh` — Linux equivalent of `build_native.bat`: runs the full
  xUnit suite, then publishes a self-contained single-file
  `linux-x64` binary (`PublishSingleFile` +
  `IncludeNativeLibrariesForSelfExtract` + compression) to `./NUP_Extractor`.
- Solution (`NupExtractor.slnx`) now contains `src_core`, `src_linux`,
  `src_native`, and `tests_native`; `dotnet test NupExtractor.slnx` builds
  and passes on both operating systems (the WPF project cross-compiles on
  Linux via `EnableWindowsTargeting`, still Windows-only at runtime).

### Changed
- `src_native/MainWindow.xaml.cs` — the ~660-line inline IPC dispatcher was
  extracted into the shared `IpcRouter`; the WPF window is now a thin
  `IPlatformShell` adapter (WebView2 messaging, dialogs, registry Explorer
  integration). Action names, response shapes, and camelCase serialization
  are byte-for-byte the same wire contract.
- `src_core/NupEngine.cs` — one platform-compatibility fix:
  `CheckDiskSpace` used `Path.GetPathRoot`, which on Unix always returns
  `/` and therefore reported the wrong volume. On Unix the full path is now
  passed to `DriveInfo` (which resolves the containing mount); the Windows
  code path is unchanged.
- `tests_native/` — retargeted from `net10.0-windows`/WPF to `net10.0`
  against `src_core`; the HKCU shell-integration probe was replaced by
  `Test_ShellIntegration_RoundTripsThroughRouter`, which drives the same
  actions through the shared router with a fake shell.
  **All 23 tests pass on Linux and Windows.**

### Removed (Windows-only behavior on Linux)
- Auto-update **apply** is refused outside Windows (`PlatformNotSupported`
  with a clear message surfaced to the UI as `update_error`); the
  replace-the-running-`.exe` + `--update-replace` relaunch mechanism has no
  Linux equivalent. Update *checking*, release notes, and the version badge
  work on both platforms.
- No folder picker, Explorer launch, `\` path assumptions, or registry
  writes are required to build, test, or run on Linux.

### Security / repository hygiene
- No keys (`prod.keys`), firmware, or game dumps are included anywhere in
  the repository or tests — only the synthetic `sample_game.nup` fixture.

### Verification
- `dotnet test NupExtractor.slnx` → 23/23 passed on Linux (x64, .NET 10.0.403).
- `./build_linux.sh` → 39.6 MB self-contained ELF64 `./NUP_Extractor`
  (sha256 `f335806d6d2cc4a2…`), launched natively under Wayland/KDE with
  the UI rendering and live IPC round-trips confirmed via screenshots.
- Headless extraction of `sample_game.nup` through the published binary
  reproduces the exact 5-file tree and byte sizes asserted by the test suite.

### Known limitations
- Desktop "Extract Here" integration on Linux is best-effort
  (`.desktop` actions + `xdg-mime`); surfacing varies by file manager.
- WebKitGTK vs Chromium may render minor CSS/animation differences.
- Linux runtime requires system `libwebkit2gtk-4.1` + GTK 3.
- GitHub Actions still builds Windows-only; a `linux-x64` release matrix
  leg is a follow-up.
