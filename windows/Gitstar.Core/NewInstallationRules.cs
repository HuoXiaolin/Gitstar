using System.Text.RegularExpressions;
namespace Gitstar.Core;

public record NewInstallationState(InstalledApp[] Before,bool Started=false);
public static class NewInstallationRules
{
    public static bool Installable(Asset asset)=>Rules.IsWindowsInstallCandidate(asset)&&(asset.Name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||asset.Name.EndsWith(".msi",StringComparison.OrdinalIgnoreCase));
    public static PendingInstallation Create(DownloadedPackage package, Release? release = null)
    {
        var prefix=Rules.CompareVersions(package.Tag,package.Tag)==null?Regex.Match(package.Tag,@"^.*?(?=\d+\.\d+)").Value:"";
        var app=new InstalledApp("new-install/"+package.Repository.ToLowerInvariant(),package.Repository.Split('/').Last(),"待检测","");
        var binding=new Binding{AppId=app.Id,Repository=package.Repository,IncludePrerelease=package.Prerelease,TagPrefix=prefix};
        return PendingInstallationRules.Create(package, app, binding, release) with { NewInstall = new([]) };
    }
    private static string PathKey(string path){try{return string.IsNullOrWhiteSpace(path)?"":Path.GetFullPath(path).ToUpperInvariant();}catch(ArgumentException){return "";}}
    public static InstalledApp[] Candidates(PendingInstallation item,IEnumerable<InstalledApp> after)
    {
        if(item.NewInstall is not {Started:true} state)return [];
        return after.Where(app=>app.ExecutablePath.Length>0&&!app.VersionNeedsReview&&(app.VersionSource.StartsWith("程序",StringComparison.Ordinal)||app.VersionSource.StartsWith("应用",StringComparison.Ordinal))
            &&PathKey(app.ExecutablePath)!=PathKey(item.Package.Path)&&Rules.CompareVersions(app.Version,item.TargetVersion) is >=0
            &&!Regex.IsMatch(Path.GetFileNameWithoutExtension(app.ExecutablePath),@"^(unins|uninstall|setup|update|crash|helper)",RegexOptions.IgnoreCase)
            &&AppMatcher.Suggest(app,[new(item.Package.Repository,"",false)]).Any(match=>match.Reliable&&match.Repository.Equals(item.Package.Repository,StringComparison.OrdinalIgnoreCase))
            &&!state.Before.Any(before=>PathKey(before.ExecutablePath)==PathKey(app.ExecutablePath)&&PathKey(app.ExecutablePath).Length>0&&Rules.CompareVersions(before.Version,app.Version)==0))
            .DistinctBy(app=>PathKey(app.ExecutablePath),StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static Binding? Bind(Settings settings,PendingInstallation item,IEnumerable<InstalledApp> after)
    {
        if(item.NewInstall is not {Started:true})return null;
        var apps=after.ToArray();var candidates=Candidates(item,apps);
        if(candidates.Length==0){var manuallyBound=settings.Bindings.Where(binding=>binding.Repository.Equals(item.Package.Repository,StringComparison.OrdinalIgnoreCase)).Where(binding=>InstalledVersion.Find(apps,binding.AppId) is {} known&&!known.VersionNeedsReview&&PathKey(known.ExecutablePath).Length>0&&(known.VersionSource.StartsWith("程序",StringComparison.Ordinal)||known.VersionSource.StartsWith("应用",StringComparison.Ordinal))&&Rules.CompareVersions(known.Version,item.TargetVersion) is >=0).ToArray();return manuallyBound.Length==1?manuallyBound[0]:null;}
        if(candidates.Length!=1)return null;var app=candidates[0];
        var existing=settings.Bindings.Where(binding=>InstalledVersion.Find([app],binding.AppId)!=null||PathKey(binding.ExecutablePath).Length>0&&PathKey(binding.ExecutablePath)==PathKey(app.ExecutablePath)).ToArray();
        if(existing.Length>0)return existing.Length==1&&existing[0].Repository.Equals(item.Package.Repository,StringComparison.OrdinalIgnoreCase)?existing[0]:null;
        var binding=new Binding{AppId=app.Id,Repository=item.Package.Repository,ExecutablePath=app.ExecutablePath,TagPrefix=item.Binding.TagPrefix,IncludePrerelease=item.Package.Prerelease};InstalledIdentity.Remember(binding,app);settings.Bindings.Add(binding);return binding;
    }
}
