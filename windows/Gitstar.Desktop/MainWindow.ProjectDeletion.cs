using System.Windows;
using System.Windows.Controls;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void ForgetDeletedProject(string repo)
    {
        bool Matches(string value)=>value.Equals(repo,StringComparison.OrdinalIgnoreCase);
        myProjects.RemoveAll(project=>Matches(project.FullName));projectWorkspaces.RemoveAll(workspace=>Matches(workspace.Repository));projectStates.Remove(repo);projectVersions.Remove(repo);projectAutomation.Remove(repo);
        repositories.RemoveAll(project=>Matches(project.FullName));releases.RemoveAll(release=>Matches(release.Repository));settings.ExtraRepositories.RemoveAll(Matches);
        foreach(var binding in settings.Bindings.Where(binding=>Matches(binding.Repository)))binding.Disabled=true;
        groups=groups.Select(group=>group with{Repositories=group.Repositories.Where(value=>!Matches(value)).ToList()}).ToList();settings.LocalGroups=settings.LocalGroups.Select(group=>group with{Repositories=group.Repositories.Where(value=>!Matches(value)).ToList()}).ToList();
        store.RemoveDocuments([], [CacheKey+"/http-releases/"+repo.ToLowerInvariant()+"/"]);Save();SaveProjects();SaveCache();PlanUpdates();RebuildGroups();if(selectedProject!=null&&Matches(selectedProject))selectedProject=null;RenderProjects();
    }
    private async Task OpenDeleteProject(ManagedProject project)
    {
        var account=ProjectsKey;ManagedProject? target=null;
        await Execute("项目读取","核对删除项目："+project.FullName,async ct=>{var fresh=await api.Project(project.FullName,ct);if(project.Id>0&&!ProjectRules.SameRepository(project,fresh))throw new IOException("仓库身份已改变，请先同步项目，再重新确认删除。");target=fresh;},resources:["repo:"+project.FullName]);
        if(target!=null&&account==ProjectsKey)DeleteProjectDialog(target);
    }
    private void DeleteProjectDialog(ManagedProject project,string? previewPath=null)
    {
        var account=ProjectsKey;var f=new Form(this,"删除项目",720,590);f.Label("将删除 GitHub 仓库："+project.FullName);
        f.Label("远程源码、提交历史、Issues、发布版本与安装包将随仓库一起删除。请确认你选中了正确项目。");
        f.Label("本地源码和安装包文件保留；项目管理关联与缓存会移除，关联此仓库的本机软件会暂停检查，可重新关联。");
        f.Label("需要仓库管理员权限，以及 GitHub 授权的 delete_repo 权限。");
        var confirmation=f.Text("输入完整仓库名称以确认");var validPermission=project.CanAdmin&&api.CanDeleteRepositories!=false;
        if(!project.CanAdmin)f.Label("当前账号没有仓库管理员权限。");else if(api.CanDeleteRepositories==false)f.Label("当前授权缺少删除权限。点击下方“授权删除权限”，授权完成后重新打开删除窗口。");
        var remove=f.Action("删除 GitHub 项目",async()=>{
            if(ProjectsKey!=account)throw new IOException("登录账号已改变，请重新打开删除窗口。");if(!confirmation.Text.Trim().Equals(project.FullName,StringComparison.OrdinalIgnoreCase))throw new IOException("请输入完整仓库名称。");
            var confirmed=confirmation.Text;f.Close();await Execute("项目删除","删除 "+project.FullName,async ct=>{var op=currentOperation.Value!;op.Repository=project.FullName;op.RemoteId=project.Id;SaveOperations();await api.DeleteRepository(project,confirmed,ct);ForgetDeletedProject(project.FullName);op.Results.Add("成功：GitHub 仓库已删除，本地源码和安装包保留");model.Status="已删除 GitHub 项目："+project.FullName+"；本地文件保留。";},resources:["repo:"+project.FullName,"projects:"+settings.Account,"cache"]);
        });remove.SetResourceReference(Control.ForegroundProperty,"DangerInk");remove.IsEnabled=false;
        confirmation.TextChanged+=(_,_)=>remove.IsEnabled=validPermission&&confirmation.Text.Trim().Equals(project.FullName,StringComparison.OrdinalIgnoreCase);
        if(project.CanAdmin&&api.CanDeleteRepositories==false)f.Action("授权删除权限",()=>{f.Close();AuthorizeGitHub();return Task.CompletedTask;});
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}
