using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    internal async Task StartupTraySmoke(string path)
    {
        var visibleTransitions=0;var loadedEvents=0;
        IsVisibleChanged+=(_,_)=>{if(IsVisible)visibleTransitions++;};
        Loaded+=(_,_)=>loadedEvents++;
        tray.Visible=true;
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        await Task.Delay(1500);
        var hidden=!IsVisible&&!ShowInTaskbar&&new WindowInteropHelper(this).Handle==IntPtr.Zero&&loadedEvents==0&&visibleTransitions==0;
        var trayReady=tray.Visible&&tray.ContextMenuStrip?.Items.Count==5;
        releases=[new(901,"example/startup","v1.0.0","Startup fixture","",false,DateTimeOffset.Now,"",[])];
        repositories=[new("example/startup","",false,true)];
        Display();await InitializeSessionAsync();
        var initializationQuiet=!IsVisible&&visibleTransitions==0&&ready;
        await ReclaimBackgroundMemory();
        var backgroundReleased=backgroundViewsReleased&&releases.Count==1;
        var externalLaunch=true;if(Environment.GetCommandLineArgs().Contains("--external-activation")){await SecondaryActivationSmoke(path);externalLaunch=IsVisible&&ShowInTaskbar&&model.Cards.Count==1;}
        Reopen();UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var reopened=IsVisible&&ShowInTaskbar&&WindowState==WindowState.Normal&&model.Cards.Count==1&&ActualWidth>0&&ActualHeight>0;
        Close();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var closeToTray=!IsVisible&&!exit&&tray.Visible;
        Reopen();UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var restored=IsVisible&&model.Cards.Count==1&&loadedEvents==1;
        var report=new{HiddenWithoutNativeWindow=hidden,TrayMenuReady=trayReady,InitializationRemainsHidden=initializationQuiet,BackgroundReclaimBeforeFirstOpen=backgroundReleased,ExternalLaunchFromColdTray=externalLaunch,ReopenShowsCachedPage=reopened,CloseReturnsToTray=closeToTray,ReopenPreservesPage=restored,VisibleTransitions=visibleTransitions};
        File.WriteAllText(Path.ChangeExtension(path,".startup-tray.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        if(!hidden||!trayReady||!initializationQuiet||!backgroundReleased||!externalLaunch||!reopened||!closeToTray||!restored){exit=true;tray.Dispose();api.Dispose();store.Dispose();Environment.ExitCode=1;Application.Current.Shutdown();return;}
        RenderSmoke(path,cards:true,dark:Environment.GetCommandLineArgs().Contains("--dark"));
    }
}
