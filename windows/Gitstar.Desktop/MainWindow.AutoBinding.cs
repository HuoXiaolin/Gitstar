using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SoftwareBinding = Gitstar.Core.Binding;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private bool discoveringSoftware;

    private async void AutoBindingDialog(string? previewPath = null)
    {
        if (quitting) return;
        if (previewPath == null)
        {
            if (discoveringSoftware) return;
            discoveringSoftware = true;
            try
            {
                model.Status = "正在检测本机软件与用户程序位置…";
                await ScanInstalledAsync();
                if (quitting) return;
            }
            catch (Exception error)
            {
                model.Status = "软件检测未完成：" + error.Message;
                DialogMessage.Show(this, model.Status, "Gitstar");
                return;
            }
            finally { discoveringSoftware = false; }
        }

        var account = CacheKey;
        var source = RepositorySearchSource();
        var suggest = AppMatcher.Prepare(source);
        var rows = installed.Where(app => !settings.Bindings.Any(binding => InstalledVersion.Find([app], binding.AppId) != null))
            .Select(app =>
            {
                var candidates = suggest(app);
                if (candidates.Count == 0)
                {
                    candidates = source.Where(repo => RepositoryLookup.IsProjectMention(repo, app.Name)).Take(8)
                        .Select(repo => new AppMatch(repo.FullName, "简介包含软件名，请确认", false)).ToList();
                }
                var first = candidates.FirstOrDefault();
                return new MatchRow
                {
                    App = app,
                    Choices = candidates.Select(candidate => candidate.Repository).ToArray(),
                    Repository = first?.Repository ?? "",
                    Selected = candidates.Count == 1 && first!.Reliable,
                    Reason = first?.Reason ?? "待匹配"
                };
            }).OrderByDescending(row => row.Selected).ThenBy(row => row.Repository.Length == 0)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var form = new Form(this, "自动匹配 / 批量关联", 1030, 700);
        form.Label("输入关键词筛选仓库，点「搜索」查 GitHub。选中仓库即勾选该行。");
        var filter = form.Text("搜索本机软件");
        var table = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            MinHeight = 180, MaxHeight = 410, Margin = new Thickness(0, 2, 0, 0),
            HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0,
            SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true
        };
        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        checkBox.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "加入本次关联");
        checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding(nameof(MatchRow.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        table.Columns.Add(new DataGridTemplateColumn
        {
            Header = "关联", Width = 55, CellTemplate = new DataTemplate { VisualTree = checkBox }, IsReadOnly = true
        });
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "本机软件", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 95,
            Binding = new System.Windows.Data.Binding(nameof(MatchRow.Name)), IsReadOnly = true
        });
        var versionStyle = new Style(typeof(TextBlock));
        versionStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        versionStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 10, 0)));
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "当前版本", Width = 90, Binding = new System.Windows.Data.Binding(nameof(MatchRow.Version)), IsReadOnly = true,
            ElementStyle = versionStyle,
            HeaderStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                (Style)form.FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)))
            {
                Setters = { new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right) }
            }
        });
        var repositoryColumn = new DataGridTemplateColumn
        {
            Header = "GitHub 仓库", Width = new DataGridLength(1.6, DataGridLengthUnitType.Star), MinWidth = 170,
            CellTemplate = BatchRepositoryPicker.CellTemplate(), IsReadOnly = true
        };
        table.Columns.Add(repositoryColumn);
        var reasonStyle = new Style(typeof(TextBlock));
        reasonStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        reasonStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding(nameof(MatchRow.Reason))));
        var reasonColumn = new DataGridTextColumn
        {
            Header = "匹配依据", Width = 145, Binding = new System.Windows.Data.Binding(nameof(MatchRow.Reason)),
            IsReadOnly = true, ElementStyle = reasonStyle
        };
        table.Columns.Add(reasonColumn);
        form.SizeChanged += (_, _) => reasonColumn.Visibility = form.ActualWidth < 680 ? Visibility.Collapsed : Visibility.Visible;
        form.Fields.Children.Add(table);

        var empty = ProjectText("没有符合筛选条件的软件。", 13, true);
        empty.Margin = new Thickness(4, 12, 0, 0);
        form.Fields.Children.Add(empty);
        Func<string, CancellationToken, Task<GitHubClient.RepositorySearchPage>> search = previewPath == null ? api.SearchRepositories : BatchRepositorySmokeSearch;
        var picker = new BatchRepositoryPicker(form, table, source, search);
        var clear = form.Action("取消勾选", () =>
        {
            foreach (var row in rows) row.Selected = false;
            return Task.CompletedTask;
        });
        clear.IsEnabled = false;
        form.Action("手动添加软件", () => { form.Close(); BindingDialog(); return Task.CompletedTask; });
        form.Action("关闭", () => { form.Close(); return Task.CompletedTask; });
        var save = form.Action("关联所选", async () =>
        {
            table.CommitEdit();
            table.CommitEdit(DataGridEditingUnit.Row, true);
            if (account != CacheKey) throw new IOException("账号已改变，请重新打开关联窗口。");
            var selected = rows.Where(row => row.Selected).ToArray();
            if (selected.Length == 0) throw new IOException("请勾选需要关联的软件。");
            foreach (var row in selected)
            {
                var address = RepositoryLookup.Address(row.Repository);
                if (address.Length == 0) throw new IOException($"请为「{row.Name}」选择仓库或填写完整地址。");
                row.Repository = address;
            }
            form.Close();
            await ApplyBatchBindings(selected, account);
        }, true);

        var view = new ListCollectionView(rows);
        table.ItemsSource = view;
        void UpdateCount()
        {
            var selected = rows.Count(row => row.Selected);
            var valid = rows.Count(row => row.Selected && RepositoryLookup.Address(row.Repository).Length > 0);
            save.Content = selected > 0 ? $"关联所选（{selected}）" : "关联所选";
            save.IsEnabled = valid > 0 && selected == valid;
            clear.IsEnabled = selected > 0;
            var visibleSelected = view.Cast<MatchRow>().Count(row => row.Selected);
            var other = selected > visibleSelected ? $"，含筛选外 {selected - visibleSelected} 项" : "";
            form.Footnote($"显示 {view.Count}/{rows.Count} 项 · 已勾选 {selected} 项{other}");
        }
        void Filter()
        {
            picker.Close();
            var query = filter.Text.Trim();
            view.Filter = item => item is MatchRow row && (query.Length == 0
                || SearchText.Score(row.Name, query) > 0 || row.Repository.Contains(query, StringComparison.OrdinalIgnoreCase));
            empty.Text = rows.Count == 0 ? "没有待关联的软件。" : "没有符合筛选条件的软件。";
            empty.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            table.Visibility = view.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
            UpdateCount();
        }
        filter.TextChanged += (_, _) => Filter();
        foreach (var row in rows) row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MatchRow.Selected) or nameof(MatchRow.Repository)) UpdateCount();
        };
        Filter();
        if (previewPath != null) AttachBatchBindingSmoke(form, table, repositoryColumn, filter, save, picker, rows, previewPath);
        form.ShowDialog();
    }

    private async Task ApplyBatchBindings(IReadOnlyList<MatchRow> selected, string account)
    {
        await Execute("关联", $"批量关联 {selected.Count} 个软件", async ct =>
        {
            var validated = new Dictionary<string, Repository>(StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            var added = 0;
            foreach (var row in selected)
            {
                ct.ThrowIfCancellationRequested();
                if (account != CacheKey) throw new OperationCanceledException("账号已改变。");
                model.Status = "正在关联：" + row.Name;
                try
                {
                    if (!validated.TryGetValue(row.Repository, out var info))
                    {
                        info = repositories.FirstOrDefault(repo => repo.FullName.Equals(row.Repository, StringComparison.OrdinalIgnoreCase))
                            ?? await api.Repo(row.Repository, ct);
                        ct.ThrowIfCancellationRequested();
                        if (account != CacheKey) throw new OperationCanceledException("账号已改变。");
                        validated[row.Repository] = info;
                        if (!repositories.Any(repo => repo.FullName.Equals(info.FullName, StringComparison.OrdinalIgnoreCase)))
                            repositories.Add(info);
                    }
                    if (!settings.Bindings.Any(binding => InstalledVersion.Find([row.App], binding.AppId) != null))
                    {
                        var binding = new SoftwareBinding { AppId = row.App.Id, Repository = info.FullName };
                        InstalledIdentity.Remember(binding, row.App);
                        settings.Bindings.Add(binding);
                        Save();
                        added++;
                    }
                    if (!releases.Any(release => release.Repository.Equals(info.FullName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var found = await api.Releases(info.FullName, ct, settings.Bindings);
                        ct.ThrowIfCancellationRequested();
                        if (account != CacheKey) throw new OperationCanceledException("账号已改变。");
                        releases.AddRange(found);
                        SaveCache();
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    failures.Add(row.Name + "：" + error.Message);
                }
            }
            SaveCache();
            RebuildGroups();
            PlanUpdates();
            Display();
            model.Status = $"批量关联完成 · 新增 {added} 项 · {updates.Count} 个软件可更新"
                + (failures.Count > 0 ? $" · {failures.Count} 项待处理：" + string.Join("；", failures.Take(3)) : "");
        });
    }
}
