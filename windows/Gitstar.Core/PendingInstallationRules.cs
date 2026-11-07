namespace Gitstar.Core;

public record PendingInstallation(string Id,DownloadedPackage Package,InstalledApp App,Binding Binding,string TargetVersion,DateTimeOffset Added)
{
    public NewInstallationState? NewInstall {get;init;}
}

public static class PendingInstallationRules
{
    public static PendingInstallation Create(DownloadedPackage package,InstalledApp app,Binding binding,Release? release=null)
    {
        var tag=InstallerVersionRules.Target(package,binding,release);
        return new(Guid.NewGuid().ToString("N"),package,app,binding.Snapshot(),tag,DateTimeOffset.UtcNow);
    }
    private static string PathKey(string path)
    {
        if(string.IsNullOrWhiteSpace(path))return "";
        try{return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();}catch(Exception error)when(error is ArgumentException or NotSupportedException){return path.ToUpperInvariant();}
    }
    public static string ProgramPath(PendingInstallation item)=>item.Binding.ExecutablePath.Length>0?item.Binding.ExecutablePath:item.App.ExecutablePath;
    public static bool SameContext(PendingInstallation a,PendingInstallation b)
    {
        var path=PathKey(ProgramPath(a));var other=PathKey(ProgramPath(b));
        return a.Package.Repository.Equals(b.Package.Repository,StringComparison.OrdinalIgnoreCase)&&a.Binding.TagPrefix==b.Binding.TagPrefix&&a.Binding.AssetPattern==b.Binding.AssetPattern
            &&a.Binding.IncludePrerelease==b.Binding.IncludePrerelease&&(path.Length>0&&other.Length>0?path==other:a.App.Id.Equals(b.App.Id,StringComparison.OrdinalIgnoreCase));
    }
    public static void Upsert(List<PendingInstallation> items,PendingInstallation entry)
    {
        var previous=items.FirstOrDefault(item=>SameContext(item,entry)&&item.Package.Path.Equals(entry.Package.Path,StringComparison.OrdinalIgnoreCase)&&PackageRules.SameVersion(item.Package,
            new(entry.Package.ReleaseId,entry.Package.Repository,entry.Package.Tag,"","",entry.Package.Prerelease,entry.Package.Published,"",[]),new(0,entry.Package.AssetName,entry.Package.Size,"","")));
        if(previous!=null)entry=entry with{Id=previous.Id,Added=previous.Added};
        items.RemoveAll(item=>item.Id==entry.Id||SameContext(item,entry)&&Rules.CompareVersions(entry.TargetVersion,item.TargetVersion)>0);
        items.Add(entry);
    }
    public static PendingInstallation[] RemoveSelected(List<PendingInstallation> items,IEnumerable<string> selected,IEnumerable<string> busy)
    {
        var ids=selected.ToHashSet(StringComparer.Ordinal);var active=busy.ToHashSet(StringComparer.Ordinal);
        var removed=items.Where(item=>ids.Contains(item.Id)&&!active.Contains(item.Id)).ToArray();
        var removedIds=removed.Select(item=>item.Id).ToHashSet(StringComparer.Ordinal);items.RemoveAll(item=>removedIds.Contains(item.Id));return removed;
    }
    public static Binding? CurrentBinding(PendingInstallation item,IEnumerable<Binding> bindings,IEnumerable<InstalledApp> apps)
    {
        var path=PathKey(ProgramPath(item));
        return bindings.FirstOrDefault(binding=>binding.Repository.Equals(item.Package.Repository,StringComparison.OrdinalIgnoreCase)&&binding.TagPrefix==item.Binding.TagPrefix&&binding.AssetPattern==item.Binding.AssetPattern&&binding.IncludePrerelease==item.Binding.IncludePrerelease
            &&(path.Length>0?PathKey(binding.ExecutablePath.Length>0?binding.ExecutablePath:InstalledVersion.Find(apps,binding.AppId)?.ExecutablePath??"")==path
                :binding.AppId.Equals(item.App.Id,StringComparison.OrdinalIgnoreCase)));
    }
    public static InstalledApp? CurrentApp(PendingInstallation item,IEnumerable<Binding> bindings,IEnumerable<InstalledApp> apps)
    {
        var all=apps.ToArray();var binding=CurrentBinding(item,bindings,all);
        return binding==null?null:InstalledVersion.Find(all,binding.AppId);
    }
    public static bool Installed(PendingInstallation item,IEnumerable<Binding> bindings,IEnumerable<InstalledApp> apps,bool fresh)
    {
        if(!fresh)return false;
        var app=CurrentApp(item,bindings,apps);if(app==null||app.VersionNeedsReview||app.ExecutablePath.Length==0||app.VersionSource.StartsWith("注册表",StringComparison.Ordinal))return false;
        var expected=PathKey(ProgramPath(item));if(expected.Length>0&&PathKey(app.ExecutablePath)!=expected)return false;
        return Rules.CompareVersions(app.Version,item.TargetVersion) is >=0;
    }
    public static bool Superseded(PendingInstallation item,IEnumerable<Release> freshReleases)
    {
        var latest=Rules.SelectRelease(freshReleases.Where(release=>release.Repository.Equals(item.Package.Repository,StringComparison.OrdinalIgnoreCase)),item.Binding);
        if(latest==null||latest.Id==item.Package.ReleaseId||Rules.CompareVersions(Rules.VersionTag(latest,item.Binding),item.TargetVersion)==0)return false;
        if(latest.IsLatest&&!latest.Prerelease&&!item.Package.Prerelease)return true;
        if(!item.Package.Tag.StartsWith(item.Binding.TagPrefix,StringComparison.Ordinal))return false;
        return ReleaseSelection.Compare(latest.Tag,latest.Published,item.Package.Tag,item.Package.Published,item.Binding.TagPrefix)>0;
    }
}
