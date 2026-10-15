using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private StackPanel FollowSection(Panel parent,string title)
    {
        var content=new StackPanel();var heading=ProjectText(title,16);heading.FontWeight=FontWeights.SemiBold;content.Children.Add(heading);
        var border=new Border{Child=content,Padding=new Thickness(16),Margin=new Thickness(0,0,0,12),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1)};border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Edge");parent.Children.Add(border);return content;
    }
    private void FollowManagementDialog(string repo,string? previewPath=null)
    {
        if(!ManageReady())return;GitHubClient.Validate(repo);var account=settings.Account;
        var info=repositories.FirstOrDefault(r=>r.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase));var initialStar=info?.Starred??false;var initialLocal=HasLocalFollow(repo);
        var f=new Form(this,"关注与分组管理",800,720);f.Label(repo);
        f.Section("关注方式");var star=f.Check("GitHub Star（同步到当前账号）",initialStar);star.IsEnabled=api.Token.Length>0;var local=f.Check("本地关注（仅保存在本机）",initialLocal);f.Label("两种关注方式独立保存；同时存在时优先显示 GitHub Star。取消关注会写入历史，本机软件关联保持不变。");
        f.Section("所属分组");var panel=new StackPanel();f.Fields.Children.Add(panel);var entries=new List<(StarGroup Group,bool Initial,CheckBox Check)>();
        void RenderGroups(){var choices=entries.ToDictionary(x=>x.Group.Source+"/"+x.Group.Id,x=>x.Check.IsChecked==true);entries.Clear();panel.Children.Clear();foreach(var group in groups.Concat(settings.LocalGroups)){var initial=group.Repositories.Contains(repo,StringComparer.OrdinalIgnoreCase);var key=group.Source+"/"+group.Id;var check=new CheckBox{Content=group.Name+" · "+group.Source,IsChecked=choices.TryGetValue(key,out var old)?old:initial,Margin=new Thickness(0,5,0,7),IsEnabled=group.Source!="GitHub"||api.Token.Length>0};panel.Children.Add(check);entries.Add((group,initial,check));}if(entries.Count==0)panel.Children.Add(ProjectText("暂无分组，可在这里新建。",12,true));}
        RenderGroups();star.Unchecked+=(_,_)=>{foreach(var entry in entries.Where(e=>e.Group.Source=="GitHub"))entry.Check.IsChecked=false;};
        f.Label("一个项目可属于多个分组。GitHub 分组需要 Star；保存时只合并此次调整，保留其他现有分组。");
        f.Action("新建分组",()=>{GroupEditor(owner:f,changed:RenderGroups);return Task.CompletedTask;});
        f.Action("管理分组",()=>{ManageFollowGroups(f,RenderGroups);return Task.CompletedTask;});
        f.Action("取消关注历史",()=>{
            string HistoryState()=>JsonSerializer.Serialize(settings.FollowHistory.Where(e=>e.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase)).Select(e=>new{e.Id,e.RestoredAt,e.RestoredAs,e.Note}));
            var before=HistoryState();FollowHistoryDialog(f);
            if(before!=HistoryState()){initialStar=repositories.Any(r=>r.Starred&&r.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase));initialLocal=HasLocalFollow(repo);star.IsChecked=initialStar;local.IsChecked=initialLocal;entries.Clear();RenderGroups();}
            return Task.CompletedTask;
        });
        f.Action("打开仓库",()=>{WindowsServices.Open("https://github.com/"+repo);return Task.CompletedTask;});
        f.Action("核对 GitHub 状态",async()=>{if(api.Token.Length==0)throw new IOException("请先登录 GitHub。");info=await ReadManagedRepo(repo,CancellationToken.None);groups=await api.Lists(CancellationToken.None);PutRepo(info);initialStar=info.Starred;star.IsChecked=initialStar;entries.Clear();RenderGroups();SaveCache();RebuildGroups();});
        f.Action("保存",async()=>{
            if(account!=settings.Account)throw new IOException("账号已改变，请重新打开管理窗口。");
            var desiredStar=star.IsChecked==true;var desiredLocal=local.IsChecked==true;var changes=entries.Where(x=>x.Initial!=(x.Check.IsChecked==true)).Select(x=>(x.Group,Add:x.Check.IsChecked==true)).ToArray();var cloud=changes.Where(x=>x.Group.Source=="GitHub").ToArray();
            if(entries.Any(x=>x.Group.Source=="GitHub"&&x.Check.IsChecked==true)&&!desiredStar)throw new IOException("加入 GitHub 分组前，请勾选 GitHub Star。");
            if(!desiredStar&&!desiredLocal&&changes.Any(c=>c.Add))throw new IOException("请先选择 Star 或本地关注，再加入分组。");
            if((desiredStar!=initialStar||cloud.Length>0)&&!ManageReady(true))return;
            if(initialStar&&!desiredStar&&DialogMessage.Show(f,"取消此项目的 GitHub Star？操作会记录到取消关注历史，可稍后恢复。","取消 Star",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            f.Close();await Execute("Star管理","保存关注与分组 · "+repo,async ct=>{
                if(cloud.Length>0&&desiredStar)await api.EnsureListWritePermission(ct);
                if(desiredStar!=initialStar)info=await SetManagedStar(repo,desiredStar,ct);
                // Local cancellation is recorded before pruning its previous group membership.
                if(initialLocal!=desiredLocal)SetLocalFollow(repo,desiredLocal,initialLocal);
                else if(desiredLocal){settings.HiddenRepositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));if(!settings.ExtraRepositories.Contains(repo,StringComparer.OrdinalIgnoreCase))settings.ExtraRepositories.Add(repo);}
                if(desiredStar)settings.HiddenRepositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));
                if(cloud.Length>0&&desiredStar){
                    var latest=await api.Lists(ct);var fresh=await ReadManagedRepo(repo,ct);if(!fresh.Starred)throw new IOException("关注已保存，但当前 GitHub Star 状态已改变，请重新核对分组。");
                    if(cloud.Any(c=>!latest.Any(g=>g.Id==c.Group.Id)))throw new IOException("关注已保存，但所选 GitHub 分组已删除，请重新选择。");
                    var current=latest.Where(g=>g.Repositories.Contains(repo,StringComparer.OrdinalIgnoreCase)).Select(g=>g.Id);var desired=Rules.MergeMembership(current,cloud.Where(c=>c.Add).Select(c=>c.Group.Id),cloud.Where(c=>!c.Add).Select(c=>c.Group.Id));
                    var actual=await api.UpdateMembership(fresh.NodeId,desired,ct);foreach(var group in latest){group.Repositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));if(actual.Contains(group.Id))group.Repositories.Add(repo);}groups=latest;PutRepo(fresh);
                }
                foreach(var change in changes.Where(c=>c.Group.Source!="GitHub")){var group=settings.LocalGroups.FirstOrDefault(g=>g.Id==change.Group.Id&&g.Source==change.Group.Source)??throw new IOException("关注已保存，但本地分组已删除。");group.Repositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));if(change.Add)group.Repositories.Add(repo);}
                if(!desiredStar&&!desiredLocal)PruneUnfollowed(repo);Save();SaveCache();RebuildGroups();model.Status="关注方式与分组已保存；取消操作可在历史记录中恢复。";
            });
        },true);f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private void ManageFollowGroups(Window owner,Action changed,string? previewPath=null)
    {
        var f=new Form(owner,"收藏分组管理",760,620);f.Label("统一管理 GitHub 与本地分组。删除分组保留 Star、本地关注和本机软件关联。");var list=new StackPanel();f.Fields.Children.Add(list);
        void Render(){list.Children.Clear();foreach(var group in groups.Concat(settings.LocalGroups).ToArray()){
            var box=FollowSection(list,group.Name);box.Children.Add(ProjectText(group.Source+" · "+group.Repositories.Count+" 个项目",12,true));var actions=new WrapPanel();box.Children.Add(actions);
            var edit=new Button{Content="编辑",Margin=new Thickness(0,0,8,0)};edit.Click+=(_,_)=>GroupEditor(group,owner:f,changed:()=>{Render();changed();});actions.Children.Add(edit);
            var delete=f.Action("删除分组",async()=>{
                if(!ManageReady(group.Source=="GitHub"))return;
                if(DialogMessage.Show(f,$"删除“{group.Name}”（{group.Source}）？\n\n分组归属会移除，项目的 Star 和本地关注保留。","删除分组",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
                await Execute("分组管理","删除分组 · "+group.Name,async ct=>{
                    if(group.Source=="GitHub"){
                        var latest=await api.Lists(ct);var fresh=latest.FirstOrDefault(g=>g.Id==group.Id)??throw new IOException("分组已删除，请刷新后重试。");if(fresh.Name!=group.Name)throw new IOException("分组名称已变化，请刷新后重新确认。");
                        try{await api.DeleteList(group.Id,ct);}catch(Exception ex)when(ex is not OperationCanceledException){if((await api.Lists(ct)).Any(g=>g.Id==group.Id))throw new IOException("删除结果未确认，请核实后重试。",ex);}
                        latest.RemoveAll(g=>g.Id==group.Id);groups=latest;
                    }else settings.LocalGroups.RemoveAll(g=>g.Id==group.Id&&g.Source==group.Source);
                    Save();SaveCache();RebuildGroups();
                });Render();changed();
            });f.Buttons.Children.Remove(delete);delete.Margin=new Thickness(0);actions.Children.Add(delete);
        }if(list.Children.Count==0)list.Children.Add(ProjectText("暂无分组。",13,true));}
        Render();f.Action("新建分组",()=>{GroupEditor(owner:f,changed:()=>{Render();changed();});return Task.CompletedTask;});f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}
