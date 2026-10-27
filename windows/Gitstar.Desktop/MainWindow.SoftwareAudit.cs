using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void SoftwareAuditDialog(Window owner,string? previewPath=null)
    {
        var f=new Form(owner,"核对软件记录",960,740);var account=settings.Account;var closed=false;var working=false;using var lifetime=new CancellationTokenSource();
        f.Label("检查已关联软件的实际程序位置与当前安装登记，找出路径变化或疑似已卸载的记录。核对本身不修改记录、不检查 GitHub 更新，也不扫描整个磁盘。");
        f.Label("请选择修复或删除；多个候选需人工确认。删除只移除 Gitstar 的关联、便携软件登记及对应缓存，不卸载软件、不删除程序或安装包。");
        var summary=ProjectText("正在核对软件记录…",12,true);f.Fields.Children.Add(summary);var abnormal=f.Check("仅显示需要处理的记录",true);var all=f.Check("全选当前结果");
        var list=new StackPanel();f.Fields.Children.Add(list);var rows=new List<SoftwareAuditEntry>();var originals=new Dictionary<Binding,Binding>();var selected=new HashSet<Binding>();var targets=new Dictionary<Binding,InstalledApp>();var commands=new List<Button>();
        bool Current()=>!closed&&account==settings.Account;
        void Ensure(SoftwareAuditEntry row){if(!Current()||!originals.TryGetValue(row.Binding,out var actual)||!settings.Bindings.Contains(actual)||JsonSerializer.Serialize(actual)!=JsonSerializer.Serialize(row.Binding))throw new IOException("账号或软件记录已变化，请重新核对。");}
        IEnumerable<SoftwareAuditEntry> Visible()=>rows.Where(row=>!abnormal.IsChecked.GetValueOrDefault()||row.State!=SoftwareAuditState.Healthy);
        Button Inline(Panel panel,string text,Func<Task> action){var button=f.Action(text,action);f.Buttons.Children.Remove(button);button.Margin=new Thickness(0,6,8,0);panel.Children.Add(button);commands.Add(button);return button;}
        void TrimCache(Binding old){var ids=old.AliasIds.Append(old.AppId).ToHashSet(StringComparer.OrdinalIgnoreCase);installed=installed.Where(app=>!ids.Contains(app.Id)||settings.Bindings.Any(binding=>binding.AppId.Equals(app.Id,StringComparison.OrdinalIgnoreCase)||(app.AliasIds??[]).Contains(binding.AppId,StringComparer.OrdinalIgnoreCase))).ToList();}
        async Task Repair(SoftwareAuditEntry[] chosen){
            if(working||chosen.Length==0)return;if(busy||batchPublishing)throw new IOException("请先等待当前任务完成，再修复软件记录。");var count=0;var skipped=0;
            var staged=new Settings{Bindings=settings.Bindings.Select(binding=>binding.Snapshot()).ToList(),PortableApps=settings.PortableApps.ToList()};var changes=new List<(SoftwareAuditEntry Row,Binding Value,InstalledApp Target)>();
            foreach(var row in chosen){Ensure(row);if(!targets.TryGetValue(row.Binding,out var target)){skipped++;continue;}var index=settings.Bindings.IndexOf(originals[row.Binding]);var copy=staged.Bindings[index];SoftwareRecordAudit.Repair(staged,copy,target,WindowsServices.AuditPath);changes.Add((row,copy,target));}
            foreach(var change in changes){Ensure(change.Row);var actual=originals[change.Row.Binding];var copy=change.Value;actual.AppId=copy.AppId;actual.AppName=copy.AppName;actual.AppLocation=copy.AppLocation;actual.AliasIds=copy.AliasIds;actual.ExecutablePath=copy.ExecutablePath;actual.ConfirmedVersion=copy.ConfirmedVersion;actual.ConfirmedVersionPath=copy.ConfirmedVersionPath;actual.ConfirmedVersionHash=copy.ConfirmedVersionHash;TrimCache(change.Row.Binding);installed=InstalledIdentity.Merge(installed.Where(app=>!app.Id.Equals(change.Target.Id,StringComparison.OrdinalIgnoreCase)&&!app.ExecutablePath.Equals(change.Target.ExecutablePath,StringComparison.OrdinalIgnoreCase)).Append(change.Target));count++;}
            settings.PortableApps=staged.PortableApps;
            installedFresh=true;Save();PlanUpdates();Display();model.Status=$"已修复 {count} 项软件记录"+(skipped>0?$"，{skipped} 项需选择候选或手动指定主程序。":"。");await Scan();summary.Text=model.Status;
        }
        async Task Remove(SoftwareAuditEntry[] chosen){
            if(working||chosen.Length==0)return;if(busy||batchPublishing)throw new IOException("请先等待当前任务完成，再删除软件记录。");foreach(var row in chosen)Ensure(row);
            if(DialogMessage.Show(f,$"删除 {chosen.Length} 项 Gitstar 软件记录？\n\n"+string.Join("\n",chosen.Take(8).Select(row=>row.Name+" · "+row.Binding.Repository))+"\n\n仅移除关联与过期登记，保留程序、配置、仓库关注和已下载安装包。","删除软件记录",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            foreach(var row in chosen)Ensure(row);
            foreach(var row in chosen){SoftwareRecordAudit.Remove(settings,originals[row.Binding]);TrimCache(row.Binding);}installedFresh=true;Save();PlanUpdates();Display();model.Status=$"已删除 {chosen.Length} 项软件记录，未删除软件文件。";await Scan();summary.Text=model.Status;
        }
        void Render(){
            list.Children.Clear();commands.Clear();var visible=Visible().ToArray();selected.IntersectWith(visible.Select(row=>row.Binding));
            foreach(var row in visible){
                var panel=new StackPanel();var check=new CheckBox{Content=row.Name+" · "+row.Status,IsChecked=selected.Contains(row.Binding),Margin=new Thickness(0,0,0,7)};check.Checked+=(_,_)=>selected.Add(row.Binding);check.Unchecked+=(_,_)=>selected.Remove(row.Binding);panel.Children.Add(check);
                panel.Children.Add(ProjectText(row.Binding.Repository+"\n原程序："+(row.OriginalPath.Length>0?row.OriginalPath:row.Binding.AppLocation.Length>0?row.Binding.AppLocation:"未记录路径")+"\n"+row.Reason,12,true));
                if(row.Candidates.Count>0){var choice=new ComboBox{ItemsSource=row.Candidates.Select(app=>app.Name+" · "+app.Version+" · "+app.ExecutablePath).ToArray(),SelectedIndex=targets.TryGetValue(row.Binding,out var target)?row.Candidates.FindIndex(app=>app.ExecutablePath.Equals(target.ExecutablePath,StringComparison.OrdinalIgnoreCase)):-1,Margin=new Thickness(0,8,0,0)};choice.SelectionChanged+=(_,_)=>{if(choice.SelectedIndex>=0)targets[row.Binding]=row.Candidates[choice.SelectedIndex];else targets.Remove(row.Binding);};panel.Children.Add(choice);}
                var buttons=new WrapPanel();panel.Children.Add(buttons);
                if(row.State!=SoftwareAuditState.Healthy){
                    Inline(buttons,"修复记录",()=>Repair([row]));
                    Inline(buttons,"选择主程序",()=>{Ensure(row);var picker=new OpenFileDialog{Filter="应用程序|*.exe",Title="选择此软件实际使用的主程序"};if(picker.ShowDialog(f)==true){var app=WindowsServices.DescribePortable(picker.FileName,row.Name);row.Candidates.RemoveAll(item=>item.ExecutablePath.Equals(app.ExecutablePath,StringComparison.OrdinalIgnoreCase));row.Candidates.Insert(0,app);targets[row.Binding]=app;Render();}return Task.CompletedTask;});
                    Inline(buttons,"删除记录",()=>Remove([row]));
                }
                var card=new Border{Child=panel,Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1)};card.SetResourceReference(BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Edge");list.Children.Add(card);
            }
            if(visible.Length==0)list.Children.Add(ProjectText(rows.Count==0?"没有需要核对的软件关联。":"没有需要处理的异常记录。",13,true));
        }
        async Task Scan(){
            if(working||!Current())return;working=true;selected.Clear();all.IsChecked=false;summary.Text="正在读取安装登记并核对已关联路径…";
            foreach(var command in commands)command.IsEnabled=false;all.IsEnabled=abnormal.IsEnabled=false;
            try{
                var bindings=settings.Bindings.ToArray();var snapshots=bindings.Select(binding=>binding.Snapshot()).ToArray();var previous=installed.ToArray();var portable=settings.PortableApps.ToArray();
                var scan=await Task.Run(()=>WindowsServices.AuditSoftware(snapshots,portable,lifetime.Token,previous),lifetime.Token);var reviewed=await Task.Run(()=>SoftwareRecordAudit.Review(snapshots,previous,scan.Apps,WindowsServices.AuditPath,scan.Warnings.Count==0),lifetime.Token);if(!Current())return;
                originals=snapshots.Select((snapshot,index)=>(snapshot,original:bindings[index])).ToDictionary(pair=>pair.snapshot,pair=>pair.original);rows=reviewed;targets.Clear();foreach(var row in rows.Where(row=>row.Candidates.Count==1))targets[row.Binding]=row.Candidates[0];Render();
                summary.Text=$"核对 {rows.Count} 项 · 正常 {rows.Count(row=>row.State==SoftwareAuditState.Healthy)} · 可修复 {rows.Count(row=>row.State==SoftwareAuditState.Repairable)} · 疑似卸载 {rows.Count(row=>row.State==SoftwareAuditState.Missing)} · 待确认 {rows.Count(row=>row.State==SoftwareAuditState.Uncertain)}"+(scan.Warnings.Count>0?"\n部分登记无法读取，未直接判为已卸载。":"");
            }catch(OperationCanceledException){}catch(Exception error){if(Current())summary.Text="核对未完成："+error.Message;}
            finally{working=false;if(!closed){foreach(var command in commands)command.IsEnabled=true;all.IsEnabled=abnormal.IsEnabled=true;}}
        }
        f.Action("重新核对",Scan);f.Action("修复所选",()=>Repair(Visible().Where(row=>selected.Contains(row.Binding)).ToArray()));f.Action("删除所选记录",()=>Remove(Visible().Where(row=>selected.Contains(row.Binding)).ToArray()));f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});
        abnormal.Click+=(_,_)=>{selected.Clear();all.IsChecked=false;Render();};all.Click+=(_,_)=>{if(all.IsChecked==true)selected.UnionWith(Visible().Select(row=>row.Binding));else selected.Clear();Render();};f.Closed+=(_,_)=>{closed=true;lifetime.Cancel();};
        if(previewPath==null)f.Loaded+=async(_,_)=>await Scan();else{
            var a=new Binding{AppId="example-old",AppName="示例编辑器",Repository="example/editor",ExecutablePath=@"D:\Apps\OldEditor\Editor.exe"};var b=new Binding{AppId="example-gone",AppName="示例工具",Repository="example/tool",ExecutablePath=@"D:\Apps\Tool\Tool.exe"};
            rows=[new(a,a.AppName,a.ExecutablePath,SoftwareAuditState.Repairable,"找到新的安装位置，请核对后选择修复。",[new("example-new",a.AppName,"2.0.0",@"D:\Apps\Editor",ExecutablePath:@"D:\Apps\Editor\Editor.exe")]),new(b,b.AppName,b.ExecutablePath,SoftwareAuditState.Missing,"原程序与对应安装登记均未找到，可修复位置或删除记录。",[])];targets[a]=rows[0].Candidates[0];Render();summary.Text="核对 2 项 · 可修复 1 · 疑似卸载 1";f.RenderPreview(previewPath);
        }
        f.ShowDialog();
    }
}
