using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private bool HasLocalFollow(string repo)=>settings.ExtraRepositories.Contains(repo,StringComparer.OrdinalIgnoreCase)||(!settings.HiddenRepositories.Contains(repo,StringComparer.OrdinalIgnoreCase)&&repositories.Any(r=>!r.Starred&&r.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase)));
    private void RecordFollowRemoval(string repo,FollowKind kind)
    {
        var entry=FollowHistory.Record(settings,repo,kind,groups.Concat(settings.LocalGroups),DateTimeOffset.UtcNow);
        if(entry.Groups.Any(g=>g.Source=="GitHub"))settings.FollowHistory[0]=entry with{Note="原 GitHub 分组来自最近一次同步，恢复时核对。"};
    }
    private void SetLocalFollow(string repo,bool followed,bool? previouslyFollowed=null,bool persist=true)
    {
        var before=previouslyFollowed??HasLocalFollow(repo);if(before&&!followed)RecordFollowRemoval(repo,FollowKind.Local);
        settings.ExtraRepositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));
        if(followed){settings.ExtraRepositories.Add(repo);settings.HiddenRepositories.RemoveAll(r=>r.Equals(repo,StringComparison.OrdinalIgnoreCase));}
        PruneUnfollowed(repo);if(persist){Save();SaveCache();}
    }
    private async Task<Repository> SetManagedStar(string repo,bool desired,CancellationToken ct)
    {
        var info=await ReadManagedRepo(repo,ct);
        if(info.Starred!=desired){await api.SetStar(info.FullName,desired,ct);if(!desired)RecordFollowRemoval(info.FullName,FollowKind.GitHubStar);}
        info=info with{Starred=desired};PutRepo(info);
        if(desired)settings.HiddenRepositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));
        else foreach(var group in groups)group.Repositories.RemoveAll(r=>r.Equals(info.FullName,StringComparison.OrdinalIgnoreCase));
        Save();SaveCache();return info;
    }
    private async Task RestoreFollowEntry(FollowHistoryEntry entry,FollowKind target,CancellationToken ct)
    {
        if(entry.Account!=settings.Account)throw new IOException("账号已改变，请重新打开历史记录。");
        if(target==FollowKind.Local){FollowHistory.RestoreLocal(settings,entry,DateTimeOffset.UtcNow);if(!repositories.Any(r=>r.FullName.Equals(entry.Repository,StringComparison.OrdinalIgnoreCase)))PutRepo(new(entry.Repository,"",false));Save();SaveCache();return;}
        await SetManagedStar(entry.Repository,true,ct);var note="";
        // Persist the confirmed Star before attempting the independent list update.
        FollowHistory.MarkRestored(settings,entry,FollowKind.GitHubStar,DateTimeOffset.UtcNow,"Star 已恢复；原分组尚未核对。");Save();
        try{
            var snapshots=entry.Groups.Where(g=>g.Source=="GitHub").ToArray();
            if(snapshots.Length>0){
                var latest=await api.Lists(ct);var available=snapshots.Where(s=>latest.Any(g=>g.Id==s.Id)).Select(s=>s.Id).ToArray();var info=repositories.First(r=>r.FullName.Equals(entry.Repository,StringComparison.OrdinalIgnoreCase));
                var desired=latest.Where(g=>g.Repositories.Contains(entry.Repository,StringComparer.OrdinalIgnoreCase)).Select(g=>g.Id).Concat(available).Distinct().ToArray();
                if(available.Length>0){var actual=await api.UpdateMembership(info.NodeId,desired,ct);foreach(var g in latest){g.Repositories.RemoveAll(r=>r.Equals(entry.Repository,StringComparison.OrdinalIgnoreCase));if(actual.Contains(g.Id))g.Repositories.Add(entry.Repository);}}
                groups=latest;if(available.Length<snapshots.Length)note=$"{snapshots.Length-available.Length} 个原 GitHub 分组已不存在，未重建。";
            }
            foreach(var snapshot in entry.Groups.Where(g=>g.Source!="GitHub")){var group=settings.LocalGroups.FirstOrDefault(g=>g.Id==snapshot.Id&&g.Source==snapshot.Source);if(group!=null&&!group.Repositories.Contains(entry.Repository,StringComparer.OrdinalIgnoreCase))group.Repositories.Add(entry.Repository);}
        }catch(Exception ex)when(ex is not OperationCanceledException){note="Star 已恢复；分组待核对："+ex.Message;}
        FollowHistory.MarkRestored(settings,entry,FollowKind.GitHubStar,DateTimeOffset.UtcNow,note);Save();SaveCache();
    }
    private void FollowHistoryDialog(Window? owner=null,string? previewPath=null)
    {
        var account=settings.Account;var f=new Form(owner??this,"取消关注历史",900,720);f.Label("记录在 Gitstar 中取消的 Star 和本地关注，可按需恢复。账号分别保存；最多保留最近 500 条。旧版隐藏记录仅提供恢复入口，不推测取消时间或原关注方式。");
        var search=f.Text("搜索仓库");var pending=f.Check("仅显示待恢复或待核对的记录",true);var list=new StackPanel();f.Fields.Children.Add(list);
        void Render(){list.Children.Clear();var entries=FollowHistory.Entries(settings).Where(e=>(!pending.IsChecked.GetValueOrDefault()||e.RestoredAt==null||e.Note.Contains("分组待核对")||e.Note.Contains("尚未核对"))&&e.Repository.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
            if(entries.Length==0)list.Children.Add(ProjectText("没有符合条件的取消关注记录。",13,true));
            foreach(var entry in entries){var box=FollowSection(list,entry.Repository);box.Children.Add(ProjectText(entry.SourceLabel+" · "+(entry.RemovedAt is {} time?time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"取消时间未知"),12,true));if(entry.Groups.Count>0)box.Children.Add(ProjectText("原分组："+string.Join("、",entry.Groups.Select(g=>g.Name)),12,true));if(entry.RestoredAt is {} restored)box.Children.Add(ProjectText($"恢复时间：{restored.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {(entry.RestoredAs==FollowKind.GitHubStar?"GitHub Star":"本地关注")}",12,true));if(entry.Note.Length>0)box.Children.Add(ProjectText(entry.Note,12,true));var actions=new WrapPanel();box.Children.Add(actions);
                void Action(string label,FollowKind target){var button=new Button{Content=label,Margin=new Thickness(0,0,8,0)};button.IsEnabled=target==FollowKind.Local||api.Token.Length>0;button.ToolTip=target==FollowKind.GitHubStar&&api.Token.Length==0?"请先登录 GitHub":label;button.Click+=async(_,_)=>{if(settings.Account!=account)throw new IOException("账号已改变，请重新打开历史记录。");button.IsEnabled=false;try{await Execute("Star管理","恢复关注 · "+entry.Repository,async ct=>{await RestoreFollowEntry(entry,target,ct);RebuildGroups();model.Status="已恢复关注；点击检查更新获取该仓库版本。";});Render();}finally{button.IsEnabled=true;}};actions.Children.Add(button);}
                Action("恢复 Star",FollowKind.GitHubStar);Action("恢复本地关注",FollowKind.Local);
            }
        }
        search.TextChanged+=(_,_)=>Render();pending.Checked+=(_,_)=>Render();pending.Unchecked+=(_,_)=>Render();Render();f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}
