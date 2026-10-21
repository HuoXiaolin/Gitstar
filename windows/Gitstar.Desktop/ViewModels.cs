using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Gitstar.Desktop;
public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string name)=>PropertyChanged?.Invoke(this,new(name));
    protected void Set<T>(ref T field,T value,[CallerMemberName]string? name=null) { if(EqualityComparer<T>.Default.Equals(field,value))return; field=value; PropertyChanged?.Invoke(this,new(name)); }
}
public sealed class Card : Observable
{
    private bool special;
    public bool IsSpecial {get=>special;set{if(special==value)return;Set(ref special,value);Changed(nameof(SpecialFollowHint));}}
    public string SpecialFollowHint=>IsSpecial?"移出特别关注":"加入特别关注";
    public RepositoryMetadata Metadata {get;init;}=new();
    private bool selected;
    public bool IsSelected {get=>selected;set=>Set(ref selected,value);}
    public bool CanManageStar=>Update==null;
    private TransferState? transfer;
    public TransferState Transfer {get=>transfer??=new();set=>transfer=value;}
    public Release Release { get; init; } = null!;
    public UpdateCandidate? Update { get; init; }
    public string Title => Update?.App.Name ?? Release.Repository.Split('/').Last();
    public string Repository => Release.Repository;
    public string FollowSource {get;init;}="";
    public string Version => Update==null ? Release.Tag : $"{Update.App.Version}  →  {Release.Tag}";
    public string VersionEvidence=>Update==null?"":$"版本来源：{Update.App.VersionSource}\n程序：{Update.App.ExecutablePath}\n{Update.App.VersionDetails}\n登记版本：{Update.App.RegisteredVersion}";
    public string Channel => Release.Channel;
    public string BadgeColor => Release.Prerelease ? "#FFF1D6" : "#E9F7F1";
    public string BadgeInk => Release.Prerelease ? "#9D650B" : "#17845E";
    public DateTimeOffset? RepositoryUpdated {get;init;}
    public bool ShowRepositoryUpdated {get;init;}
    public DateTimeOffset SortTime=>ShowRepositoryUpdated?RepositoryUpdated??DateTimeOffset.MinValue:Release.Published;
    public string Date => ShowRepositoryUpdated?RepositoryUpdated is {} updated?$"仓库更新 {updated.ToLocalTime():yyyy-MM-dd HH:mm}":"更新时间待同步":Release.Date;
    public string DateHint=>$"发布时间：{Release.Date}\n仓库更新时间：{(RepositoryUpdated is {} updated?updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"尚无缓存，点击检查更新后获取")}\n更新时间使用 GitHub 仓库的 updated_at，与版本发布时间分别记录。";
    public static IOrderedEnumerable<Card> Order(IEnumerable<Card> cards)=>cards.OrderByDescending(c=>c.SortTime).ThenByDescending(c=>c.Release.Published).ThenBy(c=>c.Repository,StringComparer.OrdinalIgnoreCase);
    private string? summary;
    public string Summary => summary ??= ReleasePreview.Create(Release.Body,Release.Tag);
    public string Action => Update==null ? "保存安装包" : Update.App.Portable?"下载便携新版":Update.Asset?.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)==true ? "获取便携新版" : "下载并安装";
    public string Accent => Update!=null ? "#168673" : "#386AC6";
}
public sealed class TransferState : Observable
{
    private string label="",detail="";
    private bool active,indeterminate,needsDetection;
    private double percentage;
    public string Label {get=>label;set=>Set(ref label,value);}
    public string Detail {get=>detail;set=>Set(ref detail,value);}
    public bool Active {get=>active;set=>Set(ref active,value);}
    public bool Indeterminate {get=>indeterminate;set=>Set(ref indeterminate,value);}
    public bool NeedsDetection {get=>needsDetection;set=>Set(ref needsDetection,value);}
    public double Percentage {get=>percentage;set=>Set(ref percentage,Math.Clamp(value,0,100));}
    public void Stage(string text,bool indeterminate=true){Label=text;Active=true;Indeterminate=indeterminate;NeedsDetection=false;}
    public void Downloaded(long received,long total){Percentage=total>0?received*100.0/total:0;Label=total>0?$"下载中 {Percentage:F0}%":"下载中";Detail=total>0?$"{received/1048576.0:F1} / {total/1048576.0:F1} MB":$"{received/1048576.0:F1} MB";}
    public void Finish(string text,bool needsDetection=false){Label=text;Active=false;Indeterminate=false;NeedsDetection=needsDetection;}
}
public sealed class MainModel : Observable
{
    public ObservableCollection<ManagedProject> Projects {get;}=new();
    private bool selectionMode;
    public bool SelectionMode {get=>selectionMode;set=>Set(ref selectionMode,value);}
    private string countCaption="最近 7 天更新";
    public string CountCaption {get=>countCaption;set=>Set(ref countCaption,value);}
    private string title="关注项目的新版本",subtitle="按收藏分组查看更新内容，将所需安装包保存到指定文件夹。",status="登录 GitHub 后同步收藏，或添加公开仓库开始使用。",account="登录 GitHub",count="0 个版本",empty="还没有版本记录",hint="点击「登录 GitHub」同步收藏，也可以添加公开仓库。";
    public string Title {get=>title;set=>Set(ref title,value);} public string Subtitle {get=>subtitle;set=>Set(ref subtitle,value);}
    public string Status {get=>status;set{if(status==value)return;Set(ref status,value);Changed(nameof(StatusSummary));}} public string StatusSummary=>StatusText.Summary(status);
    public string Account {get=>account;set=>Set(ref account,value);}
    public string Count {get=>count;set=>Set(ref count,value);} public string Empty {get=>empty;set=>Set(ref empty,value);}
    public string Hint {get=>hint;set=>Set(ref hint,value);}
    public ObservableCollection<Card> Cards {get;}=[];
    public ObservableCollection<GroupEntry> Groups {get;}=[];
}
public record GroupEntry(string Id,string Name,string Source,int Count);
public class SourceChange : Observable
{
    public GitChange Change {get;init;}=null!;
    public string Label=>Change.Label;
    private bool selected;
    public bool Selected {get=>selected;set=>Set(ref selected,value);}
}
public class MatchRow : Observable
{
    public InstalledApp App {get;init;}=null!;
    public string Name=>App.Name;
    public string Version=>App.Version;
    public IEnumerable<string> Choices {get;init;}=[];
    private bool selected;
    private string repository="",reason="";
    public bool Selected {get=>selected;set=>Set(ref selected,value);}
    public string Repository {get=>repository;set=>Set(ref repository,value);}
    public string Reason {get=>reason;set=>Set(ref reason,value);}
}

