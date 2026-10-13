namespace Gitstar.Core;

public record ManagedProject(string FullName,string Description,bool Private,string DefaultBranch,bool CanPush,bool CanAdmin,bool Archived,DateTimeOffset Updated,string Homepage="",string RepoVisibility="",bool Fork=false,long Id=0)
{
    public string Name=>FullName.Split('/').Last();
    public string Owner=>FullName.Split('/')[0];
    public string Visibility=>RepoVisibility=="internal"?"内部":Private?"私有":"公开";
    public string Access=>Archived?"已归档":CanAdmin?"管理员":CanPush?"可提交":"只读";
}
public record ProjectWorkspace(string Repository,string Folder,string Branch="");
public record ProjectVersion(long Id,string Tag,string Name,string Body,bool Draft,bool Prerelease,int AssetCount,DateTimeOffset Updated,string Url)
{
    public string Channel=>Draft?"草稿":Prerelease?"测试版":"正式版";
    public string DisplayVersion=>Draft&&ReleaseRules.TemporaryTag(Tag)?(Rules.CompareVersions(Name,Name)!=null?Name+" · 未绑定标签":"未设置版本号"):Tag;
}
public record GitChange(string Status,string Path,string Original="")
{
    public string StatusName=>Status=="??"?"新增":Status=="!!"?"已忽略":Status.Contains('U') || Status is "AA" or "DD"?"冲突":Status.Contains('R')?"重命名":Status.Contains('C')?"复制":Status.Contains('D')?"删除":Status.Contains('A')?"新增":Status.Contains('M')?"修改":Status.Contains('T')?"类型变更":"变更";
    public string Label=>StatusName+"  ·  "+Path+(Original.Length>0?"  ← "+Original:"");
    public string[] Paths=>Original.Length>0?[Path,Original]:[Path];
}
public record GitProjectState(string Folder,string Branch,string Remote,string Commit,List<GitChange> Changes,string Summary)
{
    public string? RemoteCommit {get;init;}
    public bool SourceSynced=>Commit.Length>0 && Commit.Equals(RemoteCommit,StringComparison.OrdinalIgnoreCase);
    public string SyncSummary=>Changes.Count>0?$"{Changes.Count} 个未提交变更，可上传更新。":Commit.Length==0?"尚无提交，请先添加源码。":SourceSynced?"源码已与 GitHub 同步；安装包更新请使用“同步并发布最新”。":RemoteCommit==null?"没有未提交变更；GitHub 提交状态尚未核实，可推送已有提交。":"没有未提交变更；本地提交与 GitHub 不同，可推送已有提交。";
}
public record WorkspaceDiagnosis(string Folder,string Root,string Remote)
{
    public bool HasRepository=>Root.Length>0;
    public bool IsRoot=>HasRepository && Root.TrimEnd('\\','/').Equals(Folder.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);
}
public static class ProjectRules
{
    public static bool SameRepository(ManagedProject previous,ManagedProject current)=>previous.Id>0 && previous.Id==current.Id && previous.FullName.Equals(current.FullName,StringComparison.OrdinalIgnoreCase);
    public static string[] InvalidatedProjects(IEnumerable<ManagedProject> previous,IEnumerable<ManagedProject> current)
    {
        var latest=current.ToDictionary(p=>p.FullName,StringComparer.OrdinalIgnoreCase);
        return previous.Where(p=>!latest.TryGetValue(p.FullName,out var found)||!SameRepository(p,found)).Select(p=>p.FullName).ToArray();
    }
    public static bool MatchesRemote(string remote,string repo)=>new[]{"https://github.com/"+repo,"https://github.com/"+repo+".git","git@github.com:"+repo+".git"}.Contains(remote.Trim(),StringComparer.OrdinalIgnoreCase);
    public static List<GitChange> ParseStatus(string porcelain)
    {
        var tokens=porcelain.Split('\0');var changes=new List<GitChange>();
        for(var i=0;i<tokens.Length;i++){
            var token=tokens[i];if(token.Length<4 || token[2]!=' ')continue;
            var status=token[..2];var path=token[3..];var original="";
            if(status.Contains('R') || status.Contains('C')){if(++i>=tokens.Length || tokens[i].Length==0)throw new IOException("Git 重命名记录不完整。");original=tokens[i];}
            changes.Add(new(status,path,original));
        }
        return changes;
    }
}
