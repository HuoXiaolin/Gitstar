using System.Security.Cryptography;
namespace Gitstar.Desktop;
public static class PackageFiles
{
    public static async Task<bool> Matches(DownloadedPackage package,CancellationToken ct=default)
    {
        var expected=Path.Combine(Path.GetFullPath(package.Root),Rules.SafeSegment(package.Repository.Replace('/','_')),Rules.SafeSegment(package.AssetName));
        if(!Path.GetFullPath(package.Path).Equals(expected,StringComparison.OrdinalIgnoreCase)||!File.Exists(package.Path)||new FileInfo(package.Path).Length!=package.Size)return false;
        await using var stream=File.OpenRead(package.Path);return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase);
    }
}
