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
        await File.WriteAllTextAsync(Path.Combine(folder,"source.txt"),"first");await git.Run(folder,CancellationToken.None,"add","--","source.txt");await git.Run(folder,CancellationToken.None,"commit","-m","first");
        var clean=await git.Inspect(folder,CancellationToken.None);
        check(clean.CheckedAt!=null&&clean.CommittedAt!=null&&clean.CommitCount==1&&clean.CommittedFileCount==1&&clean.LastCommitFileCount==1&&clean.CommitMessage=="first","source inspection reads actual commit time history count committed snapshot and last change count");
        check(clean.Changes.Count==0&&!clean.SourceSynced&&clean.SyncSummary.Contains("尚未核实"),"clean working tree alone does not claim source was uploaded");
        var synced=clean with{RemoteCommit=clean.Commit};check(synced.SourceSynced&&synced.SyncSummary.Contains("安装包更新"),"matching remote commit distinguishes synchronized source from release package updates");
        var pending=clean with{RemoteCommit=new string('0',40)};check(!pending.SourceSynced&&pending.SyncSummary.Contains("推送已有提交"),"clean source with different remote commit remains uploadable");
        await File.WriteAllTextAsync(Path.Combine(folder,"source.txt"),"second");var changed=await git.Inspect(folder,CancellationToken.None);
        check(!SourceWorkflow.SameSnapshot(clean,changed)&&changed.SyncSummary.Contains("未提交变更"),"fresh edit after clean review invalidates stale push-only decision");
        await File.WriteAllTextAsync(Path.Combine(folder,"new.txt"),"new");var added=await git.Inspect(folder,CancellationToken.None);
        check(!SourceWorkflow.SameSnapshot(changed,added),"new files added while source dialog is open require refreshed selection");
        check(SourceWorkflow.SameSnapshot(added,await git.Inspect(folder,CancellationToken.None)),"unchanged refreshed source snapshot remains uploadable");
        check(!SourceWorkflow.SameSnapshot(added,added with{Commit=new string('1',40)})&&!SourceWorkflow.SameSnapshot(added,added with{Remote="https://github.com/other/repo.git"}),"changed HEAD or origin invalidates reviewed upload target");
        await git.Run(folder,CancellationToken.None,"add","--","source.txt","new.txt");await git.Run(folder,CancellationToken.None,"commit","-m","second");var committed=await git.Inspect(folder,CancellationToken.None);
        check(committed.Commit!=clean.Commit&&committed.CommitCount==2&&committed.CommittedFileCount==2&&committed.LastCommitFileCount==2&&committed.Changes.Count==0&&committed.CommitMessage=="second","successful commit refreshes counts message and timestamp instead of retaining previous source status");
        await git.Run(folder,CancellationToken.None,"config","--local","credential.helper","!echo gitstar-test-helper-was-run >&2; exit 1");
        check(await git.Run(folder,CancellationToken.None,"config","--get","credential.interactive")=="false","all Gitstar Git subprocesses disable credential-manager interaction");
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        async Task Deny(){try{while(!timeout.IsCancellationRequested){using var client=await listener.AcceptTcpClientAsync(timeout.Token);using var stream=client.GetStream();var buffer=new byte[4096];var received=await stream.ReadAsync(buffer,timeout.Token);if(received==0)continue;await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"fixture\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),timeout.Token);}}catch(OperationCanceledException){}}
        var server=Deny();string denied="";try{await git.Run(folder,timeout.Token,"-c","http.proxy=","ls-remote",$"http://127.0.0.1:{port}/repo.git");}catch(IOException ex){denied=ex.Message;}finally{timeout.Cancel();await server;listener.Stop();}
        check((denied.Contains("terminal prompts disabled")||denied.Contains("unable to get password from user"))&&!denied.Contains("gitstar-test-helper-was-run"),"unauthorized Git request fails promptly without invoking configured external login helper");
        check((await git.Run(folder,CancellationToken.None,"config","--local","--get","credential.helper")).Contains("gitstar-test-helper-was-run"),"silent Git policy leaves repository and global credential configuration unchanged");
        var askpass=Path.Combine(folder,"app-askpass.cmd");await File.WriteAllTextAsync(askpass,"@echo off\r\necho fixture-answer\r\n");
        var authenticated=new GitWorkspace("fixture-token");var start=authenticated.CreateStartInfo(folder,"credential","fill");start.RedirectStandardInput=true;start.Environment["GIT_ASKPASS"]=askpass;
        using(var process=System.Diagnostics.Process.Start(start)!){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.StandardInput.WriteAsync("protocol=https\nhost=github.com\n\n");process.StandardInput.Close();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));await process.WaitForExitAsync(deadline.Token);var actual=await output;var diagnostic=await error;
            check(process.ExitCode==0&&actual.Contains("password=fixture-answer")&&!diagnostic.Contains("gitstar-test-helper-was-run"),"saved app authentication can use its own askpass while external credential managers stay disabled");
        }
        check(start.Environment["GCM_INTERACTIVE"]=="0"&&start.Environment["GIT_TERMINAL_PROMPT"]=="0","allowing app credential delivery does not enable external GUI or terminal login");
    }
}
