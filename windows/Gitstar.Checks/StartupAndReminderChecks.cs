using Gitstar.Core;
using Gitstar.Desktop;
using Microsoft.Win32;
using System.IO;

internal static class StartupAndReminderChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var binding=new Binding{AppId="old/editor",Repository="example/editor",Disabled=true,PauseUntil=now.AddDays(2),IgnoredTag="v2.0",IncludePrerelease=true,TagPrefix="desktop-",AssetPattern="portable"};
        var settings=new Settings{Bindings=[binding],Notified=["old/editor/1","new/editor/2","other/app/1","self/9"]};
        check(ReminderRules.Restricted(binding,now),"disabled paused and ignored bindings remain manageable");
        var status=ReminderRules.Status(binding,now);check(status.Contains("停止")&&status.Contains("暂停")&&status.Contains("v2.0"),"reminder manager explains every restriction");
        ReminderRules.Restore(settings,binding,new("new/editor","Editor","1.0","",AliasIds:["old/editor"]));
        check(binding.Enabled(now)&&binding.PauseUntil==null&&binding.IgnoredTag=="","restoring clears disabled pause and ignored release together");
        check(settings.Notified.SequenceEqual(["other/app/1","self/9"]),"restoring clears only selected software notification history including aliases");
        check(binding.Repository=="example/editor"&&binding.IncludePrerelease&&binding.TagPrefix=="desktop-"&&binding.AssetPattern=="portable","restoring preserves repository channel and package settings");
        check(!ReminderRules.Restricted(new(){PauseUntil=now.AddDays(-1)},now),"expired pause is shown as active");
        var missing=new Binding{AppId="not-scanned",Disabled=true};ReminderRules.Restore(settings,missing,null);check(!missing.Disabled,"missing cached software can still restore its binding");
        foreach(var area in new[]{(1920d,1040d),(1366d,728d),(1024d,720d),(768d,576d)}){
            var size=WindowsServices.MainWindowSize(1180,760,area.Item1,area.Item2);
            check(size.Width<=area.Item1*0.9+0.01&&size.Height<=area.Item2*0.86+0.01&&Math.Abs(size.Width/size.Height-1180d/760)<0.001,"default window fits work area without filling height and preserves ratio "+area);
        }
        var large=WindowsServices.MainWindowSize(1180,760,3840,2080);check(large==(1180d,760d),"large monitors keep a moderate default window size");
        var current=@"C:\Apps With Spaces\Gitstar.exe";
        check(WindowsServices.InspectStartupCommand("",current,true).Status.Contains("未找到"),"startup diagnostics identify missing entry");
        check(WindowsServices.InspectStartupCommand("\"C:\\Old\\Gitstar.exe\" --tray",current,true).Status.Contains("其他位置"),"startup diagnostics identify stale executable location");
        check(!WindowsServices.InspectStartupCommand("\""+current+"\"",current,true).Matches,"startup diagnostics reject foreground launch without tray flag");
        check(!WindowsServices.InspectStartupCommand("\""+current+"\" --TRAY",current,true).Matches,"startup diagnostics require the supported case-sensitive tray flag");
        check(!WindowsServices.InspectStartupCommand("\""+current+"\" --tray --other",current,true).Matches,"startup diagnostics reject unexpected launch arguments");
        check(!WindowsServices.InspectStartupCommand(current+" --tray",current,true).Matches,"startup executable paths with spaces must be quoted");
        check(WindowsServices.InspectStartupCommand("  \""+current.ToUpperInvariant()+"\"   --tray  ",current,true).Matches,"startup diagnostics tolerate path casing and outer whitespace");
        check(!WindowsServices.InspectStartupCommand(WindowsServices.StartupCommand(current),current,false).Matches,"missing program blocks successful startup diagnosis");
        var fixtureId=Guid.NewGuid().ToString("N");var folder=Path.Combine(Path.GetTempPath(),"Gitstar-startup-"+fixtureId);var key=@"Software\Gitstar.Tests\Startup-"+fixtureId;
        Directory.CreateDirectory(folder);var executable=Path.Combine(folder,"Gitstar.exe");File.WriteAllText(executable,"fixture only; never executed");
        try{
            using(var fixture=Registry.CurrentUser.CreateSubKey(key))fixture.SetValue("OtherApplication","preserved");
            WindowsServices.SetStartupEntry(Registry.CurrentUser,key,true,executable);
            var repaired=WindowsServices.ReadStartupEntry(Registry.CurrentUser,key,executable);
            check(repaired.Matches&&repaired.Command==WindowsServices.StartupCommand(executable),"startup registry repair roundtrip uses exact program path and tray flag");
            File.Delete(executable);var blocked=false;try{WindowsServices.SetStartupEntry(Registry.CurrentUser,key,true,executable);}catch(IOException){blocked=true;}
            check(blocked,"repair refuses an executable that no longer exists");
            WindowsServices.SetStartupEntry(Registry.CurrentUser,key,false,executable);
            using var result=Registry.CurrentUser.OpenSubKey(key);check(result?.GetValue("Gitstar")==null&&(string?)result?.GetValue("OtherApplication")=="preserved","disabling removes only Gitstar and preserves unrelated registry values");
        }finally{Registry.CurrentUser.DeleteSubKeyTree(key,false);Directory.Delete(folder,true);}
    }
}
