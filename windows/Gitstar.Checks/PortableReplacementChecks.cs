using Gitstar.Core;
using System.IO;
using System.Text;

internal static class PortableReplacementChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var work=Path.Combine(root,"portable-replacement");var target=Path.Combine(work,"installed");var downloads=Path.Combine(work,"downloads");Directory.CreateDirectory(target);Directory.CreateDirectory(downloads);
        var program=Path.Combine(target,"Editor.exe");var source=Path.Combine(downloads,"Editor-2.0.exe");var original=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Gitstar.Checks.exe"));var updated=original.Concat(Encoding.UTF8.GetBytes("new-version-fixture")).ToArray();
        File.WriteAllBytes(program,original);File.WriteAllBytes(source,updated);var config=Path.Combine(target,"settings.json");File.WriteAllText(config,"personal-settings");
        var plan=PortableReplacement.Prepare(program,source);var result=PortableReplacement.Apply(plan);
        check(File.ReadAllBytes(program).SequenceEqual(updated)&&result.Replaced==1,"direct portable EXE replacement keeps original executable filename");
        check(result.Backup==null&&!Directory.Exists(Path.Combine(target,".gitstar-backups"))&&!Directory.GetDirectories(target,".gitstar-update-*").Any(),"portable replacement defaults to no retained backup and cleans rollback staging after success");
        check(File.ReadAllText(config)=="personal-settings","standalone EXE replacement preserves neighboring personal configuration");
        File.WriteAllBytes(program,original);result=PortableReplacement.Apply(PortableReplacement.Prepare(program,source),keepBackup:true);
        check(result.Backup!=null&&File.ReadAllBytes(Path.Combine(result.Backup,"Editor.exe")).SequenceEqual(original),"optional portable backup retains the original program only when requested");
        var archive=Path.Combine(downloads,"Editor.zip");File.WriteAllText(archive,"archive already extracted fixture");var extracted=Path.Combine(downloads,"extracted");var payload=Path.Combine(extracted,"Editor-portable");Directory.CreateDirectory(payload);
        File.WriteAllBytes(Path.Combine(payload,"Editor.exe"),updated);File.WriteAllText(Path.Combine(payload,"library.dll"),"new-library");File.WriteAllText(Path.Combine(payload,"settings.json"),"factory-settings");File.WriteAllText(Path.Combine(payload,"Editor.deps.json"),"new-runtime-deps");Directory.CreateDirectory(Path.Combine(payload,"data"));File.WriteAllText(Path.Combine(payload,"data","state.db"),"factory-data");
        File.WriteAllBytes(program,original);File.WriteAllText(Path.Combine(target,"library.dll"),"old-library");File.WriteAllText(Path.Combine(target,"Editor.deps.json"),"old-runtime-deps");Directory.CreateDirectory(Path.Combine(target,"data"));File.WriteAllText(Path.Combine(target,"data","state.db"),"personal-data");
        plan=PortableReplacement.Prepare(program,archive,extracted);result=PortableReplacement.Apply(plan);
        check(File.ReadAllBytes(program).SequenceEqual(updated)&&File.ReadAllText(Path.Combine(target,"library.dll"))=="new-library"&&File.ReadAllText(Path.Combine(target,"Editor.deps.json"))=="new-runtime-deps","portable ZIP replaces executable libraries and runtime metadata from nested payload");
        check(File.ReadAllText(config)=="personal-settings"&&File.ReadAllText(Path.Combine(target,"data","state.db"))=="personal-data"&&plan.Preserved==1,"portable ZIP preserves existing configuration files and data directories");
        File.WriteAllBytes(program,original);File.WriteAllText(Path.Combine(target,"library.dll"),"old-library");File.Delete(Path.Combine(target,"Editor.deps.json"));plan=PortableReplacement.Prepare(program,archive,extracted);var rolledBack=false;
        try{PortableReplacement.Apply(plan,progress:(done,total)=>{if(done==1)throw new IOException("injected write failure");});}catch(IOException){rolledBack=true;}
        check(rolledBack&&File.ReadAllBytes(program).SequenceEqual(original)&&File.ReadAllText(Path.Combine(target,"library.dll"))=="old-library"&&!File.Exists(Path.Combine(target,"Editor.deps.json")),"failure after a committed file rolls back old program and leaves unapplied new files absent");
        check(!Directory.GetDirectories(target,".gitstar-update-*").Any(),"successful rollback removes temporary recovery copies");
        using(var cancel=new CancellationTokenSource()){var cancelled=false;try{PortableReplacement.Apply(plan,ct:cancel.Token,progress:(done,total)=>cancel.Cancel());}catch(OperationCanceledException){cancelled=true;}check(cancelled&&File.ReadAllBytes(program).SequenceEqual(original),"cancelling replacement rolls back committed files");}
        plan=PortableReplacement.Prepare(program,source);using(var locked=new FileStream(program,FileMode.Open,FileAccess.Read,FileShare.None)){var blocked=false;try{PortableReplacement.Apply(plan);}catch(IOException){blocked=true;}check(blocked,"locked running program blocks replacement before any file mutation");}
        check(File.ReadAllBytes(program).SequenceEqual(original),"locked program remains intact for later retry");
        plan=PortableReplacement.Prepare(program,source);File.WriteAllBytes(source,original);var sourceChanged=false;try{PortableReplacement.Apply(plan);}catch(IOException){sourceChanged=true;}check(sourceChanged&&File.ReadAllBytes(program).SequenceEqual(original),"changed downloaded payload is rejected before program replacement");File.WriteAllBytes(source,updated);
        plan=PortableReplacement.Prepare(program,source);File.WriteAllBytes(program,updated);var stale=false;try{PortableReplacement.Apply(plan);}catch(IOException){stale=true;}check(stale&&File.ReadAllBytes(program).SequenceEqual(updated),"stale replacement plan preserves a program changed after preparation");File.WriteAllBytes(program,original);
        var installer=Path.Combine(downloads,"Editor-setup.exe");File.WriteAllBytes(installer,updated);var rejected=false;try{PortableReplacement.Prepare(program,installer);}catch(IOException){rejected=true;}check(rejected,"setup EXE is refused for automatic portable replacement");
        rejected=false;try{PortableReplacement.Prepare(program,program);}catch(IOException){rejected=true;}check(rejected,"portable update cannot replace a program from itself");
        var invalid=Path.Combine(downloads,"Invalid.exe");File.WriteAllText(invalid,"not a Windows executable");rejected=false;try{PortableReplacement.Prepare(program,invalid);}catch(IOException){rejected=true;}check(rejected&&File.ReadAllBytes(program).SequenceEqual(original),"invalid downloaded EXE is rejected before overwriting an existing program");
        File.Move(Path.Combine(payload,"Editor.exe"),Path.Combine(payload,"Renamed.exe"));File.WriteAllBytes(Path.Combine(payload,"Other.exe"),updated);rejected=false;try{PortableReplacement.Prepare(program,archive,extracted);}catch(IOException){rejected=true;}check(rejected,"ambiguous multi-program ZIP requires explicit manual package selection");File.Delete(Path.Combine(payload,"Other.exe"));
        plan=PortableReplacement.Prepare(program,archive,extracted);PortableReplacement.Apply(plan);check(File.ReadAllBytes(program).SequenceEqual(updated)&&!File.Exists(Path.Combine(target,"Renamed.exe")),"single renamed main program in ZIP replaces original executable path");
        var outside=Path.Combine(work,"outside.exe");File.WriteAllBytes(outside,original);plan=plan with{Files=[new(source,outside,true,"")]};rejected=false;try{PortableReplacement.Apply(plan);}catch(IOException){rejected=true;}check(rejected&&File.ReadAllBytes(outside).SequenceEqual(original),"replacement rejects targets outside its software directory");
    }
}
