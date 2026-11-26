using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
namespace Gitstar.Core;
public enum RankingKind { Trending,New,Popular,Search }
public record RankingEntry(Repository Repository,int Position,string Language="",long? GainedStars=null,DateTimeOffset? Created=null);
public record RankingPage(RankingKind Kind,int Period,int Page,DateTimeOffset Fetched,List<RankingEntry> Items,int Total,bool More,bool Incomplete=false,long StarsFloor=0)
{
    public string SearchQuery { get; init; } = "";
}
public sealed record RankingSearchStep(string Query, string Progress, bool OptionalScope = false);

public static class RepositoryRankings
{
    private static readonly Regex Login = new(@"^[a-z0-9](?:[a-z0-9-]{0,37}[a-z0-9])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SearchTerm = new(@"[\p{L}\p{N}][\p{L}\p{N}+#]*");

    public static string Period(int index)=>index switch{0=>"daily",1=>"weekly",2=>"monthly",_=>throw new IOException("无效的榜单周期。")};
    public static int NewDays(int index)=>index switch{0=>7,1=>30,2=>90,_=>throw new IOException("无效的新项目周期。")};
    public static string SearchQuery(RankingKind kind,int period,string text,DateTimeOffset now)
    {
        var query="is:public archived:false fork:false";
        if(kind==RankingKind.New)return query+" created:>="+now.UtcDateTime.Date.AddDays(-NewDays(period)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(kind==RankingKind.Popular)return query;
        if(kind!=RankingKind.Search)throw new IOException("该榜单使用 GitHub 趋势页。");
        return SearchSteps(text)[0].Query;
    }

    public static IReadOnlyList<RankingSearchStep> SearchSteps(string text)
    {
        text = text.Trim().Normalize(NormalizationForm.FormKC);
        if (text.Length is < 2 or > 120) throw new IOException("请输入 2 至 120 个字符的项目关键词。");
        var profile = Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && url.Scheme is "https" or "http" ? url.AbsolutePath.Trim('/') : "";
        var address = RepositoryLookup.Address(text);
        if (address.Length > 0)
        {
            var exact = new RankingSearchStep("is:public repo:" + address, "正在查找指定仓库…", OptionalScope: profile.Length == 0);
            if (profile.Length > 0) return [exact];
            var parts = address.Split('/', 2);
            var names = Terms(parts[1]);
            return names.Length == 0 ? [exact] : [exact, new(KeywordQuery(names, "is:public user:" + parts[0]), "正在按作者和仓库名片段补搜…")];
        }

        if (text.StartsWith('@') || text.StartsWith("user:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("org:", StringComparison.OrdinalIgnoreCase) || Login.IsMatch(profile))
        {
            var organization = text.StartsWith("org:", StringComparison.OrdinalIgnoreCase);
            var login = profile.Length > 0 ? profile : text.StartsWith('@') ? text[1..] : text[(text.IndexOf(':') + 1)..];
            if (!Login.IsMatch(login)) throw new IOException("请输入有效的 GitHub 作者或组织账号。");
            return [new($"is:public {(organization ? "org" : "user")}:{login}", "正在搜索作者或组织的公开仓库…")];
        }

        var terms = Terms(text);
        if (terms.Length == 0) throw new IOException("请输入项目名称、关键词或 GitHub 地址。");
        var steps = new List<RankingSearchStep> { new(KeywordQuery(terms), "正在搜索仓库名称、简介和主题…") };
        if (Login.IsMatch(text)) steps.Add(new("is:public user:" + text, "正在按作者账号补搜…", OptionalScope: true));
        if (terms.Length > 1)
        {
            steps.Add(new(KeywordQuery([string.Concat(terms)]), "正在按连写的项目名称补搜…"));
            var mainTerm = terms.MaxBy(term => term.Length)!;
            if (mainTerm.Length >= 3) steps.Add(new(KeywordQuery([mainTerm]), "正在扩大关键词范围…"));
        }
        return steps;
    }

    private static string[] Terms(string text)
    {
        var terms = SearchTerm.Matches(text).Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (terms.Length > 15) throw new IOException("搜索关键词过多，请精简后重试。");
        return terms;
    }

    private static string KeywordQuery(IEnumerable<string> terms, string scope = "is:public") =>
        $"{scope} {string.Join(" ", terms.Select(term => $"\"{term}\""))} in:name,description,topics";
    public static List<RankingEntry> ParseTrending(string html)
    {
        if(html.Length>2*1024*1024)throw new IOException("趋势页面过大，未解析。");
        var timeout=TimeSpan.FromSeconds(2);var flags=RegexOptions.Singleline|RegexOptions.IgnoreCase;
        string Match(string value,string pattern)=>Regex.Match(value,pattern,flags,timeout).Groups[1].Value;
        string Text(string value)=>Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value,"<[^>]*>"," ",flags,timeout)),@"\s+"," ").Trim();
        long? Number(string value)=>long.TryParse(Text(value),NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var number)?number:null;
        var items=new List<RankingEntry>();
        try{foreach(Match article in Regex.Matches(html,"<article\\b[^>]*class=[\"'][^\"']*Box-row[^\"']*[\"'][^>]*>(.*?)</article>",flags,timeout)){
            var body=article.Groups[1].Value;var heading=Match(body,"<h2\\b[^>]*>(.*?)</h2>");var repo=Match(heading,"href=[\"']/([^\"'?#]+)[\"']");if(!Rules.ValidRepository(repo)||items.Any(item=>item.Repository.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase)))continue;
            var description=Text(Match(body,"<p\\b[^>]*>(.*?)</p>"));var language=Text(Match(body,"<span\\b[^>]*itemprop=[\"']programmingLanguage[\"'][^>]*>(.*?)</span>"));
            var stars=Number(Match(body,"<a\\b[^>]*href=[\"']/"+Regex.Escape(repo)+"/stargazers[\"'][^>]*>(.*?)</a>"));var gains=Match(body,"<span\\b[^>]*class=[\"'][^\"']*float-sm-right[^\"']*[\"'][^>]*>(.*?)</span>");var gained=Number(Match(Text(gains),@"([\d,]+)\s+stars?\s+(?:today|this week|this month)"));
            items.Add(new(new(repo,description,false){Metadata=new(stars,"",null)},items.Count+1,language,gained));
        }}catch(RegexMatchTimeoutException error){throw new IOException("趋势页面结构无法及时解析，请在浏览器查看。",error);}
        if(items.Count==0)throw new IOException("未能识别 GitHub 趋势榜内容，已保留上次结果，可打开榜单网页查看。");
        return items;
    }
}
