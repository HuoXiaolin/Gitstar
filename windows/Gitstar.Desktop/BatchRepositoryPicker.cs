using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Gitstar.Desktop;

internal sealed class BatchRepositoryPicker
{
    private static readonly object EditorMarker = new();
    private static readonly object SearchMarker = new();
    private readonly Repository[] local;
    private readonly Func<string, CancellationToken, Task<GitHubClient.RepositorySearchPage>> searchOnline;
    private readonly Popup popup = new() { Placement = PlacementMode.Bottom, AllowsTransparency = true, Focusable = false };
    private readonly ListBox results = new() { MaxHeight = 240, BorderThickness = new Thickness(0), Focusable = false };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(8, 6, 8, 5) };
    private TextBox? editor;
    private MatchRow? row;
    private CancellationTokenSource? request;
    private bool applying;
    private bool searchingFromButton;

    public BatchRepositoryPicker(Form form, DataGrid table, Repository[] local,
        Func<string, CancellationToken, Task<GitHubClient.RepositorySearchPage>> searchOnline)
    {
        this.local = local;
        this.searchOnline = searchOnline;
        results.SetResourceReference(Control.BackgroundProperty, "Surface");
        AutomationProperties.SetName(results, "仓库候选");
        results.ItemContainerStyle = new Style(typeof(ListBoxItem))
        {
            Setters =
            {
                new Setter(Control.FocusableProperty, false),
                new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)
            }
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var content = new StackPanel();
        content.Children.Add(results);
        content.Children.Add(status);
        var border = new Border { Child = content, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4) };
        border.SetResourceReference(Border.BackgroundProperty, "Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Edge");
        popup.Child = border;
        form.Fields.Children.Add(popup);

        table.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(EditorChanged));
        table.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(SearchClicked));
        table.GotKeyboardFocus += (_, e) =>
        {
            if (!applying && !searchingFromButton && e.NewFocus is TextBox { DataContext: MatchRow item } input && ReferenceEquals(input.Tag, EditorMarker)
                && !item.Selected && (item.Repository.Length == 0 || item.Choices.Contains(item.Repository, StringComparer.OrdinalIgnoreCase)))
                Open(input, item);
        };
        table.PreviewKeyDown += EditorKeyDown;
        results.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(results, source) is ListBoxItem { Tag: Repository repo })
            {
                e.Handled = true;
                Choose(repo.FullName, "已选择仓库");
            }
        };
        table.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.HorizontalChange != 0 || e.VerticalChange != 0) Close();
        }));
        form.LocationChanged += (_, _) => Close();
        form.SizeChanged += (_, _) => Close();
        form.Deactivated += (_, _) => Close();
        form.Closed += (_, _) => Close();
    }

    public static DataTemplate CellTemplate()
    {
        var field = new FrameworkElementFactory(typeof(TextBox));
        field.SetValue(FrameworkElement.TagProperty, EditorMarker);
        field.SetValue(FrameworkElement.MinWidthProperty, 0.0);
        field.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        field.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        field.SetValue(AutomationProperties.NameProperty, "输入仓库名称、关键词或地址");
        field.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(MatchRow.Repository))
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        field.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(MatchRow.Reason)));

        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(FrameworkElement.TagProperty, SearchMarker);
        button.SetValue(ContentControl.ContentProperty, "搜索");
        button.SetValue(AutomationProperties.NameProperty, "搜索 GitHub 仓库");
        button.SetValue(FrameworkElement.ToolTipProperty, "搜索 GitHub。留空时使用本机软件名称。");
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        button.SetValue(Control.PaddingProperty, new Thickness(9, 4, 9, 4));
        button.SetValue(DockPanel.DockProperty, Dock.Right);

        var cell = new FrameworkElementFactory(typeof(DockPanel));
        cell.AppendChild(button);
        cell.AppendChild(field);
        return new DataTemplate { VisualTree = cell };
    }

    private void EditorChanged(object sender, TextChangedEventArgs e)
    {
        if (applying || e.OriginalSource is not TextBox { DataContext: MatchRow item } input
            || !ReferenceEquals(input.Tag, EditorMarker) || !input.IsKeyboardFocused) return;
        var address = RepositoryLookup.Address(input.Text);
        item.Selected = address.Length > 0;
        item.Reason = address.Length > 0 ? "已填写仓库地址" : input.Text.Trim().Length == 0 ? "待匹配" : "待确认仓库";
        Open(input, item, refresh: true);
    }

    private async void SearchClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: MatchRow item } button || !ReferenceEquals(button.Tag, SearchMarker)
            || VisualTreeHelper.GetParent(button) is not DockPanel cell || cell.Children.OfType<TextBox>().FirstOrDefault() is not { } input) return;
        e.Handled = true;
        searchingFromButton = true;
        try { input.Focus(); }
        finally { searchingFromButton = false; }
        Open(input, item);
        await SearchAsync();
    }

    private async void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not TextBox { DataContext: MatchRow item } input || !ReferenceEquals(input.Tag, EditorMarker)) return;
        if (e.Key == Key.Escape && popup.IsOpen)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up)
        {
            if (!ReferenceEquals(row, item) || !popup.IsOpen) Open(input, item);
            if (results.Items.Count > 0)
            {
                results.SelectedIndex = Math.Clamp(results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, results.Items.Count - 1);
                results.ScrollIntoView(results.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (editor == input && results.SelectedItem is ListBoxItem { Tag: Repository repo })
            {
                Choose(repo.FullName, "已选择仓库");
                return;
            }
            if (!ReferenceEquals(row, item) || !popup.IsOpen) Open(input, item);
            var address = RepositoryLookup.Address(input.Text);
            if (address.Length > 0) Choose(address, "已填写仓库地址");
            else await SearchAsync();
        }
    }

    private void Open(TextBox input, MatchRow item, bool refresh = false)
    {
        if (!refresh && popup.IsOpen && editor == input && row == item) return;
        if (editor != input || row != item)
        {
            Close();
            editor = input;
            row = item;
            input.Unloaded += EditorUnloaded;
            input.DataContextChanged += EditorReused;
            input.LostKeyboardFocus += EditorLostFocus;
            popup.PlacementTarget = (UIElement?)VisualTreeHelper.GetParent(input) ?? input;
            popup.Width = Math.Min(640, Math.Max(200, ((FrameworkElement)popup.PlacementTarget).ActualWidth));
        }
        CancelSearch();
        Render(input.Text.Trim());
        popup.IsOpen = true;
    }

    private void Render(string query, GitHubClient.RepositorySearchPage? page = null)
    {
        var suggestions = row is { Selected: false, Choices.Count: > 0 }
            && (query.Length == 0 || row.Choices.Contains(query, StringComparer.OrdinalIgnoreCase));
        var known = suggestions
            ? row!.Choices.Select(name => local.FirstOrDefault(repo => repo.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? new Repository(name, "", false))
            : RepositoryLookup.Filter(local, query.Length == 0 ? row?.Name ?? "" : query, 8);
        var choices = known.Select(repo => (Repo: repo, Source: "本机"))
            .Concat(page?.Items.Select(repo => (Repo: repo, Source: "GitHub")) ?? [])
            .DistinctBy(candidate => candidate.Repo.FullName, StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
        results.Items.Clear();
        foreach (var (repo, source) in choices)
        {
            var body = new StackPanel();
            body.Children.Add(Line(repo.FullName + "  ·  " + source + (repo.Private ? " · 私有" : ""), 13, "Ink"));
            if (repo.Description.Length > 0) body.Children.Add(Line(repo.Description, 12, "Muted"));
            results.Items.Add(new ListBoxItem { Tag = repo, Content = body, Padding = new Thickness(8, 5, 8, 5), ToolTip = repo.FullName + "\n" + repo.Description });
        }
        results.Visibility = choices.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        status.Text = page != null && page.Items.Count == 0 ? "GitHub 未找到匹配，可更换关键词或粘贴地址。"
            : choices.Length == 0 ? "本机没有候选，按回车搜索 GitHub。"
            : page?.Incomplete == true ? "GitHub 返回了部分结果，可缩小关键词继续搜索。"
            : "点击候选或按 ↓、回车选中仓库。";
    }

    private static TextBlock Line(string text, int size, string color)
    {
        var line = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        line.SetResourceReference(TextBlock.ForegroundProperty, color);
        return line;
    }

    private async Task SearchAsync()
    {
        if (editor == null || row == null || request != null) return;
        var query = editor.Text.Trim();
        if (query.Length == 0) query = row.Name.Trim();
        if (!RepositoryLookup.CanSearch(query))
        {
            status.Text = "请输入 2 至 200 个字符的关键词或仓库地址。";
            return;
        }

        Render(query);
        status.Text = "正在搜索 GitHub…";
        using var search = new CancellationTokenSource();
        request = search;
        try
        {
            var page = await searchOnline(query, search.Token);
            if (request == search) Render(query, page);
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (request == search) status.Text = "GitHub 搜索未完成。 " + error.Message;
        }
        finally
        {
            if (request == search) request = null;
        }
    }

    private void Choose(string address, string basis)
    {
        if (editor == null || row == null) return;
        applying = true;
        try { row.ChooseRepository(address, basis); }
        finally { applying = false; Close(); }
    }

    internal Popup Popup => popup;
    internal ListBox Results => results;
    internal string Status => status.Text;

    private void EditorUnloaded(object sender, RoutedEventArgs e) => Close();
    private void EditorReused(object sender, DependencyPropertyChangedEventArgs e) => Close();
    private void EditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Close();

    private void CancelSearch()
    {
        var pending = request;
        request = null;
        if (pending != null) ApplicationExit.Cancel(pending);
    }

    internal void Close()
    {
        CancelSearch();
        if (editor != null)
        {
            editor.Unloaded -= EditorUnloaded;
            editor.DataContextChanged -= EditorReused;
            editor.LostKeyboardFocus -= EditorLostFocus;
        }
        editor = null;
        row = null;
        popup.IsOpen = false;
        popup.PlacementTarget = null;
    }
}
