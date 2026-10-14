using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task ProjectStatusSmoke(string path)
    {
        var now=DateTimeOffset.UtcNow;var repo=myProjects.First().FullName;var source=Path.Combine(store.Root,"status-source");var release=Path.Combine(store.Root,"status-release");Directory.CreateDirectory(Path.Combine(source,".git"));Directory.CreateDirectory(release);File.WriteAllText(Path.Combine(source,"version.txt"),"1.0.0.0");
        File.Copy(Environment.ProcessPath!,Path.Combine(release,"Gitstar.exe"));File.Copy(Environment.ProcessPath!,Path.Combine(release,"Gitstar-setup.exe"));File.WriteAllText(Path.Combine(release,"使用说明.md"),"status preview");
        var hashes=new List<string>();foreach(var file in Directory.GetFiles(release)){using var stream=File.OpenRead(file);hashes.Add(Convert.ToHexString(await SHA256.HashDataAsync(stream))+"  "+Path.GetFileName(file));}File.WriteAllText(Path.Combine(release,"SHA256SUMS.txt"),string.Join("\n",hashes)+"\n");
        projectAutomation[repo]=new(repo,source,release,"version.txt","main");projectWorkspaces=[new(repo,source,"main")];
        var project=myProjects.First();var commit=new string('a',40);var state=new GitProjectState(source,"main","https://github.com/"+repo+".git",commit,[],""){CheckedAt=now,CommittedAt=now.AddMinutes(-20),RemoteCheckedAt=now,RemoteCommit=commit,CommitCount=12,CommittedFileCount=100,LastCommitFileCount=28,CommitMessage="完善项目状态、附件核对与排序"};projectStates[repo]=state;
        var staged=await ReleaseAssetFiles.Stage(AutomationRules.Plan(projectAutomation[repo]).Assets,Path.Combine(store.Root,"status-server-assets"),CancellationToken.None);
        var version=new ProjectVersion(7,"1.0.0.0","1.0.0.0","",false,false,staged.Count,now.AddDays(-2),""){Published=now.AddDays(-30),Created=now.AddDays(-31),CheckedAt=now,LastUploadedAt=now,LastUploadedCount=2,LastReusedCount=2,LastAction="强制覆盖",Assets=staged.Select((a,i)=>new Asset(i,a.Name,a.Size,"","sha256:"+a.Sha)).ToList()};
        RememberProjectVersion(repo,version);await RefreshReleaseDirectoryStatus(repo,CancellationToken.None);var matched=releaseDirectoryStates[repo].PendingCount==0&&releaseDirectoryStates[repo].UnverifiedCount==0&&releaseDirectoryStates[repo].LocalCount==4;
        var extra=Path.Combine(release,"extra.msi");File.WriteAllText(extra,"new package");await RefreshReleaseDirectoryStatus(repo,CancellationToken.None);var pending=releaseDirectoryStates[repo].PendingCount==1;File.Delete(extra);await RefreshReleaseDirectoryStatus(repo,CancellationToken.None);
        var serverVersion=version with{LastUploadedAt=null,LastUploadedCount=null,LastReusedCount=null,LastAction=""};ReplaceProjectVersions(repo,[serverVersion]);var preserved=projectVersions[repo].Single().LastUploadedAt==now;
        var second=myProjects[1];projectVersions[second.FullName]=[version with{Id=8,Published=now.AddDays(-1)}];api.Dispose();var transport=new SortingTransport();api=new(settings,transport){Token="fixture-token"};var unverified=await VerifySourceRemote(state,repo,CancellationToken.None);var cleared=!unverified.SourceSynced&&unverified.RemoteCheckedAt==null;
        ProjectSort.SelectedIndex=1;RenderProjects();var before=transport.Requests;var selected=selectedProject;var updatedOrder=model.Projects.Select(p=>p.FullName).ToArray();ProjectSort.SelectedIndex=0;var publicationOrder=model.Projects.Select(p=>p.FullName).ToArray();var sortCorrect=updatedOrder.First()==repo&&publicationOrder.First()==second.FullName&&selectedProject==selected&&transport.Requests==before;
        SaveProjects();using var saved=new LocalStore(store.Root);var stored=saved.Get(ProjectsKey+"/versions",new Dictionary<string,List<ProjectVersion>>())[repo].Single();var storedStatus=saved.Get(ProjectsKey+"/release-status",new Dictionary<string,ReleaseDirectoryStatus>())[repo];var persisted=stored.LastUploadedAt==now&&stored.LastUploadedCount==2&&storedStatus.LocalCount==4&&saved.Get("settings",new Settings()).ProjectsSort==UpdateSort.Published;
        if(!matched||!pending||!preserved||!cleared||!sortCorrect||!persisted)throw new IOException("Project status regression");
        File.WriteAllText(Path.ChangeExtension(path,".project-status.json"),JsonSerializer.Serialize(new{SourceCountsAndTimesVisible=true,ConfirmedPackagesMatched=matched,MissingPackageCounted=pending,ForcedUpdateTimePreserved=preserved,FailedVerificationClearsOldSync=cleared,ProjectSortCorrect=sortCorrect,SortPreservesSelection=selectedProject==selected,SortNetworkRequests=transport.Requests-before,CacheAndPreferencePersisted=persisted}));
        RenderProjects();UpdateLayout();var element=(FrameworkElement)ProjectDetails.Children[Environment.GetCommandLineArgs().Contains("--status-release")?3:2];((ScrollViewer)ProjectDetails.Parent).ScrollToVerticalOffset(element.TranslatePoint(new Point(),ProjectDetails).Y);
    }
}
