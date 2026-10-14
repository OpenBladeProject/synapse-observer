using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynapseObserver.App;

public partial class MainWindow : Window
{
    private readonly CollectorViewModel viewModel;
    private bool closing;

    public MainWindow(bool preview)
    {
        InitializeComponent();
        viewModel = new CollectorViewModel(preview);
        DataContext = viewModel;
        Closing += HandleClosing;
    }

    internal void RenderPreview(string path, int stage)
    {
        viewModel.SetPreviewStage(stage);
        Show();
        UpdateLayout();
        var content = (FrameworkElement)Content;
        var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Background, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
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
