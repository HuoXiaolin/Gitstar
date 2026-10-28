namespace Gitstar.Desktop;
public sealed partial class GitHubClient
{
    public sealed record RepositorySearchPage(List<Repository> Items,int Total,bool Incomplete);
    public async Task<RepositorySearchPage> SearchRepositories(string text,CancellationToken ct)
    {
        text=text.Trim();if(text.Length<2||text.Length>200)throw new IOException("请输入 2 至 200 个字符的仓库名称或关键词。");
        var direct=Rules.RepositoryName(text);if(Rules.ValidRepository(direct))return new([await Repo(direct,ct)],1,false);
        var terms=string.Join(" ",text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(term=>"\""+term.Replace("\"","")+"\""));
        var data=await Request("search/repositories?q="+Uri.EscapeDataString(terms+" in:name,description")+"&per_page=30&page=1",ct:ct);
        var items=data.GetProperty("items").EnumerateArray().Where(item=>Rules.ValidRepository(S(item,"full_name"))).Select(item=>ParseRepository(item,false)).DistinctBy(item=>item.FullName,StringComparer.OrdinalIgnoreCase).ToList();
        return new(items,data.GetProperty("total_count").GetInt32(),data.TryGetProperty("incomplete_results",out var incomplete)&&incomplete.ValueKind==JsonValueKind.True);
    }
}
