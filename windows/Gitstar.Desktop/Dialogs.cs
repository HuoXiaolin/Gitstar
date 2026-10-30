using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Gitstar.Desktop;

public sealed class Form : Window
{
    public StackPanel Fields {get;}=new();
    private readonly Border surface;
    private readonly ScrollViewer scroll;
    private readonly Grid layout;
    private readonly TextBlock heading;
    private readonly Border footer;
    private readonly TextBlock footnote;
    private readonly List<Control> editControls=[];
    private Dictionary<Control,string>? editBaseline;
    private bool committing;
    private Action<string>? navigate;
    private bool hasNavigation;
    private readonly HashSet<Button> primaryActions=[];
    public WrapPanel HeaderActions {get;}=new(){HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    private readonly List<(string Title,FrameworkElement Anchor)> sections=[];
    private string selectedSection="";
    public WrapPanel Buttons {get;}=new() {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
    public Form(Window owner,string title,double width=660,double height=650)
    {
        Owner=owner; Title="Gitstar · "+title; Width=width; Height=Math.Min(height,700); MinWidth=440; MinHeight=300; WindowStartupLocation=WindowStartupLocation.CenterOwner; FontFamily=owner.FontFamily;FontSize=13;UseLayoutRounding=true;SnapsToDevicePixels=true;
        Resources.MergedDictionaries.Add(owner.Resources);Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Gitstar;component/DialogStyles.xaml",UriKind.Relative)});
        // Each dialog keeps the module that opened it, including nested dialogs.
        foreach(var key in new[]{"Accent","AccentSoft","AccentInk","ProjectSelection"})if(owner.TryFindResource(key) is Brush brush)Resources[key]=brush;
        SetResourceReference(BackgroundProperty,"Background");SetResourceReference(ForegroundProperty,"Ink");Icon=owner.Icon;
        layout=new Grid();layout.SetResourceReference(Panel.BackgroundProperty,"Background");layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid {Margin=new Thickness(24,19,24,14)};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        heading=new TextBlock{Text=title,FontSize=20,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,16,0)};header.Children.Add(heading);Grid.SetColumn(HeaderActions,1);header.Children.Add(HeaderActions);layout.Children.Add(header);
        SourceInitialized+=(_,_)=>WindowsServices.FitWindow(this,false);
        scroll=new ScrollViewer{Content=Fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,6,0)};surface=new Border {Margin=new Thickness(18,0,18,0),Padding=new Thickness(18),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Child=scroll};surface.SetResourceReference(Border.BackgroundProperty,"Surface");surface.SetResourceReference(Border.BorderBrushProperty,"Edge");
        Grid.SetRow(surface,1);layout.Children.Add(surface);
        var footerLayout=new Grid();footerLayout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footerLayout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});footnote=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0)};footnote.SetResourceReference(TextBlock.ForegroundProperty,"Muted");footerLayout.Children.Add(footnote);Grid.SetColumn(Buttons,1);footerLayout.Children.Add(Buttons);
        footer=new Border{BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(24,10,24,12),Margin=new Thickness(0,12,0,0),Child=footerLayout};footer.SetResourceReference(Border.BorderBrushProperty,"Edge");Grid.SetRow(footer,2);layout.Children.Add(footer);Content=layout;
        Loaded+=(_,_)=>OrganizeActions();
        SizeChanged+=(_,_)=>{var narrow=ActualWidth<680;Grid.SetColumn(Buttons,narrow?0:1);Grid.SetRow(Buttons,narrow?1:0);if(footerLayout.RowDefinitions.Count==0){footerLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});footerLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});}footnote.MaxWidth=narrow?double.PositiveInfinity:Math.Max(100,ActualWidth-Buttons.DesiredSize.Width-80);};
        Closing+=(_,e)=>{if(editBaseline!=null&&!committing&&editBaseline.Any(entry=>entry.Value!=EditValue(entry.Key))&&DialogMessage.Show(this,"有尚未保存的修改。关闭后将放弃这些修改，是否继续？","放弃未保存的修改",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)e.Cancel=true;};
    }
    public TextBox Text(string label,string value="",bool multiline=false)
    {
        FieldLabel(label);var box=new TextBox {Text=value,Margin=new Thickness(0,6,0,12),FontSize=13,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?100:36,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};Fields.Children.Add(box);editControls.Add(box);return box;
    }
    public void Label(string label){var text=new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,9),FontSize=12,LineHeight=19};text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Fields.Children.Add(text);}
    private void FieldLabel(string label)=>Fields.Children.Add(new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.Medium,Margin=new Thickness(0,5,0,0),FontSize=13});
    public void Section(string title){var label=new TextBlock{Text=title,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,18,0,10)};label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");Fields.Children.Add(label);sections.Add((title,label));}
    public void SectionNavigation(string? previewSection=null)
    {
        if(sections.Count<2||hasNavigation)return;hasNavigation=true;
        var aliases=Title=="Gitstar · 设置"?new Dictionary<string,string>{{"安装包管理","下载与安装包"},{"下载与更新","下载与安装包"},{"自动检查","检查与消息"},{"消息提醒","检查与消息"},{"开机静默启动","网络与启动"}}:new Dictionary<string,string>();
        string Group(string title)=>aliases.GetValueOrDefault(title,title);
        var grid=new Grid();var navColumn=new ColumnDefinition{Width=new GridLength(170)};grid.ColumnDefinitions.Add(navColumn);grid.ColumnDefinitions.Add(new());var nav=new StackPanel{Margin=new Thickness(0,4,14,0)};grid.Children.Add(new ScrollViewer{Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});surface.Child=null;surface.Padding=new Thickness(0);surface.BorderThickness=new Thickness(0);surface.SetResourceReference(Border.BackgroundProperty,"Background");var pageSurface=new Border{Child=scroll,Padding=new Thickness(18),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1)};pageSurface.SetResourceReference(Border.BackgroundProperty,"Surface");pageSurface.SetResourceReference(Border.BorderBrushProperty,"Edge");Grid.SetColumn(pageSurface,1);grid.Children.Add(pageSurface);surface.Child=grid;
        var children=Fields.Children.Cast<UIElement>().ToArray();Fields.Children.Clear();var intro=new StackPanel();Fields.Children.Add(intro);var host=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch};Fields.Children.Add(host);
        var pages=new Dictionary<string,StackPanel>();var buttons=new Dictionary<string,Button>();StackPanel target=intro;
        foreach(var child in children){var section=sections.FirstOrDefault(s=>ReferenceEquals(s.Anchor,child));if(section.Anchor!=null){var key=Group(section.Title);if(!pages.TryGetValue(key,out var existing)){existing=new StackPanel();pages[key]=existing;}target=existing;((FrameworkElement)child).Margin=new Thickness(0,target.Children.Count==0?0:18,0,12);}target.Children.Add(child);}
        navigate=title=>{title=Group(title);if(!pages.TryGetValue(title,out var panel))return;selectedSection=title;host.Content=panel;scroll.ScrollToTop();foreach(var item in buttons){item.Value.SetResourceReference(BackgroundProperty,item.Key==title?"AccentSoft":"Background");item.Value.SetResourceReference(ForegroundProperty,item.Key==title?"AccentInk":"Muted");}buttons[title].BringIntoView();};
        foreach(var name in pages.Keys){var button=new Button{Content=name,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,4),BorderThickness=new Thickness(0)};button.SetResourceReference(StyleProperty,"NavButton");button.Click+=(_,_)=>navigate(name);nav.Children.Add(button);buttons[name]=button;}
        SizeChanged+=(_,_)=>navColumn.Width=new GridLength(ActualWidth<700?136:170);
        navigate(previewSection!=null&&pages.ContainsKey(Group(previewSection))?previewSection:sections[0].Title);
    }
    public CheckBox Check(string label,bool value=false) {var check=new CheckBox {Content=label,IsChecked=value,Margin=new Thickness(0,7,0,11)};Fields.Children.Add(check);editControls.Add(check);return check;}
    public StackPanel Advanced(string title="高级操作")
    {
        var panel=new StackPanel();Fields.Children.Add(new Expander{Header=title,Content=panel,Margin=new Thickness(0,10,0,8)});return panel;
    }
    public void Into(Panel panel,Action create)
    {
        var index=Fields.Children.Count;create();while(Fields.Children.Count>index){var child=Fields.Children[index];Fields.Children.RemoveAt(index);panel.Children.Add(child);}
    }
    public ComboBox Select(string label,IEnumerable<string> items,int selected=0) {FieldLabel(label);var box=new ComboBox {ItemsSource=items,SelectedIndex=selected,Margin=new Thickness(0,6,0,12),MinHeight=36};Fields.Children.Add(box);editControls.Add(box);return box;}
    public Button Action(string label,Func<Task> action,bool primary=false)
    {
        var b=new Button {Content=label,Tag=action,Padding=new Thickness(13,7,13,7),Margin=new Thickness(8,4,0,0)};
        if(primary){primaryActions.Add(b);b.SetResourceReference(BackgroundProperty,"Accent");b.Foreground=Brushes.White;b.BorderThickness=new Thickness(0);}
        b.Click+=async(_,_)=>{b.IsEnabled=false;committing=primary||label.StartsWith("保存",StringComparison.Ordinal)||label.StartsWith("重新授权",StringComparison.Ordinal);try{await action();}catch(OperationCanceledException){DialogMessage.Show(this,"操作已暂停，可从任务记录核实进度。","Gitstar");}catch(Exception e){DialogMessage.Show(this,e.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}finally{committing=false;b.IsEnabled=true;}};
        Buttons.Children.Add(b);return b;
    }
    public void Footnote(string text)=>footnote.Text=text;
    public void FixedSummary(UIElement content){var index=Grid.GetRow(footer);layout.RowDefinitions.Insert(index,new(){Height=GridLength.Auto});Grid.SetRow(footer,index+1);Grid.SetRow(content,index);layout.Children.Add(content);}
    public TextBox Value(string label,string value){var row=new Grid{Margin=new Thickness(0,0,0,10)};row.ColumnDefinitions.Add(new(){Width=new GridLength(108)});row.ColumnDefinitions.Add(new());row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,FontSize=12});var text=new TextBox{Text=value,ToolTip=value,IsReadOnly=true,BorderThickness=new Thickness(0),MinHeight=30,Padding=new Thickness(6,4,6,4),Background=Brushes.Transparent};Grid.SetColumn(text,1);row.Children.Add(text);Fields.Children.Add(row);return text;}
    public void HeaderAction(Button button){Buttons.Children.Remove(button);button.Margin=new Thickness(8,0,0,0);HeaderActions.Children.Add(button);}
    public void NavigateSection(string title)=>navigate?.Invoke(title);
    internal bool HasUnsavedChanges=>editBaseline!=null&&editBaseline.Any(entry=>entry.Value!=EditValue(entry.Key));
    internal string SelectedSection=>selectedSection;
    public void FieldAction(Control field,Button action){if(field.Parent is not Panel parent)return;var index=parent.Children.IndexOf(field);var margin=field.Margin;parent.Children.Remove(field);Buttons.Children.Remove(action);var row=new Grid{Margin=margin};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});field.Margin=new Thickness(0);row.Children.Add(field);action.Margin=new Thickness(8,0,0,0);Grid.SetColumn(action,1);row.Children.Add(action);parent.Children.Insert(index,row);}
    public void InlineChoice(Control field,CheckBox choice){if(field.Parent is not Panel parent||choice.Parent!=parent)return;var index=parent.Children.IndexOf(field);var margin=field.Margin;parent.Children.Remove(field);parent.Children.Remove(choice);var row=new Grid{Margin=margin};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});field.Margin=new Thickness(0);row.Children.Add(field);choice.Margin=new Thickness(18,0,0,0);choice.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(choice,1);row.Children.Add(choice);parent.Children.Insert(index,row);}
    public void SectionActions(string title,params Button[] actions){var section=sections.First(s=>s.Title==title);var start=Fields.Children.IndexOf(section.Anchor);var end=sections.Select(s=>Fields.Children.IndexOf(s.Anchor)).Where(i=>i>start).DefaultIfEmpty(Fields.Children.Count).Min();var row=new WrapPanel{Margin=new Thickness(0,6,0,10)};foreach(var action in actions){Buttons.Children.Remove(action);action.Margin=new Thickness(0,0,8,6);row.Children.Add(action);}Fields.Children.Insert(end,row);}
    public void EnableEditGuard(){Footnote("更改后保存；带有即时操作说明的按钮单独生效。");ContentRendered+=(_,_)=>editBaseline=editControls.Where(c=>c is not TextBox t||!t.IsReadOnly).ToDictionary(c=>c,EditValue);}
    private static string EditValue(Control control)=>control switch{TextBox t=>t.Text,ComboBox c=>c.SelectedIndex.ToString(),CheckBox c=>c.IsChecked?.ToString()??"",_=>""};
    private void OrganizeActions()
    {
        var actions=Buttons.Children.OfType<Button>().ToArray();
        var close=actions.LastOrDefault(b=>b.IsCancel||b.Content?.ToString() is "关闭" or "取消" or "返回");
        if(actions.Length>4){
            var secondary=actions.LastOrDefault(b=>!primaryActions.Contains(b)&&b!=close);
            var overflow=actions.Where(b=>!primaryActions.Contains(b)&&b!=close&&b!=secondary).ToArray();
            var menu=new ContextMenu();foreach(var original in overflow){original.Visibility=Visibility.Collapsed;var item=new MenuItem{Header=original.Content};item.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding(nameof(IsEnabled)){Source=original});item.Click+=(_,_)=>original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu.Items.Add(item);}
            var more=new Button{Content="更多操作",ContextMenu=menu,Margin=new Thickness(8,4,0,0),Padding=new Thickness(13,7,13,7)};more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;menu.IsOpen=true;};Buttons.Children.Insert(0,more);
        }
        foreach(var primary in primaryActions.Where(b=>Buttons.Children.Contains(b)).ToArray()){Buttons.Children.Remove(primary);Buttons.Children.Add(primary);}
    }
    public static void Notes(Window owner,string title,string text) {var f=new Form(owner,title,780,600);var t=f.Text("更新说明（原文）",string.IsNullOrWhiteSpace(text)?"此版本尚未提供更新说明。":text,true);t.IsReadOnly=true;t.MinHeight=260;f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});f.ShowDialog();}
    public void RenderPreview(string path)=>ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(()=>{
        UpdateLayout();if(sections.Count>0)File.WriteAllText(Path.ChangeExtension(path,".navigation.json"),JsonSerializer.Serialize(new{Selected=selectedSection,Offset=scroll.VerticalOffset,Maximum=scroll.ScrollableHeight}));var root=(FrameworkElement)Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);Close();
    },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
}

public static class DialogMessage
{
    public static MessageBoxResult Show(Window owner,string message,string title,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.None)
    {
        var result=buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;
        var f=new Form(owner,title,560,message.Length>220?420:message.Length>100?340:270){MinHeight=220};f.Label(message);
        if(buttons is MessageBoxButton.YesNo or MessageBoxButton.OKCancel){var cancel=f.Action(buttons==MessageBoxButton.YesNo?"返回":"取消",()=>{result=buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;f.Close();return Task.CompletedTask;});cancel.IsCancel=true;cancel.IsDefault=true;}
        var confirm=f.Action(title.Contains("删除")?"确认删除":title.Contains("重置")?"确认重置":buttons==MessageBoxButton.YesNo?"继续":"确定",()=>{result=buttons==MessageBoxButton.YesNo?MessageBoxResult.Yes:MessageBoxResult.OK;f.Close();return Task.CompletedTask;},true);
        if(buttons==MessageBoxButton.OK){confirm.IsDefault=true;confirm.IsCancel=true;}
        f.ShowDialog();return result;
    }
}
