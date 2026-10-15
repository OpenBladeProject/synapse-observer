using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SynapseObserver.App;

namespace SynapseObserver.App.Tests;

public sealed class SettingEntryViewTests
{
    [Fact]
    public void ChoicePickerUpdatesReplacementCatalogAndUsesNonVirtualizedScrolling()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application();
                try
                {
                    application.Resources["MutedTextBrush"] = Brushes.Gray;
                    var entry = new SettingEntry();
                    entry.Reset(9); // Full charge: On, Off, Other.
                    var view = new SettingEntryView { DataContext = entry };
                    var host = new ContentControl { Content = view };
                    DrainBindingsAndLayout(host);

                    var picker = FindDescendant<ComboBox>(view);
                    Assert.NotNull(picker);
                    Assert.False(ScrollViewer.GetCanContentScroll(picker));
                    Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(picker));
                    Assert.Equal(280, picker.MaxDropDownHeight);
                    Assert.IsType<StackPanel>(picker.ItemsPanel.LoadContent());
                    Assert.Equal(3, picker.Items.Count);

                    entry.Reset(10); // Keyboard brightness: five levels plus Other.
                    DrainBindingsAndLayout(host);
                    Assert.Equal(6, picker.Items.Count);
                    Assert.Equal("Other state…", picker.Items[5].ToString());
                }
                finally
                {
                    application.Shutdown();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void DrainBindingsAndLayout(FrameworkElement root)
    {
        var frame = new DispatcherFrame();
        root.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        root.Measure(new Size(420, 600));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } descendant) return descendant;
        }

        return null;
    }
}
