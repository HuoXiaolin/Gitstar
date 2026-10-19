using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task DownloadVersionsSmoke(string path)
    {
        var bytes=Encoding.UTF8.GetBytes("historical-installer-fixture");var digest="sha256:"+Convert.ToHexString(SHA256.HashData(bytes));var now=DateTimeOffset.UtcNow;settings.DownloadFolder=Path.Combine(store.Root,"version-downloads");settings.KeepLatestPackages=true;ChannelFilter.SelectedIndex=0;
        Release Version(long id,string tag,int days,bool isLatest=false)=>new(id,"example/editor",tag,tag,"",false,now.AddDays(days),"",[new(id,$"Editor-{tag}-x64.exe",bytes.Length,"https://api.github.com/repos/example/editor/releases/assets/"+id,digest)]){IsLatest=isLatest};
        var current=Version(2,"v3.0.0",-5,true);var native=current.Assets[0];var arm=native with{Id=20,Name="Editor-v3.0.0-arm64.exe"};current=current with{Assets=[native with{Id=21,Name="Editor-arm64.delta"},arm,native,native with{Id=22,Name="Editor-v3.0.0-x64.msi"}]};var previous=Version(1,"v2.0.0",-15);var older=Version(3,"v1.0.0",-30);var card=new Card{Release=previous};var phase=0;var pages=new List<int>();var defaults=false;var linked=false;var kept=false;var manual=false;var requests=0;
        var selection=await ChoosePackage(card,(page,ct)=>{pages.Add(page);return Task.FromResult(new GitHubClient.DownloadVersionPage(page==1?30:1,page==1?[current,previous]:[older]));},ct=>Task.FromResult<Release?>(current),(id,ct)=>Task.FromResult<Release?>(null),null,(form,versions,assets,more,download)=>{
            if(phase++==0){
                defaults=versions.SelectedItem?.ToString()?.StartsWith(current.Tag)==true&&assets.SelectedItem?.ToString()?.Contains(native.Name)==true;
                form.UpdateLayout();var root=(FrameworkElement)form.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);
                assets.SelectedIndex=Enumerable.Range(0,assets.Items.Count).First(index=>assets.Items[index].ToString()!.Contains(arm.Name));manual=assets.SelectedItem!.ToString()!.Contains(arm.Name);more.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }else{
                kept=versions.SelectedItem!.ToString()!.StartsWith(current.Tag)&&assets.SelectedItem!.ToString()!.Contains(arm.Name)&&versions.Items.Count==3&&!more.IsEnabled;
                versions.SelectedIndex=Enumerable.Range(0,versions.Items.Count).First(index=>versions.Items[index].ToString()!.StartsWith(previous.Tag));linked=assets.SelectedItem!.ToString()!.Contains(previous.Assets[0].Name);
                requests++;_=((Func<Task>)download.Tag)();
            }
        });
        if(selection==null)throw new IOException("Version picker returned no selected package");
        api.Dispose();api=new(settings,new BulkSmokeTransport(bytes));var target=PackageTarget(selection.Release,selection.Asset,selection.Root);
        var saved=await WindowsServices.Download(api,selection.Release,selection.Asset,selection.Root,(_,_)=>{},CancellationToken.None,()=>{},_=>Task.FromResult(false),targetPath:target);
        await RecordPackage(selection.Release,selection.Asset,saved,selection.Root,CancellationToken.None,retainForBatch:selection.Historical);
        var recorded=packages.Single(package=>package.Path==saved);var rightVersion=selection.Release.Id==previous.Id&&recorded.Tag==previous.Tag&&recorded.ReleaseId==previous.Id&&File.ReadAllBytes(saved).SequenceEqual(bytes);
        var newer=new DownloadedPackage(current.Repository,current.Id,current.Tag,false,current.Published,current.Assets[0].Name,"fixture","fixture",bytes.Length,digest,"windows/x64");
        var protectedHistory=recorded.RetainForBatch&&!PackageRules.Outdated([recorded,newer]).Contains(recorded);var flat=!Directory.EnumerateDirectories(selection.Root).Any();var originalName=Path.GetFileName(saved)==selection.Asset.Name;
        var report=new{LatestDefault=defaults,NativeInstallerDefault=defaults,ManualOverrideAvailable=manual,VersionChangesPackages=linked,LoadMorePreservesSelection=kept,Pages=pages,SelectedVersionRecordedAndVerified=rightVersion,HistoricalPackageProtected=protectedHistory,FlatDirectory=flat,OriginalAttachmentName=originalName,DownloadSelections=requests};
        File.WriteAllText(Path.ChangeExtension(path,".download-versions.json"),JsonSerializer.Serialize(report));
        if(!defaults||!manual||!linked||!kept||!rightVersion||!protectedHistory||!flat||!originalName||!pages.SequenceEqual(new[]{1,2}))throw new IOException("Download version selection regression");
    }
}
