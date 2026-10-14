using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Gitstar.Core;
using Gitstar.Desktop;

static class DownloadPreferencesChecks
{
    public static async Task Run(Action<bool,string> check,string parent)
    {
        var folder=Path.Combine(parent,"flat-downloads");Directory.CreateDirectory(folder);var bytes=Encoding.UTF8.GetBytes("package-content");var asset=new Asset(22,"setup-x64.exe",bytes.Length,"","sha256:"+Convert.ToHexString(SHA256.HashData(bytes)));var release=new Release(11,"owner/repo","v1.0.0","","",false,DateTimeOffset.Now,"",[asset]);var target=WindowsServices.DownloadPath(release,asset,folder);
        check(Path.GetDirectoryName(target)==folder&&Path.GetExtension(target)==".exe","downloads save directly in configured directory and preserve installer extension");
        check(Path.GetFileName(target)==asset.Name&&WindowsServices.DownloadPath(release with{Repository="owner/other",Tag="v2.0"},asset,folder)==target,"downloads preserve exact Release attachment name without repository version or hash additions");
        check(WindowsServices.DownloadPath(release with{Tag="1.0.0.0"},asset,folder)==target,"equivalent version spellings share the same duplicate-download target");
        check(PackageRules.OriginalFileName(new string('x',240)+".msi")==new string('x',240)+".msi","long valid Release filenames are not shortened or suffixed");
        check(PackageRules.OriginalFileName("中文 安装包 v1.2.3.exe")=="中文 安装包 v1.2.3.exe","Release filename keeps Unicode spaces case and punctuation");
        foreach(var name in new[]{"Word-Formatter-Pro.v2.7.8.exe","DNSSpeedTester-x64.exe","ProxyBridge-Setup-4.1.1.exe","EMS.Simulate_6.1.0_x64_zh-CN.msi"})check(Path.GetFileName(WindowsServices.DownloadPath(release,asset with{Name=name},folder))==name,"requested EXE/MSI example keeps its exact Release attachment name");
        foreach(var name in new[]{"../outside.exe",@"dir\outside.exe","C:outside.exe","CON.exe","trailing.exe ","trailing.exe.","",new string('x',256)}){var rejected=false;try{PackageRules.OriginalFileName(name);}catch(ArgumentException){rejected=true;}check(rejected,"unsafe or invalid Windows attachment name is rejected without silent rewriting");}
        var item=new ReleaseDownload(release,asset);check(PackageRules.OriginalNameConflicts([item,item with{Release=release with{Repository="other/repo"}}]).SequenceEqual(new[]{asset.Name}),"cross-repository original-name collision requires a separate selection or directory");
        check(PackageRules.OriginalNameConflicts([item,item with{Release=release with{Tag="v2.0.0"}}]).Length==1&&PackageRules.OriginalNameConflicts([item,item with{Release=release with{Tag="1.0.0.0"}}]).Length==0,"different versions with same original name conflict while equivalent versions can deduplicate");
        var requests=0;using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}))){
            var path=await WindowsServices.Download(api,release,asset,folder,(_,_)=>{},CancellationToken.None);var package=new DownloadedPackage(release.Repository,release.Id,release.Tag,false,release.Published,asset.Name,path,folder,bytes.Length,asset.Digest![7..],"windows/x64");
            check(await PackageFiles.Matches(package)&&!Directory.EnumerateDirectories(folder).Any(),"flat download ownership is hash-verifiable without creating any child directory");
            var legacy=Path.Combine(folder,PackageRules.LegacyFlatFileName(release.Repository,asset.Name,release.Tag));File.WriteAllBytes(legacy,bytes);check(await PackageFiles.Matches(package with{Path=legacy}),"legacy generated filenames remain hash-verifiable for existing download records");File.Delete(legacy);
            File.WriteAllText(target,"unrelated");var refused=false;try{await WindowsServices.Download(api,release with{Repository="other/repo"},asset,folder,(_,_)=>{},CancellationToken.None);}catch(IOException){refused=true;}check(refused&&File.ReadAllText(target)=="unrelated","different-content original-name collision preserves existing file");File.WriteAllBytes(target,bytes);
            await WindowsServices.Download(api,release,asset,folder,(_,_)=>{},CancellationToken.None,replaceManaged:ct=>PackageFiles.Matches(package,ct),forceDownload:true);
            check(requests==2,"explicit same-version replacement downloads again even when cached digest already matches");
            check(PackageRules.SameVersion(package,release with{Tag="1.0.0.0"},asset),"same-version policy compares version numbers and asset ownership");
            check(!PackageRules.SameVersion(package,release with{Repository="owner/other"},asset)&&!PackageRules.SameVersion(package,release with{Tag="2.0.0"},asset),"same-version setting cannot authorize replacing another repository or version");
        }
        var settings=new Settings{DownloadFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","Gitstar"),SameVersionDownload=99};PathPreferences.Migrate(settings);
        check(settings.DownloadFolder.EndsWith("Gitstar_Download")&&settings.SameVersionDownload==0,"legacy default download location migrates to Gitstar_Download and invalid policy resets to ask");
        var custom=new Settings{DownloadFolder=folder,SameVersionDownload=2};PathPreferences.Migrate(custom);check(custom.DownloadFolder==folder&&custom.SameVersionDownload==2,"custom download folders and skip setting survive preference migration");
        check(PathPreferences.ProjectPath(@"D:\Agant\Github-Source\Gitstar")==@"D:\Agant\Github_Source\Gitstar"&&PathPreferences.ProjectPath(@"D:\Agant\Github-Releases\Gitstar")==@"D:\Agant\Github_Releases\Gitstar","configured source and release paths follow underscore directory rename");
        check(PathPreferences.ProjectPath(@"D:\Agant\Github-Source-other\Gitstar")==@"D:\Agant\Github-Source-other\Gitstar","directory migration leaves similarly named unrelated paths unchanged");
    }
}
