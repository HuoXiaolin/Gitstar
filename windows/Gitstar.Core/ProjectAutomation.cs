using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace Gitstar.Core;

public record ProjectAutomation(string Repository,string SourceFolder,string ReleaseFolder,string VersionFile="",string Branch="",string TagPrefix="",string AssetPatterns=AutomationRules.DefaultAssets,string NotesFile="",bool IncludeInBatch=true,bool ReplaceSameVersion=true,bool? Prerelease=null);
public record AutomationPlan(string Version,string Tag,bool Prerelease,string VersionFile,string ReleaseFolder,string[] Assets);
public static class AutomationRules
{
    public const string DefaultAssets="*.exe;*.msi;*.zip;*.7z;*.apk;*.aab;*.dmg;*.deb;*.rpm;*.AppImage;*.tar.gz;*.tar.xz;SHA256SUMS.txt;使用说明.md";
    public static string VersionFile(string source,string release,string name)
    {
        foreach(var relative in new[]{"Directory.Build.props","windows/Directory.Build.props","package.json","version.txt","VERSION"})if(File.Exists(Path.Combine(source,relative)))return relative;
        var main=Path.Combine(release,name+".exe");return File.Exists(main)?main:"";
    }
    public static string ResolveFile(string source,string file)=>Path.GetFullPath(Path.IsPathRooted(file)?file:Path.Combine(source,file));
    public static string ReadVersion(string path)
    {
        string value;var extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is ".props" or ".csproj"){
            using var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});var document=XDocument.Load(reader);var groups=document.Descendants().Where(e=>e.Name.LocalName=="PropertyGroup"&&!e.Attributes().Any(a=>a.Name.LocalName=="Condition"));
            string Property(string key)=>groups.SelectMany(g=>g.Elements()).Where(e=>e.Name.LocalName==key&&!e.Attributes().Any(a=>a.Name.LocalName=="Condition")).Select(e=>e.Value.Trim()).LastOrDefault()??"";
            value=Property("Version");if(value.Length==0){value=Property("VersionPrefix");var suffix=Property("VersionSuffix");if(suffix.Length>0)value+="-"+suffix;}
        }else if(extension==".json"){using var doc=JsonDocument.Parse(File.ReadAllText(path));value=doc.RootElement.GetProperty("version").GetString()??"";}
        else if(extension is ".exe" or ".dll"){var metadata=FileVersionInfo.GetVersionInfo(path);value=metadata.ProductVersion??metadata.FileVersion??"";}
        else value=File.ReadAllText(path).Trim();
        value=value.Trim().TrimStart('v','V');if(Rules.CompareVersions(value,value)==null||!value.Contains('.'))throw new IOException("无法读取有效版本号："+path);return value;
    }
    public static string? FileVersion(string name)
    {
        var match=Regex.Match(name,@"(?<![0-9A-Za-z])v?(\d+(?:\.\d+){1,3}(?:-(?:alpha|beta|rc|preview|dev|nightly)(?:[.-]?\d+)*)?)(?!\d)",RegexOptions.IgnoreCase);
        return match.Success?match.Groups[1].Value:null;
    }
    public static AutomationPlan Plan(ProjectAutomation config)
    {
        if(!Rules.ValidRepository(config.Repository)||!Directory.Exists(config.SourceFolder)||!Directory.Exists(config.ReleaseFolder))throw new IOException("请配置有效的项目、源码目录和发行目录。");
        var versionFile=config.VersionFile.Length>0?config.VersionFile:VersionFile(config.SourceFolder,config.ReleaseFolder,config.Repository.Split('/').Last());if(versionFile.Length==0)throw new IOException("请配置版本号来源文件（props/csproj、package.json、文本或 EXE）。");
        var path=ResolveFile(config.SourceFolder,versionFile);var version=ReadVersion(path);var root=Path.GetFullPath(config.ReleaseFolder);var versionFolders=Directory.GetDirectories(root).Select(folder=>(Folder:folder,Version:Path.GetFileName(folder))).Where(item=>Rules.CompareVersions(item.Version,item.Version)!=null && item.Version.Contains('.')).ToArray();
        if(versionFolders.Length>0){var latest=versionFolders.Aggregate((a,b)=>Rules.CompareVersions(a.Version,b.Version)>=0?a:b);if(Rules.CompareVersions(latest.Version,version)!=0)throw new IOException("源码版本与最新发行子目录版本不一致："+version+" / "+latest.Version);root=latest.Folder;}
        if(new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("发行目录不能是目录链接。");
        var patterns=config.AssetPatterns.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);if(patterns.Length==0)throw new IOException("请至少配置一种安装包匹配规则。");
        var candidates=Directory.GetFiles(root).Where(file=>patterns.Any(pattern=>System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern,Path.GetFileName(file),true))).ToArray();
        if(candidates.Any(file=>FileVersion(Path.GetFileName(file)) is {} found && Rules.CompareVersions(found,version)>0))throw new IOException("发行目录中存在高于源码版本的安装包，请先完成构建后重试。");
        var assets=candidates.Where(file=>FileVersion(Path.GetFileName(file)) is not {} found || Rules.CompareVersions(found,version)==0).OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).ToArray();
        if(!assets.Any(file=>!Path.GetFileName(file).Equals("SHA256SUMS.txt",StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(file)!=".md"))throw new IOException("未找到当前版本的安装包。");
        foreach(var file in assets){var info=new FileInfo(file);if(info.Length==0||info.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("安装包为空或是文件链接："+info.Name);if(info.Extension.Equals(".exe",StringComparison.OrdinalIgnoreCase)){var metadata=System.Diagnostics.FileVersionInfo.GetVersionInfo(file);var binary=metadata.ProductVersion?.Trim();if(binary?.Length>0&&Rules.CompareVersions(binary,binary)!=null&&Rules.CompareVersions(binary,version)!=0)throw new IOException("安装包版本不一致："+info.Name+" · "+binary+" / "+version);}}
        return new(version,config.TagPrefix+version,config.Prerelease??version.Contains('-'),path,root,assets);
    }
}
