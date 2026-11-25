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
        Button batch, TextBlock batchStatus, Button save, BatchRepositoryPicker picker, IReadOnlyList<MatchRow> rows, string path)
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

                void Press(Key key, TextBox? target = null)
                {
                    var input = target ?? editor;
                    var source = PresentationSource.FromVisual(input) ?? throw new IOException("Missing input source");
                    input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                }
                Press(Key.Down);
                Press(Key.Enter);
                var choseLocal = item.Repository == "z7786/wenxi" && item.Selected && !picker.Popup.IsOpen;
                var original = rows.Single(row => row.App.Id == "fixture2");
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

                var duplicate = rows.Single(row => row.App.Id == "fixture5");
                var unmatched = rows.Single(row => row.App.Id == "fixture3");
                foreach (var row in rows) row.Selected = false;
                var emptyBatchDisabled = !batch.IsEnabled;
                filter.Text = "Sync";
                original.Selected = duplicate.Selected = unmatched.Selected = true;
                var namesSearched = new List<string>();
                var pending = new TaskCompletionSource<GitHubClient.RepositorySearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken pendingToken = default;
                batchSearchSmoke = (query, token) =>
                {
                    namesSearched.Add(query);
                    if (query == original.Name) return Task.FromResult(new GitHubClient.RepositorySearchPage(
                        [new("example/sync-app", "跨平台同步工具", false), new("example/sync-fork", "其他候选", false)], 2, false));
                    if (query != unmatched.Name) throw new IOException("Unexpected software search: " + query);
                    pendingToken = token;
                    return pending.Task;
                };
                batch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var checkedNamesOnly = namesSearched.SequenceEqual(new[] { original.Name, unmatched.Name })
                    && !table.Items.Contains(unmatched) && item.BatchSearchPage == null;
                var preservesSelections = original.Repository == "example/sync" && duplicate.Repository == "example/sync"
                    && unmatched.Repository.Length == 0 && original.Selected && duplicate.Selected && unmatched.Selected;
                var requiresChoice = !save.IsEnabled && original.AwaitingRepositoryChoice && duplicate.AwaitingRepositoryChoice
                    && settings.Bindings.Count == 0;
                var resultsArriveEarly = batch.Content as string == "取消搜索" && batchStatus.Text.Contains("2/3", StringComparison.Ordinal)
                    && original.RepositoryAction == "候选 2" && duplicate.RepositoryAction == "候选 2";

                table.UpdateLayout();
                var batchCell = column.GetCellContent(original) ?? throw new IOException("Missing batch candidate cell");
                var batchInput = Find<TextBox>(batchCell) ?? throw new IOException("Missing batch candidate input");
                var rowCandidates = Find<Button>(batchCell) ?? throw new IOException("Missing batch candidate action");
                rowCandidates.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var opensCandidatesWithoutSearch = picker.Popup.IsOpen && namesSearched.Count == 2
                    && Candidates().SequenceEqual(new[] { "example/sync-app", "example/sync-fork" });
                Press(Key.Enter, batchInput);
                var enterNeedsHighlight = original.AwaitingRepositoryChoice && !save.IsEnabled;
                Press(Key.Down, batchInput);
                Press(Key.Enter, batchInput);
                var chosenOnly = original.Repository == "example/sync-app" && !original.AwaitingRepositoryChoice
                    && duplicate.AwaitingRepositoryChoice && unmatched.AwaitingRepositoryChoice && save.IsEnabled
                    && Equals(save.Content, "关联已确认（1）") && settings.Bindings.Count == 0;

                batch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                pending.SetResult(new([new("example/late", "", false)], 1, false));
                await form.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var cancellationPreservesResults = pendingToken.IsCancellationRequested && unmatched.BatchSearchPage == null
                    && original.RepositoryAction == "候选 2" && batchStatus.Text.Contains("已取消", StringComparison.Ordinal);

                item.Selected = true;
                batchSearchSmoke = (query, _) =>
                {
                    namesSearched.Add(query);
                    if (query == item.Name) throw new GitHubRateLimitException("fixture rate limit");
                    return Task.FromResult(new GitHubClient.RepositorySearchPage([], 0, false));
                };
                batch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var stopAtLimit = namesSearched.Skip(2).SequenceEqual(new[] { item.Name })
                    && batchStatus.Text.Contains("搜索受限", StringComparison.Ordinal) && unmatched.BatchSearchPage == null;
                item.Selected = false;
                batch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var resumeAlreadySearched = namesSearched.Skip(2).SequenceEqual(new[] { item.Name, unmatched.Name })
                    && unmatched.Reason.Contains("未找到", StringComparison.Ordinal) && batchStatus.Text.Contains("1 项无匹配", StringComparison.Ordinal);

                var version64 = rows.Single(row => row.App.Id == "fixture6");
                var version86 = rows.Single(row => row.App.Id == "fixture7");
                var primary = rows.Single(row => row.App.Id == "fixture8");
                var secondary = rows.Single(row => row.App.Id == "fixture9");
                foreach (var row in rows) row.Selected = false;
                version64.Selected = version86.Selected = primary.Selected = secondary.Selected = true;
                namesSearched.Clear();
                batchSearchSmoke = (query, _) =>
                {
                    namesSearched.Add(query);
                    var address = query switch
                    {
                        "星图工具" => "example/star-chart",
                        "DocLight" => "example/doc-light",
                        "DocSide" => "example/doc-side",
                        _ => ""
                    };
                    return Task.FromResult(new GitHubClient.RepositorySearchPage(
                        address.Length > 0 ? [new(address, "测试候选", false)] : [], address.Length > 0 ? 1 : 0, false));
                };
                batch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var simplifiedLookup = namesSearched.Where(name => name.StartsWith("星图工具", StringComparison.Ordinal))
                        .SequenceEqual(new[] { version64.Name, "星图工具", version86.Name })
                    && !namesSearched.Contains("AstralOverlay")
                    && version64.BatchSearchPage?.Items.Single().FullName == "example/star-chart"
                    && ReferenceEquals(version64.BatchSearchPage, version86.BatchSearchPage);
                var executableLookup = namesSearched.Where(name => name.StartsWith("旧版文档助手", StringComparison.Ordinal) || name.StartsWith("Doc", StringComparison.Ordinal))
                        .SequenceEqual(new[] { primary.Name, "旧版文档助手", "DocLight", "DocSide" })
                    && namesSearched.All(name => !name.Contains('\\') && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    && primary.BatchSearchPage?.Items.Single().FullName == "example/doc-light"
                    && secondary.BatchSearchPage?.Items.Single().FullName == "example/doc-side";
                var fallbackNeedsChoice = new[] { version64, version86, primary, secondary }
                    .All(row => row.AwaitingRepositoryChoice && row.Repository.Length == 0)
                    && !save.IsEnabled && settings.Bindings.Count == 0;

                filter.Text = "旧版文档助手";
                table.UpdateLayout();
                var fallbackCell = column.GetCellContent(primary) ?? throw new IOException("Missing executable candidate cell");
                var fallbackInput = Find<TextBox>(fallbackCell) ?? throw new IOException("Missing executable candidate input");
                var fallbackAction = Find<Button>(fallbackCell) ?? throw new IOException("Missing executable candidate action");
                fallbackAction.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Press(Key.Down, fallbackInput);
                Press(Key.Enter, fallbackInput);
                var fallbackManualSelection = primary.Repository == "example/doc-light" && !primary.AwaitingRepositoryChoice
                    && secondary.AwaitingRepositoryChoice && Equals(save.Content, "关联已确认（1）") && settings.Bindings.Count == 0;
                form.UpdateLayout();
                var batchBounds = batch.TransformToAncestor((FrameworkElement)form.Content).TransformBounds(new Rect(batch.RenderSize));
                var batchActionFits = batchBounds.Left >= 0 && batchBounds.Right <= ((FrameworkElement)form.Content).ActualWidth;
                var report = new
                {
                    ChineseNameSuggestedForReview = suggested, SoftwareFilter = filtered, ExactLocalMatch = local,
                    QueryRetained = queryKept, KeyboardSelectionChecksRow = choseLocal, LoadingVisible = loading,
                    RowButtonSearchesOnline = remote, StaleResponseDropped = staleDropped, PopupUsesTheme = popupTheme,
                    RemoteSelectionChecksCorrectRow = choseRemote, ErrorVisible = failure, PastedReleaseUrlNormalized = direct,
                    EmptyQueryUsesSoftwareName = fallback, EscapeCloses = escaped, EmptyFilter = empty,
                    FilteringPreservesOtherSelections = stableRows, SaveCountUpdated = validCount, OneClickCheckbox = toggled,
                    EmptyAddressBlocksSave = emptyAddressBlocksSave, SearchButtonFits = rowActionFits,
                    EmptyBatchDisabled = emptyBatchDisabled, CheckedNamesOnly = checkedNamesOnly, PreservesSelections = preservesSelections,
                    RequiresChoice = requiresChoice, ResultsArriveEarly = resultsArriveEarly,
                    OpensCandidatesWithoutSearch = opensCandidatesWithoutSearch, EnterNeedsHighlight = enterNeedsHighlight,
                    ChosenOnly = chosenOnly, CancellationPreservesResults = cancellationPreservesResults, StopAtLimit = stopAtLimit,
                    ResumeAlreadySearched = resumeAlreadySearched, BatchActionFits = batchActionFits,
                    SimplifiedLookup = simplifiedLookup, ExecutableLookup = executableLookup,
                    FallbackNeedsChoice = fallbackNeedsChoice, FallbackManualSelection = fallbackManualSelection, Narrow = narrow
                };
                File.WriteAllText(Path.ChangeExtension(path, ".interaction.json"), JsonSerializer.Serialize(report));
                if (!(suggested && filtered && local && queryKept && choseLocal && loading && remote && staleDropped && popupTheme
                    && choseRemote && failure && direct && fallback && escaped && empty && stableRows && validCount && toggled
                    && emptyAddressBlocksSave && rowActionFits && emptyBatchDisabled && checkedNamesOnly && preservesSelections
                    && requiresChoice && resultsArriveEarly && opensCandidatesWithoutSearch && enterNeedsHighlight && chosenOnly
                    && cancellationPreservesResults && stopAtLimit && resumeAlreadySearched && batchActionFits
                    && simplifiedLookup && executableLookup && fallbackNeedsChoice && fallbackManualSelection))
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
