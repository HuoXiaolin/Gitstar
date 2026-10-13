using System.Security.Cryptography;
namespace Gitstar.Desktop;

public record AutomationResult(GitProjectState Source,string Version,long ReleaseId,string Tag,int Uploaded,int Unchanged,bool Skipped);
public static class ProjectAutomationService
{
    private record Snapshot(string Name,string Path,long Size,string Sha);
    private static bool Immutable(JsonElement release)=>release.TryGetProperty("immutable",out var value)&&value.GetBoolean();
    public static async Task<AutomationResult> Run(ProjectAutomation config,GitHubClient api,string stagingRoot,Func<CancellationToken,Task<GitProjectState>> uploadSource,Action<string,long?> progress,CancellationToken ct,Func<CancellationToken,Task<string>>? sourceNotes=null)
    {
        progress("读取版本与安装包",null);var plan=await Task.Run(()=>AutomationRules.Plan(config),ct);string? configuredNotes=null;
        if(config.NotesFile.Length>0){var notesPath=AutomationRules.ResolveFile(config.SourceFolder,config.NotesFile);if(new FileInfo(notesPath).Length>100000)throw new IOException("更新日志文件过大。");configuredNotes=await File.ReadAllTextAsync(notesPath,ct);}
        var current=await api.Project(config.Repository,ct);if(!current.CanPush||current.Archived)throw new IOException("当前项目没有提交权限或已归档。");
        var found=await api.AutomationRelease(config.Repository,plan.Version,config.TagPrefix,plan.Prerelease,ct);if(found.Higher!=null)throw new IOException("GitHub 已有更高版本 "+found.Higher+"，不会用旧安装包覆盖最新版本。");
        if(found.Release is {} locked&&Immutable(locked)&&config.ReplaceSameVersion)throw new IOException("当前版本在 GitHub 已锁定，无法更新安装包；请构建更高版本。");
        var root=Path.GetFullPath(stagingRoot);Directory.CreateDirectory(root);var stage=Path.GetFullPath(Path.Combine(root,Guid.NewGuid().ToString("N")));if(!stage.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包暂存目录无效。");Directory.CreateDirectory(stage);
        try{
            var snapshots=new List<Snapshot>();foreach(var path in plan.Assets){ct.ThrowIfCancellationRequested();var name=Path.GetFileName(path);progress("准备安装包："+name,null);var copy=Path.Combine(stage,name);using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true))using(var output=new FileStream(copy,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))await input.CopyToAsync(output,ct);using var data=File.OpenRead(copy);var sha=Convert.ToHexString(await SHA256.HashDataAsync(data,ct));snapshots.Add(new(name,copy,data.Length,sha));}
            var manifest=snapshots.FirstOrDefault(item=>item.Name.Equals("SHA256SUMS.txt",StringComparison.OrdinalIgnoreCase));if(manifest!=null){var verified=0;foreach(var line in await File.ReadAllLinesAsync(manifest.Path,ct)){if(string.IsNullOrWhiteSpace(line)||line.TrimStart().StartsWith('#'))continue;var match=System.Text.RegularExpressions.Regex.Match(line,@"^([A-Fa-f0-9]{64})\s+\*?(.+)$");if(!match.Success)throw new IOException("SHA256SUMS.txt 格式无效。");var item=snapshots.FirstOrDefault(item=>item.Name.Equals(match.Groups[2].Value,StringComparison.OrdinalIgnoreCase));if(item==null)continue;if(!item.Sha.Equals(match.Groups[1].Value,StringComparison.OrdinalIgnoreCase))throw new IOException("发行文件与校验清单不一致，可能仍在构建："+item.Name);verified++;}if(verified==0)throw new IOException("校验清单没有覆盖所选安装包，请完成构建后重试。");}
            if(AutomationRules.ReadVersion(plan.VersionFile)!=plan.Version)throw new IOException("准备安装包期间版本号发生变化，请构建完成后重试。");
            progress("自动提交并推送源码",null);var source=await uploadSource(ct);await api.VerifyCommit(config.Repository,source.Commit,ct);
            var release=found.Release;if(release!=null&&!config.ReplaceSameVersion)return new(source,plan.Version,release.Value.GetProperty("id").GetInt64(),release.Value.GetProperty("tag_name").GetString()!,0,0,true);
            var tag=release?.GetProperty("tag_name").GetString()??plan.Tag;string notes;
            if(configuredNotes!=null)notes=configuredNotes;
            else notes="## 更新内容\n\n"+(sourceNotes!=null?await sourceNotes(ct):"同步最新源码与安装包。")+"\n\n本次上传源码提交：`"+source.Commit+"`\n\n安装包版本："+plan.Version;
            notes+="\n\n### 安装包 SHA-256\n\n"+string.Join("\n",snapshots.Select(item=>"- `"+item.Name+"`：`"+item.Sha.ToLowerInvariant()+"`"));
            if(release==null){progress("创建发布草稿",null);try{release=await api.CreateDraft(config.Repository,tag,source.Commit,notes,plan.Prerelease,ct);}catch(Exception ex)when(ex is IOException or HttpRequestException&&!ct.IsCancellationRequested){var reconcile=await api.AutomationRelease(config.Repository,plan.Version,config.TagPrefix,plan.Prerelease,ct);if(reconcile.Release==null)throw;release=reconcile.Release;}}
            var id=release.Value.GetProperty("id").GetInt64();progress("同步安装包",id);var uploaded=0;var unchanged=0;
            foreach(var snapshot in snapshots){
                ct.ThrowIfCancellationRequested();var detail=await api.ReleaseDetails(config.Repository,id,ct);if(Immutable(detail))throw new IOException("版本已锁定，无法继续更新安装包。");var assets=detail.GetProperty("assets").EnumerateArray().ToArray();var prior=assets.Where(asset=>asset.GetProperty("name").GetString()==snapshot.Name).ToArray();if(prior.Length>1)throw new IOException("远程存在重复安装包名称。");
                if(prior.Length==1&&await api.AutomationAssetMatches(config.Repository,prior[0],snapshot.Size,snapshot.Sha,ct)){unchanged++;continue;}
                var tempName="gitstar-pending-"+snapshot.Sha[..16].ToLowerInvariant()+"-"+snapshot.Name;var pending=assets.FirstOrDefault(asset=>asset.GetProperty("name").GetString()==tempName);progress("上传安装包："+snapshot.Name,id);
                if(pending.ValueKind==JsonValueKind.Undefined)pending=await api.UploadAutomationAsset(config.Repository,id,snapshot.Path,tempName,ct);
                if(!await api.AutomationAssetMatches(config.Repository,pending,snapshot.Size,snapshot.Sha,ct))throw new IOException("远程安装包摘要不一致，旧安装包已保留。");
                // Verify the replacement before removing the old asset. The deterministic temporary name supports retries.
                var refreshed=await api.ReleaseDetails(config.Repository,id,ct);var freshOld=refreshed.GetProperty("assets").EnumerateArray().FirstOrDefault(asset=>asset.GetProperty("name").GetString()==snapshot.Name);
                if(freshOld.ValueKind!=JsonValueKind.Undefined){if(prior.Length==0||freshOld.GetProperty("id").GetInt64()!=prior[0].GetProperty("id").GetInt64())throw new IOException("远程安装包在其他位置发生变化，请稍后重试。");await api.DeleteAutomationAsset(config.Repository,freshOld.GetProperty("id").GetInt64(),ct);}
                var renamed=await api.RenameAutomationAsset(config.Repository,pending.GetProperty("id").GetInt64(),snapshot.Name,ct);if(renamed.GetProperty("name").GetString()!=snapshot.Name)throw new IOException("安装包命名结果未确认。");uploaded++;
            }
            var verify=await api.ReleaseDetails(config.Repository,id,ct);foreach(var snapshot in snapshots){var asset=verify.GetProperty("assets").EnumerateArray().FirstOrDefault(item=>item.GetProperty("name").GetString()==snapshot.Name);if(asset.ValueKind==JsonValueKind.Undefined||!await api.AutomationAssetMatches(config.Repository,asset,snapshot.Size,snapshot.Sha,ct))throw new IOException("安装包最终结果未确认。");}
            progress("发布版本并更新日志",id);await api.UpdateAutomationRelease(config.Repository,id,tag,source.Commit,notes,plan.Prerelease,ct);var final=await api.ReleaseDetails(config.Repository,id,ct);if(final.GetProperty("draft").GetBoolean()||final.GetProperty("tag_name").GetString()!=tag||final.GetProperty("prerelease").GetBoolean()!=plan.Prerelease||!final.TryGetProperty("body",out var body)||body.GetString()!=notes)throw new IOException("发布结果尚未确认，可点击重试自动核实。");
            return new(source,plan.Version,id,tag,uploaded,unchanged,false);
        }finally{try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
