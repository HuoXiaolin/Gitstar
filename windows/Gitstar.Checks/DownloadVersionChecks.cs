using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class DownloadVersionChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;
        Release R(long id,string tag,int hours,bool beta=false,bool latest=false)=>new(id,"owner/app",tag,tag,"large notes",beta,now.AddHours(hours),"",[new(id,tag+".exe",12,"https://api.github.com/assets/"+id,"sha256:fixture")]){IsLatest=latest};
        var latest=R(1,"v3.0.0",-24,latest:true);var backport=R(2,"v2.9.9",0);var old=R(3,"v1.0.0",-100);var beta=R(4,"v4.0.0-beta.1",-5,true);
        check(DownloadVersionRules.Default([backport,old,latest],0)==latest,"save-package defaults to GitHub Latest rather than later lower-version backport");
        check(DownloadVersionRules.Default([latest,backport,beta],1)==beta&&DownloadVersionRules.Default([latest,beta],2)==beta,"save-package default follows stable and testing channel selection");
        var order=DownloadVersionRules.Order([old,backport,latest,latest with{IsLatest=false}],0);
        check(order.Count==3&&order[0]==latest&&order.Any(release=>release.Id==old.Id),"version choices deduplicate releases and retain historical versions");
        check(DownloadVersionRules.Default([],1)==null,"empty release history does not invent a default version");
        object Payload(Release release,bool draft=false)=>new{id=release.Id,tag_name=release.Tag,name=release.Name,body=release.Body,draft,prerelease=release.Prerelease,published_at=release.Published.ToString("O"),html_url="",assets=release.Assets.Select(asset=>new{id=asset.Id,name=asset.Name,size=asset.Size,browser_download_url=asset.Url,digest=asset.Digest})};
        var requests=new List<string>();var fresh=true;
        using var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            fresh&=request.Headers.CacheControl is {NoCache:true,NoStore:true};var url=request.RequestUri!;requests.Add(url.PathAndQuery);
            var payload=url.AbsolutePath.EndsWith("/latest")?JsonSerializer.Serialize(Payload(latest)):url.AbsolutePath.EndsWith("/3")?JsonSerializer.Serialize(Payload(old)):url.Query.Contains("page=2")?JsonSerializer.Serialize(new[]{Payload(old)}):JsonSerializer.Serialize(Enumerable.Range(0,30).Select(index=>Payload(backport with{Id=index+10},index==0)));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload)});
        }));
        var first=await api.DownloadVersions("owner/app",1,CancellationToken.None);var second=await api.DownloadVersions("owner/app",2,CancellationToken.None);var head=await api.LatestDownloadVersion("owner/app",CancellationToken.None);var verified=await api.DownloadVersionById("owner/app",3,CancellationToken.None);
        check(first.Count==30&&first.Versions.Count==29&&second.Count==1&&second.Versions.Single().Id==old.Id,"download history paginates by raw page count while excluding drafts");
        check(first.Versions.All(release=>release.Body=="")&&first.Versions.All(release=>release.Assets.Single().Name==backport.Assets.Single().Name),"version picker retains package metadata without keeping release note bodies");
        check(head is {IsLatest:true,Body:""}&&head.Id==latest.Id&&verified?.Id==old.Id,"latest release and cached out-of-page candidate are verified independently");
        check(fresh&&requests.Count==4,"version picker reads only requested pages using fresh remote metadata");
        using var missing=new GitHubClient(new(),new FakeHttp((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        check(await missing.LatestDownloadVersion("owner/app",CancellationToken.None)==null&&await missing.DownloadVersionById("owner/app",3,CancellationToken.None)==null,"missing Latest or deleted cached version clears obsolete version choice");
        using var denied=new GitHubClient(new(),new FakeHttp((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("denied")})));
        var failed=false;try{await denied.LatestDownloadVersion("owner/app",CancellationToken.None);}catch(IOException){failed=true;}check(failed,"version read permission failure cannot masquerade as up-to-date metadata");
    }
}
