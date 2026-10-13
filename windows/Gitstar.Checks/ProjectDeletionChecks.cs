using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class ProjectDeletionChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var expected=new ManagedProject("owner/repo","",true,"main",true,true,false,DateTimeOffset.UtcNow,Id:10);
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler){Token="fixture"}){await api.DeleteRepository(expected,"owner/repo",CancellationToken.None);check(server.Deleted&&server.Deletes==1&&server.Reads==2,"repository deletion rechecks identity and verifies absence after one exact repository DELETE");}
        foreach(var mode in new[]{"confirmation","identity","admin","scope","renamed"}){
            using var server=new Server{Id=mode=="identity"?11:10,Admin=mode!="admin",Scopes=mode=="scope"?"repo, user":"repo, delete_repo",Name=mode=="renamed"?"owner/renamed":"owner/repo"};using var api=new GitHubClient(new(),server.Handler){Token="fixture"};var blocked=false;
            try{await api.DeleteRepository(expected,mode=="confirmation"?"other/repo":"owner/repo",CancellationToken.None);}catch(IOException){blocked=true;}
            check(blocked&&server.Deletes==0,"repository deletion blocks changed or unconfirmed target before mutation: "+mode);
        }
        using(var server=new Server{Retain=true})using(var api=new GitHubClient(new(),server.Handler){Token="fixture"}){var pending=false;try{await api.DeleteRepository(expected,"owner/repo",CancellationToken.None);}catch(IOException){pending=true;}check(pending&&server.Deletes==1,"unverified deletion is not reported as completed or silently retried");}
        using(var server=new Server{Denied=true})using(var api=new GitHubClient(new(),server.Handler){Token="fixture"}){var rejected=false;try{await api.DeleteRepository(expected,"owner/repo",CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&server.Deletes==1&&!server.Deleted,"server policy denial retains local project state for error handling");}
        check(RepositoryChecks.Targets(true,[],new Settings{Bindings=[new(){AppId="app",Repository="owner/repo",Disabled=true}]}).Length==0,"paused software association no longer checks a deleted repository");
    }
    private sealed class Server:IDisposable
    {
        public long Id=10;public bool Admin=true,Deleted,Retain,Denied;public string Name="owner/repo",Scopes="repo, user, delete_repo";public int Reads,Deletes;public HttpMessageHandler Handler;
        public Server()=>Handler=new FakeHttp((request,ct)=>{
            if(request.RequestUri!.AbsolutePath!="/repos/owner/repo")throw new IOException("unexpected repository path");HttpResponseMessage result;
            if(request.Method==HttpMethod.Delete){Deletes++;if(Denied)result=new(HttpStatusCode.Forbidden){Content=new StringContent("{}")};else{Deleted=true;result=new(HttpStatusCode.NoContent);}}
            else if(request.Method==HttpMethod.Get){Reads++;result=Deleted&&!Retain?new(HttpStatusCode.NotFound){Content=new StringContent("{}")}:new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{id=Id,full_name=Name,@private=true,default_branch="main",permissions=new{push=true,admin=Admin},archived=false}))};}
            else throw new IOException("unexpected repository method");result.Headers.Add("X-OAuth-Scopes",Scopes);return Task.FromResult(result);
        });
        public void Dispose()=>Handler.Dispose();
    }
}
