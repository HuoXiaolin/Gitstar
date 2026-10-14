namespace Gitstar.Desktop;

public sealed partial class GitWorkspace
{
    public async Task EnsureIdentity(string folder,Func<CancellationToken,Task<(string Name,string Email)>> fallback,CancellationToken ct)
    {
        async Task<string> Read(string key){try{return await Run(folder,ct,"config","--get",key);}catch(IOException){return "";}}
        var name=await Read("user.name");var email=await Read("user.email");if(name.Length>0&&email.Length>0)return;
        var identity=await fallback(ct);if(name.Length==0)await Run(folder,ct,"config","--local","user.name",identity.Name);if(email.Length==0)await Run(folder,ct,"config","--local","user.email",identity.Email);
    }
    public async Task<WorkspaceDiagnosis> Diagnose(string folder,CancellationToken ct)
    {
        var path=Path.GetFullPath(folder);if(!Directory.Exists(path))throw new IOException("源码目录不存在，请选择新的目录。");
        var marker=false;
        for(var directory=new DirectoryInfo(path);directory!=null;directory=directory.Parent){
            var metadata=Path.Combine(directory.FullName,".git");if(File.Exists(metadata)||Directory.Exists(metadata)){marker=true;break;}
        }
        if(!marker){if(File.Exists(Path.Combine(path,"HEAD"))&&Directory.Exists(Path.Combine(path,"objects")))throw new IOException("此目录是裸仓库，请选择包含源码的工作目录。");return new(path,"","");}
        var root=Path.GetFullPath(await Run(path,ct,"rev-parse","--show-toplevel"));var remotes=await Run(root,ct,"remote");
        var remote=remotes.Split('\n').Select(s=>s.Trim()).Contains("origin")?await Run(root,ct,"remote","get-url","origin"):"";
        return new(path,root,remote);
    }
    public async Task<string> RepairOrigin(string folder,string repo,string expectedRemote,bool fork,CancellationToken ct)
    {
        GitHubClient.Validate(repo);var diagnosis=await Diagnose(folder,ct);
        if(!diagnosis.IsRoot || diagnosis.Remote!=expectedRemote)throw new IOException("目录或远程地址已改变，请重新检测。");
        var backup="";var gitMetadata=await Run(folder,ct,"rev-parse","--git-common-dir");var config=Path.GetFullPath(Path.Combine(folder,gitMetadata,"config"));var originalConfig=await File.ReadAllBytesAsync(config,ct);
        try{
            if(diagnosis.Remote.Length>0){
                var remotes=(await Run(folder,ct,"remote")).Split('\n').Select(s=>s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);var preferred=fork?"upstream":"gitstar-previous-origin";backup=preferred;
                for(var n=2;remotes.Contains(backup);n++){
                    if(await Run(folder,ct,"remote","get-url",backup)==diagnosis.Remote)break;
                    backup=preferred+"-"+n;
                }
                if(!remotes.Contains(backup))await Run(folder,ct,"remote","add",backup,diagnosis.Remote);
                await Run(folder,ct,"config","--local","--replace-all","remote.origin.url","https://github.com/"+repo+".git");
            }else await Run(folder,ct,"remote","add","origin","https://github.com/"+repo+".git");
            // Explicit pushurl entries would otherwise keep targeting the old repository.
            var configText=await Run(folder,ct,"config","--local","--list");if(configText.Split('\n').Any(l=>l.StartsWith("remote.origin.pushurl=",StringComparison.OrdinalIgnoreCase)))await Run(folder,ct,"config","--local","--unset-all","remote.origin.pushurl");
            return backup;
        }catch{await File.WriteAllBytesAsync(config,originalConfig,CancellationToken.None);throw;}
    }
    public async Task Push(string folder,string repo,string branch,CancellationToken ct)
    {
        GitHubClient.Validate(repo);var state=await Inspect(folder,ct);
        if(state.Branch!=branch || !ProjectRules.MatchesRemote(state.Remote,repo))throw new IOException("当前分支或 origin 与目标不一致。");
        if(state.Commit.Length==0)throw new IOException("本地还没有可推送的提交。");
        await Run(folder,ct,"check-ref-format","--branch",branch);
        await Run(folder,ct,"-c","credential.helper=","push","https://github.com/"+repo+".git","HEAD:refs/heads/"+branch);
    }
    public async Task<GitProjectState> Inspect(string folder,CancellationToken ct)
    {
        var path=Path.GetFullPath(folder);if(!Directory.Exists(path))throw new IOException("本地目录不存在。");
        var top=Path.GetFullPath(await Run(path,ct,"rev-parse","--show-toplevel"));
        if(!top.TrimEnd('\\','/').Equals(path.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new IOException("请选择 Git 仓库根目录。");
        string branch,commit="",remote="";
        try{branch=await Run(path,ct,"symbolic-ref","--short","HEAD");}catch(IOException){branch="分离 HEAD";}
        var remotes=await Run(path,ct,"remote");if(remotes.Split('\n').Select(s=>s.Trim()).Contains("origin"))remote=await Run(path,ct,"remote","get-url","origin");
        try{commit=await Run(path,ct,"rev-parse","--verify","HEAD");}catch(IOException){}
        var changes=ProjectRules.ParseStatus(await Run(path,ct,"status","--porcelain=v1","-z","--untracked-files=all"));
        var state=new GitProjectState(path,branch,remote,commit,changes,await Run(path,ct,"diff","--stat"));
        if(commit.Length>0){
            var metadata=(await Run(path,ct,"log","-1","--format=%cI%x00%s")).Split('\0',2);
            var total=await Run(path,ct,"rev-list","--count","HEAD");
            var files=await Run(path,ct,"ls-tree","-r","-z","--name-only","HEAD");
            var last=await Run(path,ct,"diff-tree","--root","--no-commit-id","--name-only","-r","-z","-m","--first-parent","HEAD");
            state=state with{CommittedAt=DateTimeOffset.TryParse(metadata[0],out var time)?time:null,CommitMessage=metadata.Length>1?metadata[1]:"",CommitCount=long.TryParse(total,out var count)?count:null,CommittedFileCount=files.Split('\0',StringSplitOptions.RemoveEmptyEntries).Length,LastCommitFileCount=last.Split('\0',StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Count()};
        }else state=state with{CommitCount=0,CommittedFileCount=0,LastCommitFileCount=0};
        return state with{CheckedAt=DateTimeOffset.UtcNow};
    }
    public async Task Initialize(string folder,string repo,string branch,CancellationToken ct)
    {
        GitHubClient.Validate(repo);var path=Path.GetFullPath(folder);if(!Directory.Exists(path))throw new IOException("请选择已存在的源码目录。");
        var parentTop="";try{parentTop=await Run(path,ct,"rev-parse","--show-toplevel");}catch(IOException){}
        if(parentTop.Length>0 || File.Exists(Path.Combine(path,".git")) || Directory.Exists(Path.Combine(path,".git")))throw new IOException("此目录已属于 Git 仓库，请关联现有仓库根目录。");
        await Run(path,ct,"check-ref-format","--branch",branch);await Run(path,ct,"init","-b",branch);await Run(path,ct,"remote","add","origin","https://github.com/"+repo+".git");
    }
    public async Task Clone(string repo,string destination,CancellationToken ct)
    {
        GitHubClient.Validate(repo);var path=Path.GetFullPath(destination);var parent=Path.GetDirectoryName(path)!;
        if(!Directory.Exists(parent))throw new IOException("目标目录的上级文件夹不存在。");
        if(File.Exists(path) || (Directory.Exists(path)&&Directory.EnumerateFileSystemEntries(path).Any()))throw new IOException("请选择不存在或空的目标目录。");
        await Run(parent,ct,"-c","credential.helper=","clone","--","https://github.com/"+repo+".git",path);
    }
}
