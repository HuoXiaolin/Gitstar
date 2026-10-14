namespace Gitstar.Core;

[Flags]
public enum CacheModule { None=0, RecentUpdates=1, InstalledApps=2, MyProjects=4, Avatars=8, FinishedTasks=16 }
public record CacheKeys(string[] Exact,string[] Prefixes);
public static class CacheRules
{
    public static string Account(string account)=>account.Length>0?account:"guest";
    public static CacheKeys Keys(string account,CacheModule module)
    {
        var root="cache/"+Account(account);
        return module switch{
            CacheModule.RecentUpdates=>new([root+"/repos",root+"/groups",root+"/releases"],[root+"/http-releases/"]),
            CacheModule.InstalledApps=>new([root+"/installed",root+"/matched-releases"],[]),
            CacheModule.MyProjects=>new([root+"/my-projects",root+"/my-projects/versions",root+"/my-projects/release-status",root+"/my-projects/synced"],[]),
            _=>new([],[])
        };
    }
    public static bool Matches(string key,CacheKeys keys)=>keys.Exact.Contains(key,StringComparer.Ordinal)||keys.Prefixes.Any(prefix=>key.StartsWith(prefix,StringComparison.Ordinal));
    public static bool Finished(Operation operation)=>operation.State=="完成"||operation.State.StartsWith("已取消",StringComparison.Ordinal)&&!operation.State.Contains("待核实",StringComparison.Ordinal);
}
