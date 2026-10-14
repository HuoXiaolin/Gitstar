namespace Gitstar.Core;

public static class DownloadVersionRules
{
    public static Release? Default(IEnumerable<Release> source,int channel=1)=>Rules.SelectRelease(source.Where(release=>channel==1||(channel==2?release.Prerelease:!release.Prerelease)),new Binding{IncludePrerelease=channel!=0});
    public static List<Release> Order(IEnumerable<Release> source,int channel=1)
    {
        var versions=source.GroupBy(release=>release.Id).Select(group=>group.OrderByDescending(release=>release.IsLatest).First()).ToList();
        var latest=Default(versions,channel);
        return versions.OrderByDescending(release=>release.Id==latest?.Id).ThenByDescending(release=>release,Comparer<Release>.Create((a,b)=>ReleaseSelection.Compare(a.Tag,a.Published,b.Tag,b.Published))).ToList();
    }
}
