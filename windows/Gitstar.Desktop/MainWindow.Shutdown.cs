using System.Diagnostics;
using System.Windows;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private int shutdownCleanupQueued;
    internal void BeginSessionEnding()
    {
        // WPF raises SessionEnding for shutdown, restart and logoff, including a tray-only startup.
        // This handler must not wait for callbacks, network completion, database locks or user input.
        ApplicationExit.BeginSessionEnding();quitting=exit=true;
        timer.Stop();backgroundMemoryTimer.Stop();store.BeginShutdown();
        ApplicationExit.Observe(taskCoordinator.CancelAllAsync());ApplicationExit.Cancel(rankingRequest);
        QueueShutdownCleanup();
    }

    private void QueueShutdownCleanup()
    {
        if(Interlocked.Exchange(ref shutdownCleanupQueued,1)!=0)return;
        var client=api;
        ApplicationExit.Observe(Task.Run(()=>{try{client.Dispose();}finally{store.Dispose();}}));
    }

    internal void ApplicationExited()
    {
        exit=quitting=true;store.BeginShutdown();QueueShutdownCleanup();
        // Explorer removes tray icons with the session. Avoid Shell IPC on the system shutdown path.
        if(!ApplicationExit.IsSessionEnding){try{tray.Visible=false;tray.Dispose();}catch(ObjectDisposedException){}}
    }

    public async void RequestExit()
    {
        if(quitting)return;
        if(busy&&DialogMessage.Show(this,"有多个任务可能正在执行。暂停后退出？下载重试会重新开始；发布或推送结果不明会留待核实。","Gitstar",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        if(ApplicationExit.IsSessionEnding)return;
        // Resolve ordinary edit guards before committing a manual exit; they cannot run inside Application.Shutdown.
        foreach(var window in OwnedWindows.Cast<Window>().Where(window=>window.IsVisible).ToArray()){
            window.Close();if(window.IsVisible||ApplicationExit.IsSessionEnding)return;
        }
        quitting=true;ApplicationExit.BeginExit();timer.Stop();backgroundMemoryTimer.Stop();
        ApplicationExit.Observe(taskCoordinator.CancelAllAsync());ApplicationExit.Cancel(rankingRequest);
        var grace=Stopwatch.StartNew();while(busy&&grace.Elapsed<TimeSpan.FromSeconds(2)&&!ApplicationExit.IsSessionEnding)await Task.Delay(25);
        if(ApplicationExit.IsSessionEnding)return;
        // Settings and task checkpoints are committed during normal operations. No VACUUM or final rescan here.
        exit=true;ApplicationExited();Application.Current.Shutdown();
    }
}
