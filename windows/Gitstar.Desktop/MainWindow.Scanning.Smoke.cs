using System.Net;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Func<CancellationToken,Task<List<InstalledApp>>>? matchedScanSmoke;
    private async Task ScanningSmoke(string path)
    {
        var transport=new ScanningSmokeTransport();api.Dispose();api=new(settings,transport){Token="fixture"};settings.Account="example-user";AccountLabel();
        settings.ExtraRepositories=["example/fast","example/slow"];settings.Bindings=[new(){AppId="fixture-fast",Repository="example/fast"}];settings.Notified.Add("fixture-fast/2");
        repositories=[];releases=[];installed=[new("fixture-fast","Fast software","1.0.0","")];installedFresh=false;pendingInstallations.Clear();
        var localGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var localCalls=0;
        matchedScanSmoke=async ct=>{localCalls++;await localGate.Task.WaitAsync(ct);return installed.ToList();};
        async Task Until(Func<bool> done){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!done())await Task.Delay(20,timeout.Token);}
        Page(false);var work=RefreshAsync(true);await Until(()=>model.Cards.Any(card=>card.Repository=="example/fast"));
        var streamedBeforeLocal=!installedFresh&&!work.IsCompleted&&!transport.Stars.Task.IsCompleted&&!transport.Groups.Task.IsCompleted;
        Page(true);localGate.TrySetResult();await Until(()=>installedFresh&&model.Cards.Any(card=>card.Repository=="example/fast"));
        var localBeforeSlow=installedFresh&&!work.IsCompleted;
        transport.Stars.TrySetResult();transport.Groups.TrySetResult();await Until(()=>releases.Any(release=>release.Repository=="example/new"));
        var newWhileOldPending=!work.IsCompleted;var firstStarPageReady=!transport.StarsTail.Task.IsCompleted;
        transport.StarsTail.TrySetResult();transport.Slow.TrySetResult();await work;
        var success=operations.Last().State=="完成"&&localCalls==1&&transport.FastPages==1&&transport.FastLatest==1;
        // Cached repositories and arriving Star pages share 24 workers, including when reads are held.
        repositories=repositories.Where(repo=>repo.Starred).Take(12).ToList();transport.HoldPages=true;
        var paused=RefreshAsync(true);await Until(()=>transport.HeldPages==24&&model.Status.Contains("24 个并行"));
        var boundedConcurrency=transport.HeldPages==24&&model.Status.Contains("24 个并行");
        taskCoordinator.Pause(automaticCheckId!);await paused.WaitAsync(TimeSpan.FromSeconds(4));var canceled=!refreshing&&transport.HeldPages==0&&operations.Last().State.StartsWith("已暂停");transport.HoldPages=false;
        repositories=repositories.Where(repo=>repo.Starred).Take(12).Append(new("example/previous","",false,true,"")).ToList();
        transport.HoldRepository="follow90";transport.StarTailFailure=true;await RefreshAsync(true).WaitAsync(TimeSpan.FromSeconds(4));
        var listingFailureSettled=transport.HeldRepositoryEntered&&transport.HeldPages==0&&operations.Last().State.StartsWith("失败")&&operations.Last().State.Contains("429")&&repositories.Any(repo=>repo.FullName=="example/previous"&&repo.Starred);
        transport.HoldRepository="";transport.StarTailFailure=false;
        var oldGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var original=installed.Single();var scanNumber=0;
        matchedScanSmoke=async ct=>{var number=++scanNumber;if(number==1)await oldGate.Task.WaitAsync(ct);return [original with{Version=number==1?"1.0.0":"3.0.0"}];};
        var stale=ScanMatchedAsync();await ScanMatchedAsync(afterChange:true);oldGate.TrySetResult();await stale;var oldResultRejected=installed.Single().Version=="3.0.0";
        myProjects=[new("example/cached","cached",false,"main",true,true,false,DateTimeOffset.UtcNow)];projectVersions["example/cached"]=[new(1,"v1","v1","",false,false,0,DateTimeOffset.UtcNow,"")];Projects_Click(this,new());
        var projects=SyncProjects();await Until(()=>myProjects.Count==101);var projectStreaming=!projects.IsCompleted&&projectVersions.ContainsKey("example/cached");
        var taskCount=operations.Count;await SyncProjects();var singleProjectSync=operations.Count==taskCount;transport.ProjectTail.TrySetResult();await projects;
        var projectComplete=myProjects.Count==101&&!myProjects.Any(project=>project.FullName=="example/cached");
        if(!streamedBeforeLocal||!localBeforeSlow||!newWhileOldPending||!firstStarPageReady||!boundedConcurrency||!listingFailureSettled||!success||!canceled||!projectStreaming||!singleProjectSync||!projectComplete||!oldResultRejected)throw new IOException("Progressive scan pipeline regression "+JsonSerializer.Serialize(new{streamedBeforeLocal,localBeforeSlow,newWhileOldPending,firstStarPageReady,boundedConcurrency,listingFailureSettled,success,canceled,projectStreaming,singleProjectSync,projectComplete,oldResultRejected,State=operations.Last().State}));
        matchedScanSmoke=null;Page(false);model.Status="分批检查验证完成，快项目无需等待慢项目、本机扫描或分组同步。";
        File.WriteAllText(Path.ChangeExtension(path,".scanning.json"),JsonSerializer.Serialize(new{StreamedBeforeLocal=streamedBeforeLocal,LocalBeforeSlowRepository=localBeforeSlow,NewReposWhileOldPending=newWhileOldPending,FirstStarPageCheckedBeforeTail=firstStarPageReady,Shared24RepositoryWorkers=boundedConcurrency,ListingFailureCancelsPagesAndPreservesFollows=listingFailureSettled,SingleScan=success,CancellationSettled=canceled,ProjectPagesStreamed=projectStreaming,ProjectDuplicateClickShared=singleProjectSync,ProjectRemovalAfterComplete=projectComplete,OldResultRejectedAfterInstallation=oldResultRejected,transport.FastPages,transport.FastLatest}));
        UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
    private sealed class ScanningSmokeTransport:HttpMessageHandler
    {
        public readonly TaskCompletionSource Stars=new(TaskCreationOptions.RunContinuationsAsynchronously),Groups=new(TaskCreationOptions.RunContinuationsAsynchronously),Slow=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ProjectTail=new(TaskCreationOptions.RunContinuationsAsynchronously),StarsTail=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource pageGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldPages,StarTailFailure,HeldRepositoryEntered;public string HoldRepository="";public int FastPages,FastLatest,HeldPages;
        private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
        private static object Repo(string name)=>new{full_name=name,@private=false,description="扫描示例项目"};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path=="/user/repos"){var first=request.RequestUri.Query.EndsWith("page=1");if(!first)await ProjectTail.Task.WaitAsync(ct);return Json(Enumerable.Range(first?0:100,first?100:1).Select(i=>new{full_name="example/project"+i,@private=false,default_branch="main",updated_at="2026-11-13T00:00:00Z",permissions=new{push=true,admin=true}}).ToArray());}
            if(path=="/user/starred"){
                await Stars.Task.WaitAsync(ct);if(!request.RequestUri.Query.EndsWith("page=1")){await StarsTail.Task.WaitAsync(ct);if(StarTailFailure)return new((HttpStatusCode)429){Content=new StringContent("rate limit")};return Json(new[]{Repo("example/tail")});}
                return Json(new[]{Repo("example/fast"),Repo("example/new")}.Concat(Enumerable.Range(0,98).Select(i=>Repo("example/follow"+i))).ToArray());
            }
            if(path=="/graphql"){await Groups.Task.WaitAsync(ct);return Json(new{data=new{viewer=new{lists=new{nodes=Array.Empty<object>(),pageInfo=new{hasNextPage=false}}}}});}
            if(path.Contains("/Gitstar/"))return new(HttpStatusCode.NotFound);
            var name=path.Split('/')[3];
            if(name=="slow")await Slow.Task.WaitAsync(ct);
            if(!path.Contains("/releases"))return Json(Repo("example/"+name));
            var latest=path.EndsWith("/latest");if(name=="fast"){if(latest)FastLatest++;else FastPages++;}
            if((HoldPages||HoldRepository==name)&&!latest){HeldPages++;if(HoldRepository==name)HeldRepositoryEntered=true;try{await pageGate.Task.WaitAsync(ct);}finally{HeldPages--;}}
            var release=new{id=name=="fast"?2L:name=="new"?3L:4L,tag_name="v2.0.0",name="示例新版",body="此项目已经完成检查，可以继续操作。",draft=false,prerelease=false,published_at="2026-11-13T00:00:00Z",html_url="",assets=Array.Empty<object>()};
            return latest?Json(release):Json(new[]{release});
        }
    }
}
