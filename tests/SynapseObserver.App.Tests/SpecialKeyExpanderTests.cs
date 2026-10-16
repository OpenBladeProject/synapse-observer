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

[Collection("WPF application")]
public sealed class SpecialKeyExpanderTests(WpfApplicationFixture fixture)
{
    [Fact]
    public void ManualCollapseKeepsBindingAndLaterKeySelectionReexpands()
    {
        fixture.Run(() =>
        {
            Window? window = null;
            try
            {
                var model = new CollectorViewModel(true);
                model.SetPreviewStage(5);
                // Pin the stock WPF template independently of the runner's desktop theme.
                // Keep the real header toggle and its two-way templated-parent binding.
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/PresentationFramework.Aero2;component/themes/aero2.normalcolor.xaml", UriKind.Relative),
                };
                var expander = new Expander
                {
                    Header = "Optional M / Fn key check",
                    DataContext = model,
                    OverridesDefaultStyle = true,
                    Style = (Style)theme[typeof(Expander)],
                };
                expander.Resources.MergedDictionaries.Add(theme);
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
            finally { window?.Close(); }
        });
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
