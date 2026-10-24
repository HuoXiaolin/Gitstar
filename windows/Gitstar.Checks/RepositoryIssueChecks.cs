using Gitstar.Desktop;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

internal static class RepositoryIssueChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var queries=new List<string>();var requests=new List<HttpMethod>();var total=2;var partial=false;
        object Issue(long id,string repo="example/editor",bool pr=false)=>new{id,title="Issue "+id,state=id==2?"closed":"open",updated_at="2026-10-24T08:00:00Z",repository_url="https://api.github.com/repos/"+repo,url="https://api.github.com/repos/"+repo+"/issues/"+id,pull_request=pr?new{url="fixture"}:null};
        object Plain(long id,string repo="example/editor")=>new{id,title="Issue "+id,state=id==2?"closed":"open",updated_at="2026-10-24T08:00:00Z",repository_url="https://api.github.com/repos/"+repo,url="https://api.github.com/repos/"+repo+"/issues/"+id};
        using var handler=new FakeHttp((request,ct)=>{
            requests.Add(request.Method);queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            if(request.RequestUri.AbsolutePath!="/search/issues")throw new IOException("Unexpected request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{total_count=total,incomplete_results=partial,items=new[]{Plain(1),Plain(2),Plain(3,"other/repo"),Issue(4,pr:true)}}))});
        });
        using var api=new GitHubClient(new(),handler){Token="fixture"};
        var page=await api.RepositoryIssues("example",false,1,CancellationToken.None);
        check(page.Items.Count==2&&page.Total==2&&!page.More,"repository Issues view lists distinct issues independently of notification threads");
        check(queries.Last().Contains("is:issue user:example state:open")&&queries.Last().Contains("sort=updated"),"repository Issues query scopes to current owner and open state ordered by update time");
        check(page.Items.All(item=>!item.Unread&&item.BrowserUrl.StartsWith("https://github.com/example/editor/issues/")),"repository Issues provide exact browser links without inventing unread notification state");
        await api.RepositoryIssues("example",true,1,CancellationToken.None);check(!queries.Last().Contains("state:open")&&page.Items.Single(item=>item.Id=="issue:2").ReasonText=="已关闭问题","repository Issues can include closed items separately from notification read status");
        total=51;page=await api.RepositoryIssues("example",false,1,CancellationToken.None);var next=await api.RepositoryIssues("example",false,2,CancellationToken.None);check(page.More&&!next.More&&queries.Last().Contains("page=2"),"repository Issues pagination uses total count and page offset");
        total=1001;partial=true;page=await api.RepositoryIssues("example",true,20,CancellationToken.None);check(!page.More&&page.Incomplete&&page.Total==1001,"repository Issues expose incomplete results and stop at GitHub search limit");
        var before=queries.Count;var rejected=false;try{await api.RepositoryIssues("example is:pr",true,1,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&queries.Count==before,"repository Issues reject account query injection before request");
        check(requests.All(method=>method==HttpMethod.Get),"repository Issues browsing never edits issues or notification read state");
    }
}
