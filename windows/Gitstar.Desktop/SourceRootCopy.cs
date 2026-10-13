namespace Gitstar.Desktop;

public static class SourceRootCopy
{
    private static readonly HashSet<string> Generated=new(StringComparer.OrdinalIgnoreCase){"bin","obj","node_modules",".vs","artifacts","outputs",".gradle",".gradle-local","dist"};
    public static async Task<GitProjectState> CopyAndInitialize(string source,string destination,string repo,string branch,bool omitGenerated,Action<string>? progress,CancellationToken ct,Func<string,CancellationToken,Task>? cloneHistory=null)
    {
        ct.ThrowIfCancellationRequested();GitHubClient.Validate(repo);
        var from=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);var target=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        if(Path.GetFullPath(source).Equals(Path.GetPathRoot(Path.GetFullPath(source)),StringComparison.OrdinalIgnoreCase))throw new IOException("请选择源码目录，不能复制整个磁盘。");
        if(!Directory.Exists(from))throw new IOException("源码目录不存在。");
        if(from.Equals(target,StringComparison.OrdinalIgnoreCase)||target.StartsWith(from+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||from.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("新根目录与源目录不能重叠。");
        void EmptyTarget(){if(File.Exists(target)||(Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any()))throw new IOException("新根目录必须不存在或为空，不会覆盖已有文件。");}
        EmptyTarget();var parent=Path.GetDirectoryName(target)??throw new IOException("不能使用磁盘根目录。");var existing=parent;
        while(!Directory.Exists(existing))existing=Path.GetDirectoryName(existing)??throw new IOException("目标上级目录无效。");
        var git=new GitWorkspace("");if((await git.Diagnose(existing,ct)).HasRepository)throw new IOException("新根目录仍处于其他 Git 仓库内，请选择仓库之外的目录。");
        Directory.CreateDirectory(parent);var stage=Path.Combine(parent,".gitstar-copy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{
            if(cloneHistory!=null){
                progress?.Invoke("准备远程提交历史…");await cloneHistory(stage,ct);var cloned=await git.Diagnose(stage,ct);
                if(!cloned.IsRoot || !ProjectRules.MatchesRemote(cloned.Remote,repo))throw new IOException("克隆后的仓库根目录或 origin 与目标不一致。");
            }
            async Task Copy(string directory,string output){
                ct.ThrowIfCancellationRequested();if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("源码包含目录链接，请先确认实际目录："+directory);
                if((File.GetAttributes(output)&FileAttributes.ReparsePoint)!=0)throw new IOException("目标包含目录链接，未写入："+output);
                foreach(var entry in Directory.EnumerateFileSystemEntries(directory)){
                    ct.ThrowIfCancellationRequested();var name=Path.GetFileName(entry);if(name.Equals(".git",StringComparison.OrdinalIgnoreCase))continue;
                    var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("源码包含链接，未复制："+entry);
                    var destinationPath=Path.Combine(output,name);
                    if(new FileInfo(destinationPath).LinkTarget!=null || ((File.Exists(destinationPath)||Directory.Exists(destinationPath))&&(File.GetAttributes(destinationPath)&FileAttributes.ReparsePoint)!=0))throw new IOException("目标包含链接，未覆盖："+destinationPath);
                    if((attributes&FileAttributes.Directory)!=0){if(omitGenerated&&Generated.Contains(name))continue;Directory.CreateDirectory(destinationPath);await Copy(entry,destinationPath);}
                    else{progress?.Invoke(Path.GetRelativePath(from,entry));await using var input=new FileStream(entry,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);await using var file=new FileStream(destinationPath,cloneHistory==null?FileMode.CreateNew:FileMode.Create,FileAccess.Write,FileShare.None,65536,true);await input.CopyToAsync(file,ct);}
                }
            }
            await Copy(from,stage);
            if(cloneHistory==null)await git.Initialize(stage,repo,branch,ct);
            else if((await git.Inspect(stage,ct)).Commit.Length==0){await git.Run(stage,ct,"check-ref-format","--branch",branch);await git.Run(stage,ct,"symbolic-ref","HEAD","refs/heads/"+branch);}
            ct.ThrowIfCancellationRequested();EmptyTarget();
            if((await git.Diagnose(parent,ct)).HasRepository)throw new IOException("目标目录的 Git 归属已改变，请重新选择。");
            if(Directory.Exists(target))Directory.Delete(target,false);Directory.Move(stage,target);
            return await git.Inspect(target,ct);
        }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
}
