using System.Windows;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private const string DownloadSettingsNoticeKey="download-settings-notice/v1";
    private bool ConfirmDownloadSettings(string root,bool preserveSelection=false,string? previewPath=null)
    {
        if(smokeMode&&previewPath==null)return true;
        if(store.Get(DownloadSettingsNoticeKey,false))return true;
        var f=new Form(this,"首次下载 · 安装包保存设置",650,600);var proceed=false;var openSettings=false;
        f.Label("下载前，请了解当前保存设置。此提示在本机仅显示一次，之后可随时到设置中修改。");
        var folder=f.Text("本次保存位置",Path.GetFullPath(root));folder.IsReadOnly=true;
        f.Section("安装包保留规则");f.Label(settings.KeepLatestPackages?"只保留最新安装包：已开启。新版本下载并校验成功后，清理同一项目、平台和正式/测试通道的旧安装包。":"只保留最新安装包：已关闭。已下载的不同版本会继续保留。");
        f.Label("建议保留一份最新安装包，方便重装或离线备用。即使仓库被删除、项目下架或发行附件被撤回，也还有本地副本可用。");
        if(settings.DeleteInstalledPackages)f.Label("当前已开启“安装成功后自动删除”。如需长期保留本地副本，可到设置关闭此选项。");
        f.Label("仅清理 Gitstar 已记录且未被修改的旧下载文件，不扫描或删除目录中的其他文件。");
        if(preserveSelection)f.Label("本次为历史版本或批量下载，会保留你明确选中的安装包。");
        f.Label("修改默认保存位置：设置 → 下载与更新\n修改保留规则：设置 → 安装包管理");
        f.Action("前往设置",()=>{openSettings=true;f.Close();return Task.CompletedTask;});
        f.Action("知道了，继续下载",()=>{proceed=true;f.Close();return Task.CompletedTask;},true).IsDefault=true;
        f.ContentRendered+=(_,_)=>store.Set(DownloadSettingsNoticeKey,true);
        if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
        if(openSettings)SettingsDialog();return proceed;
    }
    private void DownloadSettingsNoticeSmoke(string path)
    {
        var folder=@"D:\Downloads\Gitstar_Download";ConfirmDownloadSettings(folder,true,path);
        var firstShown=store.Get(DownloadSettingsNoticeKey,false);var secondSkipped=ConfirmDownloadSettings(folder,previewPath:path);settings.Account="another-example";var accountSkipped=ConfirmDownloadSettings(folder,previewPath:path);
        File.WriteAllText(Path.ChangeExtension(path,".once.json"),JsonSerializer.Serialize(new{DefaultKeepsLatest=new Settings().KeepLatestPackages,FirstShown=firstShown,SecondSkipped=secondSkipped,AccountSwitchSkipped=accountSkipped}));
        if(!firstShown||!secondSkipped||!accountSkipped)throw new IOException("First download notice must show once per local installation");
    }
}
