namespace Gitstar.Core;

public static class RefreshSchedule
{
    public static readonly int[] Hours = [0, 1, 6, 12, 24, 72, 168];
    public static readonly string[] Labels = ["仅手动刷新", "每小时", "每 6 小时", "每 12 小时", "每天", "每 3 天", "每周"];
    public static int Normalize(int hours) => Hours.Contains(hours) ? hours : 24;
    public static string Label(int hours) => Labels[Array.IndexOf(Hours, Normalize(hours))];
    public static bool CanStartAutomatic(bool refreshing,bool busy,bool batchPublishing,bool quitting,bool clearingCache)=>!refreshing&&!busy&&!batchPublishing&&!quitting&&!clearingCache;
    public static bool IsDue(int hours, DateTimeOffset? lastCheck, DateTimeOffset? lastAttempt, DateTimeOffset now)
    {
        hours = Normalize(hours);
        return hours > 0 && (lastAttempt == null || now - lastAttempt >= TimeSpan.FromHours(1))
            && (lastCheck == null || now - lastCheck >= TimeSpan.FromHours(hours));
    }
}
