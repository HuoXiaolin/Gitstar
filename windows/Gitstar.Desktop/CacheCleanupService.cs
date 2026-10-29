using System.Text;
using System.Text.RegularExpressions;
namespace Gitstar.Desktop;

public record CacheSummary(CacheModule Module,string Name,string Description,long Bytes,int Items);
public record CacheCleanupResult(int Documents,int AvatarFiles,int TaskRecords,List<string> Warnings);
public static class LocalCacheFiles
{
    public static List<FileInfo> AvatarFiles(string root)
    {
        var parent=Path.GetFullPath(root);var folder=Path.GetFullPath(Path.Combine(parent,"avatars"));
        if(!folder.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("头像缓存目录越界。");
        foreach(var path in new[]{parent,folder})if(Directory.Exists(path)&&File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("头像缓存目录是链接，已保留。");
        if(!Directory.Exists(folder))return [];
        return new DirectoryInfo(folder).GetFiles().Where(file=>Regex.IsMatch(file.Name,@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}_avatar\.img$",RegexOptions.IgnoreCase)&&!file.Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList();
    }
}
public static class CacheCleanupService
{
    public static readonly CacheModule[] Modules=[CacheModule.RecentUpdates,CacheModule.InstalledApps,CacheModule.MyProjects,CacheModule.Rankings,CacheModule.Avatars,CacheModule.FinishedTasks];
    public static List<CacheSummary> Summaries(LocalStore store,string account)
    {
        var sizes=store.DocumentSizes();var result=new List<CacheSummary>();
        foreach(var module in new[]{CacheModule.RecentUpdates,CacheModule.InstalledApps,CacheModule.MyProjects,CacheModule.Rankings}){
            var keys=CacheRules.Keys(account,module);var documents=sizes.Where(item=>CacheRules.Matches(item.Key,keys)).ToArray();
            var name=module switch{CacheModule.RecentUpdates=>"最近更新",CacheModule.InstalledApps=>"本机可更新",CacheModule.Rankings=>"排行榜",_=>"我的项目"};
            var description=module switch{CacheModule.RecentUpdates=>"仓库、版本日志、GitHub 分组及网络缓存；本地关注和分组配置保留。",CacheModule.InstalledApps=>"本机扫描结果；软件绑定、便携软件与忽略设置保留。",CacheModule.Rankings=>"本机共享的公开榜单缓存；关注、Star、分组和使用统计保留。",_=>"项目列表、版本列表和源码检查结果；源码关联与自动发布配置保留。"};
            result.Add(new(module,name,description,documents.Sum(item=>item.Value),documents.Length));
        }
        try{var files=LocalCacheFiles.AvatarFiles(store.Root);result.Add(new(CacheModule.Avatars,"作者头像","共享头像文件与内存缓存，下次浏览时按需加载。",files.Sum(file=>file.Length),files.Count));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){result.Add(new(CacheModule.Avatars,"作者头像",ex.Message,-1,0));}
        var tasks=store.Get("operations/"+CacheRules.Account(account),new List<Operation>()).Where(CacheRules.Finished).ToList();result.Add(new(CacheModule.FinishedTasks,"已结束任务记录","清理已完成、已取消记录；保留暂停、失败、待核实与进行中的任务。记录清理后无法恢复。",tasks.Count==0?0:Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(tasks)),tasks.Count));return result;
    }
    public static async Task<CacheCleanupResult> Clear(LocalStore store,string account,CacheModule selected,CancellationToken ct,Action<CacheModule>? cleared=null)
    {
        var all=Modules.Aggregate(CacheModule.None,(flags,module)=>flags|module);if(selected==CacheModule.None||(selected&~all)!=0)throw new ArgumentException("请选择需要清理的模块。");ct.ThrowIfCancellationRequested();var documents=store.DocumentSizes();var keys=Modules.Where(module=>selected.HasFlag(module)).Select(module=>CacheRules.Keys(account,module)).ToArray();var exact=keys.SelectMany(item=>item.Exact).Distinct().ToArray();var prefixes=keys.SelectMany(item=>item.Prefixes).Distinct().ToArray();
        var root="cache/"+CacheRules.Account(account);
        if(selected.HasFlag(CacheModule.RecentUpdates)&&!selected.HasFlag(CacheModule.InstalledApps)&&!documents.ContainsKey(root+"/matched-releases")){var settings=store.Get("settings",new Settings());var bound=settings.Bindings.Select(binding=>binding.Repository).ToHashSet(StringComparer.OrdinalIgnoreCase);store.Set(root+"/matched-releases",store.Get(root+"/releases",new List<Release>()).Where(release=>bound.Contains(release.Repository)).ToList());}
        var removed=documents.Count(item=>exact.Contains(item.Key,StringComparer.Ordinal)||prefixes.Any(prefix=>item.Key.StartsWith(prefix,StringComparison.Ordinal)));store.RemoveDocuments(exact,prefixes);var records=0;var avatars=0;var warnings=new List<string>();
        if(selected.HasFlag(CacheModule.RecentUpdates))store.Set(root+"/recent-cleared",true);
        foreach(var module in new[]{CacheModule.RecentUpdates,CacheModule.InstalledApps,CacheModule.MyProjects})if(selected.HasFlag(module))cleared?.Invoke(module);
        if(selected.HasFlag(CacheModule.FinishedTasks)){var key="operations/"+CacheRules.Account(account);var tasks=store.Get(key,new List<Operation>());records=tasks.RemoveAll(task=>CacheRules.Finished(task));store.Set(key,tasks);cleared?.Invoke(CacheModule.FinishedTasks);}
        if(selected.HasFlag(CacheModule.Avatars))try{avatars=await AvatarCache.ClearDisk(store.Root,ct);cleared?.Invoke(CacheModule.Avatars);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){warnings.Add("头像缓存未完全清理："+ex.Message);}
        try{await Task.Run(store.Compact);}catch(IOException ex){warnings.Add("缓存记录已清理，数据库空间整理暂未完成："+ex.Message);}
        return new(removed,avatars,records,warnings);
    }
}
