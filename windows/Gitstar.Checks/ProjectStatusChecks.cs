using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class ProjectStatusChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var published=DateTimeOffset.Parse("2026-10-01T01:00:00Z");var changed=published.AddDays(10);var forced=changed.AddHours(3);
        var payload=JsonSerializer.SerializeToElement(new{id=1,tag_name="1.0.0.0",name="Gitstar",body="",draft=false,prerelease=false,published_at=published,created_at=published.AddDays(-1),assets=new[]{new{id=2,name="Gitstar.exe",size=123,browser_download_url="",digest="sha256:abc",updated_at=changed}}});
        var version=GitHubClient.ParseProjectVersion(payload);
        check(version.Published==published&&version.Updated==changed&&version.CheckedAt!=null,"release publication asset modification and status check keep distinct timestamps");
        check(version.Assets?.Single().Digest=="sha256:abc","release status keeps remote package evidence for local pending-upload checks");
        var forcedVersion=version with{LastUploadedAt=forced,LastAction="强制覆盖",LastUploadedCount=1,LastReusedCount=3};
        check(forcedVersion.Published==published&&forcedVersion.LatestActivity==forced&&forcedVersion.LastUploadedCount==1,"same-version forced overwrite advances activity time without changing original publication date");
        var refreshed=ProjectTimeRules.PreserveLocalActivity(version,forcedVersion);
        check(ProjectTimeRules.SameRemoteVersion(version,version with{CheckedAt=changed,LastUploadedAt=forced})&&!ProjectTimeRules.SameRemoteVersion(version,version with{Body="changed"}),"remote version identity ignores local inspection clock while still detecting actual release edits");
        check(refreshed.LastUploadedAt==forced&&refreshed.LastAction=="强制覆盖"&&refreshed.LastReusedCount==3,"refreshing server status preserves confirmed local force-update history");
        var saved=JsonSerializer.Deserialize<ProjectVersion>(JsonSerializer.Serialize(refreshed))!;
        check(saved.LastUploadedAt==forced&&saved.Published==published&&saved.Assets?.Count==1,"release times upload counts and asset evidence survive cache serialization");
        var unknown=JsonSerializer.Deserialize<ProjectVersion>("{\"Id\":1,\"Tag\":\"1.0\",\"Updated\":\"2026-01-01T00:00:00Z\"}")!;
        check(unknown.Published==null&&unknown.LastUploadedAt==null&&ProjectTimeRules.Time(null)=="尚未获取","legacy release cache never invents publication or local update times");
        var projects=new[]{new ManagedProject("owner/a","",false,"main",true,true,false,changed),new ManagedProject("owner/b","",false,"main",true,true,false,changed.AddDays(-1)),new ManagedProject("owner/empty","",false,"main",true,true,false,changed.AddDays(-3))};
        var versions=new Dictionary<string,List<ProjectVersion>>(StringComparer.OrdinalIgnoreCase){["owner/a"]=[forcedVersion],["owner/b"]=[version with{Id=3,Published=published.AddDays(1),Updated=changed.AddDays(1)}]};
        check(ProjectTimeRules.Order(projects,versions,UpdateSort.Published).Select(p=>p.FullName).SequenceEqual(new[]{"owner/b","owner/a","owner/empty"}),"project publication sorting uses release publication rather than same-version upload time and puts unknowns last");
        check(ProjectTimeRules.Order(projects,versions,UpdateSort.RepositoryUpdated).First().FullName=="owner/a","project update sorting uses repository Updated independently from release activity");
        check(JsonSerializer.Deserialize<Settings>("{}")!.ProjectsSort==UpdateSort.RepositoryUpdated,"project sorting preserves former repository-update default for existing settings");
        check(CacheRules.Matches("cache/user/my-projects/release-status",CacheRules.Keys("user",CacheModule.MyProjects)),"project cache cleanup includes release-directory verification snapshots");
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            check(request.Headers.CacheControl?.NoCache==true&&request.Headers.CacheControl.NoStore==true,"post-push source verification bypasses stale GitHub branch responses");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"commit\":{\"sha\":\""+new string('a',40)+"\"}}")});
        }))){check(await api.BranchCommit("owner/app","main",CancellationToken.None)==new string('a',40),"fresh source verification reads the confirmed remote commit");}
    }
}
