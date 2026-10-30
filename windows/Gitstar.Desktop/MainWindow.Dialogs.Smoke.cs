using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private async Task DialogDesignSmoke(string path)
    {
        var reports=new List<object>();var colors=new[]{"#386AC6","#168673","#6557AB","#A26624"};
        var names=new[]{"最近更新","本机可更新","我的项目","排行榜"};
        for(var page=0;page<4;page++){
            SetPageAccent(page);var expected=(Brush)Resources["Accent"];
            var f=new Form(this,names[page]+" · 设置",Environment.GetCommandLineArgs().Contains("--narrow")?640:940,680);f.Section("下载与安装包");var folder=f.Text("默认下载目录",@"D:\Downloads\Gitstar");var keep=f.Check("只保留最新安装包",true);f.Label("按项目、平台和版本通道分别保留，便于离线重装。");f.Section("检查与消息");var frequency=f.Select("消息轮询周期",["跟随更新检查","每 30 分钟","仅手动"]);f.Label("后台检查只刷新信息；下载由用户手动开始。");
            for(var i=0;i<5;i++)f.Action("辅助操作 "+i,()=>Task.CompletedTask);
            var cancel=f.Action("取消",()=>{f.Close();return Task.CompletedTask;});cancel.IsCancel=true;
            f.Action("保存设置",()=>{f.Close();return Task.CompletedTask;},true);
            f.EnableEditGuard();f.SectionNavigation();
            var complete=new TaskCompletionSource();var selectedPage=page;
            f.ContentRendered+=(_,_)=>f.Dispatcher.BeginInvoke(async()=>{
                try{
                    var baseline=!f.HasUnsavedChanges;folder.Text=@"D:\OfflinePackages";keep.IsChecked=false;var dirty=f.HasUnsavedChanges;
                    f.NavigateSection("检查与消息");frequency.SelectedIndex=1;f.NavigateSection("下载与安装包");
                    var retained=folder.Text==@"D:\OfflinePackages"&&keep.IsChecked==false&&frequency.SelectedIndex==1&&f.SelectedSection=="下载与安装包";
                    SetPageAccent((selectedPage+1)%4);var nested=new Form(f,"嵌套提示",520,300);
                    var inherited=f.Resources["Accent"]==expected&&nested.Resources["Accent"]==expected;SetPageAccent(selectedPage);
                    await f.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);f.UpdateLayout();
                    IEnumerable<Button> Nav(DependencyObject root){if(root is Button b&&b.Template.FindName("NavLabel",b)!=null)yield return b;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var found in Nav(VisualTreeHelper.GetChild(root,i)))yield return found;}
                    var navigation=Nav(f).ToArray();var focusInside=navigation.Length>0;foreach(var b in navigation){b.Focus();f.UpdateLayout();var ring=(FrameworkElement)b.Template.FindName("Focus",b);var point=ring.TranslatePoint(new Point(),b);focusInside&=point.X>=0&&point.Y>=0&&point.X+ring.ActualWidth<=b.ActualWidth&&point.Y+ring.ActualHeight<=b.ActualHeight;}navigation.First().Focus();
                    var visible=f.Buttons.Children.OfType<Button>().Where(b=>b.Visibility==Visibility.Visible).ToArray();
                    var reachable=visible.All(b=>{var p=b.TranslatePoint(new Point(),(FrameworkElement)f.Content);return p.Y>=0&&p.Y+b.ActualHeight<=((FrameworkElement)f.Content).ActualHeight+1&&p.X>=0&&p.X+b.ActualWidth<=((FrameworkElement)f.Content).ActualWidth+1;});
                    var overflow=visible.Any(b=>b.Content?.ToString()=="更多操作")&&visible.Length<=4;
                    var file=selectedPage==0?path:Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)+"-"+selectedPage+".png");
                    var root=(FrameworkElement)f.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(file))encoder.Save(output);
                    reports.Add(new{Module=names[selectedPage],Accent=colors[selectedPage],Dark=settings.Dark,ThemeInheritedAndStable=inherited,InitiallyClean=baseline,UnsavedEditsDetected=dirty,EditsSurviveNavigation=retained,FooterReachable=reachable,SecondaryActionsInMenu=overflow,NavigationFocusInsideButton=focusInside});
                    folder.Text=@"D:\Downloads\Gitstar";keep.IsChecked=true;frequency.SelectedIndex=0;f.Close();
                    if(!baseline||!dirty||!retained||!inherited||!reachable||!overflow||!focusInside)throw new IOException("Dialog design regression: "+names[selectedPage]);
                    complete.SetResult();
                }catch(Exception error){complete.SetException(error);f.Close();}
            });
            f.ShowDialog();await complete.Task;
        }
        File.WriteAllText(Path.ChangeExtension(path,".dialog-ui.json"),JsonSerializer.Serialize(reports));
    }
}
