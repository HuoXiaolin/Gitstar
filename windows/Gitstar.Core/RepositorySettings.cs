namespace Gitstar.Core;

public record RepositoryOptions(ManagedProject Project,Dictionary<string,bool> Features);
public static class RepositorySettings
{
    public static readonly (string Key,string Label,string Section)[] Features=[
        ("has_issues","Issues · 问题跟踪","功能"),("has_wiki","Wiki · 项目文档","功能"),("has_discussions","Discussions · 讨论区","功能"),("has_projects","Projects · 项目看板","功能"),("is_template","作为模板仓库","功能"),
        ("allow_merge_commit","允许 Merge commit 合并","合并"),("allow_squash_merge","允许 Squash 压缩合并","合并"),("allow_rebase_merge","允许 Rebase 变基合并","合并"),("allow_auto_merge","允许 PR 自动合并","合并"),("delete_branch_on_merge","合并后自动删除源分支","合并"),("allow_update_branch","允许更新落后的 PR 分支","合并"),
        ("web_commit_signoff_required","网页提交需 Sign-off 签署","贡献"),("allow_forking","允许私有 Fork（依组织策略）","贡献")];
    public static Dictionary<string,object> Patch(RepositoryOptions original,string description,string homepage,string branch,bool privateRepo,bool archived,IReadOnlyDictionary<string,bool> features)
    {
        var p=original.Project;var patch=new Dictionary<string,object>();
        void Change(string key,object before,object after){if(!Equals(before,after))patch[key]=after;}
        Change("description",p.Description,description);Change("homepage",p.Homepage,homepage);Change("default_branch",p.DefaultBranch,branch);Change("private",p.Private,privateRepo);Change("archived",p.Archived,archived);
        foreach(var item in Features)if(original.Features.TryGetValue(item.Key,out var previous)&&features.TryGetValue(item.Key,out var next))Change(item.Key,previous,next);
        if(p.Archived && patch.Keys.Any(k=>k!="archived"))throw new IOException("已归档仓库只能先取消归档，再修改其他设置。");
        if(patch.ContainsKey("private") && (p.Fork || p.RepoVisibility=="internal"))throw new IOException("Fork 或内部仓库的可见性请在 GitHub 管理。");
        if(patch.ContainsKey("default_branch") && string.IsNullOrWhiteSpace(branch))throw new IOException("默认分支不能为空。");
        var mergeKeys=new[]{"allow_merge_commit","allow_squash_merge","allow_rebase_merge"};
        if(mergeKeys.Any(patch.ContainsKey) && mergeKeys.All(k=>features.TryGetValue(k,out var value)&&!value))throw new IOException("至少保留一种 PR 合并方式。");
        return patch;
    }
    public static object? Value(RepositoryOptions options,string key)=>key switch{
        "description"=>options.Project.Description,"homepage"=>options.Project.Homepage,"default_branch"=>options.Project.DefaultBranch,"private"=>options.Project.Private,"archived"=>options.Project.Archived,_=>options.Features.TryGetValue(key,out var value)?value:null};
}
