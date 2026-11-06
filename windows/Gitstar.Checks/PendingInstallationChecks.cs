using System.IO;
using System.Text.Json;
using Gitstar.Core;

internal static class PendingInstallationChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var app=new InstalledApp("old-msi-guid","Example","1.0.0",@"C:\Apps\Example",ExecutablePath:@"C:\Apps\Example\Example.exe",VersionSource:"程序产品版本");
        var binding=new Binding{AppId=app.Id,Repository="example/app",ExecutablePath=app.ExecutablePath};
        var package=new DownloadedPackage(binding.Repository,10,"v2.0.0",false,now,"Example-x64.exe",@"C:\Downloads\Example-x64.exe",@"C:\Downloads",100,"hash","windows/x64");
        var item=PendingInstallationRules.Create(package,app,binding);
        var restored=JsonSerializer.Deserialize<PendingInstallation>(JsonSerializer.Serialize(item))!;
        check(restored.Id==item.Id&&restored.Package.Path==package.Path&&restored.TargetVersion=="v2.0.0","pending installation survives persistence with its exact original package and target version");
        check(!PendingInstallationRules.Installed(item,[binding],[app],true),"an older installed version keeps the pending installation");
        var updated=app with{Version="2.0.0"};
        check(!PendingInstallationRules.Installed(item,[binding],[updated],false),"cached installed versions cannot prematurely remove pending packages");
        check(PendingInstallationRules.Installed(item,[binding],[updated],true)&&PendingInstallationRules.Installed(item,[binding],[updated with{Version="3.0"}],true),"verified same or higher version at the original installation path completes a pending installation");
        check(!PendingInstallationRules.Installed(item,[binding],[updated with{VersionSource="注册表/手动版本（未验证程序文件）"}],true)&&!PendingInstallationRules.Installed(item,[binding],[updated with{VersionNeedsReview=true}],true),"unverified registry versions and conflicting evidence do not clear pending installations");
        var moved=updated with{ExecutablePath=@"D:\Other\Example.exe"};
        check(!PendingInstallationRules.Installed(item,[binding],[moved],true),"a higher version in another installation location cannot complete the original pending update");
        var newBinding=binding.Snapshot();newBinding.AppId="new-msi-guid";
        check(PendingInstallationRules.Installed(item,[newBinding],[updated with{Id=newBinding.AppId}],true),"MSI identity changes preserve pending-update resolution through the same executable path");
        check(!PendingInstallationRules.Installed(item,[],[],true),"temporarily missing or unbound software keeps its pending record for repair");
        Release R(long id,string tag,bool beta=false,bool latest=false,string? repo=null)=>new(id,repo??binding.Repository,tag,"","",beta,now.AddDays(1),"",[]){IsLatest=latest};
        check(PendingInstallationRules.Superseded(item,[R(11,"v3.0.0")]),"a verified newer eligible release expires the older installation entry");
        check(!PendingInstallationRules.Superseded(item,[R(12,"v1.9.9")]),"a later-published lower backport does not expire the newer pending package");
        check(!PendingInstallationRules.Superseded(item,[R(11,"v3.0.0-beta.1",true)]),"a stable-only subscription is not expired by an excluded preview release");
        var betaBinding=binding.Snapshot();betaBinding.IncludePrerelease=true;
        var betaItem=item with{Binding=betaBinding};
        check(PendingInstallationRules.Superseded(betaItem,[R(11,"v3.0.0-beta.1",true)]),"included preview channels can expire older pending packages");
        check(PendingInstallationRules.Superseded(item,[R(12,"v1.9.9",latest:true)]),"an authoritative fresh GitHub Latest change is honored even when it is a rollback");
        check(!PendingInstallationRules.Superseded(item,[])&&!PendingInstallationRules.Superseded(item,[R(11,"v9.0",repo:"other/app")]),"missing or unrelated release data cannot delete pending installation entries");
        check(!PendingInstallationRules.Superseded(item,[R(11,"2.0.0.0")]),"equivalent version spellings and reissued release IDs do not expire the same target version");
        var prefix=binding.Snapshot();prefix.TagPrefix="desktop-v";
        var prefixed=PendingInstallationRules.Create(package with{Tag="desktop-v2.0.0"},app,prefix);
        check(prefixed.TargetVersion=="2.0.0"&&!PendingInstallationRules.Superseded(prefixed,[R(20,"mobile-v9.0")])&&PendingInstallationRules.Superseded(prefixed,[R(21,"desktop-v3.0")]),"multi-product repository prefixes remain independent when expiring pending updates");
        var items=new List<PendingInstallation>();PendingInstallationRules.Upsert(items,item);PendingInstallationRules.Upsert(items,PendingInstallationRules.Create(package,app,binding));
        check(items.Count==1&&items[0].Id==item.Id,"repeated download registration reuses one durable installation entry");
        var another=item with{Id="other-path",App=app with{Id="other",ExecutablePath=@"D:\Other\Example.exe"},Binding=new(){AppId="other",Repository=binding.Repository,ExecutablePath=@"D:\Other\Example.exe"}};
        items.Add(another);var newer=PendingInstallationRules.Create(package with{ReleaseId=11,Tag="v3.0"},app,binding);PendingInstallationRules.Upsert(items,newer);
        check(items.Count==2&&items.Any(entry=>entry.Id==another.Id)&&items.Any(entry=>entry.TargetVersion=="v3.0"),"newer downloads replace older pending versions only for the same installation context");
        var selection=new List<PendingInstallation>{item,another,newer};var removed=PendingInstallationRules.RemoveSelected(selection,[item.Id,item.Id,"missing"],[]);
        check(removed.Length==1&&selection.Count==2&&selection.All(entry=>entry.Id!=item.Id),"deleting selected pending entries removes only chosen identities and tolerates duplicate or stale selections");
        removed=PendingInstallationRules.RemoveSelected(selection,[another.Id,newer.Id],[newer.Id]);
        check(removed.Length==1&&selection.Single().Id==newer.Id,"active installation cannot be dismissed by a selection captured before it started");
        check(JsonSerializer.Deserialize<List<PendingInstallation>>(JsonSerializer.Serialize(selection))!.Single().Id==newer.Id,"explicit pending deletion remains absent after persistence and reload");
        check(PendingInstallationRules.RemoveSelected(selection,[],[]).Length==0&&selection.Count==1,"empty pending selection does not clear the list");
        PendingInstallationRules.RemoveSelected(selection,[newer.Id],[]);check(selection.Count==0&&package.Path==item.Package.Path,"selecting all pending entries clears only records and leaves package metadata unchanged");
    }
}
