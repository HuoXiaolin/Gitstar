using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class AutomationChecks
{
    private const string Sha="0123456789012345678901234567890123456789";
    public static async Task Run(Action<bool,string> check,string parent)
    {
        var root=Path.Combine(parent,"automation");var source=Path.Combine(root,"source");var release=Path.Combine(root,"release");Directory.CreateDirectory(source);Directory.CreateDirectory(release);await File.WriteAllTextAsync(Path.Combine(source,"version.txt"),"1.0.0.0");await File.WriteAllTextAsync(Path.Combine(release,"app.zip"),"latest package");var config=new ProjectAutomation("owner/repo",source,release,"version.txt",AssetPatterns:"*.zip");
        var props=Path.Combine(source,"Directory.Build.props");await File.WriteAllTextAsync(props,"<Project><PropertyGroup><Version>1.0.0.0</Version></PropertyGroup><PropertyGroup Condition=\"'$(TargetFramework)'=='other'\"><Version>9.0.0</Version></PropertyGroup></Project>");check(AutomationRules.ReadVersion(props)=="1.0.0.0","automatic version reading preserves four components and ignores conditional overrides");
        await File.WriteAllTextAsync(Path.Combine(source,"package.json"),"{\"version\":\"2.0.0-beta.2\"}");check(AutomationRules.ReadVersion(Path.Combine(source,"package.json"))=="2.0.0-beta.2","package.json prerelease version automatically determines test channel");
        check(AutomationRules.FileVersion("app-v1.2.3-beta.2-windows-x64.zip")=="1.2.3-beta.2","installer filename version excludes platform and file extension");
        await File.WriteAllTextAsync(Path.Combine(release,"app-v0.9.0.zip"),"older");check(AutomationRules.Plan(config).Assets.Length==1,"older version packages are excluded from current flat release folder");
        var versionFolders=Path.Combine(root,"versions");Directory.CreateDirectory(Path.Combine(versionFolders,"0.9.0"));Directory.CreateDirectory(Path.Combine(versionFolders,"1.0.0.0"));await File.WriteAllTextAsync(Path.Combine(versionFolders,"1.0.0.0","app.zip"),"current");Directory.SetLastWriteTimeUtc(Path.Combine(versionFolders,"0.9.0"),DateTime.UtcNow.AddDays(1));check(AutomationRules.Plan(config with{ReleaseFolder=versionFolders}).ReleaseFolder.EndsWith("1.0.0.0"),"latest release folder is selected by version number rather than modification time");
        var sourceCalls=0;Task<GitProjectState> Upload(CancellationToken ct){ct.ThrowIfCancellationRequested();sourceCalls++;return Task.FromResult(new GitProjectState(source,"main","https://github.com/owner/repo.git",Sha,[],""));}
        var progress=new List<string>();void Progress(string text,long? id)=>progress.Add(text);
        using(var server=new AutomationServer())using(var api=new GitHubClient(new(),server.Handler)){
            var result=await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None,ct=>Task.FromResult("- fixture commit"));
            check(result.Uploaded==1&&!server.Draft&&sourceCalls==1&&server.Creates==1,"one-click flow pushes source before draft creation then uploads verifies and publishes");
            check(server.Notes.Contains("fixture commit")&&server.Notes.Contains(Sha)&&server.Notes.Contains("SHA-256"),"automatic release notes contain commit information and package digests");
            var repeat=await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);
            check(repeat.Unchanged==1&&server.Uploads==1&&server.Creates==1,"repeated click reuses verified same-version assets and avoids duplicate release creation");
        }
        using(var server=new AutomationServer{Exists=true,Tag="v1.0.0.0",Draft=false})using(var api=new GitHubClient(new(),server.Handler)){
            server.Add("app.zip",Encoding.UTF8.GetBytes("old"));var result=await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);
            check(server.Tag=="v1.0.0.0"&&result.Tag==server.Tag&&server.Creates==0,"same numeric version reuses existing tag spelling without moving or recreating the tag");
            check(server.Events.FindIndex(e=>e.StartsWith("upload:"))<server.Events.FindIndex(e=>e.StartsWith("delete:"))&&server.Events.Last().StartsWith("publish:"),"replacement asset is uploaded and verified before removing old asset and updating release notes");
            check(server.Assets.Count==1&&server.Assets.Values.Single().Name=="app.zip","temporary upload name becomes final filename without leaving duplicate attachments");
        }
        using(var server=new AutomationServer{Exists=true,FailUpload=true})using(var api=new GitHubClient(new(),server.Handler)){
            server.Add("app.zip",Encoding.UTF8.GetBytes("old"));var failed=false;try{await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);}catch(IOException){failed=true;}
            check(failed&&server.Deletes==0&&server.Assets.Values.Single().Name=="app.zip","failed replacement upload preserves existing published installer");
        }
        using(var server=new AutomationServer{Exists=true,CorruptUpload=true})using(var api=new GitHubClient(new(),server.Handler)){
            server.Add("app.zip",Encoding.UTF8.GetBytes("old"));var failed=false;try{await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);}catch(IOException){failed=true;}
            check(failed&&server.Deletes==0&&server.Assets.Values.Any(a=>a.Name=="app.zip"),"remote digest mismatch prevents removal of old installer");
        }
        using(var stop=new CancellationTokenSource())using(var server=new AutomationServer{Exists=true,CancelAfterDelete=stop})using(var api=new GitHubClient(new(),server.Handler)){
            server.Add("app.zip",Encoding.UTF8.GetBytes("old"));var stopped=false;try{await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,stop.Token);}catch(OperationCanceledException){stopped=true;}
            server.CancelAfterDelete=null;var recovered=await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);
            check(stopped&&recovered.Uploaded==1&&server.Uploads==1&&server.Assets.Values.Single().Name=="app.zip","retry after cancellation between old removal and rename recovers verified temporary asset without another upload");
        }
        using(var server=new AutomationServer{Exists=true,Immutable=true})using(var api=new GitHubClient(new(),server.Handler)){
            var before=sourceCalls;var rejected=false;try{await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);}catch(IOException){rejected=true;}
            check(rejected&&sourceCalls==before&&server.Uploads==0,"immutable release is detected before source push or attachment changes");
        }
        using(var server=new AutomationServer{Exists=true,Tag="2.0.0",Draft=false})using(var api=new GitHubClient(new(),server.Handler)){
            var before=sourceCalls;var rejected=false;try{await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);}catch(IOException){rejected=true;}
            check(rejected&&sourceCalls==before&&server.Creates==0,"older local build cannot replace GitHub latest release");
        }
        using(var server=new AutomationServer{Exists=true})using(var api=new GitHubClient(new(),server.Handler)){
            var result=await ProjectAutomationService.Run(config with{ReplaceSameVersion=false},api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);
            check(result.Skipped&&server.Uploads==0&&server.Publishes==0,"optional same-version source-only setting does not modify release assets or notes");
        }
        using(var server=new AutomationServer{FailCreateResponse=true})using(var api=new GitHubClient(new(),server.Handler)){
            var result=await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);
            check(result.ReleaseId==100&&server.Creates==1&&!server.Draft,"uncertain draft creation response is reconciled by current tag without a duplicate release");
        }
        using(var server=new AutomationServer{NoDigest=true})using(var api=new GitHubClient(new(),server.Handler)){
            await ProjectAutomationService.Run(config,api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);
            check(server.Downloads>0,"remote assets without a digest are verified by streamed download rather than matching only file size");
        }
        using(var server=new AutomationServer())using(var api=new GitHubClient(new(),server.Handler)){
            var before=sourceCalls;var rejected=false;try{await ProjectAutomationService.Run(config with{NotesFile="missing.md"},api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);}catch(IOException){rejected=true;}
            check(rejected&&sourceCalls==before&&server.Creates==0,"missing configured release notes fail before source or release mutations");
        }
        check(!Directory.EnumerateDirectories(Path.Combine(root,"stage")).Any(),"completed failed and cancelled flows clean only their temporary local snapshots");
        var badManifest=Path.Combine(release,"SHA256SUMS.txt");await File.WriteAllTextAsync(badManifest,new string('0',64)+"  app.zip\n");using(var server=new AutomationServer())using(var api=new GitHubClient(new(),server.Handler)){var before=sourceCalls;var rejected=false;try{await ProjectAutomationService.Run(config with{AssetPatterns="*.zip;SHA256SUMS.txt"},api,Path.Combine(root,"stage"),Upload,Progress,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&sourceCalls==before&&server.Uploads==0,"mixed or unfinished release build fails checksum manifest verification before source push");}File.Delete(badManifest);
        var gitFolder=Path.Combine(root,"git-source");Directory.CreateDirectory(gitFolder);var git=new GitWorkspace("");await git.Initialize(gitFolder,"owner/repo","main",CancellationToken.None);await git.Run(gitFolder,CancellationToken.None,"config","--local","user.name","Fixture");await git.Run(gitFolder,CancellationToken.None,"config","--local","user.email","fixture@example.invalid");await File.WriteAllTextAsync(Path.Combine(gitFolder,".gitignore"),"ignored.txt\n");await File.WriteAllTextAsync(Path.Combine(gitFolder,"tracked.txt"),"first");await git.Run(gitFolder,CancellationToken.None,"add","--","tracked.txt");await File.WriteAllTextAsync(Path.Combine(gitFolder,"new.txt"),"new");await File.WriteAllTextAsync(Path.Combine(gitFolder,"ignored.txt"),"ignored");var committed=await git.AutoCommit(gitFolder,"owner/repo","main",CancellationToken.None);var tracked=await git.Run(gitFolder,CancellationToken.None,"ls-tree","-r","--name-only","HEAD");
        check(committed.Commit.Length==40&&tracked.Contains("new.txt")&&tracked.Contains("tracked.txt")&&!tracked.Contains("ignored.txt")&&committed.Changes.Count==0,"automatic commit includes staged and unstaged source files while honoring .gitignore");
        var repeated=await git.AutoCommit(gitFolder,"owner/repo","main",CancellationToken.None);check(repeated.Commit==committed.Commit,"unchanged source does not create an empty automatic commit");
        File.Delete(Path.Combine(gitFolder,"tracked.txt"));await git.Run(gitFolder,CancellationToken.None,"mv","new.txt","renamed.txt");var renamed=await git.AutoCommit(gitFolder,"owner/repo","main",CancellationToken.None);var latestTree=await git.Run(gitFolder,CancellationToken.None,"ls-tree","-r","--name-only","HEAD");check(!latestTree.Contains("tracked.txt")&&!latestTree.Contains("new.txt")&&latestTree.Contains("renamed.txt"),"automatic source commit also includes file removals and staged renames");committed=renamed;
        await File.WriteAllTextAsync(Path.Combine(gitFolder,"renamed.txt"),"changed");var wrong=false;try{await git.AutoCommit(gitFolder,"owner/other","main",CancellationToken.None);}catch(IOException){wrong=true;}check(wrong&&(await git.Run(gitFolder,CancellationToken.None,"rev-parse","HEAD"))==committed.Commit,"wrong origin is rejected before staging or committing source");
    }
    private sealed class AutomationServer:IDisposable
    {
        public sealed record Item(long Id,string Name,byte[] Data,string? Digest);
        public readonly Dictionary<long,Item> Assets=[];public readonly List<string> Events=[];public readonly HttpMessageHandler Handler;public bool Exists,Draft=true,Immutable,FailUpload,CorruptUpload,FailCreateResponse,NoDigest;public string Tag="1.0.0.0",Notes="";public int Creates,Uploads,Deletes,Publishes,Downloads;public CancellationTokenSource? CancelAfterDelete;private long next=10;
        public AutomationServer()=>Handler=new FakeHttp(Respond);
        public void Add(string name,byte[] bytes)=>Assets[++next]=new(next,name,bytes,"sha256:"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        private object Asset(Item item)=>new{id=item.Id,name=item.Name,size=item.Data.Length,state="uploaded",digest=NoDigest?null:item.Digest};
        private object Release()=>new{id=100,tag_name=Tag,name=Tag,body=Notes,draft=Draft,prerelease=false,immutable=Immutable,assets=Assets.Values.Select(Asset).ToArray(),target_commitish="original-commit"};
        private static HttpResponseMessage Json(object value,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(JsonSerializer.Serialize(value))};
        private async Task<HttpResponseMessage> Respond(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Get&&path=="/repos/owner/repo")return Json(new{id=1,full_name="owner/repo",@private=false,archived=false,permissions=new{push=true}});
            if(request.Method==HttpMethod.Get&&path.Contains("/commits/"))return Json(new{sha=Sha});
            if(path=="/repos/owner/repo/releases"&&request.Method==HttpMethod.Get)return Json(Exists?new[]{Release()}:Array.Empty<object>());
            if(path=="/repos/owner/repo/releases"&&request.Method==HttpMethod.Post){Creates++;Exists=true;var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Tag=body.RootElement.GetProperty("tag_name").GetString()!;Draft=true;Events.Add("create:"+Tag);if(FailCreateResponse){FailCreateResponse=false;throw new IOException("uncertain response");}return Json(Release(),HttpStatusCode.Created);}
            if(path=="/repos/owner/repo/releases/100/assets"&&request.Method==HttpMethod.Post){Uploads++;if(FailUpload)return Json(new{},HttpStatusCode.BadGateway);var bytes=await request.Content!.ReadAsByteArrayAsync(ct);var query=request.RequestUri.Query.TrimStart('?').Split('&').First(part=>part.StartsWith("name="))[5..];var name=Uri.UnescapeDataString(query);Add(name,bytes);var item=Assets[next];if(CorruptUpload)Assets[next]=item=item with{Digest="sha256:"+new string('0',64)};Events.Add("upload:"+name);return Json(Asset(item),HttpStatusCode.Created);}
            if(path.StartsWith("/repos/owner/repo/releases/assets/")){
                var id=long.Parse(path.Split('/').Last());if(request.Method==HttpMethod.Delete){Deletes++;Events.Add("delete:"+Assets[id].Name);Assets.Remove(id);CancelAfterDelete?.Cancel();return new(HttpStatusCode.NoContent);}
                if(request.Method==HttpMethod.Patch){using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Assets[id]=Assets[id] with{Name=body.RootElement.GetProperty("name").GetString()!};Events.Add("rename:"+Assets[id].Name);return Json(Asset(Assets[id]));}
                if(request.Method==HttpMethod.Get){Downloads++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(Assets[id].Data)};}
            }
            if(path=="/repos/owner/repo/releases/100"){
                if(request.Method==HttpMethod.Patch){using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));if(body.RootElement.TryGetProperty("tag_name",out _))throw new Exception("tag must not move");Notes=body.RootElement.GetProperty("body").GetString()!;Draft=false;Publishes++;Events.Add("publish:"+Tag);}return Json(Release());
            }
            throw new Exception("Unexpected automation request: "+request.Method+" "+request.RequestUri);
        }
        public void Dispose()=>Handler.Dispose();
    }
}
