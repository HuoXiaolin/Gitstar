using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private (int Page,bool Dark)? accentTheme;
    private static SolidColorBrush UiBrush(string color){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));brush.Freeze();return brush;}
    private void SetPageAccent(int page)
    {
        if(accentTheme==(page,settings.Dark))return;accentTheme=(page,settings.Dark);
        var accent=new[]{"#386AC6","#168673","#6557AB"}[page];
        var soft=settings.Dark?new[]{"#26384F","#233C37","#36314F"}[page]:new[]{"#EAF0FC","#E5F3ED","#EEEBF8"}[page];
        var ink=settings.Dark?new[]{"#9FC0FF","#87D9BF","#C4BBF7"}[page]:new[]{"#2F5FAA","#167560","#5D4BA0"}[page];
        Resources["Accent"]=UiBrush(accent);Resources["AccentSoft"]=UiBrush(soft);Resources["AccentInk"]=UiBrush(ink);Resources["ProjectSelection"]=UiBrush(soft);
        foreach(var button in new[]{RecentButton,LocalButton,ProjectsButton}){button.Background=Brushes.Transparent;button.Foreground=(Brush)Resources["Muted"];}
        var selected=new[]{RecentButton,LocalButton,ProjectsButton}[page];selected.Background=(Brush)Resources["AccentSoft"];selected.Foreground=(Brush)Resources["AccentInk"];
        if(CountPanel!=null){CountPanel.Background=(Brush)Resources["AccentSoft"];CountPanel.BorderBrush=(Brush)Resources["Edge"];}
    }
    private void ApplyTheme()
    {
        var palette=settings.Dark?new[]{"#141B28","#1D2736","#E9EFF7","#B0BCD0","#354254","#253143"}:new[]{"#F4F6FA","#FFFFFF","#19273D","#596B83","#DDE5EF","#EDF2F8"};
        var keys=new[]{"Background","Surface","Ink","Muted","Edge","Panel"};for(var i=0;i<keys.Length;i++)Resources[keys[i]]=UiBrush(palette[i]);
        Resources["StableSoft"]=UiBrush(settings.Dark?"#254137":"#E5F3EC");Resources["StableInk"]=UiBrush(settings.Dark?"#8ED7B6":"#267554");Resources["BetaSoft"]=UiBrush(settings.Dark?"#493A25":"#FCF1DB");Resources["BetaInk"]=UiBrush(settings.Dark?"#EBC388":"#8E6429");
        SetPageAccent(projectsPage?2:localPage?1:0);
    }
}
