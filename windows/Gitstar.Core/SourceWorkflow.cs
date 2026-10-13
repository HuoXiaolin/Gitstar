namespace Gitstar.Core;

public enum WorkspaceAction {UseRoot,Initialize,RepairOrigin,CopyIndependent}
public static class SourceWorkflow
{
    public static bool SameSnapshot(GitProjectState previous,GitProjectState current)=>previous.Folder.Equals(current.Folder,StringComparison.OrdinalIgnoreCase)&&previous.Branch==current.Branch&&previous.Remote==current.Remote&&previous.Commit==current.Commit&&previous.Changes.SequenceEqual(current.Changes);
    public static WorkspaceAction Recommend(WorkspaceDiagnosis diagnosis,string repository)=>!diagnosis.HasRepository?WorkspaceAction.Initialize:!diagnosis.IsRoot?WorkspaceAction.CopyIndependent:ProjectRules.MatchesRemote(diagnosis.Remote,repository)?WorkspaceAction.UseRoot:WorkspaceAction.RepairOrigin;
    public static string CommitMessage(IEnumerable<GitChange> changes)
    {
        var list=changes.ToArray();if(list.Length==0)return "更新源码";
        return "更新源码："+string.Join("，",list.GroupBy(c=>c.StatusName).Select(g=>$"{g.Key} {g.Count()} 项"));
    }
}
