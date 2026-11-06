using System.Text.Json;
using System.Runtime.InteropServices;
using Gitstar.Core;

internal static class NewInstallationChecks
{
    public static void Run(Action<bool,string> check)
    {
        Asset A(string name,long id=1)=>new(id,name,100,"",null);
        var now=DateTimeOffset.UtcNow;var binding=new Binding();var beta="0.6.67-beta.20261105.1";var preview=A("Example_"+beta+"_x64-setup.exe");var stable=A("Example_0.6.67_x64-setup.exe",2);var offline=A("Example_0.6.67_x64-offline-setup.exe",3);
        var release=new Release(1,"example/Example","v0.6.67","","",false,now,"",[preview,offline,stable]);
        check(InstallerVersionRules.Declared(preview.Name,release.Tag)==beta,"installer prerelease version is read without consuming architecture or setup suffixes");
        check(InstallerVersionRules.Target(release,binding,preview)==beta,"a selected preview installer does not claim to install the stable release tag");
        check(InstallerVersionRules.Target(release,binding)==release.Tag,"mixed preview and stable installers preserve the real stable upgrade target");
        var recommended=InstallerVersionRules.Recommended(release,release.Assets);
        check(!recommended.Contains(preview)&&recommended.Contains(stable),"stable release recommendations exclude same-line preview installers when stable installers exist");
        check(PackagePreference.Select(recommended,Architecture.X64)==stable,"standard native installer preferred over offline and legacy alternatives");
        check(InstallerVersionRules.Recommended(release,release.Assets,"beta").Contains(preview),"explicit preview asset matching remains available");
        check(InstallerVersionRules.Recommended(release with{Prerelease=true},release.Assets).Contains(preview),"testing releases keep their preview assets");
        check(InstallerVersionRules.Target(release with{Assets=[preview]},binding)==beta,"release containing only explicit preview installers uses their actual target version");
        check(InstallerVersionRules.Target(release with{Assets=[preview,A("Example-Setup.exe")]},binding)==release.Tag,"unknown asset version cannot silently equate preview with stable");
        check(InstallerVersionRules.Declared("Example-9.9.9-x64.exe",release.Tag)==null&&InstallerVersionRules.Declared("Example-0.6.67-to-1.0.0.exe",release.Tag)==null,"unrelated or ambiguous filename versions cannot override a release");
        check(Rules.CompareVersions("0.6.67",beta)>0,"normal semantic preview-to-stable upgrades remain distinct");
        var package=new DownloadedPackage(release.Repository,1,release.Tag,false,now,preview.Name,@"C:\Downloads\"+preview.Name,@"C:\Downloads",100,new string('A',64),"windows/x64");
        var app=new InstalledApp("new","Example",beta,@"C:\Apps\Example",ExecutablePath:@"C:\Apps\Example\Example.exe",VersionSource:"程序产品版本");
        var associated=new Binding{AppId=app.Id,Repository=release.Repository,ExecutablePath=app.ExecutablePath};var pending=PendingInstallationRules.Create(package,app,associated);
        check(PendingInstallationRules.Installed(pending,[associated],[app],true),"already-installed exact preview package clears its pending install while stable upgrade remains available");
        var fresh=NewInstallationRules.Create(package) with{NewInstall=new([],true)};
        check(NewInstallationRules.Candidates(fresh,[app]).Single()==app,"new verified uniquely named software matches its download repository");
        var settings=new Settings();var saved=NewInstallationRules.Bind(settings,fresh,[app]);
        check(saved?.ExecutablePath==app.ExecutablePath&&saved.Repository==release.Repository&&settings.Bindings.Count==1,"automatic association records actual installed program path and source repository");
        saved!.Disabled=true;saved.IgnoredTag="keep";NewInstallationRules.Bind(settings,fresh,[app]);
        check(settings.Bindings.Count==1&&saved.Disabled&&saved.IgnoredTag=="keep","rechecking installation preserves existing management choices and avoids duplicate bindings");
        check(NewInstallationRules.Candidates(fresh with{NewInstall=new([app],true)},[app]).Length==0,"unchanged pre-existing software cannot be mistaken for a newly installed application");
        var another=app with{Id="second",ExecutablePath=@"D:\Other\Example.exe"};check(NewInstallationRules.Bind(new(),fresh,[app,another])==null,"multiple matching installation paths require confirmation rather than choosing arbitrarily");
        check(NewInstallationRules.Candidates(fresh,[app with{VersionSource="注册表"},app with{Id="conflict",VersionNeedsReview=true}]).Length==0,"unverified or conflicting version evidence cannot create an automatic association");
        check(NewInstallationRules.Candidates(fresh,[app with{ExecutablePath=package.Path}]).Length==0,"downloaded installer itself cannot become the bound main executable");
        var changed=settings.Bindings[0].Snapshot();changed.Repository="other/Example";var otherSettings=new Settings{Bindings=[changed]};check(NewInstallationRules.Bind(otherSettings,fresh,[app])==null&&changed.Repository=="other/Example","existing association to another repository is never overwritten");
        var restored=JsonSerializer.Deserialize<PendingInstallation>(JsonSerializer.Serialize(fresh))!;check(restored.NewInstall is {Started:true}&&restored.Package.Repository==release.Repository,"unfinished new installation retains its baseline and source across restart");
        check(!NewInstallationRules.Installable(A("Example.zip"))&&!NewInstallationRules.Installable(A("Example-arm64.exe"))&&NewInstallationRules.Installable(stable),"download-after-install only starts compatible Windows EXE/MSI files");
    }
}
