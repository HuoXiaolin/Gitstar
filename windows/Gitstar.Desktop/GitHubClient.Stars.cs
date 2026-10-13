using System.Net;
using System.Net.Http.Headers;
namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    private void RequireLogin(){if(Token.Length==0)throw new IOException("请先登录 GitHub，再修改 Star 或 GitHub 分组。");}
    public async Task EnsureListWritePermission(CancellationToken ct=default)
    {
        RequireLogin();await EnsureFreshToken(ct);if(oauthScopes==null)await Request("user",ct:ct);
        if(oauthScopes!=null && !oauthScopes.Contains("user",StringComparer.OrdinalIgnoreCase))throw new IOException("当前 GitHub 授权缺少 user 分组写入权限（read:user 只能读取）。请在设置中点击“重新授权 GitHub”，浏览器确认新增权限后，再修改此项目的分组；已有 Star 无需重复添加。");
    }
    public async Task<bool> IsStarred(string repo,CancellationToken ct=default)
    {
        RequireLogin();await EnsureFreshToken(ct);Validate(repo);using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/user/starred/"+repo);
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Token);req.Headers.Accept.ParseAdd("application/vnd.github+json");req.Headers.Add("X-GitHub-Api-Version","2022-11-28");
        using var response=await http.SendAsync(req,ct);if(response.StatusCode==HttpStatusCode.NoContent)return true;if(response.StatusCode==HttpStatusCode.NotFound)return false;
        throw new IOException($"无法确认 Star 状态（HTTP {(int)response.StatusCode}），请检查登录和仓库访问权限。");
    }
    public async Task SetStar(string repo,bool starred,CancellationToken ct=default)
    {
        RequireLogin();Validate(repo);
        try{await Request("user/starred/"+repo,starred?HttpMethod.Put:HttpMethod.Delete,ct:ct);}
        catch(Exception ex)when(ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested){if(await IsStarred(repo,ct)!=starred)throw new IOException("Star 修改结果未确认，请刷新该项目状态后再重试。",ex);}
        if(await IsStarred(repo,ct)!=starred)throw new IOException("GitHub 返回后 Star 状态未确认，请稍后核实。");
    }
    private async Task<JsonElement> Graph(string query,object variables,CancellationToken ct)
    {
        await EnsureListWritePermission(ct);var result=await Request("graphql",HttpMethod.Post,new{query,variables},ct);
        if(result.TryGetProperty("errors",out var errors))throw new IOException("GitHub 分组操作未确认："+string.Join("；",errors.EnumerateArray().Select(e=>S(e,"message"))));
        return result.GetProperty("data");
    }
    public async Task<StarGroup> CreateList(string name,string description,bool isPrivate,CancellationToken ct=default)
    {
        var data=await Graph("mutation($input:CreateUserListInput!){createUserList(input:$input){list{id name description isPrivate}}}",new{input=new{name,description,isPrivate}},ct);
        var list=data.GetProperty("createUserList").GetProperty("list");return new(S(list,"id"),S(list,"name"),"GitHub",[],S(list,"description"),list.GetProperty("isPrivate").GetBoolean());
    }
    public async Task UpdateList(StarGroup group,string name,string description,bool isPrivate,CancellationToken ct=default)=>_ = await Graph("mutation($input:UpdateUserListInput!){updateUserList(input:$input){list{id name}}}",new{input=new{listId=group.Id,name,description,isPrivate}},ct);
    public async Task DeleteList(string id,CancellationToken ct=default)=>_ = await Graph("mutation($input:DeleteUserListInput!){deleteUserList(input:$input){clientMutationId}}",new{input=new{listId=id}},ct);
    public async Task<string[]> UpdateMembership(string nodeId,IEnumerable<string> listIds,CancellationToken ct=default)
    {
        if(nodeId.Length==0)throw new IOException("仓库缺少 GitHub Node ID，请重新读取项目。");
        var data=await Graph("mutation($input:UpdateUserListsForItemInput!){updateUserListsForItem(input:$input){lists{id}}}",new{input=new{itemId=nodeId,listIds=listIds.Distinct().ToArray()}},ct);
        var actual=data.GetProperty("updateUserListsForItem").GetProperty("lists").EnumerateArray().Select(l=>S(l,"id")).ToArray();
        if(!actual.ToHashSet().SetEquals(listIds))throw new IOException("GitHub 返回的分组归属与提交不一致，请读取远程分组核实。");
        return actual;
    }
}

