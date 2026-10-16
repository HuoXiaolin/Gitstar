using System.Windows;
using System.Windows.Controls;
namespace Gitstar.Desktop;

public sealed class RepositoryFacts : StackPanel
{
    public static readonly DependencyProperty RepositoryProperty=DependencyProperty.Register(nameof(Repository),typeof(string),typeof(RepositoryFacts),new PropertyMetadata("",Changed));
    public static readonly DependencyProperty MetadataProperty=DependencyProperty.Register(nameof(Metadata),typeof(RepositoryMetadata),typeof(RepositoryFacts),new PropertyMetadata(null,Changed));
    public string Repository {get=>(string)GetValue(RepositoryProperty);set=>SetValue(RepositoryProperty,value);}
    public RepositoryMetadata? Metadata {get=>(RepositoryMetadata?)GetValue(MetadataProperty);set=>SetValue(MetadataProperty,value);}
    public RepositoryFacts(){Orientation=Orientation.Horizontal;Render();}
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)=>((RepositoryFacts)d).Render();
    private void Render()
    {
        Children.Clear();var facts=Metadata??new();
        Add(facts.LicenseLabel,"许可识别结果来自 GitHub；未识别不代表没有许可证。点击查看仓库许可信息。","#license-ov-file");
        Add(facts.StarsLabel,"GitHub 仓库 Star 数量，随项目同步更新。点击查看 Star 列表。","/stargazers");
        if(Children[^1] is Button last)last.Margin=new Thickness(0);
    }
    private void Add(string text,string hint,string suffix)
    {
        var label=new TextBlock{Text=text,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=160};
        var button=new Button{Content=label,Padding=new Thickness(4,2,4,2),Margin=new Thickness(0,0,6,0),MinHeight=24,Height=24,ToolTip=text+"\n"+hint};
        button.SetResourceReference(StyleProperty,"TertiaryButton");button.SetResourceReference(Control.ForegroundProperty,"Muted");
        System.Windows.Automation.AutomationProperties.SetName(button,text);button.Click+=(_,_)=>{if(Rules.ValidRepository(Repository))WindowsServices.Open("https://github.com/"+Repository+suffix);};Children.Add(button);
    }
}
