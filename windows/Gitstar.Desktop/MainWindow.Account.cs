using System.Windows;
using System.Windows.Controls;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void AccountDialog(string? previewPath=null)
    {
        var account=settings.Account;var client=api;var f=new Form(this,"GitHub 账号信息",640,620);
        using var lifetime=new CancellationTokenSource();var closed=false;
        f.Section("当前账号");f.Label(account);var profile=f.Text("GitHub 主页","https://github.com/"+Uri.EscapeDataString(account));profile.IsReadOnly=true;
        var homepage=f.Action("打开 GitHub 主页",()=>{WindowsServices.Open("https://github.com/"+Uri.EscapeDataString(account));return Task.CompletedTask;});f.Buttons.Children.Remove(homepage);homepage.HorizontalAlignment=HorizontalAlignment.Left;homepage.Margin=new Thickness(0,0,0,12);f.Fields.Children.Add(homepage);
        var details=new TextBlock{Text="正在读取账号资料…",TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,5,0,12)};f.Fields.Children.Add(details);
        f.Section("授权信息");var authorization=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,0,0,12)};f.Fields.Children.Add(authorization);
        void UpdateAuthorization(){var auth=client.Credentials;authorization.Text="本地授权："+(client.Token.Length>0?"已保存（有效性以 GitHub 核对结果为准）":"未保存，请重新获取授权")+"\n授权权限："+(auth.Scope.Length>0?auth.Scope:"未提供权限明细")+"\n到期时间："+(auth.ExpiresAt is {} expiry?ProjectTimeRules.Time(expiry):"未提供到期时间")+"\n自动续期："+(auth.RefreshToken.Length>0?"已保存续期凭据":"未保存续期凭据");}
        UpdateAuthorization();
        f.Label("退出登录会清除本机登录凭据，保留此账号的缓存与配置。重新获取授权可用于修复授权失效或补充权限。");
        bool Current()=>!closed&&settings.Account==account&&api==client;
        f.Action("退出登录",()=>{
            if(!Current())throw new IOException("账号已改变，请重新打开账号信息。");
            if(busy||batchPublishing)throw new IOException("请先等待当前任务完成，再退出登录。");
            if(DialogMessage.Show(f,$"退出 {account}？此账号缓存与配置会留在本机。","退出登录",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return Task.CompletedTask;
            store.Set("profile/"+account,settings);CredentialStorage.Clear(store);var old=settings;
            settings=new Settings{ClientId=old.ClientId,Proxy=old.Proxy,DirectConnection=old.DirectConnection,Startup=old.Startup,Dark=old.Dark,DownloadFolder=old.DownloadFolder,RecentSort=old.RecentSort,ProjectsSort=old.ProjectsSort};client.Token="";Save();SwitchOperations();LoadCache();RebuildGroups();AccountLabel();PlanUpdates();Display();f.Close();return Task.CompletedTask;
        });
        f.Action("重新获取授权",()=>{if(!Current())throw new IOException("账号已改变，请重新打开账号信息。");if(busy||batchPublishing)throw new IOException("请先等待当前任务完成，再重新获取授权。");f.Close();AuthorizeGitHub();return Task.CompletedTask;},true);
        f.Closed+=(_,_)=>{closed=true;lifetime.Cancel();};
        if(previewPath==null)f.Loaded+=async(_,_)=>{
            try{var info=await client.AccountInformation(lifetime.Token);if(!Current())return;details.Text=info.Login.Equals(account,StringComparison.OrdinalIgnoreCase)?$"显示名称：{(info.Name.Length>0?info.Name:info.Login)}\n账号类型：{info.Type}\n公开仓库：{info.PublicRepositories}   ·   关注者：{info.Followers}"+(info.Bio.Length>0?"\n简介："+info.Bio:""):"授权账号与本地记录不一致，请重新获取授权。";UpdateAuthorization();}
            catch(OperationCanceledException){}catch(Exception error)when(error is IOException or HttpRequestException or JsonException or ObjectDisposedException){if(Current())details.Text="暂时无法读取账号资料："+error.Message;}
        };
        else{details.Text="显示名称：示例开发者\n账号类型：User\n公开仓库：12   ·   关注者：38\n简介：开源软件开发与维护。";authorization.Text="本地授权：已保存（演示数据）\n授权权限：repo, user, workflow\n到期时间：未提供到期时间\n自动续期：已保存续期凭据";f.RenderPreview(previewPath);}
        f.ShowDialog();
    }
}
