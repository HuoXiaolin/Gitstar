using System.Net;
namespace Gitstar.Desktop;

public record ReleaseUploadProgress(string Name,long Sent,long Total,TimeSpan Elapsed,bool AwaitingResponse,TimeSpan Idle)
{
    public double Percentage=>Total>0?Math.Clamp(Sent*100d/Total,0,100):0;
    public double BytesPerSecond=>Elapsed.TotalSeconds>0?Sent/Elapsed.TotalSeconds:0;
    public string Label=>AwaitingResponse?"文件已发送 · 等待 GitHub 确认":$"上传中 {Percentage:0}%";
    public string Detail=>$"{Name} · {Sent/1048576d:0.0} / {Total/1048576d:0.0} MB · 平均 {BytesPerSecond/1024d:0.0} KB/s · 已用 {Elapsed.TotalSeconds:0} 秒"+(Idle.TotalSeconds>=10?$" · {Idle.TotalSeconds:0} 秒无新进展":"");
}

public sealed class ProgressUploadContent(Stream source,Action<long> progress):HttpContent
{
    protected override bool TryComputeLength(out long length){length=source.Length;return true;}
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>Copy(stream,CancellationToken.None);
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken ct)=>Copy(stream,ct);
    private async Task Copy(Stream target,CancellationToken ct)
    {
        var buffer=new byte[131072];long sent=0;int count;
        while((count=await source.ReadAsync(buffer,ct).ConfigureAwait(false))>0){await target.WriteAsync(buffer.AsMemory(0,count),ct).ConfigureAwait(false);sent+=count;progress(sent);}
    }
}
