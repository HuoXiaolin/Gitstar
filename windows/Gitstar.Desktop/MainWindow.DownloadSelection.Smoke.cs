using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task DownloadSelectionSmoke(string path)
    {
        var bytes=Encoding.UTF8.GetBytes("remembered-download-fixture");var digest="sha256:"+Convert.ToHexString(SHA256.HashData(bytes));
        settings.DownloadFolder=Path.Combine(store.Root,"remembered-downloads");settings.DownloadSelections=[];packages=[];
        Asset A(long id,string name)=>new(id,name,bytes.Length,"https://api.github.com/repos/example/magpie/releases/assets/"+id,digest);
        Release R(long id,string tag,params Asset[] assets)=>new(id,"example/magpie",tag,"","",false,DateTimeOffset.UtcNow,"",assets.ToList());
        var cli=A(1,"magpie-cli-windows-amd64.exe");var gui=A(2,"magpie-windows-amd64.exe");var zip=A(3,"magpie-windows-amd64.zip");
        var first=R(1,"v0.1.100",cli,zip,gui,A(4,"magpie-windows-arm64.exe"));
        var app=new InstalledApp("portable/example","示例便携软件","0.1.0",@"D:\Applications\Example",true,ExecutablePath:@"D:\Applications\Example\magpie.exe");
        Card CardFor(Release release,bool portable)=>new(){Release=release,Update=portable?new(app,new(){AppId=app.Id,Repository=release.Repository},release,zip):null};
        async Task<PackageSelection?> Pick(Release release,bool portable,Action<Form,ListBox,Button> inspect)
        {
            Exception? failure=null;
            var result=await ChoosePackage(CardFor(release,portable),(_,_)=>Task.FromResult(new GitHubClient.DownloadVersionPage(1,[release])),_=>Task.FromResult<Release?>(release),(_,_)=>Task.FromResult<Release?>(null),null,(form,_,choices,_,download)=>{
                try{inspect(form,choices,download);}catch(Exception error){failure=error;form.Close();}
            });
            if(failure!=null)throw new IOException("Remembered download picker regression",failure);return result;
        }
        void Choose(ListBox list,string name){list.UnselectAll();list.SelectedIndex=Enumerable.Range(0,list.Items.Count).First(index=>list.Items[index].ToString()!.StartsWith(name+" "));}
        void Start(Button button)=>_=((Func<Task>)button.Tag)();
        void Require(bool condition,string message){if(!condition)throw new IOException(message);}

        var selected=await Pick(first,true,(form,choices,download)=>{
            Require(choices.SelectedItem!.ToString()!.StartsWith(zip.Name+" "),"Fresh portable picker should recommend the archive");
            Choose(choices,gui.Name);Start(download);
        })??throw new IOException("No initial package choice");
        api.Dispose();var transport=new BulkSmokeTransport(bytes);api=new(settings,transport);
        var saved=await WindowsServices.Download(api,selected.Release,selected.Asset,selected.Root,(_,_)=>{},CancellationToken.None);
        await RecordPackage(selected.Release,selected.Asset,saved,selected.Root,CancellationToken.None);
        RememberPackageSelection(selected.Release,DownloadPurpose.PortableUpdate,selected.Assets);
        // Re-read persisted data instead of relying on the current object instance.
        settings.DownloadSelections=store.Get("settings",new Settings()).DownloadSelections;
        var persisted=settings.DownloadSelections.Single();
        Require(persisted.FileNames.SequenceEqual(new[]{gui.Name}),"Successful download did not persist the selected GUI file");

        var next=first with{Id=2,Tag="v0.1.200"};
        await Pick(next,true,(form,choices,download)=>{
            Require(choices.SelectedItem!.ToString()!.StartsWith(gui.Name+" "),"Reopened picker reverted to ZIP or CLI");
            Require((form.FindName("CurrentPackageSelection") as TextBlock)?.Text==gui.Name,"Independent current-selection summary did not follow remembered file");
            form.UpdateLayout();var root=(FrameworkElement)form.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
            // Cancelling a different selection must not replace the successful preference.
            Choose(choices,cli.Name);form.Close();
        });
        Require(settings.DownloadSelections.Single().FileNames.SequenceEqual(new[]{gui.Name}),"Closing without download changed the remembered selection");
        var renamed=A(5,"magpie-v0.1.300-windows-x64.exe");var third=R(3,"v0.1.300",cli,zip,renamed);
        await Pick(third,true,(form,choices,download)=>{Require(choices.SelectedItem!.ToString()!.StartsWith(renamed.Name+" "),"Next-version filename was not matched");form.Close();});
        var missing=R(4,"v0.1.400",cli,zip);
        await Pick(missing,true,(form,choices,download)=>{Require(choices.SelectedItem!.ToString()!.StartsWith(zip.Name+" "),"Missing GUI should return to compatible portable recommendation");form.Close();});

        var msi=A(6,"Example-v1.0.0-x64.msi");var notes=A(7,"notes.json");var oldSave=R(5,"v1.0.0",msi,notes);
        RememberPackageSelection(oldSave,DownloadPurpose.Save,[msi,notes]);
        var newerMsi=A(8,"Example-v2.0.0-x64.msi");var saveRelease=R(6,"v2.0.0",A(9,"Example-v2.0.0-x64.exe"),notes,newerMsi);
        await Pick(saveRelease,false,(form,choices,download)=>{
            Require(choices.SelectedItems.Count==2&&choices.SelectedItems.Cast<string>().Any(item=>item.StartsWith(newerMsi.Name+" "))&&choices.SelectedItems.Cast<string>().Any(item=>item.StartsWith(notes.Name+" ")),"Save-only picker did not restore MSI and companion file");form.Close();
        });
        Require(settings.DownloadSelections.Single(item=>item.Purpose==DownloadPurpose.PortableUpdate).FileNames.Single()==gui.Name,"Save-only choice overwrote portable choice");
        File.WriteAllText(Path.ChangeExtension(path,".download-selection.json"),JsonSerializer.Serialize(new{
            SuccessfulDownloadRemembered=true,SettingsReloaded=true,ReopenedDefaultsToGuiExe=true,CurrentSelectionSummaryMatches=true,
            CancelPreservesPreference=true,VersionAndArchitectureAliasMatched=true,MissingVariantFallsBackToNativeRecommendation=true,
            MultipleSavedFilesRestored=true,PurposesIndependent=true,RealNetworkUsed=false,VerifiedDownloadRequests=transport.Requests
        }));
    }
}
