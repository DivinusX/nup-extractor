namespace NupExtractorApp;

/// <summary>
/// Process-wide launch state shared between the desktop shell (WPF on Windows,
/// Photino on Linux) and the platform-neutral <see cref="IpcRouter"/>.
/// Mirrors the original <c>App.InitialFilePath</c> static field.
/// </summary>
public static class StartupState
{
    /// <summary>Full path of a .nup passed on the command line, or null.</summary>
    public static string? InitialFilePath { get; set; }
}
