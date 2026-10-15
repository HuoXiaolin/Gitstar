namespace Gitstar.Core;

public enum SourceRelation {NewRemote,Same,LocalAhead,RemoteAhead,Diverged,Unrelated}
public record SourceSyncState(string Branch,string Local,string Remote,string LocalTree,string RemoteTree,SourceRelation Relation,int LocalOnly,int RemoteOnly,bool Clean,string Differences)
{
    public string Summary=>Relation switch{
        SourceRelation.NewRemote=>"远端分支尚不存在，可推送本地提交。",
        SourceRelation.Same=>"本地与远端提交一致。",
        SourceRelation.LocalAhead=>$"本地领先 {LocalOnly} 个提交，可正常推送。",
        SourceRelation.RemoteAhead=>$"远端领先 {RemoteOnly} 个提交，请先同步远端。",
        SourceRelation.Diverged=>$"本地新增 {LocalOnly} 个提交，远端新增 {RemoteOnly} 个提交，需要合并后推送。",
        _=>"本地与远端没有共同提交历史。通常是重新初始化本地仓库后关联了已有仓库。"
    };
    public bool CanRepairHistory=>Clean&&Local.Length>0&&Remote.Length>0&&Relation==SourceRelation.Unrelated;
}
public static class StatusText
{
    public static string Summary(string text)
    {
        var first=text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Trim()).FirstOrDefault(line=>line.Length>0)??"暂无消息";
        return first.Length>180?first[..180]+"…":first;
    }
}
