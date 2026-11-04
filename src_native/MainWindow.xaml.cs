using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace NupExtractorApp;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private CancellationTokenSource? _extractCts;
    private CancellationTokenSource? _verifyCts;

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

    private Stream? GetResourceStream(string path)
    {
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "static", path);
        if (File.Exists(localPath)) return File.OpenRead(localPath);

        string curPath = Path.Combine(Directory.GetCurrentDirectory(), "static", path);
        if (File.Exists(curPath)) return File.OpenRead(curPath);

        var asm = Assembly.GetExecutingAssembly();
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
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string action = GetString(root, "action") ?? "";
            string id = GetString(root, "id") ?? "";

            switch (action)
            {
                case "browse_file":
                    HandleBrowseFile(id);
                    break;

                case "browse_folder":
                    HandleBrowseFolder(id);
                    break;

                case "inspect":
                    await HandleInspectAsync(id, root);
                    break;

                case "get_tree":
                    await HandleGetTreeAsync(id, root);
                    break;

                case "search":
                    await HandleSearchAsync(id, root);
                    break;

                case "extract":
                    await HandleExtractAsync(id, root);
                    break;

                case "verify":
                    await HandleVerifyAsync(id, root);
                    break;

                case "open_folder":
                    string fPath = GetString(root, "path", "folderPath") ?? "";
                    NupEngine.OpenFolder(fPath);
                    break;

                case "cancel_extract":
                    _extractCts?.Cancel();
                    break;

                case "cancel_verify":
                    _verifyCts?.Cancel();
                    break;
            }
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                type = "error",
                error = ex.Message
            });
        }
    }

    private void HandleBrowseFile(string id)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Select Nintendo Switch NUP Container",
            Filter = "NUP Game Containers (*.nup)|*.nup|All files (*.*)|*.*"
        };
        bool? result = ofd.ShowDialog(this);
        SendIpcResponse(new
        {
            id,
            action = "browse_file_result",
            success = result == true,
            path = result == true ? ofd.FileName : null
        });
    }

    private void HandleBrowseFolder(string id)
    {
        var ofd = new OpenFolderDialog
        {
            Title = "Select Destination Extraction Folder",
            Multiselect = false
        };
        bool? result = ofd.ShowDialog(this);
        SendIpcResponse(new
        {
            id,
            action = "browse_folder_result",
            success = result == true,
            path = result == true ? ofd.FolderName : null
        });
    }

    private async Task HandleInspectAsync(string id, JsonElement root)
    {
        string? filePath = GetString(root, "filePath", "file_path", "path");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            SendIpcResponse(new { id, action = "inspect_result", success = false, error = "File path is required" });
            return;
        }

        try
        {
            var data = await Task.Run(() => NupEngine.Inspect(filePath));
            SendIpcResponse(new
            {
                id,
                action = "inspect_result",
                success = true,
                data
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                id,
                action = "inspect_result",
                success = false,
                error = ex.Message
            });
        }
    }

    private async Task HandleGetTreeAsync(string id, JsonElement root)
    {
        string? filePath = GetString(root, "filePath", "file_path");
        uint dirIndex = (uint)(GetInt(root, "dirIndex", "dir_index") ?? 0);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            SendIpcResponse(new { id, action = "get_tree_result", success = false, error = "File path is required" });
            return;
        }

        try
        {
            var data = await Task.Run(() => NupEngine.GetDirChildren(filePath, dirIndex));
            SendIpcResponse(new
            {
                id,
                action = "get_tree_result",
                success = true,
                data
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                id,
                action = "get_tree_result",
                success = false,
                error = ex.Message
            });
        }
    }

    private async Task HandleSearchAsync(string id, JsonElement root)
    {
        string? filePath = GetString(root, "filePath", "file_path");
        string query = GetString(root, "query", "q") ?? "";

        if (string.IsNullOrWhiteSpace(filePath))
        {
            SendIpcResponse(new { id, action = "search_result", success = false, error = "File path is required" });
            return;
        }

        try
        {
            var results = await Task.Run(() => NupEngine.SearchFiles(filePath, query));
            SendIpcResponse(new
            {
                id,
                action = "search_result",
                success = true,
                query,
                results
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                id,
                action = "search_result",
                success = false,
                error = ex.Message
            });
        }
    }

    private async Task HandleExtractAsync(string id, JsonElement root)
    {
        string? filePath = GetString(root, "filePath", "file_path");
        string? destDir = GetString(root, "destDir", "dest_dir");
        string mode = GetString(root, "mode") ?? "all";
        int? pkgIdx = GetInt(root, "packageIndex", "package_index");
        string? subtreePath = GetString(root, "subtreePath", "subtree_path");
        bool openWhenDone = GetBool(root, "openWhenDone", "open_when_done") ?? true;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SendIpcResponse(new { type = "extract_error", error = $"NUP container file not found: {filePath}" });
            return;
        }

        if (string.IsNullOrWhiteSpace(destDir))
        {
            SendIpcResponse(new { type = "extract_error", error = "Destination directory cannot be empty." });
            return;
        }

        _extractCts?.Cancel();
        _extractCts = new CancellationTokenSource();
        var token = _extractCts.Token;

        var progress = new Progress<ProgressInfo>(info =>
        {
            SendIpcResponse(new
            {
                type = "extract_progress",
                info
            });
        });

        try
        {
            await Task.Run(() => NupEngine.Extract(filePath, destDir, mode, pkgIdx, subtreePath, progress, token), token);

            if (openWhenDone)
            {
                NupEngine.OpenFolder(destDir);
            }

            SendIpcResponse(new
            {
                type = "extract_done",
                success = true,
                destDir
            });
        }
        catch (OperationCanceledException)
        {
            SendIpcResponse(new
            {
                type = "extract_error",
                error = "Extraction cancelled by user."
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                type = "extract_error",
                error = ex.Message
            });
        }
    }

    private async Task HandleVerifyAsync(string id, JsonElement root)
    {
        string? filePath = GetString(root, "filePath", "file_path");
        bool deep = GetBool(root, "deep") ?? true;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SendIpcResponse(new { type = "verify_error", error = $"NUP container file not found: {filePath}" });
            return;
        }

        _verifyCts?.Cancel();
        _verifyCts = new CancellationTokenSource();
        var token = _verifyCts.Token;

        var progress = new Progress<VerifyProgressInfo>(info =>
        {
            if (info.Status == "done")
            {
                SendIpcResponse(new
                {
                    type = "verify_done",
                    info
                });
            }
            else
            {
                SendIpcResponse(new
                {
                    type = "verify_progress",
                    info
                });
            }
        });

        try
        {
            await Task.Run(() => NupEngine.Verify(filePath, deep, progress, token), token);
        }
        catch (OperationCanceledException)
        {
            SendIpcResponse(new
            {
                type = "verify_error",
                error = "Verification cancelled."
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                type = "verify_error",
                error = ex.Message
            });
        }
    }

    private void SendIpcResponse(object payload)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (WebView.CoreWebView2 != null)
            {
                string json = JsonSerializer.Serialize(payload, JsonOptions);
                WebView.CoreWebView2.PostWebMessageAsJson(json);
            }
        });
    }

    private static string? GetString(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        return null;
    }

    private static int? GetInt(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number)
                return p.GetInt32();
        }
        return null;
    }

    private static bool? GetBool(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False))
                return p.GetBoolean();
        }
        return null;
    }
}
