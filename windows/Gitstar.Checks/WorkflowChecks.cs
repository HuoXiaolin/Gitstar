using System.IO;
using System.Net;
using System.Net.Http;
using Gitstar.Core;
using Gitstar.Desktop;

static class WorkflowChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var original=new ManagedProject("owner/repo","",false,"main",true,true,false,DateTimeOffset.Now,Id:10);
        check(ProjectRules.InvalidatedProjects([original],[original with{Description="edited",FullName="OWNER/REPO"}]).Length==0,"repository metadata and casing changes preserve identity-matched caches");
        check(ProjectRules.InvalidatedProjects([original],[original with{Id=11}]).SequenceEqual(new[]{"owner/repo"}),"same-name recreated repository invalidates old release and workspace state caches");
        check(ProjectRules.InvalidatedProjects([original],[]).SequenceEqual(new[]{"owner/repo"}),"deleted remote project invalidates old caches after successful project synchronization");
        check(ProjectRules.InvalidatedProjects([original with{Id=0}],[original]).Length==1,"legacy name-only project cache is invalidated once when repository identity becomes available");
        var phase=0;
        using(var identityApi=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/releases")?(phase==0?"[{\"id\":9,\"tag_name\":\"v1\",\"draft\":false,\"prerelease\":false,\"assets\":[]}]":"[]"):"{\"id\":"+(phase==0?"10":"11")+",\"full_name\":\"owner/repo\",\"private\":false}")})))){
            var before=await identityApi.Project("owner/repo",CancellationToken.None);var oldVersions=await identityApi.ProjectVersions("owner/repo",CancellationToken.None);phase=1;var after=await identityApi.Project("owner/repo",CancellationToken.None);
            check(before.Id==10&&after.Id==11&&!ProjectRules.SameRepository(before,after),"GitHub project parsing retains identity to detect deletion and recreation");
            check(oldVersions.Count==1&&(await identityApi.ProjectVersions("owner/repo",CancellationToken.None)).Count==0,"version synchronization reads current empty release list after remote deletion");
        }
        using(var missingApi=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound){Content=new StringContent("{}")})))){var missing=false;try{await missingApi.Project("owner/repo",CancellationToken.None);}catch(GitHubNotFoundException){missing=true;}check(missing,"unavailable repository response distinguishes stale-cache invalidation from temporary network failures");}
        check(SourceWorkflow.Recommend(new("C:\\plain","",""),"owner/repo")==WorkspaceAction.Initialize,"plain source directory automatically chooses initialization");
        check(SourceWorkflow.Recommend(new("C:\\repo","C:\\repo","https://github.com/owner/repo.git"),"owner/repo")==WorkspaceAction.UseRoot,"matching repository automatically chooses existing root");
        check(SourceWorkflow.Recommend(new("C:\\repo\\source","C:\\repo","https://github.com/owner/repo.git"),"owner/repo")==WorkspaceAction.CopyIndependent,"subdirectory recommendation preserves selected source scope using independent copy");
        check(SourceWorkflow.Recommend(new("C:\\repo","C:\\repo","https://github.com/another/repo.git"),"owner/repo")==WorkspaceAction.RepairOrigin,"mismatched origin uses explicit remote repair recommendation");
        check(SourceWorkflow.CommitMessage([new("??","a.cs"),new("??","b.cs"),new(" M","c.cs")])=="更新源码：新增 2 项，修改 1 项","commit summary is generated from selected changes");
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri!.AbsolutePath=="/user"?"{\"login\":\"fixture\",\"id\":17}":"{\"commit\":{\"sha\":\"0123456789012345678901234567890123456789\"}}") })))){
            var identity=await api.CommitIdentity(CancellationToken.None);check(identity.Name=="fixture"&&identity.Email=="17+fixture@users.noreply.github.com","missing commit identity can use authenticated GitHub account and noreply email");
            check((await api.BranchCommit("owner/repo","main",CancellationToken.None)).Length==40,"release commit automatically resolves from remote branch");
        }
        var folder=Path.Combine(root,"automatic-identity");Directory.CreateDirectory(folder);var git=new GitWorkspace("");await git.Initialize(folder,"owner/repo","main",CancellationToken.None);await git.Run(folder,CancellationToken.None,"config","--local","user.name","");await git.Run(folder,CancellationToken.None,"config","--local","user.email","");var calls=0;
        Task<(string Name,string Email)> Fallback(CancellationToken ct){calls++;return Task.FromResult(("Fixture","17+fixture@users.noreply.github.com"));}
        await git.EnsureIdentity(folder,Fallback,CancellationToken.None);check(calls==1&&await git.Run(folder,CancellationToken.None,"config","--get","user.name")=="Fixture","first upload prepares missing identity in selected repository");
        await git.Run(folder,CancellationToken.None,"config","--local","user.name","Existing author");await git.EnsureIdentity(folder,Fallback,CancellationToken.None);check(calls==1&&await git.Run(folder,CancellationToken.None,"config","--get","user.name")=="Existing author","configured commit identity remains unchanged without another profile request");
    }
}
