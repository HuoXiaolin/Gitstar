namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    internal bool? CanDeleteRepositories=>oauthScopes!=null?oauthScopes.Contains("delete_repo",StringComparer.OrdinalIgnoreCase):credential?.Scope is {Length:>0} scope?scope.Split([',',' '],StringSplitOptions.RemoveEmptyEntries).Contains("delete_repo",StringComparer.OrdinalIgnoreCase):null;
    public async Task DeleteRepository(ManagedProject expected,string confirmation,CancellationToken ct)
    {
        if(!confirmation.Trim().Equals(expected.FullName,StringComparison.OrdinalIgnoreCase))throw new IOException("请输入完整仓库名称确认删除。");
        if(Token.Length==0)throw new IOException("请先登录 GitHub。");
        var current=await Project(expected.FullName,ct);
        if(!ProjectRules.SameRepository(expected,current))throw new IOException("仓库身份已改变，请重新同步项目并确认。");
        if(!current.CanAdmin)throw new IOException("当前账号没有此仓库的管理员权限，不能删除。");
        if(CanDeleteRepositories==false)throw new IOException("当前授权缺少 delete_repo 删除权限，请在设置中重新授权 GitHub 后再删除。");
        await Request("repos/"+current.FullName,HttpMethod.Delete,ct:ct);
        try{await Project(current.FullName,ct);}catch(GitHubNotFoundException){return;}
        throw new IOException("删除结果尚未确认，或已出现同名仓库；请同步项目核实，勿直接重试。");
    }
}
