using System.Net;
using System.Text;
namespace Gitstar.Desktop;
public sealed partial class GitHubClient
{
    private readonly Dictionary<string,long> rankingFloors=[];
    public async Task<RankingPage> Rankings(RankingKind kind,int period,int page,string query,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(!Enum.IsDefined(kind)||page<1||page>40||kind==RankingKind.Trending&&page!=1)throw new IOException("榜单页码无效。");RepositoryRankings.Period(period);
        if(kind==RankingKind.Trending){
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var message=new HttpRequestMessage(HttpMethod.Get,"https://github.com/trending?since="+RepositoryRankings.Period(period));
            // This public HTML request deliberately carries no account credential.
            using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)throw new IOException("GitHub 趋势页暂时限制访问，请稍后刷新或在浏览器查看。");
            if(!response.IsSuccessStatusCode)throw new IOException($"GitHub 趋势页读取失败（HTTP {(int)response.StatusCode}）。");
            await using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var content=new MemoryStream();var buffer=new byte[16384];int count;while((count=await input.ReadAsync(buffer,deadline.Token))>0){if(content.Length+count>2*1024*1024)throw new IOException("趋势页超过读取限制。");content.Write(buffer,0,count);}
            var entries=RepositoryRankings.ParseTrending(Encoding.UTF8.GetString(content.ToArray()));return new(kind,period,1,DateTimeOffset.UtcNow,entries,entries.Count,false);
        }
        var searchedAt=DateTimeOffset.UtcNow;var q=RepositoryRankings.SearchQuery(kind,period,query,searchedAt);var sort=kind==RankingKind.Search?"":"&sort=stars&order=desc";
        var floorKey=kind+"/"+period;var floor=kind==RankingKind.Search?0:rankingFloors.GetValueOrDefault(floorKey,kind==RankingKind.Popular?100000L:1000L);JsonElement data;
        // Narrow high-star candidates first to avoid broad-query search timeouts. Expand
        // the same page when the range is too small; never skip a rank at a boundary.
        while(true){var filtered=q+(floor>0?" stars:>="+floor:"");data=await Request("search/repositories?q="+Uri.EscapeDataString(filtered)+sort+$"&per_page=25&page={page}",ct:ct);if(floor==0||data.GetProperty("total_count").GetInt32()>=page*25)break;floor/=2;}
        if(kind!=RankingKind.Search)rankingFloors[floorKey]=floor;
        var entriesApi=new List<RankingEntry>();foreach(var item in data.GetProperty("items").EnumerateArray()){
            if(!Rules.ValidRepository(S(item,"full_name"))||item.GetProperty("private").GetBoolean())continue;var repo=ParseRepository(item,false);if(kind!=RankingKind.Search&&(repo.Metadata.Stars is not {} stars||stars<floor))throw new IOException("GitHub 返回的项目不满足 Stars 筛选，已停止替换榜单结果。");if(entriesApi.Any(entry=>entry.Repository.FullName.Equals(repo.FullName,StringComparison.OrdinalIgnoreCase)))continue;
            DateTimeOffset? created=DateTimeOffset.TryParse(S(item,"created_at"),out var date)?date:null;if(kind==RankingKind.New&&(created==null||created.Value.UtcDateTime<searchedAt.UtcDateTime.Date.AddDays(-RepositoryRankings.NewDays(period))))throw new IOException("GitHub 返回的项目不满足新建时间筛选，已保留原有结果。");
            entriesApi.Add(new(repo,(page-1)*25+entriesApi.Count+1,S(item,"language"),Created:created));
        }
        var total=data.GetProperty("total_count").GetInt32();return new(kind,period,page,DateTimeOffset.UtcNow,entriesApi,total,page<40&&entriesApi.Count>0&&(page*25<total||floor>0&&entriesApi.Count==25),data.TryGetProperty("incomplete_results",out var partial)&&partial.ValueKind==JsonValueKind.True,floor);
    }
}
