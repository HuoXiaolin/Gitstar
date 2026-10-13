using System.Text.RegularExpressions;
namespace Gitstar.Core;
public record AppMatch(string Repository,string Reason,bool Reliable);
public static class AppMatcher
{
    public static string GitHubRepository(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Host!="github.com"||uri.Scheme is not ("http" or "https"))return "";
        var parts=uri.AbsolutePath.Trim('/').Split('/');if(parts.Length<2)return "";
        var repo=parts[0]+"/"+parts[1].Replace(".git","",StringComparison.OrdinalIgnoreCase);
        if(new[]{"settings","features","topics","orgs","users","login","marketplace","search","collections","sponsors"}.Contains(parts[0],StringComparer.OrdinalIgnoreCase))return "";
        return Rules.ValidRepository(repo)?repo:"";
    }
    private static string Normalize(string value)
    {
        value=Regex.Replace(value,@"\s*(\([^)]*\)|\[[^\]]*\])","");
        value=Regex.Replace(value,@"\s+v?\d+(?:\.\d+)+(?:[-+][\w.-]+)?\s*$","",RegexOptions.IgnoreCase);
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
    public static List<AppMatch> Suggest(InstalledApp app,IEnumerable<Repository> repositories)=>Prepare(repositories)(app);
    public static Func<InstalledApp,List<AppMatch>> Prepare(IEnumerable<Repository> repositories)
    {
        var entries=repositories.DistinctBy(r=>r.FullName,StringComparer.OrdinalIgnoreCase).Select(r=>(r.FullName,Name:Normalize(r.FullName.Split('/').Last()))).ToList();
        var index=entries.GroupBy(r=>r.Name).ToDictionary(g=>g.Key,g=>g.Select(r=>r.FullName).ToList());
        return app=>{
            if(Rules.ValidRepository(app.RepositoryHint))return [new(app.RepositoryHint,"软件登记的 GitHub 地址",true)];
            var name=Normalize(app.Name);if(name.Length<4)return [];
            if(index.TryGetValue(name,out var exact))return exact.Select(r=>new AppMatch(r,exact.Count==1?"软件与仓库名称一致":"同名仓库，请选择作者",exact.Count==1)).ToList();
            return entries.Where(r=>r.Name.Length>=5 && (name.StartsWith(r.Name,StringComparison.Ordinal)||r.Name.StartsWith(name,StringComparison.Ordinal)))
                .Take(12).Select(r=>new AppMatch(r.FullName,"名称相近，请确认",false)).ToList();
        };
    }
}
