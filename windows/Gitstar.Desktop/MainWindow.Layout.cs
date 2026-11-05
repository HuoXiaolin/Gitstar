using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void ArrangeMainLayout()
    {
        if (ModuleNavigation == null || ActualWidth <= 0) return;
        var compact = ActualWidth < 1020;
        Grid.SetRow(ModuleNavigation, compact ? 1 : 0);
        Grid.SetColumn(ModuleNavigation, compact ? 0 : 1);
        Grid.SetColumnSpan(ModuleNavigation, compact ? 3 : 1);
        ModuleNavigation.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(12, 0, 12, 0);
        ModuleNavigation.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        Grid.SetColumnSpan(UpdateSearchHost, compact ? 5 : 1);
        UpdateSearchHost.Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 12, 0);
        foreach (var control in new FrameworkElement[] { GroupFilter, ChannelFilter, SortFilter, UpdateActions })
            Grid.SetRow(control, compact ? 1 : 0);
        UpdatesToolbar.ColumnDefinitions[0].Width = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        UpdatesToolbar.ColumnDefinitions[3].Width = compact ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Grid.SetColumn(GroupFilter, compact ? 0 : 1);
        Grid.SetColumn(ChannelFilter, compact ? 1 : 2);
        Grid.SetColumn(SortFilter, compact ? 2 : 3);
        Grid.SetColumnSpan(ProjectSearchHost, compact ? 3 : 1);
        ProjectSearchHost.Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 12, 0);
        Grid.SetRow(ProjectFilter, compact ? 1 : 0);
        Grid.SetColumn(ProjectFilter, compact ? 0 : 1);
        ProjectFilter.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Grid.SetRow(ProjectToolbarActions, compact ? 1 : 0);
        Grid.SetColumn(ProjectToolbarActions, compact ? 1 : 2);
        Grid.SetColumnSpan(ProjectToolbarActions, compact ? 2 : 1);
        ProjectToolbarActions.HorizontalAlignment = HorizontalAlignment.Right;
    }

    // Native layout checks use the real template and generic fixtures, including long account/version labels.
    private void VerifyMainLayout(string path)
    {
        settings.Account = "example-developer-with-a-long-account"; AccountLabel();
        ArrangeMainLayout(); UpdateLayout();
        Rect Bounds(FrameworkElement element) => new(element.TranslatePoint(new Point(), this), element.RenderSize);
        bool Inside(FrameworkElement element)
        {
            var bounds = Bounds(element);
            return bounds.Left >= 0 && bounds.Right <= ActualWidth + 1 && bounds.Top >= 0 && bounds.Bottom <= ActualHeight + 1;
        }
        var compact = ActualWidth < 1020;
        var nav = Bounds(ModuleNavigation);
        var account = Bounds(AccountActions);
        if (!new[] { RecentButton, LocalButton, ProjectsButton, RankingsButton }.All(Inside) || nav.IntersectsWith(account))
            throw new IOException("Module navigation overlaps account actions or escapes the main window.");
        var selected = rankingsPage ? RankingsButton : projectsPage ? ProjectsButton : localPage ? LocalButton : RecentButton;
        if (new[] { RecentButton, LocalButton, ProjectsButton, RankingsButton }.Where(button => button != selected)
            .Any(button => button.Background is SolidColorBrush brush && brush.Color.A != 0))
            throw new IOException("An inactive navigation item retained a selected-looking background.");
        if (UpdatesToolbar.IsVisible && (Search.ActualWidth < 180 || !Inside(UpdateActions)))
            throw new IOException("Update search or actions are clipped.");
        var visibleFilters = new FrameworkElement[] { GroupFilter, ChannelFilter, SortFilter }.Where(item => item.IsVisible).ToArray();
        if (UpdatesToolbar.IsVisible && visibleFilters.Any(item => !Inside(item) || Bounds(item).IntersectsWith(Bounds(UpdateActions))))
            throw new IOException("Update filters overlap the actions.");
        if (ProjectsHost.IsVisible && (ProjectSearch.ActualWidth < 180 || !Inside(ProjectToolbarActions)))
            throw new IOException("Project search or actions are clipped.");
        IEnumerable<System.Windows.Controls.Primitives.ScrollBar> ScrollBars(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is System.Windows.Controls.Primitives.ScrollBar bar && bar.IsVisible) yield return bar;
                foreach (var nested in ScrollBars(child)) yield return nested;
            }
        }
        var scrollbars = ScrollBars(this).ToArray();
        if (scrollbars.Any(bar => (bar.Orientation == Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight) > 10.1))
            throw new IOException("A visible scrollbar still uses the wide system layout.");
        File.WriteAllText(Path.ChangeExtension(path, ".responsive.json"), JsonSerializer.Serialize(new
        {
            Width = ActualWidth, Height = ActualHeight, Compact = compact,
            NavigationVisible = true, AccountSeparated = true,
            SearchWidth = projectsPage ? ProjectSearch.ActualWidth : Search.ActualWidth,
            FourModuleThemesPreserved = true, SlimScrollbars = true, Scrollbars = scrollbars.Length
        }));
    }

    private async Task VerifyMainLayoutResize(string path)
    {
        var originalWidth = Width;
        var snapshots = new List<object>();
        foreach (var width in new[] { 1180d, 940d, originalWidth })
        {
            Width = width;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            VerifyMainLayout(path);
            snapshots.Add(new { Width = ActualWidth, Compact = ActualWidth < 1020, Passed = true });
        }
        File.WriteAllText(Path.ChangeExtension(path, ".resize.json"), JsonSerializer.Serialize(snapshots));
    }
}
