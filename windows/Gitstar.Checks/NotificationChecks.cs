using Gitstar.Desktop;
using Gitstar.Core;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

internal static class NotificationChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;
        check(NotificationSchedule.FollowsUpdates(new Settings().NotificationIntervalMinutes)&&NotificationSchedule.FollowsUpdates(999),"notification schedule defaults and invalid legacy values follow software updates");
        check(!NotificationSchedule.IsDue(0,null,now)&&!NotificationSchedule.IsDue(-1,null,now),"follow-update and manual-only notification modes never start standalone polling");
        check(NotificationSchedule.IsDue(15,null,now)&&!NotificationSchedule.IsDue(15,now.AddMinutes(-14),now)&&NotificationSchedule.IsDue(15,now.AddMinutes(-15),now),"independent notification interval starts once and respects exact due boundary");
        check(!NotificationSchedule.IsDue(5,now.AddMinutes(1),now),"future notification timestamps cannot cause rapid polling");
        var persisted=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{NotificationIntervalMinutes=30,RefreshIntervalHours=24}))!;
        check(persisted.NotificationIntervalMinutes==30&&persisted.RefreshIntervalHours==24,"notification interval persists independently from software update frequency");
        var calls=new List<string>();var requests=new List<HttpMethod>();var conditionals=new List<bool>();var cached=false;var tokenChanged=false;
        object Item(int id)=>new{id=id.ToString(),unread=true,reason="mention",updated_at="2026-10-24T08:00:00Z",repository=new{full_name="example/editor"},subject=new{title="Update discussion",type="Issue",url="https://api.github.com/repos/example/editor/issues/42",latest_comment_url="https://api.github.com/repos/example/editor/issues/comments/9"}};
        HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data))};
        using var handler=new FakeHttp((request,ct)=>{
            calls.Add(request.RequestUri!.PathAndQuery);requests.Add(request.Method);var conditional=request.Headers.Contains("If-Modified-Since");conditionals.Add(conditional);
            if(request.RequestUri.AbsolutePath=="/notifications"){
                if(cached&&conditional)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
                var response=Json(request.RequestUri.Query.Contains("page=2")?Array.Empty<object>():Enumerable.Range(1,50).Select(Item).ToArray());response.Content.Headers.LastModified=new DateTimeOffset(2026,10,24,8,0,0,TimeSpan.Zero);response.Headers.TryAddWithoutValidation("X-Poll-Interval","600");cached=true;if(request.Headers.Authorization?.Parameter=="second-account")tokenChanged=true;return Task.FromResult(response);
            }
            if(request.RequestUri.AbsolutePath.EndsWith("/issues/42"))return Task.FromResult(Json(new{body="Issue body",html_url="https://github.com/example/editor/issues/42"}));
            if(request.RequestUri.AbsolutePath.EndsWith("/comments/9"))return Task.FromResult(Json(new{body="Latest reply",html_url="https://github.com/example/editor/issues/42#issuecomment-9"}));
            throw new IOException("Unexpected request");
        });
        using var api=new GitHubClient(new(),handler){Token="first-account"};
        var first=await api.Notifications(false,1,CancellationToken.None);
        check(first.Items.Count==50&&first.More&&first.Items[0].Unread&&first.Items[0].ReasonText=="提及了你","notifications parse title repository reason unread state and pagination");
        check(api.NotificationPollAfter>DateTimeOffset.UtcNow.AddMinutes(9),"notifications respect server polling minimum");
        var second=await api.Notifications(false,1,CancellationToken.None);check(second.Items.Count==50&&conditionals.Last(),"notification 304 reuses unread list with If-Modified-Since");
        await api.Notifications(true,1,CancellationToken.None);check(!conditionals.Last()&&calls.Last().Contains("all=true"),"all-notification filter never reuses unread conditional cache");
        var next=await api.Notifications(false,2,CancellationToken.None);check(next.Items.Count==0&&!next.More&&!conditionals.Last(),"notification next page is independent from first page cache");
        api.Token="second-account";await api.Notifications(false,1,CancellationToken.None);check(tokenChanged&&!conditionals.Last(),"switching account token discards prior notification cache");
        var preview=await api.NotificationPreview(first.Items[0],CancellationToken.None);check(preview.Body.Contains("Latest reply")&&preview.Url.EndsWith("#issuecomment-9"),"notification preview reads latest reply and opens exact GitHub discussion");
        check(first.Items[0].BrowserUrl=="https://github.com/example/editor/issues/42"&&GitHubNoticeUrls.Browser("example/editor","https://api.github.com/repos/example/editor/pulls/7","PullRequest")=="https://github.com/example/editor/pull/7","notification issue and PR API subjects map to browser URLs");
        var count=calls.Count;var hostile=first.Items[0] with{SubjectUrl="https://evil.invalid/steal"};var fallback=await api.NotificationPreview(hostile,CancellationToken.None);check(calls.Count==count&&fallback.Url=="https://github.com/notifications","hostile subject URL cannot send credentials or open arbitrary domains");
        check(!GitHubNoticeUrls.Subject("example/editor","https://api.github.com/repos/other/private/issues/42")&&!GitHubNoticeUrls.Subject("example/editor","https://api.github.com/user")&&!GitHubNoticeUrls.Web("example/editor","https://github.com.evil.invalid/example/editor/issues/1"),"preview enforces repository and GitHub host boundaries");
        check(requests.All(method=>method==HttpMethod.Get),"notification polling and preview never mark messages read or mutate GitHub");
        using var deniedHandler=new FakeHttp((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("unsupported token")}));using var denied=new GitHubClient(new(),deniedHandler){Token="fine-grained-fixture"};var rejected=false;try{await denied.Notifications(false,1,CancellationToken.None);}catch(IOException error){rejected=error.Message.Contains("细粒度")&&error.Message.Contains("浏览器");}check(rejected,"unsupported notification credentials explain permission and browser fallback");
        api.Token="";count=calls.Count;rejected=false;try{await api.Notifications(false,1,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&calls.Count==count,"logged-out notifications require login before network request");
    }
}
