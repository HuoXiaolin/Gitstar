using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public static partial class WindowsServices
{
    internal record RegisteredProgram(InstalledApp App,string Icon="",string Uninstall="");
    private static List<RegisteredProgram> RegisteredPrograms(Action<string>? warning=null)
    {
        var list=new List<RegisteredProgram>();
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            try{
                using var root=RegistryKey.OpenBaseKey(hive,view);using var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if(uninstall==null)continue;
                foreach(var key in uninstall.GetSubKeyNames())try{
                    using var entry=uninstall.OpenSubKey(key);var name=entry?.GetValue("DisplayName") as string;
                    if(string.IsNullOrWhiteSpace(name)||Convert.ToInt32(entry?.GetValue("SystemComponent")??0)==1)continue;
                    var hint=new[]{"URLInfoAbout","URLUpdateInfo","HelpLink"}.Select(field=>AppMatcher.GitHubRepository(entry?.GetValue(field) as string??"")).FirstOrDefault(value=>value.Length>0)??"";
                    var icon=entry?.GetValue("DisplayIcon") as string??"";var candidate=ProgramPath(icon);
                    if(warning!=null&&candidate.StartsWith(@"\\",StringComparison.Ordinal)){warning("网络主程序位置暂不自动核对。");candidate="";}
                    if(candidate.Length>0&&(!File.Exists(candidate)||candidate.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(Path.GetFileNameWithoutExtension(candidate),@"^(unins|uninstall|setup|update|crash|helper|rundll)",RegexOptions.IgnoreCase)))candidate="";
                    list.Add(new(new($"{hive}/{view}/{key}",name,entry?.GetValue("DisplayVersion") as string??"未知",entry?.GetValue("InstallLocation") as string??"",RepositoryHint:hint,ExecutablePath:candidate),icon,entry?.GetValue("UninstallString") as string??""));
                }catch(Exception ex)when(ex is System.Security.SecurityException or IOException or UnauthorizedAccessException){warning?.Invoke("部分软件安装登记无法读取："+ex.Message);}
            }catch(Exception ex)when(ex is System.Security.SecurityException or IOException or UnauthorizedAccessException){warning?.Invoke("部分安装登记范围无法读取："+ex.Message);}
        }
        return list;
    }
    private static List<InstalledApp> ScanCurrentPrograms(IEnumerable<Binding> bindings,IEnumerable<InstalledApp> portable,IEnumerable<InstalledApp> known,bool matchedOnly)
    {
        var subscriptions=bindings.ToArray();return ScanInventory(RegisteredPrograms(),subscriptions,portable,known,matchedOnly,RelatedRunningPrograms(subscriptions));
    }
    public static List<InstalledApp> ScanMatched(IEnumerable<Binding> bindings,IEnumerable<InstalledApp> portable,IEnumerable<InstalledApp>? known=null)=>ScanCurrentPrograms(bindings,portable,known??[],true);
    internal static List<InstalledApp> ScanInventory(IEnumerable<RegisteredProgram> registry,IEnumerable<Binding> bindings,IEnumerable<InstalledApp> portable,IEnumerable<InstalledApp> known,bool matchedOnly,IEnumerable<ProgramVersion>? running=null,CancellationToken ct=default)
    {
        var records=registry.Concat(portable.Select(app=>new RegisteredProgram(app))).ToList();var apps=records.Select(record=>record.App).ToArray();
        var subscriptions=bindings.ToArray();var saved=known.ToArray();var targets=new Dictionary<string,List<Binding>>(StringComparer.OrdinalIgnoreCase);
        var previousIds=subscriptions.SelectMany(binding=>binding.AliasIds.Append(binding.AppId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var binding in subscriptions){
            ct.ThrowIfCancellationRequested();
            var target=InstalledIdentity.Target(binding,apps,saved);
            if(target==null){
                var previous=InstalledVersion.Find(saved,binding.AppId);
                var path=binding.ExecutablePath.Length>0?binding.ExecutablePath:previous?.ExecutablePath??"";
                if(!File.Exists(path))continue;
                var fallback=previous??new InstalledApp(binding.AppId,binding.AppName.Length>0?binding.AppName:Path.GetFileNameWithoutExtension(path),"未知",Path.GetDirectoryName(path)??"");
                target=fallback with{ExecutablePath=path};records.Add(new(target));
            }
            if(!targets.TryGetValue(target.Id,out var choices))targets[target.Id]=choices=[];
            choices.Add(binding);
        }
        var cache=new Dictionary<string,ProgramVersion?>(StringComparer.OrdinalIgnoreCase);var list=new List<InstalledApp>();
        foreach(var record in records.DistinctBy(record=>record.App.Id,StringComparer.OrdinalIgnoreCase)){
            ct.ThrowIfCancellationRequested();
            targets.TryGetValue(record.App.Id,out var matches);if(matchedOnly&&matches==null&&!previousIds.Contains(record.App.Id))continue;
            var preferred=(matches??[]).Select(binding=>binding.ExecutablePath).Where(path=>path.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var app=preferred.Length==1?record.App with{ExecutablePath=preferred[0]}:record.App;
            var locations=(matches??[]).Select(binding=>binding.AppLocation).Where(path=>path.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(app.Location.Length==0&&locations.Length==1)app=app with{Location=locations[0]};
            list.Add(ApplyConfirmedVersion(NoteOtherRunningCopies(DetectVersion(app,record.Icon,record.Uninstall,cache),running??[]),matches??[]));
        }
        return MergeInstallRecords(list);
    }
    private static string ProgramPath(string command)
    {
        var match=Regex.Match(Environment.ExpandEnvironmentVariables(command.Trim()),"^(?:\"(?<path>[^\"]+\\.exe)\"|(?<path>.+?\\.exe)(?:,|\\s|$))",RegexOptions.IgnoreCase);
        var path=match.Groups["path"].Value;return Path.IsPathFullyQualified(path)?path:"";
    }
    private static InstalledApp DetectVersion(InstalledApp app,string icon,string uninstall,Dictionary<string,ProgramVersion?> cache)
    {
        if(app.ExecutablePath.Length>0&&!File.Exists(app.ExecutablePath))return app with{Version="未知",VersionSource="指定安装位置的主程序不存在或无法访问，请核对路径",RegisteredVersion=app.RegisteredVersion.Length>0?app.RegisteredVersion:app.Version};
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var preferred=app.ExecutablePath.Length>0?app.ExecutablePath:ProgramPath(icon);
        if(preferred.Length>0)paths.Add(preferred);
        var location=Environment.ExpandEnvironmentVariables(app.Location.Trim().Trim('"'));
        var folders=new[]{location,Path.GetDirectoryName(ProgramPath(uninstall))??"",Path.GetDirectoryName(preferred)??""}.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(var folder in folders){
            var full=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if(full.Equals(Environment.GetFolderPath(Environment.SpecialFolder.System),StringComparison.OrdinalIgnoreCase)||full.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),StringComparison.OrdinalIgnoreCase)||full.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),StringComparison.OrdinalIgnoreCase))continue;
            try{foreach(var file in Directory.EnumerateFiles(full,"*.exe",SearchOption.TopDirectoryOnly).Take(100))paths.Add(file);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
        }
        foreach(var path in paths){
            if(cache.ContainsKey(path))continue;
            try{if(!File.Exists(path)){cache[path]=null;continue;}var info=FileVersionInfo.GetVersionInfo(path);cache[path]=ReadApplicationVersion(new(path,info.ProductName??"",info.FileDescription??"",info.ProductVersion??"",info.FileVersion??""));}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){cache[path]=null;}
        }
        var declared=app.Portable?app.Version:app.RegisteredVersion.Length>0?app.RegisteredVersion:app.Version;
        var iconIsProgram=preferred.Length>0 && !preferred.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        var evidence=InstalledVersion.Resolve(app.Name,declared,paths.Select(p=>cache.GetValueOrDefault(p)).OfType<ProgramVersion>(),preferred,app.ExecutablePath.Length>0 || iconIsProgram);
        if(evidence.Path.Length==0 && location.Length>0 && !Directory.Exists(location))evidence=new("未知","登记安装路径已失效，需确认当前主程序","");
        else if(evidence.Path.Length==0&&app.ExecutablePath.Length>0)evidence=new("未知","原安装位置的主程序缺失或无法读取版本，需核对路径","");
        return app with{Version=evidence.Version,ExecutablePath=evidence.Path,VersionSource=evidence.Source,RegisteredVersion=declared,VersionDetails=evidence.Details,VersionNeedsReview=evidence.NeedsReview};
    }
    private static List<InstalledApp> MergeInstallRecords(IEnumerable<InstalledApp> source)
    {
        // Merge only proven identical executables; never use the highest version from a different installation.
        return InstalledIdentity.Merge(source);
    }
}


