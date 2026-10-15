using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;

internal static class FollowHistoryChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var settings=new Settings{Account="alice",HiddenRepositories=["example/editor","example/legacy"],LocalGroups=[new("tools","工具","本地",["example/editor"])]};
        var cloud=new StarGroup("cloud","开发","GitHub",["example/editor"]);var entry=FollowHistory.Record(settings,"https://github.com/example/editor",FollowKind.GitHubStar,settings.LocalGroups.Append(cloud),now);
        check(entry.Repository=="example/editor"&&entry.Kind==FollowKind.GitHubStar&&entry.RemovedAt==now&&entry.Groups.Count==2,"unfollow history captures exact source time account and group membership");
        settings.LocalGroups[0].Repositories.Clear();check(entry.Groups.Count==2&&entry.Groups[0].Id=="tools","history snapshots survive later membership removal");
        var legacy=FollowHistory.Entries(settings).Single(e=>e.Repository=="example/legacy");check(legacy.RemovedAt==null&&legacy.Kind==FollowKind.Unknown,"legacy hidden projects have honest unknown timestamps and source");
        settings.FollowHistory.Add(new("foreign","private/secret",FollowKind.Local,now,"bob",[]));check(!FollowHistory.Entries(settings).Any(e=>e.Account=="bob"),"history view excludes other account records");
        var blocked=false;try{FollowHistory.RestoreLocal(new Settings{Account="bob"},entry,now);}catch(IOException){blocked=true;}check(blocked,"restoration cannot cross accounts");
        FollowHistory.RestoreLocal(settings,entry,now.AddMinutes(1));check(settings.ExtraRepositories.Contains("example/editor")&&!settings.HiddenRepositories.Contains("example/editor")&&settings.LocalGroups[0].Repositories.Contains("example/editor"),"local restoration restores follow and surviving local groups");
        check(!settings.LocalGroups.Any(g=>g.Source=="GitHub")&&settings.FollowHistory[0].RestoredAs==FollowKind.Local,"restoring as local never manufactures GitHub groups or remote Star state");
        FollowHistory.RestoreLocal(settings,entry,now.AddMinutes(2));check(settings.ExtraRepositories.Count(r=>r=="example/editor")==1&&settings.LocalGroups[0].Repositories.Count==1,"repeated restoration deduplicates local follow and group membership");
        var precedence=FollowLists.Local([new("example/editor","",false,true)],settings.ExtraRepositories,settings.HiddenRepositories);check(!precedence.Contains("example/editor"),"restored local backup remains subordinate to an existing GitHub Star");
        FollowHistory.RestoreLocal(settings,legacy,now);check(settings.FollowHistory.Any(e=>e.Id==legacy.Id&&e.RemovedAt==null&&e.RestoredAt==now),"legacy recovery retains unknown cancellation time after saving");
        var missing=FollowHistory.Record(settings,"example/missing",FollowKind.Local,[new("deleted","旧分组","本地",["example/missing"])],now);FollowHistory.RestoreLocal(settings,missing,now);check(!settings.LocalGroups.Any(g=>g.Id=="deleted"),"history restore does not recreate deleted groups");
        var root=Path.Combine(Path.GetTempPath(),"Gitstar-follow-history-"+Guid.NewGuid().ToString("N"));try{using(var store=new LocalStore(root))store.Set("settings",settings);using(var store=new LocalStore(root)){var saved=store.Get("settings",new Settings());check(saved.FollowHistory.Any(e=>e.Id==entry.Id&&e.RestoredAs==FollowKind.Local)&&saved.ExtraRepositories.Contains("example/editor"),"history and restoration result persist across store reopening");}}finally{Directory.Delete(root,true);}
        var bounded=new Settings{Account="alice"};for(var i=0;i<FollowHistory.Limit+3;i++)FollowHistory.Record(bounded,"example/repo"+i,FollowKind.Local,[],now.AddSeconds(i));check(bounded.FollowHistory.Count==FollowHistory.Limit&&bounded.FollowHistory[0].Repository=="example/repo502","history keeps the most recent bounded cancellation records");
        var exported=FollowLists.Export(settings.ExtraRepositories);check(!exported.Contains("RemovedAt")&&!exported.Contains("alice")&&!exported.Contains("private/secret"),"portable follow exports exclude account history and private metadata");
    }
}
