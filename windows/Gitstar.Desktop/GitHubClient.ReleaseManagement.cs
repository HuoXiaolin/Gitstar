namespace Gitstar.Desktop;

public record ManagedReleaseAsset(long Id,string Name,long Size,string Digest,string Updated,string Url);
public record ReleaseInventory(ManagedProject Project,ProjectVersion Version,bool Immutable,string Updated,List<ManagedReleaseAsset> Assets)
{
    public bool Writable=>Project.CanPush&&!Project.Archived;
}

public sealed partial class GitHubClient
{
    public async Task<ReleaseInventory> ReleaseInventory(string repo,long id,CancellationToken ct)
    {
        var project=await Project(repo,ct);var detail=await ReleaseDetails(repo,id,ct);var version=ParseProjectVersion(detail);
        if(version.Id!=id)throw new IOException("版本 ID 不一致，请重新打开管理窗口。");
        var assets=new List<ManagedReleaseAsset>();
        for(var page=1;;page++){
            var data=await Request($"repos/{repo}/releases/{id}/assets?per_page=100&page={page}",ct:ct,fresh:true);
            assets.AddRange(data.EnumerateArray().Select(asset=>new ManagedReleaseAsset(asset.GetProperty("id").GetInt64(),S(asset,"name"),asset.GetProperty("size").GetInt64(),S(asset,"digest"),S(asset,"updated_at"),S(asset,"browser_download_url"))));
            if(data.GetArrayLength()<100)break;
        }
        return new(project,version with{AssetCount=assets.Count},detail.TryGetProperty("immutable",out var locked)&&locked.GetBoolean(),S(detail,"updated_at"),assets);
    }
    private static void CheckReleaseTarget(ReleaseInventory expected,ReleaseInventory current)
    {
        if(!current.Writable)throw new IOException("当前账号没有删除权限，或仓库已归档。");
        if(!ProjectRules.SameRepository(expected.Project,current.Project)||expected.Version.Id!=current.Version.Id||expected.Version.Tag!=current.Version.Tag||expected.Version.Draft!=current.Version.Draft)throw new IOException("仓库或版本状态已改变，请刷新后重新选择。");
    }
    public async Task<ReleaseInventory> DeleteReleaseAsset(ReleaseInventory expected,ManagedReleaseAsset selected,CancellationToken ct)
    {
        if(!expected.Assets.Contains(selected))throw new IOException("安装包不属于此版本。");var repo=expected.Project.FullName;var current=await ReleaseInventory(repo,expected.Version.Id,ct);CheckReleaseTarget(expected,current);
        if(current.Immutable)throw new IOException("此版本已锁定，GitHub 不允许删除单个附件。");
        var target=current.Assets.SingleOrDefault(asset=>asset.Id==selected.Id);if(target==null)return current;
        if(target!=selected)throw new IOException("安装包已在其他位置改变，请刷新后重新选择。");
        try{await DeleteAutomationAsset(repo,selected.Id,ct);}catch(GitHubNotFoundException){}
        var verified=await ReleaseInventory(repo,expected.Version.Id,ct);CheckReleaseTarget(current,verified);
        if(verified.Assets.Any(asset=>asset.Id==selected.Id))throw new IOException("安装包删除结果尚未确认，请刷新核实。");return verified;
    }
    public async Task DeleteProjectRelease(ReleaseInventory expected,CancellationToken ct)
    {
        var repo=expected.Project.FullName;var current=await ReleaseInventory(repo,expected.Version.Id,ct);CheckReleaseTarget(expected,current);
        if(!ProjectTimeRules.SameRemoteVersion(expected.Version,current.Version)||expected.Updated!=current.Updated||!expected.Assets.OrderBy(asset=>asset.Id).SequenceEqual(current.Assets.OrderBy(asset=>asset.Id)))throw new IOException("版本内容或附件已改变，请刷新后重新确认删除。");
        await Request($"repos/{repo}/releases/{current.Version.Id}",HttpMethod.Delete,ct:ct);
        var project=await Project(repo,ct);if(!ProjectRules.SameRepository(current.Project,project)||!project.CanPush)throw new IOException("删除后仓库权限发生变化，结果待核实。");
        try{await ReleaseDetails(repo,current.Version.Id,ct);}catch(GitHubNotFoundException){return;}
        throw new IOException("版本删除结果尚未确认，请同步版本核实。");
    }
}
