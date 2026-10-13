using System.Net;
using System.Net.Http;
using System.IO;
using Gitstar.Core;
using System.Text.Json;
using Gitstar.Desktop;

static class ReleaseStateChecks
{
    private static object Release(long id,bool draft,bool beta=false)=>new{id,tag_name="1.0.0.0",name="Gitstar",body=draft?"old notes":"published notes",draft,prerelease=beta,published_at=draft?(string?)null:"2026-10-13T02:00:00Z",created_at="2026-10-13T01:00:00Z",html_url="https://github.com/owner/repo/releases/tag/1.0.0.0",assets=Enumerable.Range(0,draft?1:4).Select(index=>new{name="asset"+index}).ToArray()};
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    public static async Task Run(Action<bool,string> check,string root)
    {
        check(ManualReleaseRules.Version("1.0.0.0",[],root,"repo")=="1.0.0.0","explicit four-part release version overrides automatic file detection");
        var app=typeof(GitHubClient).Assembly.Location;check(ManualReleaseRules.Version("",[app],root,"repo")==AutomationRules.ReadVersion(app),"blank version reads actual application binary metadata");
        var missingVersion=false;try{ManualReleaseRules.Version("",[],root,"repo");}catch(IOException){missingVersion=true;}check(missingVersion,"missing application version never invents a default tag");
        var writesForSame=0;
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{if(request.Method!=HttpMethod.Get)writesForSame++;return Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/9")?Release(9,false):new[]{Release(9,false)}));}))){
            var skipped=false;try{await api.PrepareManualRelease("owner/repo","1.0.0.0","","",false,null,CancellationToken.None);}catch(IOException){skipped=true;}
            check(skipped&&writesForSame==0,"same version defaults to skip without changing remote releases or assets");
            var same=await api.PrepareManualRelease("owner/repo","1.0.0.0","","",false,null,CancellationToken.None,true);
            check(same.GetProperty("id").GetInt64()==9&&!same.GetProperty("draft").GetBoolean()&&writesForSame==0,"confirmed overwrite reuses published release without recreating or demoting its tag");
        }
        var createCount=0;var sourceVerified=false;var newSha=new string('a',40);
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            if(request.Method==HttpMethod.Post){createCount++;using var payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));check(payload.RootElement.GetProperty("tag_name").GetString()=="1.0.0.1"&&payload.RootElement.GetProperty("draft").GetBoolean()&&sourceVerified,"new manually entered version creates draft only after remote commit verification");return Json(new{id=10,tag_name="1.0.0.1",draft=true});}
            if(request.RequestUri!.AbsolutePath.Contains("/commits/")){sourceVerified=true;return Json(new{sha=newSha});}return Json(Array.Empty<object>());
        }))){var created=await api.PrepareManualRelease("owner/repo","1.0.0.1",newSha,"notes",false,null,CancellationToken.None);check(createCount==1&&created.GetProperty("id").GetInt32()==10,"new version uses regular release creation rather than overwriting older version");}
        var locked=false;
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/9")?new{id=9,tag_name="1.0.0.0",draft=false,immutable=true}:new[]{Release(9,false)}))))){try{await api.PrepareManualRelease("owner/repo","1.0.0.0","","",false,null,CancellationToken.None,true);}catch(IOException){locked=true;}check(locked,"immutable status is freshly checked before confirmed overwrite");}
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
        var tag="untagged-abcd";var patchCount=0;
        object Draft()=>new{id=9L,tag_name=tag,name="1.0.0.0",body="notes",draft=true,prerelease=false,assets=new[]{new{name="Gitstar.exe"}},created_at="2026-10-13T01:00:00Z"};
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            if(request.Method==HttpMethod.Patch){patchCount++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));tag=body.RootElement.GetProperty("tag_name").GetString()!;check(!body.RootElement.TryGetProperty("draft",out _),"draft tag repair does not publish before user-selected action");return Json(Draft());}
            return Json(request.RequestUri!.AbsolutePath.EndsWith("/releases")?new[]{Draft()}:Draft());
        }))){await api.EditDraft("owner/repo",9,"1.0.0.0","notes",false,CancellationToken.None,"1.0.0.0");var actual=await api.ProjectVersionDetails("owner/repo",9,CancellationToken.None);check(patchCount==1&&actual.Id==9&&actual.Tag=="1.0.0.0"&&actual.Draft&&actual.AssetCount==1,"manual continuation repairs temporary tag while retaining draft ID and existing attachments");}
        foreach(var state in new[]{"结果待核实：安装包命名结果未确认。","失败：网络断开","已暂停 · 远程结果待核实","等待执行"}){
            var task=new Operation{Kind="自动发布",State=state,RemoteId=411245039,Results=["成功：源码已同步"]};check(OperationRules.CanCancel(task),"stopped or pending task can be cancelled: "+state);OperationRules.CancelStopped(task);check(!OperationRules.CanCancel(task)&&task.State.StartsWith("已取消")&&task.RemoteId==411245039&&task.Results.Single()=="成功：源码已同步","cancellation stops retries while retaining remote ID and completed source evidence");
        }
        var canceled=new Operation{Kind="自动发布",State="结果待核实",RemoteId=411245039};OperationRules.CancelStopped(canceled);var dbRoot=Path.Combine(root,"cancelled-operation");using(var db=new LocalStore(dbRoot))db.Set("operations",new[]{canceled});using(var db=new LocalStore(dbRoot))check(!OperationRules.CanCancel(db.Get("operations",Array.Empty<Operation>()).Single()),"cancelled task remains non-retryable after persistent task history reload");
        check(!OperationRules.CanCancel(new(){State="完成"})&&!OperationRules.CanCancel(new(){State="已取消等待"}),"completed or already cancelled tasks cannot be cancelled again");
        var inFlight=0;var maximum=0;
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            var path=request.RequestUri!.AbsolutePath;if(path.EndsWith("/releases"))return Json(Enumerable.Range(1,8).Select(id=>Release(id,true)).ToArray());
            var count=Interlocked.Increment(ref inFlight);lock(typeof(ReleaseStateChecks))maximum=Math.Max(maximum,count);
            try{await Task.Delay(30,ct);return Json(Release(long.Parse(path.Split('/').Last()),false));}finally{Interlocked.Decrement(ref inFlight);}
        }))){var history=await api.ProjectVersions("owner/repo",CancellationToken.None);check(history.Count==8&&history.All(item=>!item.Draft)&&maximum is >1 and <=4,"draft reconciliation runs concurrently within four-request limit");}
    }
}
