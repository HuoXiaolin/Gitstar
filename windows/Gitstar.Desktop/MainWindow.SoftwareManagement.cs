using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void SoftwareManagementDialog(string? previewPath=null,Binding? focus=null)
    {
        var f=new Form(this,"本机软件管理",940,740);
        f.Label("集中管理已关联软件的仓库匹配、版本忽略和检查状态。支持多选与全选当前结果，修改后立即依据已有记录更新列表。");
        var filters=new Grid();filters.ColumnDefinitions.Add(new());filters.ColumnDefinitions.Add(new(){Width=new GridLength(190)});var searchPanel=new StackPanel{Margin=new Thickness(0,0,12,0)};var statePanel=new StackPanel();filters.Children.Add(searchPanel);Grid.SetColumn(statePanel,1);filters.Children.Add(statePanel);f.Fields.Children.Add(filters);
        TextBox search=null!;ComboBox filter=null!;f.Into(searchPanel,()=>search=f.Text("搜索软件或仓库",focus?.Repository??""));f.Into(statePanel,()=>filter=f.Select("检查状态",["全部软件","正常检查","已停止","已暂停","已忽略版本"]));
        var selectionLine=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};var selectAll=new CheckBox{Content="全选当前结果",IsThreeState=true,VerticalAlignment=VerticalAlignment.Center};var count=ProjectText("",12,true);count.Margin=new Thickness(14,0,0,0);selectionLine.Children.Add(selectAll);selectionLine.Children.Add(count);f.Fields.Children.Add(selectionLine);
        var list=new StackPanel();f.Fields.Children.Add(list);var selected=new HashSet<Binding>();Binding[] visible=[];var selecting=false;
        var action=f.Select("批量操作",["选择批量操作","忽略当前版本","暂停检查 7 天","停止自动检查","恢复检查与提醒","解除关联"]);f.Fields.Children.Remove(action);var actionLabel=f.Fields.Children[f.Fields.Children.Count-1];f.Fields.Children.Remove(actionLabel);action.Width=156;action.Margin=new Thickness(8,6,0,0);f.Buttons.Children.Add(action);
        var result=ProjectText("",12,true);result.Visibility=Visibility.Collapsed;f.Fields.Children.Insert(f.Fields.Children.IndexOf(list),result);
        Button apply=null!;
        void SelectionState(){selecting=true;try{var n=visible.Count(selected.Contains);selectAll.IsChecked=n==0?false:n==visible.Length?true:null;count.Text=$"共 {visible.Length} 项，已选 {n} 项";apply.IsEnabled=n>0&&action.SelectedIndex>0;}finally{selecting=false;}}
        void Apply(Binding[] targets,BindingAction operation)
        {
            if(targets.Length==0)return;
            if(operation==BindingAction.Unlink&&DialogMessage.Show(f,$"解除 {targets.Length} 项软件关联？\n\n"+string.Join("\n",targets.Take(8).Select(b=>InstalledVersion.Find(installed,b.AppId)?.Name??b.Repository))+"\n\n仅移除 Gitstar 中的匹配和检查设置，保留软件、便携软件记录与已下载安装包。","解除软件关联",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            var outcome=BindingManagement.Apply(settings,targets,installed,releases,operation,DateTimeOffset.UtcNow);Save();PlanUpdates();Display();
            result.Text=$"已处理 {outcome.Changed} 项"+(outcome.Skipped>0?$"；{outcome.Skipped} 项没有可用版本记录，请先检查更新。":"；设置已保存。");result.Visibility=Visibility.Visible;model.Status=result.Text;
            selected.RemoveWhere(b=>!settings.Bindings.Contains(b));Render();
        }
        void Render()
        {
            var now=DateTimeOffset.UtcNow;var query=search.Text.Trim();
            visible=settings.Bindings.Where(b=>((InstalledVersion.Find(installed,b.AppId)?.Name??"")+" "+b.Repository+" "+b.AppId).Contains(query,StringComparison.OrdinalIgnoreCase)).Where(b=>filter.SelectedIndex switch{1=>!ReminderRules.Restricted(b,now),2=>b.Disabled,3=>b.PauseUntil>now,4=>b.IgnoredTag.Length>0,_=>true}).OrderBy(b=>InstalledVersion.Find(installed,b.AppId)?.Name??b.Repository,StringComparer.OrdinalIgnoreCase).ToArray();
            selected.IntersectWith(visible);list.Children.Clear();
            if(visible.Length==0)list.Children.Add(ProjectText(settings.Bindings.Count==0?"尚未关联软件，可点击“添加 / 自动匹配”。":"没有符合条件的软件，请调整搜索或筛选。",13,true));
            foreach(var binding in visible){
                var app=InstalledVersion.Find(installed,binding.AppId);var body=new StackPanel();var heading=new Grid();heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var check=new CheckBox{IsChecked=selected.Contains(binding),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,10,0)};System.Windows.Automation.AutomationProperties.SetName(check,"选择 "+(app?.Name??binding.Repository));check.Checked+=(_,_)=>{selected.Add(binding);SelectionState();};check.Unchecked+=(_,_)=>{selected.Remove(binding);SelectionState();};heading.Children.Add(check);
                var info=new StackPanel{Margin=new Thickness(0,0,12,0)};info.Children.Add(ProjectText((app?.Name??binding.Repository.Split('/').Last())+(app!=null?"  ·  "+app.Version:""),14));info.Children.Add(ProjectText(binding.Repository,12,true));info.Children.Add(ProjectText(ReminderRules.Status(binding,now),12,true));var source=app==null?"本机记录待检测 · "+binding.AppId:app.ExecutablePath.Length>0?app.ExecutablePath:app.Location.Length>0?app.Location:app.Id;info.Children.Add(ProjectText(source,11,true));Grid.SetColumn(info,1);heading.Children.Add(info);
                foreach(var label in info.Children.OfType<TextBlock>())label.Margin=new Thickness(0,0,0,3);var sourceLabel=(TextBlock)info.Children[info.Children.Count-1];sourceLabel.TextWrapping=TextWrapping.NoWrap;sourceLabel.TextTrimming=TextTrimming.CharacterEllipsis;sourceLabel.ToolTip=source;
                var edit=new Button{Content="修改匹配",VerticalAlignment=VerticalAlignment.Top};edit.Click+=(_,_)=>{EditSoftwareBinding(f,binding);Render();};Grid.SetColumn(edit,2);heading.Children.Add(edit);body.Children.Add(heading);
                var buttons=new WrapPanel{Margin=new Thickness(28,8,0,0)};body.Children.Add(buttons);
                void Inline(string text,BindingAction operation,bool enabled=true){var button=new Button{Content=text,Margin=new Thickness(0,0,8,4),Padding=new Thickness(10,6,10,6),IsEnabled=enabled};if(operation==BindingAction.Unlink)button.Foreground=new SolidColorBrush(Color.FromRgb(190,60,75));button.Click+=(_,_)=>Apply([binding],operation);buttons.Children.Add(button);}
                Inline("忽略当前版本",BindingAction.IgnoreVersion,Rules.SelectRelease(releases.Where(r=>r.Repository.Equals(binding.Repository,StringComparison.OrdinalIgnoreCase)),binding)!=null);Inline("暂停 7 天",BindingAction.PauseWeek);if(ReminderRules.Restricted(binding,now))Inline("恢复检查",BindingAction.Restore);if(!binding.Disabled)Inline("停止检查",BindingAction.Stop);Inline("解除关联",BindingAction.Unlink);
                var card=new Border{Child=body,Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12)};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Edge");list.Children.Add(card);
            }
            SelectionState();
        }
        apply=f.Action("应用到所选",()=>{if(action.SelectedIndex>0)Apply(visible.Where(selected.Contains).ToArray(),(BindingAction)(action.SelectedIndex-1));return Task.CompletedTask;});
        f.Action("添加 / 自动匹配",()=>{f.Close();AutoBindingDialog();return Task.CompletedTask;});f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});
        selectAll.Click+=(_,_)=>{if(selecting)return;if(selectAll.IsChecked==true)selected.UnionWith(visible);else selected.ExceptWith(visible);Render();};
        search.TextChanged+=(_,_)=>{selected.Clear();Render();};filter.SelectionChanged+=(_,_)=>{selected.Clear();Render();};action.SelectionChanged+=(_,_)=>SelectionState();Render();
        if(previewPath!=null){if(Environment.GetCommandLineArgs().Contains("--software-multi")){selectAll.IsChecked=true;selectAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));action.SelectedIndex=3;SelectionState();var before=visible.Length;apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var stopped=visible.All(b=>b.Disabled);action.SelectedIndex=4;apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var restored=visible.All(b=>!ReminderRules.Restricted(b,DateTimeOffset.UtcNow));filter.SelectedIndex=2;var selectionCleared=selected.Count==0&&apply.IsEnabled==false;filter.SelectedIndex=0;selected.UnionWith(visible);Render();File.WriteAllText(Path.ChangeExtension(previewPath,".software-multi.json"),JsonSerializer.Serialize(new{DefaultShowsAll=before==settings.Bindings.Count,SelectAllAndBatchStop=stopped,BatchRestore=restored,FilterClearsHiddenSelection=selectionCleared}));if(!stopped||!restored||!selectionCleared)throw new IOException("Software multiselect regression");}f.RenderPreview(previewPath);}f.ShowDialog();
    }
    private void EditSoftwareBinding(Window owner,Binding binding,string? previewPath=null)
    {
        var f=new Form(owner,"修改软件匹配",760,720);var known=InstalledVersion.Find(installed,binding.AppId);
        var choices=installed.GroupBy(a=>a.Id).Select(g=>g.First()).OrderBy(a=>a.Name,StringComparer.OrdinalIgnoreCase).ToList();if(known==null){known=new(binding.AppId,binding.Repository.Split('/').Last(),"待检测","");choices.Insert(0,known);}var index=choices.FindIndex(a=>a.Id==known.Id);
        var software=f.Select("本机软件",choices.Select(a=>a.Name+" · "+a.Version+" · "+(a.ExecutablePath.Length>0?a.ExecutablePath:a.Location.Length>0?a.Location:a.Id)),index);
        var repo=f.Text("GitHub 仓库地址或 owner/repo",binding.Repository);var executable=f.Text("主程序 EXE 路径",binding.ExecutablePath);var beta=f.Check("包含测试版",binding.IncludePrerelease);var prefix=f.Text("版本标签前缀",binding.TagPrefix);var pattern=f.Text("安装包文件名包含",binding.AssetPattern);f.Label("保留当前停止检查与暂停设置。更换软件、仓库或版本标签前缀时，清除旧匹配的忽略版本记录。修改匹配不会发起全机扫描。");
        software.SelectionChanged+=(_,_)=>{if(software.SelectedIndex>=0)executable.Text=choices[software.SelectedIndex].ExecutablePath;};
        f.Action("选择主程序",()=>{var picker=new OpenFileDialog{Filter="应用程序|*.exe"};if(picker.ShowDialog(f)==true)executable.Text=picker.FileName;return Task.CompletedTask;});
        f.Action("保存匹配",()=>{var path=executable.Text.Trim();if(path.Length>0&&(!File.Exists(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))throw new IOException("请选择存在的主程序 EXE。");if(software.SelectedIndex<0)throw new IOException("请选择本机软件。");BindingManagement.Update(settings,binding,choices[software.SelectedIndex],repo.Text,path.Length>0?Path.GetFullPath(path):"",beta.IsChecked==true,prefix.Text,pattern.Text,installed);Save();PlanUpdates();Display();model.Status="已保存软件匹配；点击“检查更新”获取对应仓库的新版本。";f.Close();return Task.CompletedTask;},true);f.Action("取消",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}
