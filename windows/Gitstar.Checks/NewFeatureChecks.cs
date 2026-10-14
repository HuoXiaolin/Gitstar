using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class NewFeatureChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var now=DateTimeOffset.UtcNow;var assets=new List<Asset>{new(1,"Setup.EXE",20,"",null),new(2,"setup.msi",30,"",null),new(3,"source.tar.gz",40,"",null),new(4,"setup.exe.sig",1,"",null)};
        Release R(int id,int days,bool beta=false)=>new(id,"owner/app","v"+id,"","",beta,now.AddDays(-days),"",assets);
        check(BulkDownloadRules.Select([R(1,1),R(2,3),R(3,8)],now,7,["exe",".msi"]).Count==4,"bulk download includes all versions within chosen period rather than latest version only");
        check(BulkDownloadRules.Select([R(1,7),R(2,8),R(3,-1)],now,7,[".exe"]).Count==1,"bulk period includes exact boundary and excludes old and future releases");
        check(BulkDownloadRules.Select([R(1,0),R(2,0,true)],now,1,[".EXE","exe"],0).Count==1&&BulkDownloadRules.Select([R(1,0),R(2,0,true)],now,1,[".tar.gz"],2).Single().Release.Prerelease,"bulk formats are multi-select case-insensitive and channel-specific");
        var invalid=false;try{BulkDownloadRules.Extensions(["*.exe"]);}catch(ArgumentException){invalid=true;}check(invalid,"bulk formats reject wildcard or path input");
        check(BulkDownloadRules.Days.SequenceEqual(new[]{30,14,7,3,1})&&new Settings().BulkDownloadDays==7,"bulk periods include requested presets and default to seven days");
        var pages=0;
        using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{
            pages++;var page=int.Parse(request.RequestUri!.Query.Split("page=").Last());
            object Item(int id,DateTimeOffset published,bool draft=false)=>new{id,tag_name="v"+id,name="",body="not retained",prerelease=false,draft,published_at=published,html_url="",assets=assets.Select(a=>new{id=a.Id,name=a.Name,size=a.Size,browser_download_url=""})};
            // A recently published older draft can occur on a later creation-ordered page.
            var payload=page==1?Enumerable.Range(1,30).Select(i=>Item(i,now.AddDays(-100))).ToArray():new[]{Item(31,now.AddHours(-1)),Item(32,now.AddHours(-1),true)};
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(payload))});
        }))){var found=await api.ReleasesInPeriod("owner/app",now.AddDays(-7),now,CancellationToken.None);check(pages==2&&found.Single().Id==31&&found.Single().Body==""&&found.Single().Assets.Count==4,"period scan skips drafts and checks later pages without retaining release note bodies");}
        var repos=new[]{new Repository("Owner/Shared","",false,true),new Repository("owner/remote","",true,true),new Repository("owner/local","",false)};
        var local=FollowLists.Local(repos,["owner/shared","owner/imported","owner/hidden"],["owner/hidden"]);
        check(local.SequenceEqual(new[]{"owner/imported","owner/local"})&&FollowLists.GitHub(repos).Length==2,"GitHub follows take display precedence over case-insensitive duplicate local follows");
        var export=FollowLists.Export(FollowLists.GitHub(repos).Concat(local));var imported=FollowLists.Import(export);
        check(imported.Length==4&&export.Contains("GitstarLocalFollows")&&!export.Contains("Private")&&!export.Contains("Token"),"selected GitHub and local follows export as one portable local-only list without account secrets");
        check(FollowLists.Import("[\"https://github.com/owner/app\",\"OWNER/APP\"]").Length==1,"follow import accepts repository links and deduplicates names");
        invalid=false;try{FollowLists.Import("{\"schemaVersion\":2,\"kind\":\"GitstarLocalFollows\",\"repositories\":[]}");}catch(IOException){invalid=true;}check(invalid,"follow import rejects unsupported schema");
        invalid=false;try{FollowLists.Import("[\"owner/app\",\"../other\"]");}catch(ArgumentException){invalid=true;}check(invalid,"invalid imported repositories fail before settings are modified");
        var data=FollowLists.Export(imported);using(var db=new LocalStore(Path.Combine(root,"follow-transfer"))){db.Set("settings",new Settings{ExtraRepositories=FollowLists.Import(data).ToList()});}using(var db=new LocalStore(Path.Combine(root,"follow-transfer")))check(db.Get("settings",new Settings()).ExtraRepositories.Count==4,"imported local follow list persists across restart");
        check(Rules.IsWindowsInstallCandidate(new(1,"ProxyBridge-Setup-4.1.1.exe",1,"",null)),"ProxyBridge installer without architecture in name is eligible for content inspection");
        string Pe(string name,ushort machine,bool pe64){var path=Path.Combine(root,name);using var stream=File.Create(path);using var writer=new BinaryWriter(stream);stream.SetLength(512);writer.Write((ushort)0x5a4d);stream.Position=0x3c;writer.Write(0x80);stream.Position=0x80;writer.Write(0x00004550);writer.Write(machine);writer.Write((ushort)0);stream.Position=0x80+20;writer.Write((ushort)(pe64?240:224));writer.Write((ushort)0x0002);writer.Write((ushort)(pe64?0x20b:0x10b));return path;}
        var x86=Pe("neutral-setup.exe",0x14c,false);var x64=Pe("neutral-x64.exe",0x8664,true);var arm=Pe("neutral-arm.exe",0xaa64,true);
        check(InstallerCompatibility.InspectExe(x86,true).Compatible&&InstallerCompatibility.InspectExe(x86,false).Name=="Windows x86","PE content allows native x86 installer on both 32-bit and 64-bit Windows");
        check(InstallerCompatibility.InspectExe(x64,true).Compatible&&!InstallerCompatibility.InspectExe(x64,false).Compatible,"PE content allows x64 on 64-bit Windows and blocks it on 32-bit Windows");
        check(!InstallerCompatibility.InspectExe(arm,true).Compatible,"PE content blocks ARM64 installer on x64 Windows even without filename marker");
        check(InstallerCompatibility.InspectExe(typeof(Rules).Assembly.Location,false).Name=="Windows AnyCPU","managed AnyCPU image is compatible with both Windows bitnesses");
        var bad=Path.Combine(root,"not-real.exe");File.WriteAllText(bad,"This is not a PE file");invalid=false;try{InstallerCompatibility.InspectExe(bad,true);}catch(IOException){invalid=true;}check(invalid,"renamed non-PE content is rejected before opening an installer");
    }
}
