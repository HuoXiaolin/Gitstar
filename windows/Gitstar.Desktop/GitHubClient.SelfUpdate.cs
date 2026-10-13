namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    public async Task<Release> LatestGitstarRelease(CancellationToken ct)
    {
        await EnsureFreshToken(ct);var data=await Request("repos/"+AppInfo.Repository+"/releases/latest",ct:ct,fresh:true);
        if(data.GetProperty("draft").GetBoolean()||data.GetProperty("prerelease").GetBoolean())throw new IOException("GitHub 返回的版本不是已发布正式版，请到项目页面核实。");
        return ParseRelease(AppInfo.Repository,data);
    }
}
