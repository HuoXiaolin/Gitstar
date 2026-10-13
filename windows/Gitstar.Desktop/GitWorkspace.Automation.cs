namespace Gitstar.Desktop;

public sealed partial class GitWorkspace
{
    public async Task<GitProjectState> AutoUpload(string folder,string repo,string branch,CancellationToken ct)
    {
        var state=await AutoCommit(folder,repo,branch,ct);await Push(folder,repo,state.Branch,ct);return await Inspect(folder,ct);
    }
    public async Task<GitProjectState> AutoCommit(string folder,string repo,string branch,CancellationToken ct)
    {
        var state=await Inspect(folder,ct);if(!ProjectRules.MatchesRemote(state.Remote,repo))throw new IOException("源码目录 origin 与项目不一致，请重新关联。");
        if(state.Branch=="分离 HEAD"||branch.Length>0&&state.Branch!=branch)throw new IOException("当前分支与自动发布配置不一致。");
        if(state.Changes.Any(change=>change.StatusName=="冲突"))throw new IOException("源码存在合并冲突，无法自动提交。");
        await Run(folder,ct,"add","-A","--",".");
        if((await Run(folder,ct,"diff","--cached","--name-only")).Length>0)await Run(folder,ct,"commit","-m",SourceWorkflow.CommitMessage(state.Changes));
        return await Inspect(folder,ct);
    }
}
