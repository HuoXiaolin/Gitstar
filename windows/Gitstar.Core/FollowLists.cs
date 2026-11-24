using System.Text.Json;
namespace Gitstar.Core;

public static class FollowLists
{
    public static bool IsRemoved(Settings settings, string repo) =>
        settings.RemovedRepositories.Contains(repo, StringComparer.OrdinalIgnoreCase);

    public static bool IsVisible(Settings settings, string repo) =>
        !IsRemoved(settings, repo) && !settings.HiddenRepositories.Contains(repo, StringComparer.OrdinalIgnoreCase);

    public static void RemoveFromList(Settings settings, string repo, IEnumerable<StarGroup> githubGroups, DateTimeOffset now)
    {
        var name = Rules.RepositoryName(repo);
        if (IsRemoved(settings, name)) return;

        FollowHistory.Record(settings, name, FollowKind.RemovedFromList, githubGroups.Concat(settings.LocalGroups), now);
        settings.RemovedRepositories.Add(name);
        settings.ExtraRepositories.RemoveAll(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        foreach (var group in settings.LocalGroups)
            group.Repositories.RemoveAll(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static void RestoreToList(Settings settings, string repo) =>
        settings.RemovedRepositories.RemoveAll(value => value.Equals(repo, StringComparison.OrdinalIgnoreCase));

    public static string[] Normalize(IEnumerable<string> values)=>values.Select(Rules.RepositoryName).Select(name=>Rules.ValidRepository(name)?name:throw new ArgumentException("无效的仓库名称："+name)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string[] GitHub(IEnumerable<Repository> repositories)=>Normalize(repositories.Where(r=>r.Starred).Select(r=>r.FullName));
    public static string[] Local(IEnumerable<Repository> repositories,IEnumerable<string> extra,IEnumerable<string> hidden)
    {
        var cached=repositories.ToArray();var remote=GitHub(cached).ToHashSet(StringComparer.OrdinalIgnoreCase);var excluded=hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Normalize(cached.Where(r=>!r.Starred).Select(r=>r.FullName).Concat(extra)).Where(r=>!remote.Contains(r)&&!excluded.Contains(r)).ToArray();
    }
    public static string Export(IEnumerable<string> repositories)=>JsonSerializer.Serialize(new{schemaVersion=1,kind="GitstarLocalFollows",repositories=Normalize(repositories)},new JsonSerializerOptions{WriteIndented=true});
    public static string[] Import(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.ValueKind==JsonValueKind.Array)return Normalize(root.EnumerateArray().Select(e=>e.GetString()??throw new IOException("仓库清单必须是文本。")));
        if(!root.TryGetProperty("schemaVersion",out var version)||version.GetInt32()!=1||!root.TryGetProperty("kind",out var kind)||kind.GetString()!="GitstarLocalFollows")throw new IOException("请选择 Gitstar 导出的本地关注清单。");
        return Normalize(root.GetProperty("repositories").EnumerateArray().Select(e=>e.GetString()??throw new IOException("仓库清单必须是文本。")));
    }
}
