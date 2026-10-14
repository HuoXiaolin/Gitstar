using System.Text.RegularExpressions;
namespace Gitstar.Core;

public static partial class Rules
{
    public static int RecentProjectCount(IEnumerable<Release> releases,DateTimeOffset now)=>releases.Where(r=>r.Published>=now.AddDays(-7) && r.Published<=now).Select(r=>r.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public static string[] MergeMembership(IEnumerable<string> current,IEnumerable<string> add,IEnumerable<string> remove)=>current.Except(remove).Union(add).Distinct().ToArray();
    public static string CredentialAnswer(string prompt,string token)
    {
        if(!Regex.IsMatch(prompt,@"https://(?:[^@\s/]+@)?github\.com(?:[/:'])",RegexOptions.IgnoreCase))return "";
        return prompt.Contains("Username",StringComparison.OrdinalIgnoreCase)?"x-access-token":prompt.Contains("Password",StringComparison.OrdinalIgnoreCase)?token:"";
    }
    [GeneratedRegex(@"^(?:v)?(?<n>\d+(?:\.\d+){0,3})(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
    public static int? CompareVersions(string target, string current)
    {
        var a = VersionRegex().Match(target.Trim()); var b = VersionRegex().Match(current.Trim());
        if (!a.Success || !b.Success) return null;
        var xs=a.Groups["n"].Value.Split('.');var ys=b.Groups["n"].Value.Split('.');
        if(xs.Concat(ys).Any(s=>!long.TryParse(s,out _)))return null;
        var x = xs.Select(long.Parse).ToArray();var y = ys.Select(long.Parse).ToArray();
        for (var i = 0; i < 4; i++) { var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0); if (c != 0) return c; }
        var ap = a.Groups["pre"].Value; var bp = b.Groups["pre"].Value;
        if (ap == bp) return 0; if (ap == "") return 1; if (bp == "") return -1;
        var aa = ap.Split('.'); var bb = bp.Split('.');
        for (var i = 0; i < Math.Min(aa.Length, bb.Length); i++) {
            int c; var an = long.TryParse(aa[i], out var av); var bn = long.TryParse(bb[i], out var bv);
            c = an && bn ? av.CompareTo(bv) : an ? -1 : bn ? 1 : string.CompareOrdinal(aa[i], bb[i]);
            if (c != 0) return c;
        }
        return aa.Length.CompareTo(bb.Length);
    }
    public static bool ValidRepository(string text) => Regex.IsMatch(text, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") && !text.Split('/').Any(x => x is "." or "..");
    public static string RepositoryName(string value) => value.Trim().Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
    public static string SafeSegment(string name)
    {
        var s = new string(name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (s.Length == 0 || s is "." or "..") s = "asset";
        if (Regex.IsMatch(s, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) s = "_" + s;
        return s.Length > 120 ? s[..120] : s;
    }
    public static bool IsWindowsX64(Asset asset)
    {
        var s = asset.Name.ToLowerInvariant().Replace("x86_64","x64");
        if (!(s.EndsWith(".exe") || s.EndsWith(".msi") || s.EndsWith(".zip"))) return false;
        if (new[] { "arm64", "aarch64", "armv", "x86", "i386", "i686", "win32", "linux", "darwin", "macos", "osx", "android", "source", "symbols", "debug", "pdb" }.Any(s.Contains)) return false;
        return s.Contains("x64") || s.Contains("amd64") || s.Contains("win64") || (s.EndsWith(".msi") && s.Contains("64"));
    }
    public static Asset? SelectAsset(Release release, Binding binding)
    {
        var a = release.Assets.Where(IsWindowsInstallCandidate);
        if (binding.AssetPattern.Length > 0) a = a.Where(x => x.Name.Contains(binding.AssetPattern, StringComparison.OrdinalIgnoreCase));
        var matches = a.ToList(); return matches.Count == 1 ? matches[0] : null;
    }
    public static bool IsWindowsInstallCandidate(Asset asset)
        =>IsWindowsInstallCandidate(asset,Environment.Is64BitOperatingSystem);
    public static bool IsWindowsInstallCandidate(Asset asset,bool host64Bit)
    {
        var name=asset.Name.ToLowerInvariant().Replace("x86_64","x64");
        if(!(name.EndsWith(".exe") || name.EndsWith(".msi") || name.EndsWith(".zip")))return false;
        if(new[]{"arm64","aarch64","armv","arm32","intel64","ia64","linux","darwin","macos","osx","android","source","symbols","debug","pdb"}.Any(name.Contains))return false;
        var x64=name.Contains("x64") || name.Contains("amd64") || name.Contains("win64") || name.Contains("64bit") || name.Contains("64-bit");
        if(x64)return host64Bit;
        if(new[]{"x86","i386","i586","i686","win32","ia32","x32","32bit","32-bit"}.Any(name.Contains))return true;
        // EXE PE headers and MSI summary information provide architecture after downloading.
        return name.EndsWith(".msi")||name.EndsWith(".exe");
    }
    public static bool IsX64MsiTemplate(string template)=>template.Split(';')[0].Trim().Equals("x64",StringComparison.OrdinalIgnoreCase);
    public static bool IsCompatibleMsiTemplate(string template,bool host64Bit)
    {
        var platform=template.Split(';')[0].Trim();
        return platform.Equals("Intel",StringComparison.OrdinalIgnoreCase) || (host64Bit && platform.Equals("x64",StringComparison.OrdinalIgnoreCase));
    }
    public static Release? SelectRelease(IEnumerable<Release> releases, Binding binding) => ReleaseSelection.Select(releases,binding);
    public static string VersionTag(Release r, Binding b) => b.TagPrefix.Length > 0 ? r.Tag[b.TagPrefix.Length..] : r.Tag;
}
