namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    internal static RepositoryMetadata ParseMetadata(JsonElement e)
    {
        long? stars=e.TryGetProperty("stargazers_count",out var count)&&count.ValueKind==JsonValueKind.Number&&count.TryGetInt64(out var number)&&number>=0?number:null;
        var license="";
        if(e.TryGetProperty("license",out var value)&&value.ValueKind==JsonValueKind.Object){
            license=S(value,"spdx_id");if(license.Length==0||license=="NOASSERTION")license=S(value,"name");
        }
        return new(stars,license,DateTimeOffset.TryParse(S(e,"updated_at"),out var updated)?updated:null);
    }
}
