using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
internal static class RepositorySearchChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var urls=new List<string>();var methods=new List<HttpMethod>();
        object Repo(string name)=>new{full_name=name,description="Editor",@private=false,stargazers_count=20};
        using var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{urls.Add(request.RequestUri!.ToString());methods.Add(request.Method);object body=request.RequestUri.AbsolutePath.StartsWith("/search/")?new{total_count=45,incomplete_results=true,items=new[]{Repo("example/editor"),Repo("example/editor"),Repo("unsafe repo")}}:Repo("example/editor");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(body))});}));
        var page=await api.SearchRepositories("editor app",CancellationToken.None);check(page.Items.Count==1&&page.Total==45&&page.Incomplete,"repository search deduplicates validated candidates and reports incomplete total");
        check(Uri.UnescapeDataString(urls.Last()).Contains("\"editor\" \"app\" in:name,description")&&urls.Last().Contains("per_page=30"),"keyword search uses encoded best-match repository query with bounded results");
        page=await api.SearchRepositories("https://github.com/example/editor",CancellationToken.None);check(page.Items.Single().FullName=="example/editor"&&urls.Last().Contains("/repos/example/editor"),"full repository URL resolves directly without requiring keyword entry");
        page = await api.SearchRepositories("https://github.com/example/editor/releases/latest", CancellationToken.None);
        check(page.Items.Single().FullName == "example/editor" && new Uri(urls.Last()).AbsolutePath == "/repos/example/editor",
            "a pasted release URL resolves to its repository");
        var before=urls.Count;var rejected=false;try{await api.SearchRepositories("x",CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&urls.Count==before,"short online query is rejected before spending search quota");
        check(methods.All(method=>method==HttpMethod.Get),"repository selection search never creates stars or modifies remote data");

        var repos = new Repository[]
        {
            new("example/wenxi-notes", "笔记示例", false),
            new("example/magpie", "缩放工具", false),
            new("z7786/wenxi", "文析助手，一个跨平台的网盘管理工具", false)
        };
        check(RepositoryLookup.Filter(repos, "wenxi", 8).Select(repo => repo.FullName).SequenceEqual(new[] { "z7786/wenxi", "example/wenxi-notes" }),
            "a repository keyword filters unrelated repositories and ranks the exact name first");
        check(RepositoryLookup.Filter(repos, "文析助手", 8).Single() == repos[2] && RepositoryLookup.IsProjectMention(repos[2], "文析助手")
            && !RepositoryLookup.IsProjectMention(repos[2] with { Description = "文析助手插件，可选扩展" }, "文析助手"),
            "Chinese description matches can suggest the named project without treating related plug-ins as the same app");
        check(RepositoryLookup.Address("https://github.com.attacker.invalid/z7786/wenxi") == ""
            && RepositoryLookup.Filter(repos, "https://github.com/z7786/wenxi/releases/latest", 8).Single() == repos[2]
            && RepositoryLookup.Address("https://github.com/example/example.github.io.git") == "example/example.github.io",
            "repository paste handling normalizes GitHub subpages without accepting a lookalike host");
    }
}
