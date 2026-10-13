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
        check(WindowsServices.DownloadPath(release with{Repository="owner/other"},asset,folder)!=target,"different repositories with identical installer names get distinct flat filenames");
        check(WindowsServices.DownloadPath(release with{Tag="1.0.0.0"},asset,folder)==target,"equivalent version spellings share the same duplicate-download target");
        check(PackageRules.FlatFileName("owner/repo",new string('x',240)+".msi",release.Tag).EndsWith(".msi"),"long asset names cannot truncate MSI extension");
        check(PackageRules.FlatFileName("owner/repo",new string('x',240)+"A.msi",release.Tag)!=PackageRules.FlatFileName("owner/repo",new string('x',240)+"B.msi",release.Tag),"flattening long filenames does not merge distinct assets after shortening");
        var requests=0;using(var api=new GitHubClient(new(),new FakeHttp((request,ct)=>{requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}))){
            var path=await WindowsServices.Download(api,release,asset,folder,(_,_)=>{},CancellationToken.None);var package=new DownloadedPackage(release.Repository,release.Id,release.Tag,false,release.Published,asset.Name,path,folder,bytes.Length,asset.Digest![7..],"windows/x64");
            check(await PackageFiles.Matches(package)&&!Directory.EnumerateDirectories(folder).Any(),"flat download ownership is hash-verifiable without creating any child directory");
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
