using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void SpecialFollowsSmoke(string path,bool local)
    {
        settings.SpecialRepositories=[];Save();SaveCache();RebuildGroups();var normalBefore=JsonSerializer.Serialize(new{Groups=groups,Local=settings.LocalGroups,Extra=settings.ExtraRepositories});api.Dispose();var transport=new SortingTransport();api=new(settings,transport);
        IEnumerable<Button> Buttons(DependencyObject node){if(node is Button button)yield return button;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var found in Buttons(VisualTreeHelper.GetChild(node,i)))yield return found;}
        Button Heart(string repo){UpdateLayout();return Buttons(this).First(button=>AutomationProperties.GetAutomationId(button)=="SpecialFollowButton"&&button.Tag is Card card&&card.Repository==repo);}
        void Click(string repo)=>Heart(repo).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var before=model.Cards.Single(card=>card.Repository=="example/editor");var heart=Heart(before.Repository);Click(before.Repository);UpdateLayout();
        var shape=(System.Windows.Shapes.Path)heart.Content;var ink=(SolidColorBrush)shape.Fill;var red=before.IsSpecial&&ink.Color.A==255&&ink.Color.R>ink.Color.G&&ink.Color.R>ink.Color.B;var stable=ReferenceEquals(model.Cards.Single(card=>card.Repository==before.Repository),before)&&!before.IsSelected;
        var group=model.Groups.Single(item=>item.Id==SpecialFollows.GroupId);var count=group.Count==1;
        installed=[new("fixture1","示例编辑器","2.3.0",""),new("fixture2","示例同步工具","1.8.0","")];settings.Bindings=[new(){AppId="fixture1",Repository="example/editor"},new(){AppId="fixture2",Repository="example/sync"}];PlanUpdates();Page(true);var localMarked=model.Cards.Single(card=>card.Repository==before.Repository).IsSpecial;Page(false);
        GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(item=>item.Id==SpecialFollows.GroupId);var onlyMarked=model.Cards.Count==1&&model.Cards[0].Repository==before.Repository;
        Click(before.Repository);var removed=model.Cards.Count==0&&model.Groups.Single(item=>item.Id==SpecialFollows.GroupId).Count==0&&(GroupSidebar.SelectedItem as GroupEntry)?.Id==SpecialFollows.GroupId;
        GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(item=>item.Id=="all");Click(before.Repository);settings=store.Get("settings",new Settings());LoadCache();RebuildGroups();Page(local);var restored=model.Cards.Single(card=>card.Repository==before.Repository).IsSpecial;
        var selected=Heart(before.Repository);var header=(Grid)selected.Parent;var position=selected.TranslatePoint(new Point(),header);var right=Math.Abs(position.X+selected.ActualWidth-header.ActualWidth)<1.1&&Grid.GetRow(selected)==0;
        var clear=Heart("example/sync");var outline=((SolidColorBrush)((System.Windows.Shapes.Path)clear.Content).Fill).Color.A==0;
        var noCloud=transport.Requests==0;var noNormalGroupChanges=normalBefore==JsonSerializer.Serialize(new{Groups=groups,Local=settings.LocalGroups,Extra=settings.ExtraRepositories});
        File.WriteAllText(Path.ChangeExtension(path,".special-follows.json"),JsonSerializer.Serialize(new{RedHeart=red,OutlineHeart=outline,TopRight=right,NoCardResetOrBulkSelection=stable,GroupCount=count,SpecialGroupFilters=onlyMarked,SecondClickRemoves=removed,RecentAndLocalShareState=localMarked,RestoredAfterReload=restored,NoGitHubRequests=noCloud,OriginalGroupsRetained=noNormalGroupChanges}));
        if(!red||!outline||!right||!stable||!count||!onlyMarked||!removed||!localMarked||!restored||!noCloud||!noNormalGroupChanges)throw new IOException("Special follow heart or group regression");
        if(Environment.GetCommandLineArgs().Contains("--special-group")&&!local)GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(item=>item.Id==SpecialFollows.GroupId);
    }
}
