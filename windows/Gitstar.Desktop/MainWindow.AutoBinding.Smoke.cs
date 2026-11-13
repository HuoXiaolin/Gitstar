using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Func<string, CancellationToken, Task<GitHubClient.RepositorySearchPage>>? batchSearchSmoke;

    private Task<GitHubClient.RepositorySearchPage> BatchRepositorySmokeSearch(string query, CancellationToken ct) =>
        (batchSearchSmoke ?? throw new InvalidOperationException("Missing batch repository fixture"))(query, ct);

    private void AttachBatchBindingSmoke(Form form, DataGrid table, DataGridTemplateColumn column, TextBox filter,
        Button save, BatchRepositoryPicker picker, IReadOnlyList<MatchRow> rows, string path)
    {
        var narrow = Environment.GetCommandLineArgs().Contains("--narrow");
        if (narrow) form.Width = 540;
        form.ContentRendered += (_, _) => form.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var item = rows.Single(row => row.Name == "文析助手");
                var suggested = item.Repository == "z7786/wenxi" && !item.Selected && item.Reason.Contains("简介");
                filter.Text = "文析";
                table.UpdateLayout();
                var filtered = table.Items.Count == 1 && ReferenceEquals(table.Items[0], item);
                var content = column.GetCellContent(item) ?? throw new IOException("Missing repository cell");
                var editor = Find<TextBox>(content) ?? throw new IOException("Missing repository input");
                var search = Find<Button>(content) ?? throw new IOException("Missing row search action");
                if (!editor.Focus()) throw new IOException("Repository input cannot take focus");
                editor.Text = "wenxi";
                var names = Candidates();
                var local = picker.Popup.IsOpen && names.Length > 0 && names[0] == "z7786/wenxi"
                    && names.All(name => name.Contains("wenxi", StringComparison.OrdinalIgnoreCase));
                var queryKept = editor.Text == "wenxi" && item.Repository == "wenxi" && !item.Selected;

                void Press(Key key)
                {
                    var source = PresentationSource.FromVisual(editor) ?? throw new IOException("Missing input source");
                    editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                }
                Press(Key.Down);
                Press(Key.Enter);
                var choseLocal = item.Repository == "z7786/wenxi" && item.Selected && !picker.Popup.IsOpen;
                var original = rows.Single(row => row.Name == "Sync");
                var otherSelection = original.Selected;
                var slow = new TaskCompletionSource<GitHubClient.RepositorySearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken slowToken = default;
                var searches = new List<string>();
                batchSearchSmoke = (query, token) =>
                {
                    searches.Add(query);
                    if (query == "slow fixture") { slowToken = token; return slow.Task; }
                    if (query == "failure fixture") throw new IOException("fixture network unavailable");
                    return Task.FromResult(new GitHubClient.RepositorySearchPage(
                        [new("example/asterlink", "测试候选，来自 GitHub 搜索", false)], 1, false));
                };
                editor.Text = "slow fixture";
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var loading = picker.Status == "正在搜索 GitHub…";
                editor.Text = "asterlink";
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var remote = Candidates().Contains("example/asterlink") && editor.Text == "asterlink" && !item.Selected;
                slow.SetResult(new([new("example/stale", "", false)], 1, false));
                await form.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var staleDropped = slowToken.IsCancellationRequested && !Candidates().Contains("example/stale");
                await form.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var popupTheme = picker.Results.Background == form.FindResource("Surface");
                var result = picker.Results.Items.OfType<ListBoxItem>().Single(row => ((Repository)row.Tag).FullName == "example/asterlink");
                var mouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent };
                result.RaiseEvent(mouseDown);
                var choseRemote = item.Repository == "example/asterlink" && item.Selected && original.Selected == otherSelection;

                editor.Text = "failure fixture";
                Press(Key.Enter);
                var failure = picker.Status.Contains("fixture network unavailable", StringComparison.Ordinal) && !item.Selected;
                editor.Text = "https://github.com/z7786/wenxi/releases/latest";
                Press(Key.Enter);
                var direct = item.Repository == "z7786/wenxi" && item.Selected && searches.Count == 3;
                editor.Text = "";
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var fallback = searches.LastOrDefault() == item.Name;
                Press(Key.Escape);
                var escaped = !picker.Popup.IsOpen;
                var checkbox = Find<CheckBox>(table.Columns[0].GetCellContent(item)) ?? throw new IOException("Missing association checkbox");
                ((System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.CheckBoxAutomationPeer(checkbox)).Toggle();
                var toggled = item.Selected && checkbox.IsChecked == true;
                var emptyAddressBlocksSave = !save.IsEnabled;
                editor.Text = "z7786/wenxi";
                filter.Text = "unmatched test name";
                var empty = table.Visibility == Visibility.Collapsed && table.Items.Count == 0;
                filter.Text = "";
                table.UpdateLayout();
                var stableRows = table.Items.Count == rows.Count && original.Selected == otherSelection;
                var validCount = save.IsEnabled && save.Content is string label && label.Contains('（');
                var shownSearch = Find<Button>(column.GetCellContent(item) ?? throw new IOException("Missing repository cell after clearing filter"))
                    ?? throw new IOException("Missing search button after clearing filter");
                var bounds = shownSearch.TransformToAncestor(table).TransformBounds(new Rect(shownSearch.RenderSize));
                var rowActionFits = bounds.Left >= 0 && bounds.Right <= table.ActualWidth + 1;
                var report = new
                {
                    ChineseNameSuggestedForReview = suggested, SoftwareFilter = filtered, ExactLocalMatch = local,
                    QueryRetained = queryKept, KeyboardSelectionChecksRow = choseLocal, LoadingVisible = loading,
                    RowButtonSearchesOnline = remote, StaleResponseDropped = staleDropped, PopupUsesTheme = popupTheme,
                    RemoteSelectionChecksCorrectRow = choseRemote, ErrorVisible = failure, PastedReleaseUrlNormalized = direct,
                    EmptyQueryUsesSoftwareName = fallback, EscapeCloses = escaped, EmptyFilter = empty,
                    FilteringPreservesOtherSelections = stableRows, SaveCountUpdated = validCount, OneClickCheckbox = toggled,
                    EmptyAddressBlocksSave = emptyAddressBlocksSave, SearchButtonFits = rowActionFits, Narrow = narrow
                };
                File.WriteAllText(Path.ChangeExtension(path, ".interaction.json"), JsonSerializer.Serialize(report));
                if (!(suggested && filtered && local && queryKept && choseLocal && loading && remote && staleDropped && popupTheme
                    && choseRemote && failure && direct && fallback && escaped && empty && stableRows && validCount && toggled
                    && emptyAddressBlocksSave && rowActionFits))
                    throw new IOException("Batch repository interaction did not pass");

                var root = (FrameworkElement)form.Content;
                var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            catch (Exception error) { File.WriteAllText(Path.ChangeExtension(path, ".error.txt"), error.ToString()); }
            finally { form.Close(); }
        }), DispatcherPriority.ApplicationIdle);

        string[] Candidates() => picker.Results.Items.OfType<ListBoxItem>().Select(row => ((Repository)row.Tag).FullName).ToArray();
        static T? Find<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T match) return match;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (Find<T>(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
            return null;
        }
    }
}
