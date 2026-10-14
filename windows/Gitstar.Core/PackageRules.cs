namespace Gitstar.Core;

public static class PackageRules
{
    public static string OriginalFileName(string assetName)
    {
        if(string.IsNullOrWhiteSpace(assetName)||assetName.Length>255||assetName.EndsWith(' ')||assetName.EndsWith('.')||assetName.Any(c=>c<32||"<>:\"/\\|?*".Contains(c))||System.Text.RegularExpressions.Regex.IsMatch(assetName,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new ArgumentException("Release 附件原名不能用于 Windows 文件名："+assetName+"。请使用作者提供的其他附件。");
        return assetName;
    }
    // Previous releases added repository/version/hash components; retain recognition for existing records.
    public static string LegacyFlatFileName(string repository,string assetName,string? tag=null)
    {
        var project=Rules.SafeSegment(repository.Replace('/','_'));project=project[..Math.Min(60,project.Length)];var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(repository.ToLowerInvariant())))[..6].ToLowerInvariant();var extension=new string(Path.GetExtension(assetName).Where(c=>char.IsAsciiLetterOrDigit(c)||c=='.').Take(20).ToArray());var name=Rules.SafeSegment(Path.GetFileNameWithoutExtension(assetName));
        if(tag!=null&&Rules.CompareVersions(tag,tag)!=null){var raw=tag.Trim().TrimStart('v','V').Split('+')[0];var parts=raw.Split('-',2);var numbers=parts[0].Split('.').Select(long.Parse).ToList();while(numbers.Count<4)numbers.Add(0);tag=string.Join('.',numbers)+(parts.Length==2?"-"+parts[1]:"");}
        var assetId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assetName.ToLowerInvariant())))[..6].ToLowerInvariant();return Rules.SafeSegment(project+"_"+id+"_"+(tag!=null?Rules.SafeSegment(tag)+"_":"")+name)+"_"+assetId+extension;
    }
    public static string[] OriginalNameConflicts(IEnumerable<ReleaseDownload> items)=>items.GroupBy(item=>OriginalFileName(item.Asset.Name),StringComparer.OrdinalIgnoreCase).Where(group=>group.Skip(1).Any(item=>!SameDownload(group.First(),item))).Select(group=>group.Key).ToArray();
    public static bool SameDownload(ReleaseDownload a,ReleaseDownload b)=>a.Release.Repository.Equals(b.Release.Repository,StringComparison.OrdinalIgnoreCase)&&(Rules.CompareVersions(a.Release.Tag,b.Release.Tag)==0||a.Release.Tag.Equals(b.Release.Tag,StringComparison.OrdinalIgnoreCase))&&a.Release.Prerelease==b.Release.Prerelease&&a.Asset.Name.Equals(b.Asset.Name,StringComparison.OrdinalIgnoreCase)&&a.Asset.Size==b.Asset.Size&&a.Asset.Digest==b.Asset.Digest;
    public static bool SameVersion(DownloadedPackage package,Release release,Asset asset)=>package.Repository.Equals(release.Repository,StringComparison.OrdinalIgnoreCase)&&package.AssetName.Equals(asset.Name,StringComparison.OrdinalIgnoreCase)&&package.Prerelease==release.Prerelease&&(Rules.CompareVersions(package.Tag,release.Tag)==0||package.Tag.Equals(release.Tag,StringComparison.OrdinalIgnoreCase));
    public static string Family(string name)
    {
        var n=name.ToLowerInvariant();var ext=System.IO.Path.GetExtension(n);
        var platform=n.EndsWith(".apk")||n.EndsWith(".aab")?"android":n.Contains("linux")?"linux":n.Contains("macos")||n.Contains("darwin")||n.EndsWith(".dmg")?"macos":"windows";
        var arch=n.Contains("arm64")||n.Contains("aarch64")?"arm64":n.Contains("x86_64")||n.Contains("x64")||n.Contains("amd64")||n.Contains("win64")?"x64":n.Contains("x86")||n.Contains("win32")?"x86":"unknown";
        return platform+"/"+arch;
    }
    public static bool NewerVersion(string tag,DateTimeOffset published,string previousTag,DateTimeOffset previousPublished,string prefix="")
    {
        var version=tag[prefix.Length..];var previous=previousTag[prefix.Length..];var comparison=Rules.CompareVersions(version,previous);
        if(comparison!=null)return comparison>0;
        // Ambiguous labels cannot authorize deleting a recognized version.
        if(Rules.CompareVersions(version,version)!=null||Rules.CompareVersions(previous,previous)!=null)return false;
        return !tag.Equals(previousTag,StringComparison.OrdinalIgnoreCase)&&published>previousPublished;
    }
    public static DownloadedPackage[] Outdated(IEnumerable<DownloadedPackage> packages,IEnumerable<Binding>? bindings=null)
    {
        var lines=(bindings??[]).Where(b=>b.TagPrefix.Length>0).ToArray();
        string Line(DownloadedPackage p)=>lines.Where(b=>b.Repository.Equals(p.Repository,StringComparison.OrdinalIgnoreCase)&&p.Tag.StartsWith(b.TagPrefix,StringComparison.Ordinal)).OrderByDescending(b=>b.TagPrefix.Length).Select(b=>b.TagPrefix).FirstOrDefault()??"";
        return packages.GroupBy(p=>(Repository:p.Repository.ToLowerInvariant(),p.Family,p.Prerelease,Prefix:Line(p))).SelectMany(g=>g.Where(p=>!p.RetainForBatch&&g.Any(other=>NewerVersion(other.Tag,other.Published,p.Tag,p.Published,g.Key.Prefix)))).ToArray();
    }
    public static bool Installed(DownloadedPackage package,IEnumerable<Binding> bindings,IEnumerable<InstalledApp> apps)
    {
        if(package.RetainForBatch||package.Family is not ("windows/x64" or "windows/x86"))return false;
        foreach(var binding in bindings.Where(b=>b.Repository.Equals(package.Repository,StringComparison.OrdinalIgnoreCase))){
            var app=InstalledVersion.Find(apps,binding.AppId);if(app==null || app.VersionSource is not ("程序产品版本" or "程序文件版本") || !package.Tag.StartsWith(binding.TagPrefix,StringComparison.Ordinal))continue;
            if(app.ExecutablePath.Length>0 && System.IO.Path.GetFullPath(app.ExecutablePath).Equals(System.IO.Path.GetFullPath(package.Path),StringComparison.OrdinalIgnoreCase))continue;
            if(Rules.CompareVersions(app.Version,package.Tag[binding.TagPrefix.Length..]) is >=0)return true;
        }
        return false;
    }
}
