using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;
using System.Text.Json;

internal static class SoftwareAuditChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var old=Path.Combine(root,"old","Editor.exe");var current=Path.Combine(root,"new","Editor.exe");var second=Path.Combine(root,"another","Editor.exe");
        var paths=new Dictionary<string,SoftwarePathState>(StringComparer.OrdinalIgnoreCase){[old]=SoftwarePathState.Present,[current]=SoftwarePathState.Present,[second]=SoftwarePathState.Present};
        SoftwarePathState Probe(string path)=>paths.GetValueOrDefault(path,SoftwarePathState.Missing);
        var binding=new Binding{AppId="LocalMachine/Registry64/old",AppName="Editor",Repository="owner/editor",ExecutablePath=old,Disabled=true,IgnoredTag="v2",PauseUntil=DateTimeOffset.UtcNow.AddDays(3),ConfirmedVersion="1.0",ConfirmedVersionPath=old,ConfirmedVersionHash="hash"};
        var saved=new InstalledApp(binding.AppId,"Editor","1.0",Path.GetDirectoryName(old)!,ExecutablePath:old);
        SoftwareAuditEntry Review(params InstalledApp[] inventory)=>SoftwareRecordAudit.Review([binding],[saved],inventory,Probe,true).Single();
        check(Review(saved).State==SoftwareAuditState.Healthy,"software audit confirms an existing original executable without changing settings");
        var upgraded=saved with{Id="LocalMachine/Registry64/new",Version="2.0"};check(Review(upgraded).State==SoftwareAuditState.Repairable&&Review(upgraded).Candidates.Single().Id==upgraded.Id,"software audit offers registration repair when installer ID changed at the same location");
        paths[old]=SoftwarePathState.Missing;var moved=upgraded with{ExecutablePath=current,Location=Path.GetDirectoryName(current)!};
        check(Review(moved).State==SoftwareAuditState.Repairable&&Review(moved).Candidates.Single().ExecutablePath==current,"software audit discovers changed paths through current installation evidence");
        check(Review(moved,moved with{Id="second",ExecutablePath=second}).Candidates.Count==2,"software audit preserves multiple same-name installations for explicit choice");
        check(Review().State==SoftwareAuditState.Missing,"software audit suggests obsolete record only when path and registration are absent");
        check(Review(saved with{ExecutablePath=""}).State==SoftwareAuditState.Uncertain,"stale registration without a verified executable requires review rather than claiming uninstall");
        paths[old]=SoftwarePathState.Unavailable;check(Review().State==SoftwareAuditState.Uncertain,"inaccessible or offline software location is never treated as uninstall");paths[old]=SoftwarePathState.Missing;
        check(SoftwareRecordAudit.Review([binding],[saved],[],Probe,false).Single().State==SoftwareAuditState.Uncertain,"incomplete registry inventory cannot authorize uninstall classification");
        paths[old]=SoftwarePathState.Present;check(Review().State==SoftwareAuditState.Healthy,"existing program remains valid even when its uninstall registration disappeared");
        paths[old]=SoftwarePathState.Missing;var settings=new Settings{Bindings=[binding]};var pause=binding.PauseUntil;SoftwareRecordAudit.Repair(settings,binding,moved,Probe);
        check(binding.ExecutablePath==current&&binding.AppId==moved.Id&&binding.Disabled&&binding.IgnoredTag=="v2"&&binding.PauseUntil==pause&&binding.Repository=="owner/editor","software record repair updates physical identity while preserving repository pause ignore and stopped settings");
        check(binding.ConfirmedVersion.Length==0&&binding.ConfirmedVersionHash.Length==0,"path repair clears version confirmation bound to the old executable");
        var duplicate=new Binding{AppId="other",ExecutablePath=second,Repository="owner/other"};settings.Bindings.Add(duplicate);var before=JsonSerializer.Serialize(binding);var rejected=false;
        try{SoftwareRecordAudit.Repair(settings,binding,moved with{Id="other",ExecutablePath=second},Probe);}catch(IOException){rejected=true;}check(rejected&&JsonSerializer.Serialize(binding)==before,"conflicting software repair fails without changing the existing binding");
        var portable=new InstalledApp("portable/old","Portable","1",root,true,ExecutablePath:current);var portableBinding=new Binding{AppId=portable.Id,ExecutablePath=current,Repository="owner/portable"};var shared=new Binding{AppId=portable.Id,ExecutablePath=current,Repository="owner/shared"};var portableSettings=new Settings{Bindings=[portableBinding,shared],PortableApps=[portable]};
        SoftwareRecordAudit.Remove(portableSettings,portableBinding);check(portableSettings.Bindings.Count==1&&portableSettings.PortableApps.Count==1,"record deletion retains portable registration still referenced by another binding");SoftwareRecordAudit.Remove(portableSettings,shared);check(portableSettings.PortableApps.Count==0,"record deletion removes unreferenced stale portable inventory instead of leaving it behind");
        var file=Path.Combine(root,"audit-do-not-delete.exe");File.WriteAllText(file,"user-program-and-settings");var record=new Binding{AppId="portable/file",ExecutablePath=file,Repository="owner/app"};var deletion=new Settings{Bindings=[record],PortableApps=[portable with{Id=record.AppId,ExecutablePath=file}]};SoftwareRecordAudit.Remove(deletion,record);check(File.ReadAllText(file)=="user-program-and-settings","deleting software records never deletes or changes program files");
        check(WindowsServices.AuditPath(file)==SoftwarePathState.Present&&WindowsServices.AuditPath(Path.Combine(root,"missing-audit.exe"))==SoftwarePathState.Missing,"software audit distinguishes present and definitively missing local paths");
        var directory=Path.Combine(root,"directory.exe");Directory.CreateDirectory(directory);check(WindowsServices.AuditPath(directory)==SoftwarePathState.Unavailable&&WindowsServices.AuditPath(@"\\offline-server\share\App.exe")==SoftwarePathState.Unavailable,"directory masquerading as EXE and network paths require manual review");
    }
}
