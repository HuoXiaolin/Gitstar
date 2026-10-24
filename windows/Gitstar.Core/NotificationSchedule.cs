namespace Gitstar.Core;

public static class NotificationSchedule
{
    public static readonly int[] Minutes=[0,-1,5,15,30,60,120,360];
    public static readonly string[] Labels=["跟随软件更新检查","仅手动刷新","每 5 分钟","每 15 分钟","每 30 分钟","每小时","每 2 小时","每 6 小时"];
    public static int Normalize(int minutes)=>Minutes.Contains(minutes)?minutes:0;
    public static bool FollowsUpdates(int minutes)=>Normalize(minutes)==0;
    public static bool IsDue(int minutes,DateTimeOffset? lastAttempt,DateTimeOffset now)=>Normalize(minutes)>0&&(lastAttempt==null||now-lastAttempt>=TimeSpan.FromMinutes(Normalize(minutes)));
}
