using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;

internal static class SourceSyncChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var ct=CancellationToken.None;var git=new GitWorkspace("");var parent=Path.Combine(root,"sync-checks");Directory.CreateDirectory(parent);var remote=Path.Combine(parent,"remote.git");await git.Run(parent,ct,"init","--bare","-b","main",remote);
        async Task Init(string folder){Directory.CreateDirectory(folder);await git.Run(folder,ct,"init","-b","main");await git.Run(folder,ct,"config","user.name","Fixture");await git.Run(folder,ct,"config","user.email","fixture@example.invalid");await git.Run(folder,ct,"remote","add","origin","https://github.com/example/sync.git");}
        async Task Commit(string folder,string name,string content){await File.WriteAllTextAsync(Path.Combine(folder,name),content);await git.Run(folder,ct,"add","--",name);await git.Run(folder,ct,"commit","-m",name);}
        async Task Push(string folder)=>await git.Run(folder,ct,"push",remote,"HEAD:refs/heads/main");
        async Task<string> RemoteHead()=>await git.Run(parent,ct,"--git-dir="+remote,"rev-parse","refs/heads/main");
        var local=Path.Combine(parent,"local");await Init(local);await Commit(local,"base.txt","base");await Push(local);
        var same=await git.CheckSyncFrom(local,remote,"main",ct);check(same.Relation==SourceRelation.Same&&same.Clean,"sync check distinguishes identical local and remote history");
        await Commit(local,"local.txt","first");var ahead=await git.CheckSyncFrom(local,remote,"main",ct);check(ahead.Relation==SourceRelation.LocalAhead&&ahead.LocalOnly==1,"sync check recognizes normal pushable local commits");await Push(local);
        var other=Path.Combine(parent,"other");await git.Run(parent,ct,"clone",remote,other);await git.Run(other,ct,"config","user.name","Fixture");await git.Run(other,ct,"config","user.email","fixture@example.invalid");await Commit(other,"remote.txt","remote");await Push(other);
        var behind=await git.CheckSyncFrom(local,remote,"main",ct);check(behind.Relation==SourceRelation.RemoteAhead&&behind.RemoteOnly==1,"sync check identifies remote work missing locally");
        await File.WriteAllTextAsync(Path.Combine(local,"dirty.txt"),"keep");var blocked=false;try{await git.FastForwardSync(local,behind,ct);}catch(IOException){blocked=true;}check(blocked&&File.ReadAllText(Path.Combine(local,"dirty.txt"))=="keep","fast-forward refuses uncommitted work without stashing or deleting it");File.Delete(Path.Combine(local,"dirty.txt"));
        await git.FastForwardSync(local,behind,ct);check((await git.Inspect(local,ct)).Commit==behind.Remote&&File.Exists(Path.Combine(local,"remote.txt")),"safe remote synchronization advances only a clean behind branch");
        await Commit(local,"a.txt","local divergent");await Commit(other,"b.txt","remote divergent");await Push(other);var divergent=await git.CheckSyncFrom(local,remote,"main",ct);check(divergent.Relation==SourceRelation.Diverged&&divergent.LocalOnly==1&&divergent.RemoteOnly==1,"related divergent histories are not treated as unrelated roots");
        await Commit(other,"newer.txt","new concurrent remote work");await Push(other);var newer=await RemoteHead();blocked=false;try{await git.ForceUpdateRemoteFrom(local,remote,divergent,ct);}catch(IOException){blocked=true;}check(blocked&&await RemoteHead()==newer,"force-with-lease rejects a remote commit added after the user's review");
        var fresh=await git.CheckSyncFrom(local,remote,"main",ct);await File.WriteAllTextAsync(Path.Combine(local,"dirty.txt"),"keep");blocked=false;try{await git.ForceUpdateRemoteFrom(local,remote,fresh,ct);}catch(IOException){blocked=true;}check(blocked&&await RemoteHead()==newer,"forced remote update refuses changed local working files");File.Delete(Path.Combine(local,"dirty.txt"));
        await git.Run(local,ct,"config","--local","push.followTags","true");await git.Run(local,ct,"tag","-a","local-only","-m","must not upload");
        await git.Run(other,ct,"push",remote,"HEAD:refs/heads/keep","HEAD:refs/tags/keep");var backup=await git.ForceUpdateRemoteFrom(local,remote,fresh,ct);check(await RemoteHead()==fresh.Local&&await git.Run(local,ct,"rev-parse",backup)==newer,"reviewed force update verifies the remote result and preserves its original head locally");
        var tagNotUploaded=false;try{await git.Run(parent,ct,"--git-dir="+remote,"rev-parse","--verify","refs/tags/local-only");}catch(IOException){tagNotUploaded=true;}check(tagNotUploaded,"force update does not upload unselected tags even when push.followTags is enabled");
        check(await git.Run(parent,ct,"--git-dir="+remote,"rev-parse","refs/heads/keep")==newer&&await git.Run(parent,ct,"--git-dir="+remote,"rev-parse","refs/tags/keep")==newer,"forced update changes only the reviewed remote branch and leaves other branches and tags intact");
        var independent=Path.Combine(parent,"independent");await Init(independent);await Commit(independent,"source.txt","independent local source");var unrelated=await git.CheckSyncFrom(independent,remote,"main",ct);check(unrelated.Relation==SourceRelation.Unrelated&&unrelated.CanRepairHistory,"sync check detects independently initialized repositories like PureRDP");
        var oldTree=await git.Run(independent,ct,"rev-parse","HEAD^{tree}");var originalHead=unrelated.Local;var remoteBefore=await RemoteHead();var localBackup=await git.RepairHistory(independent,unrelated,ct);
        check(await git.Run(independent,ct,"rev-parse","HEAD^{tree}")==oldTree&&(await git.Inspect(independent,ct)).Changes.Count==0,"local history repair preserves the exact local tree and clean working files");
        check(await git.Run(independent,ct,"rev-parse","HEAD^")==unrelated.Remote&&await git.Run(independent,ct,"rev-parse",localBackup)==originalHead&&await RemoteHead()==remoteBefore,"history repair attaches to the remote parent while preserving original local history and not pushing");
        var repaired=await git.CheckSyncFrom(independent,remote,"main",ct);check(repaired.Relation==SourceRelation.LocalAhead&&repaired.LocalOnly==1,"repaired independent history becomes normally pushable without force");
        blocked=false;try{await git.RepairHistory(independent,unrelated,ct);}catch(IOException){blocked=true;}check(blocked,"stale history repair review cannot replace a newly changed branch");
        var empty=Path.Combine(parent,"empty.git");await git.Run(parent,ct,"init","--bare","-b","main",empty);check((await git.CheckSyncFrom(independent,empty,"main",ct)).Relation==SourceRelation.NewRemote,"missing remote branch is distinguished from an unrelated existing repository");
        var text="推送已停止\r\nraw details\n"+new string('x',400);check(StatusText.Summary(text)=="推送已停止"&&!StatusText.Summary(text).Contains('\n'),"status summary is one line while original multi-line details remain available");check(StatusText.Summary(new string('x',400)).Length==181&&StatusText.Summary("\r\n")=="暂无消息","status summary bounds long text and handles empty messages");
        var model=new MainModel();var notified=false;model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MainModel.StatusSummary))notified=true;};model.Status=text;check(notified&&model.Status==text&&model.StatusSummary=="推送已停止","status summary change notification preserves complete original error details");
    }
}
