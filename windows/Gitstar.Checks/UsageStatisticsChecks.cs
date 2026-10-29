using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;
using System.Text.Json;
internal static class UsageStatisticsChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var now=new DateTimeOffset(2026,10,29,23,59,0,TimeSpan.FromHours(8));var stats=UsageStatistics.Create(now);
        stats.Launch(now);check(stats.Launches==1&&stats.ActiveDays==0&&stats.Opens==0&&stats.LastUsed==null,"silent startup increments launches without inventing active usage");
        stats.Open(now);stats.Open(now);stats.Use(UsageFeature.Download,now);check(stats.ActiveDays==1&&stats.Opens==2&&stats.Actions==1,"same-day openings and actions share one active day");
        stats.Use(UsageFeature.SoftwareBinding,now.AddMinutes(2));check(stats.ActiveDays==2&&stats.Days["2026-10-30"].Actions==1,"usage follows local calendar day across midnight");
        stats.Open(now.AddDays(-9));check(stats.ActiveDaysSince(new(2026,10,23),new(2026,10,29))==1&&stats.ActiveDaysSince(new(2026,9,30),new(2026,10,29))==2,"weekly and monthly usage windows exclude future and older days correctly");
        var copy=JsonSerializer.Deserialize<UsageStatistics>(JsonSerializer.Serialize(stats))!;check(copy.Features[UsageFeature.Download]==1&&copy.ActiveDays==3&&copy.Launches==1,"usage counters and feature keys survive JSON persistence");
        var count=copy.Actions;copy.Use((UsageFeature)999,now);check(copy.Actions==count,"usage ignores unrecognized feature identifiers instead of storing arbitrary content");
        using var store=new LocalStore(Path.Combine(root,"usage-store"));store.Set("settings",new Settings{ExtraRepositories=["owner/editor"]});store.Set("usage/v1",stats);store.Set("usage/v1",UsageStatistics.Create(now.AddDays(1)));var cleared=store.Get("usage/v1",stats);
        check(cleared.Launches==0&&cleared.Opens==0&&cleared.ActiveDays==0&&cleared.Features.Count==0&&cleared.Days.Count==0,"reset clears all usage counters and dates immediately");
        check(store.Get("settings",new Settings()).ExtraRepositories.SequenceEqual(["owner/editor"]),"resetting usage retains unrelated settings and repositories");
    }
}
