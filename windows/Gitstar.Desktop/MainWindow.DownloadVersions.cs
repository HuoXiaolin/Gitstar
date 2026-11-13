using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed record PackageSelection(Release Release,List<Asset> Assets,string Root,bool Historical) { public Asset Asset=>Assets[0];public long? InstallAssetId {get;init;} }
    private void RememberPackageSelection(Release release,DownloadPurpose purpose,IEnumerable<Asset> downloaded)
    {
        DownloadSelectionRules.Remember(settings,release,purpose,downloaded);Save();
    }
    private Task<PackageSelection?> ChoosePackage(Card card,string? previewPath=null)=>ChoosePackage(card,(page,ct)=>api.DownloadVersions(card.Repository,page,ct),ct=>api.LatestDownloadVersion(card.Repository,ct),(id,ct)=>api.DownloadVersionById(card.Repository,id,ct),previewPath);
    private async Task<PackageSelection?> ChoosePackage(Card card,Func<int,CancellationToken,Task<GitHubClient.DownloadVersionPage>> readPage,Func<CancellationToken,Task<Release?>> readLatest,Func<long,CancellationToken,Task<Release?>> readVersion,string? previewPath,Action<Form,ComboBox,ListBox,Button,Button>? inspect=null)
    {
        var install=card.Update!=null;var purpose=DownloadSelectionRules.Purpose(install,card.Update?.App.Portable==true);
        var preference=DownloadSelectionRules.Find(settings,card.Repository,purpose,packages);
        var f=new Form(this,card.Action,700,install?520:620);f.Label(card.Repository+(install?" · "+card.Release.Tag:""));
        var selectionLabel=new TextBlock{Text="当前选择",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0),FontWeight=FontWeights.SemiBold};
        var selectionText=new TextBlock{Name="CurrentPackageSelection",Text="尚未选择文件",VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};NameScope.SetNameScope(f,new NameScope());f.RegisterName(selectionText.Name,selectionText);
        var selectionRow=new Grid();selectionRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});selectionRow.ColumnDefinitions.Add(new());selectionRow.Children.Add(selectionLabel);Grid.SetColumn(selectionText,1);selectionRow.Children.Add(selectionText);
        var selectionBox=new Border{Child=selectionRow,Padding=new Thickness(12,10,12,10),Margin=new Thickness(18,0,18,4),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8)};selectionBox.SetResourceReference(Border.BackgroundProperty,"Surface");selectionBox.SetResourceReference(Border.BorderBrushProperty,"Edge");
        f.FixedSummary(selectionBox);
        selectionBox.MouseLeftButtonUp+=(_,_)=>{var details=new Form(f,"当前选择的下载文件",700,480);details.Label(selectionText.ToolTip as string??selectionText.Text);details.Action("关闭",()=>{details.Close();return Task.CompletedTask;});details.ShowDialog();};
        using var lifetime=new CancellationTokenSource();var closed=false;f.Closed+=(_,_)=>{closed=true;ApplicationExit.Cancel(lifetime);};
        var channel=ChannelFilter.SelectedIndex;var known=new List<Release>{card.Release};var current=card.Release;Release? latest=null;
        var version=f.Select("选择版本",[]);
        if(install)((FrameworkElement)f.Fields.Children[f.Fields.Children.Count-2]).Visibility=Visibility.Collapsed;
        f.Fields.Children.Remove(version);var versionRow=new Grid{Margin=new Thickness(0,7,0,16),Visibility=install?Visibility.Collapsed:Visibility.Visible};versionRow.ColumnDefinitions.Add(new());versionRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});version.Margin=new Thickness(0);versionRow.Children.Add(version);
        var more=new Button{Content="加载更多版本",Margin=new Thickness(12,0,0,0),MinHeight=40};Grid.SetColumn(more,1);versionRow.Children.Add(more);f.Fields.Children.Add(versionRow);
        var note=new TextBlock{Text=install?"": "正在读取最新版本…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),FontSize=12,Visibility=install?Visibility.Collapsed:Visibility.Visible};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(note);
        f.Label(install?"选择安装包":"选择下载文件（支持多选）");CheckBox? selectAll=null;var selectingAll=false;
        var choices=new ListBox{SelectionMode=install?SelectionMode.Single:SelectionMode.Multiple,MinHeight=90,MaxHeight=190,Margin=new Thickness(0,4,0,12)};
        choices.SetResourceReference(Control.BackgroundProperty,"Surface");choices.SetResourceReference(Control.ForegroundProperty,"Ink");choices.SetResourceReference(Control.BorderBrushProperty,"Edge");var fileStyle=new Style(typeof(ListBoxItem));fileStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));fileStyle.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Surface")));fileStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));var chosenStyle=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};chosenStyle.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("AccentSoft")));fileStyle.Triggers.Add(chosenStyle);choices.ItemContainerStyle=fileStyle;
        if(!install){var check=new FrameworkElementFactory(typeof(CheckBox));check.SetBinding(CheckBox.ContentProperty,new System.Windows.Data.Binding());check.SetBinding(CheckBox.IsCheckedProperty,new System.Windows.Data.Binding("IsSelected"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1),Mode=System.Windows.Data.BindingMode.TwoWay});check.SetValue(CheckBox.MarginProperty,new Thickness(4,6,4,6));choices.ItemTemplate=new DataTemplate{VisualTree=check};}
        f.Fields.Children.Add(choices);
        if(!install){selectAll=new CheckBox{Content="全选当前版本文件",IsThreeState=true,Margin=new Thickness(0,0,0,10)};selectAll.Click+=(_,_)=>{if(selectingAll)return;if(choices.SelectedItems.Count==choices.Items.Count)choices.UnselectAll();else choices.SelectAll();};f.Fields.Children.Add(selectAll);}
        var folder=f.Text("保存目录",settings.DownloadFolder);
        List<Release> ordered=[];List<Asset> assets=[];var filling=false;var fillingAssets=false;var loading=false;var nextPage=1;var hasMore=true;var chosenAssets=new Dictionary<long,HashSet<long>>();Button download=null!;
        CheckBox? installAfter=null;ComboBox? installFile=null;var installChoices=new List<Asset>();
        if(!install){installAfter=f.Check("下载后安装，并自动关联到本机软件管理");installAfter.Name="InstallAfterDownload";f.RegisterName(installAfter.Name,installAfter);installAfter.ToolTip="可选择一个 Windows EXE/MSI。安装后核对实际路径与版本；无法唯一识别时保留待确认入口。";installFile=f.Select("用于安装的文件（其他所选文件只保存）",[]);installFile.Name="InstallAfterDownloadFile";f.RegisterName(installFile.Name,installFile);var installLabel=(FrameworkElement)f.Fields.Children[f.Fields.Children.Count-2];f.Fields.Children.Remove(installAfter);f.Fields.Children.Remove(installLabel);f.Fields.Children.Remove(installFile);selectionBox.Child=null;var summary=new StackPanel();summary.Children.Add(selectionRow);installAfter.Margin=new Thickness(0,8,0,0);summary.Children.Add(installAfter);summary.Children.Add(installLabel);installFile.Margin=new Thickness(0,5,0,0);summary.Children.Add(installFile);selectionBox.Child=summary;installFile.Visibility=installLabel.Visibility=Visibility.Collapsed;installAfter.Checked+=(_,_)=>RefreshInstallChoice();installAfter.Unchecked+=(_,_)=>RefreshInstallChoice();}
        f.Label(install?(card.Update!.App.Portable?"便携软件无需安装：优先选择便携 ZIP 或主程序 EXE。下载后提示替换原程序文件，保留配置与个人数据，不启动安装器。":"EXE/MSI：下载后询问是否打开安装器。ZIP：解压后提示替换程序文件，保留配置与个人数据。") :"可勾选多个文件，依次下载；默认仅保存。勾选“下载后安装”可指定一个 EXE/MSI，完成下载后打开安装器；历史版本安装包会保留。");
        f.Label("优先选中本项目上次成功下载的文件类型；没有对应文件时按当前电脑推荐。可手动更改。");
        f.Action("选择目录",()=>{var dialog=new OpenFolderDialog{Title="选择安装包保存目录"};if(Directory.Exists(folder.Text))dialog.InitialDirectory=folder.Text;if(dialog.ShowDialog(f)==true)folder.Text=dialog.FolderName;return Task.CompletedTask;});
        void RefreshInstallChoice(){if(installAfter==null||installFile==null)return;var previous=(installFile.SelectedItem as string);installChoices=choices.SelectedItems.Cast<string>().Select(item=>assets[choices.Items.IndexOf(item)]).Where(NewInstallationRules.Installable).OrderByDescending(asset=>asset.Name.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)||asset.Name.Contains("setup",StringComparison.OrdinalIgnoreCase)||asset.Name.Contains("installer",StringComparison.OrdinalIgnoreCase)).ToList();installFile.ItemsSource=installChoices.Select(asset=>asset.Name).ToArray();installFile.SelectedIndex=Math.Max(0,installChoices.FindIndex(asset=>asset.Name==previous));installAfter.IsEnabled=installChoices.Count>0;if(installChoices.Count==0)installAfter.IsChecked=false;var enabled=installAfter.IsChecked==true;var visible=enabled&&installChoices.Count>1;installFile.Visibility=visible?Visibility.Visible:Visibility.Collapsed;if(installFile.Parent is Panel panel){var labelIndex=panel.Children.IndexOf(installFile)-1;if(labelIndex>=0)((FrameworkElement)panel.Children[labelIndex]).Visibility=visible?Visibility.Visible:Visibility.Collapsed;}if(download!=null)download.Content=install?"开始下载":enabled?$"下载并安装（{choices.SelectedItems.Count} 个文件）":$"下载所选文件（{choices.SelectedItems.Count}）";}
        void Assets()
        {
            fillingAssets=true;assets=!install&&ordered.Count==0?[]:current.Assets.Where(asset=>asset.Size>0).ToList();choices.ItemsSource=assets.Select(asset=>$"{asset.Name}  ·  {asset.Size/1048576.0:F1} MB").ToArray();
            if (!chosenAssets.TryGetValue(current.Id, out var selection))
            {
                var host = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
                var pattern = card.Update?.Binding.AssetPattern ?? "";
                if (card.Update is { App.Portable: false } update)
                {
                    var installer = DownloadSelectionRules.SelectInstaller(current, update.Binding, preference, host);
                    selection = installer == null ? [] : [installer.Id];
                }
                else
                {
                    var recommended = InstallerVersionRules.Recommended(current, assets, pattern, card.Update?.Binding.TagPrefix ?? "");
                    var preferred = PackagePreference.Select(recommended, host, pattern, card.Update?.App.Portable == true);
                    var previous = DownloadSelectionRules.Match(current with { Assets = recommended }, preference, host, pattern);
                    if (preferred != null && (previous.Count == 0 || previous.All(asset => !Rules.IsWindowsInstallCandidate(asset))
                        && preference?.FileNames.Any(name => Rules.IsWindowsInstallCandidate(new(0, name, 1, "", ""))) == true)) previous.Add(preferred);
                    selection = previous.Select(asset => asset.Id).ToHashSet();
                }
            }
            choices.SelectedIndex = assets.FindIndex(asset => selection.Contains(asset.Id));
            if (!install)
                foreach (var i in Enumerable.Range(0, assets.Count).Where(i => selection.Contains(assets[i].Id)))
                    if (!choices.SelectedItems.Contains(choices.Items[i])) choices.SelectedItems.Add(choices.Items[i]);
            fillingAssets=false;choices.IsEnabled=assets.Count>0;download.IsEnabled=!loading&&choices.SelectedIndex>=0;
            if(!loading)note.Text=assets.Count==0?"此版本没有可下载的安装包，请选择其他版本。":$"{current.Tag} · {current.Channel} · {current.Published.ToLocalTime():yyyy-MM-dd HH:mm}";
            if(!loading&&assets.Count>0&&choices.SelectedIndex<0)note.Text="未识别出适合当前电脑的安装包，可展开列表手动选择其他文件保存。";
            SelectionState();
        }
        void SelectionState(){var selected=choices.SelectedItems.Cast<string>().Select(item=>assets[choices.Items.IndexOf(item)]).ToArray();selectionLabel.Text=selected.Length>1?$"当前选择（{selected.Length} 项）":"当前选择";selectionText.Text=selected.Length==0?"尚未选择文件":string.Join("；",selected.Select(asset=>asset.Name));selectionText.ToolTip=selected.Length==0?"尚未选择文件":string.Join("\n",selected.Select(asset=>$"{asset.Name} · {asset.Size/1048576.0:F1} MB"))+"\n点击查看全部所选文件。";download.IsEnabled=!loading&&selected.Length>0;download.Content=install?"开始下载":$"下载所选文件（{selected.Length}）";if(selectAll!=null){selectingAll=true;selectAll.IsChecked=selected.Length==0?false:selected.Length==choices.Items.Count?true:null;selectingAll=false;}RefreshInstallChoice();}
        choices.SelectionChanged+=(_,_)=>{if(fillingAssets)return;chosenAssets[current.Id]=choices.SelectedItems.Cast<string>().Select(item=>assets[choices.Items.IndexOf(item)].Id).ToHashSet();SelectionState();};
        void Versions(long selected)
        {
            filling=true;ordered=DownloadVersionRules.Order(known,channel);version.ItemsSource=ordered.Select(release=>$"{release.Tag}  ·  {release.Channel}"+(release.Id==latest?.Id?"  ·  最新":"")).ToArray();version.SelectedIndex=ordered.FindIndex(release=>release.Id==selected);if(version.SelectedIndex<0&&ordered.Count>0)version.SelectedIndex=0;filling=false;
            if(version.SelectedIndex>=0)current=ordered[version.SelectedIndex];Assets();
        }
        version.SelectionChanged+=(_,_)=>{if(filling||version.SelectedIndex<0)return;current=ordered[version.SelectedIndex];Assets();};
        async Task Load(bool first)
        {
            if(loading||closed)return;loading=true;version.IsEnabled=more.IsEnabled=download.IsEnabled=false;note.Text=first?"正在读取最新版本…":"正在加载更多版本…";
            try{
                var pageTask=readPage(nextPage,lifetime.Token);var latestTask=first?readLatest(lifetime.Token):Task.FromResult<Release?>(null);await Task.WhenAll(pageTask,latestTask);if(closed)return;
                var page=await pageTask;var remoteLatest=await latestTask;
                if(first){known.Clear();if(remoteLatest!=null)known.Add(remoteLatest);}
                foreach(var release in page.Versions){if(known.All(item=>item.Id!=release.Id))known.Add(release);}
                // Keep a higher cached testing candidate outside page one only after fresh verification.
                if(first&&known.All(item=>item.Id!=card.Release.Id)&&DownloadVersionRules.Default(known.Append(card.Release with{IsLatest=false}),channel)?.Id==card.Release.Id){var verified=await readVersion(card.Release.Id,lifetime.Token);if(closed)return;if(verified!=null)known.Add(verified);}
                nextPage++;hasMore=page.Count==30;latest=DownloadVersionRules.Default(known,channel);var selected=first?latest?.Id??known.FirstOrDefault()?.Id??0:current.Id;
                loading=false;Versions(selected);note.Text=ordered.Count==0?"没有可下载的已发布版本。":note.Text;
                more.Content="加载更多版本";if(inspect!=null)_=f.Dispatcher.BeginInvoke(()=>inspect(f,version,choices,more,download));
            }catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
            catch(Exception ex){if(closed)return;loading=false;Versions(current.Id);note.Text="版本读取未完成，可重试："+ex.Message;more.Content="重试读取版本";hasMore=true;}
            finally{loading=false;if(!closed){version.IsEnabled=ordered.Count>0;more.IsEnabled=hasMore;download.IsEnabled=choices.SelectedIndex>=0&&ordered.Count>0;}}
        }
        more.Click+=async(_,_)=>await Load(nextPage==1);
        download=f.Action("开始下载",()=>{
            if(loading||choices.SelectedIndex<0||choices.SelectedIndex>=assets.Count)return Task.CompletedTask;
            var selected=choices.SelectedItems.Cast<string>().Select(item=>assets[choices.Items.IndexOf(item)]).ToList();var asset=selected[0];var root=Path.GetFullPath(folder.Text);
            if(selected.GroupBy(item=>PackageRules.OriginalFileName(item.Name),StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()>1))throw new IOException("所选附件包含同名文件，请分开下载到不同目录。");
            if(install&&!Rules.IsWindowsInstallCandidate(asset)&&!(card.Update!.App.Portable&&PackagePreference.Select([asset],System.Runtime.InteropServices.RuntimeInformation.OSArchitecture,portable:true)!=null)){DialogMessage.Show(f,"此安装包与当前电脑不兼容，请选择 Windows 安装包。","Gitstar");return Task.CompletedTask;}
            if(selected.Any(item=>string.IsNullOrEmpty(item.Digest))&&DialogMessage.Show(f,"部分文件未提供 SHA-256 摘要，这些文件只能核对大小。仍要下载所选文件吗？","Gitstar",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return Task.CompletedTask;
            long? installId=null;if(installAfter?.IsChecked==true){if(installFile!.SelectedIndex<0||installFile.SelectedIndex>=installChoices.Count)throw new IOException("请选择用于安装的 Windows EXE/MSI 文件。");installId=installChoices[installFile.SelectedIndex].Id;}
            settings.DownloadFolder=root;Save();f.Tag=new PackageSelection(current,selected,root,!install&&current.Id!=latest?.Id){InstallAssetId=installId};f.Close();return Task.CompletedTask;
        },true);
        latest=install?current:null;Versions(current.Id);if(!install){download.IsEnabled=false;f.Loaded+=async(_,_)=>await Load(true);}
        else if(inspect!=null)f.Loaded+=(_,_)=>_=f.Dispatcher.BeginInvoke(()=>inspect(f,version,choices,more,download));
        if(previewPath!=null)f.RenderPreview(previewPath);
        f.ShowDialog();await Task.CompletedTask;return f.Tag as PackageSelection;
    }
}
