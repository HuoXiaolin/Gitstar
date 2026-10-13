namespace Gitstar.Core;

public static class PackageRules
{
    public static string FlatFileName(string repository,string assetName,string? tag=null)
    {
        var project=Rules.SafeSegment(repository.Replace('/','_'));project=project[..Math.Min(60,project.Length)];var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(repository.ToLowerInvariant())))[..6].ToLowerInvariant();var extension=new string(Path.GetExtension(assetName).Where(c=>char.IsAsciiLetterOrDigit(c)||c=='.').Take(20).ToArray());var name=Rules.SafeSegment(Path.GetFileNameWithoutExtension(assetName));
        if(tag!=null&&Rules.CompareVersions(tag,tag)!=null){var raw=tag.Trim().TrimStart('v','V').Split('+')[0];var parts=raw.Split('-',2);var numbers=parts[0].Split('.').Select(long.Parse).ToList();while(numbers.Count<4)numbers.Add(0);tag=string.Join('.',numbers)+(parts.Length==2?"-"+parts[1]:"");}
        var assetId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assetName.ToLowerInvariant())))[..6].ToLowerInvariant();return Rules.SafeSegment(project+"_"+id+"_"+(tag!=null?Rules.SafeSegment(tag)+"_":"")+name)+"_"+assetId+extension;
    }
    public static bool SameVersion(DownloadedPackage package,Release release,Asset asset)=>package.Repository.Equals(release.Repository,StringComparison.OrdinalIgnoreCase)&&package.AssetName.Equals(asset.Name,StringComparison.OrdinalIgnoreCase)&&package.Prerelease==release.Prerelease&&(Rules.CompareVersions(package.Tag,release.Tag)==0||package.Tag.Equals(release.Tag,StringComparison.OrdinalIgnoreCase));
    public static string Family(string name)
    {
        var n=name.ToLowerInvariant();var ext=System.IO.Path.GetExtension(n);
        var platform=n.EndsWith(".apk")||n.EndsWith(".aab")?"android":n.Contains("linux")?"linux":n.Contains("macos")||n.Contains("darwin")||n.EndsWith(".dmg")?"macos":"windows";
        var arch=n.Contains("arm64")||n.Contains("aarch64")?"arm64":n.Contains("x86_64")||n.Contains("x64")||n.Contains("amd64")||n.Contains("win64")?"x64":n.Contains("x86")||n.Contains("win32")?"x86":"unknown";
        return platform+"/"+arch;
    }
    public static DownloadedPackage[] Outdated(IEnumerable<DownloadedPackage> packages)=>packages.GroupBy(p=>(p.Repository.ToLowerInvariant(),p.Family,p.Prerelease)).SelectMany(g=>g.Where(p=>p.Published<g.Max(x=>x.Published))).ToArray();
    public static bool Installed(DownloadedPackage package,IEnumerable<Binding> bindings,IEnumerable<InstalledApp> apps)
    {
        if(package.Family is not ("windows/x64" or "windows/x86"))return false;
        foreach(var binding in bindings.Where(b=>b.Repository.Equals(package.Repository,StringComparison.OrdinalIgnoreCase))){
            var app=InstalledVersion.Find(apps,binding.AppId);if(app==null || app.VersionSource is not ("程序产品版本" or "程序文件版本") || !package.Tag.StartsWith(binding.TagPrefix,StringComparison.Ordinal))continue;
            if(app.ExecutablePath.Length>0 && System.IO.Path.GetFullPath(app.ExecutablePath).Equals(System.IO.Path.GetFullPath(package.Path),StringComparison.OrdinalIgnoreCase))continue;
            if(Rules.CompareVersions(app.Version,package.Tag[binding.TagPrefix.Length..]) is >=0)return true;
        }
        return false;
    }
}
