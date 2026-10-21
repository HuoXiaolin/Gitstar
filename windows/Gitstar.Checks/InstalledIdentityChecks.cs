using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;

internal static class InstalledIdentityChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        const string prefix="LocalMachine/Registry64/";
        var folder=Path.Combine(root,"identity-upgrade");Directory.CreateDirectory(folder);
        var program=Path.Combine(folder,"Editor.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Gitstar.Checks.exe"),program);
        var current=new InstalledApp(prefix+"new","Editor","2.0.0",folder,ExecutablePath:program);
        var old=current with{Id=prefix+"old",Version="1.0.0"};
        var binding=new Binding{AppId=old.Id,Repository="example/editor",Disabled=true,IgnoredTag="v8",PauseUntil=DateTimeOffset.UtcNow.AddDays(3)};
        var settings=new Settings{Bindings=[binding]};var repair=InstalledIdentity.Reconcile(settings,[current],[old]);
        check(binding.AppId==current.Id&&binding.ExecutablePath==program&&binding.AppLocation==folder,"upgraded registration migrates to the same installed program and records its location");
        check(binding.Disabled&&binding.IgnoredTag=="v8"&&binding.PauseUntil>DateTimeOffset.UtcNow,"registration migration preserves disabled pause and ignored-version choices");
        check(InstalledVersion.Find(repair.Apps,old.Id)?.Id==current.Id,"installer completion can find the new record using the old product code");
        check(InstalledSnapshot.Capture(repair.Apps,settings.Bindings).Apps.Single().Version=="2.0.0","snapshot retains the current detected version after identity migration");
        var next=current with{Id=prefix+"next",Version="3.0.0"};repair=InstalledIdentity.Reconcile(settings,[next],repair.Apps);
        check(InstalledVersion.Find(repair.Apps,old.Id)?.Version=="3.0.0"&&binding.AliasIds.Contains(current.Id),"successive MSI upgrades retain the original aliases without losing the binding");
        var legacy=new Settings{Bindings=[new(){AppId=prefix+"gone1",Repository="example/editor",Disabled=true},new(){AppId=prefix+"gone2",Repository="example/editor",IgnoredTag="v8"},new(){AppId=current.Id,Repository="example/editor",PauseUntil=DateTimeOffset.UtcNow.AddDays(5)}]};
        repair=InstalledIdentity.Reconcile(legacy,[current]);
        check(repair.Migrated==2&&repair.Merged==2&&legacy.Bindings.Count==1,"legacy missing IDs with one exact repository-name candidate consolidate into one installation");
        check(legacy.Bindings[0].Disabled&&legacy.Bindings[0].IgnoredTag=="v8"&&legacy.Bindings[0].PauseUntil>DateTimeOffset.UtcNow.AddDays(4),"duplicate consolidation retains the strongest existing reminder restrictions");
        check(InstalledIdentity.Reconcile(legacy,repair.Apps).Merged==0,"identity repair is idempotent after reload");
        var elsewhere=current with{Id=prefix+"other",Location=Path.Combine(root,"another"),ExecutablePath=Path.Combine(root,"another","Editor.exe")};
        var ambiguous=new Binding{AppId=prefix+"gone",Repository="example/editor"};
        check(InstalledIdentity.Target(ambiguous,[current,elsewhere])==null,"same repository in two installation directories is never guessed");
        var explicitLocation=new Binding{AppId=prefix+"gone",Repository="example/editor",AppName="Editor",AppLocation=folder};
        check(InstalledIdentity.Target(explicitLocation,[current,elsewhere])==current,"recorded installation location selects the correct side-by-side installation");
        check(InstalledIdentity.Target(explicitLocation,[elsewhere])==null,"a missing recorded installation is not redirected to another directory");
        check(InstalledIdentity.Target(ambiguous,[current with{Id="CurrentUser/Registry64/new"}])==null,"legacy migration does not jump between machine and user registration scopes");
        check(InstalledIdentity.Target(ambiguous,[current with{Name="Editor Plus"}])==null,"fuzzy repository names cannot automatically repair a missing association");
        check(InstalledIdentity.Target(ambiguous,[current with{RepositoryHint="another/editor"}])==null,"another repository hint does not qualify as an exact match");
        check(InstalledIdentity.Merge([current,current with{Id=prefix+"alias",Name="Editor 2.0.0"}]).Count==1,"same executable is deduplicated even if a registration display name changed");
        check(InstalledIdentity.Merge([current,elsewhere]).Count==2,"different executable locations stay distinct even with the same name and repository");
        var variants=new Settings{Bindings=[new(){AppId=current.Id,Repository="example/editor",TagPrefix="stable-"},new(){AppId=current.Id,Repository="example/editor",TagPrefix="beta-"}]};
        InstalledIdentity.Reconcile(variants,[current]);check(variants.Bindings.Count==2,"different release-matching rules are not silently discarded as duplicates");
        variants=new(){Bindings=[new(){AppId=current.Id,Repository="example/editor",IgnoredTag="v1"},new(){AppId=current.Id,Repository="example/editor",IgnoredTag="v2"}]};
        InstalledIdentity.Reconcile(variants,[current]);check(variants.Bindings.Count==2,"conflicting explicit ignored versions remain available for user review");
        var zed=Path.Combine(folder,"ZedG.exe");File.Copy(program,zed);var registry=new InstalledApp(prefix+"zed-new","ZedG","0.0.1","");
        var zedBinding=new Binding{AppId=prefix+"zed-old",Repository="example/zed-globalization",ExecutablePath=zed};
        var detected=WindowsServices.ScanInventory([new(registry)],[zedBinding],[],[],true);
        var actualVersion=InstalledVersion.NormalizeVersion(System.Diagnostics.FileVersionInfo.GetVersionInfo(zed).ProductVersion??"");
        check(detected.Single().Id==registry.Id&&detected.Single().ExecutablePath==zed,"matched scan recovers the saved main-program path across MSI product-code changes without an old snapshot");
        check(detected.Single().Version==actualVersion&&actualVersion!="0.0.1","actual executable version takes precedence over an obsolete registered version");
        var zedSettings=new Settings{Bindings=[zedBinding,new(){AppId=registry.Id,Repository=zedBinding.Repository}]};repair=InstalledIdentity.Reconcile(zedSettings,detected);
        check(zedSettings.Bindings.Count==1&&InstalledVersion.Find(repair.Apps,prefix+"zed-old")?.Version==actualVersion,"path-based legacy recovery removes duplicate associations and preserves completion lookup");
        var missing=zedBinding.Snapshot();missing.ExecutablePath=Path.Combine(folder,"Missing.exe");
        detected=WindowsServices.ScanInventory([new(registry)],[missing],[],[],true);
        check(detected.Single().Version=="未知","missing selected main program never borrows the registered or target-release version");
        var unregistered=zedBinding.Snapshot();unregistered.AppId=prefix+"unregistered";
        detected=WindowsServices.ScanInventory([],[unregistered],[],[],true);
        check(detected.Single().Version==actualVersion,"a saved executable can be verified after its old registration disappears even without a cached snapshot");
        var unrelated=current with{Id=prefix+"unrelated",Name="Unrelated"};
        detected=WindowsServices.ScanInventory([new(registry),new(unrelated)],[zedBinding],[],[],true);
        check(detected.Count==1,"matched checks read versions only for associated programs, not the entire software inventory");
        var stale=current with{Version="99.0.0"};var entry=new Binding{AppId=current.Id,Repository="example/editor",ExecutablePath=program};
        detected=WindowsServices.ScanInventory([new(stale)],[entry],[],[old],true);
        check(detected.Single().Version==actualVersion,"post-update record is derived from the installed file rather than a newer registry or release label");
        using(var store=new LocalStore(Path.Combine(folder,"store"))){store.Set("settings",zedSettings);var reloaded=store.Get("settings",new Settings());check(reloaded.Bindings.Single().AppLocation==folder&&reloaded.Bindings.Single().AliasIds.Contains(prefix+"zed-old"),"program location and historical aliases survive persistence and application updates");}
    }
}
