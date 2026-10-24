namespace Gitstar.Desktop;

public record AutomationResult(GitProjectState Source,string Version,long ReleaseId,string Tag,int Uploaded,int Unchanged,bool Skipped,ProjectVersion? Release=null);
public static class ProjectAutomationService
{
    private static bool Immutable(JsonElement release)=>release.TryGetProperty("immutable",out var value)&&value.GetBoolean();
    public static async Task<AutomationResult> Run(ProjectAutomation config,GitHubClient api,string stagingRoot,Func<CancellationToken,Task<GitProjectState>> uploadSource,Action<string,long?> progress,CancellationToken ct,Func<CancellationToken,Task<string>>? sourceNotes=null,Action<ReleaseUploadProgress>? uploadProgress=null)
    {
        progress("读取版本与安装包",null);var plan=await Task.Run(()=>AutomationRules.Plan(config),ct);
        var configuredNotes=ReleaseNotes.Read(config.SourceFolder,config.NotesFile,config.ManualNotes);
        var current=await api.Project(config.Repository,ct);if(!current.CanPush||current.Archived)throw new IOException("当前项目没有提交权限或已归档。");
        var found=await api.AutomationRelease(config.Repository,plan.Version,config.TagPrefix,plan.Prerelease,ct);if(found.Higher!=null)throw new IOException("GitHub 已有更高版本 "+found.Higher+"，不会用旧安装包覆盖最新版本。");
        if(found.Release is {} locked&&Immutable(locked)&&config.ReplaceSameVersion)throw new IOException("当前版本在 GitHub 已锁定，无法更新安装包；请构建更高版本。");
        var root=Path.GetFullPath(stagingRoot);Directory.CreateDirectory(root);var stage=Path.GetFullPath(Path.Combine(root,Guid.NewGuid().ToString("N")));if(!stage.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包暂存目录无效。");Directory.CreateDirectory(stage);
        try{
            var snapshots=await ReleaseAssetFiles.Stage(plan.Assets,stage,ct,name=>progress("准备安装包："+name,null));
            if(AutomationRules.ReadVersion(plan.VersionFile)!=plan.Version)throw new IOException("准备安装包期间版本号发生变化，请构建完成后重试。");
            progress("自动提交并推送源码",null);var source=await uploadSource(ct);await api.VerifyCommit(config.Repository,source.Commit,ct);
            var release=found.Release;if(release!=null&&!config.ReplaceSameVersion){var currentRelease=await api.ProjectVersionDetails(config.Repository,release.Value.GetProperty("id").GetInt64(),ct);return new(source,plan.Version,currentRelease.Id,currentRelease.Tag,0,0,true,currentRelease);}
            var originalTag=release?.GetProperty("tag_name").GetString();var tag=originalTag==null||ReleaseRules.TemporaryTag(originalTag)?plan.Tag:originalTag;var notes=configuredNotes;
            notes+="\n\n### 安装包 SHA-256\n\n"+string.Join("\n",snapshots.Select(item=>"- `"+item.Name+"`：`"+item.Sha.ToLowerInvariant()+"`"));
            if(release==null){progress("创建发布草稿",null);try{release=await api.CreateDraft(config.Repository,tag,source.Commit,notes,plan.Prerelease,ct);}catch(Exception ex)when(ex is IOException or HttpRequestException&&!ct.IsCancellationRequested){var reconcile=await api.AutomationRelease(config.Repository,plan.Version,config.TagPrefix,plan.Prerelease,ct);if(reconcile.Release==null)throw;release=reconcile.Release;}}
            var id=release.Value.GetProperty("id").GetInt64();progress("同步安装包",id);var uploaded=0;var unchanged=0;
            var verifiedAssets=new HashSet<string>(StringComparer.Ordinal);
            foreach(var snapshot in snapshots){
                ct.ThrowIfCancellationRequested();progress("核对并同步附件："+snapshot.OriginalName,id);
                if(await api.SyncReleaseAsset(config.Repository,id,snapshot.Path,snapshot.Name,snapshot.Size,snapshot.Sha,ct,verifiedAssets,text=>progress(text,id),uploadProgress))uploaded++;else unchanged++;
            }
            var verify=await api.ReleaseDetails(config.Repository,id,ct);foreach(var snapshot in snapshots){var asset=verify.GetProperty("assets").EnumerateArray().FirstOrDefault(item=>item.GetProperty("name").GetString()==snapshot.Name);if(asset.ValueKind==JsonValueKind.Undefined||!await api.AutomationAssetMatches(config.Repository,asset,snapshot.Size,snapshot.Sha,ct,verifiedAssets))throw new IOException("安装包最终结果未确认。");}
            progress("发布版本并更新日志",id);await api.UpdateAutomationRelease(config.Repository,id,tag,source.Commit,notes,plan.Prerelease,ct);var final=await api.ReleaseDetails(config.Repository,id,ct);if(final.GetProperty("draft").GetBoolean()||final.GetProperty("tag_name").GetString()!=tag||final.GetProperty("prerelease").GetBoolean()!=plan.Prerelease||!final.TryGetProperty("body",out var body)||body.GetString()!=notes)throw new IOException("发布结果尚未确认，可点击重试自动核实。");
            return new(source,plan.Version,id,tag,uploaded,unchanged,false,GitHubClient.ParseProjectVersion(final));
        }finally{try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
