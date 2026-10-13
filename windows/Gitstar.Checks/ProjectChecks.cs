using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class ProjectChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var changes=ProjectRules.ParseStatus(" M src/含 空格.cs\0?? line\nbreak.txt\0R  new.txt\0old.txt\0C  copy.txt\0source.txt\0");
        check(changes.Count==4 && changes[0].Path=="src/含 空格.cs" && changes[1].Path=="line\nbreak.txt","porcelain NUL parsing preserves Unicode spaces and newline filenames");
        check(changes[2].Paths.SequenceEqual(new[]{"new.txt","old.txt"}) && changes[3].Original=="source.txt","renames and copies include both paths for selected commits");
        var malformed=false;try{ProjectRules.ParseStatus("R  missing.txt\0");}catch(IOException){malformed=true;}check(malformed,"incomplete rename status is rejected");
        check(ProjectRules.MatchesRemote("git@github.com:owner/app.git","owner/app") && !ProjectRules.MatchesRemote("https://github.com/owner/other.git","owner/app") && !ProjectRules.MatchesRemote("https://github.com.evil/owner/app.git","owner/app"),"workspace binding requires exact GitHub remote repository");
        object Repo(int i,bool push=true,bool admin=true)=>new{full_name="owner/project"+i,description="中文项目",@private=i%2==0,default_branch="main",permissions=new{push,admin},archived=false,updated_at="2026-10-12T00:00:00Z",homepage="https://example.org"};
        HttpResponseMessage Json(object o)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(o))};
        var pages=0;
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            if(request.Headers.Authorization?.Parameter!="fixture-project-token")throw new Exception("Missing project authentication");
            pages++;if(!request.RequestUri!.Query.Contains("affiliation=owner,collaborator,organization_member"))throw new Exception("Missing affiliations");
            return Task.FromResult(Json(request.RequestUri.Query.EndsWith("&page=1")?Enumerable.Range(0,100).Select(i=>Repo(i)).ToArray():new[]{Repo(100,false,false)}));
        })){Token="fixture-project-token"}){
            var projects=await api.MyProjects(CancellationToken.None);check(pages==2 && projects.Count==101,"project sync pages through owned collaborative and organization repositories");
            check(projects[0].Private && projects[0].CanAdmin && !projects[^1].CanPush && projects[0].DefaultBranch=="main","project privacy permissions and default branch preserved");
        }
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            var body=await request.Content!.ReadAsStringAsync(ct);using var doc=JsonDocument.Parse(body);var b=doc.RootElement;
            if(request.Method==HttpMethod.Post){check(b.GetProperty("private").GetBoolean() && !b.GetProperty("auto_init").GetBoolean() && b.GetProperty("description").GetString()=="my description","project creation sends selected visibility description and initialization options");}
            else{check(request.Method==HttpMethod.Patch && b.GetProperty("homepage").GetString()=="https://example.org" && !b.TryGetProperty("private",out _) && !b.TryGetProperty("name",out _),"metadata edit does not change repository visibility or identity");}
            return Json(Repo(0));
        }))){await api.CreateProject("project0","my description",true,false,CancellationToken.None);await api.EditProject("owner/project0","changed","https://example.org",CancellationToken.None);}
        object Version(bool draft)=>new{id=7L,tag_name="v2.0-beta.1",name="Version 2",body="release notes",draft,prerelease=true,published_at=(string?)null,created_at="2026-10-12T00:00:00Z",html_url="https://github.com/owner/project0/releases",assets=new[]{new{name="app.zip"}}};
        var patched=0;
        using(var api=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            if(request.Method==HttpMethod.Patch){patched++;using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));check(doc.RootElement.GetProperty("body").GetString()=="edited notes" && !doc.RootElement.TryGetProperty("draft",out _),"draft editing keeps draft state while changing release notes");return Json(Version(true));}
            return Json(request.RequestUri!.AbsolutePath.EndsWith("/7")?Version(true):new[]{Version(true)});
        }))){var history=await api.ProjectVersions("owner/project0",CancellationToken.None);check(history.Single().Draft && history[0].AssetCount==1 && history[0].Updated>DateTimeOffset.MinValue,"release management includes draft prerelease assets and creation time");await api.EditDraft("owner/project0",7,"Version 2","edited notes",true,CancellationToken.None);check(patched==1,"draft update first verifies current remote draft state");}
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{if(request.Method!=HttpMethod.Get)throw new Exception("Published release should not be mutated");return Task.FromResult(Json(Version(false)));}))){var blocked=false;try{await api.EditDraft("owner/project0",7,"name","notes",false,CancellationToken.None);}catch(IOException){blocked=true;}check(blocked,"published versions cannot be modified through draft editor");}
        var folder=Path.Combine(root,"project-workspace");Directory.CreateDirectory(folder);var git=new GitWorkspace("");await git.Initialize(folder,"owner/project0","main",CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(folder,"含 空格.txt"),"new source");var state=await git.Inspect(folder,CancellationToken.None);
        check(state.Branch=="main" && state.Commit=="" && ProjectRules.MatchesRemote(state.Remote,"owner/project0") && state.Changes.Single().Path=="含 空格.txt","real local Git initialization and inspection handle unborn branch and Unicode source");
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);var previousOutput=Console.OutputEncoding;
        try{Console.OutputEncoding=System.Text.Encoding.GetEncoding(936);state=await git.Inspect(folder,CancellationToken.None);}finally{Console.OutputEncoding=previousOutput;}
        check(state.Changes.Single().Path=="含 空格.txt","Git filenames decode as UTF8 even when Windows console codepage is 936");
        check(new GitChange("??","新增中文.md").Label=="新增  ·  新增中文.md" && new GitChange(" M","修改.cs").StatusName=="修改" && new GitChange(" D","删除.cs").StatusName=="删除","Git status markers are displayed using clear Chinese labels");
        var nested=Path.Combine(folder,"nested");Directory.CreateDirectory(nested);var prevented=false;try{await git.Initialize(nested,"owner/other","main",CancellationToken.None);}catch(IOException){prevented=true;}check(prevented && !Directory.Exists(Path.Combine(nested,".git")),"initialization refuses to create nested repository inside existing project");
        await git.Run(folder,CancellationToken.None,"config","--local","user.name","Fixture");await git.Run(folder,CancellationToken.None,"config","--local","user.email","fixture@example.invalid");await git.Run(folder,CancellationToken.None,"add","--","含 空格.txt");await git.Run(folder,CancellationToken.None,"commit","-m","initial fixture");await git.Run(folder,CancellationToken.None,"mv","--","含 空格.txt","renamed.txt");state=await git.Inspect(folder,CancellationToken.None);
        check(state.Commit.Length==40 && state.Changes.Single().Original=="含 空格.txt" && state.Changes[0].Path=="renamed.txt","real Git rename inspection keeps old and new filenames");
        var wrongRoot=false;try{await git.Inspect(nested,CancellationToken.None);}catch(IOException){wrongRoot=true;}check(wrongRoot,"local association requires repository root rather than nested folder");
        using(var db=new LocalStore(Path.Combine(root,"project-cache"))){db.Set("cache/user-a/my-projects",new[]{new ManagedProject("owner/project0","description",true,"main",true,true,false,DateTimeOffset.UtcNow)});db.Set("cache/user-a/my-projects/workspaces",new[]{new ProjectWorkspace("owner/project0",folder,"main")});}
        using(var db=new LocalStore(Path.Combine(root,"project-cache"))){check(db.Get("cache/user-a/my-projects",Array.Empty<ManagedProject>()).Single().Private && db.Get("cache/user-a/my-projects/workspaces",Array.Empty<ProjectWorkspace>()).Single().Folder==folder,"project list and workspace associations survive reopening local store");check(db.Get("cache/user-b/my-projects",Array.Empty<ManagedProject>()).Length==0,"project cache is separated by GitHub account");}
    }
}
