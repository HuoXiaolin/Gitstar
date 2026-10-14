using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class ReleaseSelectionChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.Parse("2026-10-14T03:00:00Z");
        Release R(long id,string tag,int hours=0,bool beta=false,bool latest=false)=>new(id,"grafana/grafana",tag,tag,"Full release notes",beta,now.AddHours(hours),"",[new(id,"app-x64.exe",1,"",null)]){IsLatest=latest};
        var main=R(1,"v13.2.4",-14);var backport=R(2,"v12.4.13",-6);
        check(Rules.SelectRelease([backport,main],new())==main,"later maintenance backport does not replace higher main version");
        check(Rules.SelectRelease([main,backport],new())==main,"version selection is independent of API enumeration order");
        check(Rules.SelectRelease([R(1,"v9.99.0"),R(2,"v10.0.0",-10)],new())?.Tag=="v10.0.0","versions compare numerically rather than lexically");
        check(Rules.SelectRelease([R(1,"1.0.0.2"),R(2,"1.0.0.10",-10)],new())?.Tag=="1.0.0.10","four-component application versions retain numeric comparison");
        var beta=R(3,"v13.3.0-beta.2",-24,true);var oldBeta=R(4,"v12.5.0-beta.10",0,true);
        check(Rules.SelectRelease([beta,backport,main,oldBeta],new())==main,"stable subscription excludes testing releases");
        check(Rules.SelectRelease([beta,backport,main,oldBeta],new(){IncludePrerelease=true})==beta,"combined channel selects newer testing version rather than newer publication");
        check(Rules.SelectRelease([R(1,"v13.2.4-beta.10",0,true),main],new(){IncludePrerelease=true})==main,"stable release wins over prerelease of the same version");
        var pinned=R(5,"v13.1.0",-100,latest:true);
        check(Rules.SelectRelease([main,pinned,backport],new())==pinned,"author-selected GitHub Latest is authoritative for stable channel");
        check(Rules.SelectRelease([main,pinned,beta],new(){IncludePrerelease=true})==beta,"Latest stable marker does not hide a higher testing version");
        var prefixed=new Binding{Repository="grafana/grafana",TagPrefix="desktop-"};
        var desktop=R(6,"desktop-v4.0.0",-2);var desktopBackport=R(7,"desktop-v3.9.9",0);
        check(Rules.SelectRelease([desktopBackport,pinned,desktop,R(8,"mobile-v99.0",latest:true)],prefixed)==desktop,"subscription strips only its configured prefix and isolates other product lines");
        check(Rules.SelectRelease([R(1,"nightly-a",-1),R(2,"nightly-b")],new())?.Tag=="nightly-b","non-version labels fall back to publication time");
        var withUnknown=new[]{main,backport,R(9,"nightly-new",1)};
        check(Rules.SelectRelease(withUnknown,new())==main&&Rules.SelectRelease(withUnknown.Reverse(),new())==main,"unversioned labels cannot displace recognized versions through enumeration order");
        foreach(var source in new[]{new[]{main,backport,beta,oldBeta,desktop,desktopBackport},new[]{main,pinned,beta,backport}}){
            var compact=ReleaseRetention.Compact(source,[prefixed]);
            foreach(var binding in new[]{new Binding(),new Binding{IncludePrerelease=true},prefixed})check(Rules.SelectRelease(source,binding)?.Id==Rules.SelectRelease(compact,binding)?.Id,"bounded cache preserves stable beta and prefixed version choices");
        }
        var candidates=new ReleaseCandidates("grafana/grafana",[]);candidates.Add(backport);
        check(candidates.Needs(main.Tag,false,main.Published),"streaming parser materializes higher version even when published earlier");candidates.Add(main);
        check(!candidates.Needs(backport.Tag,false,backport.Published)&&candidates.Values.Single()==main,"streaming retention discards newer lower-version backports");
        check(JsonSerializer.Deserialize<Release>(JsonSerializer.Serialize(pinned))!.IsLatest&& !JsonSerializer.Deserialize<Release>("{\"Id\":1,\"Repository\":\"owner/app\",\"Tag\":\"v1\",\"Assets\":[]}")!.IsLatest,"Latest marker persists while legacy cache remains compatible");
        DownloadedPackage Package(Release r)=>new(r.Repository,r.Id,r.Tag,r.Prerelease,r.Published,"setup-x64.exe","fixture","fixture",1,"fixture","windows/x64");
        var mainPackage=Package(main);var backportPackage=Package(backport);
        check(PackageRules.Outdated([mainPackage,backportPackage]).SequenceEqual(new[]{backportPackage}),"keep-latest cleanup retains higher version even when its package was published earlier");
        var sameVersionAsset=mainPackage with{ReleaseId=10,AssetName="portable-x64.zip",Published=now};
        check(PackageRules.Outdated([mainPackage,sameVersionAsset]).Length==0,"same-version package attachments survive differing publication timestamps");
        var mobilePackage=Package(R(12,"mobile-v99.0"));
        var prefixedMain=Package(desktop);var prefixedOld=Package(desktopBackport);
        check(PackageRules.Outdated([prefixedMain,prefixedOld,mobilePackage],[prefixed,new Binding{Repository="grafana/grafana",TagPrefix="mobile-"}]).SequenceEqual(new[]{prefixedOld}),"package cleanup compares configured product-line versions independently");
        check(PackageRules.Outdated([mainPackage,Package(R(13,"nightly",1))]).Length==0,"unrecognized labels cannot authorize deleting versioned installer packages");
        check(PackageRules.Outdated([mainPackage,backportPackage with{RetainForBatch=true}]).Length==0,"bulk-selected historical installer versions are protected from automatic latest-only cleanup");
        check(!PackageRules.NewerVersion(backport.Tag,backport.Published,main.Tag,main.Published)&&PackageRules.NewerVersion(main.Tag,main.Published,backport.Tag,backport.Published),"managed installer replacement cannot downgrade based on publication time");
        object Payload(Release r)=>new{id=r.Id,tag_name=r.Tag,name=r.Name,body=r.Body,prerelease=r.Prerelease,draft=false,published_at=r.Published,html_url="",assets=Array.Empty<object>()};
        var pages=new Dictionary<string,GitHubClient.ReleasePage>();var phase=0;var latestBodies=0;var notModified=0;
        HttpMessageHandler Transport()=>new FakeHttp((request,ct)=>{
            var latest=request.RequestUri!.AbsolutePath.EndsWith("/latest");
            if(phase==2&&latest)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var etag=latest?"\"latest-v1\"":"\"page-v1\"";
            if(request.Headers.IfNoneMatch.Any(x=>x.Tag==etag)){notModified++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));}
            if(latest)latestBodies++;
            var json=latest?JsonSerializer.Serialize(Payload(pinned)):JsonSerializer.Serialize(new[]{Payload(backport),Payload(main),Payload(beta)});
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)};response.Headers.ETag=new(etag);return Task.FromResult(response);
        });
        using(var api=new GitHubClient(new(),Transport())){api.ReadReleasePage=key=>pages.GetValueOrDefault(key);api.SaveReleasePage=(key,page)=>pages[key]=page;
            var found=await api.Releases("grafana/grafana",CancellationToken.None);
            check(Rules.SelectRelease(found,new())?.Id==pinned.Id&&found.Single(r=>r.Id==pinned.Id).IsLatest,"GitHub Latest outside the release page is fetched and preserved through the real scan pipeline");
        }
        phase=1;
        using(var api=new GitHubClient(new(),Transport())){api.ReadReleasePage=key=>pages.GetValueOrDefault(key);api.SaveReleasePage=(key,page)=>pages[key]=page;
            var found=await api.Releases("grafana/grafana",CancellationToken.None);
            check(latestBodies==1&&notModified==2&&Rules.SelectRelease(found,new())?.Id==pinned.Id,"Latest and paged candidates survive restart and conditional 304 refresh");
            phase=2;found=await api.Releases("grafana/grafana",CancellationToken.None);
            check(Rules.SelectRelease(found,new())?.Id==main.Id&&!found.Any(r=>r.IsLatest)&&pages["grafana/grafana/latest"].Candidates.Count==0,"removed Latest clears its cached marker and falls back to highest stable version");
        }
        var legacyConditional=false;
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            legacyConditional|=request.Headers.IfNoneMatch.Count>0;
            if(request.RequestUri!.AbsolutePath.EndsWith("/latest"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new[]{Payload(backport),Payload(main)}))});
        }))){api.ReadReleasePage=_=>new("\"old-cache\"","",2,[backport]);var found=await api.Releases("grafana/grafana",CancellationToken.None);check(!legacyConditional&&Rules.SelectRelease(found,new())?.Id==main.Id,"old publication-only ETag cache is invalidated to recover discarded higher versions");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/latest")?new HttpResponseMessage(HttpStatusCode.NotFound):new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new[]{Payload(beta)}))})))){var found=await api.Releases("grafana/grafana",CancellationToken.None);check(Rules.SelectRelease(found,new())==null&&Rules.SelectRelease(found,new(){IncludePrerelease=true})?.Id==beta.Id,"prerelease-only repository works when GitHub Latest returns 404");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("unavailable")})))){var failed=false;try{await api.Releases("grafana/grafana",CancellationToken.None);}catch(IOException){failed=true;}check(failed,"Latest service failure is reported rather than silently treated as no update");}
    }
}
