namespace Gitstar.Desktop;

public record SoftwareAuditScan(List<InstalledApp> Apps,List<string> Warnings);
public static partial class WindowsServices
{
    public static SoftwarePathState AuditPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path))return SoftwarePathState.Unavailable;
        try{
            path=Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if(path.StartsWith(@"\\",StringComparison.Ordinal))return SoftwarePathState.Unavailable;
            var drive=new DriveInfo(Path.GetPathRoot(path)!);if(!drive.IsReady)return SoftwarePathState.Unavailable;
            var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)return SoftwarePathState.Unavailable;
            if(path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&(attributes&FileAttributes.Directory)!=0)return SoftwarePathState.Unavailable;
            return SoftwarePathState.Present;
        }catch(FileNotFoundException){return SoftwarePathState.Missing;}catch(DirectoryNotFoundException){return SoftwarePathState.Missing;}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException){return SoftwarePathState.Unavailable;}
    }
    public static SoftwareAuditScan AuditSoftware(IEnumerable<Binding> bindings,IEnumerable<InstalledApp> portable,CancellationToken ct,IEnumerable<InstalledApp>? previous=null)
    {
        var warnings=new List<string>();ct.ThrowIfCancellationRequested();var records=RegisteredPrograms(warnings.Add);ct.ThrowIfCancellationRequested();
        var local=records.Select(record=>{if(record.App.Location.StartsWith(@"\\")||record.Icon.TrimStart('"').StartsWith(@"\\")||record.Uninstall.TrimStart('"').StartsWith(@"\\")){warnings.Add("网络安装位置暂不自动核对，请手动确认。");return record with{App=record.App with{Location="",ExecutablePath=""},Icon="",Uninstall=""};}return record;}).ToArray();
        var apps=ScanInventory(local,[],[],[],false,ct:ct);var subscriptions=bindings.Select(binding=>{var copy=binding.Snapshot();if(copy.ExecutablePath.Length==0)copy.ExecutablePath=InstalledVersion.Find(previous??[],copy.AppId)?.ExecutablePath??"";return copy;}).ToArray();
        foreach(var app in portable){ct.ThrowIfCancellationRequested();if(app.ExecutablePath.Length>0&&AuditPath(app.ExecutablePath)==SoftwarePathState.Present)try{apps.Add(DescribePortable(app.ExecutablePath,app.Name));}catch(IOException){}}
        foreach(var program in RelatedRunningPrograms(subscriptions)){ct.ThrowIfCancellationRequested();try{apps.Add(DescribePortable(program.Path));}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}}
        foreach(var binding in subscriptions){
            ct.ThrowIfCancellationRequested();if(binding.ExecutablePath.Length==0||AuditPath(binding.ExecutablePath)!=SoftwarePathState.Missing)continue;
            var folder=Path.GetDirectoryName(binding.ExecutablePath);if(folder==null||AuditPath(folder)!=SoftwarePathState.Present)continue;
            try{foreach(var candidate in PortableExecutables(folder)){ct.ThrowIfCancellationRequested();apps.Add(DescribePortable(candidate));}}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        }
        return new(InstalledIdentity.Merge(apps),warnings.Distinct().ToList());
    }
}
