using System.Text.RegularExpressions;
namespace Gitstar.Desktop;

public record GitHubNotice(string Id,string Repository,string Title,string Type,string Reason,bool Unread,DateTimeOffset? Updated,string SubjectUrl,string CommentUrl)
{
    public string Kind=>Type switch{"Issue"=>"Issue","PullRequest"=>"Pull Request","Commit"=>"提交","Release"=>"版本发布","Discussion"=>"讨论","CheckSuite"=>"工作流",_=>Type.Length>0?Type:"消息"};
    public string ReasonText=>Reason switch{"issue_open"=>"待处理问题","issue_closed"=>"已关闭问题","mention"=>"提及了你","team_mention"=>"提及了你的团队","review_requested"=>"请求你评审","assign"=>"分配给你","ci_activity"=>"工作流动态","security_alert"=>"安全提醒","subscribed"=>"订阅更新","comment"=>"参与的讨论","author"=>"你创建的内容","state_change"=>"状态变化",_=>Reason.Length>0?Reason:"订阅更新"};
    public string BrowserUrl=>GitHubNoticeUrls.Browser(Repository,SubjectUrl,Type);
}
public record GitHubNoticePage(List<GitHubNotice> Items,bool More)
{
    public int? Total {get;init;}
    public bool Incomplete {get;init;}
}
public record GitHubNoticePreview(string Body,string Url);

public static class GitHubNoticeUrls
{
    public static bool Subject(string repo,string url)=>Rules.ValidRepository(repo)&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="api.github.com"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0&&Regex.IsMatch(uri.AbsolutePath,"^/repos/"+Regex.Escape(repo)+@"/(issues/(?:comments/)?\d+|pulls/\d+|commits/[A-Za-z0-9]+|releases/\d+|discussions/\d+|check-suites/\d+)$",RegexOptions.IgnoreCase);
    public static string Browser(string repo,string url,string type)
    {
        if(!Subject(repo,url))return "https://github.com/notifications";
        var tail=new Uri(url).AbsolutePath[("/repos/"+repo+"/").Length..];
        if(type=="Issue"&&Regex.IsMatch(tail,@"^issues/\d+$"))return "https://github.com/"+repo+"/"+tail;
        if(type=="PullRequest"&&Regex.IsMatch(tail,@"^pulls/\d+$"))return "https://github.com/"+repo+"/"+tail.Replace("pulls/","pull/");
        if(type=="Commit"&&tail.StartsWith("commits/"))return "https://github.com/"+repo+"/commit/"+tail[8..];
        return "https://github.com/notifications";
    }
    public static bool Web(string repo,string url)=>Rules.ValidRepository(repo)&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="github.com"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.AbsolutePath.StartsWith("/"+repo+"/",StringComparison.OrdinalIgnoreCase);
}

public sealed partial class GitHubClient
{
    private string? noticeModified;
    private string noticeToken="";
    private GitHubNoticePage? noticeUnread;
    public DateTimeOffset NotificationPollAfter {get;private set;}
    public async Task<GitHubNoticePage> Notifications(bool all,int page,CancellationToken ct)
    {
        if(Token.Length==0)throw new IOException("请先登录 GitHub 后查看消息。");
        if(page<1||page>1000)throw new ArgumentOutOfRangeException(nameof(page));
        if(noticeToken!=Token){noticeToken=Token;noticeModified=null;noticeUnread=null;NotificationPollAfter=default;}
        var cache=!all&&page==1;string? modified=null;
        var data=await Request($"notifications?all={all.ToString().ToLowerInvariant()}&per_page=50&page={page}",ct:ct,modifiedSince:cache&&noticeUnread!=null?noticeModified:null,headers:response=>{
            var seconds=60;if(response.Headers.TryGetValues("X-Poll-Interval",out var values)&&int.TryParse(values.FirstOrDefault(),out var interval))seconds=Math.Max(seconds,interval);
            NotificationPollAfter=DateTimeOffset.UtcNow.AddSeconds(seconds);
            if(response.Headers.TryGetValues("Last-Modified",out var dates))modified=dates.FirstOrDefault();
            else if(response.Content.Headers.TryGetValues("Last-Modified",out var contentDates))modified=contentDates.FirstOrDefault();
        });
        if(data.ValueKind==JsonValueKind.Undefined&&cache&&noticeUnread!=null)return noticeUnread;
        if(data.ValueKind!=JsonValueKind.Array)throw new IOException("GitHub 消息列表格式异常，请稍后重试。");
        var items=new List<GitHubNotice>();
        foreach(var item in data.EnumerateArray()){
            if(!item.TryGetProperty("subject",out var subject)||!item.TryGetProperty("repository",out var repository))continue;
            var id=S(item,"id");if(id.Length==0)continue;
            items.Add(new(id,S(repository,"full_name"),S(subject,"title"),S(subject,"type"),S(item,"reason"),item.TryGetProperty("unread",out var unread)&&unread.ValueKind==JsonValueKind.True,DateTimeOffset.TryParse(S(item,"updated_at"),out var updated)?updated:null,S(subject,"url"),S(subject,"latest_comment_url")));
        }
        var result=new GitHubNoticePage(items.DistinctBy(item=>item.Id).ToList(),data.GetArrayLength()==50);
        if(cache){noticeUnread=result;noticeModified=modified;}return result;
    }
    public async Task<GitHubNoticePreview> NotificationPreview(GitHubNotice item,CancellationToken ct)
    {
        var url=item.BrowserUrl;if(!GitHubNoticeUrls.Subject(item.Repository,item.SubjectUrl))return new("此类消息暂不支持正文预览，请在浏览器查看。",url);
        var subject=await Request(item.SubjectUrl,ct:ct);var html=S(subject,"html_url");if(GitHubNoticeUrls.Web(item.Repository,html))url=html;
        var body=S(subject,"body");if(body.Length==0&&subject.TryGetProperty("commit",out var commit))body=S(commit,"message");
        if(item.CommentUrl!=item.SubjectUrl&&GitHubNoticeUrls.Subject(item.Repository,item.CommentUrl)){
            var comment=await Request(item.CommentUrl,ct:ct);var latest=S(comment,"body");if(latest.Length>0)body="最新回复\n\n"+latest;html=S(comment,"html_url");if(GitHubNoticeUrls.Web(item.Repository,html))url=html;
        }
        if(body.Length==0)body="此消息未提供正文，请在浏览器查看完整内容。";
        return new(body.Length>8000?body[..8000]+"\n\n……更多内容请在浏览器查看。":body,url);
    }
}
