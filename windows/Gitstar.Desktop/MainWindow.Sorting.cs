using System.Net.Http;
using System.Windows;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed class SortingTransport : HttpMessageHandler
    {
        public int Requests {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Requests++;throw new IOException("Sorting must not access the network");}
    }
    private void SortingSmoke(string path)
    {
        var now=DateTimeOffset.UtcNow;
        repositories=[new("example/editor","",false){Updated=now.AddDays(-2)},new("example/sync","",false){Updated=now}];
        releases.Add(new(104,"example/editor","v1.0.0","旧版本","",false,now.AddYears(-1),"",[]));
        SaveCache();api.Dispose();var transport=new SortingTransport();api=new(settings,transport);
        SortFilter.SelectedIndex=0;Page(false);
        var published=model.Cards.Select(c=>c.Repository).ToArray();var count=model.Count;
        model.SelectionMode=true;model.Cards[0].IsSelected=true;
        SortFilter.SelectedIndex=1;
        var updated=model.Cards.Select(c=>c.Repository).ToArray();
        var distinct=published.SequenceEqual(new[]{"example/editor","example/sync","example/android"})&&updated.SequenceEqual(new[]{"example/sync","example/editor","example/android"});
        var selectionRetained=model.Cards.Single(c=>c.Repository=="example/editor").IsSelected;
        var newestVersionRetained=model.Cards.Single(c=>c.Repository=="example/editor").Release.Id==101;
        var countsUnchanged=model.Count==count;
        Page(true);var localHidden=SortFilter.Visibility==Visibility.Collapsed;Page(false);LoadCache();Display();
        using var saved=new LocalStore(store.Root);
        var persisted=saved.Get("settings",new Settings()).RecentSort==UpdateSort.RepositoryUpdated&&saved.Get(CacheKey+"/repos",new List<Repository>()).Single(r=>r.FullName=="example/sync").Updated==now;
        if(!distinct||!selectionRetained||!newestVersionRetained||!countsUnchanged||!localHidden||!persisted||transport.Requests!=0)throw new IOException("Updated sorting regression");
        File.WriteAllText(Path.ChangeExtension(path,".sorting.json"),JsonSerializer.Serialize(new{PublishedOrder=published,UpdatedOrder=updated,SelectionRetained=selectionRetained,NewestVersionRetained=newestVersionRetained,SevenDayCountUnchanged=countsUnchanged,LocalSortHidden=localHidden,PreferenceAndCachePersisted=persisted,NetworkRequests=transport.Requests}));
    }
    private static void TrayDotSmoke(string path)
    {
        using var plainIcon=TrayBadge.Create(false);using var dotIcon=TrayBadge.Create(true);
        using var plain=plainIcon.ToBitmap();using var dot=dotIcon.ToBitmap();
        plain.Save(Path.ChangeExtension(path,".tray-plain.png"));dot.Save(Path.ChangeExtension(path,".tray-dot.png"));
        var changed=0;var topRightOnly=true;
        for(var y=0;y<plain.Height;y++)for(var x=0;x<plain.Width;x++)if(plain.GetPixel(x,y)!=dot.GetPixel(x,y)){changed++;topRightOnly&=x>=20&&y<=12;}
        if(changed==0||!topRightOnly)throw new IOException("Tray notification dot placement regression");
        File.WriteAllText(Path.ChangeExtension(path,".tray.json"),JsonSerializer.Serialize(new{TopRightOnly=topRightOnly,ChangedPixels=changed,Width=dot.Width,Height=dot.Height}));
    }
    private void VersionSelectionSmoke(string path)
    {
        var now=DateTimeOffset.UtcNow;
        var main=new Release(201,"grafana/grafana","v13.2.4","13.2.4","Download page\nWhat's new highlights\nSecurity",false,now.AddHours(-14),"",[]){IsLatest=true};
        var backport=main with{Id=202,Tag="v12.4.13",Name="12.4.13",Published=now.AddHours(-6),IsLatest=false};
        var beta=main with{Id=203,Tag="v13.3.0-beta.2",Prerelease=true,IsLatest=false,Published=now.AddHours(-20)};
        repositories=[new("grafana/grafana","",false){Updated=now}];groups=[];settings.LocalGroups=[];
        releases=[backport,main,beta];SaveCache();LoadCache();RebuildGroups();Page(false);
        ChannelFilter.SelectedIndex=0;var stable=model.Cards.Single().Release.Tag;
        installed=[new("grafana-local","Grafana","13.2.3","")];settings.Bindings=[new(){AppId="grafana-local",Repository="grafana/grafana"}];PlanUpdates();
        Page(true);var installedTarget=model.Cards.Single().Release.Tag;
        installed=[installed.Single() with{Version="13.2.4"}];PlanUpdates();Display();var alreadyCurrent=updates.Count==0;
        Page(false);ChannelFilter.SelectedIndex=1;var combined=model.Cards.Single().Release.Tag;
        ChannelFilter.SelectedIndex=2;var betaOnly=model.Cards.Single().Release.Tag;
        SortFilter.SelectedIndex=1;var sortDoesNotChangeVersion=model.Cards.Single().Release.Tag==betaOnly;
        ChannelFilter.SelectedIndex=0;
        if(stable!="v13.2.4"||installedTarget!=stable||!alreadyCurrent||combined!=beta.Tag||betaOnly!=beta.Tag||!sortDoesNotChangeVersion)throw new IOException("Release selection UI regression");
        File.WriteAllText(Path.ChangeExtension(path,".selection.json"),JsonSerializer.Serialize(new{Stable=stable,LocalUpdateTarget=installedTarget,AlreadyCurrentNotOffered=alreadyCurrent,Combined=combined,Testing=betaOnly,SortDoesNotChangeVersion=sortDoesNotChangeVersion,CacheReloaded=true}));
    }
}
