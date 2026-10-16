using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void RenderAbout(Form f)
    {
        f.Section("关于");var current=AppInfo.Version;
        var hero=new StackPanel();var title=new TextBlock{Text="Gitstar",FontSize=26,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};title.SetResourceReference(TextBlock.ForegroundProperty,"Ink");hero.Children.Add(title);
        hero.Children.Add(ProjectText("版本 "+current+"   ·   作者 "+AppInfo.Author,13,true));hero.Children.Add(ProjectText(AppInfo.Description,13,true));
        var card=new Border{Child=hero,Padding=new Thickness(18),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,8,0,14)};card.SetResourceReference(Border.BackgroundProperty,"AccentSoft");f.Fields.Children.Add(card);
        f.Label("项目地址："+AppInfo.ProjectUrl);f.Label("开源许可：GNU GPL v3.0（GPL-3.0-only）");f.Label("更新来源：本项目的 GitHub Releases 正式版。发现更新后展示日志，由你选择下载并安装。");
        var status=new TextBlock{Text="点击检查更新，核对当前版本与最新正式版。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,12),FontSize=13};status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(status);
        var actions=new WrapPanel();f.Fields.Children.Add(actions);Release? latest=null;
        Button Inline(string text,Func<Task> work,bool primary=false){var button=f.Action(text,work,primary);f.Buttons.Children.Remove(button);button.Margin=new Thickness(0,4,8,4);actions.Children.Add(button);return button;}
        Inline("项目主页",()=>{WindowsServices.Open(AppInfo.ProjectUrl);return Task.CompletedTask;});Inline("查看发布版本",()=>{WindowsServices.Open(AppInfo.ReleasesUrl);return Task.CompletedTask;});
        var log=Inline("更新日志",()=>{if(latest!=null)Form.Notes(f,"Gitstar · "+latest.Tag,latest.Body.Length>0?latest.Body:"此版本没有提供更新日志。");return Task.CompletedTask;});log.Visibility=Visibility.Collapsed;
        var download=Inline("下载新版",()=>{if(latest!=null)WindowsServices.Open(AppInfo.ReleasesUrl+"/tag/"+Uri.EscapeDataString(latest.Tag));return Task.CompletedTask;});download.Visibility=Visibility.Collapsed;
        Inline("检查更新",async()=>{
            status.Text="正在检查 Gitstar 最新正式版…";latest=null;log.Visibility=download.Visibility=Visibility.Collapsed;
            await Execute("应用更新","检查 Gitstar 新版本",async ct=>{
                try{latest=await api.LatestGitstarRelease(ct);var compared=Rules.CompareVersions(latest.Tag,current);status.Text=compared>0?"发现新版本 "+latest.Tag+"，当前版本 "+current+"。":compared==0?"当前版本 "+current+" 已是最新。":compared<0?"当前版本 "+current+" 高于 GitHub 正式版 "+latest.Tag+"，无需更新。":"最新正式版："+latest.Tag+"；版本号格式无法自动比较，请查看发布版本。";log.Visibility=Visibility.Visible;download.Visibility=compared>0?Visibility.Visible:Visibility.Collapsed;model.Status=status.Text;}
                catch(GitHubNotFoundException){status.Text="暂未找到可用正式版，请到项目主页查看发布状态。";throw;}
                catch(Exception ex){status.Text="检查未完成："+ex.Message;throw;}
            },resources:["self-update"]);
        },true);
    }
}
