using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using NupExtractorApp;

/// <summary>
/// Linux entry point. Mirrors the command-line contract of the Windows
/// App.xaml.cs OnStartup handler so desktop actions, file association and
/// CI smoke flags behave identically on both platforms.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            string first = args[0];

            // In-place self-update is a Windows-binary mechanism (replace the
            // running .exe). The Linux package manager / manual release
            // download replaces it; AppUpdateManager refuses apply elsewhere.
            if (first == "--update-replace" && args.Length >= 3)
            {
                Console.Error.WriteLine("error: in-place auto-update is not supported on Linux builds. Download a new release from https://github.com/SPIN0ZAi/nup-extractor/releases");
                return 1;
            }

            if (first == "--extract-here" && args.Length > 1)
                return ExtractHeadless(args[1]);

            if (first == "--extract-to-folder" && args.Length > 1)
            {
                string target = args[1];
                string baseDir = Path.GetDirectoryName(Path.GetFullPath(target)) ?? Directory.GetCurrentDirectory();
                string dest = Path.Combine(baseDir, Path.GetFileNameWithoutExtension(target) + "_extracted");
                return ExtractHeadless(target, dest);
            }

            if (first == "--test-inspect")
            {
                string target = args.Length > 1 ? args[1] : "sample_game.nup";
                try
                {
                    var data = NupEngine.Inspect(target);
                    Console.WriteLine("INSPECT_SUCCESS: " + JsonSerializer.Serialize(data));
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("INSPECT_ERROR: " + ex.Message);
                    return 1;
                }
            }

            if (first == "--test-extract")
            {
                string target = args.Length > 1 ? args[1] : "sample_game.nup";
                string dest = args.Length > 2 ? args[2] : "test_native_extract";
                try
                {
                    var progress = new Progress<ProgressInfo>(p =>
                    {
                        if (p.Status == "extracting")
                            Console.WriteLine($"PROGRESS: {p.Current}/{p.Total} - {p.FileName}");
                    });
                    NupEngine.Extract(target, dest, "all", null, null, progress, CancellationToken.None);
                    Console.WriteLine("EXTRACT_SUCCESS");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXTRACT_ERROR: " + ex.Message);
                    return 1;
                }
            }

            if (File.Exists(first) && first.EndsWith(".nup", StringComparison.OrdinalIgnoreCase))
                StartupState.InitialFilePath = Path.GetFullPath(first);
        }

        var shell = new PhotinoShell();
        shell.Run();
        return 0;
    }

    private static int ExtractHeadless(string target, string? destOverride = null)
    {
        string dest = destOverride
            ?? Path.GetDirectoryName(Path.GetFullPath(target))
            ?? Directory.GetCurrentDirectory();
        try
        {
            var progress = new Progress<ProgressInfo>(_ => { });
            NupEngine.Extract(target, dest, "all", null, null, progress, CancellationToken.None);
            NupEngine.OpenFolder(dest);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to extract '{Path.GetFileName(target)}': {ex.Message}");
            return 1;
        }
    }
}
