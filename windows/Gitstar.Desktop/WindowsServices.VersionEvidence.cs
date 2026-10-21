using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;

namespace Gitstar.Desktop;

public static partial class WindowsServices
{
    internal static List<ProgramVersion> RelatedRunningPrograms(IEnumerable<Binding> bindings)
    {
        var names=bindings.SelectMany(binding=>new[]{binding.AppName,Path.GetFileNameWithoutExtension(binding.ExecutablePath)}).Select(IdentityName).Where(name=>name.Length>0).ToHashSet(StringComparer.Ordinal);
        var programs=new Dictionary<string,ProgramVersion>(StringComparer.OrdinalIgnoreCase);
        foreach(var process in Process.GetProcesses())using(process){
            try{
                if(!names.Contains(IdentityName(process.ProcessName)))continue;
                var path=process.MainModule?.FileName;if(path==null||programs.ContainsKey(path))continue;
                var info=FileVersionInfo.GetVersionInfo(path);programs[path]=ReadApplicationVersion(new(path,info.ProductName??"",info.FileDescription??"",info.ProductVersion??"",info.FileVersion??""));
            }catch(Exception ex)when(ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException){}
        }
        return programs.Values.ToList();
    }
    internal static InstalledApp NoteOtherRunningCopies(InstalledApp app,IEnumerable<ProgramVersion> running)
    {
        if(app.ExecutablePath.Length==0)return app;
        var name=IdentityName(Path.GetFileNameWithoutExtension(app.ExecutablePath));
        var related=running.Where(program=>IdentityName(Path.GetFileNameWithoutExtension(program.Path))==name).ToArray();
        var selectedIsRunning=related.Any(program=>program.Path.Equals(app.ExecutablePath,StringComparison.OrdinalIgnoreCase));
        var other=related.Where(program=>!program.Path.Equals(app.ExecutablePath,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(other.Length==0)return app;
        return app with{VersionNeedsReview=app.VersionNeedsReview||!selectedIsRunning,VersionDetails=app.VersionDetails+(selectedIsRunning?"\n另有运行副本，当前按已配置且正在运行的位置记录：\n":"\n检测到另一个运行副本，请核对实际安装位置：\n")+string.Join("\n",other.Select(program=>program.Path+" · "+(InstalledVersion.NormalizeVersion(program.ApplicationVersion.Length>0?program.ApplicationVersion:program.ProductVersion))))};
    }
    private static string IdentityName(string value)=>new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    internal static ProgramVersion ReadApplicationVersion(ProgramVersion program)
    {
        var folder=Path.GetDirectoryName(program.Path);if(folder==null)return program;
        var declarations=new List<(string Version,string Source)>();
        // Read only small, local application declarations. Never search dependency trees,
        // infer versions from arbitrary binary strings, or launch unknown programs.
        foreach(var relative in new[]{"version.json","VERSION","version.txt",Path.Combine("resources","app","package.json"),"package.json"}){
            var path=Path.Combine(folder,relative);
            try{
                if(!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||new FileInfo(path).Length>64*1024)continue;
                var text=File.ReadAllText(path).Trim();var version="";
                if(path.EndsWith(".json",StringComparison.OrdinalIgnoreCase)){
                    using var document=JsonDocument.Parse(text);var root=document.RootElement;if(root.ValueKind!=JsonValueKind.Object)continue;
                    if(relative.EndsWith("package.json",StringComparison.OrdinalIgnoreCase)){
                        if(!root.TryGetProperty("name",out var name)||name.ValueKind!=JsonValueKind.String)continue;
                        var actual=IdentityName(name.GetString()??"");
                        var expected=new[]{program.ProductName,program.Description,Path.GetFileNameWithoutExtension(program.Path)}.Select(IdentityName).Where(value=>value.Length>0);
                        if(!expected.Contains(actual,StringComparer.Ordinal))continue;
                    }
                    if(root.TryGetProperty("version",out var property)&&property.ValueKind==JsonValueKind.String)version=property.GetString()??"";
                }else if(text.Length<=128&&!text.Contains('\n'))version=text;
                if(InstalledVersion.NormalizeVersion(version).Length>0)declarations.Add((InstalledVersion.NormalizeVersion(version),"应用版本声明（"+relative+"）"));
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){}
        }
        if(declarations.Count==0)return program;
        return program with{ApplicationVersion=declarations[0].Version,ApplicationVersionSource=declarations[0].Source,ApplicationDeclarationsConflict=declarations.Select(item=>item.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1,ApplicationDeclarations=string.Join("\n",declarations.Select(item=>item.Source+"："+item.Version))};
    }
    internal static (string Version,string Path,string Hash) ConfirmInstalledVersion(string version,string program)
    {
        if(string.IsNullOrWhiteSpace(version))return("","","");
        var normalized=InstalledVersion.NormalizeVersion(version);if(normalized.Length==0)throw new IOException("请输入应用关于页显示的有效版本号。");
        if(!File.Exists(program)||!program.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("确认版本前请选择实际安装的主程序 EXE。");
        using var file=File.OpenRead(program);return(normalized,Path.GetFullPath(program),Convert.ToHexString(SHA256.HashData(file)));
    }
    internal static InstalledApp ApplyConfirmedVersion(InstalledApp app,IEnumerable<Binding> bindings)
    {
        var entries=bindings.Where(binding=>binding.ConfirmedVersion.Length>0).ToArray();if(entries.Length==0)return app;
        var versions=entries.Select(binding=>binding.ConfirmedVersion).Distinct(StringComparer.Ordinal).ToArray();
        var invalid=versions.Length!=1||app.ExecutablePath.Length==0;
        string? hash=null;
        foreach(var binding in entries){
            try{
                if(!Path.GetFullPath(app.ExecutablePath).Equals(binding.ConfirmedVersionPath,StringComparison.OrdinalIgnoreCase)||!File.Exists(app.ExecutablePath)){invalid=true;continue;}
                if(hash==null){using var file=File.OpenRead(app.ExecutablePath);hash=Convert.ToHexString(SHA256.HashData(file));}
                if(hash!=binding.ConfirmedVersionHash)invalid=true;
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){invalid=true;}
        }
        if(invalid)return app with{Version="未知",VersionSource="原确认版本已失效，请核对实际程序",VersionNeedsReview=true,VersionDetails=app.VersionDetails+"\n程序路径或文件已变化，未沿用之前确认的版本。"};
        return app with{Version=versions[0],VersionSource="用户确认的应用版本（主程序校验一致）",VersionNeedsReview=false,VersionDetails=app.VersionDetails+"\n用户确认的应用版本："+versions[0]};
    }
}
