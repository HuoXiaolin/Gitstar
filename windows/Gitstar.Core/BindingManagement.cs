namespace Gitstar.Core;

public enum BindingAction { IgnoreVersion,PauseWeek,Stop,Restore,Unlink }
public record BindingActionResult(int Changed,int Skipped);

public static class BindingManagement
{
    public static BindingActionResult Apply(Settings settings,IEnumerable<Binding> selected,IEnumerable<InstalledApp> apps,IEnumerable<Release> releases,BindingAction action,DateTimeOffset now)
    {
        var items=selected.Distinct().Where(settings.Bindings.Contains).ToArray();var changed=0;var skipped=0;
        foreach(var binding in items){
            var app=InstalledVersion.Find(apps,binding.AppId);
            switch(action){
                case BindingAction.IgnoreVersion:
                    var release=Rules.SelectRelease(releases.Where(r=>r.Repository.Equals(binding.Repository,StringComparison.OrdinalIgnoreCase)),binding);
                    if(release==null){skipped++;continue;}binding.IgnoredTag=release.Tag;break;
                case BindingAction.PauseWeek:binding.PauseUntil=now.AddDays(7);break;
                case BindingAction.Stop:binding.Disabled=true;break;
                case BindingAction.Restore:ReminderRules.Restore(settings,binding,app);break;
                case BindingAction.Unlink:settings.Bindings.Remove(binding);ReminderRules.ClearNotifications(settings,binding.AppId,app);break;
                default:throw new ArgumentOutOfRangeException(nameof(action));
            }
            changed++;
        }
        return new(changed,skipped);
    }
    public static void Update(Settings settings,Binding binding,InstalledApp app,string repository,string executable,bool beta,string prefix,string pattern,IEnumerable<InstalledApp> apps)
    {
        if(!settings.Bindings.Contains(binding))throw new IOException("此关联已移除，请重新打开管理窗口。");
        var repo=Rules.RepositoryName(repository);if(!Rules.ValidRepository(repo))throw new IOException("请输入有效的 GitHub 仓库地址或 owner/repo。");
        if(string.IsNullOrWhiteSpace(app.Id))throw new IOException("请选择本机软件。");
        var ids=(app.AliasIds??[]).Append(app.Id).ToHashSet(StringComparer.Ordinal);
        if(settings.Bindings.Any(other=>other!=binding&&ids.Contains(other.AppId)))throw new IOException("该软件已有其他关联，请先修改或解除已有匹配。");
        var changed=binding.AppId!=app.Id||!binding.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase)||binding.TagPrefix!=prefix.Trim();
        if(changed){ReminderRules.ClearNotifications(settings,binding.AppId,InstalledVersion.Find(apps,binding.AppId));ReminderRules.ClearNotifications(settings,app.Id,app);binding.IgnoredTag="";}
        binding.AppId=app.Id;binding.Repository=repo;binding.ExecutablePath=executable;binding.IncludePrerelease=beta;binding.TagPrefix=prefix.Trim();binding.AssetPattern=pattern.Trim();
        InstalledIdentity.Remember(binding,app);
    }
}
