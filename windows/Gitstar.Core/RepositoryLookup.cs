using System.Text.RegularExpressions;

namespace Gitstar.Core;

public static class RepositoryLookup
{
    private const string Architecture = @"(?<![\p{L}\p{N}])(?:x86[-_]64|x64|x86|amd64|arm64|aarch64|win32|win64|(?:32|64)[ -]?(?:bit|位))(?![\p{L}\p{N}])";
    private const string DisplayVersion = @"(?<![a-z0-9.])(?:(?:v(?:er(?:sion)?)?\.?|版本)\s*[:：]?\s*[0-9]+(?:\.[0-9]+)*|[0-9]+(?:\.[0-9]+)+)"
        + @"(?:[-.]?(?:alpha|beta|rc|preview|pre)(?:[.-]?[0-9]+)?)?(?:\+[0-9a-z.-]+)?(?:\s*版)?(?![a-z0-9.])";
    private static readonly Regex SoftwareQualifiers = new(Architecture + "|" + DisplayVersion, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LeftoverSeparators = new(@"[()（）\[\]【】]|[-_/·,:，：]+(?=\s)|(?<=\s)[-_/·,:，：]+");
    private static readonly Regex Spaces = new(@"\s+");

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

    public static string[] SoftwareSearchQueries(InstalledApp app)
    {
        var name = Spaces.Replace(app.Name.Trim(), " ");
        var path = app.ExecutablePath.Trim().Trim('"');
        var leafName = path[(path.LastIndexOfAny(['\\', '/', ':']) + 1)..];
        var executable = leafName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? leafName[..^4] : "";
        return new[] { name, SimplifySoftwareName(name, app), executable, SimplifySoftwareName(executable, app) }
            .Where(CanSearch).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string SimplifySoftwareName(string name, InstalledApp app)
    {
        var simplified = SoftwareQualifiers.Replace(name, " ");
        if (simplified != name)
            simplified = Spaces.Replace(LeftoverSeparators.Replace(simplified, " "), " ").Trim(' ', '-', '_', '/', '·', ',', '，', ':', '：');

        // A standalone number without a version marker may belong to the product name.
        var lastSpace = simplified.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            var suffix = simplified[(lastSpace + 1)..];
            if (suffix.Length > 0 && suffix.All(char.IsAsciiDigit)
                && (Rules.CompareVersions(app.Version, suffix) == 0 || Rules.CompareVersions(app.RegisteredVersion, suffix) == 0))
                simplified = simplified[..lastSpace];
        }
        return simplified;
    }

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
