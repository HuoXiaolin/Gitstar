using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private readonly DispatcherTimer backgroundMemoryTimer=new(){Interval=TimeSpan.FromSeconds(10)};
    private readonly HashSet<(string Repository,long Id)> backgroundSelections=[];
    private bool backgroundViewsReleased,reclaimingBackgroundMemory,backgroundDisplayPending;
    private bool IsInBackground=>ready&&(!IsVisible||WindowState==WindowState.Minimized);
    private bool CanReclaimBackgroundMemory=>IsInBackground&&!busy&&!batchPublishing&&!refreshing&&!clearingCache&&!exit&&!quitting&&!OwnedWindows.Cast<Window>().Any(window=>window.IsVisible);

    private void ConfigureBackgroundMemory()
    {
        IsVisibleChanged+=(_,_)=>BackgroundVisibilityChanged();
        StateChanged+=(_,_)=>BackgroundVisibilityChanged();
        backgroundMemoryTimer.Tick+=async(_,_)=>{
            if(!IsInBackground||exit){backgroundMemoryTimer.Stop();return;}
            if(!CanReclaimBackgroundMemory)return;
            backgroundMemoryTimer.Stop();await ReclaimBackgroundMemory();
        };
    }
    private void BackgroundVisibilityChanged()
    {
        if(IsInBackground)QueueBackgroundMemoryReclaim();else ResumeBackgroundViews();
    }
    private void QueueBackgroundMemoryReclaim()
    {
        if(IsInBackground&&!exit){backgroundDisplayPending=true;if(!backgroundMemoryTimer.IsEnabled)backgroundMemoryTimer.Start();}
    }
    private void ReleaseBackgroundViews()
    {
        if(backgroundViewsReleased)return;
        backgroundSelections.UnionWith(model.Cards.Where(card=>card.IsSelected).Select(card=>(card.Repository,card.Release.Id)));
        backgroundViewsReleased=true;
        model.Cards.Clear();
        renderingProjects=true;try{model.Projects.Clear();ProjectDetails.Children.Clear();}finally{renderingProjects=false;}
        AvatarCache.SuspendDecoded();
        if(installedScan is {IsCompleted:true})installedScan=null;
    }
    private void ResumeBackgroundViews()
    {
        if(IsInBackground)return;
        backgroundMemoryTimer.Stop();AvatarCache.ResumeDecoded();
        if(!backgroundViewsReleased&&!backgroundDisplayPending)return;
        backgroundViewsReleased=false;backgroundDisplayPending=false;Display();
    }
    private async Task ReclaimBackgroundMemory()
    {
        if(!CanReclaimBackgroundMemory||reclaimingBackgroundMemory)return;
        reclaimingBackgroundMemory=true;
        try{
            ReleaseBackgroundViews();store.ReleaseMemory();
            // Let WPF detach generated containers before collecting their visual trees.
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            if(!CanReclaimBackgroundMemory)return;
            await Task.Run(()=>GC.Collect(GC.MaxGeneration,GCCollectionMode.Aggressive,true,true));
            // One trim per background idle transition; no foreground or periodic working-set churn.
            if(CanReclaimBackgroundMemory){using var process=Process.GetCurrentProcess();K32EmptyWorkingSet(process.Handle);}
        }finally{reclaimingBackgroundMemory=false;}
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EmptyWorkingSet(IntPtr process);
}
