namespace Gitstar.Core;

// Join only work that is still running. A later explicit check always reads fresh data.
public sealed class SharedWork<T>
{
    private readonly object sync=new();
    private readonly Dictionary<string,Task<T>> running=new(StringComparer.Ordinal);
    public async Task<T> Run(string key,Func<Task<T>> work,CancellationToken ct=default)
    {
        Task<T> task;
        lock(sync){
            if(!running.TryGetValue(key,out task!)||task.IsCompleted){
                task=Task.Run(work);running[key]=task;
                _=task.ContinueWith(done=>{lock(sync){if(running.TryGetValue(key,out var current)&&ReferenceEquals(current,done))running.Remove(key);} _=done.Exception;},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
        }
        return await task.WaitAsync(ct);
    }
}
