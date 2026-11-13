using Gitstar.Core;
using Gitstar.Desktop;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;

internal static class ScanConcurrencyChecks
{
    private static object Release()=>new{id=1,tag_name="v2.0.0",name="v2.0.0",body="",prerelease=false,draft=false,published_at="2026-11-13T00:00:00Z",html_url="",assets=Array.Empty<object>()};
    private static HttpResponseMessage Response(HttpRequestMessage request)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(request.RequestUri!.AbsolutePath.EndsWith("/latest")?Release():request.RequestUri.AbsolutePath.EndsWith("/releases")?(object)Array.Empty<object>():new{full_name="owner/interactive",@private=false}))};
    public static async Task Run(Action<bool,string> check)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var ct=timeout.Token;
        var scanEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var allEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var sync=new object();var active=0;var scans=0;var peak=0;var scanPeak=0;var total=0;
        using(var client=new GitHubClient(new(),new FakeHttp(async(request,token)=>{
            var scan=request.RequestUri!.AbsolutePath.Contains("/releases");
            lock(sync){active++;total++;if(scan)scans++;peak=Math.Max(peak,active);scanPeak=Math.Max(scanPeak,scans);if(scans==24)scanEntered.TrySetResult();if(active==32)allEntered.TrySetResult();}
            try{await gate.Task.WaitAsync(token);return Response(request);}finally{lock(sync){active--;if(scan)scans--;}}
        }))){
            var bulk=Enumerable.Range(0,24).Select(i=>client.Releases("owner/bulk"+i,ct)).ToArray();await scanEntered.Task.WaitAsync(ct);
            var interactive=Enumerable.Range(0,8).Select(i=>client.Repo("owner/interactive"+i,ct)).ToArray();await allEntered.Task.WaitAsync(ct);
            check(scanPeak==24&&peak==32&&!gate.Task.IsCompleted,"24 occupied scan slots leave eight request slots usable for interactive reads");
            using var canceled=new CancellationTokenSource();var scanQueued=client.Repo("owner/canceled-scan",canceled.Token,background:true);var userQueued=client.Repo("owner/canceled-user",canceled.Token);canceled.Cancel();
            var stopped=0;foreach(var task in new[]{scanQueued,userQueued})try{await task;}catch(OperationCanceledException){stopped++;}
            gate.TrySetResult();await Task.WhenAll(bulk);await Task.WhenAll(interactive);
            check(stopped==2&&total==56&&active==0&&scans==0,"canceling queued scan and interactive reads does not send requests or leak occupied slots");
            await client.Releases("owner/retry",ct);check(total==58,"a scan can run again after queued cancellation");
        }

        // Exercise cancellation after a scan slot is acquired but the total read budget is full.
        var full=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);gate=new(TaskCreationOptions.RunContinuationsAsynchronously);active=0;total=0;
        using(var client=new GitHubClient(new(),new FakeHttp(async(request,token)=>{
            if(Interlocked.Increment(ref active)==32)full.TrySetResult();Interlocked.Increment(ref total);
            try{await gate.Task.WaitAsync(token);return Response(request);}finally{Interlocked.Decrement(ref active);}
        }))){
            var users=Enumerable.Range(0,32).Select(i=>client.Repo("owner/user"+i,ct)).ToArray();await full.Task.WaitAsync(ct);
            using var canceled=new CancellationTokenSource();var queued=Enumerable.Range(0,24).Select(i=>client.Repo("owner/queued"+i,canceled.Token,background:true)).ToArray();canceled.Cancel();
            var stopped=0;foreach(var task in queued)try{await task;}catch(OperationCanceledException){stopped++;}
            gate.TrySetResult();await Task.WhenAll(users);await Task.WhenAll(Enumerable.Range(0,24).Select(i=>client.Repo("owner/retry"+i,ct,background:true)));
            check(stopped==24&&total==56&&active==0,"canceling at the shared read gate returns the acquired scan slots for the next batch");
        }

        async Task<(long Milliseconds,int Peak,int Completed)> Measure(int workers)
        {
            var running=0;var max=0;var completed=0;var measurementSync=new object();
            using var client=new GitHubClient(new(),new FakeHttp(async(request,token)=>{
                lock(measurementSync){running++;max=Math.Max(max,running);}try{await Task.Delay(80,token);return Response(request);}finally{lock(measurementSync)running--;}
            }));
            var clock=Stopwatch.StartNew();await RepositoryChecks.Run(Enumerable.Range(0,96).Select(i=>"owner/repo"+i).ToArray(),async(name,token)=>{var found=await client.Releases(name,token);if(found.Count==1&&found[0].IsLatest)Interlocked.Increment(ref completed);},ct,workers);
            return(clock.ElapsedMilliseconds,max,completed);
        }
        var previous=await Measure(8);var current=await Measure(RepositoryChecks.DefaultConcurrency);
        check(previous.Completed==96&&current.Completed==96&&previous.Peak==16&&current.Peak==24,"restored repository parallelism increases actual overlapping reads from 16 to 24 without losing results");
        Console.WriteLine("SCAN_TIMING "+JsonSerializer.Serialize(new{Repositories=96,DelayPerRequestMs=80,PreviousMilliseconds=previous.Milliseconds,CurrentMilliseconds=current.Milliseconds,PreviousRequests=previous.Peak,CurrentRequests=current.Peak}));
    }
}
