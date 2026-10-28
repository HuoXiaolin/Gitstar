using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("Gitstar.Checks")]
namespace Gitstar.Desktop;

public static partial class WindowsServices
{
    internal static (double Width,double Height) MainWindowSize(double width,double height,double availableWidth,double availableHeight)
    {
        var scale=Math.Min(1,Math.Min(availableWidth*0.9/width,availableHeight*0.86/height));
        return(width*scale,height*scale);
    }
    public static void FitWindow(System.Windows.Window window,bool mainWindow)
    {
        var source=System.Windows.PresentationSource.FromVisual(window);
        var transform=source?.CompositionTarget?.TransformFromDevice??System.Windows.Media.Matrix.Identity;
        var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var area=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var topLeft=transform.Transform(new System.Windows.Point(area.Left,area.Top));
        var bottomRight=transform.Transform(new System.Windows.Point(area.Right,area.Bottom));
        var availableWidth=bottomRight.X-topLeft.X;var availableHeight=bottomRight.Y-topLeft.Y;
        var size=mainWindow?MainWindowSize(window.Width,window.Height,availableWidth,availableHeight):(Width:Math.Min(window.Width,availableWidth*0.9),Height:Math.Min(window.Height,availableHeight*0.8));
        var height=size.Height;var width=size.Width;
        window.MinHeight=Math.Min(window.MinHeight,height);window.MinWidth=Math.Min(window.MinWidth,width);
        window.Height=height;window.Width=width;window.WindowState=System.Windows.WindowState.Normal;
        window.Left=topLeft.X+(availableWidth-width)/2;window.Top=topLeft.Y+(availableHeight-height)/2;
    }
    public static List<InstalledApp> Scan(IEnumerable<InstalledApp>? portable=null,IEnumerable<Binding>? bindings=null,IEnumerable<InstalledApp>? known=null,CancellationToken ct=default)=>ScanCurrentPrograms(bindings??[],portable??[],known??[],false,ct);
    public static void Startup(bool enabled)
    {
        SetStartupEntry(Registry.CurrentUser,StartupKey,enabled,Environment.ProcessPath??throw new IOException("无法确定当前程序位置。"));
    }
    public static void Open(string path)=>Process.Start(new ProcessStartInfo(path) {UseShellExecute=true});
    public static string ExtractPortable(string zip)
    {
        var destination=Path.Combine(Path.GetDirectoryName(zip)!,Rules.SafeSegment(Path.GetFileNameWithoutExtension(zip))+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        if(Directory.Exists(destination))throw new IOException("解压目录已存在，请选择其他目录。");
        using var archive=ZipFile.OpenRead(zip); long total=0;
        // Preflight every path before creating any file. No symlinks, device names or escaping paths.
        foreach(var e in archive.Entries) {
            total=checked(total+e.Length); if(total>20L*1024*1024*1024)throw new IOException("安装包解压体积超过 20GB。");
            var parts=e.FullName.Replace('\\','/').Split('/');
            if(parts.Any(p=>p==".."||p.Contains(':')||(p.Length>0&&Rules.SafeSegment(p)!=p)))throw new IOException("ZIP 中包含不安全的文件路径。");
            var path=Path.GetFullPath(Path.Combine(destination,e.FullName));
            if(!path.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("ZIP 路径越界。");
            if(((e.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("不解压符号链接。");
        }
        Directory.CreateDirectory(destination); archive.ExtractToDirectory(destination,false); return destination;
    }
    public static string DownloadPath(Release release,Asset asset,string root)=>Path.Combine(Path.GetFullPath(root),PackageRules.OriginalFileName(asset.Name));
    public static async Task<string> Download(GitHubClient api,Release release,Asset asset,string root,Action<long,long> progress,CancellationToken ct,Action? verifying=null,Func<CancellationToken,Task<bool>>? replaceManaged=null,bool forceDownload=false,string? targetPath=null)
    {
        var path=targetPath??DownloadPath(release,asset,root);var expectedRoot=Path.GetFullPath(root);if(!Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(expectedRoot.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new IOException("安装包目标必须直接位于下载目录。");Directory.CreateDirectory(expectedRoot);
        if(File.Exists(path)) { verifying?.Invoke();if(!forceDownload&&await Verify(path,asset,ct))return path;if(replaceManaged==null || !await replaceManaged(ct))throw new IOException("保存目录中已存在同名文件："+Path.GetFileName(path)+"。未覆盖，请选择其他目录或先移走原文件。"); }
        var partial=path+".partial";
        // Partial files are not installed; retry restarts the stream instead of trusting stale ranges.
        using(var response=await api.AssetResponse(release.Repository,asset,ct)) {
            await using var input=await response.Content.ReadAsStreamAsync(ct); await using var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.Read,65536,true);
            var buffer=new byte[65536]; long count=0; int n;
            while((n=await input.ReadAsync(buffer,ct))>0) { count+=n; if(count>asset.Size)throw new IOException("下载体积超过 GitHub 声明值。"); await output.WriteAsync(buffer.AsMemory(0,n),ct); progress(count,asset.Size); }
        }
        verifying?.Invoke();if(!await Verify(partial,asset,ct))throw new IOException("安装包大小或 SHA-256 校验失败，未启动安装器。");
        if(File.Exists(path)){if(replaceManaged==null || !await replaceManaged(ct))throw new IOException("旧安装包已变化，未替换；新文件保留在临时路径。");File.Move(partial,path,true);}else File.Move(partial,path,false);return path;
    }
    public static async Task<bool> Verify(string path,Asset asset,CancellationToken ct)
    {
        if(new FileInfo(path).Length!=asset.Size)return false;
        if(string.IsNullOrEmpty(asset.Digest))return path.EndsWith(".partial",StringComparison.Ordinal); // Existing files without a digest cannot safely be reused.
        if(!asset.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))throw new IOException("该安装包的摘要算法暂不支持。");
        await using var stream=File.OpenRead(path); var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)); return hash.Equals(asset.Digest[7..],StringComparison.OrdinalIgnoreCase);
    }
}
public sealed partial class GitWorkspace
{
    private readonly string token;
    public GitWorkspace(string token)=>this.token=token;
    internal ProcessStartInfo CreateStartInfo(string folder,params string[] args)
    {
        var psi=new ProcessStartInfo("git") {WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var option in new[]{"-c","core.quotepath=false","-c","i18n.logOutputEncoding=utf-8","-c","credential.helper=","-c","credential.interactive="+(token.Length>0?"true":"false"),"-c","core.askPass="})psi.ArgumentList.Add(option);
        foreach(var arg in args)psi.ArgumentList.Add(arg);
        psi.Environment["GIT_TERMINAL_PROMPT"]="0";
        psi.Environment["GCM_INTERACTIVE"]="0";
        foreach(var key in new[]{"GIT_ASKPASS","SSH_ASKPASS","GITSTAR_ASKPASS_MODE","GITSTAR_OAUTH_TOKEN"})psi.Environment.Remove(key);
        if(token.Length>0) {psi.Environment["GIT_ASKPASS"]=Environment.ProcessPath!;psi.Environment["GITSTAR_ASKPASS_MODE"]="1";psi.Environment["GITSTAR_OAUTH_TOKEN"]=token;}
        return psi;
    }
    public async Task<string> Run(string folder,CancellationToken ct,params string[] args)
    {
        var psi=CreateStartInfo(folder,args);
        using var process=Process.Start(psi)??throw new IOException("无法启动 Git，请先安装 Git for Windows。");
        var stdout=process.StandardOutput.ReadToEndAsync(ct); var stderr=process.StandardError.ReadToEndAsync(ct);
        try {await process.WaitForExitAsync(ct);} catch {try{process.Kill(true);}catch{}throw;}
        var output=await stdout; var error=await stderr;
        if(process.ExitCode!=0){var safe=error.Replace(token.Length>0?token:"\0","[已隐藏]");var auth=safe.Contains("unable to get password from user",StringComparison.OrdinalIgnoreCase)||safe.Contains("Authentication failed",StringComparison.OrdinalIgnoreCase)||safe.Contains("terminal prompts disabled",StringComparison.OrdinalIgnoreCase)||safe.Contains("could not read Username",StringComparison.OrdinalIgnoreCase);var sync=safe.Contains("fetch first",StringComparison.OrdinalIgnoreCase)||safe.Contains("non-fast-forward",StringComparison.OrdinalIgnoreCase)||safe.Contains("stale info",StringComparison.OrdinalIgnoreCase);throw new GitCommandException(process.ExitCode,(auth?"此操作需要有效的 GitHub 授权，请在 Gitstar 设置中重新登录或核对仓库权限。\n":sync?"推送已停止：远端提交与本地不一致，请点击源码工作区的“核对同步”。\n\nGit 原始信息：\n":"Git 操作失败：")+safe[..Math.Min(safe.Length,1200)]);}
        return output.TrimEnd();
    }
    public async Task<string> Review(string folder,CancellationToken ct)
    {
        if(!Directory.Exists(folder))throw new IOException("源码目录不存在。");
        return await Run(folder,ct,"status","--short")+"\n\n"+await Run(folder,ct,"diff","--stat")+"\n\n远程地址：\n"+await Run(folder,ct,"remote","-v");
    }
    public async Task Upload(string folder,string repo,string branch,string message,IEnumerable<string> files,CancellationToken ct)
    {
        GitHubClient.Validate(repo);
        if(string.IsNullOrWhiteSpace(message))throw new ArgumentException("请填写提交说明。");
        await Run(folder,ct,"check-ref-format","--branch",branch);
        var top=Path.GetFullPath(await Run(folder,ct,"rev-parse","--show-toplevel"));
        if(!top.TrimEnd('\\','/').Equals(Path.GetFullPath(folder).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new IOException("请选择仓库根目录。");
        if((await Run(folder,ct,"diff","--cached","--name-only")).Length>0)throw new IOException("仓库已有暂存内容，请先处理，避免上传未选文件。");
        var current=await Run(folder,ct,"branch","--show-current"); if(current!=branch)throw new IOException("目标分支与当前分支不一致，请先在 Git 中切换分支。");
        var remote=await Run(folder,ct,"remote","get-url","origin");
        if(remote!= $"https://github.com/{repo}.git" && remote!=$"https://github.com/{repo}" && remote!=$"git@github.com:{repo}.git")throw new IOException("origin 与目标仓库不同，请手动检查远程地址。");
        var selected=files.Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray(); if(selected.Length==0)throw new IOException("请逐行填写需要提交的相对路径。");
        foreach(var file in selected) {
            var full=Path.GetFullPath(Path.Combine(folder,file)); if(Path.IsPathRooted(file)||!full.StartsWith(top+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||file.Split('/','\\').Contains(".git"))throw new IOException("选中文件路径越界。");
        }
        await Run(folder,ct,["add","--",..selected]); await Run(folder,ct,"commit","-m",message);
        // Fixed HTTPS destination scopes the transient OAuth credential to GitHub, without changing origin.
        await Push(folder,repo,branch,ct);
    }
}

