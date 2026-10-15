using System.Net;
using System.Text;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed class FollowSmokeTransport:HttpMessageHandler
    {
        public bool Starred=true,RejectUnstar;public int Writes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;var code=HttpStatusCode.OK;var body="{}";
            if(path=="/repos/example/editor")body="{\"full_name\":\"example/editor\",\"private\":false,\"node_id\":\"REPO_fixture\"}";
            else if(path=="/user/starred/example/editor"){
                if(request.Method==HttpMethod.Put){Starred=true;Writes++;code=HttpStatusCode.NoContent;}
                else if(request.Method==HttpMethod.Delete){Writes++;if(RejectUnstar){code=HttpStatusCode.Forbidden;body="{\"message\":\"denied fixture\"}";}else{Starred=false;code=HttpStatusCode.NoContent;}}
                else code=Starred?HttpStatusCode.NoContent:HttpStatusCode.NotFound;
            }else if(path=="/graphql"){code=HttpStatusCode.Forbidden;body="{\"message\":\"list permission denied fixture\"}";}
            else throw new IOException("Unexpected fixture request: "+request.Method+" "+path);
            var response=new HttpResponseMessage(code){Content=new StringContent(body,Encoding.UTF8,"application/json")};response.Headers.Add("X-OAuth-Scopes","user");return Task.FromResult(response);
        }
    }
    private async Task FollowManagementSmoke(string path)
    {
        settings.Account="example";settings.LocalGroups=[new("local-tools","效率工具","本地",["example/editor"])];groups=[];repositories=[new("example/editor","",false,true,"REPO_fixture")];settings.ExtraRepositories=[];
        api.Dispose();var transport=new FollowSmokeTransport();api=new(settings,transport);api.Token="offline-fixture";
        await SetManagedStar("example/editor",false,CancellationToken.None);PruneUnfollowed("example/editor");Save();SaveCache();var entry=settings.FollowHistory.Single();var recorded=entry.Kind==FollowKind.GitHubStar&&entry.Groups.Single().Id=="local-tools"&&!transport.Starred;
        var writes=transport.Writes;await RestoreFollowEntry(entry,FollowKind.Local,CancellationToken.None);var localOnly=transport.Writes==writes&&settings.ExtraRepositories.Contains("example/editor")&&settings.LocalGroups[0].Repositories.Contains("example/editor");
        var cloudEntry=entry with{Groups=entry.Groups.Append(new("missing-cloud","原 GitHub 分组","GitHub")).ToList()};
        await RestoreFollowEntry(cloudEntry,FollowKind.GitHubStar,CancellationToken.None);var restoredStar=transport.Starred&&repositories.Single().Starred&&settings.FollowHistory.Single().RestoredAs==FollowKind.GitHubStar;var groupFailurePreserved=restoredStar&&settings.FollowHistory.Single().Note.Contains("分组待核对");
        transport.RejectUnstar=true;var previous=settings.FollowHistory.Count;try{await SetManagedStar("example/editor",false,CancellationToken.None);}catch(IOException){}var failureSafe=settings.FollowHistory.Count==previous&&transport.Starred;
        transport.RejectUnstar=false;SetLocalFollow("example/editor",false);var localRecorded=settings.FollowHistory.Any(e=>e.Kind==FollowKind.Local)&&repositories.Single().Starred;
        Save();using(var reopened=new LocalStore(store.Root)){var saved=reopened.Get("settings",new Settings());recorded&=saved.FollowHistory.Count==2;}
        settings.HiddenRepositories.Add("example/legacy");groups=[new("cloud","开发项目","GitHub",["example/editor"])];
        var report=new{ConfirmedUnstarRecorded=recorded,LocalRestoreDoesNotWriteGitHub=localOnly,StarRestoreConfirmed=restoredStar,GroupFailurePreservesConfirmedStar=groupFailurePreserved,FailedUnstarDoesNotRecord=failureSafe,LocalCancellationPreservesStar=localRecorded,LegacyTimeUnknown=FollowHistory.Entries(settings).Any(e=>e.Kind==FollowKind.Unknown&&e.RemovedAt==null)};
        File.WriteAllText(Path.ChangeExtension(path,".follow-management.json"),JsonSerializer.Serialize(report));
        if(!recorded||!localOnly||!restoredStar||!groupFailurePreserved||!failureSafe||!localRecorded)throw new IOException("Follow management lifecycle regression");
        if(Environment.GetCommandLineArgs().Contains("--follow-history"))FollowHistoryDialog(previewPath:path);else if(Environment.GetCommandLineArgs().Contains("--follow-groups"))ManageFollowGroups(this,()=>{},path);else FollowManagementDialog("example/editor",path);
    }
}
