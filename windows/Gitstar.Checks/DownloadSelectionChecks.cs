using System.Runtime.InteropServices;
using System.Text.Json;
using Gitstar.Core;

internal static class DownloadSelectionChecks
{
    public static void Run(Action<bool,string> check)
    {
        Asset A(long id,string name)=>new(id,name,100,"","sha256:fixture");
        Release R(string tag,params Asset[] assets)=>new(1,"example/editor",tag,"","",false,DateTimeOffset.UtcNow,"",assets.ToList());
        var settings=new Settings();var old=R("v1.0.0",A(1,"Editor-v1.0.0-x64.msi"));
        DownloadSelectionRules.Remember(settings,old,DownloadPurpose.Install,old.Assets);
        var current=R("v2.0.0",A(2,"Editor-v2.0.0-x64.exe"),A(3,"Editor-v2.0.0-x64.msi"));
        DownloadSelectionPreference? Saved(DownloadPurpose purpose)=>DownloadSelectionRules.Find(settings,old.Repository,purpose,[]);
        List<Asset> Match(Release release,DownloadPurpose purpose=DownloadPurpose.Install,string pattern="")=>DownloadSelectionRules.Match(release,Saved(purpose),Architecture.X64,pattern);
        check(Match(current).Single().Id==3,"previous successful MSI download overrides generic EXE recommendation across versions");
        var unspecified=new DownloadSelectionPreference(old.Repository,DownloadPurpose.Install,"v1.0.0",["Editor.msi"]);
        check(DownloadSelectionRules.Match(current,unspecified,Architecture.X64).Single().Id==3,"previous MSI without architecture still prefers native MSI when new filenames add architecture labels");
        check(DownloadSelectionRules.Match(R("v2.0",A(50,"Editor-arm64.msi")),unspecified,Architecture.X64).Count==0,"unknown previous architecture does not authorize restoring an incompatible ARM installer");
        check(DownloadSelectionRules.Find(settings,"EXAMPLE/EDITOR",DownloadPurpose.Install,[])!=null&&DownloadSelectionRules.Find(settings,"another/editor",DownloadPurpose.Install,[])==null,"download selection is repository scoped with case-insensitive identity");
        check(Saved(DownloadPurpose.Save)==null&&Saved(DownloadPurpose.PortableUpdate)==null,"save installer and portable download choices do not overwrite each other");
        var serialized=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        check(DownloadSelectionRules.Match(current,DownloadSelectionRules.Find(serialized,old.Repository,DownloadPurpose.Install,[]),Architecture.X64).Single().Id==3,"download selection survives settings serialization and application restart");
        check(JsonSerializer.Deserialize<Settings>("{}")!.DownloadSelections.Count==0,"older settings without download choices retain compatible defaults");
        var gui=R("v0.1.100",A(4,"magpie-windows-amd64.exe"));DownloadSelectionRules.Remember(settings,gui,DownloadPurpose.PortableUpdate,gui.Assets);
        var both=R("v0.1.200",A(5,"magpie-cli-windows-amd64.exe"),A(6,"magpie-windows-amd64.exe"),A(7,"magpie-windows-arm64.exe"),A(8,"magpie-windows-amd64.zip"));
        check(Match(both,DownloadPurpose.PortableUpdate).Single().Id==6,"portable GUI EXE stays selected instead of earlier CLI EXE or default ZIP");
        var changed=R("v0.1.201",A(9,"magpie-cli-windows-amd64.exe"),A(10,"magpie-v0.1.201-windows-x64.exe"));
        check(Match(changed,DownloadPurpose.PortableUpdate).Single().Id==10,"release tags and amd64 x64 spelling changes preserve the chosen GUI variant");
        var cliOnly=R("v0.1.201",A(11,"magpie-cli-windows-amd64.exe"));
        check(Match(cliOnly,DownloadPurpose.PortableUpdate).Count==0,"missing GUI file does not silently match another CLI file merely because both are EXE");
        var withoutMsi=R("v2.0.0",A(12,"Editor-v2.0.0-x64.exe"));
        check(Match(withoutMsi).Count==0&&PackagePreference.Select(withoutMsi.Assets,Architecture.X64)?.Id==12,"missing previous format leaves the compatible native recommendation available");
        var incompatible=R("v2.0.0",A(13,"Editor-v2.0.0-arm64.msi"),A(14,"Editor-v2.0.0-linux-x64.zip"));
        check(Match(incompatible).Count==0,"remembered installer cannot select an incompatible architecture or operating system");
        var installer=R("v2.0.0",A(15,"Editor-setup-x64.exe"));DownloadSelectionRules.Remember(settings,installer,DownloadPurpose.PortableUpdate,installer.Assets);
        check(Match(installer,DownloadPurpose.PortableUpdate).Count==0,"portable preference does not automatically recommend an installer");
        DownloadSelectionRules.Remember(settings,old,DownloadPurpose.Save,old.Assets);
        check(Match(current,DownloadPurpose.Save).Single().Id==3,"save-only picker also remembers a previously chosen file format");
        var manual=R("v2.0.0",A(16,"Editor-arm64.exe"));DownloadSelectionRules.Remember(settings,manual,DownloadPurpose.Save,manual.Assets);
        check(Match(manual,DownloadPurpose.Save).Single().Id==16,"explicit cross-platform file saving remains available through remembered save-only selections");
        var multi=R("1.0",A(17,"main.js"),A(18,"styles.css"),A(19,"manifest.json"));DownloadSelectionRules.Remember(settings,multi,DownloadPurpose.Save,multi.Assets.Take(2));
        var multiNext=R("2.0",A(20,"manifest.json"),A(21,"styles.css"),A(22,"main.js"),A(23,"extra.css"));
        check(Match(multiNext,DownloadPurpose.Save).Select(asset=>asset.Name).SequenceEqual(new[]{"main.js","styles.css"}),"multiple successful file types are restored without selecting unrelated new attachments");
        check(Match(R("2.0",A(40,"extra.js"),A(41,"styles.css")),DownloadPurpose.Save).Select(asset=>asset.Name).SequenceEqual(new[]{"styles.css"}),"a missing companion file is not replaced by an unrelated attachment with the same extension");
        DownloadSelectionRules.Remember(settings,multi,DownloadPurpose.Save,[]);
        check(Saved(DownloadPurpose.Save)!.FileNames.SequenceEqual(new[]{"main.js","styles.css"}),"an attempt with no successful downloads cannot erase the last successful selection");
        DownloadSelectionRules.Remember(settings,multi,DownloadPurpose.Save,[multi.Assets[2]]);
        check(Saved(DownloadPurpose.Save)!.FileNames.SequenceEqual(new[]{"manifest.json"})&&Saved(DownloadPurpose.Install)!.FileNames.Single().EndsWith(".msi"),"a later successful manual selection replaces only that project and operation preference");
        var archive=R("v1.0",A(24,"Editor-windows-x64.tar.gz"));DownloadSelectionRules.Remember(settings,archive,DownloadPurpose.Save,archive.Assets);
        var archives=R("v2.0",A(25,"Editor-windows-x64.zip"),A(26,"Editor-windows-x64.tar.gz"),A(27,"Editor-windows-x64.tar.xz"));
        check(Match(archives,DownloadPurpose.Save).Single().Id==26,"compound archive formats remain distinct");
        var renamed=R("v2.0",A(28,"Desktop-windows-x64.tar.gz"),A(29,"Editor-macos-x64.tar.gz"),A(30,"Editor-windows-arm64.tar.gz"));
        check(Match(renamed,DownloadPurpose.Save).Single().Id==28,"renamed asset falls back to the same format platform and architecture");
        check(Match(R("v2.0",A(31,"Editor-osx-x64.tar.gz")),DownloadPurpose.Save).Count==0,"OSX archives cannot replace a Windows archive preference");
        check(Match(current,pattern:".exe").Count==0,"an explicit changed binding pattern takes precedence over an older matching preference");
        check(DownloadSelectionRules.Match(current with{Assets=[current.Assets[1] with{Size=0}]},Saved(DownloadPurpose.Install),Architecture.X64).Count==0,"empty assets are not restored as a remembered selection");
        DownloadedPackage P(string tag,string name,bool batch=false)=>new(old.Repository,1,tag,false,DateTimeOffset.UtcNow,name,"deleted-file","old-folder",100,"digest","windows/x64"){RetainForBatch=batch};
        var historical=DownloadSelectionRules.Find(new Settings(),old.Repository,DownloadPurpose.Install,[P("v9.0","Old-x64.exe"),P("v1.0","Latest-download-x64.msi"),P("v2.0","Batch-x64.zip",true)]);
        check(historical?.Tag=="v1.0"&&historical.FileNames.Single()=="Latest-download-x64.msi","legacy download records seed the last manual selection even after the file is cleaned up and ignore bulk retained packages");
    }
}
