using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Gitstar.Core;

public enum DownloadPurpose { Save, Install, PortableUpdate }
public record DownloadSelectionPreference(string Repository,DownloadPurpose Purpose,string Tag,List<string> FileNames);

public static class DownloadSelectionRules
{
    public static DownloadPurpose Purpose(bool install,bool portable) => !install?DownloadPurpose.Save:portable?DownloadPurpose.PortableUpdate:DownloadPurpose.Install;

    public static void Remember(Settings settings,Release release,DownloadPurpose purpose,IEnumerable<Asset> downloaded)
    {
        var names=downloaded.Where(asset=>asset.Size>0).Select(asset=>asset.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(names.Count==0)return;
        settings.DownloadSelections.RemoveAll(item=>item.Purpose==purpose&&item.Repository.Equals(release.Repository,StringComparison.OrdinalIgnoreCase));
        settings.DownloadSelections.Add(new(release.Repository,purpose,release.Tag,names));
    }

    // Only successful downloads become preferences. The source file may later be cleaned up or moved.
    public static DownloadSelectionPreference? Find(Settings settings,string repository,DownloadPurpose purpose,IEnumerable<DownloadedPackage> history)
    {
        var saved=settings.DownloadSelections.LastOrDefault(item=>item.Purpose==purpose&&item.Repository.Equals(repository,StringComparison.OrdinalIgnoreCase));
        if(saved!=null)return saved;
        // Older versions kept insertion-ordered package records, but had no explicit picker preference.
        var previous=history.LastOrDefault(item=>!item.RetainForBatch&&item.Repository.Equals(repository,StringComparison.OrdinalIgnoreCase));
        return previous==null?null:new(repository,purpose,previous.Tag,[previous.AssetName]);
    }

    public static Asset? SelectInstaller(Release release, Binding binding, DownloadSelectionPreference? previous, Architecture host)
    {
        var host64Bit = host is Architecture.X64 or Architecture.Arm64;
        var candidates = release.Assets.Where(asset => Rules.IsWindowsInstallCandidate(asset, host64Bit)
            && PackagePreference.CanRecommend(asset, host)
            && (binding.AssetPattern.Length == 0 || asset.Name.Contains(binding.AssetPattern, StringComparison.OrdinalIgnoreCase))).ToList();

        if (previous is { Purpose: DownloadPurpose.Install }
            && Match(release with { Assets = candidates }, previous, host).FirstOrDefault() is { } remembered)
        {
            // Prefer the previously selected installer format and variant. A stable build of that variant still supersedes a preview alias.
            var format = Format(remembered.Name);
            var family = Family(remembered.Name);
            var roles = Roles(remembered.Name);
            var sameVariant = candidates.Where(asset => Format(asset.Name) == format && Family(asset.Name) == family && Roles(asset.Name) == roles);
            var recommended = InstallerVersionRules.Recommended(release, sameVariant, tagPrefix: binding.TagPrefix, host64Bit: host64Bit);
            return recommended.Contains(remembered) ? remembered : PackagePreference.Select(recommended, host);
        }

        return PackagePreference.Select(InstallerVersionRules.Recommended(release, candidates, tagPrefix: binding.TagPrefix, host64Bit: host64Bit), host);
    }

    public static List<Asset> Match(Release release,DownloadSelectionPreference? previous,Architecture host,string pattern="")
    {
        if(previous==null||!previous.Repository.Equals(release.Repository,StringComparison.OrdinalIgnoreCase))return [];
        var assets=release.Assets.Where(asset=>asset.Size>0).ToList();
        if(previous.Purpose!=DownloadPurpose.Save)
        {
            var portable=previous.Purpose==DownloadPurpose.PortableUpdate;
            assets=assets.Where(asset=>PackagePreference.CanRecommend(asset,host,portable)).ToList();
            var matching=assets.Where(asset=>pattern.Length>0&&asset.Name.Contains(pattern,StringComparison.OrdinalIgnoreCase)).ToList();
            if(matching.Count>0)assets=matching;
        }
        var result=new List<Asset>();
        foreach(var name in previous.FileNames)
        {
            var available=assets.Where(asset=>!result.Any(chosen=>chosen.Id==asset.Id)).ToList();
            var shape=Shape(name,previous.Tag);
            var match=available.FirstOrDefault(asset=>asset.Name.Equals(name,StringComparison.OrdinalIgnoreCase))
                ??available.FirstOrDefault(asset=>Shape(asset.Name,release.Tag)==shape);
            if(match==null&&new[]{".exe",".msi",".msix",".msixbundle",".appx",".appxbundle",".zip",".7z",".tar.gz",".tar.xz",".tar.bz2",".tar.zst",".dmg",".pkg",".apk",".aab",".deb",".rpm",".appimage"}.Contains(Format(name)))
            {
                // Keep platform, architecture and CLI/setup/portable variants when only the name changed.
                var roles=Roles(name);
                var sameType=available.Where(asset=>Format(asset.Name)==Format(name)&&SameFamily(name,asset.Name,host)&&Roles(asset.Name)==roles).ToList();
                match=PackagePreference.Select(sameType,host)??sameType.FirstOrDefault();
            }
            if(match!=null)result.Add(match);
        }
        return result;
    }

    private static string Format(string name)
    {
        var lower=name.ToLowerInvariant();
        return new[]{".tar.gz",".tar.xz",".tar.bz2",".tar.zst"}.FirstOrDefault(lower.EndsWith)??Path.GetExtension(lower);
    }

    private static string Roles(string name) => string.Join('/',Regex.Matches(name.ToLowerInvariant(),@"(?:^|[._\- ])(cli|gui|setup|installer|portable|standalone|noinstall|online|offline|win7|windows7|server2008r2?|server2012r2?|cpu|cuda|vulkan|avx2?|source|sources|src|symbols|debug)(?=[._\- ]|$)").Select(match=>match.Groups[1].Value).Distinct().Order());

    private static string Family(string name)
    {
        var n=name.ToLowerInvariant().Replace("x86_64","x64").Replace("x86-64","x64");
        bool Has(string tokens)=>Regex.IsMatch(n,@"(?:^|[._\- ])(?:"+tokens+@")(?:[._\- ]|$)");
        var platform=Has("android")||n.EndsWith(".apk")||n.EndsWith(".aab")?"android":Has("darwin|macos|osx|mac")||n.EndsWith(".dmg")||n.EndsWith(".pkg")?"macos":Has("linux|ubuntu|debian")?"linux":"windows";
        var arch=Has("arm64|aarch64")?"arm64":Has("x64|amd64|win64|64bit|64-bit")?"x64":Has("x86|i386|i686|win32|ia32|32bit|32-bit")?"x86":Has("arm|armv7|arm32")?"arm":"unknown";
        return platform+"/"+arch;
    }

    private static bool SameFamily(string previous,string candidate,Architecture host)
    {
        var a=Family(previous).Split('/');var b=Family(candidate).Split('/');
        if(a[0]!=b[0])return false;
        if(a[1]==b[1]||b[1]=="unknown")return true;
        if(a[1]!="unknown")return false;
        var native=host switch{Architecture.X64=>"x64",Architecture.X86=>"x86",Architecture.Arm64=>"arm64",Architecture.Arm=>"arm",_=>"unknown"};
        return b[1]==native||host==Architecture.X64&&b[1]=="x86"||host==Architecture.Arm64&&b[1] is "x64" or "x86";
    }

    private static string Shape(string name,string tag)
    {
        var shape=name.ToLowerInvariant().Replace("x86_64","x64").Replace("x86-64","x64").Replace("amd64","x64").Replace("aarch64","arm64");
        // Remove the release tag as a complete component; never replace digits within x64/arm64.
        if(tag.Any(char.IsAsciiDigit))shape=Regex.Replace(shape,@"(?<![a-z0-9])"+Regex.Escape(tag.Trim().ToLowerInvariant())+@"(?![a-z0-9])","");
        shape=Regex.Replace(shape,@"(?<![a-z0-9])v?\d+(?:\.\d+){1,3}(?:[-._]?(?:alpha|beta|preview|rc|dev)[-._]?\d*)?(?![a-z0-9])","");
        return Regex.Replace(shape,@"[._\- ]+","-").Trim('-');
    }
}
