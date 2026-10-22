using Gitstar.Core;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class RootPortableReplacementChecks
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern uint QueryDosDevice(string name,StringBuilder target,int length);
    private static void Subst(params string[] args)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"subst.exe")){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;process.WaitForExit();
        if(process.ExitCode!=0)throw new IOException("Unable to configure isolated root fixture");
    }
    public static void Run(Action<bool,string> check,string root)
    {
        var fixture=Path.GetFullPath(Path.Combine(root,"root-portable"));Directory.CreateDirectory(fixture);
        var drive=Enumerable.Range('D',23).Select(value=>((char)value)+":").Reverse().First(name=>{var buffer=new StringBuilder(32768);return QueryDosDevice(name,buffer,buffer.Capacity)==0;});
        Subst(drive,fixture);
        try{
            var volume=drive+"\\";var program=Path.Combine(volume,"Editor.exe");var downloads=Path.Combine(volume,"downloads");Directory.CreateDirectory(downloads);
            var source=Path.Combine(downloads,"NewEditor.exe");var original=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Gitstar.Checks.exe"));var updated=original.Concat(Encoding.UTF8.GetBytes("root-update")).ToArray();
            File.WriteAllBytes(program,original);File.WriteAllBytes(source,updated);var neighbor=Path.Combine(volume,"unrelated.txt");File.WriteAllText(neighbor,"keep-root-data");
            var plan=PortableReplacement.Prepare(program,source);var result=PortableReplacement.Apply(plan);
            check(result.Replaced==1&&File.ReadAllBytes(program).SequenceEqual(updated),"root EXE updates from a download subdirectory and preserves original filename");
            check(result.Backup==null&&!Directory.Exists(Path.Combine(volume,".gitstar-backups"))&&!Directory.GetDirectories(volume,".gitstar-update-*").Any(),"root EXE default update cleans only its staging and retains no backup");
            File.WriteAllBytes(program,original);result=PortableReplacement.Apply(PortableReplacement.Prepare(program,source),keepBackup:true);
            check(result.Backup!=null&&File.ReadAllBytes(Path.Combine(result.Backup,"Editor.exe")).SequenceEqual(original),"root EXE optional backup preserves original bytes");
            File.WriteAllBytes(program,original);plan=PortableReplacement.Prepare(program,source);var rejected=false;
            try{PortableReplacement.Apply(plan,progress:(_,_)=>throw new IOException("injected failure"));}catch(IOException){rejected=true;}
            check(rejected&&File.ReadAllBytes(program).SequenceEqual(original),"root update failure restores original EXE");
            using(var cancel=new CancellationTokenSource()){rejected=false;try{PortableReplacement.Apply(plan,ct:cancel.Token,progress:(_,_)=>cancel.Cancel());}catch(OperationCanceledException){rejected=true;}check(rejected&&File.ReadAllBytes(program).SequenceEqual(original),"root update cancellation restores original EXE");}
            var payload=Path.Combine(downloads,"extracted");Directory.CreateDirectory(payload);File.WriteAllBytes(Path.Combine(payload,"Editor.exe"),updated);
            var archive=Path.Combine(downloads,"Editor.zip");File.WriteAllText(archive,"already extracted fixture");
            PortableReplacement.Apply(PortableReplacement.Prepare(program,archive,payload));
            check(File.ReadAllBytes(program).SequenceEqual(updated),"single EXE ZIP payload supports root target on the same volume");
            File.WriteAllText(Path.Combine(payload,"library.dll"),"extra");rejected=false;try{PortableReplacement.Prepare(program,archive,payload);}catch(IOException){rejected=true;}
            check(rejected&&!File.Exists(Path.Combine(volume,"library.dll")),"multi-file ZIP cannot scatter payload into a volume root");
            plan=PortableReplacement.Prepare(program,source);plan=plan with{Files=[..plan.Files,new(source,neighbor,true,"")]};rejected=false;try{PortableReplacement.Apply(plan);}catch(IOException){rejected=true;}
            check(rejected&&File.ReadAllText(neighbor)=="keep-root-data"&&File.ReadAllBytes(source).SequenceEqual(updated),"forged root update plan cannot change neighboring files or downloads");
            check(!Directory.GetDirectories(volume,".gitstar-update-*").Any(),"root rollback leaves no staging folders");
        }finally{
            var actual=new StringBuilder(32768);
            if(QueryDosDevice(drive,actual,actual.Capacity)!=0&&actual.ToString().Equals("\\??\\"+fixture,StringComparison.OrdinalIgnoreCase))Subst(drive,"/D");
            else throw new IOException("Isolated drive mapping changed; leaving mapping untouched");
        }
    }
}
