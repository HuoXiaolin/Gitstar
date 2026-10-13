using System.Security.Cryptography;
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
    public async Task<bool> SyncReleaseAsset(string repo,long releaseId,string path,string name,long size,string sha,CancellationToken ct)
    {
        var detail=await ReleaseDetails(repo,releaseId,ct);if(detail.TryGetProperty("immutable",out var locked)&&locked.GetBoolean())throw new IOException("版本已锁定，无法继续更新安装包。");var assets=detail.GetProperty("assets").EnumerateArray().ToArray();var prior=assets.Where(asset=>S(asset,"name")==name).ToArray();if(prior.Length>1)throw new IOException("远程存在重复安装包名称。");
        if(prior.Length==1&&await AutomationAssetMatches(repo,prior[0],size,sha,ct))return false;
        var tempName="gitstar-pending-"+sha[..16].ToLowerInvariant()+"-"+name;var pending=assets.FirstOrDefault(asset=>S(asset,"name")==tempName);
        if(pending.ValueKind==JsonValueKind.Undefined)pending=await UploadAutomationAsset(repo,releaseId,path,tempName,ct);
        if(!await AutomationAssetMatches(repo,pending,size,sha,ct))throw new IOException("远程安装包摘要不一致，旧安装包已保留。");
        var refreshed=await ReleaseDetails(repo,releaseId,ct);var freshOld=refreshed.GetProperty("assets").EnumerateArray().FirstOrDefault(asset=>S(asset,"name")==name);
        if(freshOld.ValueKind!=JsonValueKind.Undefined){if(prior.Length==0||freshOld.GetProperty("id").GetInt64()!=prior[0].GetProperty("id").GetInt64())throw new IOException("远程安装包在其他位置发生变化，请稍后重试。");await DeleteAutomationAsset(repo,freshOld.GetProperty("id").GetInt64(),ct);}
        var renamed=await RenameAutomationAsset(repo,pending.GetProperty("id").GetInt64(),name,ct);if(S(renamed,"name")!=name||renamed.GetProperty("id").GetInt64()!=pending.GetProperty("id").GetInt64())throw new IOException("安装包命名结果未确认："+name+" → "+S(renamed,"name"));return true;
    }
    public async Task DeleteAutomationAsset(string repo,long id,CancellationToken ct)=>await Request($"repos/{repo}/releases/assets/{id}",HttpMethod.Delete,ct:ct);
    public async Task<JsonElement> UploadAutomationAsset(string repo,long id,string path,string name,CancellationToken ct)
    {
        await EnsureFreshToken(ct);Validate(repo);using var stream=File.OpenRead(path);using var req=new HttpRequestMessage(HttpMethod.Post,$"https://uploads.github.com/repos/{repo}/releases/{id}/assets?name={Uri.EscapeDataString(name)}&label={Uri.EscapeDataString(Path.GetFileName(path))}"){Content=new StreamContent(stream)};
        req.Headers.Authorization=new("Bearer",Token);req.Headers.Add("X-GitHub-Api-Version","2022-11-28");req.Content.Headers.ContentType=new("application/octet-stream");using var response=await http.SendAsync(req,ct);if(!response.IsSuccessStatusCode)throw new IOException("自动上传失败："+(int)response.StatusCode+"，已完成部分保留供重试。");using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var asset=document.RootElement.Clone();if(asset.GetProperty("size").GetInt64()!=stream.Length||S(asset,"state")!="uploaded")throw new IOException("安装包上传结果未确认。");return asset;
    }
    public async Task<bool> AutomationAssetMatches(string repo,JsonElement asset,long size,string sha,CancellationToken ct)
    {
        if(asset.GetProperty("size").GetInt64()!=size||S(asset,"state")!="uploaded")return false;var digest=S(asset,"digest");if(digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))return digest[7..].Equals(sha,StringComparison.OrdinalIgnoreCase);
        using var response=await AssetResponse(repo,new(asset.GetProperty("id").GetInt64(),S(asset,"name"),size,"",null),ct);using var input=await response.Content.ReadAsStreamAsync(ct);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[81920];long total=0;int read;while((read=await input.ReadAsync(buffer,ct))>0){total+=read;if(total>size)return false;hash.AppendData(buffer,0,read);}return total==size&&Convert.ToHexString(hash.GetHashAndReset()).Equals(sha,StringComparison.OrdinalIgnoreCase);
    }
}
