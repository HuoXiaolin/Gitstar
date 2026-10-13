using System.Security.Cryptography;
using System.Windows;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private List<DownloadedPackage> packages=[];
    private string PackagesKey=>"packages/"+(settings.Account.Length>0?settings.Account:"guest");
    private void SavePackages()=>store.Set(PackagesKey,packages);
    private string PackageTarget(Release release,Asset asset,string root)
    {
        return WindowsServices.DownloadPath(release,asset,root);
    }
    private async Task<bool> CanReplacePackage(Release release,Asset asset,string path,CancellationToken ct,bool replaceSameVersion=false)
    {
        if(!settings.KeepLatestPackages&&!replaceSameVersion)return false;
        if(installed.Any(a=>a.ExecutablePath.Equals(path,StringComparison.OrdinalIgnoreCase)))return false;
        var old=packages.FirstOrDefault(p=>p.Path.Equals(path,StringComparison.OrdinalIgnoreCase) && p.Repository.Equals(release.Repository,StringComparison.OrdinalIgnoreCase) && p.Prerelease==release.Prerelease && (p.Published<=release.Published||PackageRules.SameVersion(p,release,asset)));
        return old!=null && (settings.KeepLatestPackages||PackageRules.SameVersion(old,release,asset))&&await PackageFiles.Matches(old,ct);
    }
    private async Task RecordPackage(Release release,Asset asset,string path,string root,CancellationToken ct)
    {
        string digest;if(asset.Digest?.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)==true)digest=asset.Digest[7..];else{await using var stream=File.OpenRead(path);digest=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));}
        var family=PackageRules.Family(asset.Name);
        if(path.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)){try{var platform=WindowsServices.MsiPlatform(path).Split(';')[0].Trim().ToLowerInvariant();family="windows/"+(platform=="intel"?"x86":platform);}catch(IOException){}}
        packages.RemoveAll(p=>p.Path.Equals(path,StringComparison.OrdinalIgnoreCase));packages.Add(new(release.Repository,release.Id,release.Tag,release.Prerelease,release.Published,asset.Name,Path.GetFullPath(path),Path.GetFullPath(root),asset.Size,digest,family));SavePackages();
    }
    private async Task<bool> DeleteRecordedPackage(DownloadedPackage package,bool allowCurrent=false)
    {
        if(exit||quitting || (!allowCurrent && taskCoordinator.Uses("file:"+package.Path)))return false;
        if(installed.Any(a=>a.ExecutablePath.Length>0 && Path.GetFullPath(a.ExecutablePath).Equals(package.Path,StringComparison.OrdinalIgnoreCase)))return false;
        if(!PackageFiles.ExpectedPath(package))return false;
        if(File.Exists(package.Path)){
            try{if(!await PackageFiles.Matches(package))return false;
                if(exit||quitting || (!allowCurrent && taskCoordinator.Uses("file:"+package.Path)))return false;File.Delete(package.Path);
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){model.Status="安装包清理未完成："+ex.Message;return false;}
        }
        packages.RemoveAll(p=>p.Path.Equals(package.Path,StringComparison.OrdinalIgnoreCase));SavePackages();return true;
    }
    private async Task CleanupOldPackages()
    {
        if(!settings.KeepLatestPackages || exit||quitting)return;
        foreach(var package in PackageRules.Outdated(packages)){if(!settings.KeepLatestPackages)return;await DeleteRecordedPackage(package);}
    }
    private async Task CleanupInstalledPackages(string? currentPath=null)
    {
        if(exit||quitting || !installedFresh)return;
        var candidates=packages.Where(p=>File.Exists(p.Path) && (currentPath==null?!taskCoordinator.Uses("file:"+p.Path):p.Path.Equals(currentPath,StringComparison.OrdinalIgnoreCase)) && PackageRules.Installed(p,settings.Bindings,installed) && (settings.DeleteInstalledPackages || !p.Prompted)).ToArray();
        if(candidates.Length==0)return;
        if(!settings.DeleteInstalledPackages){
            if(!settings.PromptInstalledPackageCleanup)return;
            var decision=DialogMessage.Show(this,"已确认对应软件达到安装包版本。删除这些安装包以释放空间？\n\n"+string.Join("\n",candidates.Select(p=>p.AssetName)),"清理已安装的安装包",MessageBoxButton.YesNo);
            foreach(var item in candidates){var index=packages.FindIndex(p=>p.Path.Equals(item.Path,StringComparison.OrdinalIgnoreCase));if(index>=0)packages[index]=packages[index] with{Prompted=true};}SavePackages();
            if(decision!=MessageBoxResult.Yes)return;
        }
        foreach(var package in candidates)await DeleteRecordedPackage(package,currentPath!=null);
    }
}

