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
        var verificationBytes=Enumerable.Range(0,12).Select(i=>(byte)i).ToArray();var verificationSha=Convert.ToHexString(SHA256.HashData(verificationBytes));var verifiedCounts=new List<long>();
        using(var slow=new VerificationStream(verificationBytes,TimeSpan.FromMilliseconds(30))){
            var started=System.Diagnostics.Stopwatch.StartNew();var valid=await GitHubClient.VerifyAutomationAssetStream(slow,verificationBytes.Length,verificationSha,CancellationToken.None,verifiedCounts.Add,TimeSpan.FromMilliseconds(180));
            check(valid&&started.Elapsed>TimeSpan.FromMilliseconds(180)&&verifiedCounts.Last()==verificationBytes.Length&&verifiedCounts.SequenceEqual(verifiedCounts.Order()),"remote verification resets idle deadline on each received chunk and reports real bytes even when total duration exceeds deadline");
        }
        using(var silent=new VerificationStream(verificationBytes,Timeout.InfiniteTimeSpan)){
            var timedOut=false;try{await GitHubClient.VerifyAutomationAssetStream(silent,verificationBytes.Length,verificationSha,CancellationToken.None,idleTimeout:TimeSpan.FromMilliseconds(80));}catch(IOException error){timedOut=error.Message.Contains("核验未完成");}
            check(timedOut,"remote asset verification times out when server stops delivering body data after successful headers");
        }
        using(var cancelled=new CancellationTokenSource(TimeSpan.FromMilliseconds(30)))using(var silent=new VerificationStream(verificationBytes,Timeout.InfiniteTimeSpan)){
            var stopped=false;try{await GitHubClient.VerifyAutomationAssetStream(silent,verificationBytes.Length,verificationSha,cancelled.Token,idleTimeout:TimeSpan.FromSeconds(2));}catch(OperationCanceledException){stopped=true;}
            check(stopped,"user cancellation of remote verification stays cancellation rather than becoming a network timeout");
        }
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
    private sealed class VerificationStream(byte[] bytes,TimeSpan delay):Stream
    {
        private int position;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){if(position==bytes.Length)return 0;await Task.Delay(delay,ct);buffer.Span[0]=bytes[position++];return 1;}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>bytes.Length;public override long Position{get=>position;set=>throw new NotSupportedException();}
        public override void Flush()=>throw new NotSupportedException();public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
