namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task RefreshProjectVersionStatus(ManagedProject project,CancellationToken ct)
    {
        var key=ProjectsKey;var client=api;
        async Task Versions(){var versions=await client.ProjectVersions(project.FullName,ct);ct.ThrowIfCancellationRequested();if(key!=ProjectsKey||client!=api||quitting)throw new OperationCanceledException(ct);ReplaceProjectVersions(project.FullName,versions);if(projectsPage&&selectedProject==project.FullName)RenderProjectDetails(project);}
        var versions=Versions();var local=RefreshReleaseDirectoryStatus(project.FullName,ct,versions);await Task.WhenAll(versions,local);
    }
    private async Task RefreshReleaseDirectoryStatus(string repo,CancellationToken ct,Task? versions=null)
    {
        if(!projectAutomation.TryGetValue(repo,out var config)){releaseDirectoryStates.Remove(repo);SaveProjects();return;}
        var key=ProjectsKey;
        try{
            var plan=await Task.Run(()=>AutomationRules.Plan(config),ct);var prepared=await ReleaseAssetFiles.Inspect(plan.Assets,ct);
            if(versions!=null)await versions;ct.ThrowIfCancellationRequested();if(key!=ProjectsKey||quitting)throw new OperationCanceledException(ct);
            var remote=projectVersions.GetValueOrDefault(repo)?.FirstOrDefault(v=>v.Prerelease==plan.Prerelease&&(v.Tag==plan.Tag||Rules.CompareVersions(v.Tag,plan.Tag)==0));
            int? pending=remote==null?prepared.Count:remote.Assets==null?null:0;var unverified=0;
            if(remote?.Assets!=null)foreach(var local in prepared){var asset=remote.Assets.FirstOrDefault(a=>a.Name.Equals(local.Name,StringComparison.Ordinal));if(asset==null||asset.Size!=local.Size){pending++;continue;}if(string.IsNullOrEmpty(asset.Digest)){unverified++;continue;}if(!asset.Digest.Equals("sha256:"+local.Sha,StringComparison.OrdinalIgnoreCase))pending++;}
            releaseDirectoryStates[repo]=new(plan.Tag,prepared.Count,pending,unverified,DateTimeOffset.UtcNow);
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){releaseDirectoryStates[repo]=new("",0,null,0,DateTimeOffset.UtcNow,ex.Message);}
        SaveProjects();
    }
}
