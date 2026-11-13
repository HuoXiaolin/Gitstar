using System.Diagnostics;
using Forms=System.Windows.Forms;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task RefreshAsync(bool background=false,bool matchedOnly=false)
    {
        if(refreshing||quitting){if(refreshing&&!background)model.Status="正在检查，已完成的结果会陆续显示，可直接下载或安装。";return;}
        if(background&&!RefreshSchedule.CanStartAutomatic(refreshing,busy,batchPublishing,quitting,clearingCache))return;
        if(matchedOnly&&settings.Bindings.Count==0){model.Status="尚未关联本机软件，请先使用自动匹配 / 批量关联。";return;}
        if(!matchedOnly)lastAttempt=DateTimeOffset.UtcNow;
        await Execute("检查",matchedOnly?"检查已关联软件":background?"后台自动检查关注项目":"手动检查关注项目",async parent=>{
            using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(parent);var ct=lifetime.Token;
            using var repositorySlots=new SemaphoreSlim(RepositoryChecks.DefaultConcurrency);
            var account=CacheKey;var client=api;var clock=Stopwatch.StartNew();var warnings=new List<string>();
            var checkedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var active=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bindings=settings.Bindings.Select(binding=>binding.Snapshot()).ToArray();var shown=model.Cards.Select(card=>card.Repository).ToArray();
            var completed=0;var renderedAt=TimeSpan.FromSeconds(-1);var savedAt=TimeSpan.Zero;var dirty=false;
            var localDone=false;var starsDone=matchedOnly||client.Token.Length==0;var groupsDone=starsDone;var starsSucceeded=starsDone;
            void Current(){ct.ThrowIfCancellationRequested();if(exit||quitting||account!=CacheKey||client!=api)throw new OperationCanceledException(ct);}
            void Publish(bool force=false)
            {
                Current();dirty=true;if(!force&&clock.Elapsed-renderedAt<TimeSpan.FromMilliseconds(600))return;renderedAt=clock.Elapsed;dirty=false;
                PlanUpdates();RebuildGroups(false);if(!projectsPage&&!rankingsPage)Display();
                model.Status=$"正在检查 {completed}/{checkedNames.Count} · {active.Count} 个并行 · 已用 {clock.Elapsed.TotalSeconds:0} 秒"+(!localDone?" · 本机复检中":"")+(!starsDone?" · 收藏同步中":"")+(!groupsDone?" · 分组同步中":"")+" · 已完成结果可直接使用";
                if(clock.Elapsed-savedAt>=TimeSpan.FromSeconds(5)){SaveCache();savedAt=clock.Elapsed;}
            }
            var presentation=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};
            presentation.Tick+=(_,_)=>{if(dirty&&!ct.IsCancellationRequested&&!exit&&!quitting&&account==CacheKey&&client==api)Publish();};presentation.Start();
            async Task Guard(Func<Task> work){try{await work();}catch{await lifetime.CancelAsync();throw;}}
            async Task CheckRepository(string name,CancellationToken token)
            {
                Current();active.Add(name);Publish();
                async Task Metadata()
                {
                    try{var known=repositories.FirstOrDefault(repo=>repo.FullName.Equals(name,StringComparison.OrdinalIgnoreCase));
                        if(known==null||!known.Starred||matchedOnly||client.Token.Length==0){var fresh=await client.Repo(name,token,background:true);Current();known=repositories.FirstOrDefault(repo=>repo.FullName.Equals(name,StringComparison.OrdinalIgnoreCase));repositories.RemoveAll(repo=>repo.FullName.Equals(name,StringComparison.OrdinalIgnoreCase));repositories.Add(fresh with{Starred=known?.Starred??false});}
                    }catch(Exception error)when(error is not OperationCanceledException&&error is not GitHubRateLimitException){warnings.Add(name+"：项目资料未刷新，保留上次数据。"+error.Message);}
                }
                async Task Versions()
                {
                    try{var started=DateTimeOffset.UtcNow;var found=await client.Releases(name,token,bindings);Current();releases.RemoveAll(release=>release.Repository.Equals(name,StringComparison.OrdinalIgnoreCase));releases.AddRange(found);ReconcilePendingInstallations(name,found,started);Publish();}
                    catch(Exception error)when(error is not OperationCanceledException&&error is not GitHubRateLimitException){warnings.Add(name+"："+error.Message);}
                }
                try{await Task.WhenAll(Guard(Metadata),Guard(Versions));}
                finally{active.Remove(name);completed++;if(!ct.IsCancellationRequested&&!quitting)Publish();}
            }
            Task CheckNames(IEnumerable<string> names)
            {
                var pending=RepositoryChecks.Prioritize(names,bindings.Where(binding=>!binding.Disabled).Select(binding=>binding.Repository),shown).Where(checkedNames.Add).ToArray();
                return Guard(()=>RepositoryChecks.Run(pending,async(name,token)=>{
                    await repositorySlots.WaitAsync(token);
                    try{await CheckRepository(name,token);}finally{repositorySlots.Release();}
                },ct));
            }
            async Task Local()
            {
                try{await ScanMatchedAsync(ct);Current();}
                catch(Exception error)when(error is not OperationCanceledException){warnings.Add("本机版本复检未完成，保留上次结果。"+error.Message);}
                localDone=true;Publish(true);
            }
            async Task Stars()
            {
                if(starsDone)return;
                var pageChecks=new List<Task>();
                try{
                    try{
                        var found=await client.Stars(ct,page=>{
                            Current();foreach(var repo in page){repositories.RemoveAll(old=>old.FullName.Equals(repo.FullName,StringComparison.OrdinalIgnoreCase));repositories.Add(repo);}Publish();
                            pageChecks.Add(CheckNames(page.Select(repo=>repo.FullName)));
                        },background:true);
                        Current();var names=found.Select(repo=>repo.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        // Keep local/bound metadata; only a complete response may remove a GitHub follow.
                        repositories=repositories.Where(repo=>!names.Contains(repo.FullName)).Select(repo=>repo with{Starred=false}).Concat(found).ToList();starsSucceeded=true;
                    }catch(Exception error)when(error is not OperationCanceledException&&error is not GitHubRateLimitException){warnings.Add("收藏同步未完成，已继续检查已知项目。"+error.Message);}
                    starsDone=true;Publish(true);pageChecks.Add(CheckNames(RepositoryChecks.Targets(false,repositories.Where(repo=>repo.Starred),settings)));
                }catch{
                    await lifetime.CancelAsync();
                    // Observe every page task while retaining the listing error rather than a sibling cancellation.
                    try{await Task.WhenAll(pageChecks);}catch{}
                    throw;
                }
                await Task.WhenAll(pageChecks);
            }
            async Task Groups()
            {
                if(groupsDone)return;
                try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));var found=await client.Lists(timeout.Token);Current();groups=found;}
                catch(OperationCanceledException)when(!ct.IsCancellationRequested){warnings.Add("分组同步超时，已保留原分组。");}
                catch(Exception error)when(error is not OperationCanceledException&&error is not GitHubRateLimitException){warnings.Add("分组同步未完成。"+error.Message);}
                groupsDone=true;Publish(true);
            }
            if(!matchedOnly)recentCacheCleared=false;
            var initial=RepositoryChecks.Targets(matchedOnly,repositories.Where(repo=>repo.Starred),settings);
            var local=Guard(Local);var versions=CheckNames(initial);var stars=Guard(Stars);var groupsTask=Guard(Groups);
            var messages=NotificationSchedule.FollowsUpdates(settings.NotificationIntervalMinutes)?RefreshNotificationBadge(ct):Task.CompletedTask;
            try{await Task.WhenAll(local,versions,stars,groupsTask,messages);}
            finally{presentation.Stop();if(!exit&&!quitting&&account==CacheKey){SaveCache();PlanUpdates();RebuildGroups(false);if(!projectsPage&&!rankingsPage)Display();}}
            Current();var targets=RepositoryChecks.Targets(matchedOnly,repositories.Where(repo=>repo.Starred),settings);
            if(!matchedOnly&&starsSucceeded)releases.RemoveAll(release=>!targets.Contains(release.Repository,StringComparer.OrdinalIgnoreCase));
            SaveCache();Publish(true);if(!matchedOnly&&warnings.Count==0)settings.LastCheck=DateTimeOffset.UtcNow;
            var newUpdates=updates.Where(update=>!settings.Notified.Contains($"{update.App.Id}/{update.Release.Id}")).ToList();
            if(newUpdates.Count>0){notificationLocal=true;tray.ShowBalloonTip(6000,"Gitstar",$"{newUpdates.Count} 个已关联软件有新版本，点击打开查看。",Forms.ToolTipIcon.Info);settings.Notified.AddRange(newUpdates.Select(update=>$"{update.App.Id}/{update.Release.Id}"));}
            if(!matchedOnly){Release? own=null;try{own=checkedNames.Contains(AppInfo.Repository)?Rules.SelectRelease(releases.Where(release=>release.Repository.Equals(AppInfo.Repository,StringComparison.OrdinalIgnoreCase)),new Binding()):await client.LatestGitstarRelease(ct);}catch(Exception error)when(error is IOException or HttpRequestException){}Current();if(own!=null&&Rules.CompareVersions(own.Tag,typeof(Program).Assembly.GetName().Version?.ToString()??"1.0.0.0")>0&&!settings.Notified.Contains("self/"+own.Id)){notificationLocal=false;tray.ShowBalloonTip(6000,"Gitstar 有新版本",own.Tag+" · 请在最近更新查看日志并手动下载安装。",Forms.ToolTipIcon.Info);settings.Notified.Add("self/"+own.Id);}}
            Save();model.Status=warnings.Count==0?$"检查完成 · {targets.Length} 个项目 · 用时 {clock.Elapsed.TotalSeconds:0} 秒 · {updates.Count} 个软件可更新":$"检查完成 · 用时 {clock.Elapsed.TotalSeconds:0} 秒 · {warnings.Count} 项待处理 · "+string.Join("；",warnings.Take(3));
            if(matchedOnly&&!background)await CleanupInstalledPackages();
        },background);
    }
}
