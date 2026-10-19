using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task PortableReplacementSmoke(string path)
    {
        var zip=Environment.GetCommandLineArgs().Contains("--zip");var retain=Environment.GetCommandLineArgs().Contains("--backup");var root=Path.Combine(store.Root,"portable-app");var downloads=Path.Combine(store.Root,"downloads");Directory.CreateDirectory(root);Directory.CreateDirectory(downloads);
        var program=Path.Combine(root,"Editor.exe");var bytes=File.ReadAllBytes(Environment.ProcessPath!);File.WriteAllBytes(program,bytes.Concat(new byte[]{1}).ToArray());var expected=bytes.Concat(new byte[]{2}).ToArray();File.WriteAllText(Path.Combine(root,"settings.json"),"personal-settings");
        var package=Path.Combine(downloads,zip?"Editor.zip":"Editor-2.0.0.exe");string? extracted=null;
        if(zip){File.WriteAllText(package,"already extracted ZIP fixture");extracted=Path.Combine(downloads,"extracted");var payload=Path.Combine(extracted,"Editor-portable");Directory.CreateDirectory(payload);File.WriteAllBytes(Path.Combine(payload,"Editor.exe"),expected);File.WriteAllText(Path.Combine(payload,"settings.json"),"factory-settings");File.WriteAllText(Path.Combine(payload,"library.dll"),"new-library");}else File.WriteAllBytes(package,expected);
        var app=new InstalledApp("portable/fixture-editor","示例编辑器","1.0.0",root,true,ExecutablePath:program);var release=new Release(1,"example/editor","2.0.0","","",false,DateTimeOffset.UtcNow,"",[]);var binding=new Binding{AppId=app.Id,Repository=release.Repository,ExecutablePath=program};settings.PortableApps=[app];settings.Bindings=[binding];SetInstalled([app]);var card=new Card{Release=release,Update=new(app,binding,release,null)};
        await PortableUpdateDialog(card,package,extracted,inspect:async(form,backup,replace)=>{
            var defaultNoBackup=backup.IsChecked==false;backup.IsChecked=retain;
            await ((Func<Task>)replace.Tag)();await form.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var replaced=File.ReadAllBytes(program).SequenceEqual(expected);var preserved=File.ReadAllText(Path.Combine(root,"settings.json"))=="personal-settings";var backupRoot=Path.Combine(root,".gitstar-backups");var backupCorrect=retain?Directory.Exists(backupRoot)&&Directory.GetFiles(backupRoot,"Editor.exe",SearchOption.AllDirectories).Any(file=>File.ReadAllBytes(file).SequenceEqual(bytes.Concat(new byte[]{1}))):!Directory.Exists(backupRoot);var verified=InstalledVersion.Find(installed,app.Id)?.Version=="2.0.0";var cleaned=!Directory.GetDirectories(root,".gitstar-update-*").Any();
            File.WriteAllText(Path.ChangeExtension(path,".portable-apply.json"),JsonSerializer.Serialize(new{Format=zip?"ZIP":"EXE",ButtonAppliedFiles=replaced,ConfigurationPreserved=preserved,BackupDefaultOff=defaultNoBackup,BackupChoiceHonored=backupCorrect,VersionVerified=verified,TemporaryFilesRemoved=cleaned,RepeatButtonDisabled=!replace.IsEnabled}));
            if(!replaced||!preserved||!defaultNoBackup||!backupCorrect||!verified||!cleaned||replace.IsEnabled)throw new IOException("Portable replacement UI integration failed");
            form.UpdateLayout();var visual=(FrameworkElement)form.Content;var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);form.Close();
        });
    }
}
