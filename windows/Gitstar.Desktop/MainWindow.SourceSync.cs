using System.Windows;
using System.Windows.Media;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void SourceSyncSmoke(string path)
    {
        var project=new ManagedProject("example/project","",false,"main",true,true,false,DateTimeOffset.UtcNow);
        var sync=new SourceSyncState("main",new string('a',40),new string('b',40),new string('c',40),new string('d',40),SourceRelation.Unrelated,1,1,true,"Source/Main.cs | 12 ++++++++----\nREADME.md | 2 ++\n2 files changed, 10 insertions(+), 4 deletions(-)");
        if(Environment.GetCommandLineArgs().Contains("--force-warning"))ConfirmForceUpdate(this,project,sync,path);else SourceSyncDialog(project,@"D:\Projects\Example",new GitWorkspace(""),sync,path);
    }
    private async Task CheckProjectSync(ManagedProject project)
    {
        var workspace=Workspace(project)??throw new IOException("请先关联源码目录。");var git=new GitWorkspace(api.Token);SourceSyncState? sync=null;
        await Execute("检测","核对源码同步 · "+project.FullName,async ct=>{sync=await git.CheckSync(workspace.Folder,project.FullName,workspace.Branch,ct);var state=await git.Inspect(workspace.Folder,ct);RememberWorkspace(project,state with{RemoteCommit=sync.Remote,RemoteCheckedAt=DateTimeOffset.UtcNow,SyncIssue=sync.Summary});model.Status=sync.Summary;});
        if(sync!=null)SourceSyncDialog(project,workspace.Folder,git,sync);
    }
    private void SourceSyncDialog(ManagedProject project,string folder,GitWorkspace git,SourceSyncState sync,string? previewPath=null)
    {
        var f=new Form(this,"源码同步核对",860,700);f.Label("项目："+project.FullName+"\n分支："+sync.Branch+"\n目录："+folder);f.Section("核对结果");f.Label(sync.Summary);
        f.Label("本地提交："+sync.Local+"\n远端提交："+sync.Remote);if(!sync.Clean)f.Label("本地存在未提交变更，请先提交或处理变更；同步修复和强制推送暂不可用。");
        var diff=f.Text("相对远端的本地源码差异",sync.Differences.Length>0?sync.Differences:"没有文件差异。",true);diff.IsReadOnly=true;diff.Height=170;
        f.Label("修复本地历史：保留当前本地文件快照，以远端提交为父提交重新建立历史，原本地提交保存在备份分支。此步骤不推送远端；完成后可点击“更新”正常上传。");
        f.Label("强制更新远端：远端分支将改为本地提交，远端独有提交不再属于该分支。操作前保留远端提交的本地备份；远端有新变化时自动拒绝覆盖。");
        var repair=f.Action("修复本地历史",async()=>{
            if(previewPath!=null)return;if(DialogMessage.Show(f,"将按上方差异保留本地源码，重新连接到远端历史。原本地提交会保留在 gitstar-backup 备份分支，不推送远端。\n\n确认修复 "+project.FullName+" 的 "+sync.Branch+" 分支？","修复本地历史",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            f.Close();await Execute("源码上传","修复本地提交历史 · "+project.FullName,async ct=>{var backup=await git.RepairHistory(folder,sync,ct);RememberWorkspace(project,await git.Inspect(folder,ct));model.Status="本地历史已修复，源码文件保持不变；尚未推送。\n原本地提交备份分支："+backup+"\n点击“更新”正常推送。";});
        });repair.IsEnabled=sync.CanRepairHistory;
        var fast=f.Action("同步远端更新",async()=>{if(previewPath!=null)return;f.Close();await Execute("源码上传","同步远端 · "+project.FullName,async ct=>{await git.FastForwardSync(folder,sync,ct);RememberWorkspace(project,await git.Inspect(folder,ct));model.Status="已同步远端提交；工作区与远端一致。";});});fast.IsEnabled=sync.Clean&&sync.Relation==SourceRelation.RemoteAhead;
        var force=f.Action("强制更新远端",async()=>{
            if(previewPath!=null)return;
            if(!ConfirmForceUpdate(f,project,sync))return;
            f.Close();await Execute("源码上传","强制更新远端 · "+project.FullName,async ct=>{var backup=await git.ForceUpdateRemote(folder,project.FullName,sync,ct);RememberWorkspace(project,(await git.Inspect(folder,ct)) with{RemoteCommit=sync.Local,RemoteCheckedAt=DateTimeOffset.UtcNow});model.Status="远端分支已更新并核对。\n远端原提交备份分支："+backup;});
        });force.IsEnabled=sync.Clean&&sync.Remote.Length>0&&sync.Relation!=SourceRelation.Same;force.Foreground=new SolidColorBrush(Color.FromRgb(202,65,83));
        f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private bool ConfirmForceUpdate(Window owner,ManagedProject project,SourceSyncState sync,string? previewPath=null)
    {
        var confirmed=false;var f=new Form(owner,"确认强制更新远端",760,600);
        f.Section("将替换以下远端分支");f.Label("仓库："+project.FullName+"\n分支："+sync.Branch);f.Label("远端当前提交："+sync.Remote+"\n替换为本地提交："+sync.Local);
        f.Label($"远端独有的 {sync.RemoteOnly} 个提交将离开此分支。请确认上一步展示的文件差异就是你希望发布的内容。");
        f.Label("执行前会将远端旧提交保存在当前源码仓库的 gitstar-remote-backup/… 备份分支。只更新指定分支，其他分支和已有标签保留。");
        f.Label("若远端在核对后有任何新提交，本次操作会停止，需重新核对。强制更新仍受 GitHub 权限及分支保护限制。");
        var cancel=f.Action("取消",()=>{f.Close();return Task.CompletedTask;});cancel.IsCancel=true;cancel.IsDefault=true;
        var yes=f.Action("确认强制更新",()=>{confirmed=true;f.Close();return Task.CompletedTask;});yes.Foreground=new SolidColorBrush(Color.FromRgb(202,65,83));
        if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();return confirmed;
    }
}
