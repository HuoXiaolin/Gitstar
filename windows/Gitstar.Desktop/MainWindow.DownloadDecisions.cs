using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Form? downloadDecisions;

    private async Task DownloadDecisionSmoke(string path)
    {
        using var cancel = new CancellationTokenSource();
        var install = ConfirmDownloadAction("example/editor · v2.4.0", "EditorSetup.exe",
            "安装包已校验", "打开安装器", "仅保留文件", CancellationToken.None);
        var canceled = ConfirmDownloadAction("example/reader · v1.8.0", "Reader.msi",
            "安装包已校验", "打开安装器", "仅保留文件", cancel.Token);
        var form = downloadDecisions ?? throw new IOException("Download list did not open.");
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
        var root = (FrameworkElement)form.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = File.Create(path)) encoder.Save(output);

        var first = (StackPanel)((Border)form.Fields.Children[0]).Child;
        var actions = (WrapPanel)first.Children[first.Children.Count - 1];
        ((Button)actions.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!await install || duplicate.IsCompleted || portable.IsCompleted || form.Fields.Children.Count != 2)
            throw new IOException("One row affected another download confirmation.");
        form.Close();
        if (await duplicate || await portable || downloadDecisions != null)
            throw new IOException("Closing must skip the remaining confirmations.");
    }

    private async Task<bool> ConfirmDownloadAction(
        string project, string path, string status, string confirm, string skip, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var form = downloadDecisions;
        var isNew = form == null;
        if (form == null)
        {
            form = new Form(this, "下载待处理", 780, 460) { ShowInTaskbar = false };
            form.Footnote("关闭列表会跳过剩余确认，已下载的文件会保留。");
            form.Action("关闭列表", () =>
            {
                form.Close();
                return Task.CompletedTask;
            });
            form.Closed += (_, _) => downloadDecisions = null;
            downloadDecisions = form;
        }

        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = project, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        var file = new TextBlock
        {
            Text = Path.GetFileName(path), ToolTip = path,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        details.Children.Add(file);
        var description = new TextBlock
        {
            Text = status, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        details.Children.Add(description);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        details.Children.Add(actions);
        void AddChoice(string label, bool accept)
        {
            var button = new Button { Content = label, Margin = new Thickness(8, 4, 0, 0) };
            button.Click += (_, _) =>
            {
                actions.IsEnabled = false;
                result.TrySetResult(accept);
            };
            actions.Children.Add(button);
        }
        AddChoice(skip, false);
        AddChoice(confirm, true);
        var row = new Border
        {
            Child = details, Padding = new Thickness(0, 10, 0, 10),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        row.SetResourceReference(Border.BorderBrushProperty, "Edge");

        void OnClosed(object? sender, EventArgs args) => result.TrySetResult(false);
        form.Closed += OnClosed;
        form.Fields.Children.Add(row);
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
            form.Fields.Children.Remove(row);
            if (ReferenceEquals(downloadDecisions, form) && form.Fields.Children.Count == 0)
                form.Close();
        }
    }
}
