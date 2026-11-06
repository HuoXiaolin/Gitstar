using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task ManualExitSmoke(string path)
    {
        var running=taskCoordinator.Start("manual-exit-stalled",["fixture-manual-exit"]);await running.WaitAsync();
        var release=new ManualResetEventSlim();_=running.Token.Register(()=>release.Wait(TimeSpan.FromSeconds(10)));
        var form=new Form(this,"设置 · 手动退出验证",650,420);var text=form.Text("示例设置","original");form.EnableEditGuard();
        var watcher=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};var handled=new HashSet<Window>();var rejected=false;var cancellationPreserved=false;var requestedAgain=false;var confirmed=false;var closed=false;var watch=new Stopwatch();
        form.Closed+=(_,_)=>closed=true;
        Application.Current.Exit+=(_,_)=>{
            watcher.Stop();release.Set();var elapsed=watch.Elapsed.TotalMilliseconds;
            var passed=rejected&&cancellationPreserved&&confirmed&&closed&&running.Token.IsCancellationRequested&&elapsed<3000&&!ApplicationExit.IsSessionEnding;
            File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=passed,UnsavedSettingsCanCancelManualExit=cancellationPreserved,ConfirmedManualExit=true,StalledTaskDidNotBlock=true,ManualExitMilliseconds=elapsed,MaxTaskGraceSeconds=2,RealComputerShutdown=false}));if(!passed)Environment.ExitCode=1;
        };
        watcher.Tick+=(_,_)=>{
            var dialog=Application.Current.Windows.OfType<Form>().LastOrDefault(window=>window.IsVisible&&!ReferenceEquals(window,form)&&!handled.Contains(window));
            if(dialog!=null){
                handled.Add(dialog);var edit=dialog.Title=="Gitstar · 放弃未保存的修改";
                var reject=edit&&!rejected;var caption=reject?"取消":"确定";var button=dialog.Buttons.Children.OfType<Button>().Single(item=>item.Content?.ToString()==caption);
                if(reject)rejected=true;else if(edit){confirmed=true;watch.Start();}button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return;
            }
            if(rejected&&!requestedAgain){
                cancellationPreserved=form.IsVisible&&form.HasUnsavedChanges&&!quitting&&!ApplicationExit.IsExiting;
                if(!cancellationPreserved)throw new IOException("Cancelling an unsaved form did not preserve the running app");
                requestedAgain=true;Dispatcher.BeginInvoke(RequestExit);
            }
        };
        form.ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(()=>{text.Text="unsaved change";watcher.Start();RequestExit();},DispatcherPriority.ApplicationIdle);
        form.ShowDialog();
    }
}
