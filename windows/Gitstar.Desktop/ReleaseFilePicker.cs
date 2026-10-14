using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Gitstar.Desktop;

public sealed class ReleaseFilePicker : StackPanel
{
    private sealed class Row : Observable
    {
        public string Path {get;init;}="";
        public string Label {get;init;}="";
        public long Size {get;init;}
        public bool Selectable {get;init;}=true;
        private bool selected;
        public bool Selected {get=>selected;set=>Set(ref selected,value);}
    }
    private readonly ObservableCollection<Row> rows=[];
    private readonly TextBlock summary=new(){FontSize=12,Margin=new Thickness(0,6,0,8),TextWrapping=TextWrapping.Wrap};
    private bool filling;
    private string folder="",readError="";
    public event Action? SelectionChanged;
    public string[] SelectedPaths=>rows.Where(row=>row.Selected).Select(row=>row.Path).ToArray();
    public string[] SelectedNames=>SelectedPaths.Select(path=>System.IO.Path.GetFileName(path)).ToArray();
    public ReleaseFilePicker()
    {
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,6)};Children.Add(actions);
        void Action(string title,Action action){var button=new Button{Content=title,Padding=new Thickness(10,5,10,5),MinHeight=30};button.Click+=(_,_)=>action();actions.Children.Add(button);}
        Action("全选",()=>SelectAll(true));Action("清空",()=>SelectAll(false));Action("刷新列表",()=>{Load(folder,SelectedNames);if(readError.Length==0)SelectionChanged?.Invoke();});
        summary.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Children.Add(summary);
        var list=new ListBox{ItemsSource=rows,Height=170,BorderThickness=new Thickness(1),Background=Brushes.Transparent};list.SetResourceReference(Control.BorderBrushProperty,"Edge");list.SetResourceReference(ItemsControl.ItemContainerStyleProperty,"SourceChangeRow");ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        var check=new FrameworkElementFactory(typeof(CheckBox));var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Label"));label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);check.AppendChild(label);check.SetBinding(CheckBox.IsCheckedProperty,new System.Windows.Data.Binding("Selected"){Mode=BindingMode.TwoWay});check.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding("Selectable"));check.SetBinding(ToolTipProperty,new System.Windows.Data.Binding("Path"));check.SetValue(MarginProperty,new Thickness(0,5,0,5));list.ItemTemplate=new DataTemplate{VisualTree=check};Children.Add(list);
    }
    public void Load(string path,IEnumerable<string>? selected=null)
    {
        folder=path;var names=selected?.ToHashSet(StringComparer.OrdinalIgnoreCase);filling=true;readError="";rows.Clear();
        try{
            if(!Directory.Exists(folder)){readError="发行目录不存在，请选择文件夹。";return;}
            if(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint)){readError="发行目录不能是目录链接。";return;}
            foreach(var file in Directory.EnumerateFiles(folder).OrderBy(System.IO.Path.GetFileName,StringComparer.OrdinalIgnoreCase)){
                var info=new FileInfo(file);var selectable=info.Length>0&&!info.Attributes.HasFlag(FileAttributes.ReparsePoint);var row=new Row{Path=System.IO.Path.GetFullPath(file),Size=info.Length,Selectable=selectable,Label=info.Name+"   ·   "+(info.Length/1048576.0).ToString("F2")+" MB"+(selectable?"":"（空文件或链接，不可上传）"),Selected=selectable&&(names==null||names.Contains(info.Name))};Add(row);
            }
            if(names!=null)foreach(var name in names.Where(name=>rows.All(row=>!System.IO.Path.GetFileName(row.Path).Equals(name,StringComparison.OrdinalIgnoreCase)))){PackageRules.OriginalFileName(name);Add(new(){Path=System.IO.Path.Combine(folder,name),Label=name+"（文件不存在，请取消勾选或补齐文件）",Selected=true});}
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){readError="无法读取发行文件："+ex.Message;}
        finally{filling=false;Update();}
    }
    private void Add(Row row){row.PropertyChanged+=(_,_)=>{if(!filling){Update();SelectionChanged?.Invoke();}};rows.Add(row);}
    public void SelectAll(bool selected)
    {
        filling=true;try{foreach(var row in rows)row.Selected=selected&&row.Selectable&&File.Exists(row.Path);}finally{filling=false;}Update();SelectionChanged?.Invoke();
    }
    private void Update()=>summary.Text=readError.Length>0?readError:$"已选择 {rows.Count(row=>row.Selected)} / {rows.Count} 个文件 · 合计 {rows.Where(row=>row.Selected).Sum(row=>row.Size)/1048576.0:F2} MB";
    public void SetSelected(string name,bool selected){var row=rows.First(row=>System.IO.Path.GetFileName(row.Path).Equals(name,StringComparison.OrdinalIgnoreCase));row.Selected=selected;}
}
