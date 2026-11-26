using System.Net;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Gitstar.Desktop;
public partial class MainWindow
{
    private async Task CheckRankingSearchFeedback(string path, RankingSmokeTransport transport)
    {
        var initial = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallback = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.SearchOverride = query =>
        {
            if (query.Contains("\"desktools\"", StringComparison.Ordinal))
            {
                fallbackStarted.TrySetResult();
                return fallback.Task;
            }
            return initial.Task;
        };

        Task Search(string text)
        {
            rankingKind = RankingKind.Search;
            rankingQuery = rankingSearch.Text = text;
            rankingSearchRoute = null;
            rankingPeriod = 0;
            rankingPage = 1;
            rankingData = null;
            return LoadRankings(true);
        }

        bool Loading() => rankingProgressBar.IsIndeterminate && rankingProgressBar.IsVisible
            && model.Count == "搜索中…" && rankingNoticeText.Text.StartsWith("正在", StringComparison.Ordinal);
        var request = Search("desk tools");
        UpdateLayout();
        var showsLoading = !request.IsCompleted && Loading();

        initial.SetResult(RankingSmokeTransport.Page());
        await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var showsFallback = !request.IsCompleted && Loading() && rankingNoticeText.Text.Contains("补搜", StringComparison.Ordinal);
        SaveRankingScreenshot(Path.ChangeExtension(path, ".searching.png"));
        fallback.SetResult(RankingSmokeTransport.Page("Expanded repository result"));
        await request;
        var showsResults = rankingData?.Items.Single().Repository.Description == "Expanded repository result"
            && !rankingProgressBar.IsIndeterminate && model.Count == "1 个项目" && rankingNotice.Visibility == Visibility.Collapsed;

        transport.SearchOverride = _ => Task.FromResult(RankingSmokeTransport.Page());
        await Search("@empty-fixture");
        var completedEmpty = model.Count == "0 个项目" && rankingNoticeText.Text.Contains("没有匹配", StringComparison.Ordinal)
            && rankingProgressBar.Visibility == Visibility.Collapsed;

        transport.SearchOverride = _ => Task.FromResult(RankingSmokeTransport.Page(incomplete: true));
        await Search("@incomplete-fixture");
        var incomplete = model.Count == "未完成" && rankingNoticeText.Text.Contains("不完整", StringComparison.Ordinal);

        transport.SearchOverride = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("fixture temporarily unavailable")
        });
        await Search("@offline-fixture");
        var failure = model.Count == "未完成" && rankingNoticeText.Text.Contains("搜索未完成", StringComparison.Ordinal)
            && !rankingNoticeText.Text.Contains("没有匹配", StringComparison.Ordinal) && rankingRefresh.IsEnabled;

        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.SearchOverride = query => query.Contains("old-fixture", StringComparison.Ordinal) ? late.Task
            : Task.FromResult(RankingSmokeTransport.Page("New search result"));
        var oldRequest = Search("@old-fixture");
        await Search("@new-fixture");
        late.SetResult(RankingSmokeTransport.Page());
        await oldRequest;
        var obsoleteResultIgnored = rankingQuery == "@new-fixture" && rankingData?.Items.Single().Repository.Description == "New search result"
            && rankingNotice.Visibility == Visibility.Collapsed;

        transport.SearchOverride = null;
        var report = new { ShowsLoading = showsLoading, ShowsFallback = showsFallback, ShowsResults = showsResults,
            CompletedEmpty = completedEmpty, Incomplete = incomplete, Failure = failure, ObsoleteResultIgnored = obsoleteResultIgnored };
        File.WriteAllText(Path.ChangeExtension(path, ".search-states.json"), JsonSerializer.Serialize(report));
        if (!(showsLoading && showsFallback && showsResults && completedEmpty && incomplete && failure && obsoleteResultIgnored))
            throw new IOException("Ranking search feedback regression.");
    }

    private void SaveRankingScreenshot(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private sealed class RankingSmokeTransport:HttpMessageHandler
    {
        public bool Starred;
        public int SearchRequests;
        public Func<string, Task<HttpResponseMessage>>? SearchOverride;
        private static object Repo(string description) =>
            new { full_name = "example/desk-tools", description, @private = false, node_id = "fixture-node", stargazers_count = 25431, language = "C#", created_at = "2026-10-01T00:00:00Z" };

        public static HttpResponseMessage Page(string? description = null, bool incomplete = false) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    total_count = description == null ? 0 : 1,
                    incomplete_results = incomplete,
                    items = description == null ? [] : new[] { Repo(description) }
                }))
            };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var path=request.RequestUri!.AbsolutePath;
            if(path=="/repos/example/desk-tools")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(Repo("A desktop tools project")))});
            if(path=="/user/starred/example/desk-tools"){if(request.Method==HttpMethod.Put)Starred=true;return Task.FromResult(new HttpResponseMessage(Starred?HttpStatusCode.NoContent:HttpStatusCode.NotFound));}
            if(path=="/search/repositories"){SearchRequests++;return SearchOverride?.Invoke(Uri.UnescapeDataString(request.RequestUri.Query))??Task.FromResult(Page("Search result project introduction"));}
            throw new IOException("Unexpected ranking fixture request: "+path);
        }
    }
}
