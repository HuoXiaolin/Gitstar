using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class SelfUpdateChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        check(new Settings().SelfRepository==AppInfo.Repository&&AppInfo.ProjectUrl=="https://github.com/HuoXiaolin/Gitstar","default app update source matches about-page project address");
        check(RepositoryChecks.Targets(false,[],new Settings()).Length==0,"optional app update repository does not become a followed-project scan target");
        check(RepositoryChecks.Targets(false,[new Repository(AppInfo.Repository,"",false)],new Settings()).SequenceEqual(new[]{AppInfo.Repository}),"explicitly starred app repository still participates in followed-project checks");
        object Version(bool draft=false,bool beta=false)=>new{id=1,tag_name="v1.0.0.1",name="Gitstar",body="New features",draft,prerelease=beta,published_at="2026-10-13T02:00:00Z",assets=Array.Empty<object>()};
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            check(request.Method==HttpMethod.Get&&request.RequestUri!.AbsolutePath=="/repos/HuoXiaolin/Gitstar/releases/latest"&&request.Headers.Authorization==null,"manual self-update check reads official latest release without requiring login");
            check(request.Headers.CacheControl?.NoCache==true&&request.Headers.IfNoneMatch.Count==0,"manual app update check bypasses stale release cache");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(Version()))});
        }))){var release=await api.LatestGitstarRelease(CancellationToken.None);check(release.Repository==AppInfo.Repository&&release.Body=="New features"&&Rules.CompareVersions(release.Tag,"1.0.0.0")>0,"new app version and update notes resolve from the same configured project");}
        foreach(var fixture in new[]{Version(draft:true),Version(beta:true)})using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(fixture))})))){var rejected=false;try{await api.LatestGitstarRelease(CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"self-update check excludes draft and testing releases from stable app update");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound){Content=new StringContent("{}")})))){var missing=false;try{await api.LatestGitstarRelease(CancellationToken.None);}catch(GitHubNotFoundException){missing=true;}check(missing,"missing app release is reported as unavailable rather than already up to date");}
    }
}
