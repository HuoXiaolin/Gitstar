using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Gitstar.Desktop;

public sealed class Form : Window
{
    public StackPanel Fields {get;}=new();
    public WrapPanel Buttons {get;}=new() {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(18,0,18,14)};
    public Form(Window owner,string title,double width=660,double height=650)
    {
        Owner=owner; Title="Gitstar · "+title; Width=width; Height=height; MinWidth=440; MinHeight=320; WindowStartupLocation=WindowStartupLocation.CenterOwner; FontFamily=new("Microsoft YaHei UI");
        Resources.MergedDictionaries.Add(owner.Resources);Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Gitstar;component/DialogStyles.xaml",UriKind.Relative)});
        SetResourceReference(BackgroundProperty,"Background");SetResourceReference(ForegroundProperty,"Ink");Icon=owner.Icon;
        var grid=new Grid();grid.SetResourceReference(Panel.BackgroundProperty,"Background");grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(22,16,22,0)};
        header.Children.Add(new Image{Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Gitstar.png")),Width=32,Height=32,Margin=new Thickness(0,0,12,0)});
        header.Children.Add(new TextBlock{Text=title,FontSize=21,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});grid.Children.Add(header);
        SourceInitialized+=(_,_)=>WindowsServices.FitWindow(this,false);
        var surface=new Border {CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Margin=new Thickness(18,14,18,14),Padding=new Thickness(18),Child=new ScrollViewer {Content=Fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        surface.SetResourceReference(Border.BackgroundProperty,"Surface");surface.SetResourceReference(Border.BorderBrushProperty,"Edge");Grid.SetRow(surface,1);grid.Children.Add(surface);Grid.SetRow(Buttons,2);grid.Children.Add(Buttons);Content=grid;
    }
    public TextBox Text(string label,string value="",bool multiline=false)
    {
        FieldLabel(label);var box=new TextBox {Text=value,Margin=new Thickness(0,7,0,16),FontSize=13,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?110:40,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};Fields.Children.Add(box);return box;
    }
    public void Label(string label){var text=new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,9),FontSize=12,LineHeight=19};text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Fields.Children.Add(text);}
    private void FieldLabel(string label)=>Fields.Children.Add(new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.Medium,Margin=new Thickness(0,5,0,0),FontSize=13});
    public void Section(string title){var label=new TextBlock{Text=title,Foreground=new SolidColorBrush(Color.FromRgb(102,104,220)),FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,12)};Fields.Children.Add(label);}
    public CheckBox Check(string label,bool value=false) {var check=new CheckBox {Content=label,IsChecked=value,Margin=new Thickness(0,8,0,12)};Fields.Children.Add(check);return check;}
    public StackPanel Advanced(string title="高级操作")
    {
        var panel=new StackPanel();Fields.Children.Add(new Expander{Header=title,Content=panel,Margin=new Thickness(0,10,0,8)});return panel;
    }
    public void Into(Panel panel,Action create)
    {
        var index=Fields.Children.Count;create();while(Fields.Children.Count>index){var child=Fields.Children[index];Fields.Children.RemoveAt(index);panel.Children.Add(child);}
    }
    public ComboBox Select(string label,IEnumerable<string> items,int selected=0) {FieldLabel(label);var box=new ComboBox {ItemsSource=items,SelectedIndex=selected,Margin=new Thickness(0,7,0,16),MinHeight=40};Fields.Children.Add(box);return box;}
    public Button Action(string label,Func<Task> action,bool primary=false)
    {
        var b=new Button {Content=label,Tag=action,Padding=new Thickness(16,9,16,9),Margin=new Thickness(8,6,0,0)};
        if(primary){b.Background=new SolidColorBrush(Color.FromRgb(59,111,245));b.Foreground=Brushes.White;b.BorderThickness=new Thickness(0);}
        b.Click+=async(_,_)=>{b.IsEnabled=false;try{await action();}catch(OperationCanceledException){DialogMessage.Show(this,"操作已暂停，可从任务记录核实进度。","Gitstar");}catch(Exception e){DialogMessage.Show(this,e.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}finally{b.IsEnabled=true;}};
        Buttons.Children.Add(b);return b;
    }
    public static void Notes(Window owner,string title,string text) {var f=new Form(owner,title,780,700);var t=f.Text("更新说明（原文）",text,true);t.IsReadOnly=true;t.MinHeight=500;f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});f.ShowDialog();}
    public void RenderPreview(string path)=>ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(()=>{
        UpdateLayout();var root=(FrameworkElement)Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);Close();
    },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
}

public static class DialogMessage
{
    public static MessageBoxResult Show(Window owner,string message,string title,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.None)
    {
        var result=buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;
        var f=new Form(owner,title,580,360);f.Label(message);
        if(buttons is MessageBoxButton.YesNo or MessageBoxButton.OKCancel){var cancel=f.Action(buttons==MessageBoxButton.YesNo?"否":"取消",()=>{result=buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;f.Close();return Task.CompletedTask;});cancel.IsCancel=true;cancel.IsDefault=true;}
        var confirm=f.Action(buttons==MessageBoxButton.YesNo?"是":"确定",()=>{result=buttons==MessageBoxButton.YesNo?MessageBoxResult.Yes:MessageBoxResult.OK;f.Close();return Task.CompletedTask;},true);
        if(buttons==MessageBoxButton.OK){confirm.IsDefault=true;confirm.IsCancel=true;}
        f.ShowDialog();return result;
    }
}
