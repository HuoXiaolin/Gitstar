using System.Diagnostics;
using System.ComponentModel;
namespace Gitstar.Desktop;

internal static class PortableProcessControl
{
    internal static HashSet<string> Paths(PortableReplacementPlan plan)=>plan.Files.Select(file=>Path.GetFullPath(file.Target)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    internal static List<Process> Find(IReadOnlySet<string> paths)
    {
        var found=new List<Process>();foreach(var process in Process.GetProcesses())try{if(process.MainModule?.FileName is {} path&&paths.Contains(Path.GetFullPath(path))){found.Add(process);continue;}process.Dispose();}catch{process.Dispose();}return found;
    }
    internal static async Task Stop(Process process,IReadOnlySet<string> paths,bool terminate,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(process.Id==Environment.ProcessId)throw new IOException("当前 Gitstar 不能在运行中替换自身，请使用安装包或退出后替换。");
        try{
            if(process.HasExited)return;var started=process.StartTime;
            if(process.MainModule?.FileName is not {} path||!paths.Contains(Path.GetFullPath(path)))throw new IOException("运行中的程序位置已变化，未关闭其他程序。");
            var requested=process.CloseMainWindow();
            if(requested){using var graceful=CancellationTokenSource.CreateLinkedTokenSource(ct);graceful.CancelAfter(TimeSpan.FromSeconds(3));try{await process.WaitForExitAsync(graceful.Token);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){}}
            ct.ThrowIfCancellationRequested();if(process.HasExited){await WaitForRelease(path,ct);return;}
            if(!terminate)throw new IOException("软件仍在运行，请从窗口或托盘彻底退出后重试。");
            using var current=Process.GetProcessById(process.Id);
            if(current.StartTime!=started||current.MainModule?.FileName is not {} actual||!paths.Contains(Path.GetFullPath(actual)))throw new IOException("进程标识或程序位置已变化，已停止自动关闭。");
            current.Kill(entireProcessTree:false);
            using var stopping=CancellationTokenSource.CreateLinkedTokenSource(ct);stopping.CancelAfter(TimeSpan.FromSeconds(5));try{await current.WaitForExitAsync(stopping.Token);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("目标软件尚未退出，文件未替换。");}
            await WaitForRelease(path,ct);
        }catch(ArgumentException){if(!process.HasExited)throw;}
        catch(Win32Exception error){throw new IOException("无法关闭目标软件，请核对权限或手动退出后重试："+error.Message,error);}
    }
    private static async Task WaitForRelease(string path,CancellationToken ct)
    {
        var timer=Stopwatch.StartNew();while(true){ct.ThrowIfCancellationRequested();try{using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);return;}
            catch(IOException error)when((error.HResult&0xffff) is 32 or 33){if(timer.Elapsed>TimeSpan.FromSeconds(3))throw new IOException("软件已退出，但程序文件仍被占用，请稍后重试。未替换文件。",error);await Task.Delay(100,ct);}}
    }
    internal static int? Start(string program,IReadOnlyList<string>? arguments=null,bool headless=false)
    {
        program=Path.GetFullPath(program);if(!File.Exists(program)||!program.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("更新后的主程序不存在，未启动软件。");
        try{var start=new ProcessStartInfo(program){UseShellExecute=!headless,CreateNoWindow=headless,WorkingDirectory=Path.GetDirectoryName(program)!};foreach(var argument in arguments??[])start.ArgumentList.Add(argument);using var process=Process.Start(start);return process?.Id;}
        catch(Win32Exception error){throw new IOException("文件已更新，但启动失败，请手动打开原主程序："+error.Message,error);}
    }
}
