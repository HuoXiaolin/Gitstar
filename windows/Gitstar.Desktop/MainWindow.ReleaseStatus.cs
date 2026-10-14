namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task RefreshReleaseDirectoryStatus(string repo,CancellationToken ct)
    {
        if(!projectAutomation.TryGetValue(repo,out var config)){releaseDirectoryStates.Remove(repo);SaveProjects();return;}
        var stage=Path.Combine(store.Root,"release-inspection",Guid.NewGuid().ToString("N"));
        try{
            var plan=await Task.Run(()=>AutomationRules.Plan(config),ct);var prepared=await ReleaseAssetFiles.Stage(plan.Assets,stage,ct);
            var remote=projectVersions.GetValueOrDefault(repo)?.FirstOrDefault(v=>v.Prerelease==plan.Prerelease&&(v.Tag==plan.Tag||Rules.CompareVersions(v.Tag,plan.Tag)==0));
            int? pending=remote==null?prepared.Count:remote.Assets==null?null:0;var unverified=0;
            if(remote?.Assets!=null)foreach(var local in prepared){var asset=remote.Assets.FirstOrDefault(a=>a.Name.Equals(local.Name,StringComparison.Ordinal));if(asset==null||asset.Size!=local.Size){pending++;continue;}if(string.IsNullOrEmpty(asset.Digest)){unverified++;continue;}if(!asset.Digest.Equals("sha256:"+local.Sha,StringComparison.OrdinalIgnoreCase))pending++;}
            releaseDirectoryStates[repo]=new(plan.Tag,prepared.Count,pending,unverified,DateTimeOffset.UtcNow);
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){releaseDirectoryStates[repo]=new("",0,null,0,DateTimeOffset.UtcNow,ex.Message);}
        finally{if(Directory.Exists(stage))try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        SaveProjects();
    }
}
