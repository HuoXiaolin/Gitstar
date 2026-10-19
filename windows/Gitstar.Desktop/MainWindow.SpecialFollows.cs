using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void SpecialFollow_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;if(quitting||(sender as Button)?.Tag is not Card card)return;
        var before=settings.SpecialRepositories?.ToList()??[];var selected=!SpecialFollows.Contains(settings,card.Repository);
        try{SpecialFollows.Set(settings,card.Repository,selected);store.Set("settings",settings);}
        catch(Exception error){settings.SpecialRepositories=before;DialogMessage.Show(this,error.Message,"特别关注未保存");return;}
        foreach(var item in model.Cards.Where(item=>item.Repository.Equals(card.Repository,StringComparison.OrdinalIgnoreCase)))item.IsSpecial=selected;
        var removeFromCurrent=!localPage&&(GroupSidebar.SelectedItem as GroupEntry)?.Id==SpecialFollows.GroupId&&!selected;
        RebuildGroups(refresh:removeFromCurrent);
        model.Status=card.Repository+(selected?" 已加入特别关注。":" 已移出特别关注。");
    }
}
