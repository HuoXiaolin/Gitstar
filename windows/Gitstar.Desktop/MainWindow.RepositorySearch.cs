using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Gitstar.Desktop;
public partial class MainWindow
{
    private Button AddRepositorySearch(Form owner,FrameworkElement field,Func<string> read,Action<string> choose,Func<string>? fallback=null)
    {
        var button=owner.Action("搜索选择仓库",()=>{RepositorySearchDialog(owner,read(),choose,fallback?.Invoke()??"");return Task.CompletedTask;});owner.Buttons.Children.Remove(button);button.HorizontalAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,0,0,10);owner.Fields.Children.Insert(owner.Fields.Children.IndexOf(field)+1,button);return button;
    }
    private void RepositorySearchDialog(Window owner,string initial,Action<string> choose,string fallback="",string? previewPath=null,Func<string,CancellationToken,Task<GitHubClient.RepositorySearchPage>>? searchRemote=null)
    {
        var account=CacheKey;var f=new Form(owner,"搜索 GitHub 仓库",800,710);f.Label("输入软件名称或关键词，本机仓库列表即时模糊筛选；点击“搜索 GitHub”检索线上仓库。选择结果后自动填写地址，不会添加 Star 或关注。");
        using var lifetime=new CancellationTokenSource();var closed=false;CancellationTokenSource? pending=null;var generation=0;f.Closed+=(_,_)=>{closed=true;lifetime.Cancel();};
        var query=f.Text("软件名称 / 仓库关键词 / 完整地址",initial.Length>0?initial:fallback);var hint=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,0,0,8)};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(hint);
        var list=new ListBox{MinHeight=180,MaxHeight=350,Margin=new Thickness(0,0,0,12)};f.Fields.Children.Add(list);var local=repositories.Concat(myProjects.Select(project=>new Repository(project.FullName,"我的项目",project.Private))).DistinctBy(repo=>repo.FullName,StringComparer.OrdinalIgnoreCase).ToArray();List<Repository> remote=[];Button use=null!;
        void Render(){var rows=local.Select(repo=>(Repo:repo,Score:SearchText.Score(repo.FullName+" "+repo.Description,query.Text))).Where(row=>row.Score>0).OrderByDescending(row=>row.Score).Select(row=>row.Repo).Take(30).Concat(remote).DistinctBy(repo=>repo.FullName,StringComparer.OrdinalIgnoreCase).ToArray();list.Items.Clear();foreach(var repo in rows){var body=new StackPanel();body.Children.Add(ProjectText(repo.FullName+"  ·  "+(local.Any(item=>item.FullName.Equals(repo.FullName,StringComparison.OrdinalIgnoreCase))?"本机列表":"GitHub")+(repo.Private?" · 私有":""),14));if(repo.Description.Length>0){var desc=ProjectText(repo.Description,12,true);desc.MaxWidth=650;desc.TextTrimming=TextTrimming.CharacterEllipsis;body.Children.Add(desc);}list.Items.Add(new ListBoxItem{Content=body,Tag=repo,Padding=new Thickness(6,8,6,8)});}hint.Text=rows.Length==0?"本机列表没有匹配项，可搜索 GitHub。":$"列出 {rows.Length} 项候选，请核对作者和项目说明。";use.IsEnabled=false;}
        async Task Search(){var current=++generation;pending?.Cancel();using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);pending=request;var text=query.Text.Trim();hint.Text="正在搜索 GitHub…";try{UseFeature(UsageFeature.RepositorySearch);var page=await (searchRemote??api.SearchRepositories)(text,request.Token);if(closed||current!=generation)return;remote=page.Items;Render();hint.Text+=$" GitHub 找到 {page.Total} 项，显示前 {page.Items.Count} 项。"+(page.Incomplete?"结果可能不完整，可缩小关键词后重试。":"");}catch(OperationCanceledException){}catch(Exception error){if(!closed&&current==generation)hint.Text="线上搜索未完成："+error.Message+" 本机列表仍可选择。";}finally{if(ReferenceEquals(pending,request))pending=null;}}
        void Pick(){if(account!=CacheKey)throw new IOException("账号已改变，请重新打开搜索窗口。");if(list.SelectedItem is ListBoxItem{Tag:Repository repo}){choose(repo.FullName);f.Close();}}
        f.Action("搜索 GitHub",Search,true);use=f.Action("使用所选仓库",()=>{Pick();return Task.CompletedTask;},true);f.Action("取消",()=>{f.Close();return Task.CompletedTask;});list.SelectionChanged+=(_,_)=>use.IsEnabled=list.SelectedItem!=null;list.MouseDoubleClick+=(_,_)=>Pick();query.TextChanged+=(_,_)=>{generation++;pending?.Cancel();remote=[];Render();};query.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Search();}};Render();
        if(previewPath!=null){remote=[new("example/editor","Example editor repository",false),new("example/editor-tools","Related tools",false)];Render();list.SelectedIndex=0;f.ContentRendered+=(_,_)=>{var selected=((Repository)((ListBoxItem)list.SelectedItem).Tag).FullName;File.WriteAllText(Path.ChangeExtension(previewPath,".search.json"),JsonSerializer.Serialize(new{Candidates=list.Items.Count,Selected=selected,CanChoose=use.IsEnabled}));choose(selected);};f.RenderPreview(previewPath);}
        f.ShowDialog();
    }
}
