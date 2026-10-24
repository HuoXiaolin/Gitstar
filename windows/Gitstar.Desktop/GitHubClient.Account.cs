namespace Gitstar.Desktop;

public record GitHubAccountInformation(string Login,string Name,string Bio,string Type,int PublicRepositories,int Followers);
public sealed partial class GitHubClient
{
    public async Task<GitHubAccountInformation> AccountInformation(CancellationToken ct)
    {
        var data=await Request("user",ct:ct);
        int Count(string key)=>data.TryGetProperty(key,out var value)&&value.TryGetInt32(out var count)?count:0;
        return new(S(data,"login"),S(data,"name"),S(data,"bio"),S(data,"type"),Count("public_repos"),Count("followers"));
    }
}
