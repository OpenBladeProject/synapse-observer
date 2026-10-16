using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SynapseObserver.App;

namespace SynapseObserver.App.Tests;

public sealed class SpecialKeyExpanderTests
{
    [Fact]
    public void ManualCollapseKeepsBindingAndLaterKeySelectionReexpands()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new CollectorViewModel(true);
                model.SetPreviewStage(5);
                var expander = new Expander { Header = "Optional M / Fn key check", DataContext = model };
                expander.SetBinding(Expander.IsExpandedProperty, new Binding(nameof(CollectorViewModel.IsSpecialKeyExpanded))
                {
                    Mode = BindingMode.TwoWay,
                });
                window = new Window { Content = expander, Width = 500, Height = 300 };
                window.Show();
                DrainBindingsAndLayout(window);

                Assert.True(expander.IsExpanded);
                Assert.True(BindingOperations.IsDataBound(expander, Expander.IsExpandedProperty));

                var toggle = FindDescendant<ToggleButton>(expander);
                Assert.NotNull(toggle);
                var provider = Assert.IsAssignableFrom<IToggleProvider>(
                    new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle));
                provider.Toggle();
                DrainBindingsAndLayout(window);
                Assert.False(expander.IsExpanded);
                Assert.True(BindingOperations.IsDataBound(expander, Expander.IsExpandedProperty));

                model.SelectedActivityIndex = model.Activities.ToList().FindIndex(activity => activity.Id == "performance");
                DrainBindingsAndLayout(window);
                Assert.False(expander.IsExpanded);
                Assert.True(BindingOperations.IsDataBound(expander, Expander.IsExpandedProperty));

                model.SelectedActivityIndex = model.Activities.ToList().FindIndex(activity => activity.Id == "special-key-m3");
                DrainBindingsAndLayout(window);
                Assert.True(expander.IsExpanded);
                Assert.True(BindingOperations.IsDataBound(expander, Expander.IsExpandedProperty));

            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally { window?.Close(); }
        }) { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The disconnected WPF check did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void DrainBindingsAndLayout(FrameworkElement root)
    {
        var frame = new DispatcherFrame();
        root.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        root.UpdateLayout();
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject =>
        FindDescendants<T>(root).FirstOrDefault();

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindDescendants<T>(child)) yield return descendant;
        }
    }
}
