using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Func<string> ReleaseNotesEditor(Form form,ManagedProject? project,ProjectAutomation? config,string previous,bool preview)
    {
        form.Section("更新说明");
        var source=config?.SourceFolder??"";var detected=ReleaseNotes.FilePath(source,config?.NotesFile??"");
        var mode=form.Select("说明来源",["读取日志文件","手动输入"],config?.ManualNotes!=null||detected.Length==0?1:0);
        var file=form.Text("日志文件（默认源码目录的 CHANGELOG.md）",detected);
        var controls=new WrapPanel();form.Fields.Children.Add(controls);
        var text=form.Text("更新说明（Markdown）",config?.ManualNotes??previous,true);text.Height=150;
        var manual=text.Text;
        void Load(){text.IsReadOnly=mode.SelectedIndex==0;file.IsEnabled=text.IsReadOnly;
            if(text.IsReadOnly){try{text.Text=ReleaseNotes.Read(source,file.Text,null)??"未找到 CHANGELOG.md，请选择日志文件或切换为手动输入。";}catch(IOException ex){text.Text=ex.Message;}}
            else text.Text=manual;
        }
        Button Button(string label,Action action){var button=new Button{Content=label,Margin=new Thickness(0,0,8,8),Padding=new Thickness(10,6,10,6)};button.Click+=(_,_)=>{try{action();}catch(Exception ex){DialogMessage.Show(form,ex.Message,"更新说明");}};controls.Children.Add(button);return button;}
        Button("选择日志文件",()=>{var dialog=new OpenFileDialog{Filter="更新日志|*.md;*.txt;*.markdown|所有文件|*.*"};if(Directory.Exists(source))dialog.InitialDirectory=source;if(dialog.ShowDialog(form)==true){file.Text=dialog.FileName;mode.SelectedIndex=0;Load();}});
        Button("重新读取",Load);
        void Save(){if(preview||project==null)return;if(automaticPublishing.ContainsKey(project.FullName))throw new IOException("项目正在自动发布，请完成后再保存日志配置。");
            var current=SuggestedAutomation(project);projectAutomation[project.FullName]=current with{NotesFile=file.Text.Trim(),ManualNotes=mode.SelectedIndex==1?text.Text:null};SaveProjects();
        }
        Button("保存说明配置",()=>{Read();Save();model.Status="已保存此项目的更新说明配置。";});
        form.Label("文件模式在每次上传时重新读取最新内容；手动模式使用填写的说明。保存后，后续自动发布也使用此配置；更新安装包时同步更新说明。");
        mode.SelectionChanged+=(_,_)=>{if(mode.SelectedIndex==0)manual=text.Text;Load();};
        file.LostKeyboardFocus+=(_,_)=>{if(mode.SelectedIndex==0)Load();};Load();
        string Read(){var result=ReleaseNotes.Read(source,file.Text,mode.SelectedIndex==1?text.Text:null);if(result==null)throw new IOException("请选择日志文件或手动输入更新说明。");return result;}
        if(preview&&Environment.GetCommandLineArgs().Contains("--release-notes")){
            var path=Path.Combine(store.Root,"CHANGELOG.md");File.WriteAllText(path,"## 1.0.0.0\n\n- 优化连接体验\n- 修复已知问题");file.Text=path;mode.SelectedIndex=0;Load();
            var loaded=text.IsReadOnly&&Read().Contains("优化连接体验");mode.SelectedIndex=1;text.Text="手动填写的发布说明";var typed=Read()==text.Text;
            mode.SelectedIndex=0;File.WriteAllText(path,"## 1.0.0.0\n\n- 上传前读取最新日志内容");var fresh=Read().Contains("最新日志");Load();
            if(!loaded||!typed||!fresh)throw new IOException("Release notes editor smoke failed");
            form.ContentRendered+=(_,_)=>text.BringIntoView();
            File.WriteAllText(Path.Combine(store.Root,"notes-smoke.json"),JsonSerializer.Serialize(new{FilePreview=loaded,ManualInput=typed,FreshRead=fresh}));
        }
        return ()=>{var result=Read();Save();return result;};
    }
}
