namespace Gitstar.Core;

public enum FollowKind { Unknown,GitHubStar,Local }
public record FollowGroupSnapshot(string Id,string Name,string Source);
public record FollowHistoryEntry(string Id,string Repository,FollowKind Kind,DateTimeOffset? RemovedAt,string Account,List<FollowGroupSnapshot> Groups)
{
    public DateTimeOffset? RestoredAt {get;init;}
    public FollowKind? RestoredAs {get;init;}
    public string Note {get;init;}="";
    public string SourceLabel=>Kind switch{FollowKind.GitHubStar=>"GitHub Star",FollowKind.Local=>"本地关注",_=>"旧版隐藏记录"};
}
public static class FollowHistory
{
    public const int Limit=500;
    public static FollowHistoryEntry Record(Settings settings,string repo,FollowKind kind,IEnumerable<StarGroup> groups,DateTimeOffset now)
    {
        var name=Rules.RepositoryName(repo);if(!Rules.ValidRepository(name)||kind==FollowKind.Unknown)throw new ArgumentException("取消关注记录无效。");
        var entry=new FollowHistoryEntry(Guid.NewGuid().ToString("N"),name,kind,now,settings.Account,groups.Where(g=>g.Repositories.Contains(name,StringComparer.OrdinalIgnoreCase)).Select(g=>new FollowGroupSnapshot(g.Id,g.Name,g.Source)).Distinct().ToList());
        settings.FollowHistory.Insert(0,entry);Trim(settings);return entry;
    }
    public static FollowHistoryEntry[] Entries(Settings settings)
    {
        var current=settings.FollowHistory.Where(e=>e.Account==settings.Account).ToArray();var known=current.Select(e=>e.Repository).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return current.Concat(settings.HiddenRepositories.Where(Rules.ValidRepository).Distinct(StringComparer.OrdinalIgnoreCase).Where(repo=>!known.Contains(repo)).Select(repo=>new FollowHistoryEntry("legacy/"+repo.ToLowerInvariant(),repo,FollowKind.Unknown,null,settings.Account,[]))).OrderByDescending(e=>e.RemovedAt??DateTimeOffset.MinValue).ToArray();
    }
    public static void MarkRestored(Settings settings,FollowHistoryEntry entry,FollowKind asKind,DateTimeOffset now,string note="")
    {
        if(entry.Account!=settings.Account||asKind==FollowKind.Unknown)throw new IOException("账号已改变，请重新打开取消关注历史。");
        var updated=entry with{RestoredAt=now,RestoredAs=asKind,Note=note};var index=settings.FollowHistory.FindIndex(e=>e.Id==entry.Id);
        if(index>=0)settings.FollowHistory[index]=updated;else settings.FollowHistory.Insert(0,updated);Trim(settings);
    }
    public static void RestoreLocal(Settings settings,FollowHistoryEntry entry,DateTimeOffset now)
    {
        if(entry.Account!=settings.Account||!Rules.ValidRepository(entry.Repository))throw new IOException("历史账号或仓库无效。");
        if(!settings.ExtraRepositories.Contains(entry.Repository,StringComparer.OrdinalIgnoreCase))settings.ExtraRepositories.Add(entry.Repository);
        settings.HiddenRepositories.RemoveAll(r=>r.Equals(entry.Repository,StringComparison.OrdinalIgnoreCase));
        foreach(var snapshot in entry.Groups.Where(g=>g.Source!="GitHub")){
            var group=settings.LocalGroups.FirstOrDefault(g=>g.Id==snapshot.Id&&g.Source==snapshot.Source);if(group!=null&&!group.Repositories.Contains(entry.Repository,StringComparer.OrdinalIgnoreCase))group.Repositories.Add(entry.Repository);
        }
        MarkRestored(settings,entry,FollowKind.Local,now);
    }
    private static void Trim(Settings settings){if(settings.FollowHistory.Count>Limit)settings.FollowHistory.RemoveRange(Limit,settings.FollowHistory.Count-Limit);}
}
