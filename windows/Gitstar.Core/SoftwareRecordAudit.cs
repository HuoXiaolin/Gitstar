namespace Gitstar.Core;

public enum SoftwarePathState { Present,Missing,Unavailable }
public enum SoftwareAuditState { Healthy,Repairable,Missing,Uncertain }
public record SoftwareAuditEntry(Binding Binding,string Name,string OriginalPath,SoftwareAuditState State,string Reason,List<InstalledApp> Candidates)
{
    public string Status=>State switch{SoftwareAuditState.Healthy=>"记录正常",SoftwareAuditState.Repairable=>"可修复记录",SoftwareAuditState.Missing=>"疑似已卸载",_=>"需要人工核对"};
}
public static class SoftwareRecordAudit
{
    private static string Name(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string PathKey(string path){try{return path.Length>0?Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))):"";}catch(Exception error)when(error is ArgumentException or IOException or NotSupportedException){return "";}}
    private static bool SamePath(string a,string b)=>PathKey(a) is {Length:>0} path&&path.Equals(PathKey(b),StringComparison.OrdinalIgnoreCase);
    private static bool Registered(Binding binding,InstalledApp app)=>binding.AliasIds.Append(binding.AppId).Intersect((app.AliasIds??[]).Append(app.Id),StringComparer.OrdinalIgnoreCase).Any();
    public static List<SoftwareAuditEntry> Review(IEnumerable<Binding> bindings,IEnumerable<InstalledApp> previous,IEnumerable<InstalledApp> inventory,Func<string,SoftwarePathState> probe,bool registryComplete)
    {
        var known=previous.ToArray();var fresh=inventory.ToArray();var results=new List<SoftwareAuditEntry>();
        foreach(var binding in bindings){
            var saved=InstalledVersion.Find(known,binding.AppId);var name=binding.AppName.Length>0?binding.AppName:saved?.Name??binding.Repository;
            var path=binding.ExecutablePath.Length>0?binding.ExecutablePath:saved?.ExecutablePath??"";
            var location=binding.AppLocation.Length>0?binding.AppLocation:saved?.Location??"";
            var physical=path.Length>0?probe(path):SoftwarePathState.Unavailable;
            var candidates=fresh.Where(app=>app.ExecutablePath.Length>0&&probe(app.ExecutablePath)==SoftwarePathState.Present)
                .Where(app=>SamePath(app.ExecutablePath,path)||Registered(binding,app)||Name(app.Name)==Name(name)||(path.Length>0&&Path.GetFileName(app.ExecutablePath).Equals(Path.GetFileName(path),StringComparison.OrdinalIgnoreCase))||app.RepositoryHint.Equals(binding.Repository,StringComparison.OrdinalIgnoreCase))
                .DistinctBy(app=>PathKey(app.ExecutablePath),StringComparer.OrdinalIgnoreCase).OrderByDescending(app=>SamePath(app.ExecutablePath,path)).ThenBy(app=>app.ExecutablePath,StringComparer.OrdinalIgnoreCase).ToList();
            var registered=fresh.Any(app=>!app.Portable&&Registered(binding,app));
            SoftwareAuditState state;string reason;
            if(physical==SoftwarePathState.Present){
                var current=candidates.FirstOrDefault(app=>SamePath(app.ExecutablePath,path));
                if(current!=null&&!current.Id.Equals(binding.AppId,StringComparison.OrdinalIgnoreCase)){state=SoftwareAuditState.Repairable;reason="程序位置仍有效，安装登记标识已变化，可更新关联记录。";}
                else{state=SoftwareAuditState.Healthy;reason=registered||saved?.Portable==true||binding.AppId.StartsWith("portable/",StringComparison.OrdinalIgnoreCase)?"已确认原位置的主程序仍存在。":"主程序仍存在；当前未找到安装登记，不按已卸载处理。";}
            }else if(candidates.Count>0){state=SoftwareAuditState.Repairable;reason=candidates.Count==1?"找到可用的程序位置，请核对后修复记录。":"找到多个候选位置，请人工选择，不能直接判断为同一安装。";}
            else if(registryComplete&&!registered&&(physical==SoftwarePathState.Missing||path.Length==0&&location.Length>0&&probe(location)==SoftwarePathState.Missing)){state=SoftwareAuditState.Missing;reason="原位置已不存在，且未找到对应安装登记或可用候选；可删除过期记录，也可手动选择新位置。";}
            else{state=SoftwareAuditState.Uncertain;reason=!registryComplete?"部分安装登记无法读取，暂不能判断是否已卸载。":physical==SoftwarePathState.Unavailable?"没有可核对路径，或位置暂不可访问，请检查权限、磁盘和网络。":"安装登记仍在，但无法确认主程序；请手动选择程序位置。";}
            results.Add(new(binding,name,path,state,reason,candidates));
        }
        return results;
    }
    public static void Repair(Settings settings,Binding binding,InstalledApp target,Func<string,SoftwarePathState> probe)
    {
        if(!settings.Bindings.Contains(binding))throw new IOException("软件记录已变化，请重新核对。");
        if(target.ExecutablePath.Length==0||!target.ExecutablePath.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||probe(target.ExecutablePath)!=SoftwarePathState.Present)throw new IOException("目标主程序不存在或暂不可访问，请重新选择。");
        if(settings.Bindings.Any(other=>other!=binding&&(other.AppId.Equals(target.Id,StringComparison.OrdinalIgnoreCase)||SamePath(other.ExecutablePath,target.ExecutablePath))))throw new IOException("此程序位置已有其他关联，请先核对重复记录。");
        var oldId=binding.AppId;var oldPath=binding.ExecutablePath;
        binding.ExecutablePath=Path.GetFullPath(target.ExecutablePath);InstalledIdentity.Remember(binding,target);
        if(!SamePath(oldPath,target.ExecutablePath)){binding.ConfirmedVersion="";binding.ConfirmedVersionPath="";binding.ConfirmedVersionHash="";}
        settings.PortableApps.RemoveAll(app=>app.Id==oldId&&app.Id!=target.Id&&!settings.Bindings.Any(other=>other.AppId==app.Id));
        if(target.Portable){settings.PortableApps.RemoveAll(app=>app.Id==target.Id);settings.PortableApps.Add(target);}
    }
    public static void Remove(Settings settings,Binding binding)
    {
        if(!settings.Bindings.Remove(binding))throw new IOException("软件记录已变化，请重新核对。");
        ReminderRules.ClearNotifications(settings,binding.AppId,null);
        var ids=binding.AliasIds.Append(binding.AppId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.PortableApps.RemoveAll(app=>ids.Contains(app.Id)&&!settings.Bindings.Any(other=>other.AppId.Equals(app.Id,StringComparison.OrdinalIgnoreCase)||(app.ExecutablePath.Length>0&&SamePath(other.ExecutablePath,app.ExecutablePath))));
    }
}
