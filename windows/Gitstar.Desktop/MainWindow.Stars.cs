using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void BatchResult(string repo,bool success,string message=""){var op=currentOperation.Value;if(op!=null){op.Results.Add((success?"成功":"待处理")+" · "+repo+(message.Length>0?"："+message:""));SaveOperations();}}
    private bool ManageReady(bool login=false)
    {
        if(quitting)return false;
        if(login && api.Token.Length==0){Login_Click(this,new RoutedEventArgs());return api.Token.Length>0;}
        return true;
    }
    private StarGroup? SelectedGroup()=>groups.Concat(settings.LocalGroups).FirstOrDefault(g=>g.Source+"/"+g.Id==(GroupSidebar.SelectedItem as GroupEntry)?.Id);
    private void Groups_RightClick(object sender,MouseButtonEventArgs e)
    {
        var target=e.OriginalSource as DependencyObject;while(target!=null && target is not ListBoxItem)target=VisualTreeHelper.GetParent(target);
        if(target is ListBoxItem item)item.IsSelected=true;
    }
    private void GroupMenu_Opening(object sender,ContextMenuEventArgs e){if(SelectedGroup()==null)e.Handled=true;}
    private void NewGroup_Click(object sender,RoutedEventArgs e)=>GroupEditor();
    private void EditGroup_Click(object sender,RoutedEventArgs e){if(SelectedGroup() is {} group)GroupEditor(group);}
    private async void DeleteGroup_Click(object sender,RoutedEventArgs e)
    {
        var group=SelectedGroup();if(group==null || !ManageReady(group.Source=="GitHub"))return;
        if(DialogMessage.Show(this,$"删除分组“{group.Name}”？项目的 Star 和本机软件关联均保留。","删除分组",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        await Execute("分组管理","删除分组 · "+group.Name,async ct=>{
            if(group.Source=="GitHub"){
                try{await api.DeleteList(group.Id,ct);}catch(Exception ex)when(ex is not OperationCanceledException){if((await api.Lists(ct)).Any(g=>g.Id==group.Id))throw new IOException("删除结果未确认，请核实后重试。",ex);}
                groups.RemoveAll(g=>g.Id==group.Id);
            }else settings.LocalGroups.RemoveAll(g=>g.Id==group.Id && g.Source==group.Source);
            Save();SaveCache();RebuildGroups();model.Status="分组已删除，Star 保持不变。";
        });
    }
    private void GroupEditor(StarGroup? group=null,string? previewPath=null)
    {
        if(!ManageReady())return;
        var f=new Form(this,group==null?"新建收藏分组":"编辑收藏分组",620,510);
        var source=f.Select("保存位置",["GitHub 分组（同步到账号）","本地分组（只保存在本机）"],group==null?(api.Token.Length>0?0:1):group.Source=="GitHub"?0:1);source.IsEnabled=group==null;
        var name=f.Text("分组名称",group?.Name??"");var description=f.Text("分组说明",group?.Description??"",true);var privacy=f.Check("GitHub 分组设为私有",group?.IsPrivate??false);
        privacy.IsEnabled=source.SelectedIndex==0;source.SelectionChanged+=(_,_)=>privacy.IsEnabled=source.SelectedIndex==0;
        f.Label("本地分组不会写回 GitHub；GitHub 分组需要登录。重命名和删除分组不会取消项目的 Star。");
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;});
        f.Action("保存",async()=>{
            var title=name.Text.Trim();if(title.Length==0)throw new IOException("请输入分组名称。");var remote=source.SelectedIndex==0;if(!ManageReady(remote))return;
            if(!remote && settings.LocalGroups.Any(g=>g.Id!=group?.Id && g.Name.Equals(title,StringComparison.OrdinalIgnoreCase)))throw new IOException("已有同名本地分组，请使用其他名称。");
            var details=description.Text.Trim();var isPrivate=privacy.IsChecked==true;f.Close();
            await Execute("分组管理",(group==null?"新建":"编辑")+"分组 · "+title,async ct=>{
                if(remote){
                    if(group==null){var created=await api.CreateList(title,details,isPrivate,ct);groups.Add(created);}
                    else{
                        var latest=await api.Lists(ct);var fresh=latest.FirstOrDefault(g=>g.Id==group.Id)??throw new IOException("GitHub 分组已删除，请重新选择。");
                        var nextName=title!=group.Name?title:fresh.Name;var nextDescription=details!=group.Description?details:fresh.Description;var nextPrivate=isPrivate!=group.IsPrivate?isPrivate:fresh.IsPrivate;
                        try{await api.UpdateList(fresh,nextName,nextDescription,nextPrivate,ct);}catch(Exception ex)when(ex is not OperationCanceledException){var actual=(await api.Lists(ct)).FirstOrDefault(g=>g.Id==group.Id);if(actual==null || actual.Name!=nextName || actual.Description!=nextDescription || actual.IsPrivate!=nextPrivate)throw new IOException("修改结果未确认，请核实 GitHub 分组。",ex);}
                        groups=latest;var i=groups.FindIndex(g=>g.Id==group.Id);groups[i]=fresh with{Name=nextName,Description=nextDescription,IsPrivate=nextPrivate};
                    }
                }else{
                    if(group==null)settings.LocalGroups.Add(new(Guid.NewGuid().ToString("N"),title,"本地",[],details));
                    else{var i=settings.LocalGroups.FindIndex(g=>g.Id==group.Id && g.Source==group.Source);if(i>=0)settings.LocalGroups[i]=group with{Name=title,Description=details};}
                }
                Save();SaveCache();RebuildGroups();model.Status="分组已保存。";
            });
        },true);if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private async Task<Repository> ReadManagedRepo(string repo,CancellationToken ct)
    {
        var info=await api.Repo(repo,ct);if(api.Token.Length>0)info=info with{Starred=await api.IsStarred(info.FullName,ct)};
        return info;
    }
    private void PutRepo(Repository info){repositories.RemoveAll(r=>r.FullName.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));repositories.Add(info);}
    private void PruneUnfollowed(string repo)
    {
        if(repositories.Any(r=>r.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase) && r.Starred) || settings.ExtraRepositories.Contains(repo,StringComparer.OrdinalIgnoreCase))return;
        if(!settings.HiddenRepositories.Contains(repo,StringComparer.OrdinalIgnoreCase))settings.HiddenRepositories.Add(repo);
        foreach(var group in settings.LocalGroups)group.Repositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));
        // Keep release data needed by installed bindings, but remove unsubscribed projects from the recent feed.
        if(!settings.Bindings.Any(b=>b.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase)))releases.RemoveAll(r=>r.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase));
        repositories.RemoveAll(r=>r.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase));
    }
    private async void StarDialog(string repo)
    {
        if(!ManageReady(true))return;
        try{
            var info=await ReadManagedRepo(repo,CancellationToken.None);PutRepo(info);SaveCache();
            var f=new Form(this,"管理 Star",620,430);f.Label(info.FullName);var star=f.Check("在 GitHub 上 Star 此项目",info.Starred);var follow=f.Check("同时保留本地关注（取消 Star 后仍跟踪版本）",settings.ExtraRepositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase));
            f.Label("取消 Star 会同步到 GitHub；保留已关联的本机软件。本地关注独立于 GitHub 收藏。");
            f.Action("取消",()=>{f.Close();return Task.CompletedTask;});f.Action("保存",async()=>{
                var desired=star.IsChecked==true;var keep=follow.IsChecked==true;
                if(!desired && info.Starred && DialogMessage.Show(f,"取消此项目在 GitHub 上的 Star？","取消 Star",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
                f.Close();await Execute("Star管理",info.FullName,async ct=>{
                    var currentStar=desired;if(desired!=info.Starred)await api.SetStar(info.FullName,desired,ct);else currentStar=await api.IsStarred(info.FullName,ct);PutRepo(info with{Starred=currentStar});
                    settings.ExtraRepositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));if(keep)settings.ExtraRepositories.Add(info.FullName);
                    if(currentStar || keep)settings.HiddenRepositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));else if(!settings.HiddenRepositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase))settings.HiddenRepositories.Add(info.FullName);
                    if(!currentStar)foreach(var g in groups)g.Repositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));
                    PruneUnfollowed(info.FullName);Save();SaveCache();RebuildGroups();model.Status=currentStar?"Star 与本地关注已保存。":"已取消 Star；本机软件关联保留。";
                    if((currentStar || keep) && !releases.Any(r=>r.Repository.Equals(info.FullName,StringComparison.OrdinalIgnoreCase))){try{releases.AddRange(await api.Releases(info.FullName,ct,settings.Bindings));SaveCache();Display();}catch(Exception ex)when(ex is not OperationCanceledException){model.Status="关注已保存；版本读取待重试："+ex.Message;}}
                });
            },true);f.ShowDialog();
        }catch(Exception ex){DialogMessage.Show(this,ex.Message,"无法读取 Star 状态");}
    }
    private async void MembershipDialog(string[] repos,bool batch=false)
    {
        if(!ManageReady())return;
        var remoteAvailable=api.Token.Length>0;
        if(remoteAvailable){try{groups=await api.Lists(CancellationToken.None);SaveCache();RebuildGroups();}catch(Exception ex){remoteAvailable=false;DialogMessage.Show(this,"GitHub 分组读取失败，仍可编辑本地分组。\n"+ex.Message,"分组读取未完成");}}
        var all=groups.Concat(settings.LocalGroups).ToList();var f=new Form(this,batch?"批量调整分组":"修改项目分组",640,640);f.Label(string.Join("\n",repos));
        var mode=batch?f.Select("操作",["添加到所选分组","从所选分组移除"]):null;
        var entries=all.Select(g=>(Group:g,Initial:!batch&&g.Repositories.Contains(repos[0],StringComparer.OrdinalIgnoreCase))).Select(x=>(x.Group,x.Initial,Check:f.Check($"{x.Group.Name} · {x.Group.Source}",x.Initial))).ToList();
        foreach(var row in entries)if(row.Group.Source=="GitHub")row.Check.IsEnabled=remoteAvailable;
        f.Label("一个项目可加入多个分组。保存时读取最新 GitHub 归属，只修改你此次调整的分组；未勾选其他分组不会在批量操作中被覆盖。");
        if(entries.Count==0)f.Label("暂无分组，请先从左侧 ＋ 新建分组。");
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;});f.Action("保存",async()=>{
            var changes=entries.Where(x=>batch?x.Check.IsChecked==true:x.Initial!=(x.Check.IsChecked==true)).Select(x=>(x.Group,Add:batch?mode!.SelectedIndex==0:x.Check.IsChecked==true)).ToList();
            if(changes.Count==0){f.Close();return;}if(changes.Any(x=>x.Group.Source=="GitHub")&&!ManageReady(true))return;
            f.Close();await Execute("批量管理","修改分组 · "+repos.Length+" 个项目",async ct=>{
                var failures=new List<string>();
                foreach(var repo in repos){ct.ThrowIfCancellationRequested();try{
                    var remote=changes.Where(x=>x.Group.Source=="GitHub").ToList();
                    if(remote.Count>0){
                        var latest=await api.Lists(ct);var info=await ReadManagedRepo(repo,ct);if(!info.Starred)throw new IOException("请先 Star 此项目，再加入 GitHub 分组。");
                        var current=latest.Where(g=>g.Repositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase)).Select(g=>g.Id).ToHashSet();
                        foreach(var change in remote)if(!latest.Any(g=>g.Id==change.Group.Id))throw new IOException("分组已被删除，请重新选择。");
                        current=Rules.MergeMembership(current,remote.Where(c=>c.Add).Select(c=>c.Group.Id),remote.Where(c=>!c.Add).Select(c=>c.Group.Id)).ToHashSet();
                        string[] actual;
                        try{actual=await api.UpdateMembership(info.NodeId,current,ct);}catch(Exception ex)when(ex is not OperationCanceledException){var verify=await api.Lists(ct);actual=verify.Where(g=>g.Repositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase)).Select(g=>g.Id).ToArray();if(remote.Any(c=>actual.Contains(c.Group.Id)!=c.Add))throw new IOException("分组写入未确认："+ex.Message+"；请核实后再试。",ex);latest=verify;}
                        foreach(var g in latest){g.Repositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));if(actual.Contains(g.Id))g.Repositories.Add(info.FullName);}groups=latest;PutRepo(info);
                    }
                    foreach(var change in changes.Where(x=>x.Group.Source!="GitHub")){var group=settings.LocalGroups.FirstOrDefault(g=>g.Id==change.Group.Id && g.Source==change.Group.Source);if(group==null)throw new IOException("本地分组已被删除。");group.Repositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));if(change.Add)group.Repositories.Add(repo);}
                    Save();SaveCache();BatchResult(repo,true);
                }catch(Exception ex)when(ex is not OperationCanceledException){failures.Add(repo+"："+ex.Message);BatchResult(repo,false,ex.Message);}}
                RebuildGroups();model.Status=$"分组处理完成：{repos.Length-failures.Count} 成功，{failures.Count} 待处理。";if(failures.Count>0)Form.Notes(this,"分组操作结果",string.Join("\n",failures));
            });
        },true);f.ShowDialog();
    }
    private void SelectAll_Click(object sender,RoutedEventArgs e){foreach(var card in model.Cards.Where(c=>c.CanManageStar))card.IsSelected=true;model.Status=$"已选择当前列表中的 {model.Cards.Count(c=>c.IsSelected)} 个项目。";}
    private void ClearSelection_Click(object sender,RoutedEventArgs e){foreach(var card in model.Cards)card.IsSelected=false;}
    private void ExitSelection_Click(object sender,RoutedEventArgs e){ClearSelection_Click(sender,e);model.SelectionMode=false;}
    private void BatchManage_Click(object sender,RoutedEventArgs e)
    {
        if(!ManageReady())return;var repos=model.Cards.Where(c=>c.IsSelected).Select(c=>c.Repository).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(repos.Length==0){
            var choose=new Form(this,"批量管理",620,360);choose.Label("可从当前卡片中勾选，或管理全部缓存项目（包括没有 Release 的项目）。");
            choose.Action("在卡片上选择",()=>{model.SelectionMode=true;choose.Close();model.Status="已进入批量选择，勾选项目后再次点击“批量管理”。";return Task.CompletedTask;},true);
            choose.Action("从全部项目选择",()=>{choose.Close();ProjectPicker();return Task.CompletedTask;});choose.Action("取消选择",()=>{model.SelectionMode=false;foreach(var card in model.Cards)card.IsSelected=false;choose.Close();return Task.CompletedTask;});choose.ShowDialog();return;
        }
        BatchManagementDialog(repos);
    }
    private void BatchManagementDialog(string[] repos)
    {
        var f=new Form(this,"批量管理",620,420);f.Label($"已选择 {repos.Length} 个项目。");
        if(repos.Length==1)f.Action("管理 Star",()=>{f.Close();StarDialog(repos[0]);return Task.CompletedTask;});
        f.Action("调整分组",()=>{f.Close();MembershipDialog(repos,true);return Task.CompletedTask;},true);
        f.Action("取消 Star",async()=>{
            if(!ManageReady(true))return;if(DialogMessage.Show(f,$"取消 {repos.Length} 个项目的 GitHub Star？保留本地关注及本机软件关联。","批量取消 Star",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            f.Close();await Execute("批量管理","取消 Star · "+repos.Length+" 个项目",async ct=>{
                var failed=new List<string>();foreach(var repo in repos){ct.ThrowIfCancellationRequested();try{await api.SetStar(repo,false,ct);var info=repositories.FirstOrDefault(r=>r.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase));if(info!=null)PutRepo(info with{Starred=false});foreach(var g in groups)g.Repositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));PruneUnfollowed(repo);SaveCache();Save();BatchResult(repo,true);}catch(Exception ex)when(ex is not OperationCanceledException){failed.Add(repo+"："+ex.Message);BatchResult(repo,false,ex.Message);}}
                RebuildGroups();model.Status=$"取消 Star：{repos.Length-failed.Count} 成功，{failed.Count} 待处理。";if(failed.Count>0)Form.Notes(this,"批量结果",string.Join("\n",failed));
            });
        });f.Action("退出选择",()=>{model.SelectionMode=false;foreach(var card in model.Cards)card.IsSelected=false;f.Close();return Task.CompletedTask;});f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});f.ShowDialog();
    }
    private void ProjectPicker()
    {
        var f=new Form(this,"选择要管理的项目",660,650);f.Label("包含没有 Release、已停止本地关注及本机已关联的项目。选择一个项目可管理 Star；选择多个可批量调整分组或取消 Star。");var search=f.Text("搜索项目");
        var names=repositories.Select(r=>r.FullName).Concat(settings.ExtraRepositories).Concat(settings.HiddenRepositories).Concat(settings.Bindings.Select(b=>b.Repository)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var choices=names.Select(n=>(Name:n,Check:f.Check(n))).ToList();search.TextChanged+=(_,_)=>{foreach(var row in choices)row.Check.Visibility=row.Name.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;};
        f.Action("全选当前结果",()=>{foreach(var row in choices.Where(r=>r.Check.Visibility==Visibility.Visible))row.Check.IsChecked=true;return Task.CompletedTask;});f.Action("清空",()=>{foreach(var row in choices)row.Check.IsChecked=false;return Task.CompletedTask;});
        f.Action("管理所选",()=>{var selected=choices.Where(r=>r.Check.IsChecked==true).Select(r=>r.Name).ToArray();if(selected.Length==0)throw new IOException("请先选择项目。");f.Close();BatchManagementDialog(selected);return Task.CompletedTask;},true);f.ShowDialog();
    }
    private void AddProjectDialog(string? previewPath=null)
    {
        if(!ManageReady())return;var f=new Form(this,"添加项目",640,640);var address=f.Text("GitHub 仓库名称或链接（owner/repo）");
        var mode=f.Select("关注方式",["添加 GitHub Star","仅本地关注"],api.Token.Length>0?0:1);f.Section("同时加入分组（可多选）");
        var entries=groups.Concat(settings.LocalGroups).Select(g=>(Group:g,Check:f.Check(g.Name+" · "+g.Source))).ToList();
        f.Label("GitHub 分组需要项目已 Star；本地关注独立保存。新建分组请使用左侧 ＋。这里也可恢复已停止本地关注的项目。");
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;});f.Action("添加",async()=>{
            var repo=Rules.RepositoryName(address.Text);GitHubClient.Validate(repo);var star=mode.SelectedIndex==0;var chosen=entries.Where(x=>x.Check.IsChecked==true).Select(x=>x.Group).ToList();
            if(!ManageReady(star || chosen.Any(g=>g.Source=="GitHub")))return;f.Close();
            await Execute("Star管理","添加项目 · "+repo,async ct=>{
                if(chosen.Any(g=>g.Source=="GitHub"))await api.EnsureListWritePermission(ct);var info=await ReadManagedRepo(repo,ct);if(star){await api.SetStar(info.FullName,true,ct);info=info with{Starred=true};}
                PutRepo(info);if(!star && !settings.ExtraRepositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase))settings.ExtraRepositories.Add(info.FullName);
                settings.HiddenRepositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));Save();SaveCache();
                var cloud=chosen.Where(g=>g.Source=="GitHub").ToList();
                if(cloud.Count>0){
                    if(!info.Starred)throw new IOException("项目已添加为本地关注；加入 GitHub 分组前需先 Star。");
                    var latest=await api.Lists(ct);if(cloud.Any(g=>!latest.Any(x=>x.Id==g.Id)))throw new IOException("项目已添加，但所选 GitHub 分组已被删除，请重新选择。");
                    var ids=latest.Where(g=>g.Repositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase)).Select(g=>g.Id).Concat(cloud.Select(g=>g.Id)).Distinct().ToArray();
                    string[] actual;try{actual=await api.UpdateMembership(info.NodeId,ids,ct);}catch(Exception ex)when(ex is not OperationCanceledException){var verify=await api.Lists(ct);actual=verify.Where(g=>g.Repositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase)).Select(g=>g.Id).ToArray();if(cloud.Any(g=>!actual.Contains(g.Id)))throw new IOException("项目已关注，但分组未确认："+ex.Message+"；请在修改分组中重试。",ex);latest=verify;}
                    foreach(var g in latest){g.Repositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));if(actual.Contains(g.Id))g.Repositories.Add(info.FullName);}groups=latest;
                }
                foreach(var group in chosen.Where(g=>g.Source!="GitHub"))if(!group.Repositories.Contains(info.FullName,StringComparer.OrdinalIgnoreCase))group.Repositories.Add(info.FullName);
                Save();SaveCache();RebuildGroups();
                try{var found=await api.Releases(info.FullName,ct,settings.Bindings);releases.RemoveAll(r=>r.Repository.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));releases.AddRange(found);SaveCache();PlanUpdates();Display();model.Status=found.Count==0?"项目已添加，作者尚未发布 Release。":"项目已添加，已获取该项目版本。";}catch(Exception ex)when(ex is not OperationCanceledException){model.Status="项目已添加；版本读取待重试："+ex.Message;}
            });
        },true);if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}





