namespace Gitstar.Core;

public static class ReminderRules
{
    public static bool Restricted(Binding binding,DateTimeOffset now)=>binding.Disabled||binding.PauseUntil>now||binding.IgnoredTag.Length>0;
    public static string Status(Binding binding,DateTimeOffset now)
    {
        var parts=new List<string>();
        if(binding.Disabled)parts.Add("已停止自动检查");
        if(binding.PauseUntil>now)parts.Add($"暂停至 {binding.PauseUntil.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
        if(binding.IgnoredTag.Length>0)parts.Add("已忽略版本 "+binding.IgnoredTag);
        return parts.Count==0?"正常检查与提醒":string.Join(" · ",parts);
    }
    public static void Restore(Settings settings,Binding binding,InstalledApp? app)
    {
        binding.Disabled=false;binding.PauseUntil=null;binding.IgnoredTag="";
        ClearNotifications(settings,binding.AppId,app);
    }
    public static void ClearNotifications(Settings settings,string appId,InstalledApp? app)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal){appId};
        if(app!=null){ids.Add(app.Id);ids.UnionWith(app.AliasIds??[]);}
        settings.Notified.RemoveAll(key=>key.LastIndexOf('/') is var index&&index>0&&ids.Contains(key[..index]));
    }
}
