using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace NupExtractorApp;

public partial class MainWindow : Window
{
    private WebViewShell? _shell;
    private IpcRouter? _router;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string userDataDir = Path.Combine(Path.GetTempPath(), "NupExtractor_WebView2Data");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataDir);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            // Register embedded web resource handler
            WebView.CoreWebView2.AddWebResourceRequestedFilter("https://nup.local/*", CoreWebView2WebResourceContext.All);
            WebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            // All IPC business logic now lives in the shared platform-neutral router
            _shell = new WebViewShell(WebView, this);
            _router = new IpcRouter(_shell);

            // Register IPC message handler
            WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Navigate to embedded app
            WebView.CoreWebView2.Navigate("https://nup.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize WebView2:\n{ex.Message}\n\nPlease ensure WebView2 Runtime is installed.",
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        string path = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(path)) path = "index.html";

        var stream = GetResourceStream(path);
        if (stream != null)
        {
            string mime = GetMimeType(path);
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                stream, 200, "OK", $"Content-Type: {mime}\r\nAccess-Control-Allow-Origin: *\r\n");
        }
        else
        {
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
    }

    private static Stream? GetResourceStream(string path)
    {
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "static", path);
        if (File.Exists(localPath)) return File.OpenRead(localPath);

        string curPath = Path.Combine(Directory.GetCurrentDirectory(), "static", path);
        if (File.Exists(curPath)) return File.OpenRead(curPath);

        // static/ is embedded in NupExtractorCore.dll (shared with the Linux shell)
        var asm = typeof(NupEngine).Assembly;
        string clean = path.Replace('/', '.');
        foreach (var resName in asm.GetManifestResourceNames())
        {
            if (resName.EndsWith(clean, StringComparison.OrdinalIgnoreCase))
            {
                return asm.GetManifestResourceStream(resName);
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

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_router != null)
        {
            await _router.HandleMessageAsync(e.WebMessageAsJson);
        }
    }
}

/// <summary>
/// Windows platform shell: adapts WPF dialogs, WebView2 messaging and the
/// registry-backed Explorer integration behind <see cref="IPlatformShell"/>.
/// </summary>
internal sealed class WebViewShell : IPlatformShell
{
    private readonly WebView2 _webView;
    private readonly Window _owner;

    public WebViewShell(WebView2 webView, Window owner)
    {
        _webView = webView;
        _owner = owner;
    }

    public string PlatformId => "windows";

    public Task<string?> BrowseFileAsync()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Select Nintendo Switch NUP Container",
            Filter = "NUP Game Containers (*.nup)|*.nup|All files (*.*)|*.*"
        };
        bool? result = ofd.ShowDialog(_owner);
        return Task.FromResult(result == true ? ofd.FileName : null);
    }

    public Task<string?> BrowseFolderAsync()
    {
        var ofd = new OpenFolderDialog
        {
            Title = "Select Destination Extraction Folder",
            Multiselect = false
        };
        bool? result = ofd.ShowDialog(_owner);
        return Task.FromResult(result == true ? ofd.FolderName : null);
    }

    public Task<string?> SaveFileAsync(string suggestedName)
    {
        var sfd = new SaveFileDialog
        {
            Title = $"Save {suggestedName} As",
            FileName = suggestedName,
            Filter = "All files (*.*)|*.*"
        };
        bool? result = sfd.ShowDialog(_owner);
        return Task.FromResult(result == true && !string.IsNullOrWhiteSpace(sfd.FileName) ? sfd.FileName : null);
    }

    public void SendWebMessage(string json)
    {
        _owner.Dispatcher.InvokeAsync(() =>
        {
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            }
        });
    }

    public bool RegisterShellIntegration() => ShellIntegration.Register();

    public bool UnregisterShellIntegration() => ShellIntegration.Unregister();

    public bool IsShellIntegrationRegistered() => ShellIntegration.IsRegistered();
}
