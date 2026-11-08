namespace NupExtractorApp;

/// <summary>
/// WebView2-compatible bridge for WebKitGTK/Photino.
///
/// static/app.js (unchanged) detects the native host through
/// <c>window.chrome.webview</c>, which only exists on the WebView2 runtime.
/// Photino on Linux exposes <c>window.external.sendMessage/receiveMessage</c>
/// instead. The <see cref="PhotinoShell"/> injects this script into
/// index.html before page scripts run, so the original UI contract works
/// identically on both shells.
/// </summary>
public static class ChromeShim
{
    public const string Script = """
    (function () {
        if (window.chrome && window.chrome.webview) return;
        var listeners = [];
        var bound = false;
        function ensureReceiver() {
            if (bound || !window.external || !window.external.receiveMessage) return;
            bound = true;
            window.external.receiveMessage(function (raw) {
                var data = raw;
                if (typeof raw === 'string') {
                    try { data = JSON.parse(raw); } catch (e) { /* keep raw string */ }
                }
                var ev = { data: data };
                listeners.slice().forEach(function (cb) { cb(ev); });
            });
        }
        window.chrome = window.chrome || {};
        window.chrome.webview = {
            postMessage: function (m) {
                if (window.external && window.external.sendMessage) {
                    window.external.sendMessage(typeof m === 'string' ? m : JSON.stringify(m));
                } else {
                    console.warn('NUP Extractor: host bridge is not ready');
                }
            },
            addEventListener: function (name, cb) {
                if (name === 'message') { listeners.push(cb); ensureReceiver(); }
            },
            removeEventListener: function (name, cb) {
                if (name === 'message') {
                    var i = listeners.indexOf(cb);
                    if (i >= 0) listeners.splice(i, 1);
                }
            }
        };
    })();
    """;
}
