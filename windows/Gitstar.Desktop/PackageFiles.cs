using System.Security.Cryptography;
namespace Gitstar.Desktop;
public static class PackageFiles
{
    public static bool ExpectedPath(DownloadedPackage package)
    {
        var root=Path.GetFullPath(package.Root);var path=Path.GetFullPath(package.Path);
        return new[]{Path.Combine(root,PackageRules.FlatFileName(package.Repository,package.AssetName)),Path.Combine(root,PackageRules.FlatFileName(package.Repository,package.AssetName,package.Tag)),Path.Combine(root,Rules.SafeSegment(package.Repository.Replace('/','_')),Rules.SafeSegment(package.AssetName))}.Any(expected=>path.Equals(expected,StringComparison.OrdinalIgnoreCase));
    }
    public static async Task<bool> Matches(DownloadedPackage package,CancellationToken ct=default)
    {
        if(!ExpectedPath(package)||!File.Exists(package.Path)||new FileInfo(package.Path).Length!=package.Size)return false;
        await using var stream=File.OpenRead(package.Path);return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase);
    }
}
