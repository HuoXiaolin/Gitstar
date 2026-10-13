namespace Gitstar.Core;

public static class PackageRules
{
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
