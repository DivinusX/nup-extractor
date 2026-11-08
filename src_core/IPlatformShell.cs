using System;

namespace NupExtractorApp;

/// <summary>
/// Platform-specific services the shared <see cref="IpcRouter"/> needs from the
/// desktop shell (WebView2/WPF on Windows, Photino/WebKitGTK on Linux).
/// </summary>
public interface IPlatformShell
{
    /// <summary>Stable platform identifier exposed to the UI: "windows" | "linux".</summary>
    string PlatformId { get; }

    /// <summary>Shows the native open-file picker. Returns the chosen path or null when cancelled.</summary>
    System.Threading.Tasks.Task<string?> BrowseFileAsync();

    /// <summary>Shows the native folder picker. Returns the chosen folder or null when cancelled.</summary>
    System.Threading.Tasks.Task<string?> BrowseFolderAsync();

    /// <summary>Shows the native save-file picker pre-filled with <paramref name="suggestedName"/>. Null when cancelled.</summary>
    System.Threading.Tasks.Task<string?> SaveFileAsync(string suggestedName);

    /// <summary>
    /// Posts a serialized JSON payload to the embedded webview.
    /// Must be safe to call from any thread.
    /// </summary>
    void SendWebMessage(string json);

    bool RegisterShellIntegration();

    bool UnregisterShellIntegration();

    bool IsShellIntegrationRegistered();
}
