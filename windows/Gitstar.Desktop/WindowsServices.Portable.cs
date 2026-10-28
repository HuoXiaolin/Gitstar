using System.Diagnostics;
using System.Text.RegularExpressions;
namespace Gitstar.Desktop;

public static partial class WindowsServices
{
    private static bool PortableHelper(string path)=>Regex.IsMatch(Path.GetFileNameWithoutExtension(path),@"^(?:unins|uninstall|setup|update|crash|helper)",RegexOptions.IgnoreCase);
    public static string[] PortableExecutables(string input)
    {
        if(string.IsNullOrWhiteSpace(input))throw new IOException("请选择主程序 EXE 或软件所在文件夹。");
        var path=SoftwareInputPath(input);
        if(File.Exists(path)){if(!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||PortableHelper(path))throw new IOException("请选择软件主程序 EXE，避免安装器、卸载器和更新器。");return [path];}
        if(!Directory.Exists(path))throw new IOException("软件位置不存在，请重新选择主程序或文件夹。");
        if(path.TrimEnd('\\','/').Equals(Path.GetPathRoot(path)?.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new IOException("请选择具体软件文件夹，不要选择整个磁盘。");
        string[] Files(string folder){try{return Directory.EnumerateFiles(folder,"*.exe",SearchOption.TopDirectoryOnly).Take(200).Where(file=>!PortableHelper(file)).ToArray();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return [];}}
        var candidates=Files(path);
        if(candidates.Length==0){var folders=Directory.EnumerateDirectories(path).Take(30).Where(folder=>(File.GetAttributes(folder)&FileAttributes.ReparsePoint)==0).Where(folder=>Regex.IsMatch(Path.GetFileName(folder),@"^(?:bin|app|current|latest|app-.+)$",RegexOptions.IgnoreCase));candidates=folders.SelectMany(Files).Take(200).ToArray();}
        if(candidates.Length==0)throw new IOException("此文件夹没有找到主程序，请直接选择 EXE 或选择更具体的软件目录。");
        var name=Path.GetFileName(path.TrimEnd('\\','/'));return candidates.OrderByDescending(file=>Path.GetFileNameWithoutExtension(file).Equals(name,StringComparison.OrdinalIgnoreCase)).ThenBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static InstalledApp DescribePortable(string executable,string name="",string version="")
    {
        if(string.IsNullOrWhiteSpace(executable))throw new IOException("请先选择软件的主程序 EXE。");
        var path=SoftwareInputPath(executable);
        if(!File.Exists(path)||!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("请选择存在的主程序 EXE。");
        var basename=Path.GetFileNameWithoutExtension(path);
        if(PortableHelper(path))throw new IOException("请选择软件主程序，当前文件看起来是安装器、卸载器或更新工具。");
        var info=FileVersionInfo.GetVersionInfo(path);var title=name.Trim();if(title.Length==0)title=new[]{info.ProductName,info.FileDescription,basename}.First(value=>!string.IsNullOrWhiteSpace(value))!.Trim();
        var product=InstalledVersion.NormalizeVersion(info.ProductVersion??"");var file=InstalledVersion.NormalizeVersion(info.FileVersion??"");var typed=InstalledVersion.NormalizeVersion(version);
        if(version.Trim().Length>0&&typed.Length==0)throw new IOException("当前版本格式无法识别，可清空后自动读取，或填写 2.0.0 这样的版本号。");
        var actual=typed.Length>0?typed:product.Length>0?product:file;
        return new(ManualSoftware.PortableId(path),title,actual.Length>0?actual:"未知",Path.GetDirectoryName(path)!,true,ExecutablePath:path,VersionSource:typed.Length>0?"手动确认版本":product.Length>0?"程序产品版本":file.Length>0?"程序文件版本":"未检测到程序版本",RegisteredVersion:actual);
    }
}
