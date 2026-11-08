using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Photino.NET;

namespace NupExtractorApp;

/// <summary>
/// Linux desktop shell: Photino window hosting WebKitGTK with the same
/// embedded static UI that Windows serves through WebView2. All engine work
/// is delegated to the shared <see cref="IpcRouter"/>; this class only
/// adapts platform services (dialogs, webview messaging, resource serving,
/// desktop integration) behind <see cref="IPlatformShell"/>.
/// </summary>
public sealed class PhotinoShell : IPlatformShell
{
    private const string AppHost = "nup.local";
    private const string Scheme = "app";

    private static readonly Assembly CoreAssembly = typeof(NupEngine).Assembly;

    private readonly PhotinoWindow _window;
    private readonly IpcRouter _router;
    private readonly string _assetsDir;

    public PhotinoShell()
    {
        _router = new IpcRouter(this);
        _window = new PhotinoWindow();
        _assetsDir = Path.Combine(Path.GetTempPath(), "NupExtractor_Linux_Assets");
        Directory.CreateDirectory(_assetsDir);

        _window
            .SetTitle("NUP Extractor · Switch Clean-Room Container Utility")
            .SetSize(1280, 860)
            .SetMinSize(900, 600)
            .SetResizable(true)
            .SetDevToolsEnabled(true);

        TrySetWindowIcon();

        _window.RegisterCustomSchemeHandler(Scheme, new PhotinoWindow.NetCustomSchemeDelegate(OnResourceRequested));
        _window.WebMessageReceived += OnWebMessageReceived;
        _window.Load(new Uri($"{Scheme}://{AppHost}/index.html"));
    }

    /// <summary>Platform identifier reported to the UI (get_app_info).</summary>
    public string PlatformId => "linux";

    /// <summary>Blocks until the main window is closed (mirrors WPF ShutdownMode).</summary>
    public void Run() => _window.WaitForClose();

    private void OnWebMessageReceived(object? sender, string webMessageAsJson)
    {
        // Fire-and-forget like the original async void WebView2 handler;
        // IpcRouter serializes its own responses through SendWebMessage.
        _ = _router.HandleMessageAsync(webMessageAsJson);
    }

    // ------------------------------------------------------------------
    // Resource serving (embedded static UI, identical to Windows shell)
    // ------------------------------------------------------------------

    private Stream OnResourceRequested(object? sender, string scheme, string url, out string contentType)
    {
        // url looks like app://nup.local/index.html
        string path = url;
        int authoritySep = path.IndexOf("://", StringComparison.Ordinal);
        if (authoritySep >= 0) path = path[(authoritySep + 3)..];
        int slash = path.IndexOf('/');
        path = slash >= 0 ? path[(slash + 1)..] : "";
        int query = path.IndexOfAny(['?', '#']);
        if (query >= 0) path = path[..query];
        path = path.TrimStart('/');
        if (string.IsNullOrEmpty(path)) path = "index.html";

        contentType = GetMimeType(path);

        Stream? stream = GetResourceStream(path);
        if (stream == null)
        {
            contentType = "text/plain; charset=utf-8";
            return new MemoryStream(Encoding.UTF8.GetBytes("404 Not Found"));
        }

        if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            // Inject the WebView2-compatible bridge before any page script runs.
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string html = reader.ReadToEnd();
            string script = $"<script>{ChromeShim.Script}</script>\n";
            int head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            string injected = head >= 0
                ? html.Insert(head, script)
                : script + html;
            return new MemoryStream(Encoding.UTF8.GetBytes(injected));
        }

        return stream;
    }

    private static Stream? GetResourceStream(string path)
    {
        string clean = path.Replace('/', '.');
        foreach (var resName in CoreAssembly.GetManifestResourceNames())
        {
            if (resName.EndsWith(clean, StringComparison.OrdinalIgnoreCase))
            {
                return CoreAssembly.GetManifestResourceStream(resName);
            }
        }
        return null;
    }

    private static string GetMimeType(string path)
    {
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return "text/html; charset=utf-8";
        if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return "text/css; charset=utf-8";
        if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) return "application/javascript; charset=utf-8";
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
        if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return "image/jpeg";
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return "image/svg+xml";
        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return "image/x-icon";
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "application/json";
        return "application/octet-stream";
    }

    // ------------------------------------------------------------------
    // Messaging
    // ------------------------------------------------------------------

    /// <summary>
    /// Photino marshals this onto the WebKit UI loop internally, so it is
    /// safe from Progress continuation threads.
    /// </summary>
    public void SendWebMessage(string json) => _window.SendWebMessage(json);

    // ------------------------------------------------------------------
    // Native dialogs
    // ------------------------------------------------------------------

    public async Task<string?> BrowseFileAsync()
    {
        string[]? paths = await _window.ShowOpenFileAsync(
            "Select Nintendo Switch NUP Container",
            null,
            false,
            new (string, string[])[]
            {
                ("NUP Game Containers (*.nup)", new[] { "*.nup" }),
                ("All files (*.*)", new[] { "*.*" })
            });
        return paths is { Length: > 0 } ? paths[0] : null;
    }

    public async Task<string?> BrowseFolderAsync()
    {
        string[]? paths = await _window.ShowOpenFolderAsync(
            "Select Destination Extraction Folder", null, false);
        return paths is { Length: > 0 } ? paths[0] : null;
    }

    public async Task<string?> SaveFileAsync(string suggestedName)
    {
        string? path = await _window.ShowSaveFileAsync(
            $"Save {suggestedName} As", suggestedName);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    // ------------------------------------------------------------------
    // Desktop (shell) integration — GTK equivalent of the HKCU ProgId
    // registration the Windows shell writes: .nup association + context
    // menu actions via a freedesktop .desktop entry.
    // ------------------------------------------------------------------

    private const string DesktopFileId = "io.github.spin0zai.nup-extractor.desktop";
    private const string MimeType = "application/x-nup";

    private static string DesktopFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "applications", DesktopFileId);

    public bool IsShellIntegrationRegistered() => File.Exists(DesktopFilePath);

    public bool RegisterShellIntegration()
    {
        try
        {
            string exe = Environment.ProcessPath ?? Directory.GetCurrentDirectory();
            string contents = string.Join('\n', new[]
            {
                "[Desktop Entry]",
                "Type=Application",
                "Name=NUP Extractor",
                "GenericName=Archive extractor",
                "Comment=Nintendo Switch clean-room container extractor",
                $"Exec=\"{exe}\" --extract-to-folder %f",
                "Terminal=false",
                "Categories=Utility;FileTools;Archiving;",
                "Keywords=nintendo;switch;nup;extractor;",
                $"MimeType={MimeType};",
                "Actions=ExtractHere;Open;",
                "",
                "[Desktop Action ExtractHere]",
                "Name=Extract Here",
                $"Exec=\"{exe}\" --extract-here %f",
                "",
                "[Desktop Action Open]",
                "Name=Open in NUP Extractor",
                $"Exec=\"{exe}\" %f",
                ""
            });

            Directory.CreateDirectory(Path.GetDirectoryName(DesktopFilePath)!);
            File.WriteAllText(DesktopFilePath, contents);

            TryRun("xdg-mime", $"default {DesktopFileId} {MimeType}", TimeSpan.FromSeconds(10));
            TryRun("update-desktop-database",
                   Path.GetDirectoryName(DesktopFilePath)!, TimeSpan.FromSeconds(10));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool UnregisterShellIntegration()
    {
        try
        {
            if (File.Exists(DesktopFilePath)) File.Delete(DesktopFilePath);

            // Remove our associations from the user's mimeapps.list.
            string mimeapps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "mimeapps.list");
            if (File.Exists(mimeapps))
            {
                var lines = File.ReadAllLines(mimeapps)
                    .Where(l => !l.Contains(DesktopFileId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                File.WriteAllLines(mimeapps, lines);
            }

            TryRun("update-desktop-database", Path.GetDirectoryName(DesktopFilePath)!, TimeSpan.FromSeconds(10));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryRun(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch
        {
            // Helper tool missing (e.g. xdg-mime): best effort only.
        }
    }

    // ------------------------------------------------------------------
    // Window icon
    // ------------------------------------------------------------------

    private void TrySetWindowIcon()
    {
        try
        {
            string iconPath = Path.Combine(_assetsDir, "app_icon.ico");
            if (!File.Exists(iconPath))
            {
                var res = typeof(PhotinoShell).Assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("app_icon.ico", StringComparison.OrdinalIgnoreCase));
                if (res == null) return;
                using var src = typeof(PhotinoShell).Assembly.GetManifestResourceStream(res);
                if (src == null) return;
                using var dst = File.Create(iconPath);
                src.CopyTo(dst);
            }
            _window.SetIconFile(iconPath);
        }
        catch
        {
            // Icon is cosmetic; WebKit may reject ICO — ignore.
        }
    }
}
