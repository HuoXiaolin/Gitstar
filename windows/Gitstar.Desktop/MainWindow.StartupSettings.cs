using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void RenderStartupSettings(Form f,CheckBox enabled,string? previewPath)
    {
        var executable=Environment.ProcessPath??"";var preview=previewPath!=null;
        var command=f.Text("启动项位置",WindowsServices.StartupLocation+"\n值名称：Gitstar",true);command.IsReadOnly=true;command.Height=64;command.MinHeight=64;
        var program=f.Text("当前程序位置",executable);program.IsReadOnly=true;
        var details=f.Advanced("查看已登记命令与正确命令");TextBox registered=null!;
        f.Into(details,()=>{registered=f.Text("已登记的启动命令","",true);registered.IsReadOnly=true;registered.Height=64;registered.MinHeight=64;
            var expected=f.Text("应使用的静默启动命令",WindowsServices.StartupCommand(executable),true);expected.IsReadOnly=true;expected.Height=64;expected.MinHeight=64;});
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(status);
        string? previewCommand=preview?"\"D:\\OldGitstar\\Gitstar.exe\"":null;
        StartupEntry Read()=>preview?WindowsServices.InspectStartupCommand(previewCommand??"",executable,true):WindowsServices.ReadStartupEntry(Registry.CurrentUser,WindowsServices.StartupKey,executable);
        void Check(){try{var state=Read();registered.Text=state.Command.Length==0?"（未登记）":state.Command;status.Text=state.Status+(!enabled.IsChecked.GetValueOrDefault()?" 当前设置未启用开机启动。":"");}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){status.Text="核对未完成："+ex.Message;}}
        Check();
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,10)};f.Fields.Children.Add(actions);
        Button Inline(string label,Func<Task> work){var button=f.Action(label,work);f.Buttons.Children.Remove(button);button.Margin=new Thickness(0,0,8,6);actions.Children.Add(button);return button;}
        Inline("核对启动项",()=>{Check();return Task.CompletedTask;});
        var repair=Inline("修复静默启动",()=>{
            if(preview)previewCommand=WindowsServices.StartupCommand(executable);else WindowsServices.Startup(true);
            var state=Read();if(!state.Matches)throw new IOException("写入后核对未通过："+state.Status);
            enabled.IsChecked=true;if(!preview){settings.Startup=true;Save();}
            Check();status.Text="已修复并启用静默启动；程序位置与 --tray 参数已核对。";return Task.CompletedTask;
        });
        Inline("复制启动项位置",()=>{Clipboard.SetText(WindowsServices.StartupLocation+"\n值名称：Gitstar");return Task.CompletedTask;});
        Inline("打开程序目录",()=>{WindowsServices.Open(Path.GetDirectoryName(executable)!);return Task.CompletedTask;});
        f.Label("修复将立即启用开机静默启动，只更新当前用户的 Gitstar 启动项。若 Windows 任务管理器中已禁用 Gitstar，还需在那里重新启用；登记正确不代表系统已实际执行。");
        if(preview&&Environment.GetCommandLineArgs().Contains("--startup-repair")){
            var before=Read();repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var after=Read();
            File.WriteAllText(Path.ChangeExtension(previewPath!,".startup-repair.json"),JsonSerializer.Serialize(new{StaleEntryDetected=!before.Matches,RepairedCommandVerified=after.Matches,Enabled=enabled.IsChecked==true,SilentArgument=after.Command.EndsWith(" --tray",StringComparison.Ordinal)}));
            if(before.Matches||!after.Matches||enabled.IsChecked!=true)throw new IOException("Startup settings repair regression");
        }
    }
}
