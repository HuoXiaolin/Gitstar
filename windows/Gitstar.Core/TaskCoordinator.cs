namespace Gitstar.Core;

// Independent tasks overlap; only tasks touching the same resource wait for one another.
public sealed class TaskCoordinator
{
    internal sealed class Gate {public readonly SemaphoreSlim Semaphore=new(1,1);public int References;}
    private readonly object sync=new();
    private readonly Dictionary<string,Gate> gates=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,Run> runs=[];
    public int Count {get{lock(sync)return runs.Count;}}
    public bool Contains(string id){lock(sync)return runs.ContainsKey(id);}
    public string? StopReason(string id){lock(sync)return runs.TryGetValue(id,out var run)?run.StopReason:null;}
    public bool Uses(string resource){lock(sync)return gates.TryGetValue(resource,out var gate)&&gate.References>0;}
    public Run Start(string id,IEnumerable<string> resources)
    {
        var keys=resources.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        lock(sync){
            if(runs.ContainsKey(id))throw new ArgumentException("此任务已在运行。",nameof(id));
            var claims=keys.Select(key=>{
                if(!gates.TryGetValue(key,out var gate))gates[key]=gate=new();gate.References++;return (key,gate);
            }).ToArray();
            var run=new Run(this,id,claims);runs.Add(id,run);return run;
        }
    }
    public void Pause(string id)=>StopOne(id,"暂停");
    public void Cancel(string id)=>StopOne(id,"取消");
    private void StopOne(string id,string reason)
    {
        Run? run;lock(sync){if(!runs.TryGetValue(id,out run))return;run.StopReason=reason;}
        // Cancellation callbacks may dispose a run or cancel children; never invoke them under the coordinator lock.
        try{run.Stop.Cancel();}catch(ObjectDisposedException){}
    }
    public void CancelAll()
    {
        Run[] pending;lock(sync)pending=runs.Values.ToArray();
        foreach(var run in pending)try{run.Stop.Cancel();}catch(ObjectDisposedException){}
    }
    public Task CancelAllAsync()
    {
        Run[] pending;lock(sync)pending=runs.Values.ToArray();
        var tasks=new List<Task>();
        foreach(var run in pending)try{tasks.Add(run.Stop.CancelAsync());}catch(ObjectDisposedException){}
        return Task.WhenAll(tasks);
    }
    public sealed class Run:IDisposable
    {
        private readonly TaskCoordinator owner;private readonly (string Key,Gate Gate)[] claims;private int acquired;private bool disposed;
        internal readonly CancellationTokenSource Stop=new();public CancellationToken Token=>Stop.Token;public string Id {get;}public string StopReason {get;internal set;}="暂停";
        internal Run(TaskCoordinator owner,string id,(string,Gate)[] claims){this.owner=owner;Id=id;this.claims=claims;}
        public async Task WaitAsync(){foreach(var claim in claims){await claim.Gate.Semaphore.WaitAsync(Token);acquired++;}Token.ThrowIfCancellationRequested();}
        public void Dispose(){lock(owner.sync){if(disposed)return;disposed=true;for(var i=0;i<acquired;i++)claims[i].Gate.Semaphore.Release();foreach(var claim in claims)if(--claim.Gate.References==0){owner.gates.Remove(claim.Key);claim.Gate.Semaphore.Dispose();}owner.runs.Remove(Id);Stop.Dispose();}}
    }
}

