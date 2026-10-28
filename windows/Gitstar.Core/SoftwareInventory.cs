using System.Text.RegularExpressions;
namespace Gitstar.Core;

// Discovery evidence supplements an installation; it is not a new installation per EXE.
public static class SoftwareInventory
{
    private static string Name(string value)=>new string(Regex.Replace(value,@"\s+(?:version\s+)?v?\d+(?:\.\d+)+.*$","",RegexOptions.IgnoreCase).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    private static string PathKey(string value){try{if(value.Length==0)return "";var full=Path.GetFullPath(value);return full.TrimEnd('\\','/').Equals(Path.GetPathRoot(full)?.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)?Path.GetPathRoot(full)!:full.TrimEnd('\\','/');}catch(Exception error)when(error is ArgumentException or NotSupportedException or IOException){return "";}}
    private static bool Volume(string path)=>path.Length>0&&Same(path,Path.GetPathRoot(path)??"");
    private static bool Same(string a,string b)=>a.Length>0&&PathKey(a).Equals(PathKey(b),StringComparison.OrdinalIgnoreCase);
    private static string Root(InstalledApp app)
    {
        var folder=PathKey(app.ExecutablePath.Length>0?Path.GetDirectoryName(app.ExecutablePath)??"":app.Location);
        return Regex.IsMatch(Path.GetFileName(folder),@"^(?:app|bin|cmd|current|latest|app-.+)$",RegexOptions.IgnoreCase)?Path.GetDirectoryName(folder)??folder:folder;
    }
    private static bool Within(InstalledApp candidate,InstalledApp registration)
    {
        if(Same(candidate.ExecutablePath,registration.ExecutablePath))return true;
        var root=PathKey(registration.Location.Length>0?registration.Location:Path.GetDirectoryName(registration.ExecutablePath)??"");
        return root.Length>0&&!Volume(root)&&(Same(Root(candidate),root)||Same(Path.GetDirectoryName(candidate.ExecutablePath)??"",root));
    }
    public static List<InstalledApp> Combine(IEnumerable<InstalledApp> installed,IEnumerable<InstalledApp> discovered,IEnumerable<Binding>? bindings=null)
    {
        var registered=InstalledIdentity.Merge(installed);var subscriptions=(bindings??[]).ToArray();
        bool Protected(InstalledApp app)=>subscriptions.Any(binding=>Same(binding.ExecutablePath,app.ExecutablePath)||(app.AliasIds??[]).Append(app.Id).Contains(binding.AppId,StringComparer.OrdinalIgnoreCase));
        var additions=new List<InstalledApp>();
        foreach(var group in discovered.Where(app=>app.ExecutablePath.Length>0).DistinctBy(app=>PathKey(app.ExecutablePath),StringComparer.OrdinalIgnoreCase).GroupBy(app=>Name(app.Name)+"|"+(Volume(Root(app))?app.ExecutablePath:Root(app)),StringComparer.OrdinalIgnoreCase)){
            int Score(InstalledApp app){var file=Name(Path.GetFileNameWithoutExtension(app.ExecutablePath));return file==Name(app.Name)?2:file==Name(Path.GetFileName(Root(app)))?1:0;}
            var best=group.Max(Score);var preferred=group.Where(app=>Score(app)==best).ToArray();
            // A unique product/folder-named main program wins over sibling launch helpers.
            additions.AddRange(best>0&&preferred.Length==1?group.Where(app=>app==preferred[0]||Protected(app)):group);
        }
        var remaining=new HashSet<InstalledApp>(additions);
        for(var i=0;i<registered.Count;i++){
            var app=registered[i];var matches=additions.Where(candidate=>Name(candidate.Name)==Name(app.Name)).Where(candidate=>app.Location.Length>0||app.ExecutablePath.Length>0?Within(candidate,app):(Rules.CompareVersions(app.Version,candidate.Version)==0||app.Version=="未知"&&candidate.Version=="未知")).ToArray();
            if(app.Location.Length==0&&app.ExecutablePath.Length==0&&matches.Select(Root).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)continue;
            if(matches.Length==0)continue;
            if(app.ExecutablePath.Length==0&&matches.Length==1){var target=matches[0];registered[i]=app with{ExecutablePath=target.ExecutablePath,Location=target.Location,Version=target.Version,VersionSource=target.VersionSource,RegisteredVersion=app.Version};}
            foreach(var candidate in matches)if(!Protected(candidate)&&!Same(candidate.ExecutablePath,registered[i].ExecutablePath))remaining.Remove(candidate);
        }
        return InstalledIdentity.Merge(registered.Concat(remaining.Select(candidate=>registered.FirstOrDefault(app=>Same(app.ExecutablePath,candidate.ExecutablePath)) is {} match?candidate with{Portable=match.Portable}:candidate)));
    }
}
