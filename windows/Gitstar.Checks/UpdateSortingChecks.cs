using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class UpdateSortingChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.Parse("2026-10-14T03:00:00Z");
        check(JsonSerializer.Deserialize<Repository>("{\"FullName\":\"owner/app\",\"Description\":\"\",\"Private\":false}")!.Updated==null,"old repository cache does not invent an Updated timestamp");
        check(JsonSerializer.Deserialize<Settings>("{}")!.RecentSort==UpdateSort.Published,"existing users retain publication-time sorting by default");
        object Repo(string? updated)=>new{full_name="owner/app",description="fixture",@private=false,node_id="repository-id",updated_at=updated,pushed_at=now.AddMonths(-1).ToString("O")};
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            var payload=request.RequestUri!.AbsolutePath=="/user/starred"?JsonSerializer.Serialize(new[]{Repo(now.ToString("O"))}):JsonSerializer.Serialize(Repo(now.ToString("O")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload)});
        }))){var starred=(await api.Stars(CancellationToken.None)).Single();var direct=await api.Repo("owner/app");check(starred.Updated==now&&starred.Starred,"starred repositories parse repository updated_at independently from pushed_at");check(direct.Updated==now&&!direct.Starred,"directly followed repository uses the same Updated source");check(JsonSerializer.Deserialize<Repository>(JsonSerializer.Serialize(direct))==direct,"Updated timestamp survives repository cache serialization");}
        foreach(var value in new string?[]{null,"invalid"})using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(Repo(value)))}))))check((await api.Repo("owner/app")).Updated==null,"missing or invalid Updated remains unknown");
        Release Release(string repo,DateTimeOffset published)=>new(1,repo,"1.0","","",false,published,"",[]);
        var recent=new Card{Release=Release("owner/new-release",now),RepositoryUpdated=now.AddDays(-2)};
        var active=new Card{Release=Release("owner/recent-activity",now.AddDays(-1)),RepositoryUpdated=now};
        check(Card.Order([active,recent]).First()==recent,"publication sorting uses release published time");
        var updatedRecent=new Card{Release=recent.Release,RepositoryUpdated=recent.RepositoryUpdated,ShowRepositoryUpdated=true};
        var updatedActive=new Card{Release=active.Release,RepositoryUpdated=active.RepositoryUpdated,ShowRepositoryUpdated=true};
        check(Card.Order([updatedRecent,updatedActive]).First()==updatedActive,"Updated sorting differs when repository activity and release publication differ");
        var unknown=new Card{Release=Release("owner/unknown",now.AddDays(1)),ShowRepositoryUpdated=true};
        check(Card.Order([unknown,updatedRecent,updatedActive]).Last()==unknown&&unknown.Date=="更新时间待同步","unknown Updated sorts last without substituting release publication");
        check(updatedActive.Date.StartsWith("仓库更新 ")&&updatedActive.DateHint.Contains("发布时间：")&&updatedActive.DateHint.Contains("仓库更新时间："),"cards identify Updated and show both timestamp sources in tooltip");
        var tied=new Card{Release=Release("owner/tied",now.AddDays(-3)),RepositoryUpdated=now,ShowRepositoryUpdated=true};
        check(Card.Order([tied,updatedActive]).First()==updatedActive,"equal repository Updated timestamps use publication time as stable tie break");
    }
}
