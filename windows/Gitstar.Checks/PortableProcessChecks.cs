using Gitstar.Core;
using Gitstar.Desktop;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

internal static class PortableProcessChecks
{
    private static async Task Ready(string path){for(var i=0;i<100;i++){if(File.Exists(path))try{using var parsed=JsonDocument.Parse(await File.ReadAllTextAsync(path));return;}catch(JsonException){}await Task.Delay(50);}throw new IOException("Headless process fixture did not start");}
    public static async Task Run(Action<bool,string> check,string root)
    {
        var target=Path.Combine(root,"process-target");var other=Path.Combine(root,"process-other");Directory.CreateDirectory(target);Directory.CreateDirectory(other);
        foreach(var file in Directory.GetFiles(AppContext.BaseDirectory).Where(file=>new[]{".dll",".json"}.Contains(Path.GetExtension(file)))){File.Copy(file,Path.Combine(target,Path.GetFileName(file)));File.Copy(file,Path.Combine(other,Path.GetFileName(file)));}
        var original=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Gitstar.Checks.exe"));var program=Path.Combine(target,"Editor.exe");var otherProgram=Path.Combine(other,"Editor.exe");File.WriteAllBytes(program,original);File.WriteAllBytes(otherProgram,original);
        var ready=Path.Combine(target,"ready.json");var otherReady=Path.Combine(other,"ready.json");
        var firstId=PortableProcessControl.Start(program,["--portable-process-fixture",ready],true)!.Value;var otherId=PortableProcessControl.Start(otherProgram,["--portable-process-fixture",otherReady],true)!.Value;
        using var first=Process.GetProcessById(firstId);using var unrelated=Process.GetProcessById(otherId);Process? restarted=null;
        try{
            await Ready(ready);await Ready(otherReady);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase){program};var found=PortableProcessControl.Find(paths);
            try{check(found.Count==1&&found[0].Id==firstId,"portable close locates the exact executable path and excludes same-name other installations");}finally{foreach(var process in found)process.Dispose();}
            var rejected=false;try{await PortableProcessControl.Stop(unrelated,paths,true,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&!unrelated.HasExited,"portable close refuses a process outside the reviewed replacement targets");
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();rejected=false;try{await PortableProcessControl.Stop(first,paths,true,cancelled.Token);}catch(OperationCanceledException){rejected=true;}check(rejected&&!first.HasExited,"cancelling before portable auto-close leaves the target process running");}
            await PortableProcessControl.Stop(first,paths,true,CancellationToken.None);check(first.HasExited&&!unrelated.HasExited,"automatic portable close terminates only its own non-windowed target when graceful close is unavailable");
            var updated=original.Concat(new byte[]{42}).ToArray();var source=Path.Combine(root,"Editor-new.exe");File.WriteAllBytes(source,updated);File.WriteAllText(Path.Combine(target,"settings.json"),"personal-data");PortableReplacement.Apply(PortableReplacement.Prepare(program,source));
            var restartedReady=Path.Combine(target,"restarted.json");var restartedId=PortableProcessControl.Start(program,["--portable-process-fixture",restartedReady],true)!.Value;restarted=Process.GetProcessById(restartedId);await Ready(restartedReady);
            using var state=JsonDocument.Parse(await File.ReadAllTextAsync(restartedReady));check(File.ReadAllBytes(program).SequenceEqual(updated)&&!restarted.HasExited&&state.RootElement.GetProperty("WorkingDirectory").GetString()==target,"closed portable executable is replaced and restarts from its original path and working directory");
            check(File.ReadAllText(Path.Combine(target,"settings.json"))=="personal-data"&&!Directory.Exists(Path.Combine(target,".gitstar-backups")),"automatic portable replacement preserves settings and keeps backup off by default");
            using var self=Process.GetCurrentProcess();rejected=false;try{await PortableProcessControl.Stop(self,new HashSet<string>{Environment.ProcessPath!},true,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&!self.HasExited,"portable auto-close cannot terminate the updating Gitstar process itself");
        }finally{foreach(var process in new[]{first,unrelated,restarted})if(process!=null)try{if(!process.HasExited){process.Kill(entireProcessTree:false);await process.WaitForExitAsync();}}catch(InvalidOperationException){}restarted?.Dispose();}
    }
}
