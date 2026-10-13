using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class RepositorySettingsChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var project=new ManagedProject("owner/project","description",false,"main",true,true,false,DateTimeOffset.UtcNow,"https://example.org","public");
        var features=new Dictionary<string,bool>{{"has_issues",true},{"has_wiki",true},{"allow_merge_commit",true},{"allow_squash_merge",true},{"allow_rebase_merge",true}};
        var original=new RepositoryOptions(project,features);
        check(RepositorySettings.Patch(original,project.Description,project.Homepage,"main",false,false,features).Count==0,"unchanged project settings do not produce a mutation");
        var changeFeatures=new Dictionary<string,bool>(features){["has_issues"]=false};
        var patch=RepositorySettings.Patch(original,project.Description,project.Homepage,"main",true,false,changeFeatures);
        check(patch.Count==2 && patch.ContainsKey("private") && patch.ContainsKey("has_issues") && !patch.ContainsKey("allow_squash_merge"),"settings save sends only explicitly changed values");
        var unknown=new Dictionary<string,bool>(features){["has_discussions"]=true};
        check(RepositorySettings.Patch(original,project.Description,project.Homepage,"main",false,false,unknown).Count==0,"API-omitted features cannot be enabled using guessed defaults");
        var noMerge=new Dictionary<string,bool>(features){["allow_merge_commit"]=false,["allow_squash_merge"]=false,["allow_rebase_merge"]=false};
        var rejected=false;try{RepositorySettings.Patch(original,project.Description,project.Homepage,"main",false,false,noMerge);}catch(IOException){rejected=true;}check(rejected,"at least one PR merge method must remain enabled");
        var archivedOptions=original with{Project=project with{Archived=true}};
        check(RepositorySettings.Patch(archivedOptions,project.Description,project.Homepage,"main",false,false,features).Keys.SequenceEqual(new[]{"archived"}),"archived repository can be unarchived without modifying unrelated fields");
        rejected=false;try{RepositorySettings.Patch(archivedOptions,"changed",project.Homepage,"main",false,false,features);}catch(IOException){rejected=true;}check(rejected,"archived repository must be unarchived before metadata changes");
        foreach(var guarded in new[]{project with{Fork=true},project with{RepoVisibility="internal"}}){rejected=false;try{RepositorySettings.Patch(original with{Project=guarded},project.Description,project.Homepage,"main",true,false,features);}catch(IOException){rejected=true;}check(rejected,"fork or enterprise-internal visibility is not silently converted");}
        Dictionary<string,object> Data(bool privacy=false,bool admin=true,bool archived=false,bool issues=true)=>new(){["full_name"]="owner/project",["description"]="description",["homepage"]="https://example.org",["private"]=privacy,["visibility"]=privacy?"private":"public",["default_branch"]="main",["permissions"]=new{push=true,admin},["archived"]=archived,["has_issues"]=issues,["has_wiki"]=true,["allow_merge_commit"]=true,["allow_squash_merge"]=true,["allow_rebase_merge"]=true};
        HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data))};
        var writes=0;var state=Data();
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            if(request.Method==HttpMethod.Patch){writes++;using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));foreach(var item in doc.RootElement.EnumerateObject())state[item.Name]=item.Value.ValueKind switch{JsonValueKind.True=>true,JsonValueKind.False=>false,_=>item.Value.GetString()!};state["visibility"]=(bool)state["private"]?"private":"public";}
            return Json(state);
        }))){var opts=await api.ProjectOptions("owner/project",CancellationToken.None);check(opts.Features["has_issues"]&&!opts.Features.ContainsKey("has_discussions"),"settings read preserves actual server values and unavailable options");var changed=await api.UpdateProjectOptions(opts,"description","https://example.org","main",true,false,changeFeatures,CancellationToken.None);check(writes==1&&changed.Private&&!(bool)state["has_issues"],"visibility and feature switches are updated and confirmed from GitHub response");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{if(request.Method!=HttpMethod.Get)throw new Exception("Unauthorized mutation attempted");return Task.FromResult(Json(Data(admin:false)));}))){rejected=false;try{await api.UpdateProjectOptions(original,"changed","https://example.org","main",false,false,features,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"current admin permission is checked before changing settings");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{if(request.Method!=HttpMethod.Get)throw new Exception("Stale visibility mutation attempted");return Task.FromResult(Json(Data(privacy:true)));}))){rejected=false;try{await api.UpdateProjectOptions(original,"description","https://example.org","main",true,false,features,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"visibility changed elsewhere requires a fresh confirmation");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{if(request.Method!=HttpMethod.Get)throw new Exception("Stale feature mutation attempted");return Task.FromResult(Json(Data(issues:false)));}))){rejected=false;try{await api.UpdateProjectOptions(original,"description","https://example.org","main",false,false,changeFeatures,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"concurrent changes to edited feature fields are not overwritten");}
        var branchVerified=false;var branchData=Data();
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            if(request.RequestUri!.AbsolutePath.Contains("/branches/")){branchVerified=true;return Json(new{name="develop"});}
            if(request.Method==HttpMethod.Patch){if(!branchVerified)throw new Exception("Default branch not verified");using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));branchData["default_branch"]=doc.RootElement.GetProperty("default_branch").GetString()!;}
            return Json(branchData);
        }))){var updated=await api.UpdateProjectOptions(original,"description","https://example.org","develop",false,false,features,CancellationToken.None);check(branchVerified&&updated.DefaultBranch=="develop","new default branch must exist on GitHub before changing the repository default");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(Json(Data()))))){rejected=false;try{await api.UpdateProjectOptions(original,"description","https://example.org","main",true,false,features,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"server response must confirm requested visibility before reporting success");}
        var restoreState=Data(archived:true);using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{if(request.Method==HttpMethod.Patch){using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));if(doc.RootElement.EnumerateObject().Count()!=1)throw new Exception("Unarchive should be isolated");restoreState["archived"]=doc.RootElement.GetProperty("archived").GetBoolean();}return Json(restoreState);}))) {var updated=await api.UpdateProjectOptions(archivedOptions,"description","https://example.org","main",false,false,features,CancellationToken.None);check(!updated.Archived,"unarchive flow restores writable repository state");}
    }
}
