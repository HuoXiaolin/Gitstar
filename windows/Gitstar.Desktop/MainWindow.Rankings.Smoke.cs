using System.Net;
namespace Gitstar.Desktop;
public partial class MainWindow
{
    private sealed class RankingSmokeTransport:HttpMessageHandler
    {
        public bool Starred;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var path=request.RequestUri!.AbsolutePath;
            object Repo(string description)=>new{full_name="example/desk-tools",description,@private=false,node_id="fixture-node",stargazers_count=25431,language="C#",created_at="2026-10-01T00:00:00Z"};
            if(path=="/repos/example/desk-tools")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(Repo("A desktop tools project")))});
            if(path=="/user/starred/example/desk-tools"){if(request.Method==HttpMethod.Put)Starred=true;return Task.FromResult(new HttpResponseMessage(Starred?HttpStatusCode.NoContent:HttpStatusCode.NotFound));}
            if(path=="/search/repositories")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{total_count=1,incomplete_results=false,items=new[]{Repo("Search result project introduction")}}))});
            throw new IOException("Unexpected ranking fixture request: "+path);
        }
    }
}
