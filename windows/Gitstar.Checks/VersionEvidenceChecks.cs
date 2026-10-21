using System.IO;
using Gitstar.Core;
using Gitstar.Desktop;

internal static class VersionEvidenceChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var folder=Path.Combine(root,"version-evidence");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"ExampleEditor.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Gitstar.Checks.exe"),path);
        var program=new ProgramVersion(path,"ExampleEditor","ExampleEditor","1.0.0","1.0.0");
        var evidence=InstalledVersion.Resolve("ExampleEditor","9.0.0",[program],path,true);
        check(evidence.Version=="1.0.0"&&evidence.Details.Contains("9.0.0"),"registered and actual file versions remain distinct instead of taking the largest value");
        evidence=InstalledVersion.Resolve("ExampleEditor","1.0",[program with{ApplicationVersion="2.0.0",ApplicationVersionSource="fixture application manifest"}],path,true);
        check(evidence.Version=="2.0.0"&&evidence.Source.Contains("manifest")&&evidence.Details.Contains("程序产品版本：1.0.0"),"application declarations override obsolete file attributes while retaining every observation");
        evidence=InstalledVersion.Resolve("ExampleEditor","1.0",[program with{FileVersion="8.0.0"}],path,true);
        check(!evidence.NeedsReview&&evidence.Version=="1.0.0"&&evidence.Details.Contains("8.0.0"),"a distinct Windows file build number does not displace the product version or block a valid comparison");
        check(!InstalledVersion.Resolve("ExampleEditor","1.0",[program with{FileVersion="1.0.0.0"}],path,true).NeedsReview,"three-part and equivalent Windows four-part versions do not create a false conflict");
        var json=Path.Combine(folder,"version.json");File.WriteAllText(json,"{\"version\":\"2.3.4\"}");
        check(WindowsServices.ReadApplicationVersion(program).ApplicationVersion=="2.3.4","generic local version.json is read without any application-specific rule");
        File.Delete(json);var resource=Path.Combine(folder,"resources","app");Directory.CreateDirectory(resource);var package=Path.Combine(resource,"package.json");
        File.WriteAllText(package,"{\"name\":\"example-editor\",\"version\":\"3.0.0\"}");
        check(WindowsServices.ReadApplicationVersion(program).ApplicationVersion=="3.0.0","identity-matched application package metadata is supported");
        File.WriteAllText(package,"{\"name\":\"unrelated-dependency\",\"version\":\"99.0.0\"}");
        check(WindowsServices.ReadApplicationVersion(program).ApplicationVersion.Length==0,"an unrelated package version cannot become the application version");
        File.Delete(package);var text=Path.Combine(folder,"VERSION");File.WriteAllText(text,"v4.5.6\n");
        check(WindowsServices.ReadApplicationVersion(program).ApplicationVersion=="4.5.6","single-line application VERSION files are accepted");
        File.WriteAllText(text,"version 4.5.6\nother data 99.0");
        check(WindowsServices.ReadApplicationVersion(program).ApplicationVersion.Length==0,"arbitrary multiline text is not searched for a convenient version");
        File.Delete(text);File.WriteAllText(json,"{invalid");
        check(WindowsServices.ReadApplicationVersion(program).ApplicationVersion.Length==0,"corrupt metadata falls back without breaking the inventory scan");
        File.WriteAllText(json,new string(' ',65537)+"{\"version\":\"7.0\"}");
        check(WindowsServices.ReadApplicationVersion(program).ApplicationVersion.Length==0,"oversized metadata is not loaded");
        File.WriteAllText(json,"{\"version\":\"9.0.0\"}");File.WriteAllText(text,"2.0.0");
        evidence=InstalledVersion.Resolve("ExampleEditor","1.0",[WindowsServices.ReadApplicationVersion(program)],path,true);
        check(evidence.Version=="未知"&&evidence.NeedsReview&&evidence.Details.Contains("9.0.0")&&evidence.Details.Contains("2.0.0"),"conflicting application declarations remain unresolved instead of selecting the first or largest");
        File.Delete(json);File.Delete(text);
        var confirmation=WindowsServices.ConfirmInstalledVersion("5.6.7",path);
        var binding=new Binding{AppId="example",Repository="example/editor",ExecutablePath=path,ConfirmedVersion=confirmation.Version,ConfirmedVersionPath=confirmation.Path,ConfirmedVersionHash=confirmation.Hash};
        var app=new InstalledApp("example","ExampleEditor","1.0.0",folder,ExecutablePath:path){VersionDetails="程序产品版本：1.0.0"};
        var resolved=WindowsServices.ApplyConfirmedVersion(app,[binding]);
        check(resolved.Version=="5.6.7"&&resolved.VersionSource.Contains("用户确认")&&!resolved.VersionNeedsReview,"user-confirmed about-page version is explicitly labelled and bound to the current executable");
        check(binding.Snapshot().ConfirmedVersionHash==binding.ConfirmedVersionHash,"confirmation survives binding snapshots used by background scans");
        var timestamp=File.GetLastWriteTimeUtc(path);var bytes=File.ReadAllBytes(path);var changed=bytes.ToArray();changed[changed.Length-1]^=1;File.WriteAllBytes(path,changed);File.SetLastWriteTimeUtc(path,timestamp);
        check(WindowsServices.ApplyConfirmedVersion(app,[binding]).Version=="未知","same-size replacement with unchanged timestamp still invalidates the SHA-256-bound confirmation");
        File.WriteAllBytes(path,bytes);
        var elsewhere=Path.Combine(root,"other-evidence");Directory.CreateDirectory(elsewhere);var otherPath=Path.Combine(elsewhere,"ExampleEditor.exe");File.Copy(path,otherPath);
        check(WindowsServices.ApplyConfirmedVersion(app with{ExecutablePath=otherPath},[binding]).VersionNeedsReview,"identical bytes in another installation cannot inherit a path-specific confirmation");
        check(WindowsServices.ApplyConfirmedVersion(app,[]).Version=="1.0.0","clearing confirmation restores automatic evidence rather than retaining a permanent override");
        var bad=false;try{WindowsServices.ConfirmInstalledVersion("latest",path);}catch(IOException){bad=true;}check(bad,"invalid self-reported version text is rejected");
        check(WindowsServices.ConfirmInstalledVersion("",path).Hash.Length==0,"blank confirmation has no stored version or fingerprint");
        var conflicting=binding.Snapshot();conflicting.ConfirmedVersion="6.0.0";
        check(WindowsServices.ApplyConfirmedVersion(app,[binding,conflicting]).Version=="未知","conflicting user confirmations do not silently win by list order");
        var noticed=WindowsServices.NoteOtherRunningCopies(app,[program with{Path=otherPath,ProductVersion="2.0.0"}]);
        check(noticed.Version==app.Version&&noticed.VersionNeedsReview&&noticed.VersionDetails.Contains(otherPath),"another running installation is reported without copying its version into the configured path");
        check(!WindowsServices.NoteOtherRunningCopies(app,[program]).VersionNeedsReview,"the configured running executable is not marked as a different installation");
        check(!WindowsServices.NoteOtherRunningCopies(app,[program,program with{Path=otherPath}]).VersionNeedsReview,"two intentionally running installations do not block the already selected running copy");
        check(!WindowsServices.NoteOtherRunningCopies(app,[program with{Path=Path.Combine(elsewhere,"Unrelated.exe")}]).VersionNeedsReview,"unrelated process names do not create cross-application conflicts");
        var machine=app with{Id="LocalMachine/Registry64/old"};var user=app with{Id="CurrentUser/Registry64/new",ExecutablePath=otherPath,Location=elsewhere,Version="2.0.0"};
        var selected=new Binding{AppId=machine.Id,Repository="example/editor",ExecutablePath=otherPath};
        check(InstalledIdentity.Target(selected,[machine,user])?.Id==user.Id,"explicit installation path outranks a stale registration ID when both copies still exist");
        var settings=new Settings{Bindings=[selected]};var repaired=InstalledIdentity.Reconcile(settings,[machine,user]);
        check(selected.AppId==user.Id&&!selected.AliasIds.Contains(machine.Id)&&InstalledVersion.Find(repaired.Apps,machine.Id)?.ExecutablePath==path,"switching copies preserves the other installation's identity rather than stealing its live registration alias");
        settings=new(){Bindings=[new(){AppId=app.Id,Repository="example/editor"},binding]};InstalledIdentity.Reconcile(settings,[app]);
        check(settings.Bindings.Count==1&&settings.Bindings[0].ConfirmedVersionHash==binding.ConfirmedVersionHash,"duplicate repair carries forward a single explicit confirmation without losing it");
        settings=new(){Bindings=[binding,conflicting]};InstalledIdentity.Reconcile(settings,[app]);
        check(settings.Bindings.Count==2,"incompatible confirmed versions remain available for manual review");
        using(var store=new LocalStore(Path.Combine(folder,"store"))){store.Set("settings",new Settings{Bindings=[binding]});var saved=store.Get("settings",new Settings()).Bindings.Single();check(saved.ConfirmedVersionHash==binding.ConfirmedVersionHash&&saved.ConfirmedVersionPath==path,"version confirmation and file binding survive reload");}
    }
}
