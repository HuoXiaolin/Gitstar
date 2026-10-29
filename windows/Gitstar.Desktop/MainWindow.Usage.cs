using System.Windows;
using System.Windows.Controls;
namespace Gitstar.Desktop;
public partial class MainWindow
{
    private const string UsageKey="usage/v1";
    private UsageStatistics usage=UsageStatistics.Create(DateTimeOffset.Now);
    private string usageWarning="";
    private void InitializeUsage()
    {
        if(smokeMode)return;
        try{usage=store.Get(UsageKey,UsageStatistics.Create(DateTimeOffset.Now));usage.Launch(DateTimeOffset.Now);SaveUsage();}
        catch(Exception error)when(error is IOException or JsonException){usageWarning="统计记录读取未完成，本次统计暂存于当前会话。可重置统计后重新记录。";}
    }
    private void SaveUsage(){if(smokeMode)return;try{store.Set(UsageKey,usage);usageWarning="";}catch(IOException){usageWarning="统计暂未写入本地，请稍后重试；其他数据未改动。";}}
    private void UsageOpen(){if(smokeMode)return;usage.Open(DateTimeOffset.Now);SaveUsage();}
    private void UseFeature(UsageFeature feature){if(smokeMode)return;usage.Use(feature,DateTimeOffset.Now);SaveUsage();}
    private void UseTask(string kind,bool background)
    {
        if(background)return;UsageFeature? feature=kind switch{"检查"=>UsageFeature.CheckUpdates,"下载" or "批量下载"=>UsageFeature.Download,"发布" or "自动发布"=>UsageFeature.Publish,"源码上传"=>UsageFeature.SourceUpload,_=>null};if(feature is {} value)UseFeature(value);
    }
    private void RenderUsage(Form f,string? previewPath=null)
    {
        f.Section("使用统计");var panel=new StackPanel();f.Fields.Children.Add(panel);
        void Render(){panel.Children.Clear();var now=DateTimeOffset.Now;var today=DateOnly.FromDateTime(now.Date);var week=usage.ActiveDaysSince(today.AddDays(-6),today);var month=usage.ActiveDaysSince(today.AddDays(-29),today);
            panel.Children.Add(ProjectText("Gitstar 本机使用频率",19));panel.Children.Add(ProjectText($"统计起始：{usage.Since.ToLocalTime():yyyy-MM-dd HH:mm}  ·  最近使用："+(usage.LastUsed is {} last?last.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"暂无"),12,true));
            var summary=new TextBlock{Text=$"使用天数  {usage.ActiveDays} 天\n近 7 天使用  {week} / 7 天    ·    近 30 天使用  {month} / 30 天\n程序启动  {usage.Launches:N0} 次    ·    主界面打开  {usage.Opens:N0} 次\n功能调用  {usage.Actions:N0} 次",FontSize=15,LineHeight=29,Margin=new Thickness(0,14,0,14)};summary.SetResourceReference(TextBlock.ForegroundProperty,"Ink");panel.Children.Add(summary);
            panel.Children.Add(ProjectText("启动次数包含开机静默启动；使用天数仅在打开主界面或调用功能时计入。功能统计按页面进入、保存操作或已开始的任务计数，单文件下载按文件任务、批量下载按批次计算，不代表成功完成次数。后台自动检查不计入功能调用。",12,true));
            panel.Children.Add(ProjectText("常用功能",15));var features=usage.Features.Where(item=>item.Value>0).OrderByDescending(item=>item.Value).ToArray();if(features.Length==0)panel.Children.Add(ProjectText("暂无功能记录",12,true));foreach(var item in features)panel.Children.Add(ProjectText($"{UsageStatistics.Label(item.Key)}    {item.Value:N0} 次",13));
            panel.Children.Add(ProjectText("最近 7 天",15));for(var offset=0;offset<7;offset++){var day=today.AddDays(-offset);var counts=usage.Days.GetValueOrDefault(day.ToString("yyyy-MM-dd"));panel.Children.Add(ProjectText($"{day:MM-dd}    启动 {counts?.Launches??0} 次  ·  打开 {counts?.Opens??0} 次  ·  功能调用 {counts?.Actions??0} 次",12,true));}
            panel.Children.Add(ProjectText("仅保存在本机，所有登录账号共用；从此版本开始记录，不补造历史。只记录日期与次数，不记录仓库名、文件路径或操作内容，也不会上传。",12,true));if(usageWarning.Length>0)panel.Children.Add(ProjectText(usageWarning,12,true));
        }
        Render();var reset=f.Action("重置使用统计",()=>{if(DialogMessage.Show(f,"重置本机全部使用统计？\n\n启动次数、使用天数和功能次数将清零，从现在重新记录。账号、设置、软件关联、任务记录和下载文件均保留。","重置使用统计",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return Task.CompletedTask;usage=UsageStatistics.Create(DateTimeOffset.Now);SaveUsage();Render();return Task.CompletedTask;});reset.Name="UsageResetButton";if(NameScope.GetNameScope(f)==null)NameScope.SetNameScope(f,new NameScope());f.RegisterName(reset.Name,reset);f.Buttons.Children.Remove(reset);reset.HorizontalAlignment=HorizontalAlignment.Right;reset.Margin=new Thickness(0,0,0,10);f.Fields.Children.Insert(f.Fields.Children.IndexOf(panel),reset);
        if(previewPath!=null&&Environment.GetCommandLineArgs().Contains("--settings-usage")){var fixture=UsageStatistics.Create(DateTimeOffset.Now.AddDays(-12));fixture.Launch(DateTimeOffset.Now.AddDays(-2));fixture.Open(DateTimeOffset.Now.AddDays(-2));fixture.Use(UsageFeature.Download,DateTimeOffset.Now.AddDays(-2));fixture.Launch(DateTimeOffset.Now);fixture.Open(DateTimeOffset.Now);fixture.Use(UsageFeature.SoftwareBinding,DateTimeOffset.Now);usage=fixture;Render();File.WriteAllText(Path.ChangeExtension(previewPath,".usage.json"),JsonSerializer.Serialize(new{usage.Launches,usage.Opens,usage.ActiveDays,usage.Actions,ResetAvailable=reset.IsEnabled,StorageScope="local-device",NoHistoricalBackfill=true}));}
    }
}
