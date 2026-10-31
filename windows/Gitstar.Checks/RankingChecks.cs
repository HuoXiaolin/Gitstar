using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
internal static class RankingChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        string Article(string repo,string intro="A useful &amp; simple project",string stars="1,234",string gain="23")=>$"<article class='Box-row'><a href='/login'>Star</a><h2><a href='/{repo}'>repo</a></h2><p>{intro}</p><span itemprop='programmingLanguage'>C#</span><a href='/{repo}/stargazers'>{stars}</a><span class='d-inline-block float-sm-right'>{gain} stars today</span></article>";
        var html=Article("example/editor")+Article("other/tool",stars:"9,999")+Article("example/editor")+Article("../login");var entries=RepositoryRankings.ParseTrending(html);
        check(entries.Count==2&&entries[0].Repository.FullName=="example/editor"&&entries[1].Position==2,"trending parser preserves official order while excluding duplicates and invalid repository paths");
        check(entries[0].Repository.Description=="A useful & simple project"&&entries[0].Language=="C#","ranking cards receive plain decoded project introductions and language");
        check(entries[0].Repository.Metadata.Stars==1234&&entries[0].GainedStars==23,"trending distinguishes accumulated stars from period gains");
        check(RepositoryRankings.ParseTrending(Article("example/editor").Replace("stars today","stars this month"))[0].GainedStars==23,"trending monthly gain is recognized independently of the daily label");
        check(RepositoryRankings.ParseTrending(Article("example/editor","text says 900 stars today",gain:"7"))[0].GainedStars==7,"introduction text cannot replace the period stars field");
        var rejected=false;try{RepositoryRankings.ParseTrending("<html>Sign in</html>");}catch(IOException){rejected=true;}check(rejected,"unexpected trending markup fails explicitly instead of overwriting cache with an empty list");
        var now=new DateTimeOffset(2026,10,29,12,0,0,TimeSpan.Zero);check(RepositoryRankings.SearchQuery(RankingKind.New,1,"",now).Contains("created:>=2026-09-29"),"new projects ranking uses a thirty-day creation window by default");
        check(!RepositoryRankings.SearchQuery(RankingKind.Popular,0,"",now).Contains("created:"),"popular ranking uses lifetime stars without a creation window");
        check(RepositoryRankings.SearchQuery(RankingKind.Search,0,"https://github.com/example/editor",now).Contains("repo:example/editor"),"ranking search accepts full GitHub repository URLs");
        var calls=new List<(string Path,string Query,HttpMethod Method,bool Auth)>();var total=30;
        object Repo(string name,bool secret=false)=>new{full_name=name,description="Repository introduction, not release notes",@private=secret,stargazers_count=200000,language="C#",created_at=DateTimeOffset.UtcNow.AddDays(-1).ToString("O")};
        using var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{calls.Add((request.RequestUri!.AbsolutePath,Uri.UnescapeDataString(request.RequestUri.Query),request.Method,request.Headers.Authorization!=null));return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri.Host=="github.com"?html:JsonSerializer.Serialize(new{total_count=total,incomplete_results=false,items=new[]{Repo("example/editor"),Repo("private/secret",true)}}))});})){Token="fixture"};
        var result=await api.Rankings(RankingKind.Trending,0,1,"",CancellationToken.None);check(result.Items.Count==2&&!calls.Last().Auth&&calls.Last().Query.Contains("since=daily"),"official trending requests are anonymous even when the app is signed in");
        result=await api.Rankings(RankingKind.New,1,1,"",CancellationToken.None);check(calls.Last().Query.Contains("created:>=")&&calls.Last().Query.Contains("sort=stars")&&result.Items.Count==1,"new ranking sorts by stars and does not expose private results");
        check(result.Items[0].Created!=null&&result.Items[0].Repository.Description.StartsWith("Repository introduction")&&result.More,"ranking API retains introductions and creation date with pagination");
        result=await api.Rankings(RankingKind.Popular,0,2,"",CancellationToken.None);check(result.Items[0].Position==26&&!result.More&&calls.Last().Query.Contains("page=2"),"popular pagination uses stable ordinal offsets and total count");
        await api.Rankings(RankingKind.Search,0,1,"editor tools",CancellationToken.None);check(calls.Last().Query.Contains("in:name,description")&&!calls.Last().Query.Contains("sort=stars"),"global project search uses relevance and keyword matching rather than presenting a fake trending ranking");
        await api.Rankings(RankingKind.Search,2,2,"outside ranking",CancellationToken.None);var global=calls.Last();check(global.Path=="/search/repositories"&&global.Query.Contains("page=2")&&!new[]{"stars:","created:","archived:","fork:false"}.Any(global.Query.Contains),"GitHub search fetches remote paginated results independently of ranking period, stars, archive and fork filters");
        total=2000;result=await api.Rankings(RankingKind.Popular,0,40,"",CancellationToken.None);check(!result.More&&result.Total==2000,"ranking pagination stops at GitHub's first one thousand searchable results");
        var before=calls.Count;rejected=false;try{await api.Rankings(RankingKind.Popular,0,41,"",CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&calls.Count==before,"invalid ranking pages fail before network requests");
        check(calls.All(call=>call.Method==HttpMethod.Get&&!call.Path.Contains("releases")),"ranking browsing performs only reads and never fetches release changelogs");
        var ranges=new List<string>();using var adaptive=new GitHubClient(new(),new FakeHttp((request,ct)=>{var query=Uri.UnescapeDataString(request.RequestUri!.Query);ranges.Add(query);var narrow=query.Contains("stars:>=100000");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{total_count=narrow?30:60,incomplete_results=false,items=Enumerable.Range(0,25).Select(index=>new{full_name="example/project"+index,description="intro",@private=false,stargazers_count=narrow?150000:80000})}))});}));
        var expanded=await adaptive.Rankings(RankingKind.Popular,0,2,"",CancellationToken.None);check(ranges.Count==2&&ranges.All(query=>query.Contains("page=2"))&&expanded.Items[0].Position==26&&expanded.Items.Count==25&&expanded.StarsFloor==50000,"star-range expansion refetches the same page without skipping boundary ranks");
        check(ranges.All(query=>System.Text.RegularExpressions.Regex.Matches(query,"stars:").Count==1),"ranking requests use one stars qualifier so broad and narrow ranges cannot be combined incorrectly");
    }
}
