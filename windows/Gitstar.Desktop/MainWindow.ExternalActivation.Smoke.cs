using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task ExternalActivationSmoke(string path)
    {
        var legacy=Environment.GetCommandLineArgs().Contains("--expect-external-repro");var expected=model.Cards.Count;var handle=new WindowInteropHelper(this).Handle;
        Hide();await ReclaimBackgroundMemory();var released=backgroundViewsReleased&&model.Cards.Count==0;
        ExternalShowWindow(handle,5);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(200);
        var nativeVisible=ExternalIsWindowVisible(handle);var wpfVisible=IsVisible;var recovered=IsVisible&&ShowInTaskbar&&!backgroundViewsReleased&&model.Cards.Count==expected;
        var mismatch=nativeVisible&&!wpfVisible;
        if(legacy){File.WriteAllText(Path.ChangeExtension(path,".external-activation.json"),JsonSerializer.Serialize(new{Legacy=true,ViewsReleased=released,NativeWindowVisible=nativeVisible,WpfVisible=wpfVisible,VisibilityMismatch=mismatch,ViewsRecovered=recovered}));if(!released||!mismatch||recovered)throw new IOException("Expected external activation mismatch was not reproduced");Reopen();return;}
        Hide();var changed=releases[0] with{Tag="v9.9.9"};releases[0]=changed;Display();ExternalShowWindow(handle,5);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var shortHide=IsVisible&&model.Cards.Any(card=>card.Release.Id==changed.Id&&card.Release.Tag==changed.Tag);
        WindowState=WindowState.Minimized;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var staysMinimized=WindowState==WindowState.Minimized;await ReclaimBackgroundMemory();ExternalShowWindow(handle,9);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var minimized=IsVisible&&WindowState!=WindowState.Minimized&&!backgroundViewsReleased&&model.Cards.Count==expected;
        WindowState=WindowState.Maximized;Hide();await ReclaimBackgroundMemory();ExternalSetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0057);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var positioning=IsVisible&&!backgroundViewsReleased&&model.Cards.Count==expected&&WindowState==WindowState.Maximized;
        WindowState=WindowState.Normal;Hide();await ReclaimBackgroundMemory();var launch=await SecondaryActivationSmoke(path);var secondLaunch=IsVisible&&ShowInTaskbar&&!backgroundViewsReleased&&model.Cards.Count==expected;
        File.WriteAllText(Path.ChangeExtension(path,".external-activation.json"),JsonSerializer.Serialize(new{Legacy=false,ViewsReleased=released,NativeWindowVisible=nativeVisible,WpfVisible=wpfVisible,VisibilityMismatch=mismatch,ViewsRecovered=recovered,ShortHideShowsLatestCache=shortHide,MinimizeRemainsMinimized=staysMinimized,NativeRestoreRecoversViews=minimized,SetWindowPosRecoversMaximizedWindow=positioning,SecondProcessRecoversViews=secondLaunch,OwnerProcessId=launch.GetProperty("OwnerProcessId").GetInt32(),ForegroundPermissionDelegated=launch.GetProperty("ForegroundGranted").GetBoolean()}));
        if(!released||!recovered||mismatch||!shortHide||!staysMinimized||!minimized||!positioning||!secondLaunch)throw new IOException("External window activation failed to restore WPF content");
    }
    private async Task<JsonElement> SecondaryActivationSmoke(string path)
    {
        var report=Path.ChangeExtension(path,".activation-launch.json");var before=activationSignalCount;
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--activation-probe");start.ArgumentList.Add(AppActivation.SmokeScope);start.ArgumentList.Add(report);
        using var child=Process.Start(start)??throw new IOException("Cannot start isolated launch probe");using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));await child.WaitForExitAsync(deadline.Token);
        if(child.ExitCode!=0)throw new IOException("Launch probe failed");var result=JsonDocument.Parse(File.ReadAllText(report)).RootElement.Clone();
        for(var attempt=0;attempt<100&&activationSignalCount==before;attempt++)await Task.Delay(20,deadline.Token);
        if(result.GetProperty("IsPrimary").GetBoolean()||result.GetProperty("OwnerProcessId").GetInt32()!=Environment.ProcessId||!result.GetProperty("Signaled").GetBoolean()||activationSignalCount!=before+1)throw new IOException("Second launch did not reach the owning window");return result;
    }
    [DllImport("user32.dll",EntryPoint="ShowWindow")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool ExternalShowWindow(IntPtr handle,int command);
    [DllImport("user32.dll",EntryPoint="IsWindowVisible")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool ExternalIsWindowVisible(IntPtr handle);
    [DllImport("user32.dll",EntryPoint="SetWindowPos")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool ExternalSetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
}
