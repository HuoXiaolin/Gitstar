using System.Diagnostics;
using System.Windows;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Func<CancellationToken,Task<List<InstalledApp>>>? newInstallScanSmoke;
    private Func<string,CancellationToken,Task>? newInstallLaunchSmoke;
    private PendingInstallation QueueRecentInstallation(DownloadedPackage package, Release? release = null)
    {
        var existing=pendingInstallations.FirstOrDefault(item=>item.Package.Path.Equals(package.Path,StringComparison.OrdinalIgnoreCase)&&item.Package.Sha256.Equals(package.Sha256,StringComparison.OrdinalIgnoreCase));if(existing!=null)return existing;
        var targets=settings.Bindings.Where(binding=>binding.Repository.Equals(package.Repository,StringComparison.OrdinalIgnoreCase)&&package.Tag.StartsWith(binding.TagPrefix,StringComparison.Ordinal)&&(binding.AssetPattern.Length==0||package.AssetName.Contains(binding.AssetPattern,StringComparison.OrdinalIgnoreCase))).Select(binding=>(Binding:binding,App:InstalledVersion.Find(installed,binding.AppId))).Where(item=>item.App!=null).ToArray();
        release ??= FindPackageRelease(package);
        var entry = targets.Length == 1 ? PendingInstallationRules.Create(package, targets[0].App!, targets[0].Binding, release) : NewInstallationRules.Create(package, release);
        PendingInstallationRules.Upsert(pendingInstallations,entry);SavePendingInstallations();RefreshPendingInstallationRows();UpdatePendingDownloadBadge();return entry;
    }
    private void ReconcileNewInstallations(List<InstalledApp> detected)
    {
        var removed=false;
        foreach (var pending in pendingInstallations.Where(item => item.NewInstall is { Started: true }).ToArray())
        {
            var item = ResolvePendingTarget(pending);
            var binding=NewInstallationRules.Bind(settings,item,detected);if(binding==null)continue;
            var app=InstalledVersion.Find(detected,binding.AppId);if(app==null)continue;
            // Persist the association before removing its durable retry entry.
            store.Set("settings",settings);
            pendingInstallations.RemoveAll(entry=>entry.Id==item.Id);pendingInstallationMessages.Remove(item.Id);removed=true;
            var state=TransferFor(PendingRelease(item),true);state.Finish("已安装并关联");state.Detail=app.Name+" · "+app.Version+" · "+app.ExecutablePath;model.Status="已自动关联 "+app.Name+" → "+binding.Repository;
            currentOperation.Value?.Results.Add("成功 · 自动关联 "+app.Name+" · "+app.ExecutablePath);
        }
        if(removed){SavePendingInstallations();RefreshPendingInstallationRows();RefreshPackageManager?.Invoke();UpdatePendingDownloadBadge();}
    }
    private async Task<List<InstalledApp>> ScanNewInstallationInventory(string account,CancellationToken ct)
    {
        var portable=settings.PortableApps.ToArray();var bindings=settings.Bindings.Select(binding=>binding.Snapshot()).ToArray();var known=installed.ToArray();
        var detected=smokeMode&&newInstallScanSmoke!=null?await newInstallScanSmoke(ct):await Task.Run(()=>WindowsServices.Scan(portable,bindings,known,ct),ct);
        ct.ThrowIfCancellationRequested();if(account!=PackagesKey||exit||quitting)throw new OperationCanceledException("账号或运行状态已改变。");SetInstalled(detected,true);PlanUpdates();Display();return detected;
    }
    private async Task ContinueNewInstallation(PendingInstallation item,bool reopenInstaller=false)
    {
        if(item.NewInstall==null||exit||quitting||!pendingInstallationBusy.Add(item.Id))return;
        var account=PackagesKey;var state=TransferFor(PendingRelease(item),true);RefreshPendingInstallationRows();
        try{
            await Execute(item.NewInstall.Started&&!reopenInstaller?"检测":"安装",item.Package.Repository+" · "+item.Package.Tag,async ct=>{
                state.Stage("核对已下载文件");await Task.Run(()=>ManagedPackageFiles.Verify(item.Package,ct),ct);
                var opening=!item.NewInstall.Started||reopenInstaller;
                if(opening){
                    if(!NewInstallationRules.Installable(new(0,item.Package.AssetName,item.Package.Size,"","")))throw new IOException("请选择 Windows EXE/MSI 安装包。");
                    if(item.Package.Path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){
                        var compatibility=await Task.Run(()=>InstallerCompatibility.InspectExe(item.Package.Path,Environment.Is64BitOperatingSystem),ct);if(!compatibility.Compatible)throw new IOException("安装包与当前 Windows 架构不兼容。");
                    }else if(!Rules.IsCompatibleMsiTemplate(await Task.Run(()=>WindowsServices.MsiPlatform(item.Package.Path),ct),Environment.Is64BitOperatingSystem))throw new IOException("安装包与当前 Windows 架构不兼容。");
                    state.Stage("记录安装前的软件状态");var before=await ScanNewInstallationInventory(account,ct);if(pendingInstallations.All(entry=>entry.Id!=item.Id))return;
                    if(!item.NewInstall.Started){item=item with{NewInstall=new(before.ToArray(),true)};var index=pendingInstallations.FindIndex(entry=>entry.Id==item.Id);if(index<0)return;pendingInstallations[index]=item;SavePendingInstallations();}
                    ct.ThrowIfCancellationRequested();state.Stage("等待安装器完成");pendingInstallationMessages[item.Id]="安装器已打开，完成后将检测软件并自动关联仓库。";RefreshPendingInstallationRows();
                    if(smokeMode&&newInstallLaunchSmoke!=null)await newInstallLaunchSmoke(item.Package.Path,ct);
                    else{using var process=Process.Start(new ProcessStartInfo(item.Package.Path){UseShellExecute=true})??throw new IOException("无法跟踪安装器，请完成安装后点击“核对安装”。");await process.WaitForExitAsync(ct);}
                }
                foreach(var delay in opening?new[]{0,2000,5000}:new[]{0}){
                    if(delay>0){state.Stage("等待软件完成安装");await Task.Delay(delay,ct);}
                    state.Stage("检测安装结果并关联仓库");var detected=await ScanNewInstallationInventory(account,ct);if(pendingInstallations.All(entry=>entry.Id!=item.Id))return;
                    if(NewInstallationRules.Candidates(item,detected).Length>1)break;
                }
                state.Finish("待核对安装");pendingInstallationMessages[item.Id]="尚未找到唯一、已验证的新安装程序；完成安装后点“核对安装”，或在“更多”中手动关联。";
            },resources:["file:"+item.Package.Path,"new-install:"+account],transfer:state);
        }finally{pendingInstallationBusy.Remove(item.Id);if(state.Active)state.Finish("待核对安装");RefreshPendingInstallationRows();}
    }
}
