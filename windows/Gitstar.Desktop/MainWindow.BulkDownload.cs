using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UiBinding=System.Windows.Data.Binding;
using Microsoft.Win32;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed class DownloadPick : Observable
    {
        public ReleaseDownload Item {get;init;}=null!;
        public string Label=>$"{Item.Release.Repository} · {Item.Release.Tag}  /  {Item.Asset.Name}  ·  {Item.Asset.Size/1048576.0:F1} MB";
        private bool selected=true;
        public bool Selected {get=>selected;set=>Set(ref selected,value);}
    }
    private async void BulkDownload_Click(object sender,RoutedEventArgs e)=>await BulkDownloadDialog();
    private async Task BulkDownloadDialog(string? previewPath=null)
    {
        if(quitting)return;
        settings.BulkDownloadExtensions??=[".exe",".msi",".zip"];if(!BulkDownloadRules.Days.Contains(settings.BulkDownloadDays))settings.BulkDownloadDays=7;
        var current=model.Cards.Select(c=>c.Repository).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var all=RepositoryChecks.Targets(false,repositories,settings).Where(r=>!settings.HiddenRepositories.Contains(r,StringComparer.OrdinalIgnoreCase)).ToArray();
        var f=new Form(this,"批量下载发行包",900,780);var rows=new ObservableCollection<DownloadPick>();var closed=false;var scanning=false;string? scanId=null;
        f.Label("收集所选时间段内的全部已发布版本，按格式选择附件。只保存文件，不启动安装器；保留所选全部版本，不参与自动安装包清理。可在任务中分别暂停或取消。");
        var options=new Grid();for(var i=0;i<3;i++)options.ColumnDefinitions.Add(new());f.Fields.Children.Add(options);
        var panes=Enumerable.Range(0,3).Select(i=>{var panel=new StackPanel{Margin=new Thickness(0,0,i<2?14:0,0)};Grid.SetColumn(panel,i);options.Children.Add(panel);return panel;}).ToArray();
        ComboBox period=null!,scope=null!,channel=null!;
        f.Into(panes[0],()=>period=f.Select("时间范围",BulkDownloadRules.Days.Select(d=>$"最近 {d} 天"),Math.Max(0,Array.IndexOf(BulkDownloadRules.Days,settings.BulkDownloadDays))));
        f.Into(panes[1],()=>scope=f.Select("项目范围",[$"当前分组及筛选结果（{current.Length}）",$"全部关注（{all.Length}）"]));
        f.Into(panes[2],()=>channel=f.Select("版本通道",["仅正式版","正式 + 测试","仅测试版"],ChannelFilter.SelectedIndex));
        f.Section("文件格式");var formats=new WrapPanel();f.Fields.Children.Add(formats);var suffixes=new[]{".exe",".msi",".zip",".7z",".rar",".tar.gz",".tar.xz",".apk",".dmg",".deb",".rpm",".AppImage"};var formatChecks=new List<CheckBox>();
        foreach(var suffix in suffixes){var check=new CheckBox{Content=suffix,IsChecked=settings.BulkDownloadExtensions.Contains(suffix,StringComparer.OrdinalIgnoreCase),Margin=new Thickness(0,0,20,10)};formats.Children.Add(check);formatChecks.Add(check);}
        var secondary=new Grid();secondary.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});secondary.ColumnDefinitions.Add(new());f.Fields.Children.Add(secondary);var left=new StackPanel{Margin=new Thickness(0,0,14,0)};var right=new StackPanel();Grid.SetColumn(right,1);secondary.Children.Add(left);secondary.Children.Add(right);
        TextBox custom=null!;ComboBox duplicate=null!;f.Into(left,()=>custom=f.Text("其他格式（逗号分隔，可留空）",string.Join(", ",settings.BulkDownloadExtensions.Except(suffixes,StringComparer.OrdinalIgnoreCase))));f.Into(right,()=>duplicate=f.Select("同版本文件",["下载前统一询问","重新下载并替换","跳过已有文件"],settings.SameVersionDownload));
        var folder=f.Text("保存目录",settings.DownloadFolder);
        var conditions=new StackPanel();foreach(var child in f.Fields.Children.Cast<UIElement>().Skip(1).ToArray()){f.Fields.Children.Remove(child);conditions.Children.Add(child);}var conditionPanel=new Expander{Header="下载条件",IsExpanded=true,Content=conditions,Margin=new Thickness(0,0,0,10)};f.Fields.Children.Add(conditionPanel);
        var summary=new TextBlock{Text="点击“读取发行包”生成清单，默认选中全部匹配附件。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,10)};summary.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(summary);
        var list=new ListBox{ItemsSource=rows,Height=210,BorderThickness=new Thickness(1),ItemContainerStyle=(Style)f.FindResource("SourceChangeRow")};list.SetResourceReference(Control.BackgroundProperty,"Surface");list.SetResourceReference(Control.ForegroundProperty,"Ink");list.SetResourceReference(Control.BorderBrushProperty,"Edge");ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        var checkFactory=new FrameworkElementFactory(typeof(CheckBox));var labelFactory=new FrameworkElementFactory(typeof(TextBlock));labelFactory.SetBinding(TextBlock.TextProperty,new UiBinding("Label"));labelFactory.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);checkFactory.AppendChild(labelFactory);checkFactory.SetBinding(CheckBox.IsCheckedProperty,new UiBinding("Selected"){Mode=BindingMode.TwoWay});checkFactory.SetValue(FrameworkElement.MarginProperty,new Thickness(6,6,6,6));list.ItemTemplate=new DataTemplate{VisualTree=checkFactory};VirtualizingStackPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);f.Fields.Children.Add(list);
        Button? read=null,start=null;var scanComplete=false;
        string[] Formats()=>BulkDownloadRules.Extensions(formatChecks.Where(c=>c.IsChecked==true).Select(c=>c.Content.ToString()!).Concat(custom.Text.Split([',','，',';','；',' '],StringSplitOptions.RemoveEmptyEntries)));
        void Selection(){if(start!=null)start.IsEnabled=!scanning&&scanComplete&&rows.Any(r=>r.Selected);}
        void Invalidate(){if(scanning)return;scanComplete=false;rows.Clear();conditionPanel.IsExpanded=true;summary.Text="条件已变更，请重新读取发行包。";Selection();}
        void SetScanning(bool value){scanning=value;foreach(var control in new Control[]{period,scope,channel,custom,folder,duplicate}.Concat(formatChecks))control.IsEnabled=!value;if(read!=null)read.IsEnabled=!value;Selection();}
        void Populate(IEnumerable<ReleaseDownload> items){rows.Clear();foreach(var item in items){var row=new DownloadPick{Item=item};row.PropertyChanged+=(_,_)=>Selection();rows.Add(row);}scanComplete=true;conditionPanel.Header=$"下载条件 · 最近 {BulkDownloadRules.Days[period.SelectedIndex]} 天 · {string.Join(" / ",Formats())}";conditionPanel.IsExpanded=false;Selection();}
        async Task Read(){
            var extensions=Formats();if(extensions.Length==0)throw new IOException("请至少选择一种文件格式。");var names=scope.SelectedIndex==0?current:all;if(names.Length==0)throw new IOException("当前范围没有项目，请切换范围或先同步关注列表。");
            var days=BulkDownloadRules.Days[period.SelectedIndex];var selectedChannel=channel.SelectedIndex;var now=DateTimeOffset.UtcNow;var found=new List<Release>();var failures=new List<string>();var completed=0;var finished=false;
            SetScanning(true);rows.Clear();summary.Text=$"正在读取 {names.Length} 个项目的发行记录…";
            try{await Execute("发行包读取",$"最近 {days} 天 · {names.Length} 个项目",async ct=>{
                var operation=currentOperation.Value!;scanId=operation.Id;
                await RepositoryChecks.Run(names,async(repo,token)=>{try{found.AddRange(await api.ReleasesInPeriod(repo,now.AddDays(-days),now,token));}catch(Exception ex)when(ex is not OperationCanceledException&&ex is not GitHubRateLimitException){failures.Add(repo+"："+ex.Message);operation.Results.Add("待处理："+repo+" · "+ex.Message);}finally{completed++;if(!closed)summary.Text=$"正在读取 {completed}/{names.Length} 个项目…";}},ct);
                ct.ThrowIfCancellationRequested();finished=true;
            },true,resources:["bulk-plan:"+settings.Account]);
            if(closed)return;if(!finished){summary.Text="读取已停止或未完成，可重新读取。";return;}
            var items=BulkDownloadRules.Select(found,now,days,extensions,selectedChannel);Populate(items);summary.Text=$"找到 {rows.Count} 个附件 · 合计 {items.Sum(i=>i.Asset.Size)/1048576.0:F1} MB"+(failures.Count>0?$" · {failures.Count} 个项目读取失败，可下载已读取的清单。\n"+string.Join("；",failures.Take(2)):" · 可取消不需要的附件。");
            }finally{scanId=null;if(!closed)SetScanning(false);}
        }
        foreach(var control in new[]{period,scope,channel})control.SelectionChanged+=(_,_)=>Invalidate();foreach(var control in formatChecks){control.Checked+=(_,_)=>Invalidate();control.Unchecked+=(_,_)=>Invalidate();}custom.TextChanged+=(_,_)=>Invalidate();
        read=f.Action("读取发行包",Read);f.Action("全选 / 清空",()=>{var select=rows.Any(r=>!r.Selected);foreach(var row in rows)row.Selected=select;return Task.CompletedTask;});
        f.Action("选择目录",()=>{var dialog=new OpenFolderDialog{Title="选择下载目录"};if(dialog.ShowDialog(f)==true)folder.Text=dialog.FolderName;return Task.CompletedTask;});
        start=f.Action("下载所选",()=>{
            var selected=rows.Where(r=>r.Selected).Select(r=>r.Item).ToArray();if(selected.Length==0)return Task.CompletedTask;var root=Path.GetFullPath(folder.Text);
            // Equivalent version spellings share the existing flat download name; fetch that file once.
            selected=selected.GroupBy(item=>PackageTarget(item.Release,item.Asset,root),StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderByDescending(i=>i.Release.Published).First()).ToArray();
            var decision=duplicate.SelectedIndex;var existing=selected.Count(item=>packages.Any(p=>p.Path.Equals(PackageTarget(item.Release,item.Asset,root),StringComparison.OrdinalIgnoreCase)&&PackageRules.SameVersion(p,item.Release,item.Asset)&&File.Exists(p.Path)));
            if(decision==0&&existing>0){var confirm=new Form(f,"已有同版本文件",640,400);confirm.Label($"清单中有 {existing} 个同版本文件。请选择本批任务的统一处理方式，新文件仍会在校验成功后替换。");confirm.Action("返回",()=>{confirm.Close();return Task.CompletedTask;}).IsCancel=true;confirm.Action("跳过已有",()=>{decision=2;confirm.Close();return Task.CompletedTask;});confirm.Action("全部替换",()=>{decision=1;confirm.Close();return Task.CompletedTask;},true);confirm.ShowDialog();if(decision==0)return Task.CompletedTask;}
            if(selected.Any(i=>string.IsNullOrEmpty(i.Asset.Digest))&&DialogMessage.Show(f,"部分附件没有作者提供的 SHA-256 摘要，只能核对下载大小。继续下载所选附件？","下载校验",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return Task.CompletedTask;
            settings.BulkDownloadDays=BulkDownloadRules.Days[period.SelectedIndex];settings.BulkDownloadExtensions=Formats().ToList();settings.DownloadFolder=root;Save();f.Tag=(selected,root,decision==0?2:decision);f.Close();return Task.CompletedTask;
        },true);Selection();
        f.Action("取消",()=>{if(scanId!=null)taskCoordinator.Cancel(scanId);f.Close();return Task.CompletedTask;}).IsCancel=true;f.Closed+=(_,_)=>{closed=true;if(scanId!=null)taskCoordinator.Cancel(scanId);};
        if(previewPath!=null){Populate(BulkDownloadRules.Select(releases,DateTimeOffset.UtcNow,7,[".exe",".msi",".zip"]));summary.Text=$"找到 {rows.Count} 个附件 · 默认全选 · 支持多种格式和时间范围";f.RenderPreview(previewPath);}
        f.ShowDialog();if(f.Tag is ValueTuple<ReleaseDownload[],string,int> selection)await DownloadBatch(selection.Item1,selection.Item2,selection.Item3);
    }
    private async Task DownloadBatch(ReleaseDownload[] items,string root,int duplicatePolicy)
    {
        await Execute("批量下载",$"{items.Length} 个发行包",async batchToken=>{
            var parent=currentOperation.Value!;var succeeded=0;var skipped=0;var failed=0;
            await RepositoryChecks.Run(Enumerable.Range(0,items.Length).Select(i=>i.ToString()).ToArray(),async(index,token)=>{
                var item=items[int.Parse(index)];var target=PackageTarget(item.Release,item.Asset,root);var state=new TransferState();var result="待处理";
                await Execute("下载",item.Release.Repository+" · "+item.Release.Tag+" · "+item.Asset.Name,async ct=>{
                    var operation=currentOperation.Value!;operation.Repository=item.Release.Repository;operation.Tag=item.Release.Tag;operation.File=target+".partial";
                    ct.ThrowIfCancellationRequested();
                    state.Stage("核对已有安装包");var previous=packages.FirstOrDefault(p=>p.Path.Equals(target,StringComparison.OrdinalIgnoreCase)&&PackageRules.SameVersion(p,item.Release,item.Asset));var replace=false;
                    if(previous!=null&&await PackageFiles.Matches(previous,ct)){if(duplicatePolicy!=1){var existingIndex=packages.IndexOf(previous);if(existingIndex>=0){packages[existingIndex]=previous with{RetainForBatch=true};SavePackages();}state.Finish("已跳过");result="跳过";operation.Results.Add("同版本文件已存在，按本批设置跳过并保留。");return;}replace=true;}
                    var progressAt=DateTime.UtcNow;state.Stage("下载中 0%",false);state.Detail=item.Asset.Name;
                    try{var path=await WindowsServices.Download(api,item.Release,item.Asset,root,(received,total)=>{if(received<total&&DateTime.UtcNow-progressAt<TimeSpan.FromMilliseconds(100))return;progressAt=DateTime.UtcNow;state.Downloaded(received,total);},ct,()=>state.Stage("校验中"),replacement=>CanReplacePackage(item.Release,item.Asset,target,replacement,replace),forceDownload:replace,targetPath:target);
                        await RecordPackage(item.Release,item.Asset,path,root,ct,retainForBatch:true);state.Finish("已保存安装包");state.Detail=path;operation.Results.Add("成功："+path);result="成功";
                    }catch(OperationCanceledException){state.Finish("已停止");throw;}catch(Exception ex){state.Finish("下载失败");state.Detail=ex.Message;throw;}
                },true,resources:["file:"+target],transfer:state,parentToken:token,parentStopReason:()=>taskCoordinator.StopReason(parent.Id));
                if(state.Active)state.Finish("已停止");
                if(result=="成功")succeeded++;else if(result=="跳过")skipped++;else failed++;
                parent.Results.Add(result+"："+item.Release.Repository+" · "+item.Release.Tag+" · "+item.Asset.Name);token.ThrowIfCancellationRequested();
            },batchToken,concurrency:4);
            model.Status=$"批量下载完成 · 保存 {succeeded} · 跳过 {skipped} · 待处理 {failed} · {root}";
        },resources:["bulk-download:"+settings.Account]);
    }
}
