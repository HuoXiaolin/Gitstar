using Gitstar.Core;
using Gitstar.Desktop;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.IO;

internal static class ScanningChecks
{
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    public static async Task Run(Action<bool,string> check,string root)
    {
        var ordered=RepositoryChecks.Prioritize(["owner/other","owner/shown","owner/bound","OWNER/BOUND"],["owner/bound"],["owner/shown"]);
        check(ordered.SequenceEqual(new[]{"owner/bound","owner/shown","owner/other"}),"scan prioritizes installed and visible repositories without duplicate work");
        var cardRelease=new Release(1,"owner/one","v1","","",false,DateTimeOffset.UtcNow,"",[]);var transfer=new TransferState();transfer.Stage("下载中");
        var originalCard=new Card{Release=cardRelease,Transfer=transfer,IsSelected=true};var cards=new System.Collections.ObjectModel.ObservableCollection<Card>{originalCard};var resets=0;cards.CollectionChanged+=(_,args)=>{if(args.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset)resets++;};
        Card.Reconcile(cards,[new(){Release=cardRelease,Transfer=transfer,IsSelected=true},new(){Release=cardRelease with{Id=2,Repository="owner/two"}}]);
        check(ReferenceEquals(cards[0],originalCard)&&cards[0].Transfer.Active&&cards[0].IsSelected&&resets==0,"progressive card refresh preserves existing containers selection and download progress without clearing the list");
        Card.Reconcile(cards,[new(){Release=cardRelease with{Body="changed notes"},Transfer=transfer}]);check(cards.Count==1&&cards[0].Release.Body=="changed notes"&&resets==0,"incremental cards replace changed data and remove obsolete rows without a full reset");
        var shared=new SharedWork<int>();var calls=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finish=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Work(){Interlocked.Increment(ref calls);started.TrySetResult();return await finish.Task;}
        using(var stop=new CancellationTokenSource()){
            var first=shared.Run("same",Work,stop.Token);await started.Task.WaitAsync(TimeSpan.FromSeconds(3));var second=shared.Run("same",Work);stop.Cancel();var canceled=false;try{await first;}catch(OperationCanceledException){canceled=true;}
            finish.SetResult(1);check(canceled&&await second==1&&calls==1,"duplicate local checks share one active scan and cancelling one waiter leaves the other intact");
        }
        check(await shared.Run("same",()=>Task.FromResult(2))==2,"completed local scans are not reused as fresh installation evidence");
        var failed=false;try{await shared.Run("failure",()=>Task.FromException<int>(new IOException("fixture")));}catch(IOException){failed=true;}
        check(failed&&await shared.Run("failure",()=>Task.FromResult(3))==3,"failed shared scans can be retried");

        var latestStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var firstStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active=0;var maximum=0;var pageRequests=new List<int>();var pageCache=new Dictionary<string,GitHubClient.ReleasePage>();var phase=0;
        object Release(int id,string tag,bool latest=false)=>new{id,tag_name=tag,name=tag,body="notes",prerelease=false,draft=false,published_at="2026-11-13T00:00:00Z",html_url="",assets=Array.Empty<object>()};
        using(var client=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            var uri=request.RequestUri!;
            if(uri.AbsolutePath.EndsWith("/latest")){latestStarted.TrySetResult();await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3),ct);return Json(Release(900,"v2.0.0"));}
            var page=int.Parse(uri.Query.Split("page=").Last());pageRequests.Add(page);if(!uri.Query.Contains("per_page=100"))throw new IOException("small scan page");
            if(page==1){firstStarted.TrySetResult();await latestStarted.Task.WaitAsync(TimeSpan.FromSeconds(3),ct);}
            active++;maximum=Math.Max(maximum,active);await Task.Delay(20,ct);active--;
            HttpResponseMessage response;
            if(phase==1){if(request.Headers.IfNoneMatch.Count==0)throw new IOException("missing ETag");response=new(HttpStatusCode.NotModified);}
            else{response=Json(page==4?(phase==2?Array.Empty<object>():new[]{Release(999,"desktop-v4.0.0")}):Enumerable.Range(0,100).Select(i=>Release(page*100+i,"v1.0."+i)).ToArray());response.Headers.ETag=new("\"page-"+page+"\"");}
            if(page==1)response.Headers.TryAddWithoutValidation("Link","<https://api.github.com/repos/owner/app/releases?per_page=100&page=4>; rel=\"last\"");return response;
        }))){
            client.ReadReleasePage=key=>pageCache.GetValueOrDefault(key);client.SaveReleasePage=(key,value)=>pageCache[key]=value;
            var binding=new Binding{Repository="owner/app",TagPrefix="desktop-"};
            var found=await client.Releases("owner/app",CancellationToken.None,[binding]);
            check(maximum is >1 and <=3&&pageRequests.Order().SequenceEqual(new[]{1,2,3,4}),"release history pages run concurrently within three workers and skip an unnecessary empty tail page");
            check(Rules.SelectRelease(found,new())?.Tag=="v2.0.0"&&Rules.SelectRelease(found,binding)?.Tag=="desktop-v4.0.0","parallel scan retains authoritative Latest and older product-line releases");
            pageRequests.Clear();phase=1;found=await client.Releases("owner/app",CancellationToken.None,[binding]);
            check(pageRequests.Count==4&&Rules.SelectRelease(found,binding)?.Tag=="desktop-v4.0.0","all cached history pages are conditionally revalidated without dropping older lines");
            phase=2;found=await client.Releases("owner/app",CancellationToken.None,[binding]);check(Rules.SelectRelease(found,binding)==null,"parallel refresh detects a deleted historical release instead of retaining stale candidates");
        }
        using(var response=new HttpResponseMessage(HttpStatusCode.OK)){response.Headers.TryAddWithoutValidation("Link","<https://evil.example/releases?page=99>; rel=\"last\"");check(GitHubClient.LastReleasePage(response)==null,"pagination ignores foreign hosts");}

        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releaseGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);active=0;maximum=0;var total=0;
        using(var client=new GitHubClient(new(),new FakeHttp(async(request,ct)=>{
            var count=Interlocked.Increment(ref active);Interlocked.Increment(ref total);lock(entered)maximum=Math.Max(maximum,count);if(count==32)entered.TrySetResult();
            try{await releaseGate.Task.WaitAsync(ct);return Json(new{full_name="owner/tool",@private=false});}finally{Interlocked.Decrement(ref active);}
        }))){
            var reads=Enumerable.Range(0,64).Select(_=>client.Repo("owner/tool")).ToArray();await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var canceled=new CancellationTokenSource();var queued=client.Repo("owner/canceled",canceled.Token);canceled.Cancel();var canceledBeforeNetwork=false;try{await queued;}catch(OperationCanceledException){canceledBeforeNetwork=true;}
            releaseGate.TrySetResult();await Task.WhenAll(reads);check(maximum==32&&total==64&&canceledBeforeNetwork,"shared API read limit allows 32 requests and queued cancellation does not reach the network");
        }
        await ScanConcurrencyChecks.Run(check);
        var batches=new List<int>();using(var client=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            var page=int.Parse(request.RequestUri!.Query.Split("page=").Last());return Task.FromResult(Json(Enumerable.Range(0,page==1?100:1).Select(i=>new{full_name="owner/repo"+(page*100+i),@private=false}).ToArray()));
        }))){client.Token="fixture";await client.MyProjects(CancellationToken.None,items=>batches.Add(items.Count));check(batches.SequenceEqual(new[]{100,1}),"my-project pages are delivered before the full listing completes");batches.Clear();await client.Stars(CancellationToken.None,items=>batches.Add(items.Count));check(batches.SequenceEqual(new[]{100,1}),"followed-repository pages stream independently of local inventory");}
        var searches=0;using(var client=new GitHubClient(new(),new FakeHttp((request,ct)=>{searches++;return Task.FromResult(Json(new{total_count=0,incomplete_results=false,items=Array.Empty<object>()}));}))){var result=await client.Rankings(RankingKind.New,0,1,"",CancellationToken.None);check(searches==3&&result.StarsFloor==0&&result.Items.Count==0,"sparse rankings stop repeated threshold halving after two expansions");}

        var inspectRoot=Path.Combine(root,"read-only-inspection");Directory.CreateDirectory(inspectRoot);var asset=Path.Combine(inspectRoot,"app.exe");await File.WriteAllTextAsync(asset,"fixture-package");var sha=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(asset)));var manifest=Path.Combine(inspectRoot,"SHA256SUMS.txt");await File.WriteAllTextAsync(manifest,sha+"  app.exe\n");
        var inspected=await ReleaseAssetFiles.Inspect([asset,manifest],CancellationToken.None);var staged=await ReleaseAssetFiles.Stage([asset,manifest],Path.Combine(root,"inspection-staged"),CancellationToken.None);
        check(inspected.Select(item=>(item.Name,item.Size,item.Sha)).SequenceEqual(staged.Select(item=>(item.Name,item.Size,item.Sha)))&&Directory.GetFiles(inspectRoot).Length==2,"read-only project inspection matches staged hashes without copying the package");
        await File.WriteAllTextAsync(asset,"changed");var rejected=false;try{await ReleaseAssetFiles.Inspect([asset,manifest],CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"read-only inspection still rejects a changed package against its checksum manifest");
        check(PortableProcessControl.GracefulCloseTimeout.TotalSeconds==6&&PortableProcessControl.TerminationTimeout.TotalSeconds==8&&PortableProcessControl.FileReleaseTimeout.TotalSeconds==6,"portable exit and file-release deadlines each gain three seconds");
        using(var lockFile=new FileStream(asset,FileMode.Open,FileAccess.Read,FileShare.Read)){
            var wait=PortableProcessControl.WaitForRelease(asset,CancellationToken.None);await Task.Delay(3300);check(!wait.IsCompleted,"portable replacement keeps waiting beyond the former three-second file lock deadline");lockFile.Dispose();await wait.WaitAsync(TimeSpan.FromSeconds(3));
        }
        using(var lockFile=new FileStream(asset,FileMode.Open,FileAccess.Read,FileShare.Read))using(var canceled=new CancellationTokenSource()){
            var wait=PortableProcessControl.WaitForRelease(asset,canceled.Token);canceled.Cancel();var stopped=false;try{await wait;}catch(OperationCanceledException){stopped=true;}check(stopped,"extended portable close waiting remains cancelable");
        }
    }
}
