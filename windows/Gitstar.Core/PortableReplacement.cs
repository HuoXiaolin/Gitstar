using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace Gitstar.Core;

public record PortableReplacementFile(string Source,string Target,bool Exists,string OriginalHash,string SourceHash="");
public record PortableReplacementPlan(string Program,string Root,IReadOnlyList<PortableReplacementFile> Files,int Preserved);
public record PortableReplacementResult(int Replaced,string? Backup);

public static class PortableReplacement
{
    private static readonly HashSet<string> ProgramTypes=new(StringComparer.OrdinalIgnoreCase){".exe",".dll",".pdb",".mui",".pri",".pak",".asar",".bin",".js",".css",".html",".htm",".wasm",".node",".svg",".png",".jpg",".jpeg",".gif",".ico",".ttf",".woff",".woff2",".otf",".qm",".rcc",".mo",".manifest"};
    private static bool Inside(string path,string root)=>path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static bool VolumeRoot(string path)=>Path.GetFullPath(path).TrimEnd('\\','/').Equals(Path.GetPathRoot(Path.GetFullPath(path))?.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);
    private static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input));}
    private static void NoLinks(string path)
    {
        for(var cursor=Path.GetFullPath(path);cursor!=null;cursor=Path.GetDirectoryName(cursor))
            if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new IOException("软件更新路径包含链接，请选择实际文件夹："+cursor);
    }
    private static IEnumerable<string> Files(string root)
    {
        var pending=new Stack<string>();pending.Push(root);var count=0;
        while(pending.Count>0)foreach(var item in Directory.EnumerateFileSystemEntries(pending.Pop())){
            NoLinks(item);if(Directory.Exists(item)){if(!ProtectedFolder(Path.GetFileName(item)))pending.Push(item);}
            else {if(++count>20000)throw new IOException("便携包文件过多，请确认选择了正确的软件包。");yield return item;}
        }
    }
    private static bool ProtectedFolder(string name)=>new[]{"data","userdata","user-data","config","configs","settings","logs","cache",".git",".gitstar-backups"}.Contains(name,StringComparer.OrdinalIgnoreCase)||name.StartsWith(".gitstar-update-",StringComparison.OrdinalIgnoreCase);
    private static bool ProgramFile(string path)=>ProgramTypes.Contains(Path.GetExtension(path))||path.EndsWith(".deps.json",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".runtimeconfig.json",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).Equals("icudtl.dat",StringComparison.OrdinalIgnoreCase);
    private static bool Installer(string path){var info=FileVersionInfo.GetVersionInfo(path);return Regex.IsMatch(Path.GetFileName(path)+" "+info.FileDescription+" "+info.OriginalFilename+" "+info.InternalName,@"setup|installer|^(unins|uninstall|update|crash|helper)",RegexOptions.IgnoreCase);}
    public static PortableReplacementPlan Prepare(string program,string package,string? extracted=null)
    {
        program=Path.GetFullPath(program);package=Path.GetFullPath(package);NoLinks(program);NoLinks(package);
        if(!File.Exists(program)||!program.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("请先在软件管理中关联原主程序 EXE，才能直接替换。");
        var root=Path.GetDirectoryName(program)!;var volumeRoot=VolumeRoot(root);
        var files=new List<PortableReplacementFile>();var preserved=0;
        void Add(string source,string target){NoLinks(source);NoLinks(target);if(!Inside(target,root)||source.Equals(target,StringComparison.OrdinalIgnoreCase))throw new IOException("新版与原程序的位置冲突，请将新版下载到其他目录。");if(Directory.Exists(target))throw new IOException("目标文件位置被文件夹占用："+target);if(new FileInfo(source).Length>4L*1024*1024*1024)throw new IOException("新版文件过大，请核对更新文件。");var exists=File.Exists(target);files.Add(new(source,target,exists,exists?Hash(target):"",Hash(source)));}
        if(extracted==null){
            if(Path.GetExtension(package).ToLowerInvariant()!=".exe"||Regex.IsMatch(Path.GetFileName(package),@"setup|installer|^(unins|uninstall|update|crash|helper)",RegexOptions.IgnoreCase))throw new IOException("请选择便携 ZIP 或独立主程序 EXE，安装器不能直接替换。");
            Add(package,program);
        }else{
            extracted=Path.GetFullPath(extracted);NoLinks(extracted);if(!Directory.Exists(extracted)||VolumeRoot(extracted)||!volumeRoot&&Inside(extracted,root)||Inside(root,extracted)||extracted.Equals(root,StringComparison.OrdinalIgnoreCase))throw new IOException("请选择独立的新版解压文件夹，不要选择整个磁盘或与原软件目录重叠的位置。");
            var candidates=Files(extracted).Where(file=>file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&!Regex.IsMatch(Path.GetFileName(file),@"^(unins|uninstall|setup|update|crash|helper)",RegexOptions.IgnoreCase)).ToArray();
            var matching=candidates.Where(file=>Path.GetFileName(file).Equals(Path.GetFileName(program),StringComparison.OrdinalIgnoreCase)).ToArray();
            var primary=matching.Length==1?matching[0]:matching.Length==0&&candidates.Length==1?candidates[0]:throw new IOException("无法唯一识别压缩包中的主程序，请选择对应便携包，或解压后手动替换。");
            var payload=Path.GetDirectoryName(primary)!;
            var payloadFiles=Files(payload).ToArray();
            if(volumeRoot&&(payloadFiles.Length!=1||!payloadFiles[0].Equals(primary,StringComparison.OrdinalIgnoreCase)))throw new IOException("磁盘根目录支持替换独立主程序；此便携包含多个文件，请选择独立软件目录。");
            foreach(var source in payloadFiles){
                var target=source.Equals(primary,StringComparison.OrdinalIgnoreCase)?program:Path.GetFullPath(Path.Combine(root,Path.GetRelativePath(payload,source)));
                if(File.Exists(target)&&!source.Equals(primary,StringComparison.OrdinalIgnoreCase)&&!ProgramFile(source)){preserved++;continue;}Add(source,target);
            }
        }
        if(files.Count==0||files.Sum(file=>new FileInfo(file.Source).Length)>4L*1024*1024*1024)throw new IOException("便携包为空或过大，请核对更新文件。");
        var main=files.Single(file=>file.Target.Equals(program,StringComparison.OrdinalIgnoreCase));
        if(Installer(main.Source))throw new IOException("新版文件看起来是安装器，请选择便携包或主程序 EXE。");
        if(!InstallerCompatibility.InspectExe(main.Source,Environment.Is64BitOperatingSystem).Compatible)throw new IOException("便携包主程序架构与当前 Windows 不兼容。");
        return new(program,root,files,preserved);
    }
    public static PortableReplacementResult Apply(PortableReplacementPlan plan,bool keepBackup=false,CancellationToken ct=default,Action<int,int>? progress=null)
    {
        NoLinks(plan.Root);if(!Inside(plan.Program,plan.Root)||plan.Files.Count==0)throw new IOException("更新计划无效，请重新打开更新窗口。");
        if(VolumeRoot(plan.Root)&&(plan.Files.Count!=1||!plan.Files[0].Target.Equals(plan.Program,StringComparison.OrdinalIgnoreCase)||!VolumeRoot(Path.GetDirectoryName(plan.Program)!)))throw new IOException("根目录更新仅能替换指定主程序，不能写入其他文件。");
        foreach(var file in plan.Files){
            NoLinks(file.Target);NoLinks(file.Source);if(!Inside(file.Target,plan.Root)||file.Source.Equals(file.Target,StringComparison.OrdinalIgnoreCase))throw new IOException("更新文件越过软件目录。");
            if(file.SourceHash.Length==0||Hash(file.Source)!=file.SourceHash)throw new IOException("新版文件已变化，请重新下载并核对更新文件。");
            if(File.Exists(file.Target)!=file.Exists||file.Exists&&Hash(file.Target)!=file.OriginalHash)throw new IOException("原程序文件已变化，请重新读取更新计划。");
            if(file.Exists){using var access=new FileStream(file.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}
        }
        var staging=Path.Combine(plan.Root,".gitstar-update-"+Guid.NewGuid().ToString("N"));var fresh=Path.Combine(staging,"new");var rollback=Path.Combine(staging,"old");var committed=new List<PortableReplacementFile>();var created=new List<string>();var keepStaging=false;
        try{
            Directory.CreateDirectory(fresh);Directory.CreateDirectory(rollback);
            foreach(var file in plan.Files){
                ct.ThrowIfCancellationRequested();var relative=Path.GetRelativePath(plan.Root,file.Target);var staged=Path.Combine(fresh,relative);Directory.CreateDirectory(Path.GetDirectoryName(staged)!);File.Copy(file.Source,staged);
                if(file.SourceHash!=Hash(staged))throw new IOException("新版文件复制校验失败："+relative);
                if(file.Exists){var old=Path.Combine(rollback,relative);Directory.CreateDirectory(Path.GetDirectoryName(old)!);File.Copy(file.Target,old);if(Hash(old)!=file.OriginalHash)throw new IOException("原文件已变化："+relative);}
            }
            foreach(var file in plan.Files){
                ct.ThrowIfCancellationRequested();NoLinks(file.Target);if(File.Exists(file.Target)!=file.Exists||file.Exists&&Hash(file.Target)!=file.OriginalHash)throw new IOException("目标文件更新前发生变化："+file.Target);
                var parent=Path.GetDirectoryName(file.Target)!;var missing=new Stack<string>();for(var cursor=parent;!Directory.Exists(cursor);cursor=Path.GetDirectoryName(cursor)!)missing.Push(cursor);while(missing.Count>0){var directory=missing.Pop();Directory.CreateDirectory(directory);created.Add(directory);}
                File.Move(Path.Combine(fresh,Path.GetRelativePath(plan.Root,file.Target)),file.Target,true);committed.Add(file);progress?.Invoke(committed.Count,plan.Files.Count);
            }
            ct.ThrowIfCancellationRequested();string? backup=null;
            if(keepBackup){var backupRoot=Path.Combine(plan.Root,".gitstar-backups");NoLinks(backupRoot);Directory.CreateDirectory(backupRoot);backup=Path.Combine(backupRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);Directory.Move(rollback,backup);}
            return new(committed.Count,backup);
        }catch(Exception failure){
            var errors=new List<Exception>();foreach(var file in committed.AsEnumerable().Reverse())try{NoLinks(file.Target);if(file.Exists)File.Copy(Path.Combine(rollback,Path.GetRelativePath(plan.Root,file.Target)),file.Target,true);else File.Delete(file.Target);}catch(Exception error){errors.Add(error);}
            foreach(var directory in created.AsEnumerable().Reverse())try{if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);}catch(IOException){}
            if(errors.Count>0){keepStaging=true;throw new IOException("更新未完成，部分文件无法恢复；原文件副本保留在："+rollback,new AggregateException(new[]{failure}.Concat(errors)));}throw;
        }finally{if(!keepStaging)try{Directory.Delete(staging,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
