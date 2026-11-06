using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private bool sessionShutdownSmokeStarted;
    internal async Task SessionShutdownSmoke(string path)
    {
        if(sessionShutdownSmokeStarted)return;sessionShutdownSmokeStarted=true;
        var args=Environment.GetCommandLineArgs();var trayOnly=args.Contains("--startup-tray");var logoff=args.Contains("--logoff");var critical=args.Contains("--critical");
        var visibleTransitions=0;IsVisibleChanged+=(_,_)=>{if(IsVisible)visibleTransitions++;};
        var normalCloseToTray=true;
        if(!trayOnly){var closing=new CancelEventArgs();OnClosing(this,closing);normalCloseToTray=closing.Cancel&&!IsVisible;Reopen();}
        store.Set("session-shutdown-marker","committed");
        var download=new Operation{Kind="下载",Description="stalled fixture download",State="执行中"};
        var publish=new Operation{Kind="发布",Description="queued fixture publication",State="等待执行"};operations.AddRange([download,publish]);SaveOperations();
        var running=taskCoordinator.Start(download.Id,["fixture-shutdown"]);await running.WaitAsync();
        var queued=taskCoordinator.Start(publish.Id,["fixture-shutdown"]);var wait=queued.WaitAsync();
        var releaseCallback=new ManualResetEventSlim();
        _=running.Token.Register(()=>releaseCallback.Wait(TimeSpan.FromSeconds(10)));
        var gate=typeof(LocalStore).GetField("gate",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!;
        var holdingStore=new ManualResetEventSlim();var releaseStore=new ManualResetEventSlim();
        _=Task.Run(()=>{lock(gate){holdingStore.Set();releaseStore.Wait(TimeSpan.FromSeconds(10));}});
        if(!await Task.Run(()=>holdingStore.Wait(TimeSpan.FromSeconds(2))))throw new IOException("Fixture store gate was not acquired");
        timer.Start();backgroundMemoryTimer.Start();
        Form? dirty=null;var dirtyBefore=false;var dirtyClosed=trayOnly;
        var app=Application.Current;var watch=new Stopwatch();var queryMilliseconds=0d;var allowed=false;var latePromptSuppressed=false;
        app.Exit+=(_,_)=>{
            var elapsed=watch.Elapsed.TotalMilliseconds;
            var flags=ApplicationExit.IsSessionEnding&&exit&&quitting;
            var stopped=!timer.IsEnabled&&!backgroundMemoryTimer.IsEnabled&&running.Token.IsCancellationRequested&&queued.Token.IsCancellationRequested;
            var hidden= !trayOnly || visibleTransitions==0&&new WindowInteropHelper(this).Handle==IntPtr.Zero;
            releaseStore.Set();releaseCallback.Set();
            using var saved=new LocalStore(store.Root);
            var checkpoints=saved.Get("session-shutdown-marker","")=="committed"&&saved.Get(OperationsKey,new List<Operation>()).Any(item=>item.Id==download.Id&&item.State=="执行中");
            var passed=allowed&&queryMilliseconds<500&&elapsed<1500&&flags&&stopped&&dirtyClosed&&normalCloseToTray&&hidden&&latePromptSuppressed&&checkpoints;
            File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=passed,Reason=logoff?"Logoff":critical?"Critical":"ShutdownOrRestart",TrayOnly=trayOnly,AcceptedImmediately=allowed,QueryMilliseconds=queryMilliseconds,ExitEventMilliseconds=elapsed,DirtySettingsPrepared=dirtyBefore,DirtySettingsClosedWithoutPrompt=dirtyClosed,BackgroundWorkStopped=stopped,SlowCallbackAndDatabaseNotAwaited=elapsed<1500,NormalCloseStillUsesTray=normalCloseToTray,HiddenStartupNeverShown=hidden,LatePromptsSuppressed=latePromptSuppressed,CommittedDataRetained=checkpoints,RealComputerShutdown=false}));
            if(!passed)Environment.ExitCode=1;
        };
        void Query()
        {
            var parking=typeof(Application).GetField("_parkingHwnd",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(app)??throw new IOException("WPF application notification window unavailable");
            var handle=(IntPtr)(parking.GetType().GetProperty("Handle",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(parking)??IntPtr.Zero);
            if(handle==IntPtr.Zero)throw new IOException("Missing isolated notification HWND");
            watch.Start();var query=Stopwatch.StartNew();var flags=logoff?unchecked((int)0x80000000):critical?0x40000000:0;
            allowed=SendShutdownFixtureMessage(handle,0x0011,IntPtr.Zero,new IntPtr(flags))!=IntPtr.Zero;queryMilliseconds=query.Elapsed.TotalMilliseconds;
            latePromptSuppressed=DialogMessage.Show(this,"must not prompt during shutdown","fixture",MessageBoxButton.YesNo)==MessageBoxResult.No;
        }
        if(trayOnly)Query();
        else{
            dirty=new Form(this,"设置 · 退出验证",650,420);var text=dirty.Text("示例设置","original");dirty.EnableEditGuard();dirty.Closed+=(_,_)=>dirtyClosed=true;
            dirty.ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(()=>{text.Text="unsaved change";dirtyBefore=dirty.HasUnsavedChanges;if(!dirtyBefore)throw new IOException("Dirty settings fixture was not prepared");Query();},DispatcherPriority.ApplicationIdle);
            dirty.ShowDialog();
        }
    }

    [DllImport("user32.dll",EntryPoint="SendMessageW")]
    private static extern IntPtr SendShutdownFixtureMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
}
