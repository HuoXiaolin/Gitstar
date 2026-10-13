using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task<GitProjectState?> ResolveWorkspaceRoot(Window owner,ManagedProject project,string folder,string branch,bool initialize=false,string? previewPath=null)
    {
        var git=new GitWorkspace("");var diagnosis=previewPath==null?await git.Diagnose(folder,CancellationToken.None):new WorkspaceDiagnosis(folder,@"D:\Agant\github-update","https://github.com/example/Gitstar.git");
        if(previewPath==null && diagnosis.IsRoot && ProjectRules.MatchesRemote(diagnosis.Remote,project.FullName))return await git.Inspect(diagnosis.Root,CancellationToken.None);
        if(previewPath==null && !diagnosis.HasRepository && initialize){
            GitProjectState? initialized=null;await Execute("本地项目",project.FullName,async ct=>{await git.Initialize(diagnosis.Folder,project.FullName,branch,ct);initialized=await git.Inspect(diagnosis.Folder,ct);},resources:["git:"+diagnosis.Folder]);return initialized;
        }
        var f=new Form(owner,"检查并关联源码目录",830,720);f.Label("目标项目："+project.FullName+"\n当前关联："+(Workspace(project)?.Folder??"尚未关联"));
        var candidate=f.Text("选中的源码目录",diagnosis.Folder);var root=f.Text("实际 Git 仓库根目录",diagnosis.Root);root.IsReadOnly=true;var remote=f.Text("当前 origin",diagnosis.Remote);remote.IsReadOnly=true;var hint=f.Text("目录状态与下一步","",true);hint.IsReadOnly=true;hint.MinHeight=120;
        GitProjectState? result=null;
        void Describe(){root.Text=diagnosis.Root.Length>0?diagnosis.Root:"尚未初始化 Git";remote.Text=diagnosis.Remote;hint.Text=!diagnosis.HasRepository?"此目录尚未初始化 Git。点击“使用推荐方案”即可初始化并关联目标项目。":!diagnosis.IsRoot?$"此目录尚未独立初始化 Git，当前属于外层仓库：\n{diagnosis.Root}\n在 GitHub 新建或重建仓库不会改变本地目录的 Git 归属。\n只上传此目录的源码：点击“使用推荐方案”，复制到外层仓库之外并初始化；上传整个外层仓库：在高级操作中使用检测到的根目录。":!ProjectRules.MatchesRemote(diagnosis.Remote,project.FullName)?"此目录已经是独立的 Git 根目录，但 origin 未配置或指向其他项目。点击“使用推荐方案”可查看并确认修正地址；也可选择其他源码目录。":"此目录已经是 Git 根目录，且 origin 与目标项目一致，可以直接关联，无需复制或切换目录。";}
        Describe();
        async Task Detect(){diagnosis=await git.Diagnose(candidate.Text,CancellationToken.None);candidate.Text=diagnosis.Folder;Describe();}
        f.Action("选择新根目录",async()=>{var d=new OpenFolderDialog{Title="选择新的源码根目录"};if(d.ShowDialog(f)==true){candidate.Text=d.FolderName;await Detect();}});
        f.Action("重新检测",Detect);
        f.Action("使用检测到的根目录",async()=>{await Detect();if(!diagnosis.HasRepository)throw new IOException("此目录没有 Git 仓库，请初始化或选择已有仓库。");if(!ProjectRules.MatchesRemote(diagnosis.Remote,project.FullName))throw new IOException("实际根目录属于其他仓库，请复制到独立目录，或选择根目录后确认修正 origin。");result=await git.Inspect(diagnosis.Root,CancellationToken.None);f.Close();});
        f.Action("初始化新根目录",async()=>{await Detect();if(diagnosis.HasRepository)throw new IOException("此目录已属于 Git 仓库；可以使用实际根目录，或复制到仓库之外。");await Execute("本地项目",project.FullName,async ct=>{await git.Initialize(diagnosis.Folder,project.FullName,branch,ct);result=await git.Inspect(diagnosis.Folder,ct);},resources:["git:"+diagnosis.Folder]);if(result!=null)f.Close();});
        f.Action("修正 origin 并关联",async()=>{
            await Detect();if(!diagnosis.IsRoot)throw new IOException("请选择实际仓库根目录后再修正地址。");
            if(ProjectRules.MatchesRemote(diagnosis.Remote,project.FullName)){result=await git.Inspect(diagnosis.Root,CancellationToken.None);f.Close();return;}
            if(DialogMessage.Show(f,$"目录：{diagnosis.Root}\n\n原 origin：{diagnosis.Remote}\n新 origin：https://github.com/{project.FullName}.git\n\n原地址将保留为其他 remote；此修改影响该仓库后续推送。是否确认？","确认修正仓库地址",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            await Execute("本地项目",project.FullName,async ct=>{var backup=await git.RepairOrigin(diagnosis.Root,project.FullName,diagnosis.Remote,project.Fork,ct);result=await git.Inspect(diagnosis.Root,ct);model.Status=backup.Length>0?"origin 已修正；原地址保留为 "+backup:"origin 已配置。";},resources:["git:"+diagnosis.Root]);if(result!=null)f.Close();
        });
        f.Action("复制为独立新根目录",async()=>{
            await Detect();var suggestedParent=Path.GetDirectoryName(diagnosis.HasRepository?diagnosis.Root:diagnosis.Folder)??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var copy=new Form(f,"复制并切换源码根目录",760,670);copy.Label("源目录："+diagnosis.Folder+"\n复制成功并初始化后切换关联，原文件和 Git 历史保留。");var destination=copy.Text("独立新根目录",Path.Combine(suggestedParent,Rules.SafeSegment(project.Name)+"-source"));var omit=copy.Check("排除生成目录：bin / obj / node_modules / artifacts / outputs / dist / .vs / .gradle",true);var history=copy.Check("保留远程提交历史：先克隆，再覆盖对应源码文件",api.Token.Length>0);history.IsEnabled=api.Token.Length>0;copy.Label("保留历史时，远程其他文件保留；不保留历史仅适用于空仓库。不复制源目录的 .git。目标必须不存在或为空，且位于其他 Git 仓库之外。");
            copy.Action("选择上级目录",()=>{var d=new OpenFolderDialog{Title="选择现有仓库之外的上级目录"};if(d.ShowDialog(copy)==true)destination.Text=Path.Combine(d.FolderName,Rules.SafeSegment(project.Name)+"-source");return Task.CompletedTask;});
            copy.Action("复制并切换",async()=>{var source=diagnosis.Folder;var target=Path.GetFullPath(destination.Text);var exclude=omit.IsChecked==true;var preserve=history.IsChecked==true;var progress=new Progress<string>(name=>model.Status="正在准备源码："+name);await Execute("目录复制",project.FullName,async ct=>{Func<string,CancellationToken,Task>? clone=preserve?async(stage,token)=>{await api.EnsureFreshToken(token);await new GitWorkspace(api.Token).Clone(project.FullName,stage,token);}:null;result=await SourceRootCopy.CopyAndInitialize(source,target,project.FullName,branch,exclude,name=>((IProgress<string>)progress).Report(name),ct,clone);},resources:["git:"+(diagnosis.HasRepository?diagnosis.Root:source),"git:"+target]);if(result!=null){copy.Close();f.Close();}},true);copy.ShowDialog();
        },true);
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;});
        var actions=f.Buttons.Children.OfType<Button>().ToArray();var advanced=f.Advanced("详细信息与高级修正");foreach(var field in new[]{root,remote}){var index=f.Fields.Children.IndexOf(field);var label=f.Fields.Children[index-1];f.Fields.Children.Remove(label);f.Fields.Children.Remove(field);advanced.Children.Add(label);advanced.Children.Add(field);}var alternatives=new WrapPanel();advanced.Children.Add(alternatives);
        foreach(var button in actions.Where(b=>b.Content?.ToString() is not ("选择新根目录" or "取消"))){f.Buttons.Children.Remove(button);alternatives.Children.Add(button);}
        f.Action("使用推荐方案",async()=>{await Detect();var title=SourceWorkflow.Recommend(diagnosis,project.FullName) switch{WorkspaceAction.Initialize=>"初始化新根目录",WorkspaceAction.UseRoot=>"使用检测到的根目录",WorkspaceAction.RepairOrigin=>"修正 origin 并关联",_=>"复制为独立新根目录"};await ((Func<Task>)actions.Single(b=>b.Content?.ToString()==title).Tag!)();},true);
        if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();return result;
    }
}
