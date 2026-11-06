using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Form? packageManager;
    private Action? RefreshPackageManager;
    private Func<string,bool>? packageManagerConfirm;
    private Func<string?>? packageManagerExportFolder;
    private Func<DownloadedPackage,Task>? packageManagerInstallSmoke;
    private string PackageStorageRule=>(settings.KeepLatestPackages?"只保留最新":"保留全部")+" · "+(settings.DeleteInstalledPackages?"安装后自动删除":settings.PromptInstalledPackageCleanup?"安装后询问删除":"安装后保留");

    private sealed class PackageManagerRow(DownloadedPackage package):INotifyPropertyChanged
    {
        public DownloadedPackage Package {get;}=package;
        public string Name=>Package.AssetName;
        public string Details=>Package.Repository+" · "+Package.Tag+" · "+(Package.DownloadedAt is {} time?time.ToLocalTime().ToString("MM-dd HH:mm")+" 下载":"下载时间未记录");
        public string Path=>Package.Path;
        public string Tooltip=>Package.AssetName+"\n"+Package.Repository+" · "+Package.Tag+"\n"+(Package.DownloadedAt is {} time?time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+" 下载":"下载时间未记录")+"\n"+Package.Path;
        public string Size=>CacheBytes(Package.Size);
        public PackageAvailability Availability {get;set;}=PackageAvailability.Unavailable;
        private string state="正在核对…";
        public string State {get=>state;set{state=value;PropertyChanged?.Invoke(this,new(nameof(State)));}}
        private bool selected;
        public bool Selected {get=>selected;set{if(selected==value)return;selected=value;PropertyChanged?.Invoke(this,new(nameof(Selected)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private void PackageManagerDialog(Window? owner=null)
    {
        if(packageManager is {} opened){opened.Activate();return;}
        var account=PackagesKey;var form=new Form(owner??this,"安装包管理",940,660);packageManager=form;
        if(smokeMode&&Environment.GetCommandLineArgs().Contains("--narrow"))form.Width=560;
        var rules=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,0,0,14)};rules.SetResourceReference(TextBlock.ForegroundProperty,"Muted");form.Fields.Children.Add(rules);
        var changeRules=form.Action("修改保存规则",()=>{EditPackageStorageRules(form);RefreshPackageManager?.Invoke();return Task.CompletedTask;});form.HeaderAction(changeRules);changeRules.Name="PackageStorageRules";
        var filters=new Grid{Margin=new Thickness(0,0,0,10)};filters.ColumnDefinitions.Add(new());filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});filters.RowDefinitions.Add(new(){Height=GridLength.Auto});filters.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var search=new TextBox{MinHeight=34,ToolTip="搜索文件名、仓库或版本",Name="PackageSearch"};System.Windows.Automation.AutomationProperties.SetName(search,"搜索已下载安装包");
        var searchLabel=new TextBlock{Text="搜索文件名、仓库或版本",FontSize=12,Margin=new Thickness(0,0,0,6)};filters.Children.Add(searchLabel);Grid.SetColumnSpan(searchLabel,2);Grid.SetRow(search,1);filters.Children.Add(search);
        var filter=new ComboBox{ItemsSource=new[]{"全部记录","文件可用","文件缺失 / 异常"},SelectedIndex=0,Width=150,MinHeight=34,Margin=new Thickness(10,0,0,0),Name="PackageStatusFilter"};Grid.SetRow(filter,1);Grid.SetColumn(filter,1);filters.Children.Add(filter);form.Fields.Children.Add(filters);
        var selection=new WrapPanel{Margin=new Thickness(0,0,0,8)};var all=new CheckBox{Content="全选当前结果",IsThreeState=true,Margin=new Thickness(0,0,16,0),Name="PackageSelectAll"};selection.Children.Add(all);
        var count=new TextBlock{VerticalAlignment=VerticalAlignment.Center,FontSize=12};count.SetResourceReference(TextBlock.ForegroundProperty,"Muted");selection.Children.Add(count);form.Fields.Children.Add(selection);
        var table=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,IsReadOnly=true,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=62,ColumnHeaderHeight=34,MinHeight=130,Name="DownloadedPackages",SelectionMode=DataGridSelectionMode.Single};
        var checkbox=new FrameworkElementFactory(typeof(CheckBox));checkbox.SetBinding(CheckBox.IsCheckedProperty,new System.Windows.Data.Binding(nameof(PackageManagerRow.Selected)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});checkbox.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);checkbox.SetValue(FrameworkElement.MarginProperty,new Thickness(0));
        table.Columns.Add(new DataGridTemplateColumn{Header="选择",CellTemplate=new DataTemplate{VisualTree=checkbox},Width=42,CanUserSort=false});
        var content=new FrameworkElementFactory(typeof(StackPanel));content.SetBinding(ToolTipProperty,new System.Windows.Data.Binding(nameof(PackageManagerRow.Tooltip)));
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(PackageManagerRow.Name)));name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);name.SetValue(TextBlock.FontSizeProperty,13d);name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);content.AppendChild(name);
        var details=new FrameworkElementFactory(typeof(TextBlock));details.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(PackageManagerRow.Details)));details.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,0));details.SetResourceReference(TextBlock.ForegroundProperty,"Muted");details.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);content.AppendChild(details);
        table.Columns.Add(new DataGridTemplateColumn{Header="安装包 / 来源",CellTemplate=new DataTemplate{VisualTree=content},Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=110,CanUserSort=false});
        var number=new Style(typeof(TextBlock));number.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Right));
        table.Columns.Add(new DataGridTextColumn{Header="大小",Binding=new System.Windows.Data.Binding(nameof(PackageManagerRow.Size)),Width=76,ElementStyle=number,CanUserSort=false});
        table.Columns.Add(new DataGridTextColumn{Header="状态",Binding=new System.Windows.Data.Binding(nameof(PackageManagerRow.State)),Width=126,CanUserSort=false});form.Fields.Children.Add(table);
        var selectedPath=new TextBlock{FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,10,0,0)};selectedPath.SetResourceReference(TextBlock.ForegroundProperty,"Muted");form.Fields.Children.Add(selectedPath);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,7,0,0),Name="PackageManagerMessage"};message.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");form.Fields.Children.Add(message);
        var rows=new List<PackageManagerRow>();var visible=new List<PackageManagerRow>();var changing=false;var working=false;var generation=0;
        Button install=null!,delete=null!,export=null!;
        PackageManagerRow[] Selected()=>visible.Where(row=>row.Selected).ToArray();
        bool Busy(PackageManagerRow row)=>taskCoordinator.Uses("file:"+row.Package.Path);
        void Update(){
            if(changing)return;changing=true;var chosen=Selected();all.IsChecked=chosen.Length==0?false:chosen.Length==visible.Count?true:null;changing=false;
            count.Text=$"共 {visible.Count} 项 · 已选 {chosen.Length} 项";
            install.IsEnabled=!working&&chosen.Length==1&&chosen[0].Availability==PackageAvailability.Ready&&!Busy(chosen[0])&&Rules.IsWindowsInstallCandidate(new(0,chosen[0].Name,chosen[0].Package.Size,"",""));
            install.ToolTip=chosen.Length!=1?"请选择一个 Windows 安装包；删除和导出支持多选。":"安装、继续安装或替换已关联的便携软件";
            delete.IsEnabled=export.IsEnabled=!working&&chosen.Length>0&&!chosen.Any(Busy);
            selectedPath.Text=chosen.Length==1?chosen[0].Path:chosen.Length>1?$"已选择 {chosen.Length} 个文件，支持批量删除和导出。":"勾选安装包后，可安装、删除或导出。";selectedPath.ToolTip=chosen.Length==1?chosen[0].Path:null;
        }
        void ApplyFilter(){var query=search.Text.Trim();visible=rows.Where(row=>(query.Length==0||(row.Name+" "+row.Package.Repository+" "+row.Package.Tag).Contains(query,StringComparison.OrdinalIgnoreCase))&&(filter.SelectedIndex==0||(filter.SelectedIndex==1)==(row.Availability==PackageAvailability.Ready))).ToList();table.ItemsSource=visible;Update();}
        async Task Reload(){
            if(account!=PackagesKey||ApplicationExit.IsExiting||!ReferenceEquals(packageManager,form))return;var ticket=++generation;
            var prior=rows.Where(row=>row.Selected).Select(row=>row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows=packages.AsEnumerable().Reverse().DistinctBy(package=>package.Path,StringComparer.OrdinalIgnoreCase).OrderByDescending(package=>package.DownloadedAt??DateTimeOffset.MinValue).Select(package=>new PackageManagerRow(package){Selected=prior.Contains(package.Path)}).ToList();
            foreach(var row in rows)row.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(PackageManagerRow.Selected))Update();};
            rules.Text="保存规则："+PackageStorageRule+"\n默认保存目录："+settings.DownloadFolder;rules.ToolTip=settings.DownloadFolder;ApplyFilter();
            var snapshot=rows.ToArray();var states=await Task.Run(()=>snapshot.Select(row=>ManagedPackageFiles.Inspect(row.Package)).ToArray());
            if(generation!=ticket||account!=PackagesKey||!ReferenceEquals(packageManager,form)||ApplicationExit.IsExiting)return;
            for(var i=0;i<snapshot.Length;i++){var row=snapshot[i];row.Availability=states[i].Availability;row.State=states[i].Availability==PackageAvailability.Ready&&pendingInstallations.Any(item=>item.Package.Path.Equals(row.Path,StringComparison.OrdinalIgnoreCase))?"待安装":states[i].Text;}ApplyFilter();
            if(Selected() is [var selected]){table.UpdateLayout();table.ScrollIntoView(selected);}
            if(rows.Count==0)message.Text="还没有下载记录。使用 Gitstar 下载安装包后，会自动出现在这里。";
        }
        bool Confirm(string text)=>smokeMode&&packageManagerConfirm!=null?packageManagerConfirm(text):DialogMessage.Show(form,text,"删除已下载安装包",MessageBoxButton.OKCancel)==MessageBoxResult.OK;
        async Task Run(Func<Task> action){if(working)return;if(account!=PackagesKey)throw new IOException("账号已变化，请重新打开安装包管理。");working=true;Update();try{await action();}finally{working=false;await Reload();Update();}}
        install=form.Action("安装 / 替换",()=>Run(async()=>{await InstallManagedPackage(form,Selected().Single().Package);}),true);install.Name="PackageInstall";
        export=form.Action("导出所选",()=>Run(async()=>{
            var selected=ManagedPackageFiles.ExportSelection(Selected().Select(row=>row.Package));
            string? folder;if(smokeMode&&packageManagerExportFolder!=null)folder=packageManagerExportFolder();else{var picker=new OpenFolderDialog{Title="导出安装包（复制文件，保持原名）"};folder=picker.ShowDialog(form)==true?picker.FolderName:null;}if(folder==null)return;
            await ManagePackageFiles(selected,account,false,folder,text=>message.Text=text);
        }));export.Name="PackageExport";
        delete=form.Action("删除所选",()=>Run(async()=>{
            var selected=Selected().Select(row=>row.Package).ToArray();if(!Confirm($"永久删除所选 {selected.Length} 个已下载文件及对应待安装记录？软件本身不会卸载。\n文件已缺失时，仅移除下载记录。\n\n"+string.Join("\n",selected.Take(8).Select(package=>package.AssetName))+(selected.Length>8?"\n…":"")))return;
            await ManagePackageFiles(selected,account,true,null,text=>message.Text=text);
        }));delete.Name="PackageDelete";delete.SetResourceReference(Control.ForegroundProperty,"DangerInk");
        form.Action("关闭",()=>{form.Close();return Task.CompletedTask;}).IsCancel=true;
        var refresh=form.Action("刷新",Reload);form.HeaderAction(refresh);refresh.Name="PackageRefresh";
        search.TextChanged+=(_,_)=>ApplyFilter();filter.SelectionChanged+=(_,_)=>ApplyFilter();
        all.Click+=(_,_)=>{if(changing)return;changing=true;var value=visible.Any(row=>!row.Selected);foreach(var row in visible)row.Selected=value;changing=false;Update();};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>Update();
        form.SizeChanged+=(_,_)=>{var narrow=form.ActualWidth<520;table.Columns[0].Width=narrow?36:42;table.Columns[2].Width=narrow?64:76;table.Columns[3].Width=narrow?112:126;};form.FitField(table);
        RefreshPackageManager=()=>ApplicationExit.Observe(Reload());
        form.Closed+=(_,_)=>{timer.Stop();generation++;if(ReferenceEquals(packageManager,form)){packageManager=null;RefreshPackageManager=null;}};
        form.Footnote("管理 Gitstar 记录的下载文件；导出为复制文件，保留发行包原名。");
        form.ContentRendered+=async(_,_)=>{timer.Start();await Reload();};Update();form.ShowDialog();
    }

    private async Task ManagePackageFiles(DownloadedPackage[] selected,string account,bool delete,string? folder,Action<string> status)
    {
        if(selected.Any(package=>taskCoordinator.Uses("file:"+package.Path)))throw new IOException("所选文件正在被任务使用，请等待或暂停对应任务。");
        var programs=installed.Select(app=>app.ExecutablePath).Concat(settings.Bindings.Select(binding=>binding.ExecutablePath)).Append(Environment.ProcessPath??"").ToArray();
        var completed=0;var errors=new List<string>();var transfer=new TransferState();transfer.Stage(delete?"核对并删除":"核对并导出",true);
        await Execute(delete?"安装包删除":"安装包导出",$"{selected.Length} 个文件",async ct=>{
            for(var i=0;i<selected.Length;i++){
                ct.ThrowIfCancellationRequested();if(account!=PackagesKey)throw new IOException("账号已变化，已停止处理。");var package=selected[i];transfer.Stage(delete?"核对并删除":"核对并导出",true);transfer.Detail=package.AssetName;transfer.Percentage=0;status($"{(delete?"删除":"导出")} {i+1}/{selected.Length}：{package.AssetName}");
                try{
                    if(!packages.Contains(package))throw new IOException("下载记录已变化，请刷新后重新选择。");
                    if(delete){
                        await Task.Run(()=>ManagedPackageFiles.Delete(package,programs,ct),ct);
                        if(account==PackagesKey){packages.RemoveAll(item=>item==package);pendingInstallations.RemoveAll(item=>item.Package.Path.Equals(package.Path,StringComparison.OrdinalIgnoreCase));SavePackages();SavePendingInstallations();RefreshPendingInstallationRows();UpdatePendingDownloadBadge();}
                        else{var prior=store.Get(account,new List<DownloadedPackage>());prior.RemoveAll(item=>item==package);store.Set(account,prior);var pendingKey=account+"/pending-installations-v1";var pending=store.Get(pendingKey,new List<PendingInstallation>());pending.RemoveAll(item=>item.Package.Path.Equals(package.Path,StringComparison.OrdinalIgnoreCase));store.Set(pendingKey,pending);}
                    }
                    else{
                        var position=i+1;var reporting=true;var progress=new Progress<long>(copied=>{if(!reporting||ApplicationExit.IsExiting)return;transfer.Stage("正在导出",false);transfer.Percentage=package.Size>0?Math.Min(100,100d*copied/package.Size):100;transfer.Detail=$"{position}/{selected.Length} · {package.AssetName} · {CacheBytes(copied)} / {CacheBytes(package.Size)}";status("正在导出："+transfer.Detail);});
                        try{await Task.Run(()=>ManagedPackageFiles.Export(package,folder!,ct,copied=>((IProgress<long>)progress).Report(copied)),ct);}finally{reporting=false;}
                    }
                    completed++;currentOperation.Value?.Results.Add("成功 · "+package.AssetName);
                }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){errors.Add(package.AssetName+"："+ex.Message);currentOperation.Value?.Results.Add("待处理 · "+errors[^1]);}
            }
            transfer.Finish(errors.Count==0?"完成":"部分文件待处理");status($"已{(delete?"删除":"导出 / 核对")} {completed}/{selected.Length} 项。"+(errors.Count>0?"\n"+string.Join("\n",errors.Take(3)):""));
        },resources:selected.Select(package=>"file:"+package.Path).ToArray(),transfer:transfer);
    }

    private async Task InstallManagedPackage(Window owner,DownloadedPackage package)
    {
        if(smokeMode&&packageManagerInstallSmoke!=null){await ManagedPackageFiles.Verify(package);await packageManagerInstallSmoke(package);return;}
        if(!Rules.IsWindowsInstallCandidate(new(0,package.AssetName,package.Size,"","")))throw new IOException("请选择适用于当前 Windows 的 EXE、MSI 或 ZIP 文件。");
        var pending=pendingInstallations.FirstOrDefault(item=>item.Package.Path.Equals(package.Path,StringComparison.OrdinalIgnoreCase));
        if(pending!=null){await ContinuePendingInstallation(pending);return;}
        string? linkedContext=null;
        var matches=settings.Bindings.Where(binding=>binding.Repository.Equals(package.Repository,StringComparison.OrdinalIgnoreCase)&&package.Tag.StartsWith(binding.TagPrefix,StringComparison.Ordinal)&&(!package.Prerelease||binding.IncludePrerelease)&&(binding.AssetPattern.Length==0||package.AssetName.Contains(binding.AssetPattern,StringComparison.OrdinalIgnoreCase))).Select(binding=>(Binding:binding,App:InstalledVersion.Find(installed,binding.AppId))).Where(item=>item.App!=null).ToArray();
        if(matches.Length>0){
            var chosen=0;if(matches.Length>1){var pick=new Form(owner,"选择安装位置",700,360);var target=pick.Select("同一仓库关联了多个软件，请选择目标",matches.Select(item=>item.App!.Name+" · "+item.App.ExecutablePath));var accepted=false;pick.Action("取消",()=>{pick.Close();return Task.CompletedTask;});pick.Action("继续",()=>{chosen=target.SelectedIndex;accepted=true;pick.Close();return Task.CompletedTask;},true);pick.ShowDialog();if(!accepted)return;}
            var match=matches[chosen];var item=PendingInstallationRules.Create(package,match.App!,match.Binding);
            if(PendingInstallationRules.Installed(item,settings.Bindings,installed,installedFresh)){
                if(match.App!.Portable){DialogMessage.Show(owner,"该安装位置已达到此安装包版本。可导出保留，或选择更新的版本。","已安装此版本");return;}
                linkedContext=$"已关联 {match.App!.Name}，本机版本 {match.App.Version}。将重新打开所选版本的安装器，由安装器决定修复、重装或降级。";
            }else{PendingInstallationRules.Upsert(pendingInstallations,item);SavePendingInstallations();RefreshPendingInstallationRows();await ContinuePendingInstallation(item);return;}
        }
        if(DialogMessage.Show(owner,$"打开 {package.AssetName}？\n\n仓库：{package.Repository}\n版本：{package.Tag}\n"+(package.Path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)?"此压缩包将解压到独立目录并打开文件夹。":linkedContext??"此安装包尚未关联本机软件，安装后可在“软件管理”中添加关联。"),"打开已下载安装包",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        if(linkedContext==null&&NewInstallationRules.Installable(new(0,package.AssetName,package.Size,"",""))){var fresh=QueueRecentInstallation(package);await ContinuePendingInstallation(fresh);return;}
        await Execute("安装","打开 "+package.AssetName,async ct=>{
            await Task.Run(()=>ManagedPackageFiles.Verify(package,ct),ct);ct.ThrowIfCancellationRequested();
            if(package.Path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){var check=await Task.Run(()=>InstallerCompatibility.InspectExe(package.Path,Environment.Is64BitOperatingSystem),ct);if(!check.Compatible)throw new IOException("此 EXE 与当前 Windows 架构不兼容。");}
            if(package.Path.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)&&!Rules.IsCompatibleMsiTemplate(await Task.Run(()=>WindowsServices.MsiPlatform(package.Path),ct),Environment.Is64BitOperatingSystem))throw new IOException("此 MSI 与当前 Windows 架构不兼容。");
            if(package.Path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)){var extracted=await Task.Run(()=>WindowsServices.ExtractPortable(package.Path),ct);ct.ThrowIfCancellationRequested();WindowsServices.Open(extracted);}
            else{ct.ThrowIfCancellationRequested();Process.Start(new ProcessStartInfo(package.Path){UseShellExecute=true});}
        },resources:["file:"+package.Path]);
    }
}
