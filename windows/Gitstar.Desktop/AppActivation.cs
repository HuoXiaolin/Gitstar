using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Gitstar.Desktop;

internal record ActivationNotification(int OwnerProcessId,bool ForegroundGranted,bool Signaled);

internal sealed class AppActivation : IDisposable
{
    private readonly Mutex mutex;
    private readonly MemoryMappedFile? owner;
    private readonly string ownerName;
    public EventWaitHandle Signal {get;}
    public bool IsPrimary {get;}
    public static string SmokeScope=>"smoke-"+Environment.ProcessId;

    public AppActivation(string? scope=null)
    {
        var suffix=Environment.UserName+(scope==null?"":"-"+scope);
        // Keep the signal alive before taking ownership, so startup-time requests cannot disappear.
        Signal=new(false,EventResetMode.AutoReset,@"Local\Gitstar-Open-"+suffix);
        mutex=new(true,@"Local\Gitstar-"+suffix,out var first);IsPrimary=first;
        ownerName=@"Local\Gitstar-Owner-"+suffix;
        if(first){owner=MemoryMappedFile.CreateOrOpen(ownerName,sizeof(int));using var view=owner.CreateViewAccessor();view.Write(0,Environment.ProcessId);view.Flush();}
    }
    public ActivationNotification NotifyPrimary()
    {
        var pid=0;var granted=false;
        for(var attempt=0;attempt<20;attempt++){
            try{using var map=MemoryMappedFile.OpenExisting(ownerName,MemoryMappedFileRights.Read);using var view=map.CreateViewAccessor(0,sizeof(int),MemoryMappedFileAccess.Read);pid=view.ReadInt32(0);if(pid>0)break;}
            catch(FileNotFoundException){}catch(UnauthorizedAccessException){break;}catch(IOException){break;}
            Thread.Sleep(10);
        }
        if(pid>0)granted=AllowSetForegroundWindow((uint)pid);
        return new(pid,granted,Signal.Set());
    }
    public void Dispose(){owner?.Dispose();if(IsPrimary)mutex.ReleaseMutex();mutex.Dispose();Signal.Dispose();}
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint processId);
}
