using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

internal static class RepositoryMetadataChecks
{
    public static void Run(Action<bool,string> check)
    {
        RepositoryMetadata Parse(string json){using var doc=JsonDocument.Parse(json);return GitHubClient.ParseMetadata(doc.RootElement);}
        var full=Parse("""{"stargazers_count":12345,"license":{"spdx_id":"GPL-3.0","name":"GNU GPL"},"updated_at":"2026-10-16T01:02:03Z"}""");
        check(full.Stars==12345&&full.StarsLabel=="★ 12,345 Stars","repository star count uses stargazers_count with exact display");
        check(full.License=="GPL-3.0"&&full.Updated==DateTimeOffset.Parse("2026-10-16T01:02:03Z"),"repository license and activity parse API fields");
        var empty=Parse("""{"license":null,"stargazers_count":null}""");check(empty.Stars==null&&empty.License==""&&empty.Updated==null,"missing metadata is unknown rather than invented");
        check(Parse("""{"stargazers_count":0}""").StarsLabel=="★ 0 Stars","zero stars differs from missing count");
        check(Parse("""{"license":{"spdx_id":"NOASSERTION","name":"Other"}}""").LicenseLabel=="Other","unclassified license retains GitHub name");
        check(JsonSerializer.Deserialize<RepositoryMetadata>(JsonSerializer.Serialize(full))==full,"repository metadata survives cache roundtrip");
        check(JsonSerializer.Deserialize<Repository>("""{"FullName":"owner/app","Description":"","Private":false}""")!.Metadata.Stars==null,"old repository cache loads with unknown metadata");
        check(new Card{Release=new(1,"owner/app","v1","","",false,DateTimeOffset.UtcNow,"",[]),Metadata=full}.Metadata==full,"release and local software cards can carry repository metadata");
    }
}
