namespace Gitstar.Core;

public static class ReleaseNotes
{
    public static string FilePath(string source,string configured)
    {
        if(!string.IsNullOrWhiteSpace(configured))return AutomationRules.ResolveFile(source,configured.Trim());
        foreach(var name in new[]{"CHANGELOG.md","Changelog.md","changelog.md"}){
            var path=Path.Combine(source,name);if(File.Exists(path))return Path.GetFullPath(path);
        }
        return "";
    }
    public static string? Read(string source,string configured,string? manual)
    {
        if(manual!=null){if(string.IsNullOrWhiteSpace(manual))throw new IOException("请填写更新说明，或切换为读取日志文件。");if(System.Text.Encoding.UTF8.GetByteCount(manual)>100000)throw new IOException("更新说明超过 100 KB，请精简后重试。");return manual;}
        var path=FilePath(source,configured);if(path.Length==0)return null;
        if(!File.Exists(path))throw new IOException("更新日志文件不存在："+path);
        if(new FileInfo(path).Length>100000)throw new IOException("更新日志文件超过 100 KB，请选择精简后的日志。");
        var text=File.ReadAllText(path);if(string.IsNullOrWhiteSpace(text))throw new IOException("更新日志文件为空，请填写内容或切换为手动输入。");return text;
    }
}
