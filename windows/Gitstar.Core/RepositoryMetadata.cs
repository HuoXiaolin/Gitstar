namespace Gitstar.Core;

public record RepositoryMetadata(long? Stars=null,string License="",DateTimeOffset? Updated=null)
{
    public string StarsLabel=>Stars is {} count?"★ "+count.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" Stars":"☆ Stars 待同步";
    public string LicenseLabel=>License.Length>0?License:"License 未识别";
}
