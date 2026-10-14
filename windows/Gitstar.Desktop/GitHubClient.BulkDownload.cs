namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    public async Task<List<Release>> ReleasesInPeriod(string repo,DateTimeOffset from,DateTimeOffset through,CancellationToken ct)
    {
        Validate(repo);var result=new List<Release>();
        // A release can be published long after it was created; don't stop at an old page.
        for(var page=1;;page++){
            ct.ThrowIfCancellationRequested();var count=0;
            await Request($"repos/{repo}/releases?per_page=30&page={page}",ct:ct,consume:data=>{
                count=data.GetArrayLength();foreach(var item in data.EnumerateArray())if(!item.GetProperty("draft").GetBoolean()&&DateTimeOffset.TryParse(S(item,"published_at"),out var published)&&published>=from&&published<=through){
                    // Bulk plans need package metadata, not full release-note bodies.
                    var assets=item.GetProperty("assets").EnumerateArray().Select(a=>new Asset(a.GetProperty("id").GetInt64(),S(a,"name"),a.GetProperty("size").GetInt64(),S(a,"browser_download_url"),S(a,"digest"))).ToList();
                    result.Add(new(item.GetProperty("id").GetInt64(),repo,S(item,"tag_name"),S(item,"name"),"",item.GetProperty("prerelease").GetBoolean(),published,S(item,"html_url"),assets));
                }
            });
            if(count<30)return result;
        }
    }
}
