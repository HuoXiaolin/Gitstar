using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private bool noticeChecking;
    private DateTimeOffset noticeRetryAfter;
    private DateTimeOffset? noticeLastAttempt;
    private void ResetNoticeAccount()
    {
        noticeRetryAfter=default;noticeLastAttempt=null;
        NotificationsDot.Visibility=Visibility.Collapsed;NotificationsButton.ToolTip="GitHub 消息";
        if(ready&&!smokeMode&&NotificationSchedule.IsDue(settings.NotificationIntervalMinutes,noticeLastAttempt,DateTimeOffset.UtcNow))_=RefreshNotificationBadge();
    }
    private async Task RefreshNotificationBadge(CancellationToken ct=default)
    {
        if(smokeMode||noticeChecking||exit||quitting||settings.Account.Length==0||api.Token.Length==0||DateTimeOffset.UtcNow<noticeRetryAfter||DateTimeOffset.UtcNow<api.NotificationPollAfter)return;
        noticeChecking=true;noticeLastAttempt=DateTimeOffset.UtcNow;var account=settings.Account;var client=api;
        try{
            var page=await client.Notifications(false,1,ct);
            if(exit||account!=settings.Account||client!=api)return;
            NotificationsDot.Visibility=page.Items.Any(item=>item.Unread)?Visibility.Visible:Visibility.Collapsed;
            NotificationsButton.ToolTip=page.Items.Count==0?"GitHub 消息 · 暂无未读":"GitHub 消息 · 有未读消息";
        }catch(OperationCanceledException)when(ct.IsCancellationRequested){}
        catch(Exception error)when(error is IOException or HttpRequestException or OperationCanceledException or ObjectDisposedException or JsonException){
            if(!exit&&account==settings.Account){noticeRetryAfter=DateTimeOffset.UtcNow.AddMinutes(15);NotificationsButton.ToolTip="GitHub 消息暂未同步："+error.Message;}
        }finally{noticeChecking=false;}
    }
    private void Notifications_Click(object sender,RoutedEventArgs e)=>NotificationDialog();
    private void NotificationDialog(string? previewPath=null)
    {
        var f=new Form(this,"GitHub 消息",940,690);var account=settings.Account;var client=api;
        using var lifetime=new CancellationTokenSource();CancellationTokenSource? preview=null;var closed=false;var loading=false;var pageNumber=1;var hasMore=false;var items=new List<GitHubNotice>();var browser="https://github.com/notifications";
        f.Label("GitHub 通知不等于全部 Issue。切换“我的仓库 Issues”查看尚未触发通知的问题。选择条目可预览，点击按钮或双击在浏览器查看。");
        var source=f.Select("查看内容",["GitHub 通知","我的仓库 Issues"]);
        var all=f.Check("包含已读消息");var search=f.Text("搜索本页消息或仓库");
        var status=new TextBlock{Text="正在读取消息…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(status);
        var grid=new Grid{Height=330};grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1.15,GridUnitType.Star)});
        var list=new ListBox{Margin=new Thickness(0,0,14,0),HorizontalContentAlignment=HorizontalAlignment.Stretch};list.SetResourceReference(BackgroundProperty,"Surface");list.SetResourceReference(ForegroundProperty,"Ink");list.SetResourceReference(Border.BorderBrushProperty,"Edge");ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);grid.Children.Add(list);
        var body=new TextBox{Text="选择左侧消息查看预览。",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12)};Grid.SetColumn(body,1);grid.Children.Add(body);f.Fields.Children.Add(grid);
        f.SizeChanged+=(_,_)=>grid.Height=Math.Max(160,f.ActualHeight-450);
        bool Current()=>!closed&&account==settings.Account&&client==api;
        void Render(){
            preview?.Cancel();list.Items.Clear();browser="https://github.com/notifications";body.Text="选择左侧消息查看预览。";
            foreach(var item in items.Where(item=>(item.Title+" "+item.Repository).Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase))){
                var panel=new StackPanel{Margin=new Thickness(4,6,4,6)};panel.Children.Add(new TextBlock{Text=(item.Unread?"● ":"")+item.Title,FontWeight=item.Unread?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap});var meta=new TextBlock{Text=item.Repository+"\n"+item.Kind+" · "+item.ReasonText+" · "+ProjectTimeRules.Time(item.Updated),FontSize=11,Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap};meta.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(meta);list.Items.Add(new ListBoxItem{Content=panel,Tag=item,HorizontalContentAlignment=HorizontalAlignment.Stretch});
            }
        }
        Button? previous=null,next=null,refresh=null;
        async Task Load(int number){
            if(loading||closed)return;if(!Current()){status.Text="账号已改变，请关闭后重新打开消息。";return;}
            if(account.Length==0||client.Token.Length==0){status.Text="请先登录 GitHub；也可以在浏览器打开消息页面。";return;}
            loading=true;all.IsEnabled=source.IsEnabled=false;previous!.IsEnabled=next!.IsEnabled=refresh!.IsEnabled=false;status.Text=source.SelectedIndex==1?"正在读取我的仓库 Issues…":"正在读取 GitHub 消息…";
            try{
                var page=source.SelectedIndex==1?await client.RepositoryIssues(account,all.IsChecked==true,number,lifetime.Token):await client.Notifications(all.IsChecked==true,number,lifetime.Token);if(!Current())return;
                items=page.Items;pageNumber=number;hasMore=page.More;Render();status.Text=$"第 {number} 页 · 本页 {items.Count} 条"+(page.Total is {} total?$" · 共 {total} 个 Issue":"通知")+(items.Count==0?" · 暂无结果":"")+(page.Incomplete?" · GitHub 返回了部分结果，请稍后重试":"")+(page.Total>1000?" · GitHub 搜索最多可浏览前 1000 条":"");
                if(source.SelectedIndex==0&&all.IsChecked!=true&&number==1){NotificationsDot.Visibility=items.Any(item=>item.Unread)?Visibility.Visible:Visibility.Collapsed;noticeRetryAfter=default;}
            }catch(OperationCanceledException){}catch(Exception error)when(error is IOException or HttpRequestException or ObjectDisposedException or JsonException){if(Current())status.Text="读取失败："+error.Message;}
            finally{loading=false;if(!closed){all.IsEnabled=source.IsEnabled=true;refresh!.IsEnabled=true;previous!.IsEnabled=pageNumber>1;next!.IsEnabled=hasMore;}}
        }
        previous=new Button{Content="上一页",IsEnabled=false,Margin=new Thickness(8,6,0,0),Padding=new Thickness(16,9,16,9)};next=new Button{Content="下一页",IsEnabled=false,Margin=new Thickness(8,6,0,0),Padding=new Thickness(16,9,16,9)};
        previous.Click+=async(_,_)=>{if(pageNumber>1)await Load(pageNumber-1);};next.Click+=async(_,_)=>{if(hasMore)await Load(pageNumber+1);};f.Buttons.Children.Add(previous);f.Buttons.Children.Add(next);refresh=f.Action("刷新",()=>Load(1));
        var open=f.Action("在浏览器查看",()=>{if(!Current())throw new IOException("账号已改变，请重新打开消息。");WindowsServices.Open(browser);return Task.CompletedTask;},true);
        f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});
        search.TextChanged+=(_,_)=>Render();all.Click+=async(_,_)=>await Load(1);
        source.SelectionChanged+=async(_,_)=>{preview?.Cancel();all.Content=source.SelectedIndex==1?"包含已关闭 Issue":"包含已读消息";all.IsChecked=false;search.Text="";items=[];pageNumber=1;hasMore=false;Render();if(previewPath==null)await Load(1);};
        list.SelectionChanged+=async(_,_)=>{
            preview?.Cancel();preview?.Dispose();preview=null;if(list.SelectedItem is not ListBoxItem{Tag:GitHubNotice item})return;
            browser=item.BrowserUrl;body.Text=item.Title+"\n\n"+item.Repository+"\n"+item.Kind+" · "+item.ReasonText+"\n\n正在读取正文…";
            preview=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var selected=preview;
            try{var detail=await client.NotificationPreview(item,selected.Token);if(!Current()||selected.IsCancellationRequested)return;body.Text=item.Title+"\n\n"+item.Repository+" · "+item.ReasonText+"\n\n"+detail.Body;browser=detail.Url;}
            catch(OperationCanceledException){}catch(Exception error)when(error is IOException or HttpRequestException or ObjectDisposedException or JsonException){if(Current()&&!selected.IsCancellationRequested)body.Text=item.Title+"\n\n预览暂不可用："+error.Message+"\n\n可在浏览器查看完整消息。";}
        };
        list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is ListBoxItem&&Current()&&open.IsEnabled)open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
        f.Closed+=(_,_)=>{closed=true;lifetime.Cancel();preview?.Cancel();preview?.Dispose();};
        if(previewPath==null)f.Loaded+=async(_,_)=>await Load(1);
        else{
            if(Environment.GetCommandLineArgs().Contains("--repository-issues"))source.SelectedIndex=1;
            items=[new("1","example/editor","修复便携软件更新后的版本识别","Issue","mention",true,DateTimeOffset.Now,"https://api.github.com/repos/example/editor/issues/42",""),new("2","example/toolkit","Release v2.1.0","Release","subscribed",true,DateTimeOffset.Now.AddHours(-2),"https://api.github.com/repos/example/toolkit/releases/8","")];Render();status.Text="第 1 页 · 本页 2 条消息";body.Text="修复便携软件更新后的版本识别\n\nexample/editor · 提及了你\n\n最新回复\n\n已完成更新流程验证，请核对程序路径和实际安装版本。\n\n点击下方按钮可在浏览器查看完整讨论。";f.RenderPreview(previewPath);
            if(source.SelectedIndex==1){items=[new("issue:1","example/editor","便携更新后配置保存问题","Issue","issue_open",false,DateTimeOffset.Now,"https://api.github.com/repos/example/editor/issues/1",""),new("issue:2","example/editor","全屏状态下快捷键支持","Issue","issue_open",false,DateTimeOffset.Now.AddDays(-1),"https://api.github.com/repos/example/editor/issues/2","")];Render();status.Text="第 1 页 · 本页 2 条 · 共 2 个 Issue";body.Text="便携更新后配置保存问题\n\nexample/editor · 待处理问题\n\n更新完成后需要确认配置文件保留。\n\n此视图包含未触发 GitHub 通知的 Issue。";}
        }
        f.ShowDialog();
    }
}
