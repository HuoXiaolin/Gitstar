using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Desktop;

static class ReleaseManagementChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);var selected=before.Assets.Single(asset=>asset.Id==10);var after=await api.DeleteReleaseAsset(before,selected,CancellationToken.None);
            check(selected.CreatedAt==DateTimeOffset.Parse("2026-10-12T20:25:30Z")&&selected.CreatedAt!=DateTimeOffset.Parse(selected.Updated),"asset upload time uses its own created_at rather than release publication or asset update time");
            check(before.Assets.Select(asset=>asset.CreatedAt).Distinct().Count()==before.Assets.Count,"every release attachment retains its individual upload time");
            var legacy=JsonSerializer.Deserialize<ManagedReleaseAsset>("{\"Id\":1,\"Name\":\"old.exe\",\"Size\":1,\"Digest\":\"\",\"Updated\":\"2026-10-13T00:00:00Z\",\"Url\":\"\"}")!;check(legacy.CreatedAt==null&&Gitstar.Core.ProjectTimeRules.Time(legacy.CreatedAt)=="尚未获取","missing upload metadata remains unknown instead of borrowing updated_at");
            check(after.Version.AssetCount==1&&after.Assets.Single().Id==20&&after.Version.Tag==before.Version.Tag&&server.Exists,"individual asset deletion preserves release tag and unselected installer");
            check(server.Writes.SequenceEqual(new[]{"/repos/owner/repo/releases/assets/10"}),"asset deletion sends only selected asset ID to remote delete endpoint");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);await api.DeleteProjectRelease(before,CancellationToken.None);
            check(!server.Exists&&server.Writes.SequenceEqual(new[]{"/repos/owner/repo/releases/9"}),"whole release deletion uses release endpoint without deleting Git tag or repository");
        }
        using(var server=new Server{Immutable=true})using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);var blocked=false;try{await api.DeleteReleaseAsset(before,before.Assets[0],CancellationToken.None);}catch(IOException){blocked=true;}check(blocked&&server.Writes.Count==0,"immutable release prevents deleting an individual asset");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);server.Push=false;var blocked=false;try{await api.DeleteProjectRelease(before,CancellationToken.None);}catch(IOException){blocked=true;}check(blocked&&server.Writes.Count==0,"delete rechecks live permission and does not trust cached writable state");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);server.Names[10]="changed.exe";var blocked=false;try{await api.DeleteReleaseAsset(before,before.Assets[0],CancellationToken.None);}catch(IOException){blocked=true;}check(blocked&&server.Writes.Count==0,"changed attachment is retained rather than deleting using stale confirmation");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);server.Names[30]="new.apk";var blocked=false;try{await api.DeleteProjectRelease(before,CancellationToken.None);}catch(IOException){blocked=true;}check(blocked&&server.Writes.Count==0,"new attachment added after confirmation prevents whole release deletion");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);server.ProjectId=101;var blocked=false;try{await api.DeleteProjectRelease(before,CancellationToken.None);}catch(IOException){blocked=true;}check(blocked&&server.Writes.Count==0,"same-name recreated repository cannot be deleted using previous release snapshot");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);server.Names.Remove(10);var after=await api.DeleteReleaseAsset(before,before.Assets[0],CancellationToken.None);check(after.Assets.Count==1&&server.Writes.Count==0,"already removed asset reconciles as absent without another destructive request");
        }
        using(var server=new Server{IgnoreDelete=true})using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);var unconfirmed=false;try{await api.DeleteProjectRelease(before,CancellationToken.None);}catch(IOException){unconfirmed=true;}check(unconfirmed&&server.Exists,"successful HTTP delete response is not accepted until remote release absence is verified");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            var before=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);var blocked=false;try{await api.DeleteReleaseAsset(before,new(99,"unrelated.exe",100,"","",""),CancellationToken.None);}catch(IOException){blocked=true;}check(blocked&&server.Writes.Count==0,"asset not included in selected release snapshot is rejected");
        }
        using(var server=new Server())using(var api=new GitHubClient(new(),server.Handler)){
            for(var id=1;id<=101;id++)server.Names[id]="asset-"+id+".exe";var inventory=await api.ReleaseInventory("owner/repo",9,CancellationToken.None);check(inventory.Assets.Count==101&&server.AssetPages==2,"asset management reads all pages rather than only embedded release asset summary");
        }
    }
    private sealed class Server:IDisposable
    {
        public readonly Dictionary<long,string> Names=new(){{10,"Gitstar.exe"},{20,"Gitstar-setup.exe"}};public readonly List<string> Writes=[];public readonly HttpMessageHandler Handler;public bool Exists=true,Push=true,Immutable,IgnoreDelete;public long ProjectId=100;public int AssetPages;
        public Server()=>Handler=new FakeHttp(Respond);
        private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
        private object[] Assets()=>Names.Select(item=>(object)new{id=item.Key,name=item.Value,size=100L,digest="sha256:fixture",created_at=DateTimeOffset.Parse("2026-10-12T20:15:30Z").AddMinutes(item.Key).ToString("O"),updated_at="2026-10-13T00:00:00Z",browser_download_url="https://github.com/owner/repo/releases/download/1.0.0.0/"+item.Value}).ToArray();
        private Task<HttpResponseMessage> Respond(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Delete){Writes.Add(path);if(!IgnoreDelete){if(path=="/repos/owner/repo/releases/9"){Exists=false;Names.Clear();}else if(path.StartsWith("/repos/owner/repo/releases/assets/"))Names.Remove(long.Parse(path.Split('/').Last()));else throw new Exception("Unexpected destructive endpoint");}return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));}
            if(request.Method!=HttpMethod.Get)throw new Exception("Unexpected management mutation");
            if(path=="/repos/owner/repo")return Task.FromResult(Json(new{id=ProjectId,full_name="owner/repo",@private=false,archived=false,permissions=new{push=Push,admin=Push}}));
            if(!Exists)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound){Content=new StringContent("{}")});
            if(path=="/repos/owner/repo/releases/9")return Task.FromResult(Json(new{id=9,tag_name="1.0.0.0",name="1.0.0.0",body="release notes",draft=false,prerelease=false,immutable=Immutable,created_at="2026-10-13T00:00:00Z",published_at="2026-10-13T00:00:00Z",updated_at="2026-10-13T00:00:00Z",assets=Assets()}));
            if(path=="/repos/owner/repo/releases/9/assets"){AssetPages++;var page=int.Parse(request.RequestUri.Query.Split("page=").Last());return Task.FromResult(Json(Assets().Skip((page-1)*100).Take(100).ToArray()));}
            throw new Exception("Unexpected management request: "+path);
        }
        public void Dispose()=>Handler.Dispose();
    }
}
