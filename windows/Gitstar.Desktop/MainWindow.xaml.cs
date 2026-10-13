using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
namespace Gitstar.Desktop;

public partial class MainWindow : Window
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);WindowsServices.FitWindow(this,true);
    }
    private readonly LocalStore store;
    private Settings settings;
    private GitHubClient api;
    private readonly MainModel model=new();
    private List<Repository> repositories=[];
    private List<Release> releases=[];
    private List<StarGroup> groups=[];
    private List<InstalledApp> installed=[];
    private bool installedFresh;
    private Task<List<InstalledApp>>? installedScan;
    private List<UpdateCandidate> updates=[];
    private readonly Dictionary<string,TransferState> transfers=new(StringComparer.OrdinalIgnoreCase);
    private string TransferKey(Release release,bool install)=>$"{settings.Account}/{release.Repository}/{release.Id}/{install}";
    private TransferState TransferFor(Release release,bool install) {var key=TransferKey(release,install);if(!transfers.TryGetValue(key,out var transfer))transfers[key]=transfer=new();return transfer;}
    private readonly List<Operation> operations;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMinutes(5)};
    private readonly TaskCoordinator taskCoordinator=new();
    private readonly AsyncLocal<Operation?> currentOperation=new();
    private readonly Dictionary<string,TransferState> taskTransfers=[];
    private bool busy=>taskCoordinator.Count>0;
    private bool clearingCache,recentCacheCleared;
    private bool refreshing,quitting;
    private bool localPage,exit,ready;
    private bool notificationLocal=true;
    private bool syncingGroups;
    private string? automaticCheckId;
    private DateTimeOffset? lastAttempt;
    private string CacheKey=>"cache/"+(settings.Account.Length>0?settings.Account:"guest");
    private string OperationsKey=>"operations/"+(settings.Account.Length>0?settings.Account:"guest");
    public MainWindow(bool smoke=false)
    {
        InitializeComponent();DataContext=model;
        store=new(smoke?Path.Combine(Path.GetTempPath(),"Gitstar-render-"+Guid.NewGuid().ToString("N")):null);
        settings=store.Get("settings",new Settings());settings.SelfRepository=AppInfo.Repository;api=new(settings);AvatarCache.Configure(settings,smoke);LoadToken();
        operations=store.Get(OperationsKey,new List<Operation>());
        foreach(var o in operations.Where(x=>x.State is "执行中" or "等待执行"))o.State=o.State=="等待执行"?"已暂停等待":o.Kind=="下载"?"下载已暂停，请重新选择安装包重试":"结果待核实";
        SaveOperations();LoadCache();RebuildGroups();ApplyTheme();AccountLabel();
        tray=new Forms.NotifyIcon {Icon=new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Gitstar.ico"))!.Stream),Text="Gitstar",Visible=!smoke};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Reopen);
        tray.BalloonTipClicked+=(_,_)=>Dispatcher.Invoke(()=>{Reopen();Page(notificationLocal);});
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开 Gitstar",null,(_,_)=>Dispatcher.Invoke(Reopen));
        menu.Items.Add("立即检查更新",null,async(_,_)=>await Dispatcher.InvokeAsync(()=>RefreshAsync()));
        menu.Items.Add("任务",null,(_,_)=>Dispatcher.Invoke(()=>Tasks_Click(this,new RoutedEventArgs())));
        menu.Items.Add("设置",null,(_,_)=>Dispatcher.Invoke(()=>Settings_Click(this,new RoutedEventArgs())));
        menu.Items.Add("彻底退出",null,(_,_)=>Dispatcher.Invoke(RequestExit));tray.ContextMenuStrip=menu;
        Closing+=OnClosing;
        timer.Tick+=async(_,_)=>{if(RefreshSchedule.CanStartAutomatic(refreshing,busy,batchPublishing,quitting,clearingCache) && RefreshSchedule.IsDue(settings.RefreshIntervalHours,settings.LastCheck,lastAttempt,DateTimeOffset.UtcNow))await RefreshAsync(true);};
        Loaded+=async(_,_)=>{
            ready=true;PlanUpdates();Display();
            if(smoke)return;
            if(exit)return;
            if(settings.Startup)WindowsServices.Startup(true);
            if(Environment.GetCommandLineArgs().Contains("--tray"))Hide();
            timer.Start();if(RefreshSchedule.IsDue(settings.RefreshIntervalHours,settings.LastCheck,lastAttempt,DateTimeOffset.UtcNow))await RefreshAsync(true);
            else if(settings.Bindings.Count>0)await Execute("检测","启动时复检已关联软件",async ct=>{model.Status=$"正在复检 {settings.Bindings.Count} 个已关联软件…";await ScanMatchedAsync();ct.ThrowIfCancellationRequested();PlanUpdates();Display();StartupCheckCompleted();},true,resources:["cache"]);
            else model.Status="已加载上次检查结果，可手动检查更新。";
        };
    }
    private void StartupCheckCompleted()=>model.Status=$"启动复检完成 · {settings.Bindings.Count} 个已关联软件 · {updates.Count} 个可更新";
    private void LoadToken() {ConfigureApiCache();api.CredentialsChanged=auth=>CredentialStorage.Save(store,auth);try{api.SetCredentials(CredentialStorage.Load(store));}catch{model.Status="凭据不可读取，请重新登录。";}}
    private void ConfigureApiCache()
    {
        api.ReadReleasePage=key=>store.Get<GitHubClient.ReleasePage?>(CacheKey+"/http-releases/"+key,null);
        api.SaveReleasePage=(key,page)=>store.Set(CacheKey+"/http-releases/"+key,page);
    }
    private async Task ScanInstalledAsync(bool force=false)
    {
        var accountKey=CacheKey;
        if(force && installedScan is {IsCompleted:false})await installedScan;
        if(installedScan==null || installedScan.IsCompleted){var portable=settings.PortableApps.ToArray();var bindings=settings.Bindings.Select(b=>new Binding{AppId=b.AppId,ExecutablePath=b.ExecutablePath}).ToArray();installedScan=Task.Run(()=>WindowsServices.Scan(portable,bindings));}
        var detected=await installedScan;if(exit || accountKey!=CacheKey)return;
        SetInstalled(detected);
    }
    private async Task ScanMatchedAsync()
    {
        var accountKey=CacheKey;var bindings=settings.Bindings.Select(b=>new Binding{AppId=b.AppId,ExecutablePath=b.ExecutablePath}).ToArray();var known=installed.ToArray();var portable=settings.PortableApps.ToArray();
        var fresh=await Task.Run(()=>WindowsServices.ScanMatched(bindings,portable,known));if(exit || accountKey!=CacheKey)return;
        SetInstalled(known.Where(a=>!bindings.Any(b=>InstalledVersion.Find([a],b.AppId)!=null)).Concat(fresh).ToList());
    }
    private void SetInstalled(List<InstalledApp> detected){installed=detected;installedFresh=true;SaveInstalledSnapshot();}
    private void SaveInstalledSnapshot()=>store.Set(CacheKey+"/installed",InstalledSnapshot.Capture(installed,settings.Bindings));
    private void Save(){store.Set("settings",settings);if(installedFresh)SaveInstalledSnapshot();}
    private void SaveOperations()=>store.Set(OperationsKey,operations);
    private void SwitchOperations(){operations.Clear();operations.AddRange(store.Get(OperationsKey,new List<Operation>()));}
    private void LoadCache() {
        PathPreferences.Migrate(settings);store.Set("settings",settings);repositories=store.Get(CacheKey+"/repos",new List<Repository>());recentCacheCleared=store.Get(CacheKey+"/recent-cleared",false);
        var saved=store.Get(CacheKey+"/releases",new List<Release>()).Concat(store.Get(CacheKey+"/matched-releases",new List<Release>())).GroupBy(release=>(release.Repository.ToLowerInvariant(),release.Id)).Select(group=>group.First());releases=ReleaseRetention.Compact(saved,settings.Bindings);
        groups=store.Get(CacheKey+"/groups",new List<StarGroup>());packages=store.Get(PackagesKey,new List<DownloadedPackage>());installed=InstalledSnapshot.Capture(store.Get(CacheKey+"/installed",new InstalledSnapshot(DateTimeOffset.MinValue,[])).Apps,settings.Bindings).Apps;installedFresh=false;LoadProjects();
    }
    private void SaveCache() {releases=ReleaseRetention.Compact(releases,settings.Bindings);PruneTransfers();var bound=settings.Bindings.Select(binding=>binding.Repository).ToHashSet(StringComparer.OrdinalIgnoreCase);store.Set(CacheKey+"/repos",repositories);store.Set(CacheKey+"/releases",recentCacheCleared?new List<Release>():releases);store.Set(CacheKey+"/matched-releases",releases.Where(release=>bound.Contains(release.Repository)).ToList());store.Set(CacheKey+"/groups",groups);store.Set(CacheKey+"/recent-cleared",recentCacheCleared);}
    private void PruneTransfers()
    {
        var retained=releases.SelectMany(r=>new[]{TransferKey(r,false),TransferKey(r,true)}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var key in transfers.Where(p=>!p.Value.Active && !p.Value.NeedsDetection && !retained.Contains(p.Key)).Select(p=>p.Key).ToArray())transfers.Remove(key);
    }
    private void AccountLabel()=>model.Account=settings.Account.Length==0?"登录 GitHub":settings.Account;
    private void RebuildGroups()
    {
        var selected=(GroupSidebar.SelectedItem as GroupEntry)?.Id??"all";var allGroups=groups.Concat(settings.LocalGroups).ToList();
        var repoNames=repositories.Select(r=>r.FullName).Concat(releases.Select(r=>r.Repository)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        syncingGroups=true;
        try{
            model.Groups.Clear();model.Groups.Add(new("all","全部收藏","所有项目",repoNames.Count));
            model.Groups.Add(new("ungrouped","未分组","尚未加入分组",repoNames.Count(r=>!allGroups.Any(g=>g.Repositories.Contains(r,StringComparer.OrdinalIgnoreCase)))));
            foreach(var g in allGroups)model.Groups.Add(new(g.Source+"/"+g.Id,g.Name,g.Source,g.Repositories.Distinct(StringComparer.OrdinalIgnoreCase).Count()));
            GroupFilter.ItemsSource=model.Groups.Select(g=>g.Name).ToArray();
            var index=model.Groups.ToList().FindIndex(g=>g.Id==selected);GroupFilter.SelectedIndex=Math.Max(index,0);GroupSidebar.SelectedIndex=GroupFilter.SelectedIndex;
        }finally{syncingGroups=false;}Display();
    }
    private void PlanUpdates()
    {
        updates=[];
        foreach(var b in settings.Bindings.Where(b=>b.Enabled(DateTimeOffset.UtcNow))) {
            var app=InstalledVersion.Find(installed,b.AppId); if(app==null)continue;
            var r=Rules.SelectRelease(releases.Where(r=>r.Repository.Equals(b.Repository,StringComparison.OrdinalIgnoreCase)),b); if(r==null||r.Tag==b.IgnoredTag)continue;
            if(Rules.CompareVersions(Rules.VersionTag(r,b),app.Version)>0)updates.Add(new(app,b,r,Rules.SelectAsset(r,b)));
        }
        if(tray!=null){tray.Text=$"Gitstar · {updates.Count} 个软件可更新";var old=tray.Icon;tray.Icon=TrayBadge.Create(updates.Count);old?.Dispose();}
    }
    private void Display()
    {
        if(!ready)return;
        if(projectsPage){RenderProjects();return;}
        var search=Search.Text.Trim();var channel=ChannelFilter.SelectedIndex;
        var groupIndex=GroupFilter.SelectedIndex;var allGroups=groups.Concat(settings.LocalGroups).ToList();
        IEnumerable<Card> cards=localPage?updates.Select(u=>new Card {Release=u.Release,Update=u,Transfer=TransferFor(u.Release,true)}):releases.Select(r=>new Card {Release=r});
        if(!localPage&&recentCacheCleared)cards=[];
        cards=cards.Where(c=>channel==1 || (channel==2?c.Release.Prerelease:!c.Release.Prerelease));
        if(!localPage)cards=cards.Where(c=>!settings.HiddenRepositories.Contains(c.Repository,StringComparer.OrdinalIgnoreCase));
        if(!localPage&&groupIndex==1)cards=cards.Where(c=>!allGroups.Any(g=>g.Repositories.Contains(c.Repository,StringComparer.OrdinalIgnoreCase)));
        if(!localPage&&groupIndex>=2 && groupIndex-2<allGroups.Count)cards=cards.Where(c=>allGroups[groupIndex-2].Repositories.Contains(c.Repository,StringComparer.OrdinalIgnoreCase));
        cards=cards.Where(c=>(c.Title+" "+c.Repository+" "+c.Version).Contains(search,StringComparison.OrdinalIgnoreCase));
        if(!localPage)cards=cards.GroupBy(c=>c.Repository).Select(g=>g.OrderByDescending(c=>c.Release.Published).First());
        model.Cards.Clear();foreach(var card in cards.OrderByDescending(c=>c.Release.Published)){card.Transfer=TransferFor(card.Release,localPage);model.Cards.Add(card);}
        var now=DateTimeOffset.UtcNow;
        model.CountCaption=localPage?"本机待更新软件":"最近 7 天更新";
        model.Count=localPage?$"{model.Cards.Count} 个可更新":$"{Rules.RecentProjectCount(model.Cards.Select(c=>c.Release),now)} 个项目";
        CountPanel.Background=(Brush)Resources["AccentSoft"];
        model.Empty=localPage?"没有已确认的软件更新":"还没有匹配的版本";
        model.Hint=localPage?$"已检测 {installed.Count} 个软件，已关联 {settings.Bindings.Count} 个。点击「自动匹配 / 批量关联」集中确认软件仓库；未匹配项仍可手动管理。":"登录 GitHub 同步收藏，或添加仓库；也可调整左侧分组、搜索和版本通道。";
        EmptyPanel.Visibility=model.Cards.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    private void Filter_Changed(object sender,RoutedEventArgs e){if(syncingGroups)return;if(GroupSidebar!=null && GroupFilter!=null){syncingGroups=true;try{GroupSidebar.SelectedIndex=GroupFilter.SelectedIndex;}finally{syncingGroups=false;}}Display();}
    private void Sidebar_Changed(object sender,SelectionChangedEventArgs e){if(syncingGroups||GroupFilter==null)return;syncingGroups=true;try{GroupFilter.SelectedIndex=GroupSidebar.SelectedIndex;}finally{syncingGroups=false;}Display();}
    private void Recent_Click(object sender,RoutedEventArgs e)=>Page(false);
    private void Local_Click(object sender,RoutedEventArgs e)=>Page(true);
    private void Page(bool local) {projectsPage=false;ProjectsHost.Visibility=Visibility.Collapsed;UpdatesToolbar.Visibility=UpdatesContent.Visibility=Visibility.Visible;ProjectsButton.Background=(Brush)Resources["Surface"];ProjectsButton.Foreground=(Brush)Resources["Ink"];localPage=local;if(local)model.SelectionMode=false;BatchButton.Visibility=local?Visibility.Collapsed:Visibility.Visible;SidebarColumn.Width=new GridLength(local?0:208);GroupPanel.Visibility=local?Visibility.Collapsed:Visibility.Visible;AddButton.Content=local?"自动匹配 / 批量关联":"添加项目";model.Title=local?"本机软件更新":"关注项目的新版本";model.Subtitle=local?"查看已关联软件的新版本，按需下载并安装。":"按收藏分组查看更新内容，将所需安装包保存到指定文件夹。";GroupFilter.IsEnabled=!local;SetPageAccent(local?1:0);Display();}
    private async void Refresh_Click(object sender,RoutedEventArgs e)=>await RefreshAsync(matchedOnly:localPage);
    private async Task RefreshAsync(bool background=false,bool matchedOnly=false)
    {
        if(refreshing||quitting)return;
        if(background&&!RefreshSchedule.CanStartAutomatic(refreshing,busy,batchPublishing,quitting,clearingCache))return;
        if(matchedOnly && settings.Bindings.Count==0){model.Status="尚未关联本机软件，请先使用自动匹配 / 批量关联。";return;}
        if(api.Token.Length==0 && settings.ExtraRepositories.Count==0 && settings.Bindings.Count==0 && settings.SelfRepository.Length==0){await ScanInstalledAsync(true);if(exit)return;if(!background)model.Status="已重新扫描本机软件；登录 GitHub 或添加仓库后可检查新版本。";return;}
        if(!matchedOnly)lastAttempt=DateTimeOffset.UtcNow;
        await Execute("检查",matchedOnly?"检查已关联软件":background?"后台自动检查关注项目":"手动检查关注项目",async ct=>{
            var clock=Stopwatch.StartNew();var warnings=new List<string>();model.Status=matchedOnly?"正在复检已关联软件…":"正在重新检测本机版本…";if(matchedOnly)await ScanMatchedAsync();else await ScanInstalledAsync(true);if(exit)return;ct.ThrowIfCancellationRequested();PlanUpdates();Display();model.Status=matchedOnly?"正在检查已关联仓库…":"正在同步收藏和分组…";
            if(!matchedOnly && api.Token.Length>0) {
                repositories=await api.Stars(ct);
                try{groups=await api.Lists(ct);}catch(Exception e)when(e is not OperationCanceledException && e is not GitHubRateLimitException){warnings.Add(e.Message);}
            }
            var names=RepositoryChecks.Targets(matchedOnly,repositories,settings).ToList();
            if(!matchedOnly)recentCacheCleared=false;
            var completed=0;var active=new HashSet<string>();var savedAt=TimeSpan.Zero;
            void Progress()=>model.Status=$"正在检查 {completed}/{names.Count} · {active.Count} 个并行 · 已用 {clock.Elapsed.TotalSeconds:0} 秒 · "+string.Join(" / ",active);
            try{await RepositoryChecks.Run(names,async(name,token)=>{
                active.Add(name);Progress();
                try{if(!repositories.Any(r=>r.FullName.Equals(name,StringComparison.OrdinalIgnoreCase)))repositories.Add(await api.Repo(name,token));var found=await api.Releases(name,token,settings.Bindings);releases.RemoveAll(r=>r.Repository.Equals(name,StringComparison.OrdinalIgnoreCase));releases.AddRange(found);}
                catch(Exception e)when(e is not OperationCanceledException && e is not GitHubRateLimitException){warnings.Add(name+"："+e.Message);}
                finally{active.Remove(name);completed++;Progress();if(clock.Elapsed-savedAt>=TimeSpan.FromSeconds(5)){SaveCache();savedAt=clock.Elapsed;}}
            },ct);}finally{SaveCache();}
            // Repositories removed from Stars are not retained unless explicitly subscribed or bound.
            if(!matchedOnly)releases.RemoveAll(r=>!names.Contains(r.Repository,StringComparer.OrdinalIgnoreCase));SaveCache();RebuildGroups();
            if(exit)return;
            PlanUpdates();Display();
            if(!matchedOnly && warnings.Count==0)settings.LastCheck=DateTimeOffset.UtcNow;
            var newUpdates=updates.Where(u=>!settings.Notified.Contains($"{u.App.Id}/{u.Release.Id}")).ToList();
            if(newUpdates.Count>0) {notificationLocal=true;tray.ShowBalloonTip(6000,"Gitstar",$"{newUpdates.Count} 个已关联软件有新版本，点击打开查看。",Forms.ToolTipIcon.Info);settings.Notified.AddRange(newUpdates.Select(u=>$"{u.App.Id}/{u.Release.Id}"));}
            if(!matchedOnly){Release? own=null;try{own=names.Contains(AppInfo.Repository,StringComparer.OrdinalIgnoreCase)?Rules.SelectRelease(releases.Where(r=>r.Repository.Equals(AppInfo.Repository,StringComparison.OrdinalIgnoreCase)),new Binding()):await api.LatestGitstarRelease(ct);}catch(Exception ex)when(ex is IOException or System.Net.Http.HttpRequestException){/* Optional app update checks do not invalidate subscribed-project results; About exposes check errors. */}if(own!=null && Rules.CompareVersions(own.Tag,typeof(Program).Assembly.GetName().Version?.ToString()??"1.0.0.0")>0 && !settings.Notified.Contains("self/"+own.Id)){notificationLocal=false;tray.ShowBalloonTip(6000,"Gitstar 有新版本",own.Tag+" · 请在最近更新查看日志并手动下载安装。",Forms.ToolTipIcon.Info);settings.Notified.Add("self/"+own.Id);}}
            Save();model.Status=warnings.Count==0?$"检查完成 · {names.Count} 个项目 · 用时 {clock.Elapsed.TotalSeconds:0} 秒 · {updates.Count} 个软件可更新":$"检查完成 · 用时 {clock.Elapsed.TotalSeconds:0} 秒，{warnings.Count} 项待处理："+string.Join("；",warnings.Take(3));
            if(matchedOnly && !background)await CleanupInstalledPackages();
        },background);
    }
    private async Task Execute(string kind,string description,Func<CancellationToken,Task> work,bool background=false,string[]? resources=null,TransferState? transfer=null)
    {
        if(quitting)return;
        if((kind is "自动发布" or "发布" or "源码上传" or "项目删除") && automaticCheckId is {} scanning)taskCoordinator.Pause(scanning);
        var op=new Operation {Kind=kind,Description=description,State="等待执行"};operations.Add(op);
        using var run=taskCoordinator.Start(op.Id,resources??["cache"]);if(transfer!=null)taskTransfers[op.Id]=transfer;SaveOperations();
        var previous=currentOperation.Value;currentOperation.Value=op;var started=false;
        if(kind=="检查"){refreshing=true;if(background)automaticCheckId=op.Id;}
        try {await run.WaitAsync();started=true;op.State="执行中";SaveOperations();var runningStatus=kind+" · "+description+" · 正在处理…";model.Status=runningStatus;await work(run.Token);op.State=op.Results.Any(r=>r.StartsWith("待处理"))?(op.Results.Any(r=>r.StartsWith("成功"))?"部分完成，请核实待处理项":"未完成，请核实待处理项"):"完成";if(model.Status==runningStatus)model.Status=description+" · "+op.State;}        catch(OperationCanceledException){
            var remote=kind is "自动发布" or "发布" or "发布包管理" or "项目删除" or "源码上传" or "Star管理" or "分组管理" or "批量管理" or "项目管理";
            op.State=!started?"已"+run.StopReason+"等待":remote?"已"+run.StopReason+" · 远程结果待核实":run.StopReason=="取消"?"已取消":kind=="下载"?"已暂停（下载重试会重新开始）":"已暂停";
            if(run.StopReason=="取消" && kind=="下载" && op.File.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)){try{if(File.Exists(op.File))File.Delete(op.File);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){op.Results.Add("临时文件清理待处理："+ex.Message);}}
            model.Status=description+" · "+op.State;
        }        catch(Exception e){op.State=kind is "自动发布" or "发布" or "发布包管理" or "项目删除" or "源码上传" or "Star管理" or "分组管理" or "批量管理" or "项目管理"?"结果待核实："+e.Message:"失败："+e.Message;model.Status=e.Message;if(!background)DialogMessage.Show(this,e.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{if(started && (kind is "Star管理" or "分组管理" or "批量管理" or "项目管理")){Save();SaveCache();RebuildGroups();PlanUpdates();Display();}op.Updated=DateTimeOffset.Now;taskTransfers.Remove(op.Id);SaveOperations();currentOperation.Value=previous;if(kind=="检查"){refreshing=false;if(automaticCheckId==op.Id){automaticCheckId=null;if(run.Token.IsCancellationRequested)lastAttempt=null;}}run.Dispose();if(settings.KeepLatestPackages && !quitting && !exit)_=Dispatcher.BeginInvoke(async()=>await CleanupOldPackages());}
    }
    private async void Login_Click(object sender,RoutedEventArgs e)
    {
        if(busy||batchPublishing){DialogMessage.Show(this,"请先等待当前任务完成。","Gitstar");return;}
        if(settings.Account.Length>0) {
            if(DialogMessage.Show(this,$"退出 {settings.Account}？此账号缓存将留在本机，界面切回未登录状态。","Gitstar",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            store.Set("profile/"+settings.Account,settings);CredentialStorage.Clear(store);var old=settings;
            settings=new Settings {ClientId=old.ClientId,Proxy=old.Proxy,DirectConnection=old.DirectConnection,Startup=old.Startup,Dark=old.Dark,DownloadFolder=old.DownloadFolder};api.Token="";Save();SwitchOperations();LoadCache();RebuildGroups();AccountLabel();PlanUpdates();Display();return;
        }
        AuthorizeGitHub();
    }
    private void AuthorizeGitHub()
    {
        if(settings.ClientId.Length==0){
            WindowsServices.Open("https://github.com/login");
            var setup=new Form(this,"GitHub 登录设置",640,480);
            setup.Label("已在默认浏览器打开 GitHub 登录。首次使用请创建自己的 OAuth App，启用 Device Flow，并填写 Client ID。网页登录后还需授权 Gitstar 才能同步收藏与私有仓库。");
            var client=setup.Text("OAuth App Client ID");
            setup.Action("创建 OAuth App",()=>{WindowsServices.Open("https://github.com/settings/applications/new");return Task.CompletedTask;});
            setup.Action("保存并授权",()=>{if(string.IsNullOrWhiteSpace(client.Text))throw new ArgumentException("请填写 Client ID。");settings.ClientId=client.Text.Trim();Save();setup.Close();return Task.CompletedTask;},true);
            setup.ShowDialog();if(settings.ClientId.Length==0)return;
        }
        var f=new Form(this,"GitHub 浏览器授权",600,390);f.Label("使用自己的 OAuth App Client ID；请在 GitHub 启用 Device Flow。权限 repo 用于私有仓库和发布，user 用于编辑 Star 分组，workflow 用于上传 Actions 文件。user 是较宽的用户读写权限，由浏览器授权页确认。");
        var code=f.Text("授权码","正在获取…");code.IsReadOnly=true;
        var verificationUrl="https://github.com/login/device";
        f.Action("打开浏览器登录",()=>{WindowsServices.Open(verificationUrl);return Task.CompletedTask;},true);
        using var loginCancel=new CancellationTokenSource();f.Action("取消",()=>{loginCancel.Cancel();f.Close();return Task.CompletedTask;});f.Closed+=(_,_)=>loginCancel.Cancel();
        f.Loaded+=async(_,_)=>{
            try {
                var authorization=await api.DeviceAuthorization(settings.ClientId,(c,u)=>{code.Text=c;verificationUrl=u;try{Clipboard.SetText(c);}catch(System.Runtime.InteropServices.ExternalException){code.ToolTip="自动复制不可用，请手动复制授权码。";}WindowsServices.Open(verificationUrl);},loginCancel.Token);
                var previous=api.Credentials;api.SetCredentials(authorization);string name;try{name=await api.Account(loginCancel.Token);}catch{api.SetCredentials(previous);throw;}var old=settings;
                if(old.Account.Length>0)store.Set("profile/"+old.Account,old);settings=name==old.Account?old:store.Get("profile/"+name,new Settings());settings.Account=name;settings.ClientId=old.ClientId;settings.Proxy=old.Proxy;settings.DirectConnection=old.DirectConnection;settings.Startup=old.Startup;settings.Dark=old.Dark;
                CredentialStorage.Save(store,authorization);Save();SwitchOperations();LoadCache();RebuildGroups();AccountLabel();f.Close();if(projectsPage)await SyncProjects();else await RefreshAsync(matchedOnly:localPage);
            }catch(OperationCanceledException){}catch(Exception ex){code.Text=ex.Message;}
        };f.ShowDialog();
    }
    private void Notes_Click(object sender,RoutedEventArgs e) {if((sender as Button)?.Tag is Card c)Form.Notes(this,c.Repository+" · "+c.Release.Tag,c.Release.Body);}
    private async void Download_Click(object sender,RoutedEventArgs e)
    {
        if((sender as Button)?.Tag is not Card card)return;
        if(card.Transfer.Active)return;
        if(quitting)return;
        if(card.Update!=null && !installedFresh){await ScanMatchedAsync();PlanUpdates();Display();if(!updates.Any(u=>u.App.Id==card.Update.App.Id && u.Release.Id==card.Release.Id)){model.Status="已复检当前版本，此软件已不需要该更新。";return;}}
        if(card.Transfer.NeedsDetection){SetInstalled(WindowsServices.Scan(settings.PortableApps,settings.Bindings));PlanUpdates();card.Transfer.Finish("");Display();model.Status="已重新检测实际版本；未完成的更新仍保留在列表。";return;}
        var assets=card.Release.Assets.Where(a=>a.Size>0).ToList();if(assets.Count==0){DialogMessage.Show(this,"此版本没有作者上传的安装包。","Gitstar");return;}
        var f=new Form(this,card.Action,700,520);f.Label(card.Repository+" · "+card.Release.Tag);
        var choices=f.Select("选择安装包",assets.Select(a=>$"{a.Name}  ·  {a.Size/1024.0/1024:F1} MB"));
        if(card.Update?.Asset!=null)choices.SelectedIndex=assets.IndexOf(card.Update.Asset);
        var folder=f.Text("保存目录",settings.DownloadFolder);var install=card.Update!=null;
        f.Label(install?"EXE/MSI：下载后询问是否打开安装器。ZIP：解压到新目录后打开文件夹，迁移完成后重新关联。":"只保存安装包，不启动安装器。可选择 Windows 或 Android 文件。");
        f.Action("选择目录",()=>{var d=new OpenFolderDialog {Title="选择安装包保存目录"};if(d.ShowDialog(f)==true)folder.Text=d.FolderName;return Task.CompletedTask;});
        f.Action("开始下载",()=>{
            var asset=assets[choices.SelectedIndex];var root=Path.GetFullPath(folder.Text);
            if(install&&!Rules.IsWindowsInstallCandidate(asset)){DialogMessage.Show(f,"此安装包与当前电脑不兼容，请选择 Windows 安装包。","Gitstar");return Task.CompletedTask;}
            if(string.IsNullOrEmpty(asset.Digest)&&DialogMessage.Show(f,"作者未提供 SHA-256 摘要，本次只能核对文件大小。仍要下载吗？","Gitstar",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return Task.CompletedTask;
            settings.DownloadFolder=root;Save();f.Tag=(asset,root);f.Close();return Task.CompletedTask;
        },true);f.ShowDialog();if(f.Tag is not ValueTuple<Asset,string> selection)return;
        var downloadTarget=PackageTarget(card.Release,selection.Item1,selection.Item2);card.Transfer.Stage("等待下载任务");
        await Execute("下载",card.Repository+" · "+selection.Item1.Name,async ct=>{
            var state=card.Transfer;var operation=currentOperation.Value!;operation.Repository=card.Repository;operation.Tag=card.Release.Tag;operation.File=downloadTarget+".partial";state.Percentage=0;state.Detail=selection.Item1.Name;state.Stage("下载中 0%",false);
            try{
                var progressAt=DateTime.MinValue;
                var replaceSame=false;var previousPackage=packages.FirstOrDefault(p=>p.Path.Equals(downloadTarget,StringComparison.OrdinalIgnoreCase)&&PackageRules.SameVersion(p,card.Release,selection.Item1));
                if(previousPackage!=null&&await PackageFiles.Matches(previousPackage,ct)){
                    state.Stage("处理同版本安装包");var decision=settings.SameVersionDownload;
                    if(decision==0){var duplicate=new Form(this,"同版本安装包已存在",640,420);duplicate.Label(card.Repository+" · "+card.Release.Tag+"\n\n"+downloadTarget+"\n\n选择重新下载并替换，或跳过本次下载和安装。旧文件会保留到新文件校验成功。");duplicate.Action("跳过",()=>{decision=2;duplicate.Close();return Task.CompletedTask;}).IsCancel=true;duplicate.Action("重新下载并替换",()=>{decision=1;duplicate.Close();return Task.CompletedTask;},true);duplicate.ShowDialog();if(decision==0)decision=2;}
                    if(decision==2){state.Finish("已跳过下载");state.Detail=downloadTarget;operation.Results.Add("已存在同版本安装包，跳过下载和安装。");model.Status="已跳过同版本安装包："+selection.Item1.Name;return;}replaceSame=true;
                }
                // Download continuations run on the UI context; report directly to avoid queued percentages overwriting later stages.
                var path=await WindowsServices.Download(api,card.Release,selection.Item1,selection.Item2,(a,b)=>{if(a<b && DateTime.UtcNow-progressAt<TimeSpan.FromMilliseconds(100))return;progressAt=DateTime.UtcNow;state.Downloaded(a,b);model.Status=$"正在下载 {selection.Item1.Name} · {state.Detail}";},ct,()=>{state.Stage("校验中");model.Status="正在校验安装包："+selection.Item1.Name;},token=>CanReplacePackage(card.Release,selection.Item1,downloadTarget,token,replaceSame),forceDownload:replaceSame,targetPath:downloadTarget);
                model.Status="安装包已保存："+path;
                await RecordPackage(card.Release,selection.Item1,path,selection.Item2,ct);
                if(!install){state.Finish("已保存安装包");state.Detail=path;return;}
                if(path.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)){state.Stage("检测安装包架构");var platform=await Task.Run(()=>WindowsServices.MsiPlatform(path),ct);if(!Rules.IsCompatibleMsiTemplate(platform,Environment.Is64BitOperatingSystem)){state.Finish("安装包架构不兼容");state.Detail="MSI 平台："+platform+"；安装包已保存，未启动安装器。";throw new IOException(state.Detail);}}
                if(path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) {
                    state.Stage("解压中");var dir=await Task.Run(()=>WindowsServices.ExtractPortable(path),ct);WindowsServices.Open(dir);state.Finish("便携新版已就绪");state.Detail=dir;model.Status="便携新版已解压到新目录；迁移配置后请重新关联，不会覆盖旧目录。";
                }else{
                    state.Stage("等待安装确认");
                    if(DialogMessage.Show(this,"安装包已校验。现在打开安装器？由你完成安装步骤，退出后自动检测实际版本。","Gitstar",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){state.Finish("安装包已下载");state.Detail=path;return;}
                    ct.ThrowIfCancellationRequested();var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
                    if(process==null){state.Finish("重新检测版本",true);state.Detail="无法跟踪安装器，请完成安装后重新检测。";return;}
                    state.Stage("安装器运行中");state.Detail="请在安装器中完成操作；安装器未提供可读取的百分比。";model.Status=state.Detail;
                    await ObserveInstaller(process,card,settings.Account,ct,path);
                }
            }catch(OperationCanceledException){if(!state.NeedsDetection){state.Finish("重新下载");state.Detail="下载已停止，重试将重新下载。";}throw;}
            catch(Exception ex){if(state.Label!="安装包架构不兼容")state.Finish("重试下载");state.Detail=ex.Message;throw;}
        },resources:new[]{"file:"+downloadTarget}.Concat(install?new[]{"app:"+card.Update!.App.Id}:Array.Empty<string>()).ToArray(),transfer:card.Transfer);
        if(card.Transfer.Active)card.Transfer.Finish("重新下载");
    }
    private async Task ObserveInstaller(Process process,Card card,string account,CancellationToken ct,string packagePath)
    {
        var state=card.Transfer;
        try{
            await process.WaitForExitAsync(ct);if(settings.Account!=account){state.Finish("重新检测版本",true);return;}state.Stage("检测安装结果");
            SetInstalled(await Task.Run(()=>WindowsServices.Scan(settings.PortableApps,settings.Bindings)));PlanUpdates();
            var actual=InstalledVersion.Find(installed,card.Update?.App.Id??"");
            var target=card.Update==null?card.Release.Tag:Rules.VersionTag(card.Release,card.Update.Binding);
            if(actual!=null && Rules.CompareVersions(actual.Version,target) is >=0){state.Finish("已更新");state.Detail="检测到已安装版本："+actual.Version;model.Status=card.Title+" 已更新至 "+actual.Version;Display();await CleanupInstalledPackages(packagePath);}
            else{state.Finish("重新检测版本",true);state.Detail="未确认目标版本；安装器可能已交给另一个进程，或安装被取消。";model.Status=card.Title+"："+state.Detail;Display();}
        }catch(OperationCanceledException){state.Finish("重新检测版本",true);state.Detail="已停止等待安装器；安装器仍可继续运行，请稍后检测版本。";throw;}catch(Exception ex){state.Finish("重新检测版本",true);state.Detail="无法确认安装结果："+ex.Message;}
        finally{process.Dispose();}
    }
    private void Options_Click(object sender,RoutedEventArgs e)
    {
        if((sender as Button)?.Tag is not Card c)return;
        var f=new Form(this,"项目更新选项",620,530);f.Label(c.Repository);
        f.Action("修改分组",()=>{f.Close();MembershipDialog([c.Repository]);return Task.CompletedTask;});
        f.Action("管理 Star",()=>{f.Close();StarDialog(c.Repository);return Task.CompletedTask;});
        f.Action("停止本地关注",()=>{settings.ExtraRepositories.RemoveAll(r=>r.Equals(c.Repository,StringComparison.OrdinalIgnoreCase));if(!settings.HiddenRepositories.Contains(c.Repository,StringComparer.OrdinalIgnoreCase))settings.HiddenRepositories.Add(c.Repository);Save();RebuildGroups();f.Close();model.Status="已停止最近更新页的本地关注；GitHub Star 和本机软件关联保留。可通过添加项目恢复。";return Task.CompletedTask;});
        if(c.Update==null) {f.Label("版本通道筛选只改变浏览结果。自动更新订阅请在关联本机软件时设置。");f.Action("打开仓库",()=>{WindowsServices.Open("https://github.com/"+c.Repository);return Task.CompletedTask;});}
        else {
            var b=c.Update.Binding;var beta=f.Check("检查此软件的测试版",b.IncludePrerelease);var stop=f.Check("停止自动检查",b.Disabled);var pause=f.Text("暂停至日期（yyyy-MM-dd，留空恢复）",b.PauseUntil?.AddDays(-1).ToLocalTime().ToString("yyyy-MM-dd")??"");
            f.Action("忽略此版本",()=>{b.IgnoredTag=c.Release.Tag;Save();PlanUpdates();Display();f.Close();return Task.CompletedTask;});
            f.Action("保存",()=>{b.IncludePrerelease=beta.IsChecked==true;b.Disabled=stop.IsChecked==true;b.PauseUntil=pause.Text.Trim().Length==0?null:DateTimeOffset.Parse(pause.Text).AddDays(1);Save();PlanUpdates();Display();f.Close();return Task.CompletedTask;},true);
        }
        f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});f.ShowDialog();
    }
    private void Add_Click(object sender,RoutedEventArgs e)
    {
        if(localPage){AutoBindingDialog();return;}
        AddProjectDialog();
    }
    private void AutoBindingDialog(string? previewPath=null)
    {
        if(quitting)return;
        if(previewPath==null)SetInstalled(WindowsServices.Scan(settings.PortableApps,settings.Bindings));
        var knownNames=repositories.Select(r=>r.FullName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var suggest=AppMatcher.Prepare(repositories);
        var rows=installed.Where(a=>!settings.Bindings.Any(b=>b.AppId==a.Id)).Select(a=>{
            var matches=suggest(a);var first=matches.FirstOrDefault();
            return new MatchRow {App=a,Repository=first?.Repository??"",Choices=matches.Count>0?matches.Select(m=>m.Repository).ToArray():knownNames,Selected=matches.Count==1 && first!.Reliable,Reason=first?.Reason??"未匹配；可搜索或粘贴仓库"};
        }).OrderByDescending(r=>r.Selected).ThenBy(r=>r.Repository.Length==0).ThenBy(r=>r.Name).ToList();
        var f=new Form(this,"自动匹配 / 批量关联",1030,760);
        f.Label($"已检测 {installed.Count} 个软件，已有 {settings.Bindings.Count} 个关联；本次 {rows.Count} 个待处理，{rows.Count(r=>r.Selected)} 个可靠候选已勾选。");
        f.Label("优先使用软件登记的 GitHub 地址，其次匹配已同步仓库名称。同名或相近结果请先确认；可以直接修改仓库，一次关联多个软件。已有的忽略、暂停和测试版设置不会改动。");
        var table=new DataGrid {ItemsSource=rows,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,MinHeight=320,MaxHeight=420,Margin=new Thickness(0,14,0,12),HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,SelectionMode=DataGridSelectionMode.Single,EnableRowVirtualization=true,EnableColumnVirtualization=true};
        table.Columns.Add(new DataGridCheckBoxColumn {Header="关联",ElementStyle=(Style)f.FindResource(typeof(CheckBox)),EditingElementStyle=(Style)f.FindResource(typeof(CheckBox)),Binding=new System.Windows.Data.Binding(nameof(MatchRow.Selected)){Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged},Width=55});
        table.Columns.Add(new DataGridTextColumn {Header="本机软件",Binding=new System.Windows.Data.Binding(nameof(MatchRow.Name)),IsReadOnly=true,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        table.Columns.Add(new DataGridTextColumn {Header="当前版本",Binding=new System.Windows.Data.Binding(nameof(MatchRow.Version)),IsReadOnly=true,Width=105});
        var repoColumn=new DataGridTemplateColumn {Header="GitHub 仓库（可编辑）",Width=new DataGridLength(1.4,DataGridLengthUnitType.Star)};
        var combo=new FrameworkElementFactory(typeof(ComboBox));combo.SetValue(ComboBox.IsEditableProperty,true);combo.SetValue(ComboBox.IsTextSearchEnabledProperty,true);combo.SetBinding(ComboBox.ItemsSourceProperty,new System.Windows.Data.Binding(nameof(MatchRow.Choices)));combo.SetBinding(ComboBox.TextProperty,new System.Windows.Data.Binding(nameof(MatchRow.Repository)){Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});repoColumn.CellTemplate=new DataTemplate {VisualTree=combo};table.Columns.Add(repoColumn);
        table.Columns.Add(new DataGridTextColumn {Header="匹配依据",Binding=new System.Windows.Data.Binding(nameof(MatchRow.Reason)),IsReadOnly=true,Width=180});f.Fields.Children.Add(table);
        f.Action("取消勾选",()=>{foreach(var row in rows)row.Selected=false;return Task.CompletedTask;});
        f.Action("手动管理 / 便携软件",()=>{f.Close();BindingDialog();return Task.CompletedTask;});
        f.Action("关联所选",async()=>{
            table.CommitEdit();table.CommitEdit(DataGridEditingUnit.Row,true);var selected=rows.Where(r=>r.Selected).ToList();if(selected.Count==0)throw new IOException("请勾选需要关联的软件。");
            foreach(var row in selected){row.Repository=Rules.RepositoryName(row.Repository);GitHubClient.Validate(row.Repository);}
            f.Close();
            await Execute("关联",$"批量关联 {selected.Count} 个软件",async ct=>{
                var validated=new Dictionary<string,Repository>(StringComparer.OrdinalIgnoreCase);var failures=new List<string>();var added=0;
                foreach(var row in selected){ct.ThrowIfCancellationRequested();model.Status="正在关联："+row.Name;
                    try{
                        if(!validated.TryGetValue(row.Repository,out var info)){info=repositories.FirstOrDefault(r=>r.FullName.Equals(row.Repository,StringComparison.OrdinalIgnoreCase))??await api.Repo(row.Repository,ct);validated[row.Repository]=info;if(!repositories.Any(r=>r.FullName==info.FullName))repositories.Add(info);}
                        if(!settings.Bindings.Any(b=>b.AppId==row.App.Id)){settings.Bindings.Add(new(){AppId=row.App.Id,Repository=info.FullName});Save();added++;}
                        if(!releases.Any(r=>r.Repository.Equals(info.FullName,StringComparison.OrdinalIgnoreCase))){releases.AddRange(await api.Releases(info.FullName,ct,settings.Bindings));SaveCache();}
                    }catch(Exception ex)when(ex is not OperationCanceledException){failures.Add(row.Name+"："+ex.Message);}
                }
                SaveCache();RebuildGroups();PlanUpdates();Display();model.Status=$"批量关联完成：新增 {added} 个，{updates.Count} 个软件可更新。"+(failures.Count>0?"待处理："+string.Join("；",failures.Take(3)):"");
            });
        },true);f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});
        if(previewPath!=null)f.ContentRendered+=(_,_)=>f.Dispatcher.BeginInvoke(()=>{f.UpdateLayout();
            static T? Find<T>(DependencyObject parent) where T:DependencyObject {if(parent is T match)return match;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var found=Find<T>(VisualTreeHelper.GetChild(parent,i));if(found!=null)return found;}return null;}
            var comboView=Find<ComboBox>(repoColumn.GetCellContent(table.Items[0]));comboView?.ApplyTemplate();var editor=comboView?.Template.FindName("PART_EditableTextBox",comboView) as TextBox;
            var original=rows[0].Repository;if(editor!=null){editor.Text="fixture/edited";editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();}var edited=rows[0].Repository=="fixture/edited";if(editor!=null){editor.Text=original;editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();}
            var check=Find<CheckBox>(table.Columns[0].GetCellContent(table.Items[0]));var originalCheck=rows[0].Selected;if(check!=null)check.IsChecked=!originalCheck;var checkEdited=rows[0].Selected!=originalCheck;if(check!=null)check.IsChecked=originalCheck;
            File.WriteAllText(Path.ChangeExtension(previewPath,".interaction.json"),JsonSerializer.Serialize(new{EditableRepositoryRoundtrip=edited,CheckBoxRoundtrip=checkEdited},new JsonSerializerOptions{WriteIndented=true}));
            var root=(FrameworkElement)f.Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(previewPath))encoder.Save(stream);f.Close();},DispatcherPriority.ApplicationIdle);
        f.ShowDialog();
    }
    private void BindingDialog()
    {
        SetInstalled(WindowsServices.Scan(settings.PortableApps,settings.Bindings));var f=new Form(this,"检测与关联本机软件",820,730);
        f.Label("读取软件登记信息并核对主程序版本；无法唯一识别时可指定主程序 EXE。仓库关联由你确认，避免同名误识别。选择已有项可修改、暂停或恢复检查。");
        var app=f.Select("检测到的软件",installed.Select(a=>$"{a.Name}  ·  {a.Version}"));
        var portable=f.Check("手动添加便携软件");var name=f.Text("便携软件名称");var version=f.Text("便携软件当前版本");var location=f.Text("便携软件所在文件夹");
        var mainProgram=f.Text("主程序 EXE 路径（可留空自动识别；不要选择卸载器/更新器）");f.Action("选择主程序",()=>{var picker=new OpenFileDialog{Filter="应用程序|*.exe"};if(picker.ShowDialog(f)==true)mainProgram.Text=picker.FileName;return Task.CompletedTask;});
        var repo=f.Text("关联 GitHub 仓库 owner/repo");var prefix=f.Text("版本标签前缀（多产品仓库，例如 desktop-v；留空使用完整 tag）");var pattern=f.Text("安装包文件名包含（可留空；多包时下载前手动选）");
        var beta=f.Check("包含测试版");var disabled=f.Check("停止自动检查");var ignored=f.Text("忽略的版本 tag（清空可恢复）");var pause=f.Text("暂停至 yyyy-MM-dd（清空可恢复）");
        void Fill(){if(app.SelectedIndex<0)return;var b=settings.Bindings.FirstOrDefault(b=>(b.AppId==installed[app.SelectedIndex].Id || installed[app.SelectedIndex].AliasIds?.Contains(b.AppId)==true));mainProgram.Text=b?.ExecutablePath??"";repo.Text=b?.Repository??"";prefix.Text=b?.TagPrefix??"";pattern.Text=b?.AssetPattern??"";beta.IsChecked=b?.IncludePrerelease??false;disabled.IsChecked=b?.Disabled??false;ignored.Text=b?.IgnoredTag??"";pause.Text=b?.PauseUntil?.AddDays(-1).ToLocalTime().ToString("yyyy-MM-dd")??"";}
        app.SelectionChanged+=(_,_)=>Fill();Fill();
        f.Action("保存关联",async()=>{
            if(mainProgram.Text.Trim().Length>0 && (!File.Exists(mainProgram.Text.Trim()) || !mainProgram.Text.Trim().EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("请选择存在的主程序 EXE。");var r=Rules.RepositoryName(repo.Text);await api.Repo(r);
            InstalledApp a;
            if(portable.IsChecked==true){if(name.Text.Trim().Length==0||!Directory.Exists(location.Text)||Rules.CompareVersions(version.Text,version.Text)==null)throw new ArgumentException("请填写名称、可比较版本和存在的目录。");a=new("portable/"+Guid.NewGuid().ToString("N"),name.Text.Trim(),version.Text.Trim(),Path.GetFullPath(location.Text),true);settings.PortableApps.Add(a);}
            else {if(app.SelectedIndex<0)throw new ArgumentException("请先选择软件。");a=installed[app.SelectedIndex];}
            var b=settings.Bindings.FirstOrDefault(b=>b.AppId==a.Id || a.AliasIds?.Contains(b.AppId)==true);if(b==null){b=new(){AppId=a.Id};settings.Bindings.Add(b);}if(mainProgram.Text.Trim().Length>0 && (!File.Exists(mainProgram.Text.Trim()) || !mainProgram.Text.Trim().EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("请选择存在的主程序 EXE。");b.ExecutablePath=mainProgram.Text.Trim().Length>0?Path.GetFullPath(mainProgram.Text.Trim()):"";b.Repository=r;b.TagPrefix=prefix.Text;b.AssetPattern=pattern.Text;b.IncludePrerelease=beta.IsChecked==true;b.Disabled=disabled.IsChecked==true;b.IgnoredTag=ignored.Text;b.PauseUntil=pause.Text.Trim().Length==0?null:DateTimeOffset.Parse(pause.Text).AddDays(1);
            Save();f.Close();await RefreshAsync();
        },true);
        f.Action("移除关联",()=>{if(app.SelectedIndex>=0){var id=installed[app.SelectedIndex].Id;settings.Bindings.RemoveAll(b=>b.AppId==id);Save();PlanUpdates();Display();f.Close();}return Task.CompletedTask;});f.ShowDialog();
    }
    private void Settings_Click(object sender,RoutedEventArgs e)=>SettingsDialog();
    private void SettingsDialog(string? previewPath=null)
    {
        if(busy||batchPublishing){DialogMessage.Show(this,"请先等待任务完成，再修改账号与网络设置。","Gitstar");return;}
        var f=new Form(this,"设置",940,730);f.Section("GitHub 账号与授权");var client=f.Text("GitHub OAuth App Client ID",settings.ClientId);f.Label("浏览器设备授权无需把 Client Secret 写入客户端。创建 OAuth App 后启用 Device Flow。");
        f.Section("本地数据与清理");var dataFolder=f.Text("应用数据目录",Path.GetFullPath(store.Root));dataFolder.IsReadOnly=true;dataFolder.ToolTip=dataFolder.Text;var database=f.Text("数据库文件",Path.Combine(dataFolder.Text,"gitstar.db"));database.IsReadOnly=true;database.ToolTip=database.Text;
        var dataActions=new WrapPanel{Margin=new Thickness(0,0,0,8)};f.Fields.Children.Add(dataActions);var openData=f.Action("打开数据目录",()=>{WindowsServices.Open(dataFolder.Text);return Task.CompletedTask;});var copyData=f.Action("复制目录路径",()=>{Clipboard.SetText(dataFolder.Text);return Task.CompletedTask;});foreach(var button in new[]{openData,copyData}){f.Buttons.Children.Remove(button);button.Margin=new Thickness(0,0,8,0);dataActions.Children.Add(button);}
        var cleanCache=f.Action("按模块清理缓存",()=>{CacheCleanupDialog(f);return Task.CompletedTask;});f.Buttons.Children.Remove(cleanCache);cleanCache.Margin=new Thickness(0,0,8,0);dataActions.Children.Add(cleanCache);
        var compactDb=f.Action("整理数据库空间",()=>CompactDatabase(f));f.Buttons.Children.Remove(compactDb);compactDb.Margin=new Thickness(0,0,8,0);dataActions.Children.Add(compactDb);var storageUsage=store.StorageUsage();f.Label("数据库文件："+CacheBytes(storageUsage.Database)+"；有效内容："+CacheBytes(storageUsage.Content)+"；可回收空闲页："+CacheBytes(storageUsage.Free)+"。整理空间保留全部数据，按模块清理仅删除所选缓存。");f.Label("此目录保存登录凭据、设置、分组、软件与源码目录关联、缓存及任务记录。数据库运行时可能同时存在 gitstar.db-wal 和 gitstar.db-shm 文件。");f.Label("清理方法：从系统托盘选择“彻底退出”，再删除上面的应用数据目录。关闭窗口只会收起到托盘；删除数据后应用将恢复初始状态。下载目录中的安装包需单独整理。");
        f.Section("安装包管理");var keepLatest=f.Check("只保留各项目、平台和正式/测试通道的最新安装包",settings.KeepLatestPackages);var autoDelete=f.Check("确认安装成功后自动删除安装包",settings.DeleteInstalledPackages);var promptDelete=f.Check("更新检查完成后，询问是否删除已安装的安装包",settings.PromptInstalledPackageCleanup);promptDelete.IsEnabled=!autoDelete.IsChecked.GetValueOrDefault();autoDelete.Checked+=(_,_)=>promptDelete.IsEnabled=false;autoDelete.Unchecked+=(_,_)=>promptDelete.IsEnabled=true;f.Label("清理 Gitstar 记录的下载文件。自动删除默认关闭；确认主程序已达到目标版本后处理。同版本的其他安装包保留。选择保留后不重复询问。旧下载文件可手动整理。");
        f.Section("下载与更新");var folder=f.Text("默认下载目录",settings.DownloadFolder);var sameVersion=f.Select("同版本安装包已存在时",["每次询问：替换或跳过","重新下载并替换","跳过下载和安装"],settings.SameVersionDownload);f.Label("安装包直接保存在此目录，不创建项目或版本子文件夹。文件名包含项目和版本，区分不同项目的同名安装包。替换前会核对旧下载记录，新文件校验成功后才覆盖。");f.Section("网络与启动");var proxy=f.Text("应用代理地址（http/https/socks5，无用户名密码；留空使用系统代理）",settings.Proxy);var direct=f.Check("直连（不使用系统代理）",settings.DirectConnection);var startup=f.Check("登录 Windows 后启动，并收起到托盘",settings.Startup);f.Section("外观");var dark=f.Check("深色模式",settings.Dark);
        f.Section("自动检查");var frequency=f.Select("后台刷新频率",RefreshSchedule.Labels,Array.IndexOf(RefreshSchedule.Hours,RefreshSchedule.Normalize(settings.RefreshIntervalHours)));
        f.Label("默认每天刷新一次，也可设为仅手动。切换页面只显示缓存；点击「检查更新」或托盘「立即检查更新」可强制刷新，不受周期限制。后台不下载；关闭窗口收起托盘。");
        RenderAbout(f);
        f.Action("重新授权 GitHub",()=>{settings.ClientId=client.Text.Trim();Save();f.Close();AuthorizeGitHub();return Task.CompletedTask;});
        f.Action("导出公共配置",()=>{var d=new SaveFileDialog {Filter="JSON|*.json",FileName="Gitstar-settings.json"};if(d.ShowDialog(f)==true){var publicRepos=repositories.Where(r=>!r.Private).Select(r=>r.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);var payload=new {schemaVersion=1,repositories=settings.ExtraRepositories.Where(publicRepos.Contains).ToArray(),groups=settings.LocalGroups.Select(g=>g with {Repositories=g.Repositories.Where(publicRepos.Contains).ToList()}).Where(g=>g.Repositories.Count>0).ToArray()};File.WriteAllText(d.FileName,JsonSerializer.Serialize(payload,new JsonSerializerOptions {WriteIndented=true}));}return Task.CompletedTask;});
        f.Action("导入分组",()=>{var d=new OpenFileDialog {Filter="JSON|*.json"};if(d.ShowDialog(f)!=true)return Task.CompletedTask;using var doc=JsonDocument.Parse(File.ReadAllText(d.FileName));if(doc.RootElement.GetProperty("schemaVersion").GetInt32()!=1)throw new IOException("不支持的配置版本。");var incoming=JsonSerializer.Deserialize<List<StarGroup>>(doc.RootElement.GetProperty("groups").GetRawText())??[];if(incoming.Any(g=>g.Repositories.Any(r=>!Rules.ValidRepository(r))))throw new IOException("分组中的仓库格式无效。");if(DialogMessage.Show(f,$"导入 {incoming.Count} 个分组？会作为独立本地分组保留。","Gitstar",MessageBoxButton.OKCancel)==MessageBoxResult.OK){foreach(var g in incoming)settings.LocalGroups.Add(g with {Id=Guid.NewGuid().ToString("N"),Source="导入"});Save();RebuildGroups();}return Task.CompletedTask;});
        f.Action("保存",()=>{
            if(proxy.Text.Trim().Length>0 && (!Uri.TryCreate(proxy.Text.Trim(),UriKind.Absolute,out var u)||u.Scheme is not ("http" or "https" or "socks5")||u.UserInfo.Length>0))throw new ArgumentException("代理请输入 http、https 或 socks5 地址，不能包含密码。");
            settings.SelfRepository=AppInfo.Repository;settings.ClientId=client.Text.Trim();settings.DownloadFolder=Path.GetFullPath(folder.Text);settings.Proxy=proxy.Text.Trim();settings.DirectConnection=direct.IsChecked==true;settings.Startup=startup.IsChecked==true;settings.Dark=dark.IsChecked==true;
            settings.SameVersionDownload=sameVersion.SelectedIndex;settings.KeepLatestPackages=keepLatest.IsChecked==true;settings.DeleteInstalledPackages=autoDelete.IsChecked==true;settings.PromptInstalledPackageCleanup=promptDelete.IsChecked==true;settings.RefreshIntervalHours=RefreshSchedule.Hours[frequency.SelectedIndex];WindowsServices.Startup(settings.Startup);Save();var authorization=api.Credentials;api.Dispose();api=new(settings);ConfigureApiCache();api.CredentialsChanged=auth=>CredentialStorage.Save(store,auth);api.SetCredentials(authorization);AvatarCache.Configure(settings);ApplyTheme();Display();f.Close();return Task.CompletedTask;
        },true);f.SectionNavigation(previewPath!=null?(Environment.GetCommandLineArgs().Contains("--settings-about")?"关于":Environment.GetCommandLineArgs().Contains("--settings-appearance")?"外观":Environment.GetCommandLineArgs().Contains("--settings-auto")?"自动检查":"下载与更新"):null);if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private void Tasks_Click(object sender,RoutedEventArgs e)=>TasksDialog();
    private void TasksDialog(string? previewPath=null)
    {
        var f=new Form(this,"任务记录",850,620);
        var rows=new List<(Operation Op,TextBlock Label,Button Pause,Button Cancel,Button Resume)>();
        foreach(var o in operations.AsEnumerable().Reverse().Take(80)){
            var row=new Grid{Margin=new Thickness(0,6,0,12)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var label=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,12,0)};label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");row.Children.Add(label);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};Grid.SetColumn(actions,1);
            var pause=new Button{Content="暂停",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,6,0)};var cancel=new Button{Content="取消",Padding=new Thickness(12,7,12,7)};
            pause.Click+=(_,_)=>taskCoordinator.Pause(o.Id);cancel.Click+=(_,_)=>{if(taskCoordinator.Contains(o.Id))taskCoordinator.Cancel(o.Id);else if(OperationRules.CanCancel(o)){try{if(o.Kind=="下载" && o.File.EndsWith(".partial",StringComparison.OrdinalIgnoreCase) && File.Exists(o.File))File.Delete(o.File);OperationRules.CancelStopped(o);SaveOperations();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){DialogMessage.Show(f,ex.Message,"临时文件清理未完成");}}};actions.Children.Add(pause);actions.Children.Add(cancel);
            var resume=new Button{Content="重试 / 继续",Padding=new Thickness(12,7,12,7),Margin=new Thickness(6,0,0,0),Visibility=o.Kind=="自动发布"?Visibility.Visible:Visibility.Collapsed};resume.Click+=async(_,_)=>{var project=myProjects.FirstOrDefault(p=>p.FullName.Equals(o.Repository,StringComparison.OrdinalIgnoreCase));if(project==null){model.Status="此项目已不在当前项目列表，请先刷新项目列表。";return;}f.Close();await AutoPublishProject(project,true);};actions.Children.Add(resume);row.Children.Add(actions);f.Fields.Children.Add(row);rows.Add((o,label,pause,cancel,resume));
        }
        void Update(){foreach(var row in rows){row.Label.Text=$"{row.Op.Updated:MM-dd HH:mm}  [{row.Op.Kind}] {row.Op.Description}\n{row.Op.State}"+(taskTransfers.TryGetValue(row.Op.Id,out var transfer)?" · "+transfer.Label+"\n"+transfer.Detail:"")+(row.Op.Results.Count>0?"\n"+string.Join("\n",row.Op.Results):"")+(row.Op.RemoteId!=null?$"\nRelease ID：{row.Op.RemoteId}":"");row.Pause.IsEnabled=taskCoordinator.Contains(row.Op.Id);row.Cancel.IsEnabled=row.Pause.IsEnabled||OperationRules.CanCancel(row.Op);row.Resume.IsEnabled=!row.Pause.IsEnabled&&OperationRules.CanCancel(row.Op)&&projectAutomation.ContainsKey(row.Op.Repository)&&!automaticPublishing.ContainsKey(row.Op.Repository);row.Resume.Visibility=row.Op.Kind=="自动发布"&&OperationRules.CanCancel(row.Op)?Visibility.Visible:Visibility.Collapsed;}}
        if(previewPath!=null&&Environment.GetCommandLineArgs().Contains("--cancel-failed")){Update();var row=rows.First(item=>item.Op.Kind=="自动发布");var enabled=row.Cancel.IsEnabled;if(!enabled)throw new IOException("Failed task cancel button is disabled");row.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Update();if(OperationRules.CanCancel(row.Op)||row.Resume.IsEnabled||row.Resume.Visibility!=Visibility.Collapsed)throw new IOException("Cancelled task still allows retry");File.WriteAllText(Path.ChangeExtension(previewPath,".cancel.json"),JsonSerializer.Serialize(new{CancelWasEnabled=enabled,row.Op.State,RetryHidden=row.Resume.Visibility==Visibility.Collapsed,row.Op.RemoteId}));}
        var refresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};refresh.Tick+=(_,_)=>Update();f.Closed+=(_,_)=>refresh.Stop();Update();refresh.Start();
        f.Label("每个任务独立暂停或取消。暂停下载保留 .partial，重试从头下载；取消清理未完成临时文件，不删除已完成安装包。上传/发布保留已完成部分，结果不明请核实远程。外部安装器不会被关闭。");
        f.Action("暂停全部任务",()=>{taskCoordinator.CancelAll();return Task.CompletedTask;});f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private void SourceDialog(ManagedProject? project=null,string? previewPath=null)
    {
        var f=new Form(this,"上传更新",900,800);var workspace=project==null?null:Workspace(project);f.Label("目标项目："+(project?.FullName??"在高级操作中填写"));
        var folder=f.Text("源码目录",workspace?.Folder??"");folder.IsReadOnly=true;
        var status=new TextBlock{Text="正在读取本地变更…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontSize=12};status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(status);
        var selected=new ObservableCollection<SourceChange>();var selection=new ListBox{ItemsSource=selected,MaxHeight=250,MinHeight=80,BorderThickness=new Thickness(0),Background=Brushes.Transparent,ItemContainerStyle=(Style)f.FindResource("SourceChangeRow")};ScrollViewer.SetHorizontalScrollBarVisibility(selection,ScrollBarVisibility.Disabled);
        var factory=new FrameworkElementFactory(typeof(CheckBox));var fileLabel=new FrameworkElementFactory(typeof(TextBlock));fileLabel.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Label"));fileLabel.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);factory.AppendChild(fileLabel);factory.SetBinding(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding("Label"));factory.SetBinding(CheckBox.IsCheckedProperty,new System.Windows.Data.Binding("Selected"){Mode=System.Windows.Data.BindingMode.TwoWay});factory.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,5));selection.ItemTemplate=new DataTemplate{VisualTree=factory};VirtualizingPanel.SetIsVirtualizing(selection,true);VirtualizingPanel.SetVirtualizationMode(selection,VirtualizationMode.Recycling);f.Fields.Children.Add(selection);
        var quick=new WrapPanel();f.Fields.Children.Add(quick);foreach(var option in new[]{("全选",true),("清空",false)}){var b=new Button{Content=option.Item1,Padding=new Thickness(10,5,10,5)};b.Click+=(_,_)=>{foreach(var row in selected)row.Selected=option.Item2;};quick.Children.Add(b);}
        var message=f.Text("提交说明（自动生成，可修改）");var advanced=f.Advanced();TextBox repo=null!,branch=null!,review=null!;
        f.Into(advanced,()=>{repo=f.Text("目标仓库",project?.FullName??"");repo.IsReadOnly=project!=null;branch=f.Text("分支",workspace?.Branch is {Length:>0}?workspace.Branch:project?.DefaultBranch is {Length:>0}?project.DefaultBranch:"main");review=f.Text("Git 状态与远程详情","",true);review.IsReadOnly=true;});
        var advancedActions=new WrapPanel();advanced.Children.Add(advancedActions);
        void Extra(string title,Func<Task> work){var button=f.Action(title,work);f.Buttons.Children.Remove(button);advancedActions.Children.Add(button);}
        string reviewedFolder="",reviewedRepo="",generatedMessage="";GitProjectState? state=null;bool scanning=false,closed=false;Button? upload=null,choose=null;
        f.Closed+=(_,_)=>closed=true;
        void Update(){var chosen=selected.Where(c=>c.Selected).Select(c=>c.Change).ToArray();var next=SourceWorkflow.CommitMessage(chosen);if(message.Text.Length==0||message.Text==generatedMessage)message.Text=next;generatedMessage=next;if(upload!=null){upload.Content=state?.Changes.Count==0?(state.SourceSynced?"重新核实":"推送已有提交"):"上传更新";upload.IsEnabled=!scanning&&state!=null&&(chosen.Length>0||state.Commit.Length>0&&state.Changes.Count==0);}if(choose!=null)choose.IsEnabled=!scanning;}
        void Apply(GitProjectState fresh){state=fresh;folder.Text=fresh.Folder;branch.Text=fresh.Branch;reviewedFolder=fresh.Folder;reviewedRepo=Rules.RepositoryName(repo.Text);selected.Clear();foreach(var change in fresh.Changes){var row=new SourceChange{Change=change,Selected=true};row.PropertyChanged+=(_,_)=>Update();selected.Add(row);}review.Text=$"分支：{fresh.Branch}\norigin：{fresh.Remote}\nHEAD：{fresh.Commit}\n\n{fresh.Summary}";status.Text=$"{fresh.Branch} · {fresh.Changes.Count} 个变更，已默认勾选，可取消不需要上传的文件。";status.Text=fresh.SyncSummary;if(project!=null&&previewPath==null)RememberWorkspace(project,fresh);Update();}
        async Task Scan(){if(scanning||closed)return;if(folder.Text.Length==0){status.Text="选择源码目录后会自动检查并准备上传。";return;}scanning=true;state=null;Update();status.Text="正在检查源码目录与变更…";try{var target=Rules.RepositoryName(repo.Text);GitHubClient.Validate(target);var model=project??new ManagedProject(target,"",false,"main",true,false,false,DateTimeOffset.MinValue);var fresh=await ResolveWorkspaceRoot(f,model,folder.Text,branch.Text.Trim());if(fresh!=null)fresh=await VerifySourceRemote(fresh,target,CancellationToken.None);if(!closed){if(fresh!=null)Apply(fresh);else status.Text="尚未关联此目录，可更换目录或重新检查。";}}catch(Exception ex){if(!closed)status.Text=ex.Message+" 可点击更换目录重试。";}finally{scanning=false;if(!closed)Update();}}
        choose=f.Action("更换目录",async()=>{var d=new OpenFolderDialog{Title="选择源码目录"};if(d.ShowDialog(f)==true){folder.Text=d.FolderName;await Scan();}});
        f.Action("重新检查",Scan);
        Extra("预览差异",async()=>{if(state==null)throw new IOException("请先选择有效的源码目录。");var paths=selected.Where(x=>x.Selected).SelectMany(x=>x.Change.Paths).Distinct().ToArray();if(paths.Length==0)throw new IOException("请先勾选文件。");var git=new GitWorkspace("");var diff=await git.Run(state.Folder,CancellationToken.None,["diff","--no-ext-diff","--",..paths]);Form.Notes(f,"选中变更",string.Join("\n",paths)+"\n\n"+diff+"\n\n新增文件可在本地目录查看原文件。");});
        Extra("提交身份",()=>{var identity=new Form(f,"本仓库提交身份",590,470);var name=identity.Text("提交者名称");var email=identity.Text("提交者邮箱");identity.Label("已配置的身份保持不变；缺失时上传将使用 GitHub 用户名与隐私邮箱。");identity.Action("保存",async()=>{if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(email.Text))throw new IOException("请填写名称和邮箱。");var path=Path.GetFullPath(folder.Text);var git=new GitWorkspace("");var author=name.Text.Trim();var address=email.Text.Trim();var saved=false;await Execute("本地项目","设置提交身份",async ct=>{await git.Inspect(path,ct);await git.Run(path,ct,"config","--local","user.name",author);await git.Run(path,ct,"config","--local","user.email",address);saved=true;},resources:["git:"+path]);if(saved)identity.Close();},true);identity.ShowDialog();return Task.CompletedTask;});
        Extra("推送已有提交",async()=>{if(state==null)throw new IOException("请先选择有效源码目录。");var path=state.Folder;var target=Rules.RepositoryName(repo.Text);var targetBranch=branch.Text.Trim();f.Close();await Execute("源码上传",target,async ct=>{currentOperation.Value!.Repository=target;await api.EnsureFreshToken(ct);var git=new GitWorkspace(api.Token);await git.Push(path,target,targetBranch,ct);if(project!=null)RememberWorkspace(project,await git.Inspect(path,ct));model.Status="已有提交已推送。";},resources:["repo:"+target,"git:"+path]);});
        upload=f.Action("上传更新",async()=>{
            if(api.Token.Length==0)throw new IOException("请先登录 GitHub。");if(state==null)throw new IOException("请先选择有效的源码目录。");var path=Path.GetFullPath(folder.Text);var target=Rules.RepositoryName(repo.Text);if(reviewedFolder!=path||reviewedRepo!=target)throw new IOException("目录或仓库改变，请重新检查。");var reviewedState=state;var current=await new GitWorkspace("").Inspect(path,CancellationToken.None);if(!SourceWorkflow.SameSnapshot(reviewedState,current)){Apply(await VerifySourceRemote(current,target,CancellationToken.None));status.Text="源码在上次检查后发生变化，列表已更新，请确认后上传。";return;}if(current.Changes.Count==0){current=await VerifySourceRemote(current,target,CancellationToken.None);Apply(current);if(current.SourceSynced){status.Text=current.SyncSummary;return;}}var chosen=selected.Where(x=>x.Selected).SelectMany(x=>x.Change.Paths).Distinct().ToArray();var pushOnly=current.Changes.Count==0&&current.Commit.Length>0;if(!pushOnly&&chosen.Length==0)throw new IOException("请勾选上传文件。");if(!pushOnly&&string.IsNullOrWhiteSpace(message.Text))throw new IOException("请填写提交说明。");var targetBranch=branch.Text.Trim();var commitMessage=message.Text;f.Close();
            await Execute("源码上传",target,async ct=>{currentOperation.Value!.Repository=target;await api.EnsureFreshToken(ct);var git=new GitWorkspace(api.Token);var fresh=await git.Inspect(path,ct);if(!ProjectRules.MatchesRemote(fresh.Remote,target))throw new IOException("origin 已改变，请重新关联。");if(!SourceWorkflow.SameSnapshot(current,fresh))throw new IOException("源码在准备上传后发生变化，请重新检查并确认文件。");if(pushOnly)await git.Push(path,target,targetBranch,ct);else{await git.EnsureIdentity(path,api.CommitIdentity,ct);await git.Upload(path,target,targetBranch,commitMessage,chosen,ct);}if(project!=null)RememberWorkspace(project,await VerifySourceRemote(await git.Inspect(path,ct),target,ct));model.Status=pushOnly?"已有提交已推送。":"源码更新已上传。";},resources:["repo:"+target,"git:"+path]);
        },true);Update();
        if(previewPath!=null){if(project!=null){var fixture=projectStates.GetValueOrDefault(project.FullName)??new GitProjectState(workspace?.Folder??@"D:\Projects\Gitstar","main","https://github.com/"+project.FullName+".git",new string('a',40),[new(" M","windows/Gitstar.Desktop/MainWindow.xaml.cs")],"1 file changed");if(Environment.GetCommandLineArgs().Contains("--source-synced"))fixture=fixture with{Changes=[],RemoteCommit=fixture.Commit};Apply(fixture);}f.RenderPreview(previewPath);}else f.ContentRendered+=async(_,_)=>await Scan();f.ShowDialog();
    }
    private void PublishDialog(ManagedProject? project=null,ProjectVersion? version=null,string? previewPath=null)
    {
        var f=new Form(this,"更新发布版本",850,800);f.Label("目标项目："+(project?.FullName??"在高级操作中填写"));var config=project==null?null:SuggestedAutomation(project);var releaseFolder=f.Text("本地发行目录",config?.ReleaseFolder??"");releaseFolder.IsReadOnly=true;var tag=f.Text("版本号","");f.Label("留空读取应用文件的版本号；相同版本默认跳过；确认强制覆盖后更新附件，填写新版本号则发布新版本。");var beta=f.Check("测试版",version?.Prerelease??false);var notes=f.Text("更新日志（Markdown）",version?.Body??"",true);var assets=f.Text("安装包",config==null?"":string.Join(Environment.NewLine,ManualReleaseRules.Assets(config.ReleaseFolder,config.AssetPatterns)),true);assets.IsReadOnly=true;assets.MinHeight=70;
        var versionHint=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),FontSize=12};versionHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(versionHint);
        f.Fields.Children.Remove(versionHint);f.Fields.Children.Insert(f.Fields.Children.IndexOf(beta),versionHint);notes.MinHeight=70;notes.Height=70;assets.Height=90;
        void ReadVersionHint(){try{var resolved=ManualReleaseRules.Version(tag.Text,assets.Text.Split('\n').Select(p=>p.Trim()).Where(p=>p.Length>0),releaseFolder.Text,project?.Name??"");versionHint.Text=(tag.Text.Trim().Length==0?"应用文件版本：":"使用填写版本：")+resolved;}catch(IOException ex){versionHint.Text=ex.Message;}}
        assets.TextChanged+=(_,_)=>ReadVersionHint();tag.TextChanged+=(_,_)=>ReadVersionHint();ReadVersionHint();
        var advanced=f.Advanced();TextBox repo=null!,commit=null!,id=null!;f.Into(advanced,()=>{repo=f.Text("目标仓库",project?.FullName??"");commit=f.Text("提交 SHA（自动读取，可手动指定）",project!=null&&projectStates.TryGetValue(project.FullName,out var localState)?localState.Commit:"");id=f.Text("草稿 ID（继续发布时自动填写）",version?.Draft==true?version.Id.ToString():"");});repo.IsReadOnly=project!=null;if(version?.Draft==true)id.IsReadOnly=true;
        var readiness=new TextBlock{Text=version?.Draft==true?"继续已有草稿，上传后核对并发布。":"将自动读取本地或远程默认分支提交。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,8),FontSize=12};readiness.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(readiness);
        Button? publishButton=null;var reading=previewPath==null&&project!=null&&version?.Draft!=true;
        if(reading)f.ContentRendered+=async(_,_)=>{try{var workspace=Workspace(project!);if(workspace!=null&&Directory.Exists(workspace.Folder)){var local=await new GitWorkspace("").Inspect(workspace.Folder,CancellationToken.None);if(!ProjectRules.MatchesRemote(local.Remote,project!.FullName))throw new IOException("本地 origin 与项目不一致，请先关联正确的源码目录。");commit.Text=local.Commit;}else commit.Text=await api.BranchCommit(project!.FullName,project.DefaultBranch,CancellationToken.None);readiness.Text=commit.Text.Length>0?"提交已读取；发布前会验证已上传到 GitHub。":"源码还没有提交，请先上传更新。";}catch(Exception ex){commit.Text="";readiness.Text=ex.Message+" 可在高级操作指定提交。";}finally{reading=false;if(publishButton!=null)publishButton.IsEnabled=true;}};
        f.Action("选择安装包",()=>{var d=new OpenFileDialog {Multiselect=true,Filter="安装包|*.exe;*.msi;*.zip;*.apk;*.aab|所有文件|*.*"};if(project!=null){var releaseFolder=SuggestedAutomation(project).ReleaseFolder;if(Directory.Exists(releaseFolder))d.InitialDirectory=releaseFolder;}if(d.ShowDialog(f)==true)assets.Text=string.Join(Environment.NewLine,d.FileNames);return Task.CompletedTask;});
        var openRelease=f.Action("查看仓库",()=>{var r=Rules.RepositoryName(repo.Text);GitHubClient.Validate(r);WindowsServices.Open("https://github.com/"+r+"/releases");return Task.CompletedTask;});
        f.Buttons.Children.Remove(openRelease);advanced.Children.Add(openRelease);
        async Task Publish(bool releaseNow){
            if(reading)throw new IOException("正在读取提交，请稍候。");
            if(api.Token.Length==0)throw new IOException("请先登录 GitHub。");var r=Rules.RepositoryName(repo.Text);GitHubClient.Validate(r);
            var paths=assets.Text.Split('\n').Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();if(paths.Any(p=>!File.Exists(p)))throw new IOException("有安装包路径不存在。");if(paths.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=paths.Count)throw new IOException("安装包文件名重复。");
            if(paths.Count==0)throw new IOException("请选择需要上传的安装包。");
            var resolvedTag=ManualReleaseRules.Version(tag.Text,paths,releaseFolder.Text,project?.Name??r.Split('/').Last());
            var resume=long.TryParse(id.Text,out var existing);
            var t=resolvedTag;var sha=commit.Text.Trim();var body=notes.Text;var pre=beta.IsChecked==true;await api.EnsureFreshToken(CancellationToken.None);var found=await api.AutomationRelease(r,t,"",pre,CancellationToken.None);var overwrite=false;
            if(found.Release is {} prior){
                var confirmation=new Form(f,"版本已存在",650,530);confirmation.Label(r+" · "+prior.GetProperty("tag_name").GetString()+" 已存在。默认跳过上传；强制覆盖会更新所选附件和日志，保留版本标签。");var files=confirmation.Text("将上传的附件",string.Join(Environment.NewLine,paths.Select(p=>Path.GetFileName(p)+" · "+new FileInfo(p).Length.ToString("N0")+" 字节")),true);files.IsReadOnly=true;
                confirmation.Action("强制覆盖",()=>{overwrite=true;confirmation.Close();return Task.CompletedTask;},true);confirmation.Action("跳过",()=>{confirmation.Close();return Task.CompletedTask;});confirmation.ShowDialog();if(!overwrite){readiness.Text="同版本已存在，已跳过上传。";return;}
            }
            f.Close();
            await Execute("发布",r+" · "+t,async ct=>{
                var op=currentOperation.Value!;op.Repository=r;op.Tag=t;SaveOperations();
                var stage=Path.Combine(store.Root,"release-staging",Guid.NewGuid().ToString("N"));try{
                var packages=await ReleaseAssetFiles.Stage(paths,stage,ct);
                var draft=await api.PrepareManualRelease(r,t,sha,body,pre,resume?existing:null,ct,overwrite);RememberProjectVersion(r,GitHubClient.ParseProjectVersion(draft));
                var releaseId=draft.GetProperty("id").GetInt64();op.RemoteId=releaseId;SaveOperations();
                foreach(var package in packages){model.Status="同步安装包："+package.OriginalName;await api.SyncReleaseAsset(r,releaseId,package.Path,package.Name,package.Size,package.Sha,ct);}
                var verified=await api.ReleaseDetails(r,releaseId,ct);var assetNames=string.Join("\n",verified.GetProperty("assets").EnumerateArray().Select(a=>$"{a.GetProperty("name").GetString()} · {a.GetProperty("size").GetInt64()/1024.0/1024:F1} MB"));
                if(!draft.GetProperty("draft").GetBoolean()){await api.UpdateAutomationRelease(r,releaseId,draft.GetProperty("tag_name").GetString()!,sha,body.Length>0?body:draft.GetProperty("body").GetString()??"",pre,ct);}
                else if(releaseNow)await api.Publish(r,releaseId,ct);
                var latest=await api.ProjectVersionDetails(r,releaseId,ct);RememberProjectVersion(r,latest);
                if(releaseNow&&latest.Draft)throw new IOException("GitHub 尚未确认版本已发布，草稿记录已更新，可点击“核实状态”继续核实。");
                model.Status=latest.Draft?$"草稿已保留，Release ID：{releaseId}。":"版本已发布："+r+" · "+latest.Tag;
                }finally{try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
            },resources:["repo:"+r]);
        }
        publishButton=f.Action("更新",()=>Publish(true),true);publishButton.IsEnabled=!reading;var saveDraft=f.Action("保存为草稿",()=>Publish(false));f.Buttons.Children.Remove(saveDraft);advanced.Children.Add(saveDraft);if(previewPath!=null){notes.Text="优化发布流程，支持同版本安装包覆盖更新。";versionHint.Text="应用文件版本：1.0.0.0";File.WriteAllText(Path.ChangeExtension(previewPath,".version.json"),JsonSerializer.Serialize(new{Tag=tag.Text,Directory=releaseFolder.Text,Editable=!tag.IsReadOnly,DraftId=id.Text}));f.RenderPreview(previewPath);}f.ShowDialog();
    }
    public void Reopen(){Show();WindowState=WindowState.Normal;Activate();}
    private void OnClosing(object? sender,CancelEventArgs e) {if(exit)return;e.Cancel=true;Hide();model.Status="Gitstar 已收起到托盘，后台任务继续运行。";}
    public async void RequestExit()
    {
        if(quitting)return;if(busy){if(DialogMessage.Show(this,"有多个任务可能正在执行。暂停后退出？下载重试会重新开始；发布或推送结果不明会留待核实。","Gitstar",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;quitting=true;taskCoordinator.CancelAll();while(busy)await Task.Delay(100);}
        exit=true;timer.Stop();Save();SaveOperations();tray.Visible=false;tray.Dispose();api.Dispose();try{await Task.Run(store.ReclaimUnusedSpace);}catch(IOException){}store.Dispose();Application.Current.Shutdown();
    }
    public async void RenderSmoke(string path,bool cards=false,bool local=false,bool dark=false,bool firstGroup=false,bool matching=false,bool settingsPreview=false,bool avatars=false,bool progressPreview=false,bool groupPreview=false,bool addPreview=false,bool batchPreview=false,bool tasksPreview=false,bool projectsPreview=false,bool sourcePreview=false,bool createPreview=false,bool projectSettingsPreview=false,bool workspacePreview=false,bool publishPreview=false,bool automationPreview=false)
    {
        settings.Dark=dark;ApplyTheme();
        var workArea=Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        var dpi=VisualTreeHelper.GetDpi(this);
        File.WriteAllText(Path.ChangeExtension(path,".layout.json"),JsonSerializer.Serialize(new {Height=ActualHeight,Width=ActualWidth,WorkAreaHeight=workArea.Height,DpiScale=dpi.DpiScaleY,HeightRatio=ActualHeight*dpi.DpiScaleY/workArea.Height,State=WindowState.ToString()},new JsonSerializerOptions {WriteIndented=true}));
        if(cards){
            releases=[new(101,"example/editor","v2.4.0","编辑器","2026-10-08 · v2.4.0\n## 更新内容\n- 新增工作区搜索，支持按文件类型筛选。\n- 优化大文件打开性能，减少等待时间。\n- 修复窗口恢复后布局错位的问题。\n- 改善快捷键提示。",false,DateTimeOffset.Now,"",[new(1,"Editor-win-x64.exe",50000000,"","")]),new(102,"example/sync","v1.8.2","同步工具","2026-10-08 · v1.8.2",false,DateTimeOffset.Now.AddHours(-3),"",[new(2,"Sync-win-x64.zip",10000000,"","")]),new(103,"example/android","v3.0.0-beta.2","Android 客户端","测试版：调整导航与主题，欢迎试用并反馈问题。",true,DateTimeOffset.Now.AddDays(-1),"",[new(3,"Client.apk",6000000,"","")])];
            if(avatars){AvatarCache.Configure(settings);releases[0]=releases[0] with {Repository="x6nux/zed-globalization"};await AvatarCache.Load("x6nux");}
            settings.Dark=dark;ApplyTheme();if(local){installed=[new("fixture1","示例编辑器","2.3.0",""),new("fixture2","示例同步工具","1.8.0","")];settings.Bindings=[new(){AppId="fixture1",Repository="example/editor"},new(){AppId="fixture2",Repository="example/sync"}];PlanUpdates();}
            if(!local){groups=[new("fixture-tools","效率工具","GitHub",["example/editor","example/sync"]),new("fixture-android","Android","GitHub",["example/android"])];settings.LocalGroups=[new("fixture-local","我的常用","本地",["example/editor"])];RebuildGroups();}
            ChannelFilter.SelectedIndex=1;Page(local);
            if(firstGroup&&!local)GroupSidebar.SelectedIndex=2;
            if(batchPreview){model.SelectionMode=true;model.Cards[0].IsSelected=true;}
            if(progressPreview){model.Cards[0].Transfer.Stage("下载中 0%",false);model.Cards[0].Transfer.Downloaded(27000000,50000000);if(model.Cards.Count>1)model.Cards[1].Transfer.Stage("安装器运行中");var before=model.Cards[0].Transfer;Page(!local);Page(local);File.WriteAllText(Path.ChangeExtension(path,".progress.json"),JsonSerializer.Serialize(new{ProgressPreserved=ReferenceEquals(before,model.Cards[0].Transfer),Label=model.Cards[0].Transfer.Label,CountCaption=model.CountCaption,Count=model.Count},new JsonSerializerOptions{WriteIndented=true}));}
        }
        if(Environment.GetCommandLineArgs().Contains("--startup-completion")){
            await Execute("检测","启动时复检已关联软件",ct=>{ct.ThrowIfCancellationRequested();PlanUpdates();Display();StartupCheckCompleted();return Task.CompletedTask;},true,resources:["cache"]);
            var startupStatus=model.Status;var startupFinished=operations.Last().State=="完成"&&startupStatus.StartsWith("启动复检完成");
            await Execute("检测","无自定义提示的任务",ct=>Task.CompletedTask,true,resources:["cache"]);var fallback=model.Status=="无自定义提示的任务 · 完成";
            await Execute("检测","自定义完成提示的任务",ct=>{model.Status="自定义结果保留";return Task.CompletedTask;},true,resources:["cache"]);var preserved=model.Status=="自定义结果保留";
            if(!startupFinished||!fallback||!preserved)throw new IOException("Task completion status regression");model.Status=startupStatus;
            File.WriteAllText(Path.ChangeExtension(path,".completion.json"),JsonSerializer.Serialize(new{StartupFinished=startupFinished,Status=startupStatus,FallbackFinished=fallback,CustomStatusPreserved=preserved}));
        }
        if(projectsPreview){api.Token="offline-fixture";settings.Account="example";AccountLabel();settings.Dark=dark;ApplyTheme();myProjects=[new("example/Gitstar","跨设备 GitHub 项目管理与软件更新助手",false,"main",true,true,false,DateTimeOffset.Now),new("example/NotesKit","轻量笔记与知识库工具",true,"main",true,true,false,DateTimeOffset.Now.AddDays(-1)),new("team/CloudBridge","参与协作的服务连接组件",false,"develop",true,false,false,DateTimeOffset.Now.AddDays(-2))];projectAutomation["example/Gitstar"]=new("example/Gitstar",@"D:\Agant\Github_Source\Gitstar",@"D:\Agant\Github_Releases\Gitstar","windows/Directory.Build.props","main");projectsSynced=DateTimeOffset.Now;projectWorkspaces=[new("example/Gitstar",@"D:\Projects\Gitstar","main")];projectStates["example/Gitstar"]=new(@"D:\Projects\Gitstar","main","https://github.com/example/Gitstar.git","8a72bd918e23f41c456789012345678901234567",[new(" M","src/MainWindow.xaml"),new("??","docs/Gitstar使用说明.md"),new("??","docs/Windows版本设计.md"),new("??","docs/产品与技术设计.md")],"2 files changed");projectVersions["example/Gitstar"]=[new(2,"v0.2.0-beta.1","工作区管理升级","新增项目工作台、源码变更选择与草稿管理。",true,true,2,DateTimeOffset.Now,""),new(1,"v0.1.1","正式版","优化扫描速度与本机更新体验。",false,false,2,DateTimeOffset.Now.AddDays(-2),"")];Projects_Click(this,new());}
        if(projectsPreview&&Environment.GetCommandLineArgs().Contains("--deleted-project-state")){
            var repo=myProjects.First().FullName;var folder=Path.Combine(store.Root,"retained-project-files");Directory.CreateDirectory(folder);var sourceFile=Path.Combine(folder,"source.txt");var releaseFile=Path.Combine(folder,"installer.exe");File.WriteAllText(sourceFile,"retained source");File.WriteAllText(releaseFile,"retained installer");projectWorkspaces=[new(repo,folder,"main")];projectAutomation[repo]=new(repo,folder,folder);settings.ExtraRepositories.Add(repo);settings.Bindings=[new(){AppId="fixture-app",Repository=repo}];releases=[new(9,repo,"1.0.0.0","","",false,DateTimeOffset.Now,"",[])];store.Set(CacheKey+"/http-releases/"+repo.ToLowerInvariant()+"/1",new{ETag="fixture"});Save();SaveProjects();SaveCache();ForgetDeletedProject(repo);
            using var reopened=new LocalStore(store.Root);var saved=reopened.Get(ProjectsKey,new List<ManagedProject>());var workspaces=reopened.Get(ProjectsKey+"/workspaces",new List<ProjectWorkspace>());var configs=reopened.Get(ProjectsKey+"/automation",new Dictionary<string,ProjectAutomation>());var preferences=reopened.Get("settings",new Settings());
            var cleared=!saved.Any(project=>project.FullName==repo)&&!workspaces.Any(workspace=>workspace.Repository==repo)&&!configs.ContainsKey(repo)&&!reopened.Get(CacheKey+"/releases",new List<Release>()).Any(release=>release.Repository==repo)&&!reopened.DocumentSizes().ContainsKey(CacheKey+"/http-releases/"+repo.ToLowerInvariant()+"/1");var filesRetained=File.ReadAllText(sourceFile)=="retained source"&&File.ReadAllText(releaseFile)=="retained installer";var bindingPaused=preferences.Bindings.Single().Disabled;
            if(!cleared||!filesRetained||!bindingPaused||saved.Count!=2)throw new IOException("Project deletion cleanup regression");File.WriteAllText(Path.ChangeExtension(path,".project-deleted.json"),JsonSerializer.Serialize(new{CacheAndAssociationsCleared=cleared,LocalFilesRetained=filesRetained,BindingPaused=bindingPaused,OtherProjectsRetained=saved.Count}));
        }
        if(projectsPreview&&Environment.GetCommandLineArgs().Contains("--delete-project")){DeleteProjectDialog(myProjects.First() with{Id=100},path);exit=true;tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}
        if(projectsPreview&&Environment.GetCommandLineArgs().Contains("--release-directory")){LinkReleaseDirectoryDialog(myProjects.First(),path);exit=true;tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}
        if(projectsPreview&&Environment.GetCommandLineArgs().Contains("--published-state")){
            var repo="example/Gitstar";var confirmed=projectVersions[repo][0] with{Tag="1.0.0.0",Draft=false,Prerelease=false,AssetCount=4};RememberProjectVersion(repo,confirmed);
            using var reopened=new LocalStore(store.Root);var persisted=reopened.Get(ProjectsKey+"/versions",new Dictionary<string,List<ProjectVersion>>())[repo].Single(version=>version.Id==confirmed.Id);
            if(persisted!=confirmed)throw new IOException("Published state persistence failed");
            File.WriteAllText(Path.ChangeExtension(path,".release-state.json"),JsonSerializer.Serialize(new{persisted.Draft,persisted.Channel,persisted.AssetCount,Persisted=true}));
            UpdateLayout();var versionSection=(FrameworkElement)ProjectDetails.Children[3];((ScrollViewer)ProjectDetails.Parent).ScrollToVerticalOffset(versionSection.TranslatePoint(new Point(),ProjectDetails).Y);
        }
        if(Environment.GetCommandLineArgs().Contains("--cache-clean-state")){
            var bindings=settings.Bindings.Count;store.Set("settings",settings);SaveCache();SaveInstalledSnapshot();var before=updates.Count;
            await CacheCleanupService.Clear(store,settings.Account,CacheModule.RecentUpdates,CancellationToken.None,ApplyCacheCleared);Page(false);var recentEmpty=model.Cards.Count==0;Page(true);var localPreserved=updates.Count==before&&installed.Count>0;
            await CacheCleanupService.Clear(store,settings.Account,CacheModule.InstalledApps,CancellationToken.None,ApplyCacheCleared);var localEmpty=updates.Count==0;LoadCache();PlanUpdates();Display();
            if(!recentEmpty||!localPreserved||!localEmpty||settings.Bindings.Count!=bindings||!recentCacheCleared||installed.Count!=0)throw new IOException("Module cache isolation failed");
            File.WriteAllText(Path.ChangeExtension(path,".cache-clean.json"),JsonSerializer.Serialize(new{RecentEmpty=recentEmpty,LocalPreserved=localPreserved,LocalEmpty=localEmpty,BindingsRetained=settings.Bindings.Count,ClearStatePersisted=recentCacheCleared}));
        }
        if(Environment.GetCommandLineArgs().Contains("--cache-cleanup")){
            store.Set("settings",settings);SaveCache();SaveProjects();SaveInstalledSnapshot();operations.AddRange([new(){State="完成",Description="已完成下载"},new(){State="已取消",Description="已取消任务"},new(){State="结果待核实",Description="需要核实的发布",RemoteId=1}]);SaveOperations();CacheCleanupDialog(previewPath:path);exit=true;tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;
        }
        if(projectsPreview&&Environment.GetCommandLineArgs().Contains("--deleted-release-state")){
            var repo="example/Gitstar";var id=projectVersions[repo][0].Id;releases=[new(id,repo,"1.0.0.0","","",false,DateTimeOffset.Now,"",[])];ForgetProjectVersion(repo,id);
            using var reopened=new LocalStore(store.Root);var history=reopened.Get(ProjectsKey+"/versions",new Dictionary<string,List<ProjectVersion>>())[repo];var savedReleases=reopened.Get(CacheKey+"/releases",new List<Release>());
            if(history.Any(version=>version.Id==id)||savedReleases.Any(version=>version.Id==id))throw new IOException("Deleted release remains in persistent cache");File.WriteAllText(Path.ChangeExtension(path,".deleted-release.json"),JsonSerializer.Serialize(new{RemovedFromProjectCache=true,RemovedFromUpdateCache=true,OtherVersionsRetained=history.Count}));
        }
        if(projectsPreview&&Environment.GetCommandLineArgs().Contains("--release-assets")){
            var project=myProjects.First() with{Id=100};var version=projectVersions[project.FullName][0] with{Tag="1.0.0.0",Name="1.0.0.0",Draft=false,Prerelease=false,AssetCount=4};
            ReleaseManagerDialog(new(project,version,false,"fixture",[new(10,"Gitstar.exe",1141999,"sha256:fixture","fixture",""),new(20,"Gitstar-setup.exe",2655147,"sha256:fixture","fixture",""),new(30,"USAGE.md",24994,"sha256:fixture","fixture",""),new(40,"SHA256SUMS.txt",250,"sha256:fixture","fixture","")]),path);exit=true;tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;
        }
        if(automationPreview){AutomationConfigDialog(myProjects.First(),path);exit=true;tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}if(workspacePreview){await ResolveWorkspaceRoot(this,myProjects.First(),@"D:\Agant\github-update\outputs\Gitstar-source","main",previewPath:path);exit=true;tray.Icon?.Dispose();tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}
        if(sourcePreview || createPreview || projectSettingsPreview || publishPreview){if(sourcePreview)SourceDialog(myProjects.First(),path);else if(publishPreview)PublishDialog(myProjects.First(),Environment.GetCommandLineArgs().Contains("--temporary-draft")?projectVersions["example/Gitstar"][0] with{Tag="untagged-abcd",Name="1.0.0.0"}:null,path);else if(projectSettingsPreview)EditProjectDialog(myProjects.First(),path);else CreateProjectDialog(path);exit=true;tray.Icon?.Dispose();tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}
        File.WriteAllText(Path.ChangeExtension(path,".layout.json"),JsonSerializer.Serialize(new {Height=ActualHeight,Width=ActualWidth,WorkAreaHeight=workArea.Height,DpiScale=dpi.DpiScaleY,HeightRatio=ActualHeight*dpi.DpiScaleY/workArea.Height,State=WindowState.ToString(),HeaderHeight=((FrameworkElement)Content).Margin.Top+BrandRow.ActualHeight+PageHeaderRow.ActualHeight+PageHeaderRow.Margin.Top+PageHeaderRow.Margin.Bottom,Cards=model.Cards.Count,SelectedGroup=(GroupSidebar.SelectedItem as GroupEntry)?.Id},new JsonSerializerOptions {WriteIndented=true}));
        if(tasksPreview){
            var a=new Operation{Kind="下载",Description="example/editor · Editor-x64.exe",State="执行中"};var b=new Operation{Kind="发布",Description="example/sync · v1.9.0",State="等待执行"};var c=new Operation{Kind="下载",Description="example/android · Client.apk",State="已暂停（下载重试会重新开始）"};operations.AddRange([a,b,c]);
            var failed=new Operation{Kind="自动发布",Repository="example/Gitstar",Description="example/Gitstar",State="结果待核实：安装包命名结果未确认。",RemoteId=411245039,Results=["成功：源码已同步"]};operations.Add(failed);projectAutomation[failed.Repository]=new(failed.Repository,"source","release");
            using var first=taskCoordinator.Start(a.Id,["file:a"]);using var second=taskCoordinator.Start(b.Id,["repo:b"]);var progress=new TransferState();progress.Stage("下载中 42%",false);progress.Percentage=42;progress.Detail="21.0 / 50.0 MB";taskTransfers[a.Id]=progress;TasksDialog(path);exit=true;tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;
        }        if(settingsPreview){SettingsDialog(path);exit=true;tray.Icon?.Dispose();tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}
        if(groupPreview || addPreview){if(groupPreview)GroupEditor(settings.LocalGroups.FirstOrDefault(),path);else AddProjectDialog(path);exit=true;tray.Icon?.Dispose();tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}
        if(matching){settings.Bindings.Clear();installed=[new("fixture1","示例编辑器","2.3.0","",RepositoryHint:"example/editor"),new("fixture2","Sync","1.8.0",""),new("fixture3","未识别的软件","1.0","")];repositories=[new("example/editor","",false),new("example/sync","",false)];AutoBindingDialog(path);exit=true;tray.Icon?.Dispose();tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();return;}
        if(!Environment.GetCommandLineArgs().Contains("--startup-completion"))model.Status=cards?"离线样例渲染 · 示例项目仅用于界面检查，不是已同步的 GitHub 数据":"离线界面渲染检查 · 未连接 GitHub";UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var root=(FrameworkElement)Content;var client=new System.Windows.Media.Imaging.CroppedBitmap(bitmap,new Int32Rect(0,0,Math.Min(bitmap.PixelWidth,(int)(root.ActualWidth+root.Margin.Left+root.Margin.Right)),Math.Min(bitmap.PixelHeight,(int)(root.ActualHeight+root.Margin.Top+root.Margin.Bottom))));var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(client));using(var stream=File.Create(path))encoder.Save(stream);exit=true;tray.Icon?.Dispose();tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();
    }
}






























