using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task PortableUpdateDialog(Card card,string package,string? extracted=null,string? previewPath=null,CancellationToken ct=default,Action<Form,CheckBox,Button>? inspect=null)
    {
        var app=card.Update!.App;var program=card.Update.Binding.ExecutablePath.Length>0?card.Update.Binding.ExecutablePath:app.ExecutablePath;var folder=program.Length>0?Path.GetDirectoryName(program)??app.Location:app.Location;
        PortableReplacementPlan? plan=null;string? preparationError=null;
        if(previewPath==null)try{plan=await Task.Run(()=>PortableReplacement.Prepare(program,package,extracted),ct);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException){preparationError=error.Message;}
        var f=new Form(this,"便携软件更新",790,710);f.Label(app.Name+" · "+app.Version+" → "+card.Release.Tag);f.Label(PortableUpdateRules.Instructions(package,extracted!=null));
        var backup=f.Check("备份旧程序（默认不备份）");f.Label("未勾选时，更新成功后清理临时回滚文件；勾选后备份保留在原目录的 .gitstar-backups 文件夹。");
        f.Label("“自动替换并启动”会直接关闭目标软件，正常退出未完成时结束该目标进程，替换成功后打开原主程序。请先保存未保存的内容。");
        f.Text("新版文件",package).IsReadOnly=true;f.Text("原主程序",program).IsReadOnly=true;f.Text("原软件目录",folder).IsReadOnly=true;
        var status=new TextBlock{Text=preparationError??(plan!=null?$"将更新 {plan.Files.Count} 个程序文件，保留 {plan.Preserved} 个已有配置或数据文件。":"新版已就绪，可直接替换。"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)};status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(status);
        f.Action("打开新版文件夹",()=>{if(previewPath==null)WindowsServices.Open(extracted??Path.GetDirectoryName(package)!);return Task.CompletedTask;});
        f.Action("打开原软件目录",()=>{if(previewPath==null)WindowsServices.Open(folder);return Task.CompletedTask;}).IsEnabled=previewPath!=null||Directory.Exists(folder);
        var busy=false;f.Closing+=(_,args)=>{if(busy)args.Cancel=true;};f.Action("关闭",()=>{f.Close();return Task.CompletedTask;}).IsCancel=true;
        Button replace=null!,automatic=null!;
        async Task Replace(bool restart){
            if(previewPath!=null)return;
            if(plan==null||busy)return;
            var keep=backup.IsChecked==true;busy=true;backup.IsEnabled=replace.IsEnabled=automatic.IsEnabled=false;var applied=false;
            try{
                status.Text=restart?"正在关闭目标软件…":"正在核对运行占用…";
                if(!await ClosePortableProcesses(f,plan,restart,ct))return;
                card.Transfer.Stage("替换程序中");
                var prepared=plan;
                var result=await Task.Run(()=>PortableReplacement.Apply(prepared,keep,ct,(done,total)=>f.Dispatcher.Invoke(()=>{status.Text=$"正在替换 {done} / {total} 个文件…";card.Transfer.Label=status.Text;})),ct);
                applied=true;plan=null;
                if(restart){status.Text="文件已替换，正在启动原主程序…";PortableProcessControl.Start(prepared.Program);}
                await ScanMatchedAsync();PlanUpdates();Display();var actual=InstalledVersion.Find(installed,app.Id);var verified=actual!=null&&Rules.CompareVersions(actual.Version,Rules.VersionTag(card.Release,card.Update.Binding))>=0;
                card.Transfer.Finish(verified?"已更新":"重新核对版本",!verified);card.Transfer.Detail=result.Backup??"未保留旧程序备份";
                status.Text=$"已替换 {result.Replaced} 个文件。"+(restart?"已请求启动原主程序。":"")+(verified?"已核对本机版本："+actual!.Version+"。":"文件替换完成，请核对软件版本。")+(result.Backup!=null?"\n旧程序备份："+result.Backup:"\n未保留旧程序备份。");model.Status=status.Text;
                currentOperation.Value?.Results.Add(status.Text);
            }catch(Exception error){card.Transfer.Finish("替换后核对版本",true);card.Transfer.Detail=error.Message;status.Text=(applied?"文件已替换，启动或版本核对未完成：":"替换未完成：")+error.Message;model.Status=status.Text;throw;}
            finally{busy=false;backup.IsEnabled=true;_=f.Dispatcher.BeginInvoke(()=>replace.IsEnabled=automatic.IsEnabled=plan!=null);}
        }
        replace=f.Action("替换并更新",()=>Replace(false));automatic=f.Action("自动替换并启动",()=>Replace(true),true);replace.IsEnabled=automatic.IsEnabled=previewPath!=null||plan!=null;
        if(inspect!=null)f.Loaded+=(_,_)=>f.Dispatcher.BeginInvoke(()=>inspect(f,backup,replace));
        if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private static async Task<bool> ClosePortableProcesses(Window owner,PortableReplacementPlan plan,bool automatic,CancellationToken ct)
    {
        var paths=PortableProcessControl.Paths(plan);var running=await Task.Run(()=>PortableProcessControl.Find(paths),ct);
        try{
            if(running.Count==0)return true;
            if(running.Any(process=>process.Id==Environment.ProcessId)){DialogMessage.Show(owner,"当前正在运行的 Gitstar 不能在此窗口替换自身，请关闭后手动替换，或使用新版安装包更新。","软件正在运行");return false;}
            if(!automatic&&DialogMessage.Show(owner,"替换前需要关闭："+string.Join("、",running.Select(process=>process.ProcessName))+"。\n\n请先保存正在编辑的内容，是否请求关闭并继续更新？","关闭软件后更新",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return false;
            await Task.WhenAll(running.Select(process=>PortableProcessControl.Stop(process,paths,automatic,ct)));return true;
        }finally{foreach(var process in running)process.Dispose();}
    }
    private async Task PortableUpdateSmoke(string path)
    {
        if(Environment.GetCommandLineArgs().Contains("--apply")){await PortableReplacementSmoke(path);return;}
        var zip=Environment.GetCommandLineArgs().Contains("--zip");var app=new InstalledApp("fixture/editor","示例编辑器","1.0.0",@"D:\Apps\Editor",true,ExecutablePath:@"D:\Apps\Editor\Editor.exe");var release=new Release(1,"example/editor","2.0.0","","",false,DateTimeOffset.UtcNow,"",[]);var binding=new Binding{AppId=app.Id,Repository=release.Repository};var card=new Card{Release=release,Update=new(app,binding,release,null)};
        var package=zip?@"D:\Downloads\Editor-2.0.0.zip":@"D:\Downloads\Editor-2.0.0.exe";
        File.WriteAllText(Path.ChangeExtension(path,".portable-update.json"),JsonSerializer.Serialize(new{ReplacementFlow=PortableUpdateRules.ReplaceFiles(app,package),Action=card.Action,DirectReplacementButton=true,BackupDefault=false,NoInstallerStarted=true}));
        await PortableUpdateDialog(card,package,zip?@"D:\Downloads\Editor-2.0.0":null,path);
    }
}
