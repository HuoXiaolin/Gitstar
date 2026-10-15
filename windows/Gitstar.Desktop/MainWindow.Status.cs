using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private void StatusDetails_Click(object sender,RoutedEventArgs e)=>StatusDetailsDialog(smokeMode&&Environment.GetCommandLineArgs().Contains("--status-details")?Environment.GetCommandLineArgs().ElementAtOrDefault(2):null);
    private void StatusDetailsDialog(string? previewPath=null)
    {
        var f=new Form(this,"消息详情",820,620);var full=model.Status;var text=f.Text("完整消息",full,true);text.IsReadOnly=true;text.Height=330;
        f.Action("复制详情",()=>{Clipboard.SetText(full);return Task.CompletedTask;});f.Action("任务记录",()=>{f.Close();TasksDialog();return Task.CompletedTask;});f.Action("关闭",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private void StatusSmoke(string path)
    {
        model.Status="推送已停止：远端提交与本地不一致，请点击“核对同步”。\n\n"+string.Join("\n",Enumerable.Range(1,40).Select(i=>"Git 详细输出 "+i+"：完整原始错误信息保留。"));UpdateLayout();
        var report=new{OneLineHeight=StatusButton.ActualHeight<=26.1,SummaryContainsNoLineBreaks=!model.StatusSummary.Contains('\n')&&!model.StatusSummary.Contains('\r'),FullMessagePreserved=model.Status.Split('\n').Length>40,DetailsButtonAvailable=StatusButton.IsEnabled};
        File.WriteAllText(Path.ChangeExtension(path,".status-bar.json"),JsonSerializer.Serialize(report));if(!report.OneLineHeight||!report.SummaryContainsNoLineBreaks||!report.FullMessagePreserved)throw new IOException("Status bar layout regression");
    }
}
