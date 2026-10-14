using System.Windows;
using System.IO;
using SynapseObserver.Core;

namespace SynapseObserver.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var exportIndex = Array.IndexOf(e.Args, "--export-inventory");
        if (exportIndex >= 0 && exportIndex + 1 < e.Args.Length)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var inventory = await Task.Run(() => new InventoryCollector().CollectAsync());
                await EvidenceBundleWriter.WriteAsync(new EvidenceSession(inventory), Path.GetFullPath(e.Args[exportIndex + 1]));
                Shutdown(0);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Shutdown(1);
            }

            return;
        }

        var renderIndex = Array.IndexOf(e.Args, "--render-preview");
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            var output = Path.GetFullPath(e.Args[renderIndex + 1]);
            Directory.CreateDirectory(output);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            for (int stage = 0; stage < 4; stage++)
            {
                new MainWindow(true).RenderPreview(Path.Combine(output, $"collector-stage-{stage}.png"), stage);
            }

            new MainWindow(true) { Width = 560 }.RenderPreview(Path.Combine(output, "collector-narrow-custom.png"), 3);
            Shutdown();
            return;
        }

        var window = new MainWindow(e.Args.Contains("--preview", StringComparer.Ordinal));
        MainWindow = window;
        window.Show();
    }
}
