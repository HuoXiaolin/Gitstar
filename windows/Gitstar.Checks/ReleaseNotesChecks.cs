using Gitstar.Core;
using System.IO;
using System.Text.Json;

internal static class ReleaseNotesChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var source=Path.Combine(root,"notes");Directory.CreateDirectory(source);var log=Path.Combine(source,"CHANGELOG.md");
        check(ReleaseNotes.Read(source,"",null)==null,"missing default log permits generated automatic notes");
        File.WriteAllText(log,"版本一：修复连接");check(ReleaseNotes.Read(source,"",null)=="版本一：修复连接","default source CHANGELOG is read as UTF8");
        File.WriteAllText(log,"版本二");check(ReleaseNotes.Read(source,"",null)=="版本二","each upload reads current log contents");
        File.WriteAllText(Path.Combine(source,"custom.md"),"自定义日志");check(ReleaseNotes.Read(source,"custom.md",null)=="自定义日志","explicit relative log overrides default");
        check(ReleaseNotes.Read(source,log,null)=="版本二","explicit absolute log is supported");
        check(ReleaseNotes.Read(source,"missing.md","手动说明")=="手动说明","manual notes override file without reading it");
        bool Fails(string path){try{ReleaseNotes.Read(source,path,null);return false;}catch(IOException){return true;}}
        check(Fails("missing.md"),"configured missing log blocks upload instead of silently using other notes");
        File.WriteAllText(log,"  ");check(Fails(log),"empty file cannot silently overwrite release notes");
        File.WriteAllText(log,new string('x',100001));check(Fails(log),"oversized release log is rejected");
        var config=new ProjectAutomation("owner/app",source,root){ManualNotes="手动内容"};
        check(JsonSerializer.Deserialize<ProjectAutomation>(JsonSerializer.Serialize(config))?.ManualNotes=="手动内容","manual note selection persists for automatic publishing");
    }
}
