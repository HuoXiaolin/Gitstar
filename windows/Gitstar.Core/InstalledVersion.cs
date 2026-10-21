using System.Text.RegularExpressions;
namespace Gitstar.Core;

public record ProgramVersion(string Path,string ProductName,string Description,string ProductVersion,string FileVersion)
{
    public string ApplicationVersion {get;init;}="";
    public string ApplicationVersionSource {get;init;}="";
    public bool ApplicationDeclarationsConflict {get;init;}
    public string ApplicationDeclarations {get;init;}="";
}
public record VersionEvidence(string Version,string Source,string Path)
{
    public string Details {get;init;}="";
    public bool NeedsReview {get;init;}
}
public static class InstalledVersion
{
    public static string NormalizeVersion(string value)
    {
        var text=value.Trim();if(Regex.IsMatch(text,@"^\d+(?:,\s*\d+){1,3}$"))text=Regex.Replace(text,@",\s*",".");
        if(Rules.CompareVersions(text,text)==null)return "";
        text=text.TrimStart('v','V').Split('+')[0];if(Regex.IsMatch(text,@"^\d+\.\d+\.\d+\.0$"))text=text[..^2];return text;
    }
    private static string Name(string text)=>new string(Regex.Replace(text,@"\s*\([^)]*\)|\s+v?\d+(?:\.\d+)+.*$","").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    private static bool Match(string value,string app)=>value==app || new[]{"app","desktop","client","gui"}.Any(s=>value==app+s);
    private static int Score(string app,ProgramVersion file,string preferred,bool preferredTrusted)
    {
        var name=Name(System.IO.Path.GetFileNameWithoutExtension(file.Path));var expected=Name(app);
        if(Regex.IsMatch(name,@"^(unins|uninstall|setup|update|crash|helper)") || (name.Contains("service") && !expected.Contains("service")))return 0;
        var score=Match(name,expected)?100:Match(Name(file.ProductName),expected)?80:Match(Name(file.Description),expected)?70:0;
        if(score==0 && preferredTrusted && file.Path.Equals(preferred,StringComparison.OrdinalIgnoreCase))score=100;
        if(score>0 && file.Path.Equals(preferred,StringComparison.OrdinalIgnoreCase))score+=200;
        return score;
    }
    public static VersionEvidence Resolve(string app,string registered,IEnumerable<ProgramVersion> files,string preferred="",bool preferredTrusted=false)
    {
        var ranked=files.Select(f=>(File:f,Score:Score(app,f,preferred,preferredTrusted))).Where(f=>f.Score>0).OrderByDescending(f=>f.Score).ToArray();
        if(ranked.Length>0 && (ranked.Length==1||ranked[0].Score>ranked[1].Score)){
            var file=ranked[0].File;var application=NormalizeVersion(file.ApplicationVersion);var product=NormalizeVersion(file.ProductVersion);var numeric=NormalizeVersion(file.FileVersion);
            var observations=new[]{(Version:application,Source:file.ApplicationVersionSource),(Version:product,Source:"程序产品版本"),(Version:numeric,Source:"程序文件版本"),(Version:NormalizeVersion(registered),Source:"安装登记版本")}.Where(item=>item.Version.Length>0).ToArray();
            var version=application.Length>0?application:product.Length>0?product:numeric;
            var source=application.Length>0?file.ApplicationVersionSource:product.Length>0?"程序产品版本":"程序文件版本";
            if(file.ApplicationDeclarationsConflict)return new("未知","应用版本声明不一致，请核对",file.Path){Details=file.ApplicationDeclarations,NeedsReview=true};
            // Product and file versions can intentionally use different numbering schemes.
            // Keep both observations without confusing a Windows build number with the app version.
            if(version.Length>0)return new(version,source,file.Path){Details=string.Join("\n",observations.Select(item=>item.Source+"："+item.Version))};
        }
        return new(NormalizeVersion(registered) is {Length:>0} value?value:registered,ranked.Length>1?"注册表/手动版本（程序文件不唯一）":"注册表/手动版本（未验证程序文件）","");
    }
    public static InstalledApp? Find(IEnumerable<InstalledApp> apps,string id)=>apps.FirstOrDefault(a=>a.Id.Equals(id,StringComparison.OrdinalIgnoreCase) || a.AliasIds?.Contains(id,StringComparer.OrdinalIgnoreCase)==true);
}
