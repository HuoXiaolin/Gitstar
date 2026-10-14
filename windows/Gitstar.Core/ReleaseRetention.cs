namespace Gitstar.Core;

public static class ReleaseRetention
{
    // Keep exactly the candidates needed by the feed's three channels and each installed-app release line.
    public static List<Release> Compact(IEnumerable<Release> source,IEnumerable<Binding> bindings)
    {
        var subscriptions=bindings.ToArray();var result=new List<Release>();
        foreach(var repo in source.GroupBy(r=>r.Repository,StringComparer.OrdinalIgnoreCase)){
            var candidates=new ReleaseCandidates(repo.Key,subscriptions);foreach(var release in repo)candidates.Add(release);result.AddRange(candidates.Values);
        }
        return result;
    }
}

public sealed class ReleaseCandidates
{
    private readonly string[] prefixes;
    private readonly Dictionary<(string,bool),Release> candidates=[];
    public ReleaseCandidates(string repo,IEnumerable<Binding> bindings)=>prefixes=bindings.Where(b=>b.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase)).Select(b=>b.TagPrefix).Append("").Distinct(StringComparer.Ordinal).ToArray();
    public bool Needs(string tag,bool beta,DateTimeOffset published)=>prefixes.Any(prefix=>tag.StartsWith(prefix,StringComparison.Ordinal) && (!candidates.TryGetValue((prefix,beta),out var old)||ReleaseSelection.Preferred(tag,beta,published,false,old,prefix)));
    public void Add(Release release){foreach(var prefix in prefixes)if(release.Tag.StartsWith(prefix,StringComparison.Ordinal) && (!candidates.TryGetValue((prefix,release.Prerelease),out var old)||ReleaseSelection.Preferred(release.Tag,release.Prerelease,release.Published,release.IsLatest,old,prefix)))candidates[(prefix,release.Prerelease)]=release;}
    public List<Release> Values=>candidates.Values.DistinctBy(r=>r.Id).ToList();
}
