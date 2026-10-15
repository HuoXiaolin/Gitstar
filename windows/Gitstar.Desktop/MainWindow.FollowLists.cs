using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using UiBinding=System.Windows.Data.Binding;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed class FollowPick : Observable
    {
        public string Name {get;init;}="";private bool selected=true;
        public bool Selected {get=>selected;set=>Set(ref selected,value);}
    }
    private void AddFollowSettings(Form form)
    {
        form.Section("关注列表");form.Label("导出时可选择 GitHub 账号关注、本地关注，统一生成本地关注清单。导入只保存在本机；与账号收藏重复时优先显示为 GitHub 关注，不会重复列出。");
        var actions=new WrapPanel();form.Fields.Children.Add(actions);
        foreach(var button in new[]{form.Action("导入关注列表",()=>{ImportFollowDialog(form);return Task.CompletedTask;}),form.Action("导出关注列表",()=>{ExportFollowDialog(form);return Task.CompletedTask;})}){form.Buttons.Children.Remove(button);button.Margin=new Thickness(0,0,8,8);actions.Children.Add(button);}
        var history=form.Action("取消关注历史",()=>{FollowHistoryDialog(form);return Task.CompletedTask;});form.Buttons.Children.Remove(history);history.Margin=new Thickness(0,0,8,8);actions.Children.Add(history);
    }
    private void ExportFollowDialog(Window owner,string? previewPath=null)
    {
        var remote=FollowLists.GitHub(repositories);var local=FollowLists.Local(repositories,settings.ExtraRepositories,settings.HiddenRepositories);var f=new Form(owner,"导出关注列表",680,460);
        f.Label("所有勾选来源会合并、去重，导出为可在其他账号或电脑导入的本地关注清单。使用上次同步的账号收藏，不包含凭据、源码或分组设置；可能包含私有仓库名称，请妥善保管。");
        var github=f.Check($"当前 GitHub 账号：{settings.Account} · {remote.Length} 个项目",settings.Account.Length>0);github.IsEnabled=settings.Account.Length>0;
        var offline=f.Check($"本地关注 · {local.Length} 个项目",true);
        f.Action("取消",()=>{f.Close();return Task.CompletedTask;}).IsCancel=true;
        f.Action("导出为本地清单",()=>{var names=(github.IsChecked==true?remote:[]).Concat(offline.IsChecked==true?local:[]).ToArray();if(names.Length==0)throw new IOException("所选来源没有关注项目。");var picker=new SaveFileDialog{Filter="Gitstar 本地关注清单|*.json",FileName="Gitstar-local-follows.json"};if(picker.ShowDialog(f)==true){File.WriteAllText(picker.FileName,FollowLists.Export(names));model.Status=$"已导出 {FollowLists.Normalize(names).Length} 个项目为本地关注清单。";f.Close();}return Task.CompletedTask;},true);
        if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private void ImportFollowDialog(Window owner)
    {
        var picker=new OpenFileDialog{Filter="Gitstar 本地关注清单|*.json"};if(picker.ShowDialog(owner)!=true)return;if(new FileInfo(picker.FileName).Length>2*1024*1024)throw new IOException("关注清单超过 2 MB，请拆分后导入。");
        var names=FollowLists.Import(File.ReadAllText(picker.FileName));var f=new Form(owner,"导入本地关注",780,600);f.Label($"清单包含 {names.Length} 个项目。可选择需要导入的仓库；已有项自动去重，GitHub 收藏优先显示。本操作不会修改 GitHub Star。");
        var rows=names.Select(name=>new FollowPick{Name=name}).ToArray();var list=new ListBox{ItemsSource=rows,Height=300,BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent,ItemContainerStyle=(Style)f.FindResource("SourceChangeRow")};
        var factory=new FrameworkElementFactory(typeof(CheckBox));factory.SetBinding(CheckBox.IsCheckedProperty,new UiBinding("Selected"){Mode=BindingMode.TwoWay});factory.SetBinding(ContentControl.ContentProperty,new UiBinding("Name"));factory.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,5));list.ItemTemplate=new DataTemplate{VisualTree=factory};VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);f.Fields.Children.Add(list);
        f.Action("全选 / 清空",()=>{var selected=rows.Any(row=>!row.Selected);foreach(var row in rows)row.Selected=selected;return Task.CompletedTask;});f.Action("取消",()=>{f.Close();return Task.CompletedTask;}).IsCancel=true;
        f.Action("导入本地关注",()=>{var selected=rows.Where(row=>row.Selected).Select(row=>row.Name).ToArray();if(selected.Length==0)throw new IOException("请至少选择一个项目。");var before=settings.ExtraRepositories.Count;settings.ExtraRepositories=FollowLists.Normalize(settings.ExtraRepositories.Concat(selected)).ToList();settings.HiddenRepositories.RemoveAll(repo=>selected.Contains(repo,StringComparer.OrdinalIgnoreCase));Save();SaveCache();RebuildGroups();Display();model.Status=$"已导入本地关注 · 新增 {settings.ExtraRepositories.Count-before} 项，已有项已去重。点击检查更新获取版本。";f.Close();return Task.CompletedTask;},true);f.ShowDialog();
    }
    private void FollowListSmoke(string path)
    {
        settings.Account="example";AccountLabel();repositories=[new("example/editor","",false,true),new("example/sync","",false),new("example/android","",false,true)];settings.ExtraRepositories=["EXAMPLE/EDITOR","example/sync"];Save();SaveCache();RebuildGroups();Page(false);
        GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(g=>g.Id=="local-following");var onlyLocal=model.Cards.Count==1&&model.Cards[0].Repository=="example/sync"&&model.Cards[0].FollowSource=="本地关注";
        GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(g=>g.Id=="all");var githubFirst=model.Cards.Count==3&&model.Cards.Count(c=>c.FollowSource=="GitHub 关注")==2;var noGitHubRoot=!model.Groups.Any(g=>g.Id=="github-following");
        GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(g=>g.Id=="GitHub/fixture-tools");var remoteGroupCorrect=model.Cards.Count==1&&model.Cards[0].Repository=="example/editor";
        foreach(var group in groups)group.Repositories.RemoveAll(repo=>repo!="example/editor");SaveCache();RebuildGroups();GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(g=>g.Id=="ungrouped");var ungroupedCorrect=model.Cards.Count==1&&model.Cards[0].Repository=="example/android";
        var serialized=FollowLists.Export(FollowLists.GitHub(repositories).Concat(FollowLists.Local(repositories,settings.ExtraRepositories,settings.HiddenRepositories)));var names=FollowLists.Import(serialized);var roundTrip=names.Length==3;
        LoadCache();RebuildGroups();GroupSidebar.SelectedIndex=model.Groups.ToList().FindIndex(g=>g.Id=="local-following");var restored=model.Cards.Count==1;
        if(!onlyLocal||!githubFirst||!roundTrip||!restored||!noGitHubRoot||!remoteGroupCorrect||!ungroupedCorrect)throw new IOException("Follow source precedence regression");
        File.WriteAllText(Path.ChangeExtension(path,".follows.json"),JsonSerializer.Serialize(new{LocalOnly=onlyLocal,GitHubPrecedence=githubFirst,ExportedAsLocalList=roundTrip,CacheRestored=restored,NoSeparateGitHubGroup=noGitHubRoot,GitHubSubgroupsPreserved=remoteGroupCorrect,GitHubUngroupedPreserved=ungroupedCorrect}));
    }
}
