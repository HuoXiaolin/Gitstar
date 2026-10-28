using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace Gitstar.Desktop;

public static partial class WindowsServices
{
    internal static string SoftwareInputPath(string input)
    {
        var path=Path.GetFullPath(Environment.ExpandEnvironmentVariables(input.Trim().Trim('"')));
        if(path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)){
            var target=ShortcutProgram(path);
            if(target.Length==0)throw new IOException("快捷方式没有指向可读取的 EXE，请直接选择软件主程序。");
            path=target;
        }
        return path;
    }
    private static string ShortcutProgram(string path)
    {
        object? shell=null,link=null;
        try{
            if(!File.Exists(path))return "";
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link=((dynamic)shell!).CreateShortcut(path);string target=((dynamic)link).TargetPath;
            target=Environment.ExpandEnvironmentVariables(target);
            return Path.IsPathFullyQualified(target)&&target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&!target.StartsWith(@"\\")?target:"";
        }catch(Exception ex)when(ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException){return "";}
        finally{if(link!=null&&Marshal.IsComObject(link))Marshal.FinalReleaseComObject(link);if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
    }
    // Bounded discovery of local, known application locations. Never walk all AppData or a disk.
    internal static List<InstalledApp> DiscoverUserPrograms(IEnumerable<string> roots,IEnumerable<string>? shortcuts=null,CancellationToken ct=default)
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Local(string path){try{return !path.StartsWith(@"\\")&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){return false;}}
        string[] Dirs(string root){try{return Directory.EnumerateDirectories(root).Take(250).Where(path=>!Path.GetFileName(path).StartsWith('.')).Where(Local).ToArray();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return [];}}
        void Read(string folder){ct.ThrowIfCancellationRequested();if(!Local(folder))return;try{foreach(var path in Directory.EnumerateFiles(folder,"*.exe").Take(100).Where(Local).Where(p=>!PortableHelper(p)))paths.Add(path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}}
        foreach(var root in roots.Where(Directory.Exists).Where(Local))foreach(var folder in Dirs(root)){
            Read(folder);foreach(var child in Dirs(folder).Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"^(?:bin|app|current|latest|app-.+)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)))Read(child);
        }
        foreach(var path in shortcuts??[]){ct.ThrowIfCancellationRequested();if(path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&Local(path)&&!PortableHelper(path))paths.Add(path);}
        var result=new List<InstalledApp>();foreach(var path in paths){ct.ThrowIfCancellationRequested();try{
            if(path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path),@"(?:^|[-_. ])(?:updater?|uninstall|unins\d*|helper|crashpad_handler|proxy|pwa_launcher)(?:$|[-_. ])",System.Text.RegularExpressions.RegexOptions.IgnoreCase))continue;
            var app=DescribePortable(path);var info=System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            if(app.Name.Contains("Windows",StringComparison.OrdinalIgnoreCase)&&app.Name.Contains("Operating System",StringComparison.OrdinalIgnoreCase))continue;
            if(app.Name.StartsWith("Microsoft Office",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(info.FileDescription))app=app with{Name=info.FileDescription.Trim()};
            result.Add(app with{Portable=false});
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){}}
        return result;
    }
    internal static List<InstalledApp> UserPrograms(CancellationToken ct=default)
    {
        var targets=new List<string>();
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})try{
            using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view);using var root=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
            if(root!=null)foreach(var name in root.GetSubKeyNames()){ct.ThrowIfCancellationRequested();using var entry=root.OpenSubKey(name);var path=ProgramPath(entry?.GetValue("") as string??"");if(path.Length>0)targets.Add(path);}
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){}
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)}){
            var queue=new Queue<(string Path,int Depth)>();queue.Enqueue((root,0));var visited=0;
            while(queue.TryDequeue(out var item)&&visited++<250){ct.ThrowIfCancellationRequested();try{
                if(!Directory.Exists(item.Path)||(File.GetAttributes(item.Path)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(var link in Directory.EnumerateFiles(item.Path,"*.lnk").Take(100)){var target=ShortcutProgram(link);if(target.Length>0)targets.Add(target);}
                if(item.Depth<3)foreach(var child in Directory.EnumerateDirectories(item.Path).Take(100))queue.Enqueue((child,item.Depth+1));
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}}
        }
        return DiscoverUserPrograms([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Programs")],targets,ct);
    }
}
