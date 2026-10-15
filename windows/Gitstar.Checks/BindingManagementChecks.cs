using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;

internal static class BindingManagementChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var first=new Binding{AppId="app/a",Repository="example/a",IncludePrerelease=false};var second=new Binding{AppId="app/b",Repository="example/b"};var missing=new Binding{AppId="app/c",Repository="example/c"};
        var apps=new[]{new InstalledApp("app/a","A","1.0","",AliasIds:["legacy/a"]),new InstalledApp("app/b","B","1.0","")};
        var settings=new Settings{Bindings=[first,second,missing],PortableApps=[apps[0]],Notified=["legacy/a/1","app/b/2"]};
        var releases=new[]{new Release(1,"example/a","v2.0","","",false,now,"",[]),new Release(2,"example/a","v3.0-beta.1","","",true,now,"",[]),new Release(3,"example/b","v4.0","","",false,now,"",[])};
        var stopped=BindingManagement.Apply(settings,[first,first],apps,releases,BindingAction.Stop,now);check(stopped.Changed==1&&first.Disabled&&!second.Disabled,"batch actions operate once per selected binding and leave unselected software unchanged");
        BindingManagement.Apply(settings,[first,second],apps,releases,BindingAction.PauseWeek,now);check(first.PauseUntil==now.AddDays(7)&&second.PauseUntil==now.AddDays(7)&&first.Disabled,"batch pause keeps existing disabled state and applies the selected duration");
        var ignored=BindingManagement.Apply(settings,[first,second,missing],apps,releases,BindingAction.IgnoreVersion,now);check(ignored.Changed==2&&ignored.Skipped==1&&first.IgnoredTag=="v2.0"&&second.IgnoredTag=="v4.0","batch ignore selects each software's own channel and skips missing release records");
        BindingManagement.Apply(settings,[first],apps,releases,BindingAction.Restore,now);check(!ReminderRules.Restricted(first,now)&&ReminderRules.Restricted(second,now)&&settings.Notified.SequenceEqual(["app/b/2"]),"selective restore clears only selected restrictions and notification history");
        BindingManagement.Apply(settings,[new Binding{AppId="foreign"}],apps,releases,BindingAction.Unlink,now);check(settings.Bindings.Count==3,"batch operations reject bindings outside the current settings");
        var blocked=false;try{BindingManagement.Update(settings,first,apps[1],"example/new","",true,"","",apps);}catch(IOException){blocked=true;}check(blocked&&first.AppId=="app/a"&&first.Repository=="example/a","conflicting software rematch fails before mutating existing binding");
        blocked=false;try{BindingManagement.Update(settings,first,apps[0],"not a repository","",true,"","",apps);}catch(IOException){blocked=true;}check(blocked&&first.Repository=="example/a","invalid repository does not partially change a match");
        first.Disabled=true;first.PauseUntil=now.AddDays(2);first.IgnoredTag="v2.0";settings.Notified.Add("legacy/a/1");
        BindingManagement.Update(settings,first,apps[0],"https://github.com/example/new","C:\\Apps\\A.exe",true," desktop- "," portable ",apps);
        check(first.Repository=="example/new"&&first.ExecutablePath=="C:\\Apps\\A.exe"&&first.IncludePrerelease&&first.TagPrefix=="desktop-"&&first.AssetPattern=="portable","rematching saves repository executable channel prefix and package preference");
        check(first.Disabled&&first.PauseUntil==now.AddDays(2)&&first.IgnoredTag==""&&!settings.Notified.Contains("legacy/a/1"),"rematching clears stale ignore history while preserving stop and pause choices");
        var root=Path.Combine(Path.GetTempPath(),"Gitstar-binding-management-"+Guid.NewGuid().ToString("N"));
        try{using(var store=new LocalStore(root))store.Set("settings",settings);using(var store=new LocalStore(root)){var saved=store.Get("settings",new Settings());check(saved.Bindings[0].Repository=="example/new"&&saved.Bindings[0].Disabled&&saved.Bindings[1].IgnoredTag=="v4.0","managed software edits and independent restrictions persist after reopening storage");}}finally{Directory.Delete(root,true);}
        var removed=BindingManagement.Apply(settings,[first,second],apps,releases,BindingAction.Unlink,now);check(removed.Changed==2&&settings.Bindings.SequenceEqual([missing])&&settings.PortableApps.Count==1&&settings.Notified.Count==0,"batch unlink removes only selected associations and preserves portable software records");
    }
}
