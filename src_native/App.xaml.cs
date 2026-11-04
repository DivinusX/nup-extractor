using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace NupExtractorApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--test-inspect")
        {
            string target = e.Args.Length > 1 ? e.Args[1] : "sample_game.nup";
            try
            {
                var data = NupEngine.Inspect(target);
                Console.WriteLine("INSPECT_SUCCESS: " + JsonSerializer.Serialize(data));
            }
            catch (Exception ex)
            {
                Console.WriteLine("INSPECT_ERROR: " + ex.Message);
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--test-extract")
        {
            string target = e.Args.Length > 1 ? e.Args[1] : "sample_game.nup";
            string dest = e.Args.Length > 2 ? e.Args[2] : "test_native_extract";
            try
            {
                var progress = new Progress<ProgressInfo>(p =>
                {
                    if (p.Status == "extracting")
                        Console.WriteLine($"PROGRESS: {p.Current}/{p.Total} - {p.FileName}");
                });
                NupEngine.Extract(target, dest, "all", null, null, progress, CancellationToken.None);
                Console.WriteLine("EXTRACT_SUCCESS");
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXTRACT_ERROR: " + ex.Message);
            }
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
    }
}
