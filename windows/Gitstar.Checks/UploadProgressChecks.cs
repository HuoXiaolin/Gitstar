using Gitstar.Desktop;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

internal static class UploadProgressChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var bytes=new byte[400000];Random.Shared.NextBytes(bytes);var counts=new List<long>();using(var source=new MemoryStream(bytes))using(var content=new ProgressUploadContent(source,counts.Add))using(var output=new MemoryStream()){
            await content.CopyToAsync(output);check(output.ToArray().SequenceEqual(bytes)&&counts.Count>1&&counts.Last()==bytes.Length&&counts.SequenceEqual(counts.Order()),"upload streams original content in chunks with monotonic actual-write counts");
            check(content.Headers.ContentLength==bytes.Length,"streaming upload keeps Content-Length for GitHub without chunked framing");
        }
        using(var stop=new CancellationTokenSource())using(var source=new MemoryStream(bytes))using(var content=new ProgressUploadContent(source,_=>stop.Cancel()))using(var output=new MemoryStream()){
            var cancelled=false;try{await content.CopyToAsync(output,stop.Token);}catch(OperationCanceledException){cancelled=true;}check(cancelled&&output.Length>0&&output.Length<bytes.Length,"upload cancellation stops streaming between chunks instead of sending the entire file");
        }
        var waiting=new ReleaseUploadProgress("app.exe",100,100,TimeSpan.FromSeconds(2),true,TimeSpan.FromSeconds(1));check(waiting.Percentage==100&&waiting.Label.Contains("等待 GitHub")&&!waiting.Label.Contains("完成"),"100 percent sent is shown as awaiting server confirmation rather than upload complete");
        check(new ReleaseUploadProgress("app.exe",1024,2048,TimeSpan.FromSeconds(2),false,TimeSpan.FromSeconds(12)).BytesPerSecond==512&&new ReleaseUploadProgress("app.exe",1,2,TimeSpan.FromSeconds(20),false,TimeSpan.FromSeconds(12)).Detail.Contains("无新进展"),"upload progress reports measured average speed and visible stall duration");
        var path=Path.Combine(root,"upload-fixture.exe");await File.WriteAllBytesAsync(path,bytes);var reports=new List<ReleaseUploadProgress>();var calls=0;
        using(var handler=new FakeHttp(async(request,ct)=>{
            calls++;var payload=await request.Content!.ReadAsByteArrayAsync(ct);await Task.Delay(1100,ct);
            return new HttpResponseMessage(HttpStatusCode.Created){Content=new StringContent(JsonSerializer.Serialize(new{id=7,name="app.exe",size=payload.Length,state="uploaded",digest="sha256:"+Convert.ToHexString(SHA256.HashData(payload))}))};
        }))using(var api=new GitHubClient(new(),handler)){
            var asset=await api.UploadAutomationAsset("owner/repo",1,path,"app.exe",CancellationToken.None,reports.Add);
            check(calls==1&&asset.GetProperty("size").GetInt64()==bytes.Length&&reports.First().Sent==0&&reports.Last().Sent==bytes.Length,"upload endpoint streams once and reports initial and final measured byte counts");
            check(reports.Any(item=>item.AwaitingResponse)&&reports.All(item=>item.Sent<=item.Total),"upload remains in server confirmation phase while response is delayed");
        }
        using(var stop=new CancellationTokenSource())using(var handler=new FakeHttp(async(request,ct)=>{await request.Content!.ReadAsByteArrayAsync(ct);stop.Cancel();await Task.Delay(Timeout.Infinite,ct);return new(HttpStatusCode.OK);}))using(var api=new GitHubClient(new(),handler)){
            var cancelled=false;try{await api.UploadAutomationAsset("owner/repo",1,path,"app.exe",stop.Token);}catch(OperationCanceledException){cancelled=true;}check(cancelled,"user cancellation remains cancellation rather than being mislabeled as a slow-network timeout");
        }
    }
}
