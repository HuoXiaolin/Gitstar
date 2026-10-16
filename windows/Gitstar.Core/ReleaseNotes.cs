namespace Gitstar.Core;

public static class ReleaseNotes
{
    public const string DefaultText="更新应用与安装包。";
    public static string FilePath(string source,string configured)
    {
        if(!string.IsNullOrWhiteSpace(configured))return AutomationRules.ResolveFile(source,configured.Trim());
        foreach(var name in new[]{"CHANGELOG.md","Changelog.md","changelog.md"}){
            var path=Path.Combine(source,name);if(File.Exists(path))return Path.GetFullPath(path);
        }
        return "";
    }
    public static string Read(string source,string configured,string? manual)
    {
        if(manual!=null){if(string.IsNullOrWhiteSpace(manual))return DefaultText;if(System.Text.Encoding.UTF8.GetByteCount(manual)>100000)throw new IOException("更新说明超过 100 KB，请精简后重试。");return manual;}
        var path=FilePath(source,configured);if(path.Length==0)return DefaultText;
        if(!File.Exists(path))throw new IOException("更新日志文件不存在："+path);
        if(new FileInfo(path).Length>100000)throw new IOException("更新日志文件超过 100 KB，请选择精简后的日志。");
        var text=File.ReadAllText(path);return string.IsNullOrWhiteSpace(text)?DefaultText:text;
    }
}
