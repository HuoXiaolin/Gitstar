using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;
using System.Runtime.InteropServices;

internal static class SoftwareEntryChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        Asset A(long id,string name)=>new(id,name,100,"",null);
        var files=new[]{A(1,"app-arm64.delta"),A(2,"app-arm64.exe"),A(3,"app-x86.exe"),A(4,"app-x64.msi"),A(5,"app-x64.exe"),A(6,"app-linux-x64.zip")};
        check(PackagePreference.Select(files,Architecture.X64)?.Id==5,"save package prefers native x64 EXE instead of the first delta or ARM file");
        check(PackagePreference.Select(files,Architecture.X86)?.Id==3,"32-bit host defaults to x86 package");
        check(PackagePreference.Select(files,Architecture.Arm64)?.Id==2,"ARM64 host defaults to native ARM64 package");
        check(PackagePreference.Select([A(1,"app-x86.exe"),A(2,"app.exe")],Architecture.X64)?.Id==2,"unspecified architecture installer ranks above explicit compatibility fallback");
        check(PackagePreference.Select([A(1,"app-x86.exe")],Architecture.X64)?.Id==1,"x64 host may fall back to x86 installer");
        check(PackagePreference.Select([A(1,"app-x86_64.zip"),A(2,"app-x86.exe")],Architecture.X64)?.Id==1,"native x86_64 archive ranks above x86 compatibility package");
        check(PackagePreference.Select([A(1,"app-x64.exe"),A(2,"app-x64.msi")],Architecture.X64,".msi")?.Id==2,"saved filename preference remains honored for compatible files");
        check(PackagePreference.Select([A(1,"app-arm64.exe"),A(2,"app-linux-x64.zip"),A(3,"app-source.zip"),A(4,"app-x64.delta")],Architecture.X64)==null,"incompatible or non-install files are not automatically recommended");
        check(PackagePreference.Select([A(1,"app-x64.exe") with{Size=0}],Architecture.X64)==null,"empty attachment is not recommended");
        check(PackagePreference.Select([A(1,"app-setup-x64.exe"),A(2,"app-x64.zip"),A(3,"app-x64.msi")],Architecture.X64,portable:true)?.Id==2,"portable update recommends archive instead of installer");
        check(PackagePreference.Select([A(1,"app-x64.exe")],Architecture.X64,portable:true)?.Id==1,"portable main EXE remains available for file replacement");
        check(PackagePreference.Select([A(1,"app-setup-x64.exe"),A(2,"app-x64.msi")],Architecture.X64,portable:true)==null,"portable update never automatically recommends an installer");
        var portableApp=new InstalledApp("portable/fixture","Editor","1.0",root,true);
        check(PortableUpdateRules.ReplaceFiles(portableApp,"Editor.exe")&&PortableUpdateRules.ReplaceFiles(portableApp,"Editor.msi"),"portable packages are routed to file guidance without launching an installer");
        check(!PortableUpdateRules.ReplaceFiles(portableApp with{Portable=false},"Editor.exe")&&PortableUpdateRules.ReplaceFiles(portableApp with{Portable=false},"Editor.zip"),"registered EXE retains installation flow while ZIP uses file replacement");
        check(PortableUpdateRules.Instructions("Editor.exe").Contains("替换原主程序")&&PortableUpdateRules.Instructions("Editor.zip",true).Contains("保留配置和个人数据"),"portable update explains EXE replacement and ZIP configuration preservation");
        check(PortableUpdateRules.Instructions("Editor-setup.exe").Contains("不会启动安装器")&&PortableUpdateRules.Instructions("Editor.msi").Contains("不适合直接替换"),"accidentally chosen installer receives a portable-specific explanation");
        var portableRelease=new Release(1,"example/editor","2.0","","",false,DateTimeOffset.UtcNow,"",[]);
        check(new Card{Release=portableRelease,Update=new(portableApp,new Binding{AppId=portableApp.Id,Repository=portableRelease.Repository},portableRelease,null)}.Action=="下载便携新版","portable update card describes download instead of installation");
        var directory=Path.Combine(root,"manual-entry");Directory.CreateDirectory(directory);var executable=Path.Combine(directory,"Editor.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Gitstar.Checks.exe"),executable);
        var detected=WindowsServices.DescribePortable(executable);
        check(WindowsServices.PortableExecutables(directory).SequenceEqual([executable]),"selecting software folder locates its main program");
        check(detected.Name.Length>0&&detected.Location==directory&&detected.ExecutablePath==executable&&Rules.CompareVersions(detected.Version,detected.Version)!=null,"selecting EXE detects product name version and directory");
        var settings=new Settings();var saved=ManualSoftware.Save(settings,detected,[],"https://github.com/example/editor",false,"","");
        check(settings.PortableApps.Count==1&&settings.Bindings.Single().Repository=="example/editor"&&settings.Bindings[0].ExecutablePath==executable,"EXE and repository are sufficient to persist portable app and binding without network");
        var binding=settings.Bindings[0];binding.Disabled=true;binding.IgnoredTag="v9";binding.PauseUntil=DateTimeOffset.UtcNow.AddDays(3);
        var again=ManualSoftware.Save(settings,detected with{Id="portable/another-id"},[saved],"example/editor",true,"","");
        check(settings.PortableApps.Count==1&&settings.Bindings.Count==1&&again.Id==saved.Id,"re-adding the same EXE updates its existing record instead of duplicating it");
        check(binding.Disabled&&binding.IgnoredTag=="v9"&&binding.PauseUntil>DateTimeOffset.UtcNow&&binding.IncludePrerelease,"updating a binding preserves pause ignore and disabled state");
        var invalid=false;try{ManualSoftware.Save(settings,detected,[],"invalid repo",false,"","");}catch(IOException){invalid=true;}
        check(invalid&&settings.PortableApps.Count==1&&settings.Bindings.Count==1,"invalid repository is rejected before creating any orphan software record");
        var registered=new InstalledApp("registered/editor","Editor","1.0",directory,ExecutablePath:executable,AliasIds:["registered/alias"]);var registeredSettings=new Settings{Bindings=[new(){AppId="registered/alias",Repository="example/editor",Disabled=true}]};
        var reused=ManualSoftware.Save(registeredSettings,detected,[registered],"example/editor",false,"","");
        check(!reused.Portable&&reused.Id==registered.Id&&registeredSettings.PortableApps.Count==0&&registeredSettings.Bindings.Count==1,"choosing an already installed EXE reuses its existing installation and alias binding");
        var unknown=Path.Combine(directory,"NoVersion.exe");File.WriteAllText(unknown,"fixture without version resource");var unversioned=WindowsServices.DescribePortable(unknown);var other=new Settings();ManualSoftware.Save(other,unversioned,[],"example/unversioned",false,"","");
        check(unversioned.Version=="未知"&&other.Bindings.Count==1,"unversioned EXE may be added without requiring a fabricated version");
        check(WindowsServices.DescribePortable(unknown,version:"1.2.3").Version=="1.2.3","optional manual version works when program metadata is absent");
        var helper=Path.Combine(directory,"Setup.exe");File.Copy(executable,helper);var refused=false;try{WindowsServices.DescribePortable(helper);}catch(IOException){refused=true;}check(refused,"installer helpers cannot be mistaken for the main application");
        check(!WindowsServices.PortableExecutables(directory).Contains(helper),"folder detection excludes installer and uninstaller helpers");
        using(var store=new LocalStore(Path.Combine(directory,"store"))){store.Set("settings",settings);var restored=store.Get("settings",new Settings());check(restored.PortableApps.Single().ExecutablePath==executable&&restored.Bindings.Single().ExecutablePath==executable,"portable program path survives saved configuration roundtrip");}
        var moved=detected with{Id=saved.Id,ExecutablePath=unknown,Location=Path.GetDirectoryName(unknown)!};ManualSoftware.Save(settings,moved,[saved],"example/editor",false,"","");
        check(settings.PortableApps.Count==1&&settings.Bindings.Count==1&&settings.PortableApps[0].ExecutablePath==unknown&&settings.Bindings[0].AppId==saved.Id&&settings.Bindings[0].ExecutablePath==unknown,"changing an existing portable program location preserves identity and updates both stored paths");
    }
}
