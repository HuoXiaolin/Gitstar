namespace Gitstar.Core;

public record IdentityRepair(List<InstalledApp> Apps,int Migrated,int Merged);

// Registry product codes can change on every MSI upgrade. Keep installation identity
// separate from its current uninstall key, without conflating side-by-side installs.
public static class InstalledIdentity
{
    private static string Name(string text)=>new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    private static string PathKey(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";
        try{return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'))).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();}
        catch(Exception ex)when(ex is ArgumentException or NotSupportedException or IOException){return "";}
    }
    private static bool SamePath(string a,string b)=>PathKey(a) is {Length:>0} path&&path==PathKey(b);
    private static bool SameScope(string oldId,string newId)
    {
        var a=oldId.Split('/',3);var b=newId.Split('/',3);
        return a.Length==3&&b.Length==3&&a[0].Equals(b[0],StringComparison.OrdinalIgnoreCase)&&a[1].Equals(b[1],StringComparison.OrdinalIgnoreCase);
    }
    private static InstalledApp? Unique(IEnumerable<InstalledApp> apps)
    {
        var matches=apps.DistinctBy(app=>app.Id,StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return matches.Length==1?matches[0]:null;
    }
    public static List<InstalledApp> Merge(IEnumerable<InstalledApp> source)=>source
        .GroupBy(app=>PathKey(app.ExecutablePath) is {Length:>0} path?"file:"+path:RegistrationKey(app),StringComparer.OrdinalIgnoreCase)
        .Select(group=>{
            var current=group.OrderBy(app=>app.Portable).First();
            return current with{AliasIds=group.SelectMany(app=>(app.AliasIds??[]).Append(app.Id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()};
        }).OrderBy(app=>app.Name,StringComparer.OrdinalIgnoreCase).ToList();
    private static string RegistrationKey(InstalledApp app)
    {
        var parts=app.Id.Split('/',3);
        return parts.Length==3&&parts[1].StartsWith("Registry",StringComparison.Ordinal)?"registry:"+parts[0]+"/"+parts[2]+"|"+PathKey(app.Location)+"|"+Name(app.Name)+"|"+app.Version:"id:"+app.Id;
    }

    public static InstalledApp? Target(Binding binding,IEnumerable<InstalledApp> detected,IEnumerable<InstalledApp>? previous=null)
    {
        var apps=detected.ToArray();var ids=binding.AliasIds.Append(binding.AppId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var saved=InstalledVersion.Find(previous??[],binding.AppId);
        var executable=binding.ExecutablePath.Length>0?binding.ExecutablePath:saved?.ExecutablePath??"";
        if(executable.Length>0){
            var physical=apps.Where(app=>SamePath(app.ExecutablePath,executable)).ToArray();
            if(physical.Length>0)return Unique(physical);
        }
        var exact=apps.Where(app=>ids.Contains(app.Id)||(app.AliasIds??[]).Any(ids.Contains)).ToArray();
        if(exact.Length>0)return Unique(exact);
        if(saved?.Portable==true||binding.AppId.StartsWith("portable/",StringComparison.OrdinalIgnoreCase))return null;
        var scoped=apps.Where(app=>!app.Portable&&SameScope(binding.AppId,app.Id)).ToArray();
        var name=binding.AppName.Length>0?binding.AppName:saved?.Name??"";
        var location=binding.AppLocation.Length>0?binding.AppLocation:saved?.Location??"";
        if(name.Length>0){
            var named=scoped.Where(app=>Name(app.Name)==Name(name)).ToArray();
            if(location.Length>0){
                var located=named.Where(app=>SamePath(app.Location,location)).ToArray();
                if(located.Length>0)return Unique(located);
                // A known location changed: do not redirect to another installation.
                if(named.Any(app=>app.Location.Length>0))return null;
            }
            if(named.Length>0)return Unique(named);
        }
        if(executable.Length>0){
            var fileName=Name(Path.GetFileNameWithoutExtension(executable));
            var folder=Path.GetDirectoryName(executable)??"";
            var named=scoped.Where(app=>Name(app.Name)==fileName&&(app.Location.Length==0||SamePath(app.Location,folder))).ToArray();
            if(named.Length>0)return Unique(named);
        }
        // Legacy bindings may have lost every old snapshot. Only accept a unique,
        // exact repository/name match in the same registration scope, never a fuzzy guess.
        if(name.Length==0&&executable.Length==0&&Rules.ValidRepository(binding.Repository)){
            var repository=new Repository(binding.Repository,"",false);
            return Unique(scoped.Where(app=>AppMatcher.Suggest(app,[repository]).Any(match=>match.Reliable&&match.Repository.Equals(binding.Repository,StringComparison.OrdinalIgnoreCase))));
        }
        return null;
    }
    public static void Remember(Binding binding,InstalledApp app)
    {
        binding.AliasIds=binding.AliasIds.Append(binding.AppId).Concat(app.AliasIds??[]).Append(app.Id).Where(id=>id.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        binding.AppId=app.Id;binding.AppName=app.Name;binding.AppLocation=app.ExecutablePath.Length>0?Path.GetDirectoryName(app.ExecutablePath)??app.Location:app.Location;
        if(binding.ExecutablePath.Length==0&&app.ExecutablePath.Length>0)binding.ExecutablePath=app.ExecutablePath;
    }
    public static IdentityRepair Reconcile(Settings settings,IEnumerable<InstalledApp> detected,IEnumerable<InstalledApp>? previous=null)
    {
        var apps=Merge(detected);var migrated=0;var merged=0;
        foreach(var binding in settings.Bindings){
            var target=Target(binding,apps,previous);if(target==null)continue;
            if(!binding.AppId.Equals(target.Id,StringComparison.OrdinalIgnoreCase)){
                migrated++;
                if(apps.Any(app=>app.Id.Equals(binding.AppId,StringComparison.OrdinalIgnoreCase))){
                    // Switching to a different live installation must not turn its still-valid
                    // registration into an alias of the selected copy.
                    var otherId=binding.AppId;binding.AliasIds=binding.AliasIds.Where(id=>!id.Equals(otherId,StringComparison.OrdinalIgnoreCase)).ToArray();binding.AppId=target.Id;
                }
            }
            Remember(binding,target);
            var otherLiveIds=apps.Where(app=>!app.Id.Equals(target.Id,StringComparison.OrdinalIgnoreCase)).Select(app=>app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            binding.AliasIds=binding.AliasIds.Where(id=>!otherLiveIds.Contains(id)).ToArray();
        }
        foreach(var group in settings.Bindings.GroupBy(binding=>new{Id=binding.AppId.ToUpperInvariant(),Repository=binding.Repository.ToUpperInvariant(),Program=PathKey(binding.ExecutablePath),binding.TagPrefix,binding.AssetPattern,binding.IncludePrerelease}).ToArray()){
            var items=group.ToArray();if(items.Length<2||InstalledVersion.Find(apps,items[0].AppId)==null)continue;
            // Conflicting explicit choices need user review rather than silently dropping one.
            if(items.Select(item=>item.IgnoredTag).Where(tag=>tag.Length>0).Distinct(StringComparer.Ordinal).Count()>1)continue;
            var confirmations=items.Where(item=>item.ConfirmedVersion.Length>0).DistinctBy(item=>(item.ConfirmedVersion,item.ConfirmedVersionPath,item.ConfirmedVersionHash)).ToArray();
            if(confirmations.Length>1)continue;
            var keep=items[0];keep.Disabled=items.Any(item=>item.Disabled);
            if(confirmations.Length==1){keep.ConfirmedVersion=confirmations[0].ConfirmedVersion;keep.ConfirmedVersionPath=confirmations[0].ConfirmedVersionPath;keep.ConfirmedVersionHash=confirmations[0].ConfirmedVersionHash;}
            keep.PauseUntil=items.Max(item=>item.PauseUntil);keep.IgnoredTag=items.Select(item=>item.IgnoredTag).FirstOrDefault(tag=>tag.Length>0)??"";
            keep.AliasIds=items.SelectMany(item=>item.AliasIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach(var duplicate in items.Skip(1)){settings.Bindings.Remove(duplicate);merged++;}
        }
        foreach(var app in apps.ToArray()){
            var aliases=settings.Bindings.Where(binding=>binding.AppId.Equals(app.Id,StringComparison.OrdinalIgnoreCase)).SelectMany(binding=>binding.AliasIds);
            apps[apps.IndexOf(app)]=app with{AliasIds=(app.AliasIds??[]).Concat(aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()};
        }
        return new(apps,migrated,merged);
    }
}
