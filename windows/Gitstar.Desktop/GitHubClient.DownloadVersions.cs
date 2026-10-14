namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    public sealed record DownloadVersionPage(int Count,List<Release> Versions);
    public async Task<DownloadVersionPage> DownloadVersions(string repo,int page,CancellationToken ct)
    {
        Validate(repo);if(page<1)throw new ArgumentOutOfRangeException(nameof(page));
        var count=0;List<Release> versions=[];
        await Request($"repos/{repo}/releases?per_page=30&page={page}",ct:ct,fresh:true,consume:data=>{
            count=data.GetArrayLength();foreach(var item in data.EnumerateArray())if(!item.GetProperty("draft").GetBoolean())versions.Add(ParseRelease(repo,item,false));
        });return new(count,versions);
    }
    public async Task<Release?> LatestDownloadVersion(string repo,CancellationToken ct)
    {
        Validate(repo);try{var item=await Request($"repos/{repo}/releases/latest",ct:ct,fresh:true);if(item.GetProperty("draft").GetBoolean()||item.GetProperty("prerelease").GetBoolean())throw new IOException("GitHub Latest 未指向已发布正式版，请稍后重试。");return ParseRelease(repo,item,false) with{IsLatest=true};}
        catch(GitHubNotFoundException){return null;}
    }
    public async Task<Release?> DownloadVersionById(string repo,long id,CancellationToken ct)
    {
        Validate(repo);if(id<=0)throw new ArgumentOutOfRangeException(nameof(id));
        try{var item=await Request($"repos/{repo}/releases/{id}",ct:ct,fresh:true);return item.GetProperty("draft").GetBoolean()?null:ParseRelease(repo,item,false);}
        catch(GitHubNotFoundException){return null;}
    }
}
