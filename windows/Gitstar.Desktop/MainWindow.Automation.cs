using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private Dictionary<string,ProjectAutomation> projectAutomation=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,TransferState> automaticPublishing=new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim automationSlots=new(4);
    private bool batchPublishing;
    private void RenderProjectAutomation(ManagedProject project)
    {
        var section=ProjectSection("同步与发布");var config=projectAutomation.GetValueOrDefault(project.FullName);
        if(section.Parent is System.Windows.Controls.Border surface){surface.SetResourceReference(BackgroundProperty,"AccentSoft");surface.BorderThickness=new Thickness(0);}
        section.Children.Add(ProjectText(config==null?"配置源码目录、发行目录和版本号来源后，一键提交全部源码变更并发布最新安装包。":"源码："+config.SourceFolder+"\n发行："+config.ReleaseFolder,12,true));
        if(config!=null)section.Children.Add(ProjectText("版本来源："+(config.VersionFile.Length>0?config.VersionFile:"自动读取")+"   ·   "+(config.IncludeInBatch?"参与全部发布":"仅单独发布"),11,true));
        var actions=new WrapPanel();section.Children.Add(actions);var running=automaticPublishing.TryGetValue(project.FullName,out var state);var button=ProjectAction(actions,"同步并发布最新",()=>AutoPublishProject(project),true,ProjectWritable(project)&&!running);
        if(running){button.Opacity=1;button.SetBinding(ContentControl.ContentProperty,new System.Windows.Data.Binding(nameof(TransferState.Label)){Source=state});}
        ProjectAction(actions,"自动发布配置",()=>ProjectDialog(()=>AutomationConfigDialog(project)),enabled:!running);
        section.Children.Add(ProjectText("按 .gitignore 提交全部变更；自动读取版本与日志。已存在相同版本时更新附件与日志，保留版本标签。",11,true));
    }
    private ProjectAutomation SuggestedAutomation(ManagedProject project)
    {
        if(projectAutomation.TryGetValue(project.FullName,out var existing))return existing;
        var linked=Workspace(project);var source=linked!=null&&Directory.Exists(linked.Folder)?linked.Folder:Path.Combine(@"D:\Agant\Github_Source",Rules.SafeSegment(project.Name));var release=Path.Combine(@"D:\Agant\Github_Releases",Rules.SafeSegment(project.Name));
        return new(project.FullName,source,release,AutomationRules.VersionFile(source,release,project.Name),Workspace(project)?.Branch??project.DefaultBranch);
    }
    private void LinkReleaseDirectoryDialog(ManagedProject project,string? previewPath=null)
    {
        var config=SuggestedAutomation(project);var key=ProjectsKey;var f=new Form(this,"切换 / 关联发行目录",720,470);f.Label("项目："+project.FullName);
        var folder=f.Text("本地发行目录",config.ReleaseFolder);f.Label("选择存放安装包的文件夹。保存后，“同步并发布最新”和手动选择安装包都使用此目录；不移动或删除原目录中的文件。");
        f.Action("选择目录",()=>{var d=new OpenFolderDialog{Title="选择安装包所在的发行目录"};if(Directory.Exists(folder.Text))d.InitialDirectory=folder.Text;if(d.ShowDialog(f)==true)folder.Text=d.FolderName;return Task.CompletedTask;});
        f.Action("保存关联",()=>{
            if(string.IsNullOrWhiteSpace(folder.Text))throw new IOException("请选择发行目录。");var path=Path.GetFullPath(folder.Text.Trim());if(!Directory.Exists(path))throw new IOException("发行目录不存在，请重新选择。");
            if(ProjectsKey!=key)throw new IOException("登录账号已改变，请重新打开目录关联。");if(automaticPublishing.ContainsKey(project.FullName))throw new IOException("本项目正在同步发布，请完成后再切换目录。");
            var current=SuggestedAutomation(project);var version=current.VersionFile;
            if(Path.IsPathRooted(version)&&Path.GetFullPath(version).StartsWith(Path.GetFullPath(current.ReleaseFolder).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))version="";
            if(version.Length==0)version=AutomationRules.VersionFile(current.SourceFolder,path,project.Name);
            projectAutomation[project.FullName]=current with{ReleaseFolder=path,VersionFile=version};SaveProjects();RenderProjectDetails(project);model.Status="已关联发行目录："+path;f.Close();return Task.CompletedTask;
        },true);f.Action("取消",()=>{f.Close();return Task.CompletedTask;});if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private async Task<bool> InferAutomation(ManagedProject project)
    {
        if(projectAutomation.ContainsKey(project.FullName))return true;var key=ProjectsKey;var config=SuggestedAutomation(project);
        if(!Directory.Exists(config.SourceFolder)||!Directory.Exists(config.ReleaseFolder)||config.VersionFile.Length==0)return false;
        try{var diagnosis=await new GitWorkspace("").Diagnose(config.SourceFolder,CancellationToken.None);if(ProjectsKey!=key||!diagnosis.IsRoot||!ProjectRules.MatchesRemote(diagnosis.Remote,project.FullName))return false;projectAutomation[project.FullName]=config;SaveProjects();return true;}catch(IOException){return false;}
    }
    private void AutomationConfigDialog(ManagedProject project,string? previewPath=null)
    {
        var config=SuggestedAutomation(project);var f=new Form(this,"自动发布配置",780,740);f.Label("项目："+project.FullName+"\n保存配置后，点击同步发布即可自动执行，不再要求核对文件或确认发布。");
        var source=f.Text("源码目录",config.SourceFolder);var release=f.Text("发行目录",config.ReleaseFolder);var version=f.Text("版本号来源文件",config.VersionFile);f.Label("支持 Directory.Build.props / csproj、package.json、版本文本和 EXE。可填写相对源码目录的路径；留空自动识别。发行目录取当前版本文件，若有版本子目录则选择版本号最大的目录。");
        var participate=f.Check("参与“全部同步并发布最新”",config.IncludeInBatch);var advanced=f.Advanced("发布规则与更新日志");TextBox branch=null!,prefix=null!,patterns=null!,notes=null!;CheckBox replace=null!;ComboBox channel=null!;
        f.Into(advanced,()=>{branch=f.Text("提交分支（留空使用当前分支）",config.Branch);prefix=f.Text("版本标签前缀（可留空，例如 v）",config.TagPrefix);patterns=f.Text("附件匹配规则（用分号分隔）",config.AssetPatterns);notes=f.Text("更新日志文件（可选，相对源码目录或完整路径）",config.NotesFile);replace=f.Check("同版本自动更新附件与日志，保留版本标签",config.ReplaceSameVersion);channel=f.Select("版本类型",["根据版本号自动判断","正式版","测试版"],config.Prerelease==null?0:config.Prerelease==false?1:2);});
        f.Action("选择源码目录",()=>{var d=new OpenFolderDialog{Title="选择已关联 GitHub 的源码根目录"};if(d.ShowDialog(f)==true){source.Text=d.FolderName;version.Text=AutomationRules.VersionFile(source.Text,release.Text,project.Name);}return Task.CompletedTask;});
        f.Action("选择发行目录",()=>{var d=new OpenFolderDialog();if(d.ShowDialog(f)==true){release.Text=d.FolderName;if(version.Text.Length==0)version.Text=AutomationRules.VersionFile(source.Text,release.Text,project.Name);}return Task.CompletedTask;});
        f.Action("选择版本文件",()=>{var d=new OpenFileDialog{Filter="版本来源|*.props;*.csproj;*.json;*.txt;*.exe;*.dll|所有文件|*.*"};if(d.ShowDialog(f)==true)version.Text=d.FileName;return Task.CompletedTask;});
        async Task SaveConfig(bool publish){
            var updated=new ProjectAutomation(project.FullName,Path.GetFullPath(source.Text.Trim()),Path.GetFullPath(release.Text.Trim()),version.Text.Trim(),branch.Text.Trim(),prefix.Text.Trim(),patterns.Text.Trim(),notes.Text.Trim(),participate.IsChecked==true,replace.IsChecked==true,channel.SelectedIndex==0?null:channel.SelectedIndex==2);
            var diagnosis=await new GitWorkspace("").Diagnose(updated.SourceFolder,CancellationToken.None);if(!diagnosis.IsRoot||!ProjectRules.MatchesRemote(diagnosis.Remote,project.FullName))throw new IOException("源码目录需要先关联此项目的 Git 仓库，请在源码工作区使用“切换 / 关联目录”。");
            if(!Directory.Exists(updated.ReleaseFolder))throw new IOException("发行目录不存在，请选择构建后的目录。");projectAutomation[project.FullName]=updated;SaveProjects();RenderProjectDetails(project);f.Close();if(publish)await AutoPublishProject(project);
        }
        f.Action("保存配置",()=>SaveConfig(false));f.Action("保存并同步发布",()=>SaveConfig(true),true);if(previewPath!=null)f.RenderPreview(previewPath);f.ShowDialog();
    }
    private async Task<bool> AutoPublishProject(ManagedProject project,bool configuredOnly=false)
    {
        var key=ProjectsKey;if(!projectAutomation.ContainsKey(project.FullName)&&!await InferAutomation(project)){if(!configuredOnly&&ProjectsKey==key)AutomationConfigDialog(project);return false;}if(ProjectsKey!=key)return false;var config=projectAutomation[project.FullName];
        if(!ProjectWritable(project)||automaticPublishing.ContainsKey(project.FullName))return false;
        var transfer=new TransferState();transfer.Stage("等待同步发布");automaticPublishing[project.FullName]=transfer;if(selectedProject==project.FullName)RenderProjectDetails(project);var completed=false;
        try{
            await Execute("自动发布",project.FullName,async ct=>{
                var op=currentOperation.Value!;op.Repository=project.FullName;transfer.Stage("排队等待发布");await automationSlots.WaitAsync(ct);
                try{
                    void Progress(string text,long? id){transfer.Stage(text);model.Status=project.Name+" · "+text;if(id!=null){op.RemoteId=id;SaveOperations();}}
                    await api.EnsureFreshToken(ct);var git=new GitWorkspace(api.Token);
                    var result=await ProjectAutomationService.Run(config,api,Path.Combine(store.Root,"automation-staging"),async token=>{
                        await api.EnsureFreshToken(token);git=new GitWorkspace(api.Token);var before=await git.Inspect(config.SourceFolder,token);if(!ProjectRules.MatchesRemote(before.Remote,project.FullName))throw new IOException("源码目录 origin 与项目不一致，请重新关联。");await git.EnsureIdentity(config.SourceFolder,api.CommitIdentity,token);var state=await git.AutoUpload(config.SourceFolder,project.FullName,config.Branch,token);state=await VerifySourceRemote(state,project.FullName,token);RememberWorkspace(project,state);op.Results.Add("成功：源码已同步 · "+state.Commit[..Math.Min(10,state.Commit.Length)]);SaveOperations();return state;
                    },Progress,ct,token=>git.Run(config.SourceFolder,token,"log","-5","--pretty=format:- %s (%h)"));
                    op.Tag=result.Tag;op.RemoteId=result.ReleaseId;op.Results.Add(result.Skipped?"成功：按配置保留同版本附件":"成功："+result.Tag+" · 上传 "+result.Uploaded+" 个，复用 "+result.Unchanged+" 个附件");
                    if(result.Release is {} verified)RememberProjectVersion(project.FullName,result.Skipped?verified:verified with{LastUploadedAt=DateTimeOffset.UtcNow,LastUploadedCount=result.Uploaded,LastReusedCount=result.Unchanged,LastAction="同步并发布"});await RefreshReleaseDirectoryStatus(project.FullName,ct);transfer.Stage("同步发布完成");completed=true;
                }finally{automationSlots.Release();}
            },background:true,resources:["repo:"+project.FullName,"git:"+Path.GetFullPath(config.SourceFolder),"release:"+Path.GetFullPath(config.ReleaseFolder)],transfer:transfer);
        }finally{automaticPublishing.Remove(project.FullName);if(projectsPage&&selectedProject==project.FullName)RenderProjectDetails(project);}
        return completed;
    }
    private async void AllAutoPublish_Click(object sender,RoutedEventArgs e)
    {
        if(batchPublishing)return;if(api.Token.Length==0){Login_Click(this,new());return;}batchPublishing=true;AllAutoPublishButton.IsEnabled=false;AllAutoPublishButton.Content="批量同步发布中…";
        try{
            var candidates=myProjects.Where(ProjectWritable).ToArray();foreach(var candidate in candidates)await InferAutomation(candidate);
            var projects=candidates.Where(p=>projectAutomation.TryGetValue(p.FullName,out var config)&&config.IncludeInBatch).ToArray();
            if(projects.Length==0){model.Status="尚未配置参与全部发布的项目，请在项目中设置自动发布配置。";return;}
            var results=await Task.WhenAll(projects.Select(project=>AutoPublishProject(project,true)));model.Status=$"全部同步发布结束：完成 {results.Count(value=>value)} 个，未完成 {results.Count(value=>!value)} 个；未配置、只读及未参与的项目已跳过。详情见任务记录。";
        }finally{batchPublishing=false;AllAutoPublishButton.Content="全部同步并发布最新";AllAutoPublishButton.IsEnabled=true;}
    }
}
