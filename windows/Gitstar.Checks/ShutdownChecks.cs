using System.Diagnostics;
using System.IO;
using System.Reflection;
using Gitstar.Core;
using Gitstar.Desktop;

internal static class ShutdownChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var coordinator=new TaskCoordinator();using var run=coordinator.Start("stalled",["fixture-resource"]);await run.WaitAsync();
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var registration=run.Token.Register(()=>{entered.Set();release.Wait(TimeSpan.FromSeconds(5));});
        var clock=Stopwatch.StartNew();var cancellation=coordinator.CancelAllAsync();clock.Stop();
        try{
            check(clock.Elapsed<TimeSpan.FromMilliseconds(500)&&run.Token.IsCancellationRequested,"shutdown cancellation returns immediately and marks tokens without waiting for slow callbacks");
            check(await Task.Run(()=>entered.Wait(TimeSpan.FromSeconds(2)))&&!cancellation.IsCompleted,"slow cancellation callbacks run asynchronously outside the shutdown caller");
            var other=Task.Run(()=>{using var independent=coordinator.Start("independent",["other-resource"]);return coordinator.Count;});
            check(await Task.WhenAny(other,Task.Delay(1000))==other,"slow cancellation callback does not hold the coordinator lock");
        }finally{release.Set();await cancellation.WaitAsync(TimeSpan.FromSeconds(2));}
        check(cancellation.IsCompletedSuccessfully,"asynchronous task cancellation can still drain normally");
        using var store=new LocalStore(Path.Combine(root,"shutdown-store"));store.Set("setting","saved-before-shutdown");
        var gate=typeof(LocalStore).GetField("gate",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!;
        using var holding=new ManualResetEventSlim();using var releaseStore=new ManualResetEventSlim();
        var holder=Task.Run(()=>{lock(gate){holding.Set();releaseStore.Wait(TimeSpan.FromSeconds(5));}});
        await Task.Run(()=>holding.Wait(TimeSpan.FromSeconds(2)));
        try{
            clock.Restart();store.BeginShutdown();store.Set("setting","late-write");var fallback=store.Get("setting","fallback");store.ReclaimUnusedSpace();store.ReleaseMemory();var sizes=store.DocumentSizes();clock.Stop();
            check(clock.Elapsed<TimeSpan.FromMilliseconds(500)&&fallback=="fallback"&&sizes.Count==0,"session shutdown skips late cache reads writes and compaction even while the database gate is busy");
        }finally{releaseStore.Set();await holder;}
        using var reopened=new LocalStore(store.Root);
        check(reopened.Get("setting","")=="saved-before-shutdown","already committed settings remain recoverable without shutdown-time writes or compaction");
    }
}
