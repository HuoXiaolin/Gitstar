using System.Security.Cryptography;
namespace Gitstar.Desktop;
public static class PackageFiles
{
    public static bool ExpectedPath(DownloadedPackage package)
    {
        var root=Path.GetFullPath(package.Root);var path=Path.GetFullPath(package.Path);
        var expected=new List<string>{Path.Combine(root,PackageRules.LegacyFlatFileName(package.Repository,package.AssetName)),Path.Combine(root,PackageRules.LegacyFlatFileName(package.Repository,package.AssetName,package.Tag)),Path.Combine(root,Rules.SafeSegment(package.Repository.Replace('/','_')),Rules.SafeSegment(package.AssetName))};
        try{expected.Add(Path.Combine(root,PackageRules.OriginalFileName(package.AssetName)));}catch(ArgumentException){}
        return expected.Any(candidate=>path.Equals(candidate,StringComparison.OrdinalIgnoreCase));
    }
    public static async Task<bool> Matches(DownloadedPackage package,CancellationToken ct=default)
    {
        if(!ExpectedPath(package)||!File.Exists(package.Path)||new FileInfo(package.Path).Length!=package.Size)return false;
        await using var stream=File.OpenRead(package.Path);return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase);
    }
}
