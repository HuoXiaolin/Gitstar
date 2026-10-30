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
        f.Label("按实际安装位置管理软件、仓库匹配和检查状态。支持搜索、多选与批量操作。");
        f.Footnote("管理操作即时保存；解除关联不删除软件文件。");
        var filters=new Grid();filters.ColumnDefinitions.Add(new());filters.ColumnDefinitions.Add(new(){Width=new GridLength(190)});var searchPanel=new StackPanel{Margin=new Thickness(0,0,12,0)};var statePanel=new StackPanel();filters.Children.Add(searchPanel);Grid.SetColumn(statePanel,1);filters.Children.Add(statePanel);f.Fields.Children.Add(filters);
        TextBox search=null!;ComboBox filter=null!;f.Into(searchPanel,()=>search=f.Text("搜索软件或仓库",focus?.Repository??""));f.Into(statePanel,()=>filter=f.Select("检查状态",["全部软件","正常检查","已停止","已暂停","已忽略版本","版本待核对"]));
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
            visible=settings.Bindings.Where(b=>((InstalledVersion.Find(installed,b.AppId)?.Name??"")+" "+b.Repository+" "+b.AppId).Contains(query,StringComparison.OrdinalIgnoreCase)).Where(b=>filter.SelectedIndex switch{1=>!ReminderRules.Restricted(b,now),2=>b.Disabled,3=>b.PauseUntil>now,4=>b.IgnoredTag.Length>0,5=>InstalledVersion.Find(installed,b.AppId) is not {} app||app.VersionNeedsReview,_=>true}).OrderBy(b=>InstalledVersion.Find(installed,b.AppId)?.Name??b.Repository,StringComparer.OrdinalIgnoreCase).ToArray();
            selected.IntersectWith(visible);list.Children.Clear();
            if(visible.Length==0)list.Children.Add(ProjectText(settings.Bindings.Count==0?"尚未关联软件，可点击“添加软件”。":"没有符合条件的软件，请调整搜索或筛选。",13,true));
            foreach(var binding in visible){
                var app=InstalledVersion.Find(installed,binding.AppId);var row=new Grid{Margin=new Thickness(0,11,0,11)};row.ColumnDefinitions.Add(new(){Width=new GridLength(28)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(135)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var check=new CheckBox{IsChecked=selected.Contains(binding),VerticalAlignment=VerticalAlignment.Center};System.Windows.Automation.AutomationProperties.SetName(check,"选择 "+(app?.Name??binding.Repository));check.Checked+=(_,_)=>{selected.Add(binding);SelectionState();};check.Unchecked+=(_,_)=>{selected.Remove(binding);SelectionState();};row.Children.Add(check);
                var info=new StackPanel{Margin=new Thickness(0,0,14,0)};var name=app?.Name??(binding.AppName.Length>0?binding.AppName:binding.Repository.Split('/').Last());var title=ProjectText(name+(app!=null?"  ·  "+app.Version:""),14);title.Margin=new Thickness(0,0,0,3);info.Children.Add(title);
                var source=app==null?(binding.ExecutablePath.Length>0?binding.ExecutablePath:binding.AppLocation.Length>0?binding.AppLocation:binding.AppId):app.ExecutablePath.Length>0?app.ExecutablePath:app.Location.Length>0?app.Location:app.Id;
                var address=ProjectText(binding.Repository+"   ·   "+source,12,true);address.Margin=new Thickness(0);address.TextWrapping=TextWrapping.NoWrap;address.TextTrimming=TextTrimming.CharacterEllipsis;address.ToolTip=source+"\n版本来源："+(app?.VersionSource??"待核对")+"\n"+(app?.VersionDetails??"");info.Children.Add(address);Grid.SetColumn(info,1);row.Children.Add(info);
                var state=ProjectText(app?.VersionNeedsReview==true?"版本待核对":app==null?"安装位置待核对":ReminderRules.Status(binding,now),12,true);state.VerticalAlignment=VerticalAlignment.Center;state.Margin=new Thickness(0,0,12,0);Grid.SetColumn(state,2);row.Children.Add(state);
                var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,3);row.Children.Add(actions);var edit=new Button{Content="编辑",Padding=new Thickness(10,6,10,6),ToolTip="修改软件匹配、路径与版本来源"};edit.Click+=(_,_)=>{EditSoftwareBinding(f,binding);Render();};actions.Children.Add(edit);
                var menu=new ContextMenu();var more=new Button{Content="更多",Padding=new Thickness(10,6,10,6),Margin=new Thickness(0),ContextMenu=menu,ToolTip="忽略、暂停、恢复或解除关联"};more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};actions.Children.Add(more);
                void Inline(string text,BindingAction operation,bool enabled=true){var item=new MenuItem{Header=text,IsEnabled=enabled};if(operation==BindingAction.Unlink)item.SetResourceReference(Control.ForegroundProperty,"DangerInk");item.Click+=(_,_)=>Apply([binding],operation);menu.Items.Add(item);}
                Inline("忽略当前版本",BindingAction.IgnoreVersion,Rules.SelectRelease(releases.Where(r=>r.Repository.Equals(binding.Repository,StringComparison.OrdinalIgnoreCase)),binding)!=null);Inline("暂停 7 天",BindingAction.PauseWeek);if(ReminderRules.Restricted(binding,now))Inline("恢复检查",BindingAction.Restore);if(!binding.Disabled)Inline("停止检查",BindingAction.Stop);Inline("解除关联",BindingAction.Unlink);
                var card=new Border{Child=row,BorderThickness=new Thickness(0,0,0,1)};card.SetResourceReference(Border.BorderBrushProperty,"Edge");list.Children.Add(card);
            }
            SelectionState();
        }
        apply=f.Action("应用到所选",()=>{if(action.SelectedIndex>0)Apply(visible.Where(selected.Contains).ToArray(),(BindingAction)(action.SelectedIndex-1));return Task.CompletedTask;});
        f.HeaderAction(f.Action("核对软件",()=>{SoftwareAuditDialog(f);selected.Clear();Render();return Task.CompletedTask;}));f.HeaderAction(f.Action("添加软件",()=>{BindingDialog();Render();return Task.CompletedTask;},true));f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});
        selectAll.Click+=(_,_)=>{if(selecting)return;if(selectAll.IsChecked==true)selected.UnionWith(visible);else selected.ExceptWith(visible);Render();};
        search.TextChanged+=(_,_)=>{selected.Clear();Render();};filter.SelectionChanged+=(_,_)=>{selected.Clear();Render();};action.SelectionChanged+=(_,_)=>SelectionState();Render();
        if(previewPath!=null){if(Environment.GetCommandLineArgs().Contains("--software-multi")){selectAll.IsChecked=true;selectAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));action.SelectedIndex=3;SelectionState();var before=visible.Length;apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var stopped=visible.All(b=>b.Disabled);action.SelectedIndex=4;apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var restored=visible.All(b=>!ReminderRules.Restricted(b,DateTimeOffset.UtcNow));filter.SelectedIndex=2;var selectionCleared=selected.Count==0&&apply.IsEnabled==false;filter.SelectedIndex=0;selected.UnionWith(visible);Render();File.WriteAllText(Path.ChangeExtension(previewPath,".software-multi.json"),JsonSerializer.Serialize(new{DefaultShowsAll=before==settings.Bindings.Count,SelectAllAndBatchStop=stopped,BatchRestore=restored,FilterClearsHiddenSelection=selectionCleared}));if(!stopped||!restored||!selectionCleared)throw new IOException("Software multiselect regression");}f.RenderPreview(previewPath);}f.ShowDialog();
    }
    private void EditSoftwareBinding(Window owner,Binding binding,string? previewPath=null)
    {
        var f=new Form(owner,"修改软件匹配",760,720);var known=InstalledVersion.Find(installed,binding.AppId);
        var choices=installed.GroupBy(a=>a.Id).Select(g=>g.First()).OrderBy(a=>a.Name,StringComparer.OrdinalIgnoreCase).ToList();if(known==null){known=new(binding.AppId,binding.Repository.Split('/').Last(),"待检测","");choices.Insert(0,known);}var index=choices.FindIndex(a=>a.Id==known.Id);
        var software=f.Select("本机软件",choices.Select(a=>a.Name+" · "+a.Version+" · "+(a.ExecutablePath.Length>0?a.ExecutablePath:a.Location.Length>0?a.Location:a.Id)),index);
        var updateMode=f.Select("更新方式",["便携软件：下载后替换程序文件","安装版软件：下载后运行安装器"],known.Portable?0:1);
        var repo=f.Text("GitHub 仓库地址或 owner/repo",binding.Repository);var executable=f.Text("主程序 EXE 路径",binding.ExecutablePath);var beta=f.Check("包含测试版",binding.IncludePrerelease);var advanced=f.Advanced("版本来源与高级匹配");TextBox prefix=null!,pattern=null!,confirmed=null!;f.Into(advanced,()=>{prefix=f.Text("版本标签前缀",binding.TagPrefix);pattern=f.Text("安装包文件名包含",binding.AssetPattern);confirmed=f.Text("应用关于页版本（可选，文件属性不准确时填写）",binding.ConfirmedVersion);f.Label("保留停止检查与暂停设置；更换软件、仓库或标签前缀后清除旧忽略版本。优先读取程序版本，手动确认绑定当前 EXE 的 SHA-256，文件或位置改变后失效。");});
        AddRepositorySearch(f,repo,()=>repo.Text,value=>repo.Text=value,()=>known.Name);
        (string Version,string Path,string Hash)? confirmation=null;
        executable.TextChanged+=(_,_)=>{confirmed.Text="";confirmation=null;};
        confirmed.TextChanged+=(_,_)=>confirmation=null;
        software.SelectionChanged+=(_,_)=>{if(software.SelectedIndex>=0){executable.Text=choices[software.SelectedIndex].ExecutablePath;updateMode.SelectedIndex=choices[software.SelectedIndex].Portable?0:1;}};
        f.FieldAction(executable,f.Action("选择主程序",()=>{var picker=new OpenFileDialog{Filter="应用程序|*.exe"};if(picker.ShowDialog(f)==true)executable.Text=picker.FileName;return Task.CompletedTask;}));
        f.Action("选择运行中的程序",()=>{
            var running=WindowsServices.RelatedRunningPrograms([binding.Snapshot()]);if(running.Count==0)throw new IOException("未找到相关的运行中程序，请先打开该软件，或直接选择主程序文件。");
            var picker=new Form(f,"核对实际运行位置",780,480);var choice=picker.Select("运行中的相关软件",running.Select(program=>program.Path+" · "+InstalledVersion.NormalizeVersion(program.ProductVersion)));
            picker.Label("选择应用关于页对应的实际进程。多份安装分别保留，不会用另一份程序的版本覆盖当前安装记录。");
            picker.Action("使用此程序",()=>{if(choice.SelectedIndex<0)throw new IOException("请选择程序。");executable.Text=running[choice.SelectedIndex].Path;picker.Close();return Task.CompletedTask;},true);picker.Action("取消",()=>{picker.Close();return Task.CompletedTask;});picker.ShowDialog();return Task.CompletedTask;
        });
        var confirmVersion=f.Action("重新确认应用版本",()=>{confirmation=WindowsServices.ConfirmInstalledVersion(confirmed.Text,executable.Text.Trim());if(confirmation.Value.Version.Length==0)throw new IOException("请先填写应用关于页显示的版本号。");model.Status="已核对当前主程序文件，保存匹配后生效。";return Task.CompletedTask;});f.Buttons.Children.Remove(confirmVersion);advanced.Children.Add(confirmVersion);
        f.Action("保存匹配",async()=>{
            var path=executable.Text.Trim();if(path.Length>0)path=WindowsServices.SoftwareInputPath(path);if(path.Length>0&&(!File.Exists(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))throw new IOException("请选择存在的主程序 EXE。");if(software.SelectedIndex<0)throw new IOException("请选择本机软件。");if(updateMode.SelectedIndex==0&&path.Length==0)throw new IOException("便携替换需要指定主程序 EXE。");
            var unchanged=confirmed.Text.Trim()==binding.ConfirmedVersion&&path.Equals(binding.ConfirmedVersionPath,StringComparison.OrdinalIgnoreCase);
            var evidence=confirmation??(unchanged?(Version:binding.ConfirmedVersion,Path:binding.ConfirmedVersionPath,Hash:binding.ConfirmedVersionHash):WindowsServices.ConfirmInstalledVersion(confirmed.Text,path));
            BindingManagement.Update(settings,binding,choices[software.SelectedIndex],repo.Text,path.Length>0?Path.GetFullPath(path):"",beta.IsChecked==true,prefix.Text,pattern.Text,installed);
            var selected=choices[software.SelectedIndex] with{ExecutablePath=path,Portable=updateMode.SelectedIndex==0};settings.PortableApps.RemoveAll(app=>app.Id.Equals(selected.Id,StringComparison.OrdinalIgnoreCase));if(selected.Portable)settings.PortableApps.Add(selected);
            binding.ConfirmedVersion=evidence.Version;binding.ConfirmedVersionPath=evidence.Path;binding.ConfirmedVersionHash=evidence.Hash;
            var prior=installed.Select(app=>app.Id.Equals(selected.Id,StringComparison.OrdinalIgnoreCase)?selected:app).ToArray();var snapshot=binding.Snapshot();var portable=settings.PortableApps.ToArray();
            var detected=await Task.Run(()=>WindowsServices.ScanMatched([snapshot],portable,prior));
            SetInstalled(prior.Where(app=>InstalledVersion.Find([app],binding.AppId)==null).Concat(detected).ToList());
            Save();UseFeature(UsageFeature.SoftwareBinding);PlanUpdates();Display();model.Status="已保存软件匹配与版本来源，并重新核对实际安装版本。";f.Close();
        },true);f.Action("取消",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
}
