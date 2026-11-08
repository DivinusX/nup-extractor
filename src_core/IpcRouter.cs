using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NupExtractorApp;

/// <summary>
/// Platform-neutral IPC dispatcher. Receives raw JSON messages from the embedded
/// webview (WebView2 on Windows, Photino/WebKitGTK on Linux), executes engine work
/// and streams responses back through <see cref="IPlatformShell.SendWebMessage"/>.
///
/// The wire contract (action names, response shapes, camelCase serialization) is
/// identical to the original WPF MainWindow implementation so static/app.js works
/// unchanged against both shells.
/// </summary>
public sealed class IpcRouter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IPlatformShell _shell;

    private CancellationTokenSource? _extractCts;
    private CancellationTokenSource? _verifyCts;
    private CancellationTokenSource? _updateCts;

    public IpcRouter(IPlatformShell shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    public async Task HandleMessageAsync(string webMessageAsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(webMessageAsJson);
            var root = doc.RootElement;
            string action = GetString(root, "action") ?? "";
            string id = GetString(root, "id") ?? "";

            switch (action)
            {
                case "browse_file":
                    await HandleBrowseFileAsync(id);
                    break;

                case "browse_folder":
                    await HandleBrowseFolderAsync(id);
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

                case "get_app_info":
                    SendIpcResponse(new
                    {
                        id,
                        action = "app_info_result",
                        success = true,
                        version = AppUpdateManager.CurrentVersion,
                        platform = _shell.PlatformId,
                        initialFilePath = StartupState.InitialFilePath
                    });
                    break;

                case "check_disk_space":
                    HandleCheckDiskSpace(id, root);
                    break;

                case "extract_single_file":
                    await HandleExtractSingleFileAsync(id, root);
                    break;

                case "get_shell_integration_status":
                    SendIpcResponse(new
                    {
                        id,
                        action = "shell_integration_status_result",
                        success = true,
                        registered = _shell.IsShellIntegrationRegistered()
                    });
                    break;

                case "toggle_shell_integration":
                    bool enable = GetBool(root, "enable") ?? false;
                    bool regSuccess = enable ? _shell.RegisterShellIntegration() : _shell.UnregisterShellIntegration();
                    SendIpcResponse(new
                    {
                        id,
                        action = "toggle_shell_integration_result",
                        success = regSuccess,
                        registered = _shell.IsShellIntegrationRegistered()
                    });
                    break;

                case "check_update":
                    await HandleCheckUpdateAsync(id, root);
                    break;

                case "start_update":
                    await HandleStartUpdateAsync(id, root);
                    break;

                case "cancel_update":
                    _updateCts?.Cancel();
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

    private async Task HandleBrowseFileAsync(string id)
    {
        string? path = await _shell.BrowseFileAsync();
        SendIpcResponse(new
        {
            id,
            action = "browse_file_result",
            success = path != null,
            path
        });
    }

    private async Task HandleBrowseFolderAsync(string id)
    {
        string? path = await _shell.BrowseFolderAsync();
        SendIpcResponse(new
        {
            id,
            action = "browse_folder_result",
            success = path != null,
            path
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

    private async Task HandleCheckUpdateAsync(string id, JsonElement root)
    {
        bool manual = GetBool(root, "manual") ?? false;
        try
        {
            var res = await Task.Run(() => AppUpdateManager.CheckForUpdatesAsync());
            SendIpcResponse(new
            {
                id,
                action = "check_update_result",
                success = string.IsNullOrEmpty(res.Error),
                error = res.Error,
                manual,
                data = res
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                id,
                action = "check_update_result",
                success = false,
                manual,
                error = ex.Message
            });
        }
    }

    private async Task HandleStartUpdateAsync(string id, JsonElement root)
    {
        string? downloadUrl = GetString(root, "downloadUrl", "download_url");
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            SendIpcResponse(new { type = "update_error", error = "No valid download URL provided for update." });
            return;
        }

        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();
        var token = _updateCts.Token;

        var progress = new Progress<UpdateProgressInfo>(info =>
        {
            SendIpcResponse(new
            {
                type = "update_progress",
                info
            });
        });

        try
        {
            await Task.Run(() => AppUpdateManager.ApplyUpdateAsync(downloadUrl, progress, token), token);
        }
        catch (OperationCanceledException)
        {
            SendIpcResponse(new
            {
                type = "update_error",
                error = "Update cancelled by user."
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                type = "update_error",
                error = ex.Message
            });
        }
    }

    private void HandleCheckDiskSpace(string id, JsonElement root)
    {
        string? destDir = GetString(root, "destDir", "dest_dir", "path");
        ulong requiredBytes = (ulong)(GetInt(root, "requiredBytes", "required_bytes") ?? 0);

        if (root.TryGetProperty("requiredBytes", out var rb) && rb.ValueKind == JsonValueKind.Number)
        {
            if (rb.TryGetUInt64(out ulong rbu)) requiredBytes = rbu;
        }

        var info = NupEngine.CheckDiskSpace(destDir ?? "", requiredBytes);
        SendIpcResponse(new
        {
            id,
            action = "check_disk_space_result",
            success = true,
            data = info
        });
    }

    private async Task HandleExtractSingleFileAsync(string id, JsonElement root)
    {
        string? nupPath = GetString(root, "filePath", "file_path");
        int fileIndex = GetInt(root, "fileIndex", "file_index") ?? -1;
        string suggestedName = GetString(root, "fileName", "file_name", "name") ?? "extracted_file";

        if (string.IsNullOrWhiteSpace(nupPath) || !File.Exists(nupPath) || fileIndex < 0)
        {
            SendIpcResponse(new { id, action = "extract_single_file_result", success = false, error = "Invalid file parameters." });
            return;
        }

        string? chosen = await _shell.SaveFileAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(chosen))
        {
            SendIpcResponse(new
            {
                id,
                action = "extract_single_file_result",
                success = false,
                cancelled = true
            });
            return;
        }

        try
        {
            string dest = chosen;
            await Task.Run(() => NupEngine.ExtractSingleFile(nupPath, fileIndex, dest));
            SendIpcResponse(new
            {
                id,
                action = "extract_single_file_result",
                success = true,
                destPath = dest,
                fileName = Path.GetFileName(dest)
            });
        }
        catch (Exception ex)
        {
            SendIpcResponse(new
            {
                id,
                action = "extract_single_file_result",
                success = false,
                error = ex.Message
            });
        }
    }

    private void SendIpcResponse(object payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        _shell.SendWebMessage(json);
    }

    internal static string? GetString(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        return null;
    }

    internal static int? GetInt(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number)
                return p.GetInt32();
        }
        return null;
    }

    internal static bool? GetBool(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False))
                return p.GetBoolean();
        }
        return null;
    }
}
