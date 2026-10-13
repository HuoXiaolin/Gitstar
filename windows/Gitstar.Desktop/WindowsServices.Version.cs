using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public static partial class WindowsServices
{
    public static List<InstalledApp> ScanMatched(IEnumerable<Binding> bindings,IEnumerable<InstalledApp> portable,IEnumerable<InstalledApp>? known=null)
    {
        var subscriptions=bindings.ToArray();var saved=portable.Concat(known??[]).ToArray();var list=new List<InstalledApp>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var cache=new Dictionary<string,ProgramVersion?>(StringComparer.OrdinalIgnoreCase);
        foreach(var binding in subscriptions){
            var previous=InstalledVersion.Find(saved,binding.AppId);
            var ids=(previous?.AliasIds??[]).Append(binding.AppId).Distinct(StringComparer.OrdinalIgnoreCase);
            var found=false;
            foreach(var id in ids){
                if(!seen.Add(id))continue;var parts=id.Split('/',3);
                if(parts.Length!=3 || !Enum.TryParse<RegistryHive>(parts[0],out var hive) || hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser) || !Enum.TryParse<RegistryView>(parts[1],out var view) || view is not (RegistryView.Registry64 or RegistryView.Registry32))continue;
                try{using var root=RegistryKey.OpenBaseKey(hive,view);using var entry=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"+parts[2]);
                    if(entry==null)continue;var name=entry.GetValue("DisplayName") as string;if(string.IsNullOrWhiteSpace(name))continue;
                    var item=new InstalledApp(id,name,entry.GetValue("DisplayVersion") as string??"未知",entry.GetValue("InstallLocation") as string??"",RepositoryHint:previous?.RepositoryHint??"",ExecutablePath:binding.ExecutablePath);
                    list.Add(DetectVersion(item,entry.GetValue("DisplayIcon") as string??"",entry.GetValue("UninstallString") as string??"",cache));found=true;
                }catch(Exception ex)when(ex is System.Security.SecurityException or IOException or UnauthorizedAccessException){}
            }
            if(!found && previous!=null && (previous.Portable || File.Exists(previous.ExecutablePath) || File.Exists(binding.ExecutablePath)))list.Add(DetectVersion(previous with{ExecutablePath=binding.ExecutablePath.Length>0?binding.ExecutablePath:previous.ExecutablePath},"","",cache));
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
            try{if(!File.Exists(path)){cache[path]=null;continue;}var info=FileVersionInfo.GetVersionInfo(path);cache[path]=new(path,info.ProductName??"",info.FileDescription??"",info.ProductVersion??"",info.FileVersion??"");}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){cache[path]=null;}
        }
        var declared=app.Portable?app.Version:app.RegisteredVersion.Length>0?app.RegisteredVersion:app.Version;
        var iconIsProgram=preferred.Length>0 && !preferred.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        var evidence=InstalledVersion.Resolve(app.Name,declared,paths.Select(p=>cache.GetValueOrDefault(p)).OfType<ProgramVersion>(),preferred,app.ExecutablePath.Length>0 || iconIsProgram);
        if(evidence.Path.Length==0 && location.Length>0 && !Directory.Exists(location))evidence=new("未知","登记安装路径已失效，需确认当前主程序","");
        return app with{Version=evidence.Version,ExecutablePath=evidence.Path,VersionSource=evidence.Source,RegisteredVersion=declared};
    }
    private static List<InstalledApp> MergeInstallRecords(IEnumerable<InstalledApp> source)
    {
        // Merge only proven identical executables; never use the highest version from a different installation.
        return source.GroupBy(a=>a.ExecutablePath.Length>0?a.Name.ToLowerInvariant()+"|"+a.ExecutablePath.ToLowerInvariant():"id:"+a.Id).Select(g=>g.First() with{AliasIds=g.SelectMany(a=>(a.AliasIds??[]).Append(a.Id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()}).OrderBy(a=>a.Name).ToList();
    }
}


