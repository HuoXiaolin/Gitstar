using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed class BulkSmokeTransport(byte[] bytes):HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
    }
    private async Task BulkTransferSmoke(string path)
    {
        var bytes=Encoding.UTF8.GetBytes("bulk-download-fixture");var digest="sha256:"+Convert.ToHexString(SHA256.HashData(bytes));api.Dispose();var transport=new BulkSmokeTransport(bytes);api=new(settings,transport);settings.KeepLatestPackages=false;
        var root=Path.Combine(store.Root,"batch-downloads");var now=DateTimeOffset.UtcNow;
        var items=new[]{1,2}.Select(i=>{var asset=new Asset(i,$"Setup-v1.0.{i}.exe",bytes.Length,"https://api.github.com/repos/example/editor/releases/assets/"+i,digest);return new ReleaseDownload(new(i,"example/editor","v1.0."+i,"","",false,now,"",[asset]),asset);}).ToArray();
        await DownloadBatch(items,root,2);var saved=items.All(i=>File.ReadAllBytes(PackageTarget(i.Release,i.Asset,root)).SequenceEqual(bytes))&&Directory.GetFiles(root).Length==2&&!Directory.EnumerateDirectories(root).Any();
        var requests=transport.Requests;await DownloadBatch(items,root,2);var skipped=transport.Requests==requests&&operations.Last(o=>o.Kind=="批量下载").Results.All(r=>r.StartsWith("跳过"));
        var blockedRoot=Path.Combine(store.Root,"blocked-downloads");var blocked=PackageTarget(items[0].Release,items[0].Asset,blockedRoot);
        using(var holder=taskCoordinator.Start("bulk-smoke-holder",["file:"+blocked])){
            await holder.WaitAsync();var batch=DownloadBatch([items[0]],blockedRoot,2);await Task.Delay(20);var parent=operations.Last(o=>o.Kind=="批量下载");taskCoordinator.Cancel(parent.Id);
            if(await Task.WhenAny(batch,Task.Delay(2000))!=batch)throw new IOException("Queued bulk child did not stop with its parent");await batch;
            var cancelled=parent.State=="已取消"&&operations.Last(o=>o.Kind=="下载").State=="已取消等待"&&!holder.Token.IsCancellationRequested&&!File.Exists(blocked);
            if(!saved||!skipped||!cancelled)throw new IOException("Bulk transfer regression");
            File.WriteAllText(Path.ChangeExtension(path,".bulk-transfer.json"),JsonSerializer.Serialize(new{FilesSavedAndVerified=saved,FlatDirectory=true,ExistingSameVersionSkipped=skipped,ParentCancelStopsQueuedChild=cancelled,IndependentTaskPreserved=!holder.Token.IsCancellationRequested}));
        }
    }
}
