namespace Gitstar.Core;

public static class SpecialFollows
{
    public const string GroupId="special-following";
    public static bool Contains(Settings settings,string repository)=>(settings.SpecialRepositories??[]).Contains(repository,StringComparer.OrdinalIgnoreCase);
    public static void Set(Settings settings,string repository,bool selected)
    {
        var name=Rules.RepositoryName(repository);if(!Rules.ValidRepository(name))throw new IOException("项目地址无效，无法设置特别关注。");
        settings.SpecialRepositories??=[];settings.SpecialRepositories.RemoveAll(value=>value.Equals(name,StringComparison.OrdinalIgnoreCase));
        if(selected)settings.SpecialRepositories.Add(name);
    }
    public static string[] Visible(Settings settings,IEnumerable<string> repositories)=>repositories.Where(repo=>Contains(settings,repo)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
