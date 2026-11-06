using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task PackageManagerSmoke(string path)
    {
        settings.KeepLatestPackages=false;settings.DeleteInstalledPackages=false;settings.PromptInstalledPackageCleanup=false;packages=[];pendingInstallations=[];settings.Bindings=[];
        var root=Path.Combine(store.Root,"downloads");var exported=Path.Combine(store.Root,"export");Directory.CreateDirectory(root);Directory.CreateDirectory(exported);settings.DownloadFolder=root;
        var bytes=Encoding.UTF8.GetBytes("package-manager-fixture-only");var hash=Convert.ToHexString(SHA256.HashData(bytes));
        DownloadedPackage Make(string repo,string file,bool missing=false){var full=Path.Combine(root,file);if(!missing)File.WriteAllBytes(full,bytes);var item=new DownloadedPackage(repo,1,"v2.0.0",false,DateTimeOffset.UtcNow,file,full,root,bytes.Length,hash,"windows/x64"){DownloadedAt=DateTimeOffset.Now};packages.Add(item);return item;}
        var first=Make("example/editor","Editor-Setup-x64.exe");var second=Make("example/portable","Portable-win64.zip");Make("example/addon","languages.json");Make("example/missing","Missing-x64.msi",true);
        var app=new InstalledApp("fixture/editor","Editor","1.0.0",Path.Combine(store.Root,"apps"),ExecutablePath:Path.Combine(store.Root,"apps","Editor.exe"));var binding=new Binding{Repository=first.Repository,AppId=app.Id,ExecutablePath=app.ExecutablePath};
        pendingInstallations.Add(PendingInstallationRules.Create(first,app,binding));SavePackages();SavePendingInstallations();
        var installCount=0;packageManagerConfirm=_=>true;packageManagerExportFolder=()=>exported;packageManagerInstallSmoke=_=>{installCount++;return Task.CompletedTask;};
        var completion=new TaskCompletionSource();
        _=Dispatcher.BeginInvoke(async()=>{
            try{
                for(var attempt=0;attempt<100&&packageManager==null;attempt++)await Task.Delay(20);
                var form=packageManager??throw new IOException("Package manager did not open from task entry");
                T Find<T>(DependencyObject parent,string name)where T:FrameworkElement{if(parent is T element&&element.Name==name)return element;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){try{return Find<T>(VisualTreeHelper.GetChild(parent,i),name);}catch(InvalidOperationException){}}throw new InvalidOperationException(name);}
                var table=Find<DataGrid>(form,"DownloadedPackages");var search=Find<TextBox>(form,"PackageSearch");var filter=Find<ComboBox>(form,"PackageStatusFilter");var all=Find<CheckBox>(form,"PackageSelectAll");
                var install=form.Buttons.Children.OfType<Button>().Single(button=>button.Name=="PackageInstall");var export=form.Buttons.Children.OfType<Button>().Single(button=>button.Name=="PackageExport");var delete=form.Buttons.Children.OfType<Button>().Single(button=>button.Name=="PackageDelete");
                for(var attempt=0;attempt<100&&table.Items.Cast<PackageManagerRow>().Any(row=>row.State=="正在核对…");attempt++)await Task.Delay(20);
                var ready=table.Items.Count==4&&table.Items.Cast<PackageManagerRow>().Count(row=>row.Availability==PackageAvailability.Ready)==3;
                all.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));var selectedAll=table.Items.Cast<PackageManagerRow>().All(row=>row.Selected)&&!install.IsEnabled&&delete.IsEnabled&&export.IsEnabled;
                all.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));var cleared=table.Items.Cast<PackageManagerRow>().All(row=>!row.Selected);
                search.Text="editor";var searched=table.Items.Count==1;var selected=(PackageManagerRow)table.Items[0];selected.Selected=true;
                await ((Func<Task>)install.Tag!)();var installWorked=installCount==1&&File.Exists(first.Path);
                await ((Func<Task>)export.Tag!)();var exportedOriginal=File.Exists(Path.Combine(exported,first.AssetName))&&File.Exists(first.Path);
                search.Text="";filter.SelectedIndex=2;var missingFilter=table.Items.Count==1&&((PackageManagerRow)table.Items[0]).Availability==PackageAvailability.Missing;filter.SelectedIndex=0;
                var row=table.Items.Cast<PackageManagerRow>().Single(item=>item.Path==first.Path);row.Selected=true;
                packageManagerConfirm=_=>false;await ((Func<Task>)delete.Tag!)();var declined=File.Exists(first.Path)&&pendingInstallations.Count==1;packageManagerConfirm=_=>true;
                using(var held=taskCoordinator.Start("fixture-package-use",["file:"+first.Path])){await held.WaitAsync();await Task.Delay(1100);if(delete.IsEnabled)throw new IOException("Busy package delete remained enabled");}
                var busyProtected=true;
                _=Dispatcher.BeginInvoke(()=>{
                    var rules=Application.Current.Windows.OfType<Form>().Single(window=>window.Title=="Gitstar · 安装包保存规则");
                    var choices=rules.Fields.Children.OfType<ComboBox>().ToArray();choices[0].SelectedIndex=0;choices[1].SelectedIndex=1;
                    var save=rules.Buttons.Children.OfType<Button>().Single(button=>button.Content?.ToString()=="保存规则");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                },DispatcherPriority.ApplicationIdle);
                form.HeaderActions.Children.OfType<Button>().Single(button=>button.Name=="PackageStorageRules").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var ruleSaved=settings.KeepLatestPackages&&settings.PromptInstalledPackageCleanup&&!settings.DeleteInstalledPackages&&store.Get("settings",new Settings()).KeepLatestPackages;
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);form.UpdateLayout();
                var view=(FrameworkElement)form.Content;var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);
                var fits=table.ActualWidth<=form.ActualWidth-30&&table.Columns.Sum(column=>column.ActualWidth)<=table.ActualWidth+2&&form.Buttons.ActualWidth<=form.ActualWidth-30;
                await ((Func<Task>)delete.Tag!)();var deleted=packages.Count==3&&!File.Exists(first.Path)&&pendingInstallations.Count==0&&File.Exists(second.Path)&&File.Exists(Path.Combine(exported,first.AssetName));
                using(var reload=new LocalStore(store.Root))if(reload.Get(PackagesKey,new List<DownloadedPackage>()).Count!=3)throw new IOException("Package deletion not saved");
                form.Close();packageManagerConfirm=null;packageManagerExportFolder=null;packageManagerInstallSmoke=null;
                var passed=ready&&selectedAll&&cleared&&searched&&installWorked&&exportedOriginal&&missingFilter&&declined&&busyProtected&&ruleSaved&&fits&&deleted;
                File.WriteAllText(Path.ChangeExtension(path,".package-manager.json"),JsonSerializer.Serialize(new{Passed=passed,TasksEntryOpens=true,RecordsLoaded=ready,SelectAll=selectedAll,ClearSelection=cleared,RepositorySearch=searched,InstallUsesVerifiedFile=installWorked,ExportPreservesOriginalNameAndSource=exportedOriginal,MissingFilter=missingFilter,DeleteConfirmationRespected=declined,BusyFileProtected=busyProtected,StorageRuleSaved=ruleSaved,LayoutFits=fits,DeleteClearsOnlySelectedFileAndPendingEntry=deleted,RealInstallersStarted=false,RealUserDataChanged=false}));
                if(!passed)throw new IOException("Package management interaction regression");completion.SetResult();
            }catch(Exception ex){packageManager?.Close();completion.SetException(ex);}
        },DispatcherPriority.ApplicationIdle);
        _=Dispatcher.BeginInvoke(()=>{
            var tasks=taskWindow??throw new IOException("Task list did not open");tasks.HeaderActions.Children.OfType<Button>().Single(button=>button.Name=="PackageManagerEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        },DispatcherPriority.ApplicationIdle);
        TasksDialog();await completion.Task;
        var settingsCheck=new TaskCompletionSource();
        _=Dispatcher.BeginInvoke(()=>{
            Form? settingsForm=null;
            try{
                settingsForm=Application.Current.Windows.OfType<Form>().Single(window=>window.Title=="Gitstar · 设置");settingsForm.NavigateSection("安装包管理");settingsForm.UpdateLayout();
                IEnumerable<T> Elements<T>(DependencyObject root)where T:DependencyObject{if(root is T item)yield return item;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Elements<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
                var entry=Elements<Button>(settingsForm).Single(button=>button.Name=="PackageManagerEntry");
                var keep=Elements<CheckBox>(settingsForm).Single(box=>box.Content?.ToString()?.StartsWith("只保留各项目",StringComparison.Ordinal)==true);var original=keep.IsChecked;keep.IsChecked=!original;
                var opened=false;_=Dispatcher.BeginInvoke(()=>{opened=packageManager!=null;packageManager?.Close();},DispatcherPriority.ApplicationIdle);
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var preserved=keep.IsChecked==!original;keep.IsChecked=original;
                settingsForm.Close();var passed=opened&&preserved;File.WriteAllText(Path.ChangeExtension(path,".package-entry.json"),JsonSerializer.Serialize(new{Passed=passed,SettingsEntryOpens=opened,UnchangedManagerPreservesUnsavedSettings=preserved}));
                if(!passed)throw new IOException("Settings package manager entry regression");settingsCheck.SetResult();
            }catch(Exception ex){settingsForm?.Close();settingsCheck.SetException(ex);}
        },DispatcherPriority.ApplicationIdle);
        SettingsDialog();await settingsCheck.Task;
    }
}
