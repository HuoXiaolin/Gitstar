using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;
internal static class SoftwareDiscoveryChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var programs=Path.Combine(root,"user-programs");var app=Path.Combine(programs,"Editor");var nested=Path.Combine(programs,"Client","app-2.0");var ignored=Path.Combine(programs,"Other","cache","deep");
        foreach(var folder in new[]{app,nested,ignored})Directory.CreateDirectory(folder);
        var main=Path.Combine(app,"Editor.exe");File.Copy(Environment.ProcessPath!,main);File.Copy(main,Path.Combine(app,"Update.exe"));File.Copy(main,Path.Combine(nested,"Client.exe"));File.Copy(main,Path.Combine(ignored,"Hidden.exe"));
        var found=WindowsServices.DiscoverUserPrograms([programs]);
        check(found.Count==2&&found.Any(item=>item.ExecutablePath==main),"user program discovery includes AppData-style programs and versioned app folders");
        check(found.All(item=>!item.Portable),"user install locations do not imply portable replacement mode");
        check(found.All(item=>!item.ExecutablePath.Contains("Hidden")&&!item.ExecutablePath.EndsWith("Update.exe")),"discovery excludes helper tools and arbitrary deep cache trees");
        check(WindowsServices.DiscoverUserPrograms([programs],[main,main.ToUpperInvariant()]).Count==2,"duplicate shortcut and directory evidence shares one physical identity");
        var external=Path.Combine(root,"shortcut-target.exe");File.Copy(main,external);
        check(WindowsServices.DiscoverUserPrograms([], [external]).Single().ExecutablePath==external,"shortcut evidence can discover applications outside standard program folders");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();var canceled=false;try{WindowsServices.DiscoverUserPrograms([programs],ct:cancellation.Token);}catch(OperationCanceledException){canceled=true;}check(canceled,"user program discovery respects audit cancellation");
        Environment.SetEnvironmentVariable("GITSTAR_DISCOVERY_FIXTURE",app);try{check(WindowsServices.PortableExecutables("%GITSTAR_DISCOVERY_FIXTURE%").Single()==main,"manual program selection expands environment variables in folder paths");check(WindowsServices.DescribePortable("%GITSTAR_DISCOVERY_FIXTURE%\\Editor.exe").ExecutablePath==main,"manual EXE selection expands AppData-style environment paths");}finally{Environment.SetEnvironmentVariable("GITSTAR_DISCOVERY_FIXTURE",null);}
        var settings=new Settings();var original=WindowsServices.DescribePortable(main,"Editor","1.0");ManualSoftware.Save(settings,original,[],"owner/editor",false,"","");
        ManualSoftware.Save(settings,original,[original],"owner/editor",false,"","",portableOverride:false);check(settings.Bindings.Count==1&&settings.PortableApps.Count==0,"changing update method preserves one binding and removes obsolete portable inventory");
        var registered=original with{Id="registered/editor",Portable=false};
        check(!WindowsServices.ScanInventory([new(registered)],[],[registered],[],false).Single().Portable,"discovered installed app does not override registered update mode");
        check(WindowsServices.ScanInventory([new(registered)],[],[registered with{Portable=true}],[],false).Single().Portable,"explicit portable update choice survives subsequent registry scans");
        check(WindowsServices.ScanInventory([new(registered)],[],[registered with{Portable=true},registered with{Id="discovered/editor"}],[],false).Single().Portable,"duplicate discovered path cannot erase an explicit portable update choice");
    }
}
