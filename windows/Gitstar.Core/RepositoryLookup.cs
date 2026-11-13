namespace Gitstar.Core;

public static class RepositoryLookup
{
    public static string Address(string text)
    {
        var address = text.Trim().TrimEnd('/');
        return Rules.ValidRepository(address) ? address : AppMatcher.GitHubRepository(address);
    }

    public static Repository[] Filter(IEnumerable<Repository> repositories, string query, int limit)
    {
        var address = Address(query);
        if (address.Length > 0)
            return repositories.Where(repo => repo.FullName.Equals(address, StringComparison.OrdinalIgnoreCase)).Take(1).ToArray();

        query = query.Trim();
        return repositories.Select(repo => (Repo: repo, Score: Score(repo, query)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Repo.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(limit).Select(candidate => candidate.Repo).ToArray();
    }

    private static int Score(Repository repo, string query)
    {
        var name = repo.FullName[(repo.FullName.IndexOf('/') + 1)..];
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 300;
        if (query.Length > 0 && name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 200;
        var nameScore = Math.Max(SearchText.Score(name, query) * 2, SearchText.Score(repo.FullName, query));
        var descriptionScore = repo.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 60 : 0;
        return query.Length == 0 ? 1 : Math.Max(nameScore, descriptionScore);
    }

    public static bool CanSearch(string query) => query.Length is >= 2 and <= 200
        && (Address(query).Length > 0 || query.Any(char.IsLetterOrDigit));

    public static bool IsProjectMention(Repository repo, string appName)
    {
        var name = appName.Trim();
        if (name.Length < 4) return false;
        var description = repo.Description.Trim();
        if (!description.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return false;
        if (description.Length == name.Length) return true;
        var next = description[name.Length];
        return char.IsWhiteSpace(next) || next is ',' or '，' or ':' or '：' or '(' or '（' or '·' or '。' or '、' or '—' or '!';
    }
}
