using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using Gitstar.Desktop;

static class ReleaseStateChecks
{
    private static object Release(long id,bool draft,bool beta=false)=>new{id,tag_name="1.0.0.0",name="Gitstar",body=draft?"old notes":"published notes",draft,prerelease=beta,published_at=draft?(string?)null:"2026-10-13T02:00:00Z",created_at="2026-10-13T01:00:00Z",html_url="https://github.com/owner/repo/releases/tag/1.0.0.0",assets=Enumerable.Range(0,draft?1:4).Select(index=>new{name="asset"+index}).ToArray()};
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    public static async Task Run(Action<bool,string> check)
    {
        var bypassed=true;var writes=0;var details=0;
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            bypassed&=request.Headers.CacheControl?.NoCache==true&&request.Headers.CacheControl.NoStore&&request.Headers.IfNoneMatch.Count==0;
            if(request.Method!=HttpMethod.Get)writes++;
            var detail=request.RequestUri!.AbsolutePath.EndsWith("/9");if(detail)details++;
            return Task.FromResult(Json(detail?Release(9,false):new[]{Release(9,true)}));
        }))){var history=await api.ProjectVersions("owner/repo",CancellationToken.None);var actual=history.Single();
            check(!actual.Draft&&actual.Channel=="正式版"&&actual.AssetCount==4&&actual.Body=="published notes","stale draft list is reconciled with published release details including current notes and assets");
            check(details==1&&writes==0&&bypassed,"release status verification is read-only and bypasses conditional or proxy caches");
        }
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/9")?Release(9,false,true):new[]{Release(9,true)}))))){var history=await api.ProjectVersions("owner/repo",CancellationToken.None);check(history.Single().Channel=="测试版","draft verification preserves published prerelease channel rather than assuming stable");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/9")?new HttpResponseMessage(HttpStatusCode.NotFound){Content=new StringContent("{}")}:Json(new[]{Release(9,true)}))))){check((await api.ProjectVersions("owner/repo",CancellationToken.None)).Count==0,"deleted draft is removed from synchronized release list");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/9")?new HttpResponseMessage(HttpStatusCode.ServiceUnavailable):Json(new[]{Release(9,true)}))))){var failed=false;try{await api.ProjectVersions("owner/repo",CancellationToken.None);}catch(IOException){failed=true;}check(failed,"temporary release-detail failure does not accept unverified draft list as a successful synchronization");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(Json(Release(10,false)))))){var rejected=false;try{await api.ProjectVersionDetails("owner/repo",9,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"mismatched release ID is rejected before overwriting cached version state");}
        var inFlight=0;var maximum=0;
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            var path=request.RequestUri!.AbsolutePath;if(path.EndsWith("/releases"))return Json(Enumerable.Range(1,8).Select(id=>Release(id,true)).ToArray());
            var count=Interlocked.Increment(ref inFlight);lock(typeof(ReleaseStateChecks))maximum=Math.Max(maximum,count);
            try{await Task.Delay(30,ct);return Json(Release(long.Parse(path.Split('/').Last()),false));}finally{Interlocked.Decrement(ref inFlight);}
        }))){var history=await api.ProjectVersions("owner/repo",CancellationToken.None);check(history.Count==8&&history.All(item=>!item.Draft)&&maximum is >1 and <=4,"draft reconciliation runs concurrently within four-request limit");}
    }
}
