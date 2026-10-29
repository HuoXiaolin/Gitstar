using System.Windows;
using System.Windows.Controls;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task CompactDatabase(Window owner)
    {
        if(busy||clearingCache)throw new IOException("请等待正在进行的任务结束后整理数据库。");clearingCache=true;
        try{var before=store.StorageUsage();await Execute("缓存清理","整理数据库空间",async ct=>{if(taskCoordinator.Count>1)throw new IOException("有新任务开始，请稍后重试。");ct.ThrowIfCancellationRequested();await Task.Run(store.Compact);var after=store.StorageUsage();model.Status="数据库已整理："+CacheBytes(before.Database)+" → "+CacheBytes(after.Database);currentOperation.Value!.Results.Add(model.Status+"；数据内容全部保留。");},resources:["cache","projects:"+settings.Account]);}
        finally{clearingCache=false;}
    }
    private static string CacheBytes(long bytes)=>bytes<0?"无法统计":bytes<1024?$"{bytes} B":bytes<1024*1024?$"{bytes/1024.0:F1} KB":$"{bytes/1024.0/1024:F1} MB";
    private void ApplyCacheCleared(CacheModule module)
    {
        if(module==CacheModule.RecentUpdates){recentCacheCleared=true;repositories.Clear();groups.Clear();releases=store.Get(CacheKey+"/matched-releases",new List<Release>());RebuildGroups();}
        if(module==CacheModule.InstalledApps){installed.Clear();installedFresh=false;installedScan=null;}
        if(module==CacheModule.MyProjects){myProjects.Clear();projectVersions.Clear();releaseDirectoryStates.Clear();projectStates.Clear();projectsSynced=null;selectedProject=null;}
        if(module==CacheModule.FinishedTasks)operations.RemoveAll(operation=>CacheRules.Finished(operation));
        if(module==CacheModule.Rankings){rankingGeneration++;rankingRequest?.Cancel();rankingData=null;}
        PlanUpdates();Display();
    }
    private void CacheCleanupDialog(Window? owner=null,string? previewPath=null)
    {
        var f=new Form(owner??this,"按模块清理缓存",860,710);var checks=new Dictionary<CacheModule,CheckBox>();var rows=new StackPanel();var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};message.SetResourceReference(TextBlock.ForegroundProperty,"Muted");var accountKey=CacheKey;var closed=false;Button? cleanButton=null;f.Closed+=(_,_)=>closed=true;
        f.Label("当前账号："+(settings.Account.Length==0?"未登录":settings.Account)+"。模块大小为缓存内容大小，头像为此电脑的共享缓存。");var storage=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontSize=12};storage.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(storage);f.Fields.Children.Add(rows);f.Fields.Children.Add(message);
        void Selection(){if(cleanButton!=null)cleanButton.IsEnabled=!clearingCache&&checks.Values.Any(check=>check.IsChecked==true);}
        void Render(){var usage=store.StorageUsage();storage.Text="数据库文件："+CacheBytes(usage.Database)+"   ·   有效内容："+CacheBytes(usage.Content)+"   ·   可回收空闲页："+CacheBytes(usage.Free)+"\n“整理数据库空间”保留全部数据；按模块清理会同时回收数据库空间。";var selected=checks.Where(item=>item.Value.IsChecked==true).Select(item=>item.Key).ToHashSet();rows.Children.Clear();checks.Clear();foreach(var summary in CacheCleanupService.Summaries(store,settings.Account)){
            var content=new StackPanel();var check=new CheckBox{Content=summary.Name+"   ·   "+CacheBytes(summary.Bytes)+(summary.Module==CacheModule.FinishedTasks?$"   ·   {summary.Items} 条":""),IsChecked=selected.Contains(summary.Module),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};checks[summary.Module]=check;check.Checked+=(_,_)=>Selection();check.Unchecked+=(_,_)=>Selection();content.Children.Add(check);
            var detail=new TextBlock{Text=summary.Description,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(32,0,0,0),LineHeight=19};detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(detail);
            var card=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),Child=content};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Edge");rows.Children.Add(card);
        }Selection();}
        async Task Clean(){
            if(accountKey!=CacheKey)throw new IOException("账号已改变，请重新打开缓存清理。");if(busy||clearingCache)throw new IOException("请等待正在进行的任务结束后清理缓存。");var selected=checks.Where(item=>item.Value.IsChecked==true).Aggregate(CacheModule.None,(flags,item)=>flags|item.Key);if(selected==CacheModule.None)throw new IOException("请先选择需要清理的模块。");var names=CacheCleanupService.Summaries(store,settings.Account).Where(summary=>selected.HasFlag(summary.Module)).Select(summary=>summary.Name);
            if(DialogMessage.Show(f,"清理以下模块？\n\n"+string.Join("\n",names)+"\n\n登录凭据、设置、源码、下载文件和所有关联配置保留。缓存可通过刷新重新获取；已结束任务记录清理后无法恢复。","清理所选缓存",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            clearingCache=true;var cleanupId="";var completed=false;try{
                foreach(var check in checks.Values)check.IsEnabled=false;message.Text="正在清理缓存并整理数据库空间…";
                foreach(var button in f.Buttons.Children.OfType<Button>())if(button.Content?.ToString()!="关闭")button.IsEnabled=false;
                await Execute("缓存清理","清理所选模块",async ct=>{if(taskCoordinator.Count>1)throw new IOException("有新任务开始，请等待任务结束后重试。");cleanupId=currentOperation.Value!.Id;var result=await CacheCleanupService.Clear(store,settings.Account,selected,ct,ApplyCacheCleared);currentOperation.Value!.Results.Add($"成功：清理 {result.Documents} 组缓存、{result.AvatarFiles} 个头像和 {result.TaskRecords} 条任务记录");foreach(var warning in result.Warnings)currentOperation.Value.Results.Add("待处理："+warning);completed=true;if(!closed)message.Text="清理完成。"+(result.Warnings.Count>0?"\n"+string.Join("\n",result.Warnings):"缓存数据可在对应页面手动刷新。");},resources:["cache","projects:"+settings.Account]);
                if(selected.HasFlag(CacheModule.FinishedTasks)){operations.RemoveAll(operation=>operation.Id==cleanupId&&CacheRules.Finished(operation));SaveOperations();}
            }finally{clearingCache=false;if(!closed){if(!completed)message.Text="清理未完成，请查看任务记录。已完成的清理保留。";Render();foreach(var button in f.Buttons.Children.OfType<Button>())button.IsEnabled=true;Selection();}}
        }
        f.Action("全选",()=>{foreach(var check in checks.Values)check.IsChecked=true;return Task.CompletedTask;});f.Action("清除选择",()=>{foreach(var check in checks.Values)check.IsChecked=false;return Task.CompletedTask;});f.Action("刷新大小",()=>{if(!clearingCache)Render();return Task.CompletedTask;});f.Action("整理数据库空间",async()=>{await CompactDatabase(f);if(!closed){Render();message.Text=model.Status;}});cleanButton=f.Action("清理所选缓存",Clean,true);f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});Render();if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}
