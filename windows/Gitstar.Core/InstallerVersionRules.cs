using System.Text.RegularExpressions;
namespace Gitstar.Core;

public static class InstallerVersionRules
{
    private static string Raw(string tag, Binding binding) => tag.StartsWith(binding.TagPrefix, StringComparison.Ordinal) ? tag[binding.TagPrefix.Length..] : tag;
    private static string NumericVersion(string version) => version.Trim().TrimStart('v', 'V').Split('-', '+')[0];
    private static bool IsPreview(string version) => version.Split('+')[0].Contains('-');

    public static string? Declared(string file,string releaseVersion)
    {
        if(Rules.CompareVersions(releaseVersion,releaseVersion)==null)return null;
        var name=Path.GetFileNameWithoutExtension(file);var matches=Regex.Matches(name,@"(?<![\d.])\d+\.\d+(?:\.\d+){0,2}");if(matches.Count!=1)return null;
        var match=matches[0];var numeric=match.Value;if(Rules.CompareVersions(numeric,NumericVersion(releaseVersion))!=0)return null;
        var suffix=name[(match.Index+match.Length)..];var preview=Regex.Match(suffix,@"^-(?<pre>(?:alpha|beta|rc|preview|pre|nightly|dev|canary|snapshot)(?:[.-][0-9A-Za-z]+)*)",RegexOptions.IgnoreCase);
        var version=numeric;if(preview.Success){var pre=Regex.Replace(preview.Groups["pre"].Value,@"(?:[-.](?:x64|x86|amd64|arm64|aarch64|win32|win64|windows|win|setup|installer|portable))+$","",RegexOptions.IgnoreCase);version+="-"+pre;}
        return Rules.CompareVersions(version,version)==null?null:version;
    }
    private static string? AttachmentVersion(Asset asset, string releaseVersion, IReadOnlyList<Asset> attachments)
    {
        var declared = Declared(asset.Name, releaseVersion);
        if (declared == null || IsPreview(declared) || asset.Size <= 0 || !Rules.IsWindowsInstallCandidate(asset)) return declared;
        var digest = asset.Digest;
        if (digest is not {Length: 71} || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !digest[7..].All(Uri.IsHexDigit)) return declared;

        // The publisher can attach the same installer under a stable name and its original preview name.
        var extension = Path.GetExtension(asset.Name);
        var previews = attachments.Where(other => other.Size == asset.Size && string.Equals(other.Digest, digest, StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(other.Name).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .Select(other => Declared(other.Name, releaseVersion))
            .OfType<string>().Where(IsPreview).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return previews.Length == 1 ? previews[0] : declared;
    }

    public static string Target(Release release, Binding binding, Asset? asset = null)
    {
        var raw = Raw(release.Tag, binding);
        if (asset != null) return PreferDeclared(AttachmentVersion(asset, raw, release.Assets), raw);

        var versions = release.Assets.Where(Rules.IsWindowsInstallCandidate)
            .Where(item => binding.AssetPattern.Length == 0 || item.Name.Contains(binding.AssetPattern, StringComparison.OrdinalIgnoreCase))
            .Select(item => AttachmentVersion(item, raw, release.Assets)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return versions.Length == 1 ? PreferDeclared(versions[0], raw) : raw;
    }

    private static string PreferDeclared(string? declared, string raw) => declared != null && Rules.CompareVersions(declared, raw) != 0 ? declared : raw;

    public static string Target(DownloadedPackage package, Binding binding, Release? release = null, string? savedTarget = null)
    {
        var raw = Raw(package.Tag, binding);
        var attachments = release != null && release.Id == package.ReleaseId && release.Tag == package.Tag
            && release.Repository.Equals(package.Repository, StringComparison.OrdinalIgnoreCase) ? release.Assets : [];
        var asset = new Asset(0, package.AssetName, package.Size, "", "sha256:" + package.Sha256);
        var declared = AttachmentVersion(asset, raw, attachments);
        var target = PreferDeclared(declared, raw);

        // Keep a target already resolved for this saved package when the release cache is pruned.
        if (savedTarget != null && !IsPreview(target) && IsPreview(savedTarget)
            && Rules.CompareVersions(savedTarget, target) is < 0 && Rules.CompareVersions(NumericVersion(savedTarget), NumericVersion(target)) == 0)
            return savedTarget;
        return target;
    }

    public static List<Asset> Recommended(Release release, IEnumerable<Asset> assets, string pattern = "")
    {
        var all = assets.ToList();
        var raw = release.Tag;
        if (release.Prerelease || IsPreview(raw) || Rules.CompareVersions(raw, raw) == null) return all;

        var candidates = all.Select(asset => (Asset: asset, Version: AttachmentVersion(asset, raw, release.Assets))).ToArray();
        var hasStable = candidates.Any(item => Rules.IsWindowsInstallCandidate(item.Asset) && Rules.CompareVersions(item.Version ?? "", raw) == 0
            && (pattern.Length == 0 || item.Asset.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
        return hasStable ? candidates.Where(item => Rules.CompareVersions(item.Version ?? "", raw) is not < 0).Select(item => item.Asset).ToList() : all;
    }
}
