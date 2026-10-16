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
        var initial=config?.ManualNotes is {} saved?(string.IsNullOrWhiteSpace(saved)?0:2):detected.Length>0?1:string.IsNullOrWhiteSpace(previous)?0:2;
        var mode=form.Select("说明来源（可选）",["默认简要说明","读取日志文件","手动输入"],initial);
        var file=form.Text("日志文件（默认源码目录的 CHANGELOG.md）",detected);
        var controls=new WrapPanel();form.Fields.Children.Add(controls);
        var text=form.Text("更新说明（Markdown）",config?.ManualNotes??previous,true);text.Height=150;
        var manual=text.Text;
        void Load(){text.IsReadOnly=mode.SelectedIndex!=2;file.IsEnabled=mode.SelectedIndex==1;
            if(mode.SelectedIndex==1){try{text.Text=ReleaseNotes.Read(source,file.Text,null);}catch(IOException ex){text.Text=ex.Message;}}
            else text.Text=mode.SelectedIndex==0?ReleaseNotes.DefaultText:manual;
        }
        Button Button(string label,Action action){var button=new Button{Content=label,Margin=new Thickness(0,0,8,8),Padding=new Thickness(10,6,10,6)};button.Click+=(_,_)=>{try{action();}catch(Exception ex){DialogMessage.Show(form,ex.Message,"更新说明");}};controls.Children.Add(button);return button;}
        Button("选择日志文件",()=>{var dialog=new OpenFileDialog{Filter="更新日志|*.md;*.txt;*.markdown|所有文件|*.*"};if(Directory.Exists(source))dialog.InitialDirectory=source;if(dialog.ShowDialog(form)==true){file.Text=dialog.FileName;mode.SelectedIndex=1;Load();}});
        Button("重新读取",Load);
        void Save(){if(preview||project==null)return;if(automaticPublishing.ContainsKey(project.FullName))throw new IOException("项目正在自动发布，请完成后再保存日志配置。");
            var current=SuggestedAutomation(project);projectAutomation[project.FullName]=current with{NotesFile=file.Text.Trim(),ManualNotes=mode.SelectedIndex==1?null:mode.SelectedIndex==2?text.Text:""};SaveProjects();
        }
        Button("保存说明配置",()=>{Read();Save();model.Status="已保存此项目的更新说明配置。";});
        form.Label("无需选择或填写，默认说明为“"+ReleaseNotes.DefaultText+"”。手动留空或日志文件为空也使用默认说明。保存后，后续自动发布沿用此配置；文件模式在上传时重新读取最新内容。");
        text.TextChanged+=(_,_)=>{if(!text.IsReadOnly)manual=text.Text;};mode.SelectionChanged+=(_,_)=>Load();
        file.LostKeyboardFocus+=(_,_)=>{if(mode.SelectedIndex==1)Load();};Load();
        string Read()=>ReleaseNotes.Read(source,file.Text,mode.SelectedIndex==1?null:mode.SelectedIndex==2?text.Text:"");
        if(preview&&Environment.GetCommandLineArgs().Contains("--release-notes")){
            var path=Path.Combine(store.Root,"CHANGELOG.md");File.WriteAllText(path,"## 2.0.0\n\n- 优化连接体验\n- 修复已知问题");file.Text=path;mode.SelectedIndex=1;Load();
            var loaded=text.IsReadOnly&&Read().Contains("优化连接体验");mode.SelectedIndex=2;text.Text="手动填写的发布说明";var typed=Read()==text.Text;text.Clear();var blank=Read()==ReleaseNotes.DefaultText;
            mode.SelectedIndex=1;File.WriteAllText(path,"## 2.0.0\n\n- 上传前读取最新日志内容");var fresh=Read().Contains("最新日志");Load();mode.SelectedIndex=0;var defaults=Read()==ReleaseNotes.DefaultText;
            if(!loaded||!typed||!fresh||!blank||!defaults)throw new IOException("Release notes editor smoke failed");
            form.ContentRendered+=(_,_)=>text.BringIntoView();
            File.WriteAllText(Path.Combine(store.Root,"notes-smoke.json"),JsonSerializer.Serialize(new{FilePreview=loaded,ManualInput=typed,FreshRead=fresh,BlankUsesDefault=blank,DefaultWithoutSelection=defaults}));
        }
        return ()=>{var result=Read();Save();return result;};
    }
}
