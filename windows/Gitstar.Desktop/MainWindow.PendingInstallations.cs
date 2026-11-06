using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private List<PendingInstallation> pendingInstallations=[];
    private readonly HashSet<string> pendingInstallationBusy=[];
    private readonly Dictionary<string,string> pendingInstallationMessages=[];
    private readonly HashSet<string> unavailablePendingPackages=[];
    private Func<PendingInstallation,Task>? pendingInstallationSmokeAction;
    private string PendingInstallationsKey=>PackagesKey+"/pending-installations-v1";
    private void SavePendingInstallations()=>store.Set(PendingInstallationsKey,pendingInstallations);

    private void LoadPendingInstallations()
    {
        pendingInstallations=store.Get(PendingInstallationsKey,new List<PendingInstallation>());
        var importLegacy=!store.Get(PendingInstallationsKey+"/legacy-imported",false);
        // Older downloads have no pending-install flag. Recover ordinary bound packages, not batch archives.
        foreach(var package in packages.Where(package=>importLegacy&&!package.RetainForBatch)){
            if(pendingInstallations.Any(item=>item.Package.Path.Equals(package.Path,StringComparison.OrdinalIgnoreCase)))continue;
            if(PackagePreference.Select([new(0,package.AssetName,package.Size,"","")],System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)==null)continue;
            foreach(var binding in settings.Bindings.Where(binding=>binding.Repository.Equals(package.Repository,StringComparison.OrdinalIgnoreCase)&&package.Tag.StartsWith(binding.TagPrefix,StringComparison.Ordinal))){
                var app=InstalledVersion.Find(installed,binding.AppId);if(app==null||PackagePreference.Select([new(0,package.AssetName,package.Size,"","")],System.Runtime.InteropServices.RuntimeInformation.OSArchitecture,portable:app.Portable)==null)continue;
                PendingInstallationRules.Upsert(pendingInstallations,PendingInstallationRules.Create(package,app,binding));
            }
        }
        if(importLegacy){SavePendingInstallations();store.Set(PendingInstallationsKey+"/legacy-imported",true);}
        RefreshPendingInstallationRows();
        UpdatePendingDownloadBadge();
    }

    private PendingInstallation AddPendingInstallation(Card card,Release release,string path)
    {
        var package=packages.Single(item=>item.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
        var pending=PendingInstallationRules.Create(package,card.Update!.App,card.Update.Binding);
        PendingInstallationRules.Upsert(pendingInstallations,pending);SavePendingInstallations();
        var saved=pendingInstallations.Last(item=>item.Package.Path.Equals(path,StringComparison.OrdinalIgnoreCase)&&PendingInstallationRules.SameContext(item,pending));
        card.Transfer.Finish("继续安装");card.Transfer.Detail=path;
        RefreshPendingInstallationRows();UpdatePendingDownloadBadge();return saved;
    }

    private void ReconcilePendingInstallations(string? checkedRepository=null,IEnumerable<Release>? freshReleases=null,DateTimeOffset? checkStarted=null)
    {
        if(exit||quitting)return;
        var fresh=freshReleases?.ToArray();
        var removed=pendingInstallations.RemoveAll(item=>(
            PendingInstallationRules.Installed(item,settings.Bindings,installed,installedFresh)
            ||fresh!=null&&(checkStarted==null||item.Added<=checkStarted)&&item.Package.Repository.Equals(checkedRepository,StringComparison.OrdinalIgnoreCase)&&PendingInstallationRules.Superseded(item,fresh)));
        if(removed>0){SavePendingInstallations();RefreshPendingInstallationRows();}
        foreach(var entry in pendingInstallations){var transfer=TransferFor(PendingRelease(entry),true);if(!transfer.Active&&!transfer.NeedsDetection)transfer.Finish("继续安装");}
        UpdatePendingDownloadBadge();
    }

    private Release PendingRelease(PendingInstallation item)
    {
        var package=item.Package;
        return releases.FirstOrDefault(release=>release.Repository.Equals(package.Repository,StringComparison.OrdinalIgnoreCase)&&release.Id==package.ReleaseId)
            ??new(package.ReleaseId,package.Repository,package.Tag,package.Tag,"",package.Prerelease,package.Published,"https://github.com/"+package.Repository+"/releases",[new(0,package.AssetName,package.Size,"","sha256:"+package.Sha256)]);
    }
    private Card? PendingCard(PendingInstallation item)
    {
        var binding=PendingInstallationRules.CurrentBinding(item,settings.Bindings,installed);
        var app=PendingInstallationRules.CurrentApp(item,settings.Bindings,installed);
        if(binding==null||app==null||app.VersionNeedsReview||app.Portable!=item.App.Portable)return null;
        var release=PendingRelease(item);return new(){Release=release,Update=new(app,binding,release,release.Assets.FirstOrDefault(asset=>asset.Name==item.Package.AssetName)),Transfer=TransferFor(release,true)};
    }

    private async Task ContinuePendingInstallation(PendingInstallation item)
    {
        if(exit||quitting||pendingInstallationBusy.Contains(item.Id))return;
        var card=PendingCard(item);
        if(card==null){model.Status="原软件关联或安装位置已变化，请在软件管理中核对匹配。";Reminders_Click(this,new RoutedEventArgs());return;}
        if(unavailablePendingPackages.Contains(item.Id)){await DownloadCard(card,true);return;}
        pendingInstallationBusy.Add(item.Id);RefreshPendingInstallationRows();
        try{
            await Execute("安装",item.App.Name+" · "+item.Package.Tag,async ct=>{
                if(!await PackageFiles.Matches(item.Package,ct)){
                    unavailablePendingPackages.Add(item.Id);pendingInstallationMessages[item.Id]="安装包不存在或内容已改变，请重新下载。";model.Status=pendingInstallationMessages[item.Id];return;
                }
                if(smokeMode&&pendingInstallationSmokeAction!=null){await pendingInstallationSmokeAction(item);return;}
                await ScanMatchedAsync();ReconcilePendingInstallations();
                if(pendingInstallations.All(entry=>entry.Id!=item.Id))return;
                if(PendingInstallationRules.Installed(item,settings.Bindings,installed,installedFresh))return;
                ct.ThrowIfCancellationRequested();card=PendingCard(item)??throw new IOException("软件关联已变化，请重新核对安装位置。");
                var package=item.Package.Path;var state=card.Transfer;
                if(package.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){
                    var platform=await Task.Run(()=>InstallerCompatibility.InspectExe(package,Environment.Is64BitOperatingSystem),ct);
                    if(!platform.Compatible){unavailablePendingPackages.Add(item.Id);pendingInstallationMessages[item.Id]="安装包与当前 Windows 架构不兼容，请重新下载。";return;}
                }
                if(package.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)&&!Rules.IsCompatibleMsiTemplate(await Task.Run(()=>WindowsServices.MsiPlatform(package),ct),Environment.Is64BitOperatingSystem))throw new IOException("MSI 与当前 Windows 架构不兼容，请重新选择下载文件。");
                if(PortableUpdateRules.ReplaceFiles(card.Update!.App,package)){
                    string? extracted=null;if(package.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))extracted=await Task.Run(()=>WindowsServices.ExtractPortable(package),ct);
                    state.Finish("替换后核对版本",true);await PortableUpdateDialog(card,package,extracted,ct:ct);
                }else{
                    ct.ThrowIfCancellationRequested();var process=Process.Start(new ProcessStartInfo(package){UseShellExecute=true})??throw new IOException("无法跟踪安装器，请安装后重新核对版本。");
                    state.Stage("安装器运行中");await ObserveInstaller(process,card,settings.Account,ct,package);
                }
                pendingInstallationMessages[item.Id]="尚未确认达到目标版本，可继续处理或重新检测。";
            },resources:["file:"+item.Package.Path,"app:"+card.Update!.App.Id],transfer:card.Transfer);
        }finally{
            pendingInstallationBusy.Remove(item.Id);ReconcilePendingInstallations();RefreshPendingInstallationRows();
        }
    }

    private int PendingDownloadCount=>pendingInstallations.Count+(downloadDecisions?.Fields.Children.OfType<Border>().Count(row=>row.Tag is DownloadDecisionRow)??0);
    private void UpdatePendingDownloadBadge()
    {
        if(TasksPendingDot==null)return;
        TasksPendingDot.Visibility=PendingDownloadCount>0?Visibility.Visible:Visibility.Collapsed;
        TasksButton.ToolTip=PendingDownloadCount>0?$"{PendingDownloadCount} 项下载待处理；已保存的安装包可继续安装。":"任务记录";
    }
}
