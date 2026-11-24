using System.Windows;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private bool ConfirmFollowRemoval(Window owner, string account, IEnumerable<string> names)
    {
        if (quitting) return false;
        if (account != CacheKey) throw new IOException("账号已改变，请重新打开关注管理。");

        var targets = FollowLists.Normalize(names).Where(repo => !FollowLists.IsRemoved(settings, repo)).ToArray();
        if (targets.Length == 0) return true;
        var preview = string.Join("\n", targets.Take(5));
        if (targets.Length > 5) preview += $"\n另有 {targets.Length - 5} 个项目";

        var answer = DialogMessage.Show(owner,
            $"从 Gitstar 列表移除以下项目？\n\n{preview}\n\n将停止检查这些仓库。本机软件关联和已下载的文件会保留。可在「取消关注历史」恢复项目。",
            "从列表移除", MessageBoxButton.OKCancel);
        if (answer != MessageBoxResult.OK || quitting) return false;
        if (account != CacheKey) throw new IOException("账号已改变，请重新打开关注管理。");

        var removed = targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        foreach (var repo in targets)
            FollowLists.RemoveFromList(settings, repo, groups, now);

        store.Set("settings", settings);
        repositories.RemoveAll(repo => removed.Contains(repo.FullName));
        releases.RemoveAll(release => removed.Contains(release.Repository));
        SaveCache();
        PlanUpdates();
        RebuildGroups();
        model.Status = targets.Length == 1 ? $"已从列表移除 {targets[0]}。" : $"已从列表移除 {targets.Length} 个项目。";
        return true;
    }
}
