using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
namespace Gitstar.Core;
public enum RankingKind { Trending,New,Popular,Search }
public record RankingEntry(Repository Repository,int Position,string Language="",long? GainedStars=null,DateTimeOffset? Created=null);
public record RankingPage(RankingKind Kind,int Period,int Page,DateTimeOffset Fetched,List<RankingEntry> Items,int Total,bool More,bool Incomplete=false,long StarsFloor=0);
public static class RepositoryRankings
{
    public static string Period(int index)=>index switch{0=>"daily",1=>"weekly",2=>"monthly",_=>throw new IOException("无效的榜单周期。")};
    public static int NewDays(int index)=>index switch{0=>7,1=>30,2=>90,_=>throw new IOException("无效的新项目周期。")};
    public static string SearchQuery(RankingKind kind,int period,string text,DateTimeOffset now)
    {
        var query="is:public archived:false fork:false";
        if(kind==RankingKind.New)return query+" created:>="+now.UtcDateTime.Date.AddDays(-NewDays(period)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(kind==RankingKind.Popular)return query;
        if(kind!=RankingKind.Search)throw new IOException("该榜单使用 GitHub 趋势页。");
        query="is:public";
        text=text.Trim();if(text.Length<2||text.Length>120)throw new IOException("请输入 2 至 120 个字符的项目关键词。");
        var repo=Rules.RepositoryName(text);if(Rules.ValidRepository(repo))return query+" repo:"+repo;
        var terms=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(terms.Length>15)throw new IOException("搜索关键词过多，请精简后重试。");
        return query+" "+string.Join(" ",terms.Select(term=>"\""+term.Replace("\"","")+"\""))+" in:name,description";
    }
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
