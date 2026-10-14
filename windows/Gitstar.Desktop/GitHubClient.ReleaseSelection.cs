namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    private async Task<Release?> LatestStableRelease(string repo,string signature,CancellationToken ct)
    {
        var key=repo.ToLowerInvariant()+"/latest";var cached=ReadReleasePage?.Invoke(key);
        if(cached?.Signature!=signature)cached=null;
        var unchanged=false;var nextTag="";JsonElement data;
        try{data=await Request($"repos/{repo}/releases/latest",ct:ct,etag:cached?.ETag,cacheResult:(tag,same)=>{nextTag=tag;unchanged=same;});}
        catch(GitHubNotFoundException){SaveReleasePage?.Invoke(key,new("",signature,0,[]));return null;}
        if(unchanged)return cached!.Candidates.SingleOrDefault();
        if(data.GetProperty("draft").GetBoolean()||data.GetProperty("prerelease").GetBoolean())throw new IOException("GitHub Latest 未指向已发布正式版，请稍后重试。");
        var release=ParseRelease(repo,data) with{IsLatest=true};SaveReleasePage?.Invoke(key,new(nextTag,signature,1,[release]));return release;
    }
}
