namespace Gitstar.Core;

public static class RepositoryChecks
{
    public static async Task Run(IReadOnlyList<string> names,Func<string,CancellationToken,Task> check,CancellationToken ct,int concurrency=24)
    {
        var next=-1;var stopped=0;
        async Task Worker()
        {
            while(Volatile.Read(ref stopped)==0){
                ct.ThrowIfCancellationRequested();var index=Interlocked.Increment(ref next);if(index>=names.Count)return;
                try{await check(names[index],ct);}catch{Interlocked.Exchange(ref stopped,1);throw;}
            }
        }
        if(concurrency<1)throw new ArgumentOutOfRangeException(nameof(concurrency));
        await Task.WhenAll(Enumerable.Range(0,Math.Min(concurrency,names.Count)).Select(_=>Worker()));
    }
}


