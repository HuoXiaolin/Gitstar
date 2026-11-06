using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Form? taskWindow;
    private sealed record TaskRow(Operation Operation, Border View, Grid Layout, StackPanel Content,
        TextBlock Title, TextBlock Metadata, TextBlock State, TextBlock Detail, ProgressBar Progress,
        WrapPanel Actions, Button Pause, Button Cancel, Button Resume, Button Details);

    private void Tasks_Click(object sender, RoutedEventArgs e)
    {
        UseFeature(UsageFeature.Tasks);
        TasksDialog();
    }

    private void TasksDialog(string? previewPath = null)
    {
        if (taskWindow is {} opened) { opened.Activate(); return; }
        var form = new Form(this, "任务记录", 850, 620);
        if(previewPath!=null&&Environment.GetCommandLineArgs().Contains("--narrow"))form.Width=560;
        taskWindow = form;
        var rows = new Dictionary<string, TaskRow>();
        var list = new StackPanel();
        var summary = new TextBlock { Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        form.Fields.Children.Add(summary);
        form.Fields.Children.Add(list);
        var empty=new StackPanel{Margin=new Thickness(0,24,0,24)};
        empty.Children.Add(new TextBlock{Text="还没有任务记录",FontSize=16,FontWeight=FontWeights.SemiBold});
        var emptyHint=new TextBlock{Text="下载、检查或发布项目后，可在这里查看进度与处理结果。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
        emptyHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");empty.Children.Add(emptyHint);form.Fields.Children.Add(empty);
        var pending = form.Action("下载待处理", () =>
        {
            form.Close();OpenDownloadDecisionList();
            return Task.CompletedTask;
        });
        form.HeaderAction(pending);
        pending.Name="PendingDownloadsEntry";
        pending.SetResourceReference(Control.ForegroundProperty,"AccentInk");
        pending.SetResourceReference(Control.BorderBrushProperty,"Accent");
        var packagesEntry=form.Action("安装包管理",()=>{form.Close();PackageManagerDialog();return Task.CompletedTask;});
        packagesEntry.Name="PackageManagerEntry";packagesEntry.ToolTip="当前保存规则："+PackageStorageRule;form.HeaderAction(packagesEntry);
        Button? pauseAll=null;

        string DetailsText(Operation op)=>$"{op.Kind} · {op.Description}\n记录时间  {op.Updated:yyyy-MM-dd HH:mm:ss}\n\n{op.State}"
            +(taskTransfers.TryGetValue(op.Id,out var transfer)?"\n"+transfer.Label+"\n"+transfer.Detail:"")
            +(op.Results.Count>0?"\n\n"+string.Join("\n",op.Results):"")
            +(op.RemoteId!=null?$"\n\nRelease ID  {op.RemoteId}":"");

        TaskRow CreateRow(Operation operation)
        {
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var content=new StackPanel{Margin=new Thickness(0,0,16,0)};layout.Children.Add(content);
            var title=new TextBlock{FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};content.Children.Add(title);
            var statusLine=new Grid{Margin=new Thickness(0,6,0,0)};statusLine.ColumnDefinitions.Add(new(){Width=GridLength.Auto});statusLine.ColumnDefinitions.Add(new());content.Children.Add(statusLine);
            var metadata=new TextBlock{FontSize=12,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};metadata.SetResourceReference(TextBlock.ForegroundProperty,"Muted");statusLine.Children.Add(metadata);
            var state=new TextBlock{FontSize=13,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(state,1);statusLine.Children.Add(state);
            var detail=new TextBlock{FontSize=12,Margin=new Thickness(0,3,0,0),TextTrimming=TextTrimming.CharacterEllipsis};detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(detail);
            var progress=new ProgressBar{Minimum=0,Maximum=100,Height=3,Margin=new Thickness(0,9,0,0),Visibility=Visibility.Collapsed};progress.SetResourceReference(Control.ForegroundProperty,"Accent");progress.SetResourceReference(Control.BackgroundProperty,"Panel");content.Children.Add(progress);
            var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(actions, 1);
            Button Action(string text,bool quiet=false)
            {
                var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5),MinHeight=32, Margin = new Thickness(6, 3, 0, 3) };
                if(quiet){button.SetResourceReference(StyleProperty,"TertiaryButton");button.SetResourceReference(Control.ForegroundProperty,"Ink");}
                actions.Children.Add(button);
                return button;
            }
            var details=Action("详情",true);
            details.ToolTip="查看完整状态与处理结果";
            details.Click+=(_,_)=>{
                var detailForm=new Form(form,"任务详情",760,550);
                var text=detailForm.Text("完整记录",DetailsText(operation),true);text.IsReadOnly=true;text.MinHeight=260;
                detailForm.Action("关闭",()=>{detailForm.Close();return Task.CompletedTask;});detailForm.ShowDialog();
            };
            var pause = Action("暂停");
            pause.ToolTip="暂停 Gitstar 当前任务，已启动的安装器继续运行";
            pause.Click += (_, _) => taskCoordinator.Pause(operation.Id);
            var cancel = Action("取消",true);
            cancel.Click += (_, _) =>
            {
                if (taskCoordinator.Contains(operation.Id)) taskCoordinator.Cancel(operation.Id);
                else if (OperationRules.CanCancel(operation))
                {
                    // Old records may point at user files ending in .partial. Only the download owns temp cleanup.
                    OperationRules.CancelStopped(operation);
                    SaveOperations();
                }
                Update();
            };
            var resume = Action("重试 / 继续");resume.SetResourceReference(Control.ForegroundProperty,"AccentInk");resume.SetResourceReference(Control.BorderBrushProperty,"Accent");
            resume.Click += async (_, _) =>
            {
                var project = myProjects.FirstOrDefault(p => p.FullName.Equals(operation.Repository, StringComparison.OrdinalIgnoreCase));
                if (project == null) { model.Status = "此项目已不在当前项目列表，请先刷新项目列表。"; return; }
                form.Close();
                await AutoPublishProject(project, true);
            };
            layout.Children.Add(actions);
            var view=new Border{Child=layout,Padding=new Thickness(0,12,0,12),BorderThickness=new Thickness(0,0,0,1)};view.SetResourceReference(Border.BorderBrushProperty,"Edge");
            return new(operation,view,layout,content,title,metadata,state,detail,progress,actions,pause,cancel,resume,details);
        }

        void Update()
        {
            // Keep existing row controls alive while jobs change or arrive. No full-list rebuild on each timer tick.
            var visible = operations.Where(op => taskCoordinator.Contains(op.Id))
                .Concat(operations.AsEnumerable().Reverse().Take(80)).DistinctBy(op => op.Id).ToArray();
            var retained = visible.Select(op => op.Id).ToHashSet();
            foreach (var old in rows.Keys.Where(id => !retained.Contains(id)).ToArray())
            {
                list.Children.Remove(rows[old].View);
                rows.Remove(old);
            }
            foreach (var operation in visible)
            {
                if (rows.ContainsKey(operation.Id)) continue;
                var row = CreateRow(operation);
                rows.Add(operation.Id, row);
                list.Children.Add(row.View);
            }
            var pendingCount = PendingDownloadCount;
            pending.Content = $"下载待处理（{pendingCount}）";
            pending.IsEnabled = pendingCount > 0;
            pending.Visibility=pendingCount>0?Visibility.Visible:Visibility.Collapsed;
            empty.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
            summary.Visibility=rows.Count>0?Visibility.Visible:Visibility.Collapsed;
            summary.Text = $"进行中 {taskCoordinator.Count} 项 · 最近 {rows.Count} 项记录 · 自动刷新";
            if(pauseAll!=null)pauseAll.IsEnabled=taskCoordinator.Count>0;
            foreach (var row in rows.Values)
            {
                var op = row.Operation;
                var active=taskCoordinator.Contains(op.Id);taskTransfers.TryGetValue(op.Id,out var transfer);
                row.Title.Text=op.Description;row.Metadata.Text=$"{op.Kind} · {op.Updated:MM-dd HH:mm}";
                row.State.Text=op.State+(transfer!=null&&transfer.Label.Length>0?" · "+transfer.Label:"");row.State.ToolTip=row.State.Text;
                row.State.SetResourceReference(TextBlock.ForegroundProperty,active?"AccentInk":op.State.Contains("失败")||op.State.Contains("待核实")?"DangerInk":"Ink");
                row.Detail.Text=transfer?.Detail.Length>0?transfer.Detail:op.Results.Count>0?op.Results[0]:"";row.Detail.ToolTip=row.Detail.Text;row.Detail.Visibility=row.Detail.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
                row.Progress.Visibility=transfer?.Active==true?Visibility.Visible:Visibility.Collapsed;
                row.Progress.IsIndeterminate=transfer?.Indeterminate==true;row.Progress.Value=transfer?.Percentage??0;
                row.Pause.IsEnabled = active;row.Pause.Visibility=active?Visibility.Visible:Visibility.Collapsed;
                row.Cancel.IsEnabled = row.Pause.IsEnabled || OperationRules.CanCancel(op);
                row.Cancel.Visibility=row.Cancel.IsEnabled?Visibility.Visible:Visibility.Collapsed;
                row.Resume.IsEnabled = !row.Pause.IsEnabled && OperationRules.CanCancel(op)
                    && projectAutomation.ContainsKey(op.Repository) && !automaticPublishing.ContainsKey(op.Repository);
                row.Resume.Visibility = op.Kind == "自动发布" && row.Resume.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                var narrow = form.ActualWidth > 0 && form.ActualWidth < 720;
                Grid.SetColumnSpan(row.Content, narrow ? 2 : 1);row.Content.Margin=new Thickness(0,0,narrow?0:16,0);
                Grid.SetColumn(row.Actions, narrow ? 0 : 1);
                Grid.SetColumnSpan(row.Actions, narrow ? 2 : 1);
                Grid.SetRow(row.Actions, narrow ? 1 : 0);
                row.Actions.Margin = new Thickness(0, narrow ? 8 : 0, 0, 0);
            }
        }

        Update();
        if (previewPath != null && Environment.GetCommandLineArgs().Contains("--tasks-live"))
        {
            var added = new Operation { Kind = "下载", Description = "example/notes · Notes.zip", State = "等待执行" };
            var original = rows.Values.First().View;
            operations.Add(added);
            Update();
            if (!rows.ContainsKey(added.Id) || !rows.Values.Any(row => ReferenceEquals(row.View, original)))
                throw new IOException("Task list must append live jobs without rebuilding existing rows.");
            var oldPartial = Path.Combine(store.Root, "existing-attachment.partial");
            File.WriteAllText(oldPartial, "user attachment");
            added.File = oldPartial;
            rows[added.Id].Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!File.Exists(oldPartial) || OperationRules.CanCancel(added))
                throw new IOException("Cancelling a stored task must not delete an arbitrary attachment.");
            File.WriteAllText(Path.ChangeExtension(previewPath, ".tasks.json"), JsonSerializer.Serialize(new
            { LiveRows = true, ExistingRowsPreserved = true, CancelPreservesFiles = true }));
        }
        if (previewPath != null && Environment.GetCommandLineArgs().Contains("--cancel-failed"))
        {
            var row = rows.Values.First(item => item.Operation.Kind == "自动发布");
            var enabled = row.Cancel.IsEnabled;
            if (!enabled) throw new IOException("Failed task cancel button is disabled");
            row.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Update();
            if (OperationRules.CanCancel(row.Operation) || row.Resume.IsEnabled || row.Resume.Visibility != Visibility.Collapsed)
                throw new IOException("Cancelled task still allows retry");
            File.WriteAllText(Path.ChangeExtension(previewPath, ".cancel.json"), JsonSerializer.Serialize(new
            { CancelWasEnabled = enabled, row.Operation.State, RetryHidden = true, row.Operation.RemoteId }));
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Update();
        form.Closed += (_, _) => { timer.Stop(); taskWindow = null; };
        form.SizeChanged += (_, _) => Update();
        timer.Start();
        form.Footnote("暂停下载后需重新下载，已完成安装包会保留。外部安装器继续运行。发布结果不明时，请先核实远端。");
        pauseAll=form.Action("暂停全部任务", () => { taskCoordinator.CancelAll(); return Task.CompletedTask; });pauseAll.IsEnabled=taskCoordinator.Count>0;
        form.Action("关闭", () => { form.Close(); return Task.CompletedTask; });
        if(previewPath!=null)form.ContentRendered+=(_,_)=>form.Dispatcher.BeginInvoke(()=>{
            Update();form.UpdateLayout();var narrow=form.ActualWidth<720;
            foreach(var row in rows.Values){
                var actions=row.Actions.TransformToAncestor(row.Layout).TransformBounds(new Rect(row.Actions.RenderSize));
                if(actions.Left < -1||actions.Right>row.Layout.ActualWidth+1||row.Title.ActualWidth<120||Grid.GetRow(row.Actions)!=(narrow?1:0))
                    throw new IOException("Task row actions or title do not fit the responsive layout.");
                if(!taskCoordinator.Contains(row.Operation.Id)&&row.Pause.Visibility!=Visibility.Collapsed)
                    throw new IOException("Inactive task exposes an unavailable pause action.");
            }
            File.WriteAllText(Path.ChangeExtension(previewPath,".tasks-layout.json"),JsonSerializer.Serialize(new{Width=form.ActualWidth,Narrow=narrow,Rows=rows.Count,InlineActions=!narrow,DetailsAvailable=rows.Values.All(row=>row.Details.IsEnabled),UnavailableActionsHidden=true}));
        },DispatcherPriority.ApplicationIdle);
        if (previewPath != null) form.RenderPreview(previewPath);
        form.ShowDialog();
    }
}
