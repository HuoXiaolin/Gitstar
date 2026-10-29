namespace Gitstar.Core;

public enum UsageFeature { RecentPage,LocalPage,ProjectsPage,Notifications,Settings,Tasks,CheckUpdates,Download,Publish,SourceUpload,SoftwareBinding,RepositorySearch,Rankings }
public sealed class UsageDay
{
    public long Launches {get;set;}
    public long Opens {get;set;}
    public long Actions {get;set;}
}
public sealed class UsageStatistics
{
    public DateTimeOffset Since {get;set;}
    public DateTimeOffset? LastUsed {get;set;}
    public long Launches {get;set;}
    public long Opens {get;set;}
    public Dictionary<string,UsageDay> Days {get;set;}=[];
    public Dictionary<UsageFeature,long> Features {get;set;}=[];
    public int ActiveDays=>Days.Values.Count(day=>day.Opens>0||day.Actions>0);
    public long Actions=>Features.Values.Sum();
    public static UsageStatistics Create(DateTimeOffset now)=>new(){Since=now};
    private UsageDay Day(DateTimeOffset now){if(Since==default)Since=now;var key=now.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);if(!Days.TryGetValue(key,out var day))Days[key]=day=new();return day;}
    public void Launch(DateTimeOffset now){Day(now).Launches++;Launches++;}
    public void Open(DateTimeOffset now){Day(now).Opens++;Opens++;LastUsed=now;}
    public void Use(UsageFeature feature,DateTimeOffset now){if(!Enum.IsDefined(feature))return;Day(now).Actions++;Features[feature]=Features.GetValueOrDefault(feature)+1;LastUsed=now;}
    public int ActiveDaysSince(DateOnly first,DateOnly last)=>Days.Count(item=>DateOnly.TryParseExact(item.Key,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var day)&&day>=first&&day<=last&&(item.Value.Opens>0||item.Value.Actions>0));
    public static string Label(UsageFeature feature)=>feature switch{UsageFeature.RecentPage=>"最近更新",UsageFeature.LocalPage=>"本机可更新",UsageFeature.ProjectsPage=>"我的项目",UsageFeature.Notifications=>"查看消息",UsageFeature.Settings=>"打开设置",UsageFeature.Tasks=>"查看任务",UsageFeature.CheckUpdates=>"手动检查更新",UsageFeature.Download=>"下载任务",UsageFeature.Publish=>"发布版本任务",UsageFeature.SourceUpload=>"源码上传任务",UsageFeature.SoftwareBinding=>"保存软件关联",UsageFeature.RepositorySearch=>"线上仓库搜索",UsageFeature.Rankings=>"排行榜",_=>"其他"};
}
