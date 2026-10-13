using System.IO;
using Gitstar.Core;
using Gitstar.Desktop;

static class WorkspaceChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var git=new GitWorkspace("");var repository=Path.Combine(root,"repair-origin");Directory.CreateDirectory(repository);await git.Initialize(repository,"upstream/original","main",CancellationToken.None);
        var source=Path.Combine(repository,"nested-source");Directory.CreateDirectory(source);await File.WriteAllTextAsync(Path.Combine(source,"中文源码.cs"),"// source kept intact");Directory.CreateDirectory(Path.Combine(source,"bin"));await File.WriteAllTextAsync(Path.Combine(source,"bin","generated.dll"),"fixture");
        var nested=await git.Diagnose(source,CancellationToken.None);var own=await git.Diagnose(repository,CancellationToken.None);
        check(nested.HasRepository&&!nested.IsRoot&&nested.Root.Equals(repository,StringComparison.OrdinalIgnoreCase)&&nested.Remote=="https://github.com/upstream/original.git","directory diagnosis identifies parent repository root and origin");
        check(own.IsRoot,"existing repository root is recognized without reinitializing history");
        var plain=Path.Combine(root,"plain-source");Directory.CreateDirectory(plain);check(!(await git.Diagnose(plain,CancellationToken.None)).HasRepository,"ordinary source directory can be initialized independently");
        await git.Run(repository,CancellationToken.None,"remote","add","upstream","https://github.com/someone/other.git");await git.Run(repository,CancellationToken.None,"remote","set-url","--add","--push","origin","https://github.com/upstream/original.git");
        var backup=await git.RepairOrigin(repository,"owner/fork",own.Remote,true,CancellationToken.None);
        check(backup=="upstream-2"&&await git.Run(repository,CancellationToken.None,"remote","get-url",backup)==own.Remote,"origin repair preserves original address without overwriting existing upstream");
        check(await git.Run(repository,CancellationToken.None,"remote","get-url","origin")=="https://github.com/owner/fork.git"&&await git.Run(repository,CancellationToken.None,"remote","get-url","--push","origin")=="https://github.com/owner/fork.git","origin repair directs both fetch and push to selected project");
        var rejected=false;try{await git.RepairOrigin(repository,"owner/another",own.Remote,false,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"stale remote diagnosis cannot overwrite a newer origin setting");
        var destination=Path.Combine(root,"independent-source");var copied=await SourceRootCopy.CopyAndInitialize(source,destination,"owner/fork","main",true,null,CancellationToken.None);
        check(copied.Folder.Equals(destination,StringComparison.OrdinalIgnoreCase)&&copied.Remote=="https://github.com/owner/fork.git"&&File.ReadAllText(Path.Combine(destination,"中文源码.cs"))=="// source kept intact","nested source can be copied into an independent new Git root");
        check(!Directory.Exists(Path.Combine(destination,"bin"))&&File.Exists(Path.Combine(source,"bin","generated.dll"))&&File.Exists(Path.Combine(repository,".git","config")),"generated files excluded while original source and Git metadata remain intact");
        var occupied=Path.Combine(root,"occupied-root");Directory.CreateDirectory(occupied);File.WriteAllText(Path.Combine(occupied,"keep.txt"),"keep");rejected=false;try{await SourceRootCopy.CopyAndInitialize(source,occupied,"owner/fork","main",true,null,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&File.ReadAllText(Path.Combine(occupied,"keep.txt"))=="keep","copy refuses to overwrite an occupied destination");
        rejected=false;try{await SourceRootCopy.CopyAndInitialize(source,Path.Combine(repository,"another-root"),"owner/fork","main",true,null,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected&&!Directory.Exists(Path.Combine(repository,"another-root")),"new root inside another Git repository is rejected before copying");
        rejected=false;try{await SourceRootCopy.CopyAndInitialize(source,Path.Combine(source,"child"),"owner/fork","main",true,null,CancellationToken.None);}catch(IOException){rejected=true;}check(rejected,"source and destination cannot overlap");
        var cancelled=Path.Combine(root,"cancelled-copy");using(var cancel=new CancellationTokenSource()){rejected=false;try{await SourceRootCopy.CopyAndInitialize(source,cancelled,"owner/fork","main",true,_=>cancel.Cancel(),cancel.Token);}catch(OperationCanceledException){rejected=true;}}
        check(rejected&&!Directory.Exists(cancelled)&&!Directory.GetDirectories(root,".gitstar-copy-*").Any(),"cancelled copy removes owned staging directory without replacing destination");
        var remote=Path.Combine(root,"local-remote-history");Directory.CreateDirectory(remote);await git.Initialize(remote,"owner/fork","main",CancellationToken.None);await git.Run(remote,CancellationToken.None,"config","user.name","Fixture");await git.Run(remote,CancellationToken.None,"config","user.email","fixture@example.invalid");File.WriteAllText(Path.Combine(remote,"中文源码.cs"),"old");File.WriteAllText(Path.Combine(remote,"keep-remote.txt"),"remote extra");await git.Run(remote,CancellationToken.None,"add",".");await git.Run(remote,CancellationToken.None,"commit","-m","remote baseline");var oldCommit=(await git.Inspect(remote,CancellationToken.None)).Commit;
        var preserved=await SourceRootCopy.CopyAndInitialize(source,Path.Combine(root,"preserved-history"),"owner/fork","main",true,null,CancellationToken.None,async(stage,ct)=>{await git.Run(root,ct,"clone","--",remote,stage);await git.Run(stage,ct,"remote","set-url","origin","https://github.com/owner/fork.git");});
        check(preserved.Commit==oldCommit&&File.ReadAllText(Path.Combine(preserved.Folder,"中文源码.cs"))=="// source kept intact"&&File.ReadAllText(Path.Combine(preserved.Folder,"keep-remote.txt"))=="remote extra","new root can preserve remote history while overlaying selected source files");
        var allFiles=await SourceRootCopy.CopyAndInitialize(source,Path.Combine(root,"all-source-files"),"owner/fork","main",false,null,CancellationToken.None);check(File.Exists(Path.Combine(allFiles.Folder,"bin","generated.dll")),"generated-folder exclusions can be disabled explicitly");
    }
}
