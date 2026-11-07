using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task RecentInstallationSmoke(string path)
    {
        settings.Bindings=[];installed=[];packages=[];pendingInstallations=[];settings.DownloadSelections=[];settings.DownloadFolder=Path.Combine(store.Root,"downloads");
        var bytes=File.ReadAllBytes(Environment.ProcessPath!);var hash=Convert.ToHexString(SHA256.HashData(bytes));
        var setup=new Asset(1,"ExampleEditor-Setup-x64.exe",bytes.Length,"https://api.github.com/repos/example/ExampleEditor/releases/assets/1","sha256:"+hash);
        var release=new Release(101,"example/ExampleEditor","v2.1.2","ExampleEditor","",false,DateTimeOffset.UtcNow,"",[setup,setup with{Id=2,Name="ExampleEditor.exe"},setup with{Id=3,Name="SHA256SUMS.txt"}]);
        ChannelFilter.SelectedIndex=0;var defaultSave=false;var multi=false;var checkboxVisible=false;Exception? failed=null;
        var selection=await ChoosePackage(new(){Release=release},(_,_)=>Task.FromResult(new GitHubClient.DownloadVersionPage(1,[release])),_=>Task.FromResult<Release?>(release),(_,_)=>Task.FromResult<Release?>(null),null,(form,_,files,_,download)=>{
            try{
                if(Environment.GetCommandLineArgs().Contains("--narrow"))form.Width=560;
                var after=(CheckBox)form.FindName("InstallAfterDownload");var target=(ComboBox)form.FindName("InstallAfterDownloadFile");defaultSave=after.IsChecked!=true;files.SelectAll();after.IsChecked=true;multi=target.Items.Count==2&&target.SelectedItem?.ToString()==setup.Name;
                form.UpdateLayout();var view=(FrameworkElement)form.Content;var bounds=after.TransformToAncestor(view).TransformBounds(new Rect(after.RenderSize));checkboxVisible=bounds.Bottom<view.ActualHeight&&bounds.Top>=0;
                var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);_=((Func<Task>)download.Tag!)();
            }catch(Exception ex){failed=ex;form.Close();}
        });
        if(failed!=null)throw failed;if(selection?.InstallAssetId!=setup.Id)throw new IOException("After-download installer choice not returned");
        var root=selection.Root;Directory.CreateDirectory(root);var file=Path.Combine(root,setup.Name);File.WriteAllBytes(file,bytes);await RecordPackage(release,setup,file,root,CancellationToken.None);
        var program=Path.Combine(store.Root,"apps","ExampleEditor.exe");Directory.CreateDirectory(Path.GetDirectoryName(program)!);File.WriteAllBytes(program,bytes);
        var app=new InstalledApp("fixture/new-editor","ExampleEditor","2.1.2",Path.GetDirectoryName(program)!,ExecutablePath:program,VersionSource:"程序产品版本");
        var scans=0;var launches=0;newInstallScanSmoke=_=>Task.FromResult(++scans==1?new List<InstalledApp>():[app]);newInstallLaunchSmoke=(_,_)=>{launches++;return Task.CompletedTask;};
        var item=QueueRecentInstallation(packages.Single());await ContinuePendingInstallation(item);
        var bound=settings.Bindings.SingleOrDefault();var association=bound?.Repository==release.Repository&&bound.ExecutablePath==program&&pendingInstallations.Count==0&&launches==1;
        using(var saved=new LocalStore(store.Root))association&=saved.Get("settings",new Settings()).Bindings.Any(binding=>binding.Repository==release.Repository&&binding.ExecutablePath==program);
        var incomplete=QueueRecentInstallation(packages.Single()); // Existing association follows normal update handling; use a new repository for the unfinished-install fixture.
        pendingInstallations.RemoveAll(entry=>entry.Id==incomplete.Id);var unmatched=NewInstallationRules.Create(packages.Single() with{Repository="example/Unmatched"});pendingInstallations.Add(unmatched);SavePendingInstallations();newInstallScanSmoke=_=>Task.FromResult(new List<InstalledApp>());
        await ContinuePendingInstallation(unmatched);var preserved=pendingInstallations.Any(entry=>entry.Id==unmatched.Id&&entry.NewInstall is {Started:true});
        pendingInstallations=[];LoadPendingInstallations();var restartRetains=pendingInstallations.Any(entry=>entry.Id==unmatched.Id&&entry.NewInstall is {Started:true});
        newInstallScanSmoke=null;newInstallLaunchSmoke=null;
        var betaVersion = "0.6.67-beta.20261105.1";
        var betaAsset = setup with { Name = "ExampleEditor_" + betaVersion + "_x64-setup.exe" };
        var stableAsset = setup with { Id = 4, Name = "ExampleEditor_0.6.67_x64-setup.exe", Digest = "sha256:" + new string('B', 64) };
        var companion = release.Assets[2];
        var mixed = release with { Tag = "v0.6.67", Assets = [betaAsset, stableAsset, companion] };
        RememberPackageSelection(mixed,DownloadPurpose.Save,[betaAsset,companion]);var stableDefault=false;
        await ChoosePackage(new(){Release=mixed},(_,_)=>Task.FromResult(new GitHubClient.DownloadVersionPage(1,[mixed])),_=>Task.FromResult<Release?>(mixed),(_,_)=>Task.FromResult<Release?>(null),null,(form,_,files,_,_)=>{stableDefault=files.SelectedItems.Cast<string>().Any(file=>file.StartsWith(stableAsset.Name+" "))&&!files.SelectedItems.Cast<string>().Any(file=>file.StartsWith(betaAsset.Name+" "));form.Close();});
        var betaApp=app with{Version=betaVersion};var betaBinding=new Binding{Repository=release.Repository,AppId=app.Id,ExecutablePath=program};settings.Bindings=[betaBinding];installed=[betaApp];installedFresh=true;releases=[mixed];
        var betaPackage=packages[0] with{Tag=mixed.Tag,AssetName=betaAsset.Name,ReleaseId=mixed.Id};pendingInstallations=[PendingInstallationRules.Create(betaPackage,betaApp,betaBinding)];TransferFor(mixed,true).Finish("继续安装");PlanUpdates();
        var oldPendingCleared=pendingInstallations.Count==0&&updates.Count==1&&updates[0].Asset?.Name==stableAsset.Name&&TransferFor(mixed,true).Label=="";
        installed=[app with{Version="0.6.67"}];PlanUpdates();var finalStableComplete=updates.Count==0;
        var passed=defaultSave&&multi&&checkboxVisible&&association&&preserved&&restartRetains&&oldPendingCleared&&finalStableComplete&&stableDefault;
        File.WriteAllText(Path.ChangeExtension(path,".recent-install.json"),JsonSerializer.Serialize(new{Passed=passed,DefaultOnlySaves=defaultSave,MultiFileDownloadsInstallOnlyChosenFile=multi,InstallChoiceAlwaysVisible=checkboxVisible,AutomaticBindingUsesActualInstalledPath=association,UnconfirmedInstallRemainsPending=preserved,RestartRetainsSourceAndBaseline=restartRetains,InstalledPreviewPendingClearedAndStableInstallerRecommended=oldPendingCleared,InstalledStableHasNoUpdateReminder=finalStableComplete,RememberedPreviewReplacedByStableDefault=stableDefault,RealInstallerStarted=false}));
        if(!passed)throw new IOException("Recent installation regression");
    }
}
