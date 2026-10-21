namespace Gitstar.Core;

public record Asset(long Id, string Name, long Size, string Url, string? Digest);
public record Release(long Id, string Repository, string Tag, string Name, string Body, bool Prerelease, DateTimeOffset Published, string Url, List<Asset> Assets)
{
    public bool IsLatest {get;init;}
    public string Channel => Prerelease ? "测试版" : "正式版";
    public string Date => Published.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
public record Repository(string FullName, string Description, bool Private, bool Starred=false, string NodeId="")
{
    public RepositoryMetadata Metadata {get;init;}=new();
    public DateTimeOffset? Updated {get;init;}
}
public enum UpdateSort { Published, RepositoryUpdated }
public record StarGroup(string Id, string Name, string Source, List<string> Repositories, string Description="", bool IsPrivate=false);
public record InstalledApp(string Id, string Name, string Version, string Location, bool Portable = false, string RepositoryHint = "",string ExecutablePath="",string VersionSource="注册表",string RegisteredVersion="",string[]? AliasIds=null)
{
    public string VersionDetails {get;init;}="";
    public bool VersionNeedsReview {get;init;}
}
public class Binding
{
    public string ConfirmedVersion {get;set;}="";
    public string ConfirmedVersionPath {get;set;}="";
    public string ConfirmedVersionHash {get;set;}="";
    public string AppName {get;set;}="";
    public string AppLocation {get;set;}="";
    public string[] AliasIds {get;set;}=[];
    public Binding Snapshot()=>(Binding)MemberwiseClone();
    public string ExecutablePath {get;set;}="";
    public string AppId { get; set; } = "";
    public string Repository { get; set; } = "";
    public string AssetPattern { get; set; } = "";
    public string TagPrefix { get; set; } = "";
    public bool IncludePrerelease { get; set; }
    public bool Disabled { get; set; }
    public string IgnoredTag { get; set; } = "";
    public DateTimeOffset? PauseUntil { get; set; }
    public bool Enabled(DateTimeOffset now) => !Disabled && (PauseUntil == null || PauseUntil <= now);
}
public class Settings
{
    public List<string> SpecialRepositories {get;set;}=[];
    public List<FollowHistoryEntry> FollowHistory {get;set;}=[];
    public int BulkDownloadDays {get;set;}=7;
    public List<string> BulkDownloadExtensions {get;set;}=[".exe",".msi",".zip"];
    public UpdateSort RecentSort {get;set;}
    public UpdateSort ProjectsSort {get;set;}=UpdateSort.RepositoryUpdated;
    public int SameVersionDownload {get;set;}
    public bool KeepLatestPackages {get;set;}
    public bool DeleteInstalledPackages {get;set;}
    public bool PromptInstalledPackageCleanup {get;set;}=true;
    public string ClientId { get; set; } = "";
    public string Account { get; set; } = "";
    public string DownloadFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Gitstar_Download");
    public string Proxy { get; set; } = "";
    public bool DirectConnection { get; set; }
    public bool Startup { get; set; }
    public bool Dark { get; set; }
    public DateTimeOffset? LastCheck { get; set; }
    public int RefreshIntervalHours { get; set; } = 24;
    public List<Binding> Bindings { get; set; } = [];
    public List<InstalledApp> PortableApps { get; set; } = [];
    public List<string> ExtraRepositories { get; set; } = [];
    public List<string> HiddenRepositories { get; set; } = [];
    public List<StarGroup> LocalGroups { get; set; } = [];
    public List<string> Notified { get; set; } = [];
    public string SelfRepository { get; set; } = AppInfo.Repository;
}
public record DownloadedPackage(string Repository,long ReleaseId,string Tag,bool Prerelease,DateTimeOffset Published,string AssetName,string Path,string Root,long Size,string Sha256,string Family,bool Prompted=false)
{
    public bool RetainForBatch {get;init;}
}
public record UpdateCandidate(InstalledApp App, Binding Binding, Release Release, Asset? Asset);
public class Operation
{
    public List<string> Results {get;set;}=[];
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public string State { get; set; } = "等待";
    public string Repository { get; set; } = "";
    public string Tag { get; set; } = "";
    public string File { get; set; } = "";
    public long? RemoteId { get; set; }
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;
}

