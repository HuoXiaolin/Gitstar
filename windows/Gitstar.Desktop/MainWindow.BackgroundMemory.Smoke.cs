using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private static object MemorySample(){using var process=Process.GetCurrentProcess();process.Refresh();return new{ResidentBytes=process.WorkingSet64,PrivateBytes=process.PrivateMemorySize64,ManagedBytes=GC.GetTotalMemory(false)};}
    private async Task BackgroundMemorySmoke(string path)
    {
        var baseline=Environment.GetCommandLineArgs().Contains("--background-baseline");
        model.SelectionMode=true;if(model.Cards.Count>0)model.Cards[0].IsSelected=true;
        var selectedCards=model.Cards.Where(card=>card.IsSelected).Select(card=>(card.Repository,card.Release.Id)).ToHashSet();
        var project=selectedProject;var releaseIds=releases.Select(release=>release.Id).ToArray();var repoCount=repositories.Count;var preference=settings.RecentSort;
        store.Set("background-memory-marker","keep");
        UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(1500);
        var foreground=MemorySample();Hide();
        var activeGuard=true;
        if(!baseline){
            using var run=taskCoordinator.Start("background-memory-guard",["fixture-memory"]);await run.WaitAsync();
            await ReclaimBackgroundMemory();activeGuard=!backgroundViewsReleased&&!run.Token.IsCancellationRequested;
        }
        else backgroundMemoryTimer.Stop();
        await Task.Delay(12500);var idle=MemorySample();
        var cleared=baseline||backgroundViewsReleased&&model.Cards.Count==0&&model.Projects.Count==0&&ProjectDetails.Children.Count==0;
        var dataRetained=releases.Select(release=>release.Id).SequenceEqual(releaseIds)&&repositories.Count==repoCount&&store.Get("background-memory-marker","")=="keep";
        var foregroundGuard=true;
        await Task.Delay(5000);var settled=MemorySample();var watch=Stopwatch.StartNew();Reopen();UpdateLayout();watch.Stop();
        var projectRestored=selectedProject==project&&(!projectsPage||ProjectDetails.Children.Count>0);
        Page(false);model.SelectionMode=true;Display();
        var selectionRestored=selectedCards.SetEquals(model.Cards.Where(card=>card.IsSelected).Select(card=>(card.Repository,card.Release.Id)));
        await ReclaimBackgroundMemory();foregroundGuard=!backgroundViewsReleased&&IsVisible&&model.Cards.Count>0;
        WindowState=WindowState.Minimized;
        if(!baseline)await ReclaimBackgroundMemory();else backgroundMemoryTimer.Stop();
        var minimizedSupported=baseline||backgroundViewsReleased;Reopen();Display();
        var restored=model.Cards.Count>0&&settings.RecentSort==preference;
        Hide();backgroundMemoryTimer.Stop();var changed=releases[0] with{Tag="v9.9.9"};releases[0]=changed;Display();Reopen();
        var shortHideUpdated=model.Cards.Any(card=>card.Release.Id==changed.Id&&card.Version==changed.Tag);
        var report=new{Baseline=baseline,Foreground=foreground,BackgroundIdle=idle,BackgroundSettled=settled,ReopenMilliseconds=watch.Elapsed.TotalMilliseconds,ActiveTasksUntouched=activeGuard,ViewsReleased=cleared,DataRetained=dataRetained,ProjectSelectionRestored=projectRestored,CardSelectionRestored=selectionRestored,ForegroundUntouched=foregroundGuard,MinimizedSupported=minimizedSupported,ViewsRestored=restored,ShortHideShowsLatestCache=shortHideUpdated};
        File.WriteAllText(Path.ChangeExtension(path,".background-memory.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        if(!activeGuard||!cleared||!dataRetained||!projectRestored||!selectionRestored||!foregroundGuard||!minimizedSupported||!restored||!shortHideUpdated)throw new IOException("Background memory lifecycle regression; see the diagnostic report.");
    }
}
