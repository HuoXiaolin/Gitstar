using System.Text.RegularExpressions;
namespace Gitstar.Core;

public record ReleaseDownload(Release Release,Asset Asset);
public static class BulkDownloadRules
{
    public static readonly int[] Days=[30,14,7,3,1];
    public static string[] Extensions(IEnumerable<string> values)=>values.Select(v=>v.Trim().ToLowerInvariant()).Where(v=>v.Length>0).Select(v=>v.StartsWith('.')?v:"."+v).Select(v=>Regex.IsMatch(v,@"^\.[a-z0-9]+(?:\.[a-z0-9]+)*$")?v:throw new ArgumentException("文件格式请输入扩展名，例如 .exe、.msi、.tar.gz。")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static List<ReleaseDownload> Select(IEnumerable<Release> releases,DateTimeOffset now,int days,IEnumerable<string> extensions,int channel=1)
    {
        if(!Days.Contains(days))throw new ArgumentException("请选择 30、14、7、3 或 1 天。");
        var suffixes=Extensions(extensions);if(suffixes.Length==0)throw new ArgumentException("请至少选择一种文件格式。");
        return releases.Where(r=>r.Published>=now.AddDays(-days)&&r.Published<=now&&(channel==1||(channel==2?r.Prerelease:!r.Prerelease))).OrderByDescending(r=>r.Published).SelectMany(r=>r.Assets.Where(a=>a.Size>0&&suffixes.Any(s=>a.Name.EndsWith(s,StringComparison.OrdinalIgnoreCase))).Select(a=>new ReleaseDownload(r,a))).DistinctBy(d=>(d.Release.Repository.ToLowerInvariant(),d.Release.Id,d.Asset.Id)).ToList();
    }
}
