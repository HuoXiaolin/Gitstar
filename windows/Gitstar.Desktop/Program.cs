using System.Windows;
using System.Windows.Threading;
namespace Gitstar.Desktop;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if(args.FirstOrDefault()=="--git-askpass" || Environment.GetEnvironmentVariable("GITSTAR_ASKPASS_MODE")=="1") {var prompt=args.FirstOrDefault()=="--git-askpass"?args.Skip(1):args;Console.WriteLine(Rules.CredentialAnswer(string.Join(" ",prompt),Environment.GetEnvironmentVariable("GITSTAR_OAUTH_TOKEN")??""));return;}
        var smoke=args.FirstOrDefault()=="--render-smoke";
        using var mutex=new Mutex(true,@"Local\Gitstar-"+Environment.UserName,out var first);
        if(!first&&!smoke){using var signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\Gitstar-Open-"+Environment.UserName);signal.Set();return;}
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.DispatcherUnhandledException+=(_,e)=>{MessageBox.Show(e.Exception.Message,"Gitstar",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
        try {
            var window=new MainWindow(smoke);app.MainWindow=window;
            using var open=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\Gitstar-Open-"+Environment.UserName);
            var registered=ThreadPool.RegisterWaitForSingleObject(open,(_,_)=>app.Dispatcher.BeginInvoke(window.Reopen),null,Timeout.Infinite,false);
            if(smoke)window.Loaded+=(_,_)=>window.Dispatcher.BeginInvoke(()=>window.RenderSmoke(args.ElementAtOrDefault(1)??"Gitstar-preview.png",args.Contains("--cards"),args.Contains("--local"),args.Contains("--dark"),args.Contains("--first-group"),args.Contains("--matching"),args.Contains("--settings"),args.Contains("--avatars"),args.Contains("--progress"),args.Contains("--group-edit"),args.Contains("--add-project"),args.Contains("--batch-select"),args.Contains("--tasks"),args.Contains("--projects"),args.Contains("--project-source"),args.Contains("--project-create"),args.Contains("--project-settings"),args.Contains("--workspace-repair"),args.Contains("--project-publish"),args.Contains("--project-automation")),DispatcherPriority.ApplicationIdle);
            app.Run(window);registered.Unregister(null);
        }catch(Exception e){File.WriteAllText(Path.Combine(Path.GetTempPath(),"Gitstar-startup-error.txt"),e.ToString());if(!smoke)MessageBox.Show("Gitstar 无法启动："+e.Message,"Gitstar");Environment.ExitCode=1;}
    }
}

