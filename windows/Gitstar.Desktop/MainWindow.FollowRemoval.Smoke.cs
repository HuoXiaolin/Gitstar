using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task FollowRemovalSmoke(string path)
    {
        const string repo = "example/retired-editor";
        const string other = "example/current-tool";
        var transport = new FollowRemovalTransport(repo, other);
        api.Dispose();
        api = new(settings, transport);
        settings.Account = "example-user";
        settings.KeepLatestPackages = false;
        settings.NotificationIntervalMinutes = 15;
        AccountLabel();

        var app = new InstalledApp("fixture-editor", "示例编辑器", "1.0.0", store.Root);
        var binding = new Binding { AppId = app.Id, Repository = repo };
        settings.Bindings = [binding];
        settings.ExtraRepositories = [other];
        settings.LocalGroups = [new("local-editors", "编辑器", "本地", [repo, other])];
        SpecialFollows.Set(settings, repo, true);
        installed = [app];
        SaveInstalledSnapshot();
        matchedScanSmoke = _ => Task.FromResult(new List<InstalledApp> { app });
        repositories = [new(other, "", false)];
        releases = [new(10, repo, "v2.0.0", "旧版记录", "缓存中的发行说明", false, DateTimeOffset.UtcNow, "", []),
            new(11, other, "v2.0.0", "当前项目", "", false, DateTimeOffset.UtcNow, "", [])];
        var installer = Path.Combine(store.Root, "editor-setup.exe");
        File.WriteAllText(installer, "retained fixture");
        packages = [new(repo, 10, "v2.0.0", false, DateTimeOffset.UtcNow, "editor-setup.exe", installer, store.Root, 16, "", "windows/x64")];
        SavePackages();
        store.Set(PendingInstallationsKey + "/legacy-imported", true);
        PlanUpdates();
        RebuildGroups(false);
        ChannelFilter.SelectedIndex = 1;
        Page(false);
        var card = model.Cards.Single(item => item.Repository == repo);

        var scan = RefreshAsync(background: true);
        await transport.ReleaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var actionVisible = false;
        var cancelKeptCard = false;
        var immediate = false;
        var requestsBeforeRemoval = transport.TargetRequests;
        var interaction = Dispatcher.InvokeAsync(() =>
        {
            var manage = Application.Current.Windows.OfType<Form>().Single(form => form.Title == "Gitstar · 关注与分组管理");
            try
            {
                var button = manage.Buttons.Children.OfType<Button>().Single(item => item.Name == "RemoveFollowedProject");
                actionVisible = button.IsVisible && api.Token.Length == 0;
                RespondToRemoval("取消");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                cancelKeptCard = manage.IsVisible && model.Cards.Any(item => item.Repository == repo) && settings.FollowHistory.Count == 0;
                Capture(manage, path);
                RespondToRemoval("确定");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                immediate = !manage.IsVisible && !scan.IsCompleted && model.Cards.All(item => item.Repository != repo)
                    && updates.All(update => update.Release.Repository != repo) && transport.TargetRequests == requestsBeforeRemoval;
            }
            finally
            {
                if (manage.IsVisible) manage.Close();
            }
        }, DispatcherPriority.ApplicationIdle);
        Options_Click(new Button { Tag = card }, new RoutedEventArgs());
        transport.AllowRelease.TrySetResult();
        await interaction.Task;
        await scan.WaitAsync(TimeSpan.FromSeconds(6));
        var lateResultIgnored = releases.All(release => release.Repository != repo) && repositories.All(item => item.FullName != repo);

        settings = store.Get("settings", new Settings());
        LoadCache();
        PlanUpdates();
        RebuildGroups();
        var retained = settings.Bindings.Single().Repository == repo && packages.Single().Repository == repo && File.Exists(installer);
        var afterReload = FollowLists.IsRemoved(settings, repo) && !model.Cards.Any(item => item.Repository == repo)
            && releases.All(release => release.Repository != repo) && model.Groups.Single(group => group.Id == SpecialFollows.GroupId).Count == 0;

        api.Token = "offline-fixture";
        await RefreshAsync(background: true).WaitAsync(TimeSpan.FromSeconds(6));
        var afterSync = transport.StarReads == 1 && transport.TargetRequests == requestsBeforeRemoval && transport.RemoteWrites == 0
            && model.Cards.All(item => item.Repository != repo) && model.Cards.Any(item => item.Repository == other)
            && model.Groups.Single(group => group.Id == "GitHub/fixture-list").Count == 1;
        var record = settings.FollowHistory.Single(entry => entry.Repository == repo);
        await RestoreFollowEntry(record, FollowKind.Local, CancellationToken.None);
        var restored = record.Kind == FollowKind.RemovedFromList && !FollowLists.IsRemoved(settings, repo)
            && settings.ExtraRepositories.Contains(repo) && settings.LocalGroups[0].Repositories.Contains(repo)
            && RepositoryChecks.Targets(true, [], settings).Contains(repo) && transport.RemoteWrites == 0;
        var passed = actionVisible && cancelKeptCard && immediate && lateResultIgnored && retained && afterReload && afterSync && restored;
        File.WriteAllText(Path.ChangeExtension(path, ".follow-removal.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, ActionVisibleWithoutLogin = actionVisible, CancelKeptCard = cancelKeptCard, RemovedWhileScanning = immediate,
            LateReleaseResultIgnored = lateResultIgnored, KeptBindingAndInstaller = retained, RemovedAfterCacheReload = afterReload,
            RemovedAfterReturningStarAndGroup = afterSync, RestoredFromHistory = restored, transport.RemoteWrites
        }));
        if (!passed) throw new IOException("Followed list removal regression.");
    }

    private void RespondToRemoval(string label)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            var confirm = Application.Current.Windows.OfType<Form>().Single(form => form.Title == "Gitstar · 从列表移除");
            confirm.Buttons.Children.OfType<Button>().Single(button => button.Content?.ToString() == label)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, DispatcherPriority.ApplicationIdle);
    }

    private static void Capture(Form form, string path)
    {
        form.UpdateLayout();
        var root = (FrameworkElement)form.Content;
        var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        png.Save(file);
    }

    private sealed class FollowRemovalTransport(string removed, string other) : HttpMessageHandler
    {
        public TaskCompletionSource ReleaseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int TargetRequests { get; private set; }
        public int RemoteWrites { get; private set; }
        public int StarReads { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/user/starred")
            {
                StarReads++;
                return Json(new[] { Repo(removed), Repo(other) });
            }
            if (path == "/graphql")
            {
                var group = new { id = "fixture-list", name = "GitHub 列表", items = Page(new[] { new { nameWithOwner = removed }, new { nameWithOwner = other } }) };
                return Json(new { data = new { viewer = new { lists = Page(new[] { group }) } } });
            }
            if (request.Method != HttpMethod.Get)
            {
                RemoteWrites++;
                throw new IOException("This fixture only supports GitHub reads.");
            }
            if (path == "/repos/" + AppInfo.Repository + "/releases/latest") return new(HttpStatusCode.NotFound);
            if (path.StartsWith("/repos/" + removed, StringComparison.Ordinal))
            {
                TargetRequests++;
                if (path.EndsWith("/releases", StringComparison.Ordinal))
                {
                    ReleaseStarted.TrySetResult();
                    await AllowRelease.Task.WaitAsync(ct);
                    return Json(new[] { Release(21) });
                }
                return new(HttpStatusCode.NotFound);
            }
            if (path == "/repos/" + other) return Json(Repo(other));
            if (path == "/repos/" + other + "/releases/latest") return Json(Release(22));
            if (path == "/repos/" + other + "/releases") return Json(new[] { Release(22) });
            throw new IOException("Unexpected fixture request " + path);
        }

        private static object Repo(string name) => new { full_name = name, @private = false };
        private static object Page<T>(T[] nodes) => new { nodes, pageInfo = new { hasNextPage = false } };
        private static object Release(long id) => new
        {
            id, tag_name = "v2.1.0", name = "测试版本", body = "", draft = false, prerelease = false,
            published_at = "2026-11-24T00:00:00Z", html_url = "", assets = Array.Empty<object>()
        };
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
