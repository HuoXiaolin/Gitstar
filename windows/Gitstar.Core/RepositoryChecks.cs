namespace Gitstar.Core;

public static class RepositoryChecks
{
    public const int DefaultConcurrency=24;
    public static string[] Targets(bool matchedOnly,IEnumerable<Repository> stars,Settings settings)=>(matchedOnly?settings.Bindings.Where(binding=>!binding.Disabled).Select(binding=>binding.Repository):stars.Select(repo=>repo.FullName).Concat(settings.ExtraRepositories).Concat(settings.Bindings.Where(binding=>!binding.Disabled).Select(binding=>binding.Repository))).Where(Rules.ValidRepository).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string[] Prioritize(IEnumerable<string> names,IEnumerable<string> bound,IEnumerable<string> visible)
    {
        var bindings=bound.ToHashSet(StringComparer.OrdinalIgnoreCase);var shown=visible.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>bindings.Contains(name)?0:shown.Contains(name)?1:2).ToArray();
    }
    public static async Task Run(IReadOnlyList<string> names,Func<string,CancellationToken,Task> check,CancellationToken ct,int concurrency=DefaultConcurrency)
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


