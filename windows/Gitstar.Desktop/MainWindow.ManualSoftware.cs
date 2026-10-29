using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void BindingDialog(string? previewPath=null)
    {
        var account=CacheKey;var known=installed.OrderBy(app=>app.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        var f=new Form(this,"添加与关联软件",790,710);f.Label("选择主程序或软件文件夹，再填写 GitHub 仓库即可保存。名称、版本和目录自动读取；多个程序可从列表选择，保存不等待联网扫描。");
        using var lifetime=new CancellationTokenSource();var closed=false;f.Closed+=(_,_)=>{closed=true;lifetime.Cancel();};
        var mode=f.Select("添加方式",["选择程序（EXE / 快捷方式 / 文件夹）","从本机检测结果中选择"],known.Length>0?1:0);
        var search=f.Text("搜索本机软件（名称或路径）");search.Visibility=Visibility.Collapsed;var searchLabel=(FrameworkElement)f.Fields.Children[f.Fields.Children.IndexOf(search)-1];searchLabel.Visibility=Visibility.Collapsed;
        var existing=f.Select("已安装的软件",known.Select(app=>$"{app.Name} · {app.Version}"),-1);var existingLabel=(FrameworkElement)f.Fields.Children[f.Fields.Children.IndexOf(existing)-1];
        var executable=f.Text("软件位置（主程序 EXE 或所在文件夹）");var index=f.Fields.Children.IndexOf(executable);f.Fields.Children.Remove(executable);
        var pathRow=new Grid();pathRow.ColumnDefinitions.Add(new());pathRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pathRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});executable.Margin=new Thickness(0,0,0,10);pathRow.Children.Add(executable);f.Fields.Children.Insert(index,pathRow);
        var programs=f.Select("检测到多个程序，请选择主程序",[],-1);var programsLabel=(FrameworkElement)f.Fields.Children[f.Fields.Children.IndexOf(programs)-1];programs.Visibility=programsLabel.Visibility=Visibility.Collapsed;string[] candidates=[];var finding=false;
        var summary=new TextBlock{Text="选择主程序后，自动识别软件信息。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14),FontSize=12};summary.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(summary);
        var repo=f.Select("GitHub 仓库（可粘贴完整网址或 owner/repo）",repositories.Select(r=>r.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase),-1);repo.IsEditable=true;repo.IsTextSearchEnabled=true;
        var updateMode=f.Select("更新方式",["便携软件：下载后替换程序文件","安装版软件：下载后运行安装器"]);
        var advanced=f.Advanced("软件信息与可选设置");TextBox name=null!,version=null!,location=null!,prefix=null!,pattern=null!;CheckBox beta=null!;
        f.Into(advanced,()=>{name=f.Text("软件名称（自动读取，可修改）");version=f.Text("当前版本（自动读取，识别不到可留空）");location=f.Text("软件目录（自动读取）");location.IsReadOnly=true;prefix=f.Text("版本标签前缀（多产品仓库可选）");pattern=f.Text("安装包文件名包含（可选）");beta=f.Check("包含测试版");});
        InstalledApp? detected=null;var filling=false;var repoEdited=false;
        AddRepositorySearch(f,repo,()=>repo.Text,value=>repo.Text=value,()=>detected?.Name??search.Text);
        repo.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>{if(!filling)repoEdited=true;}));
        void Apply(InstalledApp app){detected=app;filling=true;try{
            executable.Text=app.ExecutablePath;executable.ToolTip=app.ExecutablePath;name.Text=app.Name;version.Text=app.Version=="未知"?"":app.Version;location.Text=app.Location;
            var binding=settings.Bindings.FirstOrDefault(item=>InstalledVersion.Find([app],item.AppId)!=null);
            if(binding!=null||!repoEdited){repo.Text=binding?.Repository??AppMatcher.Suggest(app,repositories).FirstOrDefault(match=>match.Reliable)?.Repository??"";repoEdited=false;}
            prefix.Text=binding?.TagPrefix??"";pattern.Text=binding?.AssetPattern??"";beta.IsChecked=binding?.IncludePrerelease??false;updateMode.SelectedIndex=app.Portable?0:1;
            summary.Text=app.Name+" · "+app.Version+"\n"+app.Location+(app.Version=="未知"?"\n未读取到版本，可先保存关联；在可选设置中补充当前版本后即可比较更新。":"");
        }finally{filling=false;}}
        void ApplyProgram(string path){var app=WindowsServices.DescribePortable(path);var old=known.FirstOrDefault(item=>item.ExecutablePath.Equals(app.ExecutablePath,StringComparison.OrdinalIgnoreCase));if(old!=null)app=app with{Id=old.Id,Name=old.Name,Portable=old.Portable,AliasIds=old.AliasIds};Apply(app);}
        void ReadProgram(){try{var input=executable.Text;candidates=WindowsServices.PortableExecutables(input);finding=true;programs.ItemsSource=candidates.Select(path=>Path.GetFileName(path)+"  ·  "+Path.GetDirectoryName(path)).ToArray();programs.SelectedIndex=candidates.Length==1?0:-1;finding=false;programs.Visibility=programsLabel.Visibility=candidates.Length>1?Visibility.Visible:Visibility.Collapsed;
            if(candidates.Length==1)ApplyProgram(candidates[0]);else{detected=null;summary.Text=$"找到 {candidates.Length} 个程序，请从列表选择主程序。";}
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){finding=false;detected=null;summary.Text=ex.Message;}}
        programs.SelectionChanged+=(_,_)=>{if(!finding&&programs.SelectedIndex>=0){try{ApplyProgram(candidates[programs.SelectedIndex]);}catch(Exception ex){summary.Text=ex.Message;detected=null;}}};
        var pick=f.Action("选择主程序",()=>{var dialog=new OpenFileDialog{Title="选择软件主程序或快捷方式",Filter="程序或快捷方式|*.exe;*.lnk",DereferenceLinks=true};if(dialog.ShowDialog(f)==true){executable.Text=dialog.FileName;ReadProgram();}return Task.CompletedTask;},true);f.Buttons.Children.Remove(pick);pick.Margin=new Thickness(10,0,0,10);Grid.SetColumn(pick,1);pathRow.Children.Add(pick);
        var folder=f.Action("选择文件夹",()=>{var dialog=new OpenFolderDialog{Title="选择软件所在文件夹"};if(dialog.ShowDialog(f)==true){executable.Text=dialog.FolderName;ReadProgram();}return Task.CompletedTask;});f.Buttons.Children.Remove(folder);folder.Margin=new Thickness(8,0,0,10);Grid.SetColumn(folder,2);pathRow.Children.Add(folder);
        void Mode(){search.Visibility=searchLabel.Visibility=existing.Visibility=existingLabel.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
            name.IsReadOnly=version.IsReadOnly=mode.SelectedIndex==1&&existing.SelectedIndex>=0&&!known[existing.SelectedIndex].Portable;
            if(mode.SelectedIndex==1){programs.Visibility=programsLabel.Visibility=Visibility.Collapsed;if(existing.SelectedIndex>=0)Apply(known[existing.SelectedIndex]);else summary.Text=known.Length>0?"输入名称或路径搜索，然后选择软件；也可直接选择主程序。":"没有软件缓存，可选择 EXE 添加，或点击重新检测。";}
            else if(executable.Text.Length>0)ReadProgram();else {detected=null;summary.Text="选择主程序后，自动识别软件信息。";}
        }
        mode.SelectionChanged+=(_,_)=>Mode();
        executable.LostKeyboardFocus+=(_,_)=>{if(!filling&&executable.Text.Length>0&&executable.Text!=detected?.ExecutablePath)ReadProgram();};Mode();
        var inventory=known;var refreshing=false;
        void FilterKnown(){var id=detected?.Id;refreshing=true;known=inventory.Select(app=>(App:app,Score:SearchText.Score(app.Name+" "+app.ExecutablePath+" "+app.Location,search.Text))).Where(row=>row.Score>0).OrderByDescending(row=>row.Score).ThenBy(row=>row.App.Name,StringComparer.OrdinalIgnoreCase).Select(row=>row.App).ToArray();existing.ItemsSource=known.Select(app=>$"{app.Name} · {app.Version} · {app.ExecutablePath}").ToArray();existing.SelectedIndex=Array.FindIndex(known,app=>app.Id==id);refreshing=false;}
        search.TextChanged+=(_,_)=>FilterKnown();existing.SelectionChanged+=(_,_)=>{if(!refreshing&&mode.SelectedIndex==1&&existing.SelectedIndex>=0)Apply(known[existing.SelectedIndex]);};
        f.Action("重新检测",async()=>{var portable=settings.PortableApps.ToArray();var bindings=settings.Bindings.Select(b=>b.Snapshot()).ToArray();summary.Text="正在检测安装登记、用户程序目录与开始菜单…";var prior=installed.ToArray();var token=lifetime.Token;var scanned=await Task.Run(()=>WindowsServices.Scan(portable,bindings,prior,token),token);if(closed)return;if(account!=CacheKey)throw new IOException("账号已改变，请重新打开添加窗口。");SetInstalled(scanned);inventory=scanned.OrderBy(app=>app.Name,StringComparer.OrdinalIgnoreCase).ToArray();FilterKnown();summary.Text=$"已检测 {inventory.Length} 个软件，已填写内容保留。切换为本机检测结果后，可搜索名称或路径。";});
        var save=f.Action("保存关联",()=>{
            if(account!=CacheKey)throw new IOException("账号已改变，请重新打开添加窗口。");if(executable.Text.Trim().Length>0)executable.Text=WindowsServices.SoftwareInputPath(executable.Text);InstalledApp selected;
            if(executable.Text.Trim().Length>0){
                if(Directory.Exists(executable.Text)){ReadProgram();if(detected==null)throw new IOException("请从检测结果中选择软件主程序。");}
                selected=WindowsServices.DescribePortable(executable.Text,name.Text,version.Text==detected?.Version?"":version.Text);
                var previous=installed.FirstOrDefault(app=>app.ExecutablePath.Equals(selected.ExecutablePath,StringComparison.OrdinalIgnoreCase));if(previous!=null)selected=selected with{Id=previous.Id,AliasIds=previous.AliasIds,Portable=previous.Portable};
            }else if(mode.SelectedIndex==1&&existing.SelectedIndex>=0)selected=known[existing.SelectedIndex];else throw new IOException("请选择本机软件或软件主程序。");
            var app=ManualSoftware.Save(settings,selected,installed,repo.Text,beta.IsChecked==true,prefix.Text,pattern.Text,portableOverride:updateMode.SelectedIndex==0);SetInstalled(installed.Where(item=>item.Id!=app.Id).Append(app).ToList());Save();UseFeature(UsageFeature.SoftwareBinding);PlanUpdates();Display();model.Status="已保存 "+app.Name+" 的关联；点击“检查更新”获取 GitHub 最新版本。";f.Close();return Task.CompletedTask;
        },true);
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;});
        if(previewPath!=null){var sample=Path.Combine(store.Root,"ExampleEditor.exe");File.Copy(Environment.ProcessPath!,sample,true);executable.Text=store.Root;ReadProgram();var folderFound=detected?.ExecutablePath==sample;var second=Path.Combine(store.Root,"OtherEditor.exe");File.Copy(sample,second,true);executable.Text=store.Root;ReadProgram();var requiresChoice=programs.Visibility==Visibility.Visible&&programs.SelectedIndex==-1;programs.SelectedIndex=Array.IndexOf(candidates,sample);repo.Text="example/editor";if(detected==null||!folderFound||!requiresChoice)throw new IOException("Manual software preview could not identify program or folder choices");File.WriteAllText(Path.ChangeExtension(previewPath,".manual-software.json"),JsonSerializer.Serialize(new{AutoName=name.Text.Length>0,AutoVersion=version.Text.Length>0,AutoFolder=location.Text.Length>0,OnlyExeAndRepositoryRequired=true,FolderFindsProgram=folderFound,MultipleProgramsRequireSelection=requiresChoice,AdvancedCollapsed=!((Expander)advanced.Parent).IsExpanded}));f.RenderPreview(previewPath);}
        f.ShowDialog();
    }
}
