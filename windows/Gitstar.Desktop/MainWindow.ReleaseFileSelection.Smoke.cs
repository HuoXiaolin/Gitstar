namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void PrepareReleaseFileSelectionSmoke(ManagedProject project)
    {
        var source=Path.Combine(store.Root,"selection-source");var folder=Path.Combine(store.Root,"selection-release");Directory.CreateDirectory(source);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(source,"version.txt"),"1.0.0.0");
        File.Copy(Environment.ProcessPath!,Path.Combine(folder,project.Name+".exe"));File.Copy(Environment.ProcessPath!,Path.Combine(folder,project.Name+"-setup.exe"));File.WriteAllText(Path.Combine(folder,"USAGE.md"),"fixture documentation");File.WriteAllText(Path.Combine(folder,"symbols.pdb"),"fixture symbols");
        projectAutomation[project.FullName]=new(project.FullName,source,folder,"version.txt");SaveProjects();
    }
    private void ReleaseFileSelectionSmoke(ManagedProject project,ReleaseFilePicker picker,string path)
    {
        var defaults=picker.SelectedNames.Length==4;picker.SetSelected("USAGE.md",false);picker.SetSelected("symbols.pdb",false);var selected=picker.SelectedNames;
        using var reopened=new LocalStore(store.Root);var saved=reopened.Get(ProjectsKey+"/automation",new Dictionary<string,ProjectAutomation>())[project.FullName];var persisted=saved.SelectedAssets!=null&&saved.SelectedAssets.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(selected);
        File.WriteAllText(Path.Combine(saved.ReleaseFolder,"new-file.zip"),"not selected");var plan=AutomationRules.Plan(saved);var automatic=plan.Assets.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(selected)&&plan.Assets.Length==2;
        picker.Load(saved.ReleaseFolder,saved.SelectedAssets);var reload=picker.SelectedNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(selected);
        picker.SelectAll(false);var emptySaved=projectAutomation[project.FullName].SelectedAssets is {Count:0};var emptyBlocked=false;try{AutomationRules.Plan(projectAutomation[project.FullName]);}catch(IOException){emptyBlocked=true;}
        foreach(var name in selected)picker.SetSelected(name,true);
        var report=new{DefaultAllSelected=defaults,SelectionPersistsAcrossRestart=persisted,AutomaticPlanUsesOnlySelected=automatic,NewFilesRemainUnselected=reload,EmptyChoiceSaved=emptySaved,EmptyChoiceStopsAutomaticUpload=emptyBlocked,Selected=selected};
        File.WriteAllText(Path.ChangeExtension(path,".release-selection.json"),JsonSerializer.Serialize(report));
        if(!defaults||!persisted||!automatic||!reload||!emptySaved||!emptyBlocked)throw new IOException("Release file selection lifecycle regression");
    }
}
