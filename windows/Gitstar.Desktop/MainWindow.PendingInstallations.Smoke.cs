using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task PendingInstallationsSmoke(string path)
    {
        var root=Path.Combine(store.Root,"pending-packages");Directory.CreateDirectory(root);settings.Bindings=[];pendingInstallations=[];packages=[];installed=[];installedFresh=true;
        var bytes=Encoding.UTF8.GetBytes("verified-pending-installation-fixture");var hash=Convert.ToHexString(SHA256.HashData(bytes));
        Card Make(string repo,string name,bool portable){
            var app=new InstalledApp("fixture/"+name,name,"1.0.0",Path.Combine(store.Root,"apps",name),portable,ExecutablePath:Path.Combine(store.Root,"apps",name,name+".exe"),VersionSource:"程序产品版本");
            var binding=new Binding{AppId=app.Id,Repository=repo,ExecutablePath=app.ExecutablePath};settings.Bindings.Add(binding);installed.Add(app);
            var asset=new Asset(portable?2:1,name+"-x64.exe",bytes.Length,"","sha256:"+hash);var release=new Release(portable?2:1,repo,"v2.0.0",name,"",false,DateTimeOffset.UtcNow,"",[asset]);releases.Add(release);
            return new(){Release=release,Update=new(app,binding,release,asset),Transfer=TransferFor(release,true)};
        }
        var first=Make("example/editor","Editor",false);var second=Make("example/portable","Portable",true);
        foreach(var card in new[]{first,second}){var asset=card.Release.Assets.Single();var file=Path.Combine(root,asset.Name);File.WriteAllBytes(file,bytes);await RecordPackage(card.Release,asset,file,root,CancellationToken.None);AddPendingInstallation(card,card.Release,file);}
        OpenDownloadDecisionList();var form=downloadDecisions??throw new IOException("Missing persistent list");if(Environment.GetCommandLineArgs().Contains("--narrow"))form.Width=560;
        if(Environment.GetCommandLineArgs().Contains("--pending-selection")){await PendingSelectionSmoke(path);return;}
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);form.UpdateLayout();
        var initial=pendingInstallations.Count==2&&pendingInstallationRows.Count==2&&taskCoordinator.Count==0;
        var view=(FrameworkElement)form.Content;var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);
        var widthOK=pendingInstallationRows.Values.All(row=>{var layout=(Grid)row.View.Child;var action=(WrapPanel)layout.Children[1];var bounds=action.TransformToAncestor(layout).TransformBounds(new Rect(action.RenderSize));return bounds.Right<=layout.ActualWidth+1&&bounds.Left>=-1;});
        form.Close();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var closedRetains=pendingInstallations.Count==2&&downloadDecisions==null&&TasksPendingDot.Visibility==Visibility.Visible;
        using(var reloaded=new LocalStore(store.Root))if(reloaded.Get(PendingInstallationsKey,new List<PendingInstallation>()).Count!=2)throw new IOException("Closing did not retain durable records");
        pendingInstallations=[];LoadPendingInstallations();var loaded=pendingInstallations.Count==2;

        _=Dispatcher.BeginInvoke(()=>{
            var tasks=Application.Current.Windows.OfType<Form>().Single(window=>window.Title=="Gitstar · 任务记录");
            var entry=tasks.HeaderActions.Children.OfType<Button>().Single(button=>button.Name=="PendingDownloadsEntry");
            if(entry.Visibility!=Visibility.Visible||!entry.Content!.ToString()!.Contains('2'))throw new IOException("Persistent task entry was missing");
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        },DispatcherPriority.ApplicationIdle);
        TasksDialog();var reopened=downloadDecisions!=null&&pendingInstallationRows.Count==2;
        var temporary=ConfirmDownloadAction("example/duplicate","Duplicate.exe","同版本询问","重新下载","跳过",CancellationToken.None);
        downloadDecisions!.Close();if(await temporary)throw new IOException("Closing accepted an unrelated temporary question");
        var mixedCloseRetains=pendingInstallations.Count==2;
        OpenDownloadDecisionList();var resumed=0;pendingInstallationSmokeAction=_=>{resumed++;return Task.CompletedTask;};
        var original=pendingInstallations.Single(item=>item.App.Name=="Editor");await ContinuePendingInstallation(original);
        var installerExitNotCompletion=resumed==1&&pendingInstallations.Count==2&&taskCoordinator.Count==0;
        installed=installed.Select(app=>app.Id==first.Update!.App.Id?app with{Version="2.0.0"}:app).ToList();PlanUpdates();
        var installedRemoved=pendingInstallations.Count==1&&pendingInstallations[0].App.Name=="Portable";
        ReconcilePendingInstallations(second.Repository,[second.Release with{Id=33,Tag="v3.0.0",IsLatest=true}],DateTimeOffset.MinValue);
        var staleResponseIgnored=pendingInstallations.Count==1;
        ReconcilePendingInstallations(second.Repository,[second.Release with{Id=33,Tag="v3.0.0",IsLatest=true}]);
        var newerRemoved=pendingInstallations.Count==0&&Directory.GetFiles(root).Length==2;
        LoadPendingInstallations();var noResurrection=pendingInstallations.Count==0;

        installed=installed.Select(app=>app.Id==first.Update!.App.Id?app with{Version="1.0.0"}:app).ToList();
        var corrupt=AddPendingInstallation(first,first.Release,Path.Combine(root,first.Release.Assets[0].Name));File.WriteAllText(corrupt.Package.Path,"modified by user");
        OpenDownloadDecisionList();await ContinuePendingInstallation(corrupt);
        var changedFileBlocked=resumed==1&&pendingInstallations.Count==1&&unavailablePendingPackages.Contains(corrupt.Id);
        downloadDecisions!.Close();pendingInstallationSmokeAction=null;

        const string previewVersion = "2.0.0-beta.20261105.1";
        var stableName = new Asset(10, "Editor_2.0.0_x64-setup.exe", bytes.Length, "", "sha256:" + hash);
        var previewName = stableName with { Id = 11, Name = $"Editor_{previewVersion}_x64-setup.exe" };
        var offline = stableName with { Id = 12, Name = "Editor_2.0.0_x64-offline-setup.exe", Size = bytes.Length * 2, Digest = "sha256:" + new string('B', 64) };
        var portable = offline with { Id = 13, Name = "Editor_2.0.0_x64-portable.zip" };
        var aliasRelease = first.Release with { Id = 44, Assets = [stableName, previewName, offline, portable] };
        releases = [aliasRelease];
        RememberPackageSelection(aliasRelease, DownloadPurpose.Install, [stableName]);
        var aliasPath = Path.Combine(root, stableName.Name);
        File.WriteAllBytes(aliasPath, bytes);
        await RecordPackage(aliasRelease, stableName, aliasPath, root, CancellationToken.None);
        var app = first.Update!.App with { Version = previewVersion };
        installed = [app];
        installedFresh = false;
        var legacy = PendingInstallationRules.Create(packages.Single(package => package.Path == aliasPath), app, first.Update.Binding);
        pendingInstallations = [legacy];
        SavePendingInstallations();
        LoadPendingInstallations();
        OpenDownloadDecisionList();
        var aliasRetargeted = pendingInstallations.Single().TargetVersion == previewVersion && downloadDecisions != null;
        installedFresh = true;
        PlanUpdates();
        LoadPendingInstallations();
        var installedAliasCleared = pendingInstallations.Count == 0 && downloadDecisions == null && updates.Count == 0
            && TasksPendingDot.Visibility == Visibility.Collapsed && File.Exists(aliasPath);
        var rebuilt = stableName with { Digest = "sha256:" + new string('C', 64) };
        releases = [aliasRelease with { Assets = [rebuilt, previewName, offline, portable] }];
        PlanUpdates();
        var rebuiltShown = updates.Count == 1 && updates[0].Asset == rebuilt && updates[0].TargetVersion == aliasRelease.Tag;
        var rebuiltSelected = false;
        if (rebuiltShown)
        {
            var update = updates[0];
            await ChoosePackage(new() { Release = update.Release, Update = update },
                (_, _) => throw new InvalidOperationException("Local picker must not read a version page"),
                _ => throw new InvalidOperationException("Local picker must not request Latest"),
                (_, _) => throw new InvalidOperationException("Local picker must not request release details"),
                null, (picker, _, files, _, _) =>
                {
                    rebuiltSelected = files.SelectedItem is string selected && selected.StartsWith(rebuilt.Name + " ", StringComparison.Ordinal);
                    picker.Close();
                });
        }

        var passed=initial&&widthOK&&closedRetains&&loaded&&reopened&&mixedCloseRetains&&installerExitNotCompletion&&installedRemoved&&staleResponseIgnored&&newerRemoved&&noResurrection&&changedFileBlocked&&aliasRetargeted&&installedAliasCleared&&rebuiltShown&&rebuiltSelected;
        File.WriteAllText(Path.ChangeExtension(path,".pending-installations.json"),JsonSerializer.Serialize(new{Passed=passed,DownloadsReleaseTaskResources=initial,RowActionsFit=widthOK,ClosingRetainsEntries=closedRetains,DurableReload=loaded,TasksEntryReopens=reopened,TemporaryQuestionsDoNotDeleteInstallations=mixedCloseRetains,InstallerExitAloneKeepsEntry=installerExitNotCompletion,VerifiedInstalledRemovesEntry=installedRemoved,NewerReleaseRemovesEntryButPreservesFile=newerRemoved,NoLegacyResurrection=noResurrection,ChangedFileCannotExecute=changedFileBlocked,AliasRetargetedOnReload=aliasRetargeted,InstalledAliasClearsPendingBadgeAndDuplicateUpdate=installedAliasCleared,RebuiltPreferredInstallerStillOffered=rebuiltShown,UpdateCardAndPickerAgree=rebuiltSelected,RealInstallerStarted=false,RealNetworkUsed=false}));
        if(!passed)throw new IOException("Persistent installation regression");
    }
    private async Task PendingSelectionSmoke(string path)
    {
        var first=pendingInstallations[0];var other=first with{Id="fixture-stale",Package=first.Package with{Repository="example/reader",AssetName="Reader-Setup-x64.exe",Path=Path.Combine(first.Package.Root,"Reader-Setup-x64.exe")},App=first.App with{Id="fixture/reader",Name="Reader",ExecutablePath=Path.Combine(store.Root,"missing","Reader.exe")},Binding=new(){AppId="fixture/reader",Repository="example/reader",ExecutablePath=Path.Combine(store.Root,"missing","Reader.exe")}};
        File.Copy(first.Package.Path,other.Package.Path);packages.Add(other.Package);pendingInstallations.Add(other);SavePackages();SavePendingInstallations();RefreshPendingInstallationRows();
        var allFiles=packages.Select(package=>package.Path).ToArray();var form=downloadDecisions!;
        pendingSelectAll!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var selectAll=selectedPendingInstallations.Count==3;
        pendingSelectAll.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var deselectAll=selectedPendingInstallations.Count==0;
        pendingInstallationRows[first.Id].Selection.IsChecked=true;pendingDeleteSmokeConfirm=()=>false;pendingDeleteSelected!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var cancelPreserves=pendingInstallations.Count==3;
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);form.UpdateLayout();
        var view=(FrameworkElement)form.Content;var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);
        var layoutFits=pendingInstallationRows.Values.All(row=>{var layout=(Grid)row.View.Child;var actions=(WrapPanel)layout.Children[1];var bounds=actions.TransformToAncestor(layout).TransformBounds(new Rect(actions.RenderSize));return bounds.Right<=layout.ActualWidth+1&&bounds.Left>=-1&&row.Title.ActualWidth>=120;});
        pendingDeleteSmokeConfirm=()=>true;pendingDeleteSelected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var deletesOnlySelected=pendingInstallations.Count==2&&pendingInstallations.All(item=>item.Id!=first.Id)&&packages.Count==3&&allFiles.All(File.Exists);
        using(var reopened=new LocalStore(store.Root))if(reopened.Get(PendingInstallationsKey,new List<PendingInstallation>()).Count!=2)throw new IOException("Pending deletion not persisted");
        form.Close();LoadPendingInstallations();OpenDownloadDecisionList();var reloadKeepsDeletion=pendingInstallations.Count==2&&!pendingInstallations.Any(item=>item.Id==first.Id)&&selectedPendingInstallations.Count==0;
        var busy=pendingInstallations[0];pendingInstallationBusy.Add(busy.Id);RefreshPendingInstallationRows();
        pendingSelectAll!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var activeProtected=!pendingInstallationRows[busy.Id].Selection.IsEnabled&&!selectedPendingInstallations.Contains(busy.Id)&&selectedPendingInstallations.Count==1;
        pendingDeleteSelected!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));activeProtected&=pendingInstallations.Count==1&&pendingInstallations[0].Id==busy.Id;
        pendingInstallationBusy.Remove(busy.Id);RefreshPendingInstallationRows();pendingSelectAll!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));pendingDeleteSelected!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        LoadPendingInstallations();var lastClearsBadge=pendingInstallations.Count==0&&downloadDecisions==null&&TasksPendingDot.Visibility==Visibility.Collapsed&&allFiles.All(File.Exists);pendingDeleteSmokeConfirm=null;
        var passed=selectAll&&deselectAll&&cancelPreserves&&layoutFits&&deletesOnlySelected&&reloadKeepsDeletion&&activeProtected&&lastClearsBadge;
        File.WriteAllText(Path.ChangeExtension(path,".pending-selection.json"),JsonSerializer.Serialize(new{Passed=passed,SelectAll=selectAll,DeselectAll=deselectAll,CancelPreservesEntries=cancelPreserves,LayoutFits=layoutFits,OnlySelectedRemovedAndFilesRetained=deletesOnlySelected,ReloadDoesNotRestoreDeleted=reloadKeepsDeletion,ActiveInstallationProtected=activeProtected,EmptyListClearsBadgeAndCloses=lastClearsBadge,RealInstallersStarted=false}));
        if(!passed)throw new IOException("Pending selection regression");
    }
}
