using System.Windows;
using System.Windows.Controls;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private sealed record PendingInstallationRow(Border View,TextBlock Title,TextBlock File,TextBlock Status,Button Install,CheckBox Selection,Button More);
    private readonly Dictionary<string,PendingInstallationRow> pendingInstallationRows=[];
    private readonly HashSet<string> selectedPendingInstallations=[];
    private CheckBox? pendingSelectAll;
    private Button? pendingDeleteSelected;
    private Func<bool>? pendingDeleteSmokeConfirm;
    private bool updatingPendingSelection;
    private Form GetDownloadDecisionForm()
    {
        if(downloadDecisions is {} existing)return existing;
        var form=new Form(this,"下载待处理",780,460){ShowInTaskbar=false};downloadDecisions=form;
        pendingSelectAll=new CheckBox{Content="全选",Name="PendingSelectAll",IsThreeState=true,Margin=new Thickness(0,0,16,0),VerticalAlignment=VerticalAlignment.Center};
        pendingSelectAll.Click+=(_,_)=>{if(updatingPendingSelection)return;var selectable=pendingInstallationRows.Where(row=>row.Value.Selection.IsEnabled).Select(row=>row.Key).ToArray();var choose=selectable.Any(id=>!selectedPendingInstallations.Contains(id));foreach(var id in selectable){if(choose)selectedPendingInstallations.Add(id);else selectedPendingInstallations.Remove(id);}UpdatePendingSelection();};form.HeaderActions.Children.Add(pendingSelectAll);
        var count=new TextBlock{FontSize=13,VerticalAlignment=VerticalAlignment.Center};count.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");form.HeaderActions.Children.Add(count);form.Tag=count;
        form.Footnote("关闭仅收起列表；删除所选仅移除待办，安装包文件保留，可从“安装包管理”继续安装。");
        pendingDeleteSelected=form.Action("删除所选",()=>{DeleteSelectedPendingInstallations();return Task.CompletedTask;});pendingDeleteSelected.Name="PendingDeleteSelected";pendingDeleteSelected.SetResourceReference(Control.ForegroundProperty,"DangerInk");
        var check=form.Action("重新检测本机",async()=>{await Execute("检测","核对待安装包",async ct=>{if(pendingInstallations.Any(item=>item.NewInstall!=null))await ScanNewInstallationInventory(PackagesKey,ct);else await ScanMatchedAsync();ct.ThrowIfCancellationRequested();PlanUpdates();Display();});});check.Name="PendingInstallCheck";
        form.Action("关闭列表",()=>{form.Close();return Task.CompletedTask;});
        form.Closed+=(_,_)=>{if(ReferenceEquals(downloadDecisions,form))downloadDecisions=null;pendingInstallationRows.Clear();selectedPendingInstallations.Clear();pendingSelectAll=null;pendingDeleteSelected=null;UpdatePendingDownloadBadge();};
        RefreshPendingInstallationRows();return form;
    }
    private void OpenDownloadDecisionList()
    {
        if(exit||quitting)return;
        ReconcilePendingInstallations();var form=GetDownloadDecisionForm();RefreshPendingInstallationRows();
        if(form.Fields.Children.Count==0){form.Close();model.Status="当前没有待处理的安装包。";return;}
        if(!form.IsVisible)form.Show();if(form.WindowState==WindowState.Minimized)form.WindowState=WindowState.Normal;form.Activate();
    }
    private void RefreshPendingInstallationRows()
    {
        if(downloadDecisions is not {} form||exit||quitting)return;
        foreach(var key in pendingInstallationRows.Keys.Where(key=>pendingInstallations.All(item=>item.Id!=key)).ToArray()){
            form.Fields.Children.Remove(pendingInstallationRows[key].View);pendingInstallationRows.Remove(key);selectedPendingInstallations.Remove(key);
        }
        foreach(var pending in pendingInstallations){
            var id=pending.Id;
            if(!pendingInstallationRows.TryGetValue(id,out var row)){
                var layout=new Grid();layout.ColumnDefinitions.Add(new());layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
                var details=new StackPanel{Margin=new Thickness(0,0,16,0)};
                var selection=new CheckBox{Content=details,Margin=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="PendingSelect"};layout.Children.Add(selection);
                selection.Checked+=(_,_)=>{if(updatingPendingSelection)return;selectedPendingInstallations.Add(id);UpdatePendingSelection();};selection.Unchecked+=(_,_)=>{if(updatingPendingSelection)return;selectedPendingInstallations.Remove(id);UpdatePendingSelection();};
                var title=new TextBlock{FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};details.Children.Add(title);
                var file=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};details.Children.Add(file);
                var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,4,0,0)};status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");details.Children.Add(status);
                var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);layout.Children.Add(actions);
                var folder=new Button{Content="打开文件夹",MinHeight=32,Padding=new Thickness(10,5,10,5),Margin=new Thickness(8,3,0,3)};folder.SetResourceReference(StyleProperty,"TertiaryButton");actions.Children.Add(folder);
                folder.Click+=(_,_)=>{var entry=pendingInstallations.FirstOrDefault(item=>item.Id==id);if(entry!=null&&Directory.Exists(entry.Package.Root))WindowsServices.Open(entry.Package.Root);};
                var install=new Button{MinHeight=32,Padding=new Thickness(10,5,10,5),Margin=new Thickness(8,3,0,3)};install.SetResourceReference(Control.ForegroundProperty,"AccentInk");install.SetResourceReference(Control.BorderBrushProperty,"Accent");actions.Children.Add(install);
                install.Click+=async(_,_)=>{var entry=pendingInstallations.FirstOrDefault(item=>item.Id==id);if(entry!=null)await ContinuePendingInstallation(entry);};
                var more=new Button{Content="更多",Margin=new Thickness(8,3,0,3),Padding=new Thickness(10,5,10,5),MinHeight=32};more.SetResourceReference(StyleProperty,"TertiaryButton");actions.Children.Add(more);var menu=new ContextMenu();var retry=new MenuItem{Header="重新打开安装器"};var manual=new MenuItem{Header="手动关联软件"};menu.Items.Add(retry);menu.Items.Add(manual);more.ContextMenu=menu;more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};retry.Click+=async(_,_)=>{var entry=pendingInstallations.FirstOrDefault(item=>item.Id==id);if(entry!=null)await ContinueNewInstallation(entry,true);};manual.Click+=async(_,_)=>{var entry=pendingInstallations.FirstOrDefault(item=>item.Id==id);if(entry==null)return;BindingDialog(defaultRepository:entry.Package.Repository);await Execute("检测","核对安装关联",async ct=>{await ScanNewInstallationInventory(PackagesKey,ct);});};
                void Arrange(){var narrow=form.ActualWidth>0&&form.ActualWidth<680;Grid.SetColumnSpan(selection,narrow?2:1);details.Margin=new Thickness(0,0,narrow?0:16,0);Grid.SetRow(actions,narrow?1:0);Grid.SetColumn(actions,narrow?0:1);Grid.SetColumnSpan(actions,narrow?2:1);actions.Margin=new Thickness(0,narrow?8:0,0,0);}
                form.SizeChanged+=(_,_)=>Arrange();Arrange();
                var view=new Border{Child=layout,Tag=pending.Id,Padding=new Thickness(0,14,0,14),BorderThickness=new Thickness(0,0,0,1)};view.SetResourceReference(Border.BorderBrushProperty,"Edge");form.Fields.Children.Add(view);
                row=new(view,title,file,status,install,selection,more);pendingInstallationRows[id]=row;
            }
            row.Title.Text=pending.App.Name+" · "+pending.Package.Tag;row.File.Text=pending.Package.AssetName;row.File.ToolTip=pending.Package.Path;
            var linked=PendingCard(pending)!=null;var busy=pendingInstallationBusy.Contains(id);
            row.Status.Text=pendingInstallationMessages.GetValueOrDefault(id)??(pending.NewInstall!=null?"下载来源已记录；安装后将自动检测并关联本机软件。":linked?"安装包已保存，可继续安装；关闭列表或重启软件后仍会保留。":"原软件关联或安装位置已变化，请先核对匹配。");
            row.Install.Content=busy?"处理中…":pending.NewInstall is {} fresh?fresh.Started?"核对安装":"打开安装器":!linked?"核对关联":unavailablePendingPackages.Contains(id)?"重新下载":pending.App.Portable||pending.Package.Path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)?"处理替换":"打开安装器";
            row.More.Visibility=pending.NewInstall!=null?Visibility.Visible:Visibility.Collapsed;row.More.IsEnabled=!busy;
            row.Install.IsEnabled=!busy;
            row.Selection.IsEnabled=!busy;if(busy)selectedPendingInstallations.Remove(id);
        }
        UpdatePendingSelection();
        UpdateDownloadDecisionCount(form);UpdatePendingDownloadBadge();
        foreach(var button in form.Buttons.Children.OfType<Button>().Where(button=>button.Name=="PendingInstallCheck"))button.Visibility=pendingInstallations.Count>0?Visibility.Visible:Visibility.Collapsed;
        if(form.IsVisible&&form.Fields.Children.Count==0)form.Close();
    }
    private void UpdatePendingSelection()
    {
        if(updatingPendingSelection)return;updatingPendingSelection=true;
        try{
            selectedPendingInstallations.RemoveWhere(id=>!pendingInstallationRows.ContainsKey(id)||pendingInstallationBusy.Contains(id));
            foreach(var row in pendingInstallationRows)row.Value.Selection.IsChecked=selectedPendingInstallations.Contains(row.Key);
            var selectable=pendingInstallationRows.Where(row=>row.Value.Selection.IsEnabled).Select(row=>row.Key).ToArray();
            if(pendingSelectAll!=null){pendingSelectAll.Visibility=pendingInstallationRows.Count>0?Visibility.Visible:Visibility.Collapsed;pendingSelectAll.IsEnabled=selectable.Length>0;pendingSelectAll.IsChecked=selectedPendingInstallations.Count==0?false:selectable.All(selectedPendingInstallations.Contains)?true:null;}
            if(pendingDeleteSelected!=null){pendingDeleteSelected.Visibility=pendingInstallationRows.Count>0?Visibility.Visible:Visibility.Collapsed;pendingDeleteSelected.Content=selectedPendingInstallations.Count>0?$"删除所选（{selectedPendingInstallations.Count}）":"删除所选";pendingDeleteSelected.IsEnabled=selectedPendingInstallations.Count>0;}
        }finally{updatingPendingSelection=false;}
    }
    private void DeleteSelectedPendingInstallations()
    {
        if(downloadDecisions is not {} form||exit||quitting)return;var account=PackagesKey;
        var selected=pendingInstallations.Where(item=>selectedPendingInstallations.Contains(item.Id)&&!pendingInstallationBusy.Contains(item.Id)).ToArray();if(selected.Length==0)return;
        var confirmed=smokeMode&&pendingDeleteSmokeConfirm!=null?pendingDeleteSmokeConfirm():DialogMessage.Show(form,$"从待处理列表中删除所选 {selected.Length} 项？\n\n安装包文件会保留，可在“任务 → 安装包管理”中继续安装或删除文件。\n\n"+string.Join("\n",selected.Take(8).Select(item=>item.App.Name+" · "+item.Package.Tag)),"删除所选待处理项",MessageBoxButton.OKCancel)==MessageBoxResult.OK;
        if(!confirmed||account!=PackagesKey||exit||quitting)return;
        var removed=PendingInstallationRules.RemoveSelected(pendingInstallations,selected.Select(item=>item.Id),pendingInstallationBusy);
        foreach(var item in removed){selectedPendingInstallations.Remove(item.Id);pendingInstallationMessages.Remove(item.Id);unavailablePendingPackages.Remove(item.Id);var state=TransferFor(PendingRelease(item),true);if(!state.Active)state.Finish("安装包已保存");}
        SavePendingInstallations();RefreshPendingInstallationRows();UpdatePendingDownloadBadge();model.Status=$"已删除 {removed.Length} 项下载待办，安装包文件已保留。";
    }
}
