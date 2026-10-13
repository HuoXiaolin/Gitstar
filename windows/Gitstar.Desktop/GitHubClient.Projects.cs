namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    public async Task<string> BranchCommit(string repo,string branch,CancellationToken ct)
    {
        Validate(repo);var data=await Request($"repos/{repo}/branches/{Uri.EscapeDataString(branch)}",ct:ct);var sha=S(data.GetProperty("commit"),"sha");
        if(!System.Text.RegularExpressions.Regex.IsMatch(sha,"^[a-fA-F0-9]{40}$"))throw new IOException("无法确认分支的最新提交。");return sha;
    }
    public async Task<(string Name,string Email)> CommitIdentity(CancellationToken ct)
    {
        var data=await Request("user",ct:ct);var login=S(data,"login");if(login.Length==0 || !data.TryGetProperty("id",out var id)||!id.TryGetInt64(out var number))throw new IOException("无法读取 GitHub 提交身份。");
        return (login,$"{number}+{login}@users.noreply.github.com");
    }
    private static ManagedProject ParseProject(JsonElement e)
    {
        var push=false;var admin=false;
        if(e.TryGetProperty("permissions",out var p)){push=p.TryGetProperty("push",out var a)&&a.GetBoolean();admin=p.TryGetProperty("admin",out a)&&a.GetBoolean();}
        return new(S(e,"full_name"),S(e,"description"),e.GetProperty("private").GetBoolean(),S(e,"default_branch"),push,admin,e.TryGetProperty("archived",out var archived)&&archived.GetBoolean(),DateTimeOffset.TryParse(S(e,"updated_at"),out var date)?date:DateTimeOffset.MinValue,S(e,"homepage"),S(e,"visibility"),e.TryGetProperty("fork",out var fork)&&fork.GetBoolean(),e.TryGetProperty("id",out var id)&&id.TryGetInt64(out var number)?number:0);
    }
    public async Task<List<ManagedProject>> MyProjects(CancellationToken ct)
    {
        if(Token.Length==0)throw new IOException("请先登录 GitHub。");
        var list=new List<ManagedProject>();
        for(var page=1;;page++){
            var data=await Request($"user/repos?affiliation=owner,collaborator,organization_member&sort=updated&direction=desc&per_page=100&page={page}",ct:ct);
            list.AddRange(data.EnumerateArray().Select(ParseProject));if(data.GetArrayLength()<100)return list;
        }
    }
    public async Task<ManagedProject> Project(string repo,CancellationToken ct){Validate(repo);return ParseProject(await Request("repos/"+repo,ct:ct,fresh:true));}
    public async Task<ManagedProject> CreateProject(string name,string description,bool privateRepo,bool readme,CancellationToken ct)
    {
        if(!Rules.ValidRepository("owner/"+name))throw new ArgumentException("仓库名称不合法。");
        return ParseProject(await Request("user/repos",HttpMethod.Post,new{name,description,@private=privateRepo,auto_init=readme},ct)) with{CanPush=true,CanAdmin=true};
    }
    public async Task<ManagedProject> EditProject(string repo,string description,string homepage,CancellationToken ct)
    {
        Validate(repo);return ParseProject(await Request("repos/"+repo,HttpMethod.Patch,new{description,homepage},ct));
    }
    private static RepositoryOptions ParseOptions(JsonElement e)=>new(ParseProject(e),RepositorySettings.Features.Where(f=>e.TryGetProperty(f.Key,out var value)&&value.ValueKind is JsonValueKind.True or JsonValueKind.False).ToDictionary(f=>f.Key,f=>e.GetProperty(f.Key).GetBoolean()));
    public async Task<RepositoryOptions> ProjectOptions(string repo,CancellationToken ct){Validate(repo);return ParseOptions(await Request("repos/"+repo,ct:ct));}
    public async Task<ManagedProject> UpdateProjectOptions(RepositoryOptions original,string description,string homepage,string branch,bool privateRepo,bool archived,IReadOnlyDictionary<string,bool> features,CancellationToken ct)
    {
        var repo=original.Project.FullName;Validate(repo);var patch=RepositorySettings.Patch(original,description,homepage,branch,privateRepo,archived,features);
        var current=await ProjectOptions(repo,ct);if(!current.Project.CanAdmin)throw new IOException("当前账号没有此仓库的管理权限，请同步项目后重试。");
        if(current.Project.Archived!=original.Project.Archived)throw new IOException("仓库归档状态已改变，请重新打开项目设置。");
        foreach(var key in patch.Keys)if(!Equals(RepositorySettings.Value(current,key),RepositorySettings.Value(original,key)))throw new IOException("设置已在其他位置改变（"+key+"），请重新打开窗口确认。");
        if(patch.ContainsKey("private") && (current.Project.Fork || current.Project.RepoVisibility=="internal"))throw new IOException("Fork 或内部仓库的可见性请在 GitHub 管理。");
        if(patch.ContainsKey("default_branch")){
            var data=await Request($"repos/{repo}/branches/{Uri.EscapeDataString(branch)}",ct:ct);if(S(data,"name")!=branch)throw new IOException("所选默认分支未在远程仓库确认。");
        }
        if(patch.Count==0)return current.Project;
        var updated=ParseOptions(await Request("repos/"+repo,HttpMethod.Patch,patch,ct));
        foreach(var item in patch)if(!Equals(RepositorySettings.Value(updated,item.Key),item.Value))throw new IOException("GitHub 返回的设置与提交内容不一致（"+item.Key+"），请同步项目核实。");
        return updated.Project;
    }
    public async Task<List<ProjectVersion>> ProjectVersions(string repo,CancellationToken ct)
    {
        Validate(repo);var data=await Request($"repos/{repo}/releases?per_page=30&page=1",ct:ct,fresh:true);
        var versions=data.EnumerateArray().Select(ParseProjectVersion).ToArray();
        using var slots=new SemaphoreSlim(4);var reconciled=await Task.WhenAll(versions.Select(async version=>{
            if(!version.Draft)return version;await slots.WaitAsync(ct);
            try{return await ProjectVersionDetails(repo,version.Id,ct);}catch(GitHubNotFoundException){return null;}finally{slots.Release();}
        }));
        return reconciled.OfType<ProjectVersion>().OrderByDescending(version=>version.Updated).ToList();
    }
    internal static ProjectVersion ParseProjectVersion(JsonElement e)=>new(e.GetProperty("id").GetInt64(),S(e,"tag_name"),S(e,"name"),S(e,"body"),e.GetProperty("draft").GetBoolean(),e.GetProperty("prerelease").GetBoolean(),e.GetProperty("assets").GetArrayLength(),DateTimeOffset.TryParse(S(e,"published_at"),out var date)||DateTimeOffset.TryParse(S(e,"created_at"),out date)?date:DateTimeOffset.MinValue,S(e,"html_url"));
    public async Task<ProjectVersion> ProjectVersionDetails(string repo,long id,CancellationToken ct)
    {
        var version=ParseProjectVersion(await ReleaseDetails(repo,id,ct));if(version.Id!=id)throw new IOException("GitHub 返回的版本 ID 不一致，请重新同步版本。");return version;
    }
    public async Task EditDraft(string repo,long id,string name,string notes,bool beta,CancellationToken ct,string? tag=null)
    {
        var detail=await ReleaseDetails(repo,id,ct);if(!detail.GetProperty("draft").GetBoolean())throw new IOException("此版本已发布，请在 GitHub 管理已发布版本。");
        var patch=new Dictionary<string,object>{{"name",name},{"body",notes},{"prerelease",beta}};
        if(tag!=null){if(!ReleaseRules.ValidTag(tag))throw new IOException("请填写有效版本号，例如 1.0.0.0；不能使用草稿的临时标签。");if(tag!=S(detail,"tag_name")){var found=await AutomationRelease(repo,tag,"",beta,ct);if(found.Release is {} existing&&existing.GetProperty("id").GetInt64()!=id)throw new IOException("版本 "+tag+" 已存在。可使用“同步并发布最新”更新同版本附件，无需增加版本号。");patch["tag_name"]=tag;}}
        var result=await Request($"repos/{repo}/releases/{id}",HttpMethod.Patch,patch,ct);if(tag!=null&&S(result,"tag_name")!=tag)throw new IOException("草稿版本号修改未确认，请同步版本核实。");
    }
}
