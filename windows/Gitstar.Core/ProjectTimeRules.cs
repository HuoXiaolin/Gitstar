namespace Gitstar.Core;

public static class ProjectTimeRules
{
    public static string Time(DateTimeOffset? value)=>value is {} time&&time!=DateTimeOffset.MinValue?time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"尚未获取";
    public static IEnumerable<ManagedProject> Order(IEnumerable<ManagedProject> projects,IReadOnlyDictionary<string,List<ProjectVersion>> versions,UpdateSort sort)=>projects.OrderByDescending(project=>sort==UpdateSort.RepositoryUpdated?project.Updated:versions.TryGetValue(project.FullName,out var releases)?releases.Where(v=>!v.Draft&&v.Published!=null).Select(v=>v.Published!.Value).DefaultIfEmpty(DateTimeOffset.MinValue).Max():DateTimeOffset.MinValue).ThenBy(project=>project.FullName,StringComparer.OrdinalIgnoreCase);
    public static ProjectVersion PreserveLocalActivity(ProjectVersion fresh,ProjectVersion? previous)=>fresh.LastUploadedAt!=null||previous==null?fresh:fresh with{LastUploadedAt=previous.LastUploadedAt,LastUploadedCount=previous.LastUploadedCount,LastReusedCount=previous.LastReusedCount,LastAction=previous.LastAction};
    public static bool SameRemoteVersion(ProjectVersion a,ProjectVersion b)=>a.Id==b.Id&&a.Tag==b.Tag&&a.Name==b.Name&&a.Body==b.Body&&a.Draft==b.Draft&&a.Prerelease==b.Prerelease&&a.AssetCount==b.AssetCount&&a.Updated==b.Updated&&a.Url==b.Url&&a.Published==b.Published&&a.Created==b.Created;
}
