using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Form? downloadDecisions;
    private sealed record DownloadDecisionRow(Grid Layout,StackPanel Details,WrapPanel Actions,Button Accept,Button Skip);

    private static void UpdateDownloadDecisionCount(Form form)
    {
        if(form.Tag is TextBlock count)count.Text=$"{form.Fields.Children.Count} 项待处理";
    }

    private async Task DownloadDecisionSmoke(string path)
    {
        using(var alreadyCanceled=new CancellationTokenSource()){
            alreadyCanceled.Cancel();
            try{
                await ConfirmDownloadAction("example/canceled", "Canceled.exe", "已停止", "打开安装器", "跳过", alreadyCanceled.Token);
                throw new IOException("An already canceled task opened a confirmation.");
            }catch(OperationCanceledException){}
            if(downloadDecisions!=null)throw new IOException("An already canceled task created a pending window.");
        }
        using var cancel = new CancellationTokenSource();
        var install = ConfirmDownloadAction("example/editor · v2.4.0", "EditorSetup.exe",
            "安装包已校验", "打开安装器", "仅保留文件", CancellationToken.None);
        var canceled = ConfirmDownloadAction("example/reader · v1.8.0", "Reader.msi",
            "安装包已校验", "打开安装器", "仅保留文件", cancel.Token);
        var form = downloadDecisions ?? throw new IOException("Download list did not open.");
        if(Environment.GetCommandLineArgs().Contains("--narrow"))form.Width=560;
        var duplicate = ConfirmDownloadAction("example/sync · v3.1.0", "SyncSetup.exe",
            "同版本文件已存在，新文件校验成功后才会替换。", "重新下载", "跳过", CancellationToken.None);
        if (!ReferenceEquals(form, downloadDecisions) || form.Fields.Children.Count != 3)
            throw new IOException("Concurrent confirmations must share one window.");

        cancel.Cancel();
        try { await canceled; throw new IOException("Confirmation ignored cancellation."); }
        catch (OperationCanceledException) { }
        var portable = ConfirmDownloadAction("example/notes · v4.0.0", "NotesPortable.zip",
            "便携新版已下载，可继续查看替换选项。", "处理替换", "仅保留文件", CancellationToken.None);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        form.UpdateLayout();
        var narrow=form.ActualWidth<680;
        foreach(var item in form.Fields.Children.OfType<Border>()){
            var row=(DownloadDecisionRow)item.Tag;
            var bounds=row.Actions.TransformToAncestor(row.Layout).TransformBounds(new Rect(row.Actions.RenderSize));
            if(bounds.Left < -1||bounds.Right>row.Layout.ActualWidth+1||row.Details.ActualWidth<150||Grid.GetRow(row.Actions)!=(narrow?1:0))
                throw new IOException("Download decisions do not fit the responsive row layout.");
        }
        if((form.Tag as TextBlock)?.Text!=$"{form.Fields.Children.Count} 项待处理")throw new IOException("Pending count differs from the displayed rows.");
        File.WriteAllText(Path.ChangeExtension(path,".download-decisions-layout.json"),JsonSerializer.Serialize(new{Width=form.ActualWidth,Narrow=narrow,Rows=form.Fields.Children.Count,InlineActions=!narrow,PendingCountAccurate=true}));
        var root = (FrameworkElement)form.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = File.Create(path)) encoder.Save(output);

        var first = (DownloadDecisionRow)((Border)form.Fields.Children[0]).Tag;
        first.Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!await install || duplicate.IsCompleted || portable.IsCompleted || form.Fields.Children.Count != 2)
            throw new IOException("One row affected another download confirmation.");
        form.Close();
        if (await duplicate || await portable || downloadDecisions != null)
            throw new IOException("Closing must skip the remaining confirmations.");

        var next=ConfirmDownloadAction("example/reopened", "Next.exe", "安装包已校验", "打开安装器", "仅保留文件", CancellationToken.None);
        var reopened=downloadDecisions??throw new IOException("A new download did not reopen the pending list.");
        if(ReferenceEquals(reopened,form)||reopened.Fields.Children.Count!=1)
            throw new IOException("The reopened pending list reused closed rows.");
        var only=(DownloadDecisionRow)((Border)reopened.Fields.Children[0]).Tag;
        only.Skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(await next||downloadDecisions!=null)
            throw new IOException("Skipping the last item must close the reopened pending list.");
    }

    private async Task<bool> ConfirmDownloadAction(
        string project, string path, string status, string confirm, string skip, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(exit||quitting)throw new OperationCanceledException("Gitstar 正在退出。");
        var form = GetDownloadDecisionForm();
        var isNew = !form.IsVisible;

        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var details = new StackPanel{Margin=new Thickness(0,0,16,0)};layout.Children.Add(details);
        details.Children.Add(new TextBlock
        {
            Text = project, FontSize=14,FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        var file = new TextBlock
        {
            Text = Path.GetFileName(path), ToolTip = path,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        details.Children.Add(file);
        var description = new TextBlock
        {
            Text = status,FontSize=12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        details.Children.Add(description);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center };
        Grid.SetColumn(actions,1);layout.Children.Add(actions);
        Button AddChoice(string label, bool accept)
        {
            var button = new Button { Content = label,Padding=new Thickness(10,5,10,5),MinHeight=32, Margin = new Thickness(8, 3, 0, 3) };
            if(accept){button.SetResourceReference(Control.ForegroundProperty,"AccentInk");button.SetResourceReference(Control.BorderBrushProperty,"Accent");}
            else{button.SetResourceReference(StyleProperty,"TertiaryButton");button.SetResourceReference(Control.ForegroundProperty,"Ink");}
            button.Click += (_, _) =>
            {
                actions.IsEnabled = false;
                result.TrySetResult(accept);
            };
            actions.Children.Add(button);
            return button;
        }
        var skipButton=AddChoice(skip, false);
        var acceptButton=AddChoice(confirm, true);
        var row = new Border
        {
            Child = layout,Tag=new DownloadDecisionRow(layout,details,actions,acceptButton,skipButton), Padding = new Thickness(0, 14, 0, 14),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        row.SetResourceReference(Border.BorderBrushProperty, "Edge");
        void ArrangeRow(){var narrow=form.ActualWidth>0&&form.ActualWidth<680;Grid.SetColumnSpan(details,narrow?2:1);details.Margin=new Thickness(0,0,narrow?0:16,0);Grid.SetColumn(actions,narrow?0:1);Grid.SetColumnSpan(actions,narrow?2:1);Grid.SetRow(actions,narrow?1:0);actions.Margin=new Thickness(0,narrow?8:0,0,0);}
        void OnSizeChanged(object sender,SizeChangedEventArgs args)=>ArrangeRow();
        form.SizeChanged+=OnSizeChanged;ArrangeRow();

        void OnClosed(object? sender, EventArgs args) => result.TrySetResult(false);
        form.Closed += OnClosed;
        form.Fields.Children.Add(row);
        UpdateDownloadDecisionCount(form);
        UpdatePendingDownloadBadge();
        try
        {
            if (isNew) form.Show();
            var accepted = await result.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            return accepted;
        }
        finally
        {
            form.Closed -= OnClosed;
            form.SizeChanged-=OnSizeChanged;
            form.Fields.Children.Remove(row);
            UpdateDownloadDecisionCount(form);
            UpdatePendingDownloadBadge();
            if (ReferenceEquals(downloadDecisions, form) && form.Fields.Children.Count == 0)
                form.Close();
        }
    }
}
