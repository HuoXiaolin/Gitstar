using System.Windows;
using Microsoft.Win32;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void EditPackageStorageRules(Window owner)
    {
        var account=PackagesKey;var form=new Form(owner,"安装包保存规则",680,500);
        var retention=form.Select("下载文件保留方式",["只保留最新安装包（按项目、平台及正式 / 测试通道）","保留全部已下载的安装包"],settings.KeepLatestPackages?0:1);
        var installedRule=form.Select("确认软件已安装后的处理",["保留安装包","询问是否删除","自动删除安装包"],settings.DeleteInstalledPackages?2:settings.PromptInstalledPackageCleanup?1:0);
        var folder=form.Text("默认保存目录",settings.DownloadFolder);
        form.FieldAction(folder,form.Action("选择目录",()=>{var picker=new OpenFolderDialog{Title="选择默认保存目录"};if(Directory.Exists(folder.Text))picker.InitialDirectory=folder.Text;if(picker.ShowDialog(form)==true)folder.Text=picker.FolderName;return Task.CompletedTask;}));
        form.Label("建议保留最新安装包，便于离线重装；项目下架或附件撤回后仍有本地副本。只清理 Gitstar 记录且内容校验一致的文件，不删除已关联的主程序。");
        form.Label("保存规则对后续下载与检查生效，不移动已有文件。本页保存时不立即清理；已有文件可在安装包管理中选择后删除或导出。");
        form.Action("取消",()=>{form.Close();return Task.CompletedTask;}).IsCancel=true;
        form.Action("保存规则",()=>{
            if(account!=PackagesKey)throw new IOException("账号已变化，请重新打开保存规则。");var input=Environment.ExpandEnvironmentVariables(folder.Text.Trim());if(!Path.IsPathFullyQualified(input))throw new IOException("请选择有效的完整保存目录。");var path=Path.GetFullPath(input);
            settings.KeepLatestPackages=retention.SelectedIndex==0;settings.DeleteInstalledPackages=installedRule.SelectedIndex==2;settings.PromptInstalledPackageCleanup=installedRule.SelectedIndex==1;settings.DownloadFolder=path;Save();form.Close();return Task.CompletedTask;
        },true);form.EnableEditGuard();form.ShowDialog();
    }
}
