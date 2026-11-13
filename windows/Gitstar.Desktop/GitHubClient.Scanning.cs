using System.Text.RegularExpressions;
namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    private readonly object releaseCacheSync=new();
    private ReleasePage? ReadScanPage(string key){lock(releaseCacheSync)return ReadReleasePage?.Invoke(key);}
    private void SaveScanPage(string key,ReleasePage page){lock(releaseCacheSync)SaveReleasePage?.Invoke(key,page);}
    internal static int? LastReleasePage(HttpResponseMessage response)
    {
        if(!response.Headers.TryGetValues("Link",out var links))return null;
        foreach(var link in links.SelectMany(value=>value.Split(','))){
            if(!Regex.IsMatch(link,@";\s*rel=""last""",RegexOptions.IgnoreCase))continue;
            var match=Regex.Match(link,@"<([^>]+)>");
            if(!Uri.TryCreate(match.Groups[1].Value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="api.github.com")continue;
            var page=Regex.Match(uri.Query,@"(?:\?|&)page=(\d+)(?:&|$)");
            if(int.TryParse(page.Groups[1].Value,out var value)&&value>0&&value<=100000)return value;
        }
        return null;
    }
    public async Task<List<Release>> Releases(string repo,CancellationToken ct,IEnumerable<Binding>? bindings=null)
    {
        Validate(repo);var subscriptions=bindings?.ToArray()??[];
        const int pageSize=100;
        var signature="version-selection-v3-100\n"+string.Join("\n",subscriptions.Where(b=>b.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase)).Select(b=>b.TagPrefix).Append("").Distinct().Order(StringComparer.Ordinal));
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);var token=lifetime.Token;
        var selected=new ReleaseCandidates(repo,subscriptions);var sync=new object();
        async Task<ReleasePage> Page(int number)
        {
            try{
                var key=$"{repo.ToLowerInvariant()}/{number}";var cached=ReadScanPage(key);if(cached?.Signature!=signature)cached=null;
                var count=0;var unchanged=false;var etag="";int? last=null;var candidates=new ReleaseCandidates(repo,subscriptions);
                await Request($"repos/{repo}/releases?per_page={pageSize}&page={number}",ct:token,background:true,etag:cached?.ETag,headers:response=>last=LastReleasePage(response),cacheResult:(tag,same)=>{etag=tag;unchanged=same;},consume:data=>{
                    count=data.GetArrayLength();foreach(var value in data.EnumerateArray())if(!value.GetProperty("draft").GetBoolean()){
                        var tag=S(value,"tag_name");var beta=value.GetProperty("prerelease").GetBoolean();var published=DateTimeOffset.TryParse(S(value,"published_at"),out var date)?date:DateTimeOffset.MinValue;
                        if(candidates.Needs(tag,beta,published))candidates.Add(ParseRelease(repo,value));
                    }
                });
                var result=unchanged?cached! with{LastPage=last??cached!.LastPage}:new ReleasePage(etag,signature,count,candidates.Values){LastPage=last};
                SaveScanPage(key,result);lock(sync)foreach(var release in result.Candidates)selected.Add(release);return result;
            }catch{await lifetime.CancelAsync();throw;}
        }
        async Task Latest(){try{var latest=await LatestStableRelease(repo,signature,token);if(latest!=null)lock(sync)selected.Add(latest);}catch{await lifetime.CancelAsync();throw;}}
        async Task Pages()
        {
            var first=await Page(1);var end=first.LastPage??1;var tail=first;
            if(end>1){var numbers=Enumerable.Range(2,end-1).Select(n=>n.ToString()).ToArray();await RepositoryChecks.Run(numbers,async(n,_)=>{var result=await Page(int.Parse(n));if(int.Parse(n)==end)tail=result;},token,3);}
            // Missing Link (including some 304 responses) must not truncate history.
            // A full final page is probed once more to catch additions beyond a cached boundary.
            if(tail.Count>=pageSize)for(var number=end+1;;number++){var page=await Page(number);if(page.Count<pageSize)break;}
        }
        await Task.WhenAll(Latest(),Pages());return selected.Values;
    }
}
