using System.Text.RegularExpressions;
namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    public async Task<GitHubNoticePage> RepositoryIssues(string owner,bool includeClosed,int page,CancellationToken ct)
    {
        if(Token.Length==0)throw new IOException("请先登录 GitHub 后查看仓库 Issues。");
        if(!Regex.IsMatch(owner,@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}$"))throw new IOException("GitHub 账号名称无效，请重新登录。");
        if(page<1||page>20)throw new ArgumentOutOfRangeException(nameof(page));
        var query="is:issue user:"+owner+(includeClosed?"":" state:open");
        var data=await Request($"search/issues?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page=50&page={page}",ct:ct,fresh:true);
        var items=new List<GitHubNotice>();
        foreach(var issue in data.GetProperty("items").EnumerateArray()){
            if(issue.TryGetProperty("pull_request",out _))continue;
            var repositoryUrl=S(issue,"repository_url");if(!Uri.TryCreate(repositoryUrl,UriKind.Absolute,out var repository)||repository.Scheme!="https"||repository.Host!="api.github.com"||!repository.IsDefaultPort||repository.UserInfo.Length>0||repository.Query.Length>0||repository.Fragment.Length>0||!repository.AbsolutePath.StartsWith("/repos/",StringComparison.OrdinalIgnoreCase))continue;
            var repo=repository.AbsolutePath[7..];if(!Rules.ValidRepository(repo)||!repo.Split('/')[0].Equals(owner,StringComparison.OrdinalIgnoreCase))continue;
            var subject=S(issue,"url");if(!GitHubNoticeUrls.Subject(repo,subject))continue;
            items.Add(new("issue:"+issue.GetProperty("id").GetInt64(),repo,S(issue,"title"),"Issue",S(issue,"state")=="closed"?"issue_closed":"issue_open",false,DateTimeOffset.TryParse(S(issue,"updated_at"),out var updated)?updated:null,subject,""));
        }
        var total=data.GetProperty("total_count").GetInt32();var incomplete=data.TryGetProperty("incomplete_results",out var partial)&&partial.ValueKind==JsonValueKind.True;
        return new(items.DistinctBy(item=>item.Id).ToList(),page<20&&page*50<total){Total=total,Incomplete=incomplete};
    }
}
