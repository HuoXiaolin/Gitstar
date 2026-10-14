using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void ForgetProjectVersion(string repo,long id)
    {
        releaseDirectoryStates.Remove(repo);
        if(projectVersions.TryGetValue(repo,out var history))history.RemoveAll(version=>version.Id==id);
        releases.RemoveAll(release=>release.Id==id&&release.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase));SaveProjects();SaveCache();PlanUpdates();
        if(projectsPage&&selectedProject==repo)RenderProjectDetails(myProjects.FirstOrDefault(project=>project.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase)));else if(!projectsPage)Display();
    }
    private void RememberReleaseInventory(ReleaseInventory inventory)
    {
        releaseDirectoryStates.Remove(inventory.Project.FullName);
        var repo=inventory.Project.FullName;RememberProjectVersion(repo,inventory.Version);
        for(var index=0;index<releases.Count;index++)if(releases[index].Id==inventory.Version.Id&&releases[index].Repository.Equals(repo,StringComparison.OrdinalIgnoreCase))releases[index]=releases[index] with{Assets=inventory.Assets.Select(asset=>new Asset(asset.Id,asset.Name,asset.Size,asset.Url,asset.Digest)).ToList()};
        SaveCache();PlanUpdates();if(!projectsPage)Display();
    }
    private async Task OpenReleaseManager(ManagedProject project,ProjectVersion version)
    {
        var key=ProjectsKey;ReleaseInventory? inventory=null;
        await Execute("版本读取",project.FullName+" · "+version.Tag,async ct=>{inventory=await api.ReleaseInventory(project.FullName,version.Id,ct);RememberReleaseInventory(inventory);},resources:["repo:"+project.FullName]);
        if(inventory!=null&&key==ProjectsKey)ReleaseManagerDialog(inventory);
    }
    private void ReleaseManagerDialog(ReleaseInventory initial,string? previewPath=null)
    {
        var current=initial;var account=ProjectsKey;var f=new Form(this,"管理发布包",850,660);var commands=new List<Button>();var closed=false;var modifying=false;Button? deleteVersion=null,refresh=null;
        f.Closed+=(_,_)=>closed=true;
        void EnsureAccount(){if(account!=ProjectsKey)throw new IOException("账号已改变，请重新打开发布包管理。");}
        void SetBusy(bool value){modifying=value;foreach(var command in commands)command.IsEnabled=!value&&current.Writable&&!current.Immutable;if(deleteVersion!=null)deleteVersion.IsEnabled=!value&&current.Writable;if(refresh!=null)refresh.IsEnabled=!value;}
        Button Inline(Panel parent,string text,Func<Task> action){var button=f.Action(text,action);f.Buttons.Children.Remove(button);button.Margin=new Thickness(12,0,0,0);parent.Children.Add(button);return button;}
        void Render(){
            f.Fields.Children.Clear();commands.Clear();f.Section(current.Version.DisplayVersion+" · "+current.Version.Channel);f.Label(current.Project.FullName+"   ·   "+current.Assets.Count+" 个安装包 / 附件");
            if(!current.Writable)f.Label("当前账号没有删除权限，或仓库已归档。");else if(current.Immutable)f.Label("此版本已锁定，无法删除单个附件。");
            f.Section("安装包与附件");if(current.Assets.Count==0)f.Label("此版本没有安装包附件。");
            foreach(var asset in current.Assets){
                var selected=asset;var expected=current;var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var text=new StackPanel{Margin=new Thickness(0,0,8,0)};text.Children.Add(new TextBlock{Text=asset.Name,FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});var meta=new TextBlock{Text=CacheBytes(asset.Size)+(asset.Digest.Length>0?"   ·   SHA-256 已提供":"")+"   ·   上传时间："+ProjectTimeRules.Time(asset.CreatedAt),FontSize=12,Margin=new Thickness(0,6,0,0),TextWrapping=TextWrapping.Wrap};meta.SetResourceReference(TextBlock.ForegroundProperty,"Muted");text.Children.Add(meta);row.Children.Add(text);
                var actions=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);row.Children.Add(actions);
                var delete=Inline(actions,"删除附件",async()=>{
                    EnsureAccount();if(modifying)return;
                    if(DialogMessage.Show(f,$"删除 GitHub 上的附件？\n\n{expected.Project.FullName} · {expected.Version.Tag}\n{selected.Name}\n\n此操作不能撤销。版本和本地文件保留。","删除附件",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
                    SetBusy(true);try{await Execute("发布包管理",expected.Project.FullName+" · 删除 "+selected.Name,async ct=>{var op=currentOperation.Value!;op.Repository=expected.Project.FullName;op.Tag=expected.Version.Tag;op.RemoteId=expected.Version.Id;SaveOperations();var updated=await api.DeleteReleaseAsset(expected,selected,ct);current=updated;RememberReleaseInventory(updated);op.Results.Add("成功：已删除附件 "+selected.Name);if(!closed)Render();},resources:["repo:"+expected.Project.FullName]);}finally{SetBusy(false);}
                });delete.SetResourceReference(Control.ForegroundProperty,"DangerInk");commands.Add(delete);
                var card=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),Child=row};card.SetResourceReference(Border.BorderBrushProperty,"Edge");card.SetResourceReference(Border.BackgroundProperty,"Surface");f.Fields.Children.Add(card);
            }
            f.Label("删除只影响 GitHub 发布内容，本地安装包与源码保留。再次同步并发布时，会按配置重新上传本地附件。");SetBusy(modifying);
        }
        deleteVersion=f.Action("删除整个版本",async()=>{
            EnsureAccount();if(modifying)return;var expected=current;
            if(DialogMessage.Show(f,$"永久删除整个发布版本及其 {expected.Assets.Count} 个附件？\n\n{expected.Project.FullName} · {expected.Version.Tag}\n\n此操作不能撤销。Git 标签、源码和本地文件保留。","删除发布版本",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            SetBusy(true);try{await Execute("发布包管理",expected.Project.FullName+" · 删除版本 "+expected.Version.Tag,async ct=>{var op=currentOperation.Value!;op.Repository=expected.Project.FullName;op.Tag=expected.Version.Tag;op.RemoteId=expected.Version.Id;SaveOperations();await api.DeleteProjectRelease(expected,ct);ForgetProjectVersion(expected.Project.FullName,expected.Version.Id);op.Results.Add("成功：已删除发布版本及附件，保留 Git 标签");if(!closed)f.Close();},resources:["repo:"+expected.Project.FullName]);}finally{SetBusy(false);}
        });deleteVersion.SetResourceReference(Control.ForegroundProperty,"DangerInk");
        f.Action("打开 GitHub",()=>{WindowsServices.Open("https://github.com/"+current.Project.FullName+"/releases");return Task.CompletedTask;});
        refresh=f.Action("刷新附件",async()=>{EnsureAccount();if(modifying)return;SetBusy(true);try{await Execute("版本读取",current.Project.FullName,async ct=>{current=await api.ReleaseInventory(current.Project.FullName,current.Version.Id,ct);RememberReleaseInventory(current);if(!closed)Render();},resources:["repo:"+current.Project.FullName]);}finally{SetBusy(false);}});
        f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});Render();if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}
