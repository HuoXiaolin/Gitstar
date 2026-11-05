using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            var width=Environment.GetCommandLineArgs().Contains("--extra-narrow")?540:Environment.GetCommandLineArgs().Contains("--narrow")?640:940;
            var f=new Form(this,names[page]+" · 设置",width,680);var headerAction=f.Action(page==3?"核对并修复当前已关联软件的安装位置、路径与版本检查记录":"查看当前下载目录",()=>Task.CompletedTask);f.HeaderAction(headerAction);
            f.Section("下载与安装包");var folder=f.Text("默认下载目录",@"D:\Downloads\Gitstar");var browse=f.Action("选择保存文件夹",()=>Task.CompletedTask);f.FieldAction(folder,browse);
            const string keepLabel="只保留各项目、平台、处理器架构及正式或测试通道的最新安装包，便于离线备用和重新安装";
            var keep=f.Check(keepLabel,true);f.Label("按项目、平台和版本通道分别保留，便于离线重装。");
            var scrollProbe=new ScrollViewer{Height=84,VerticalScrollBarVisibility=ScrollBarVisibility.Visible,HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,Content=new TextBlock{Text="滚动布局核对 · 示例安装包与路径\n横向和纵向滚动均保留，使用测试内容核对实际尺寸。",Width=1000,Height=180,FontSize=12,Margin=new Thickness(4)}};f.Fields.Children.Add(scrollProbe);
            f.Section("检查与消息");var frequency=f.Select("消息轮询周期",["跟随更新检查","每 30 分钟","仅手动"]);f.Label("后台检查只刷新信息；下载由用户手动开始。");
            for(var i=0;i<5;i++)f.Action(i==4?"导出当前选择的项目设置":"辅助操作 "+i,()=>Task.CompletedTask);
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
                    IEnumerable<FrameworkElement> Elements(DependencyObject element){if(element is FrameworkElement item)yield return item;for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)foreach(var nestedElement in Elements(VisualTreeHelper.GetChild(element,i)))yield return nestedElement;}
                    bool InsideWidth(FrameworkElement item,FrameworkElement parent){var p=item.TranslatePoint(new Point(),parent);return p.X>=-1&&p.X+item.ActualWidth<=parent.ActualWidth+1;}
                    var checkboxText=Elements(keep).OfType<TextBlock>().FirstOrDefault(item=>item.Text==keepLabel);
                    var labelsWrap=checkboxText is {TextWrapping:TextWrapping.Wrap}&&InsideWidth(checkboxText,keep);
                    var fieldsFit=new FrameworkElement[]{folder,browse,keep}.All(item=>InsideWidth(item,f.Fields))&&folder.ActualWidth>=140;
                    var headerReachable=InsideWidth(headerAction,(FrameworkElement)f.Content)&&headerAction.TranslatePoint(new Point(0,headerAction.ActualHeight),(FrameworkElement)f.Content).Y<folder.TranslatePoint(new Point(),(FrameworkElement)f.Content).Y;
                    var title=Elements(f).OfType<TextBlock>().First(item=>item.Text==names[selectedPage]+" · 设置");var headerBelow=headerAction.TranslatePoint(new Point(),(FrameworkElement)f.Content).Y>=title.TranslatePoint(new Point(0,title.ActualHeight),(FrameworkElement)f.Content).Y+5;
                    var headerReflows=selectedPage==0?!headerBelow:selectedPage==3&&width==540?headerBelow:true;
                    scrollProbe.ScrollToHorizontalOffset(24);scrollProbe.ScrollToVerticalOffset(24);await f.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);f.UpdateLayout();
                    var scrollMoves=scrollProbe.HorizontalOffset>0&&scrollProbe.VerticalOffset>0;var bars=Elements(f).OfType<ScrollBar>().Where(bar=>bar.IsVisible&&bar.ActualWidth>0&&bar.ActualHeight>0).ToArray();
                    var scrollSizes=bars.Select(bar=>new{Orientation=bar.Orientation.ToString(),Thickness=bar.Orientation==Orientation.Vertical?bar.ActualWidth:bar.ActualHeight,Grip=Elements(bar).OfType<Border>().FirstOrDefault(part=>part.Name=="ScrollGrip")}).ToArray();
                    var slimBars=scrollSizes.Any(item=>item.Orientation=="Horizontal")&&scrollSizes.Any(item=>item.Orientation=="Vertical")&&scrollSizes.All(item=>item.Thickness>=8&&item.Thickness<=10.5&&item.Grip!=null&&(item.Orientation=="Vertical"?item.Grip.ActualWidth:item.Grip.ActualHeight)<=6.5);
                    var corner=Elements(scrollProbe).OfType<System.Windows.Shapes.Rectangle>().FirstOrDefault(part=>ReferenceEquals(part.TemplatedParent,scrollProbe)&&Grid.GetRow(part)==1&&Grid.GetColumn(part)==1);
                    var cornerTransparent=corner is {ActualWidth:>0,ActualHeight:>0,Fill:SolidColorBrush cornerBrush}&&cornerBrush.Color.A==0;
                    scrollProbe.ScrollToTop();scrollProbe.ScrollToLeftEnd();await f.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);f.UpdateLayout();
                    var file=selectedPage==0?path:Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)+"-"+selectedPage+".png");
                    var root=(FrameworkElement)f.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(file))encoder.Save(output);
                    reports.Add(new{Module=names[selectedPage],Accent=colors[selectedPage],Dark=settings.Dark,Width=width,ThemeInheritedAndStable=inherited,InitiallyClean=baseline,UnsavedEditsDetected=dirty,EditsSurviveNavigation=retained,FooterReachable=reachable,SecondaryActionsInMenu=overflow,NavigationFocusInsideButton=focusInside,LongCheckboxWraps=labelsWrap,FormRowsFit=fieldsFit,HeaderActionsReachable=headerReachable,HeaderReflowsByContent=headerReflows,SlimScrollBars=slimBars,HorizontalAndVerticalScroll=scrollMoves,ScrollCornerTransparent=cornerTransparent,ScrollBarSizes=scrollSizes.Select(item=>new{item.Orientation,item.Thickness,VisualThickness=item.Grip==null?0:item.Orientation=="Vertical"?item.Grip.ActualWidth:item.Grip.ActualHeight}).ToArray()});
                    File.WriteAllText(Path.ChangeExtension(path,".dialog-ui.json"),JsonSerializer.Serialize(reports));
                    folder.Text=@"D:\Downloads\Gitstar";keep.IsChecked=true;frequency.SelectedIndex=0;f.Close();
                    if(!baseline||!dirty||!retained||!inherited||!reachable||!overflow||!focusInside||!labelsWrap||!fieldsFit||!headerReachable||!headerReflows||!slimBars||!scrollMoves||!cornerTransparent)throw new IOException("Dialog design regression: "+names[selectedPage]);
                    complete.SetResult();
                }catch(Exception error){folder.Text=@"D:\Downloads\Gitstar";keep.IsChecked=true;frequency.SelectedIndex=0;complete.SetException(error);f.Close();}
            });
            f.ShowDialog();await complete.Task;
        }
        File.WriteAllText(Path.ChangeExtension(path,".dialog-ui.json"),JsonSerializer.Serialize(reports));
    }
}
