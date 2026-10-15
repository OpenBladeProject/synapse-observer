using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SynapseObserver.App;

public partial class MainWindow : Window
{
    private readonly CollectorViewModel viewModel;
    private bool closing;
    private bool wasRecording;
    private bool hadInventory;
    private bool pendingExportFocus;
    private bool hadCapacityRecovery;
    private SpecialKeyProbe? specialKeyProbe;

    public MainWindow(bool preview)
    {
        InitializeComponent();
        viewModel = new CollectorViewModel(preview);
        DataContext = viewModel;
        Closing += HandleClosing;
        SourceInitialized += (_, _) =>
        {
            if (preview) return;
            specialKeyProbe = new SpecialKeyProbe(new WindowInteropHelper(this).Handle);
            viewModel.ConfigureSpecialKeyProbe(specialKeyProbe, () => IsActive && IsEnabled);
        };
        Deactivated += (_, _) => viewModel.CancelSpecialKeyProbe();
        Closed += async (_, _) =>
        {
            specialKeyProbe?.Dispose();
            await viewModel.CloseAsync();
        };
        viewModel.PropertyChanged += (_, _) =>
        {
            if (!hadInventory && viewModel.HasInventory)
            {
                hadInventory = true;
                // Expand the initial compact window once when evidence becomes
                // available. Preserve a height the user has already chosen.
                if (WindowState == WindowState.Normal && Math.Abs(Height - 420) < 0.5)
                    Height = 640;
            }
            var finished = wasRecording && !viewModel.IsRecording;
            wasRecording = viewModel.IsRecording;
            var recoveryAvailable = viewModel.ShowCapacityRecovery;
            pendingExportFocus |= finished || recoveryAvailable && !hadCapacityRecovery;
            hadCapacityRecovery = recoveryAvailable;
            if (pendingExportFocus && !viewModel.IsBusy)
            {
                pendingExportFocus = false;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!closing && IsEnabled && IsVisible && viewModel.ShowExport)
                    {
                        ExportCard.BringIntoView();
                        if (!viewModel.ShowCapacityRecovery) SaveEvidenceButton.Focus();
                        else if (viewModel.CapacityRestorationRequired && !viewModel.CapacityRestorationConfirmed)
                            CapacityRestorationCheckBox.Focus();
                        else if (viewModel.SaveAndContinueCommand.CanExecute(null)) ContinueEvidenceButton.Focus();
                        else SavePartialEvidenceButton.Focus();
                    }
                }));
            }
        };
    }

    internal void RenderPreview(string path, int stage)
    {
        viewModel.SetPreviewStage(stage);
        Show();
        UpdateLayout();
        if (stage == 4)
        {
            ExportCard.BringIntoView();
            UpdateLayout();
        }
        // The preview uses a real maximized window; record dimensions to catch
        // restored-size caps that also restrict Windows maximization.
        File.WriteAllText(Path.ChangeExtension(path, ".layout.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            State = WindowState.ToString(), ActualWidth, ActualHeight,
            RestoreWidth = RestoreBounds.Width, RestoreHeight = RestoreBounds.Height,
            WorkAreaWidth = SystemParameters.WorkArea.Width, WorkAreaHeight = SystemParameters.WorkArea.Height,
        }));
        var content = (FrameworkElement)Content;
        var contentOffset = VisualTreeHelper.GetOffset(content);
        var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Background, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            drawing.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(contentOffset.X, contentOffset.Y, content.ActualWidth, content.ActualHeight) }, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        }

        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
        Close();
    }

    private async void HandleClosing(object? sender, CancelEventArgs e)
    {
        viewModel.CancelSpecialKeyProbe();
        if (closing || (!viewModel.IsRecording && !viewModel.IsBusy))
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        // Yield until WPF has finished the current Closing event before Close
        // is called again; a synchronously completed stop must not reenter it.
        await Task.Yield();
        await viewModel.CloseAsync();
        closing = true;
        Close();
    }
}
