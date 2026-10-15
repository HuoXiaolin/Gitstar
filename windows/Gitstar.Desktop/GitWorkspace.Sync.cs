namespace Gitstar.Desktop;

internal sealed class GitCommandException(int exitCode,string message):IOException(message)
{
    public int ExitCode {get;}=exitCode;
}

public sealed partial class GitWorkspace
{
    private const string SyncRef="refs/gitstar/sync-check";
    public async Task<SourceSyncState> CheckSync(string folder,string repo,string branch,CancellationToken ct)
    {
        GitHubClient.Validate(repo);
        var state=await Inspect(folder,ct);if(!ProjectRules.MatchesRemote(state.Remote,repo)||state.Branch!=branch)throw new IOException("源码目录、分支或 origin 与项目不一致。");
        return await CheckSyncFrom(folder,"https://github.com/"+repo+".git",branch,ct);
    }
    internal async Task<SourceSyncState> CheckSyncFrom(string folder,string remote,string branch,CancellationToken ct)
    {
        await Run(folder,ct,"check-ref-format","--branch",branch);var state=await Inspect(folder,ct);if(state.Branch!=branch||state.Commit.Length==0)throw new IOException("请先在目标分支创建本地提交，再核对同步。");
        var tree=await Run(folder,ct,"rev-parse","HEAD^{tree}");var found=await Run(folder,ct,"ls-remote","--heads",remote,"refs/heads/"+branch);
        if(found.Length==0)return new(branch,state.Commit,"",tree,"",SourceRelation.NewRemote,0,0,state.Changes.Count==0,"");
        await Run(folder,ct,"fetch","--no-tags","--no-recurse-submodules",remote,"+refs/heads/"+branch+":"+SyncRef);
        var remoteCommit=await Run(folder,ct,"rev-parse",SyncRef);var remoteTree=await Run(folder,ct,"rev-parse",SyncRef+"^{tree}");var counts=(await Run(folder,ct,"rev-list","--left-right","--count","HEAD..."+SyncRef)).Split(['\t',' '],StringSplitOptions.RemoveEmptyEntries);
        var localCount=int.Parse(counts[0]);var remoteCount=int.Parse(counts[1]);var relation=localCount==0&&remoteCount==0?SourceRelation.Same:remoteCount==0?SourceRelation.LocalAhead:localCount==0?SourceRelation.RemoteAhead:SourceRelation.Diverged;
        if(relation==SourceRelation.Diverged){try{await Run(folder,ct,"merge-base","HEAD",SyncRef);}catch(GitCommandException ex)when(ex.ExitCode==1){relation=SourceRelation.Unrelated;}}
        return new(branch,state.Commit,remoteCommit,tree,remoteTree,relation,localCount,remoteCount,state.Changes.Count==0,await Run(folder,ct,"diff","--stat",SyncRef,"HEAD","--"));
    }
    internal async Task<string> RepairHistory(string folder,SourceSyncState expected,CancellationToken ct)
    {
        if(!expected.CanRepairHistory)throw new IOException("仅在工作区干净且两边历史独立时，才可修复提交历史。");
        var current=await Inspect(folder,ct);var remote=await Run(folder,ct,"rev-parse",SyncRef);
        if(current.Branch!=expected.Branch||current.Commit!=expected.Local||current.Changes.Count>0||remote!=expected.Remote||await Run(folder,ct,"rev-parse","HEAD^{tree}")!=expected.LocalTree)throw new IOException("源码或同步核对结果已变化，请重新核对。");
        var backup="gitstar-backup/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];await Run(folder,ct,"branch",backup,expected.Local);
        // Keep exactly the reviewed local file tree; only attach its new commit to the remote history.
        var commit=await Run(folder,ct,"commit-tree",expected.LocalTree,"-p",expected.Remote,"-m","同步源码历史：保留本地源码快照");
        await Run(folder,ct,"update-ref","refs/heads/"+expected.Branch,commit,expected.Local);return backup;
    }
    internal async Task FastForwardSync(string folder,SourceSyncState expected,CancellationToken ct)
    {
        var current=await Inspect(folder,ct);if(expected.Relation!=SourceRelation.RemoteAhead||!expected.Clean||current.Commit!=expected.Local||current.Branch!=expected.Branch||current.Changes.Count>0||await Run(folder,ct,"rev-parse",SyncRef)!=expected.Remote)throw new IOException("源码或同步状态已变化，请重新核对。");
        await Run(folder,ct,"merge","--ff-only",SyncRef);
    }
    public async Task<string> ForceUpdateRemote(string folder,string repo,SourceSyncState expected,CancellationToken ct)
    {
        GitHubClient.Validate(repo);var state=await Inspect(folder,ct);if(!ProjectRules.MatchesRemote(state.Remote,repo))throw new IOException("origin 与目标仓库不一致。");
        return await ForceUpdateRemoteFrom(folder,"https://github.com/"+repo+".git",expected,ct);
    }
    internal async Task<string> ForceUpdateRemoteFrom(string folder,string remote,SourceSyncState expected,CancellationToken ct)
    {
        var state=await Inspect(folder,ct);
        if(!expected.Clean||expected.Remote.Length==0||state.Changes.Count>0||state.Commit!=expected.Local||state.Branch!=expected.Branch||await Run(folder,ct,"rev-parse",SyncRef)!=expected.Remote)throw new IOException("本地源码或核对结果已变化，请重新核对，未强制推送。");
        var backup="gitstar-remote-backup/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];await Run(folder,ct,"branch",backup,expected.Remote);
        await Run(folder,ct,"push","--no-follow-tags","--recurse-submodules=no","--force-with-lease=refs/heads/"+expected.Branch+":"+expected.Remote,remote,expected.Local+":refs/heads/"+expected.Branch);
        var result=await Run(folder,ct,"ls-remote","--heads",remote,"refs/heads/"+expected.Branch);if(!result.StartsWith(expected.Local+"\t",StringComparison.Ordinal))throw new IOException("推送命令已返回，但远端提交未确认，请重新核对。");
        return backup;
    }
}
