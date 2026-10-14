using System.IO;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

static class ReleaseFileSelectionChecks
{
    public static async Task Run(Action<bool,string> check,string parent)
    {
        var root=Path.Combine(parent,"file-selection");var source=Path.Combine(root,"source");var folder=Path.Combine(root,"release");Directory.CreateDirectory(source);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(source,"version.txt"),"1.0.0.0");
        foreach(var name in new[]{"app.zip","README.md","symbols.pdb"})File.WriteAllText(Path.Combine(folder,name),"fixture "+name);
        var config=new ProjectAutomation("owner/repo",source,folder,"version.txt");
        check(AutomationRules.Plan(config).Assets.Length==3,"default automatic publication selects all files in the release directory");
        var chosen=config with{SelectedAssets=["app.zip"]};var saved=JsonSerializer.Deserialize<ProjectAutomation>(JsonSerializer.Serialize(chosen))!;
        check(AutomationRules.Plan(saved).Assets.Select(Path.GetFileName).SequenceEqual(new[]{"app.zip"}),"saved manual selection overrides automatic patterns after serialization");
        File.WriteAllText(Path.Combine(folder,"new.zip"),"not chosen");check(AutomationRules.Plan(saved).Assets.Length==1,"new release files are not silently added to a remembered manual selection");
        check(AutomationRules.Plan(config with{AssetPatterns="*.zip"}).Assets.Length==2,"legacy explicit attachment rules remain effective until manual selection is saved");
        foreach(var names in new List<string>[] {[],["absent.exe"],["app.zip","absent.exe"],["../source/version.txt"]}){var blocked=false;try{AutomationRules.Plan(config with{SelectedAssets=names});}catch(Exception ex)when(ex is IOException or ArgumentException){blocked=true;}check(blocked,"empty missing or escaping selection blocks automatic upload without falling back to all files");}
        File.WriteAllText(Path.Combine(folder,"app-v0.9.0.zip"),"old");var stale=false;try{AutomationRules.Plan(config with{SelectedAssets=["app-v0.9.0.zip"]});}catch(IOException){stale=true;}check(stale,"remembered older-version filename cannot silently publish under the latest version");
        var plan=AutomationRules.Plan(saved);var stage=Path.Combine(root,"staged");var prepared=await ReleaseAssetFiles.Stage(plan.Assets,stage,CancellationToken.None);
        check(prepared.Count==1&&prepared.Single().OriginalName=="app.zip"&&Directory.GetFiles(stage).Length==1,"publication staging contains only checked files and excludes unchecked documentation and symbols");
        var docs=AutomationRules.Plan(config with{SelectedAssets=["README.md"]});check(docs.Assets.Length==1,"explicit documentation-only update keeps the selected file instead of requiring another installer");
        var manifest=Path.Combine(folder,"SHA256SUMS.txt");File.WriteAllText(manifest,new string('a',64)+"  app.zip\n");var manifestPlan=AutomationRules.Plan(config with{SelectedAssets=["SHA256SUMS.txt"]});var manifestOnly=await ReleaseAssetFiles.Stage(manifestPlan.Assets,Path.Combine(root,"manifest-only"),CancellationToken.None);check(manifestOnly.Count==1&&manifestOnly[0].Name=="SHA256SUMS.txt","explicit checksum-only update does not require uploading unchecked packages");
    }
}
