using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed record PackageSelection(Release Release,Asset Asset,string Root,bool Historical);
    private Task<PackageSelection?> ChoosePackage(Card card,string? previewPath=null)=>ChoosePackage(card,(page,ct)=>api.DownloadVersions(card.Repository,page,ct),ct=>api.LatestDownloadVersion(card.Repository,ct),(id,ct)=>api.DownloadVersionById(card.Repository,id,ct),previewPath);
    private async Task<PackageSelection?> ChoosePackage(Card card,Func<int,CancellationToken,Task<GitHubClient.DownloadVersionPage>> readPage,Func<CancellationToken,Task<Release?>> readLatest,Func<long,CancellationToken,Task<Release?>> readVersion,string? previewPath,Action<Form,ComboBox,ComboBox,Button,Button>? inspect=null)
    {
        var install=card.Update!=null;var f=new Form(this,card.Action,700,install?520:620);f.Label(card.Repository+(install?" · "+card.Release.Tag:""));
        using var lifetime=new CancellationTokenSource();var closed=false;f.Closed+=(_,_)=>{closed=true;lifetime.Cancel();};
        var channel=ChannelFilter.SelectedIndex;var known=new List<Release>{card.Release};var current=card.Release;Release? latest=null;
        var version=f.Select("选择版本",[]);
        if(install)((FrameworkElement)f.Fields.Children[f.Fields.Children.Count-2]).Visibility=Visibility.Collapsed;
        f.Fields.Children.Remove(version);var versionRow=new Grid{Margin=new Thickness(0,7,0,16),Visibility=install?Visibility.Collapsed:Visibility.Visible};versionRow.ColumnDefinitions.Add(new());versionRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});version.Margin=new Thickness(0);versionRow.Children.Add(version);
        var more=new Button{Content="加载更多版本",Margin=new Thickness(12,0,0,0),MinHeight=40};Grid.SetColumn(more,1);versionRow.Children.Add(more);f.Fields.Children.Add(versionRow);
        var note=new TextBlock{Text=install?"": "正在读取最新版本…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),FontSize=12,Visibility=install?Visibility.Collapsed:Visibility.Visible};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(note);
        var choices=f.Select("选择安装包",[]);var folder=f.Text("保存目录",settings.DownloadFolder);
        f.Label(install?"EXE/MSI：下载后询问是否打开安装器。ZIP：解压后打开文件夹，迁移完成后重新关联。":"只保存安装包，不启动安装器。可切换其他版本，历史版本安装包会保留。");
        f.Action("选择目录",()=>{var dialog=new OpenFolderDialog{Title="选择安装包保存目录"};if(Directory.Exists(folder.Text))dialog.InitialDirectory=folder.Text;if(dialog.ShowDialog(f)==true)folder.Text=dialog.FolderName;return Task.CompletedTask;});
        List<Release> ordered=[];List<Asset> assets=[];var filling=false;var loading=false;var nextPage=1;var hasMore=true;
        Button download=null!;
        void Assets()
        {
            assets=!install&&ordered.Count==0?[]:current.Assets.Where(asset=>asset.Size>0).ToList();choices.ItemsSource=assets.Select(asset=>$"{asset.Name}  ·  {asset.Size/1048576.0:F1} MB").ToArray();choices.SelectedIndex=assets.Count>0?0:-1;
            if(install&&card.Update?.Asset is {} preferred){var index=assets.FindIndex(asset=>asset.Id==preferred.Id);if(index>=0)choices.SelectedIndex=index;}
            choices.IsEnabled=assets.Count>0;download.IsEnabled=!loading&&assets.Count>0;
            if(!loading)note.Text=assets.Count==0?"此版本没有可下载的安装包，请选择其他版本。":$"{current.Tag} · {current.Channel} · {current.Published.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
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
            finally{loading=false;if(!closed){version.IsEnabled=ordered.Count>0;more.IsEnabled=hasMore;download.IsEnabled=assets.Count>0&&ordered.Count>0;}}
        }
        more.Click+=async(_,_)=>await Load(nextPage==1);
        download=f.Action("开始下载",()=>{
            if(loading||choices.SelectedIndex<0||choices.SelectedIndex>=assets.Count)return Task.CompletedTask;
            var asset=assets[choices.SelectedIndex];var root=Path.GetFullPath(folder.Text);
            if(install&&!Rules.IsWindowsInstallCandidate(asset)){DialogMessage.Show(f,"此安装包与当前电脑不兼容，请选择 Windows 安装包。","Gitstar");return Task.CompletedTask;}
            if(string.IsNullOrEmpty(asset.Digest)&&DialogMessage.Show(f,"作者未提供 SHA-256 摘要，本次只能核对文件大小。仍要下载吗？","Gitstar",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return Task.CompletedTask;
            settings.DownloadFolder=root;Save();f.Tag=new PackageSelection(current,asset,root,!install&&current.Id!=latest?.Id);f.Close();return Task.CompletedTask;
        },true);
        latest=install?current:null;Versions(current.Id);if(!install){download.IsEnabled=false;f.Loaded+=async(_,_)=>await Load(true);}
        if(previewPath!=null)f.RenderPreview(previewPath);
        f.ShowDialog();await Task.CompletedTask;return f.Tag as PackageSelection;
    }
}
