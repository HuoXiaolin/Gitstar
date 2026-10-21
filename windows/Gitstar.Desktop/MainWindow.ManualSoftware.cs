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
        var mode=f.Select("添加方式",["添加便携软件（EXE / 文件夹）","关联已安装的软件"]);
        var existing=f.Select("已安装的软件",known.Select(app=>$"{app.Name} · {app.Version}"),known.Length>0?0:-1);var existingLabel=(FrameworkElement)f.Fields.Children[f.Fields.Children.IndexOf(existing)-1];
        var executable=f.Text("软件位置（主程序 EXE 或所在文件夹）");var index=f.Fields.Children.IndexOf(executable);f.Fields.Children.Remove(executable);
        var pathRow=new Grid();pathRow.ColumnDefinitions.Add(new());pathRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pathRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});executable.Margin=new Thickness(0,0,0,10);pathRow.Children.Add(executable);f.Fields.Children.Insert(index,pathRow);
        var programs=f.Select("检测到多个程序，请选择主程序",[],-1);var programsLabel=(FrameworkElement)f.Fields.Children[f.Fields.Children.IndexOf(programs)-1];programs.Visibility=programsLabel.Visibility=Visibility.Collapsed;string[] candidates=[];var finding=false;
        var summary=new TextBlock{Text="选择主程序后，自动识别软件信息。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14),FontSize=12};summary.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(summary);
        var repo=f.Select("GitHub 仓库（可粘贴完整网址或 owner/repo）",repositories.Select(r=>r.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase),-1);repo.IsEditable=true;repo.IsTextSearchEnabled=true;
        var advanced=f.Advanced("软件信息与可选设置");TextBox name=null!,version=null!,location=null!,prefix=null!,pattern=null!;CheckBox beta=null!;
        f.Into(advanced,()=>{name=f.Text("软件名称（自动读取，可修改）");version=f.Text("当前版本（自动读取，识别不到可留空）");location=f.Text("软件目录（自动读取）");location.IsReadOnly=true;prefix=f.Text("版本标签前缀（多产品仓库可选）");pattern=f.Text("安装包文件名包含（可选）");beta=f.Check("包含测试版");});
        InstalledApp? detected=null;var filling=false;var repoEdited=false;
        repo.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>{if(!filling)repoEdited=true;}));
        void Apply(InstalledApp app){detected=app;filling=true;try{
            executable.Text=app.ExecutablePath;executable.ToolTip=app.ExecutablePath;name.Text=app.Name;version.Text=app.Version=="未知"?"":app.Version;location.Text=app.Location;
            var binding=settings.Bindings.FirstOrDefault(item=>InstalledVersion.Find([app],item.AppId)!=null);
            if(binding!=null||!repoEdited){repo.Text=binding?.Repository??AppMatcher.Suggest(app,repositories).FirstOrDefault(match=>match.Reliable)?.Repository??"";repoEdited=false;}
            prefix.Text=binding?.TagPrefix??"";pattern.Text=binding?.AssetPattern??"";beta.IsChecked=binding?.IncludePrerelease??false;
            summary.Text=app.Name+" · "+app.Version+"\n"+app.Location+(app.Version=="未知"?"\n未读取到版本，可先保存关联；在可选设置中补充当前版本后即可比较更新。":"");
        }finally{filling=false;}}
        void ApplyProgram(string path){var app=WindowsServices.DescribePortable(path);if(mode.SelectedIndex==1&&existing.SelectedIndex>=0){var old=known[existing.SelectedIndex];app=app with{Id=old.Id,Name=old.Name,Portable=old.Portable,AliasIds=old.AliasIds};}Apply(app);}
        void ReadProgram(){try{var input=executable.Text;candidates=WindowsServices.PortableExecutables(input);finding=true;programs.ItemsSource=candidates.Select(path=>Path.GetFileName(path)+"  ·  "+Path.GetDirectoryName(path)).ToArray();programs.SelectedIndex=candidates.Length==1?0:-1;finding=false;programs.Visibility=programsLabel.Visibility=candidates.Length>1?Visibility.Visible:Visibility.Collapsed;
            if(candidates.Length==1)ApplyProgram(candidates[0]);else{detected=null;summary.Text=$"找到 {candidates.Length} 个程序，请从列表选择主程序。";}
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){finding=false;detected=null;summary.Text=ex.Message;}}
        programs.SelectionChanged+=(_,_)=>{if(!finding&&programs.SelectedIndex>=0){try{ApplyProgram(candidates[programs.SelectedIndex]);}catch(Exception ex){summary.Text=ex.Message;detected=null;}}};
        var pick=f.Action("选择主程序",()=>{var dialog=new OpenFileDialog{Title="选择软件主程序",Filter="应用程序|*.exe"};if(dialog.ShowDialog(f)==true){executable.Text=dialog.FileName;ReadProgram();}return Task.CompletedTask;},true);f.Buttons.Children.Remove(pick);pick.Margin=new Thickness(10,0,0,10);Grid.SetColumn(pick,1);pathRow.Children.Add(pick);
        var folder=f.Action("选择文件夹",()=>{var dialog=new OpenFolderDialog{Title="选择软件所在文件夹"};if(dialog.ShowDialog(f)==true){executable.Text=dialog.FolderName;ReadProgram();}return Task.CompletedTask;});f.Buttons.Children.Remove(folder);folder.Margin=new Thickness(8,0,0,10);Grid.SetColumn(folder,2);pathRow.Children.Add(folder);
        void Mode(){existing.Visibility=existingLabel.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
            name.IsReadOnly=version.IsReadOnly=mode.SelectedIndex==1&&existing.SelectedIndex>=0&&!known[existing.SelectedIndex].Portable;
            if(mode.SelectedIndex==1){programs.Visibility=programsLabel.Visibility=Visibility.Collapsed;if(existing.SelectedIndex>=0)Apply(known[existing.SelectedIndex]);else summary.Text="没有软件缓存，可选择 EXE 添加，或点击重新检测。";}
            else if(executable.Text.Length>0)ReadProgram();else {detected=null;summary.Text="选择主程序后，自动识别软件信息。";}
        }
        mode.SelectionChanged+=(_,_)=>Mode();existing.SelectionChanged+=(_,_)=>{if(mode.SelectedIndex==1)Mode();};
        executable.LostKeyboardFocus+=(_,_)=>{if(!filling&&executable.Text.Length>0&&executable.Text!=detected?.ExecutablePath)ReadProgram();};Mode();
        f.Action("重新检测",async()=>{var known=installed.ToArray();var scanned=await Task.Run(()=>WindowsServices.Scan(settings.PortableApps,settings.Bindings,known));if(account!=CacheKey)throw new IOException("账号已改变，请重新打开添加窗口。");SetInstalled(scanned);f.Close();BindingDialog();});
        var save=f.Action("保存关联",()=>{
            if(account!=CacheKey)throw new IOException("账号已改变，请重新打开添加窗口。");InstalledApp selected;
            if(mode.SelectedIndex==0){if(Directory.Exists(executable.Text)){ReadProgram();if(detected==null)throw new IOException("请从检测结果中选择软件主程序。");}selected=WindowsServices.DescribePortable(executable.Text,name.Text,version.Text==detected?.Version?"":version.Text);}
            else {if(existing.SelectedIndex<0)throw new IOException("请选择软件，或切换为选择 EXE。");if(Directory.Exists(executable.Text)){ReadProgram();if(detected==null)throw new IOException("请从检测结果中选择软件主程序。");}selected=detected?.Id==known[existing.SelectedIndex].Id?detected:known[existing.SelectedIndex];if(selected.Portable)selected=WindowsServices.DescribePortable(executable.Text,name.Text,version.Text==detected?.Version?"":version.Text) with{Id=selected.Id,AliasIds=selected.AliasIds};else if(executable.Text.Trim().Length>0){var path=WindowsServices.PortableExecutables(executable.Text).Single();selected=selected with{ExecutablePath=path};}}
            var app=ManualSoftware.Save(settings,selected,known,repo.Text,beta.IsChecked==true,prefix.Text,pattern.Text);SetInstalled(installed.Where(item=>item.Id!=app.Id).Append(app).ToList());Save();PlanUpdates();Display();model.Status="已保存 "+app.Name+" 的关联；点击“检查更新”获取 GitHub 最新版本。";f.Close();return Task.CompletedTask;
        },true);
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;});
        if(previewPath!=null){var sample=Path.Combine(store.Root,"ExampleEditor.exe");File.Copy(Environment.ProcessPath!,sample,true);executable.Text=store.Root;ReadProgram();var folderFound=detected?.ExecutablePath==sample;var second=Path.Combine(store.Root,"OtherEditor.exe");File.Copy(sample,second,true);executable.Text=store.Root;ReadProgram();var requiresChoice=programs.Visibility==Visibility.Visible&&programs.SelectedIndex==-1;programs.SelectedIndex=Array.IndexOf(candidates,sample);repo.Text="example/editor";if(detected==null||!folderFound||!requiresChoice)throw new IOException("Manual software preview could not identify program or folder choices");File.WriteAllText(Path.ChangeExtension(previewPath,".manual-software.json"),JsonSerializer.Serialize(new{AutoName=name.Text.Length>0,AutoVersion=version.Text.Length>0,AutoFolder=location.Text.Length>0,OnlyExeAndRepositoryRequired=true,FolderFindsProgram=folderFound,MultipleProgramsRequireSelection=requiresChoice,AdvancedCollapsed=!((Expander)advanced.Parent).IsExpanded}));f.RenderPreview(previewPath);}
        f.ShowDialog();
    }
}
