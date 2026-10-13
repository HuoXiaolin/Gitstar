namespace Gitstar.Core;

public record InstalledSnapshot(DateTimeOffset CheckedAt,List<InstalledApp> Apps)
{
    public static InstalledSnapshot Capture(IEnumerable<InstalledApp> apps,IEnumerable<Binding> bindings)
    {
        var ids=bindings.Select(b=>b.AppId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(DateTimeOffset.UtcNow,apps.Where(a=>ids.Contains(a.Id) || (a.AliasIds??[]).Any(ids.Contains)).ToList());
    }
}
