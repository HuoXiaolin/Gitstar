namespace Gitstar.Desktop;

internal static class ApplicationExit
{
    private static int exiting,sessionEnding;
    public static bool IsExiting=>Volatile.Read(ref exiting)!=0;
    public static bool IsSessionEnding=>Volatile.Read(ref sessionEnding)!=0;
    public static void BeginExit()=>Interlocked.Exchange(ref exiting,1);
    public static void BeginSessionEnding(){Interlocked.Exchange(ref sessionEnding,1);BeginExit();}

    public static void Observe(Task task)=>_=IgnoreFailure(task);
    private static async Task IgnoreFailure(Task task){try{await task.ConfigureAwait(false);}catch(Exception){}}
    public static void Cancel(CancellationTokenSource? source)
    {
        if(source==null)return;
        try{if(IsSessionEnding)Observe(source.CancelAsync());else source.Cancel();}catch(ObjectDisposedException){}
    }
}
