using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void Reminders_Click(object sender,RoutedEventArgs e)=>ReminderDialog();
    private void RestoreReminders(IEnumerable<Binding> bindings)
    {
        foreach(var binding in bindings)ReminderRules.Restore(settings,binding,InstalledVersion.Find(installed,binding.AppId));
        Save();PlanUpdates();Display();model.Status="已恢复软件更新检查与提醒；已根据缓存更新列表，可点击“检查更新”获取最新版本。";
    }
    private void ReminderDialog(string? previewPath=null)=>SoftwareManagementDialog(previewPath);
    private void ReminderSmoke(string path)
    {
        installed=[new("fixture/stopped","示例编辑器","1.0","",AliasIds:["old/editor"]),new("fixture/paused","示例同步工具","1.0",""),new("fixture/ignored","示例终端工具","1.0","")];
        settings.Bindings=[new(){AppId=installed[0].Id,Repository="example/editor",Disabled=true},new(){AppId=installed[1].Id,Repository="example/sync",PauseUntil=DateTimeOffset.UtcNow.AddDays(7)},new(){AppId=installed[2].Id,Repository="example/terminal",IgnoredTag="v2.0"}];
        releases=settings.Bindings.Select((b,i)=>new Release(i+900,b.Repository,"v2.0","fixture","",false,DateTimeOffset.UtcNow,"",[])).ToList();
        settings.Notified=["old/editor/900","fixture/stopped/900","unrelated/app/999"];PlanUpdates();Page(true);var hidden=model.Cards.Count==0;
        RestoreReminders([settings.Bindings[0]]);var restored=model.Cards.Count==1&&settings.Notified.SequenceEqual(["unrelated/app/999"]);
        using(var reopened=new LocalStore(store.Root)){var saved=reopened.Get("settings",new Settings());restored&=!saved.Bindings[0].Disabled&&saved.Bindings[1].PauseUntil>DateTimeOffset.UtcNow;}
        RestoreReminders(settings.Bindings);var allRestored=model.Cards.Count==3&&settings.Bindings.All(b=>!ReminderRules.Restricted(b,DateTimeOffset.UtcNow));
        settings.Bindings[0].Disabled=true;settings.Bindings[1].PauseUntil=DateTimeOffset.UtcNow.AddDays(7);settings.Bindings[2].IgnoredTag="v2.0";Save();PlanUpdates();Display();
        File.WriteAllText(Path.ChangeExtension(path,".reminders.json"),JsonSerializer.Serialize(new{RestrictedUpdatesHidden=hidden,RestoreOneRefreshesCacheAndPersists=restored,RestoreAllRefreshesCache=allRestored,ManagementEntryVisible=RemindersButton.Visibility==Visibility.Visible}));
        if(!hidden||!restored||!allRestored||RemindersButton.Visibility!=Visibility.Visible)throw new IOException("Reminder restoration regression");
        installed.Add(new("fixture/normal","示例截图工具","1.0",""));settings.Bindings.Add(new(){AppId="fixture/normal",Repository="example/capture"});
        if(Environment.GetCommandLineArgs().Contains("--software-editor")){EditSoftwareBinding(this,settings.Bindings[0],path);return;}
        ReminderDialog(path);
    }
}
