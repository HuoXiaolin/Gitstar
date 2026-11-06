using System.Text.RegularExpressions;
namespace Gitstar.Core;

public static class InstallerVersionRules
{
    private static string Raw(Release release,Binding binding)=>binding.TagPrefix.Length>0&&release.Tag.StartsWith(binding.TagPrefix,StringComparison.Ordinal)?release.Tag[binding.TagPrefix.Length..]:release.Tag;
    public static string? Declared(string file,string releaseVersion)
    {
        if(Rules.CompareVersions(releaseVersion,releaseVersion)==null)return null;
        var name=Path.GetFileNameWithoutExtension(file);var matches=Regex.Matches(name,@"(?<![\d.])\d+\.\d+(?:\.\d+){0,2}");if(matches.Count!=1)return null;
        var match=matches[0];var numeric=match.Value;var releaseNumeric=releaseVersion.TrimStart('v','V').Split('-','+')[0];if(Rules.CompareVersions(numeric,releaseNumeric)!=0)return null;
        var suffix=name[(match.Index+match.Length)..];var preview=Regex.Match(suffix,@"^-(?<pre>(?:alpha|beta|rc|preview|pre|nightly|dev|canary|snapshot)(?:[.-][0-9A-Za-z]+)*)",RegexOptions.IgnoreCase);
        var version=numeric;if(preview.Success){var pre=Regex.Replace(preview.Groups["pre"].Value,@"(?:[-.](?:x64|x86|amd64|arm64|aarch64|win32|win64|windows|win|setup|installer|portable))+$","",RegexOptions.IgnoreCase);version+="-"+pre;}
        return Rules.CompareVersions(version,version)==null?null:version;
    }
    // A release title can say "stable" while its actual Windows installers still declare beta.
    // Only consistent, explicit same-line asset versions override it; beta and stable remain distinct.
    public static string Target(Release release,Binding binding,Asset? asset=null)
    {
        var raw=Raw(release,binding);if(asset!=null){var declared=Declared(asset.Name,raw);return declared!=null&&Rules.CompareVersions(declared,raw)!=0?declared:raw;}
        var candidates=release.Assets.Where(Rules.IsWindowsInstallCandidate).Where(item=>binding.AssetPattern.Length==0||item.Name.Contains(binding.AssetPattern,StringComparison.OrdinalIgnoreCase)).ToArray();
        var declarations=candidates.Select(item=>Declared(item.Name,raw)).ToArray();if(declarations.Any(version=>version==null))return raw;
        var versions=declarations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return versions.Length==1&&Rules.CompareVersions(versions[0]!,raw)!=0?versions[0]!:raw;
    }
    public static string Target(DownloadedPackage package,Binding binding)=>Target(new(package.ReleaseId,package.Repository,package.Tag,"","",package.Prerelease,package.Published,"",[]),binding,new(0,package.AssetName,package.Size,"",""));
    public static List<Asset> Recommended(Release release,IEnumerable<Asset> assets,string pattern="")
    {
        var all=assets.ToList();if(release.Prerelease)return all;var binding=new Binding();var raw=Raw(release,binding);if(raw.Contains('-')||Rules.CompareVersions(raw,raw)==null)return all;
        var scoped=all.Where(asset=>pattern.Length==0||asset.Name.Contains(pattern,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(!scoped.Any(asset=>Rules.IsWindowsInstallCandidate(asset)&&Declared(asset.Name,raw) is {} declared&&Rules.CompareVersions(declared,raw)==0))return all;
        return all.Where(asset=>Declared(asset.Name,raw) is not {} declared||Rules.CompareVersions(declared,raw) is not <0).ToList();
    }
}
