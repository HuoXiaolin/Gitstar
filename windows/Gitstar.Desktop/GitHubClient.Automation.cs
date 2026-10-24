using System.Security.Cryptography;
using System.Diagnostics;
namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    public async Task<(JsonElement? Release,string? Higher)> AutomationRelease(string repo,string version,string prefix,bool beta,CancellationToken ct)
    {
        Validate(repo);JsonElement? selected=null;var temporary=new List<JsonElement>();string? higher=null;var exact=prefix+version;
        for(var page=1;;page++){
            var data=await Request($"repos/{repo}/releases?per_page=100&page={page}",ct:ct,fresh:true);
            foreach(var release in data.EnumerateArray()){
                var tag=S(release,"tag_name");var plain=prefix.Length>0?(tag.StartsWith(prefix,StringComparison.Ordinal)?tag[prefix.Length..]:""):tag;var comparison=Rules.CompareVersions(plain,version);
                if(release.GetProperty("draft").GetBoolean()&&ReleaseRules.TemporaryTag(tag)&&(S(release,"name")==exact||Rules.CompareVersions(S(release,"name"),version)==0))temporary.Add(release.Clone());
                if(comparison>0 && release.GetProperty("prerelease").GetBoolean()==beta&&!release.GetProperty("draft").GetBoolean()&&(higher==null||Rules.CompareVersions(plain,higher)>0))higher=plain;
                if(tag==exact||comparison==0){if(selected is {} prior && S(prior,"tag_name")!=tag){if(tag==exact)selected=release.Clone();else if(S(prior,"tag_name")!=exact)throw new IOException("多个版本标签对应当前版本，无法确定自动发布目标。");}else selected=release.Clone();}
            }
            if(data.GetArrayLength()<100){if(selected==null){if(temporary.Count>1)throw new IOException("存在多个同版本的临时草稿，请选择需要继续的草稿。");selected=temporary.Count==1?temporary[0]:null;}return (selected,higher);}
        }
    }
    public async Task<JsonElement> UpdateAutomationRelease(string repo,long id,string tag,string commit,string notes,bool beta,CancellationToken ct)
    {
        var current=await ReleaseDetails(repo,id,ct);var patch=new Dictionary<string,object>{{"name",tag},{"body",notes},{"prerelease",beta},{"draft",false},{"make_latest",beta?"false":"true"}};
        if(S(current,"tag_name")!=tag){if(!current.GetProperty("draft").GetBoolean()||!ReleaseRules.TemporaryTag(S(current,"tag_name")))throw new IOException("版本标签已发生变化，请同步版本后重试。");patch["tag_name"]=tag;patch["target_commitish"]=commit;}
        return await Request($"repos/{repo}/releases/{id}",HttpMethod.Patch,patch,ct);
    }
    public Task<JsonElement> RenameAutomationAsset(string repo,long id,string name,CancellationToken ct)=>Request($"repos/{repo}/releases/assets/{id}",HttpMethod.Patch,new{name},ct);
    public async Task<bool> SyncReleaseAsset(string repo,long releaseId,string path,string name,long size,string sha,CancellationToken ct,HashSet<string>? verified=null,Action<string>? progress=null,Action<ReleaseUploadProgress>? uploadProgress=null)
    {
        var detail=await ReleaseDetails(repo,releaseId,ct);if(detail.TryGetProperty("immutable",out var locked)&&locked.GetBoolean())throw new IOException("版本已锁定，无法继续更新安装包。");var assets=detail.GetProperty("assets").EnumerateArray().ToArray();var prior=assets.Where(asset=>S(asset,"name")==name).ToArray();if(prior.Length>1)throw new IOException("远程存在重复安装包名称。");
        progress?.Invoke("核对远程附件："+name);
        if(prior.Length==1&&await AutomationAssetMatches(repo,prior[0],size,sha,ct,verified,progress))return false;
        var tempName="gitstar-pending-"+sha[..16].ToLowerInvariant()+"-"+name;var pending=assets.FirstOrDefault(asset=>S(asset,"name")==tempName);
        if(pending.ValueKind==JsonValueKind.Undefined){progress?.Invoke("上传附件："+name+" · "+(size/1024d/1024d).ToString("0.0")+" MB");pending=await UploadAutomationAsset(repo,releaseId,path,tempName,ct,uploadProgress);}
        progress?.Invoke("GitHub 已接收，核对附件："+name);
        if(!await AutomationAssetMatches(repo,pending,size,sha,ct,verified,progress))throw new IOException("远程安装包摘要不一致，旧安装包已保留。");
        var refreshed=await ReleaseDetails(repo,releaseId,ct);var freshOld=refreshed.GetProperty("assets").EnumerateArray().FirstOrDefault(asset=>S(asset,"name")==name);
        if(freshOld.ValueKind!=JsonValueKind.Undefined){if(prior.Length==0||freshOld.GetProperty("id").GetInt64()!=prior[0].GetProperty("id").GetInt64())throw new IOException("远程安装包在其他位置发生变化，请稍后重试。");await DeleteAutomationAsset(repo,freshOld.GetProperty("id").GetInt64(),ct);}
        var renamed=await RenameAutomationAsset(repo,pending.GetProperty("id").GetInt64(),name,ct);if(S(renamed,"name")!=name||renamed.GetProperty("id").GetInt64()!=pending.GetProperty("id").GetInt64())throw new IOException("安装包命名结果未确认："+name+" → "+S(renamed,"name"));return true;
    }
    public async Task DeleteAutomationAsset(string repo,long id,CancellationToken ct)=>await Request($"repos/{repo}/releases/assets/{id}",HttpMethod.Delete,ct:ct);
    public async Task<JsonElement> UploadAutomationAsset(string repo,long id,string path,string name,CancellationToken ct,Action<ReleaseUploadProgress>? progress=null)
    {
        await EnsureFreshToken(ct);Validate(repo);using var stream=File.OpenRead(path);var total=stream.Length;var clock=Stopwatch.StartNew();long sent=0,lastActivity=0;var stalled=false;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(30));using var monitorStop=new CancellationTokenSource();
        void Report()=>progress?.Invoke(new(Path.GetFileName(path),Interlocked.Read(ref sent),total,clock.Elapsed,Interlocked.Read(ref sent)>=total,clock.Elapsed-TimeSpan.FromTicks(Interlocked.Read(ref lastActivity))));
        async Task Monitor(){try{using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));while(await timer.WaitForNextTickAsync(monitorStop.Token).ConfigureAwait(false)){Report();if(clock.Elapsed-TimeSpan.FromTicks(Interlocked.Read(ref lastActivity))>=TimeSpan.FromMinutes(2)){stalled=true;deadline.Cancel();return;}}}catch(OperationCanceledException)when(monitorStop.IsCancellationRequested){}}
        Report();var monitor=Monitor();
        try{
            using var req=new HttpRequestMessage(HttpMethod.Post,$"https://uploads.github.com/repos/{repo}/releases/{id}/assets?name={Uri.EscapeDataString(name)}&label={Uri.EscapeDataString(Path.GetFileName(path))}"){Content=new ProgressUploadContent(stream,value=>{Interlocked.Exchange(ref sent,value);Interlocked.Exchange(ref lastActivity,clock.Elapsed.Ticks);})};
            req.Headers.Authorization=new("Bearer",Token);req.Headers.Add("X-GitHub-Api-Version","2022-11-28");req.Content.Headers.ContentType=new("application/octet-stream");
            using var response=await uploadHttp.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,deadline.Token);Report();if(!response.IsSuccessStatusCode)throw new IOException("自动上传失败："+(int)response.StatusCode+"，已完成部分保留供重试。");using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));var asset=document.RootElement.Clone();if(asset.GetProperty("size").GetInt64()!=total||S(asset,"state")!="uploaded")throw new IOException("安装包上传结果未确认。");return asset;
        }catch(OperationCanceledException error)when(!ct.IsCancellationRequested){throw new IOException((stalled?"上传连续 2 分钟无进展，请检查网络或代理。":"上传超过 30 分钟，请检查网络后重试。")+$" 已发送 {Interlocked.Read(ref sent)/1048576d:0.0} / {total/1048576d:0.0} MB；远端结果待核实，重试会先核对已有附件。",error);}
        finally{monitorStop.Cancel();await monitor;}
    }
    public async Task<bool> AutomationAssetMatches(string repo,JsonElement asset,long size,string sha,CancellationToken ct,HashSet<string>? verified=null,Action<string>? progress=null)
    {
        if(asset.GetProperty("size").GetInt64()!=size||S(asset,"state")!="uploaded")return false;var digest=S(asset,"digest");if(digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))return digest[7..].Equals(sha,StringComparison.OrdinalIgnoreCase);
        // GitHub asset IDs identify immutable content; renaming keeps the ID, replacing creates a new ID.
        // The caller owns this set for one publish attempt only, never across projects or runs.
        var key=repo.ToLowerInvariant()+"/"+asset.GetProperty("id").GetInt64()+"/"+size+"/"+sha.ToUpperInvariant();
        if(verified?.Contains(key)==true)return true;
        progress?.Invoke("远端未提供摘要，下载核验："+S(asset,"name")+" · "+(size/1024d/1024d).ToString("0.0")+" MB");
        using var response=await AssetResponse(repo,new(asset.GetProperty("id").GetInt64(),S(asset,"name"),size,"",null),ct);using var input=await response.Content.ReadAsStreamAsync(ct);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[81920];long total=0;int read;while((read=await input.ReadAsync(buffer,ct))>0){total+=read;if(total>size)return false;hash.AppendData(buffer,0,read);}var matches=total==size&&Convert.ToHexString(hash.GetHashAndReset()).Equals(sha,StringComparison.OrdinalIgnoreCase);if(matches)verified?.Add(key);return matches;
    }
}
