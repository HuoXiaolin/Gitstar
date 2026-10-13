namespace Gitstar.Core;

public static class ManualReleaseRules
{
    public static string Version(string entered,IEnumerable<string> packages,string folder,string name)
    {
        var typed=entered.Trim();if(typed.Length>0){if(!ReleaseRules.ValidTag(typed))throw new IOException("请填写有效版本号，例如 1.0.0.0。");return typed;}
        var selected=packages.ToArray();var main=selected.FirstOrDefault(p=>Path.GetFileName(p).Equals(name+".exe",StringComparison.OrdinalIgnoreCase));
        if(main==null&&File.Exists(Path.Combine(folder,name+".exe")))main=Path.Combine(folder,name+".exe");
        if(main!=null)return AutomationRules.ReadVersion(main);
        var candidates=selected.Where(p=>Path.GetExtension(p).Equals(".exe",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(p).Equals(".dll",StringComparison.OrdinalIgnoreCase)).ToArray();
        var applications=candidates.Where(p=>!Path.GetFileNameWithoutExtension(p).Contains("setup",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileNameWithoutExtension(p).Contains("install",StringComparison.OrdinalIgnoreCase)).ToArray();if(applications.Length>0)candidates=applications;
        var versions=new List<string>();foreach(var path in candidates){try{versions.Add(AutomationRules.ReadVersion(path));}catch(IOException){}}
        var distinct=versions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(distinct.Length==1)return distinct[0];
        if(distinct.Length>1)throw new IOException("所选应用文件的版本号不一致，请选择同一版本的文件或填写版本号。");
        throw new IOException("无法从应用文件读取版本号，请选择带版本信息的应用文件，或填写版本号。");
    }
    public static string[] Assets(string folder,string patterns)
    {
        if(!Directory.Exists(folder))return [];
        var rules=patterns.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        return Directory.GetFiles(folder).Where(file=>rules.Any(pattern=>System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern,Path.GetFileName(file),true))).OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
