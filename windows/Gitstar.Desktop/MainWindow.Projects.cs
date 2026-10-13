using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace Gitstar.Desktop;

public partial class MainWindow
{
    private bool projectsPage,renderingProjects;
    private List<ManagedProject> myProjects=[];
    private List<ProjectWorkspace> projectWorkspaces=[];
    private readonly Dictionary<string,GitProjectState> projectStates=new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string,List<ProjectVersion>> projectVersions=new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? projectsSynced;
    private string? selectedProject;
    private string ProjectsKey=>CacheKey+"/my-projects";
    private void LoadProjects()
    {
        myProjects=store.Get(ProjectsKey,new List<ManagedProject>());projectWorkspaces=store.Get(ProjectsKey+"/workspaces",new List<ProjectWorkspace>());
        projectVersions=new(store.Get(ProjectsKey+"/versions",new Dictionary<string,List<ProjectVersion>>()),StringComparer.OrdinalIgnoreCase);
        projectsSynced=store.Get<DateTimeOffset?>(ProjectsKey+"/synced",null);projectStates.Clear();selectedProject=null;
        projectAutomation=new(store.Get(ProjectsKey+"/automation",new Dictionary<string,ProjectAutomation>()),StringComparer.OrdinalIgnoreCase);
        projectWorkspaces=projectWorkspaces.Select(w=>w with{Folder=PathPreferences.ProjectPath(w.Folder)}).ToList();foreach(var key in projectAutomation.Keys.ToArray()){var c=projectAutomation[key];projectAutomation[key]=c with{SourceFolder=PathPreferences.ProjectPath(c.SourceFolder),ReleaseFolder=PathPreferences.ProjectPath(c.ReleaseFolder),VersionFile=PathPreferences.ProjectPath(c.VersionFile),NotesFile=PathPreferences.ProjectPath(c.NotesFile)};}SaveProjects();
    }
    private void SaveProjects(){store.Set(ProjectsKey,myProjects);store.Set(ProjectsKey+"/workspaces",projectWorkspaces);store.Set(ProjectsKey+"/versions",projectVersions);store.Set(ProjectsKey+"/synced",projectsSynced);store.Set(ProjectsKey+"/automation",projectAutomation);}
    private void RememberProjectVersion(string repo,ProjectVersion version)
    {
        var history=projectVersions.GetValueOrDefault(repo)??[];projectVersions[repo]=history.Where(item=>item.Id!=version.Id).Append(version).OrderByDescending(item=>item.Updated).Take(30).ToList();SaveProjects();
        if(projectsPage&&selectedProject==repo)RenderProjectDetails(myProjects.FirstOrDefault(p=>p.FullName.Equals(repo,StringComparison.OrdinalIgnoreCase)));
    }
    private async Task<ProjectVersion?> VerifyProjectVersion(ManagedProject project,ProjectVersion version)
    {
        ProjectVersion? fresh=null;
        await Execute("版本核实",project.FullName+" · "+version.Tag,async ct=>{
            try{fresh=await api.ProjectVersionDetails(project.FullName,version.Id,ct);RememberProjectVersion(project.FullName,fresh);model.Status=fresh.Tag+" · 已核实为"+fresh.Channel;}
            catch(GitHubNotFoundException){if(projectVersions.TryGetValue(project.FullName,out var history))history.RemoveAll(item=>item.Id==version.Id);SaveProjects();if(selectedProject==project.FullName)RenderProjectDetails(project);model.Status="此版本已不存在或无权访问，已清除旧记录。";}
        },resources:["repo:"+project.FullName]);return fresh;
    }
    private async Task OpenDraftAction(ManagedProject project,ProjectVersion version,bool edit)
    {
        var fresh=await VerifyProjectVersion(project,version);if(fresh?.Draft!=true)return;
        if(edit)EditDraftDialog(project,fresh);else PublishDialog(project,fresh);
    }
    private ProjectWorkspace? Workspace(ManagedProject project)=>projectWorkspaces.FirstOrDefault(w=>w.Repository.Equals(project.FullName,StringComparison.OrdinalIgnoreCase));
    private string SuggestedReleaseTag(ManagedProject? project,ProjectVersion? version=null)
    {
        if(version!=null&&!ReleaseRules.TemporaryTag(version.Tag))return version.Tag;
        if(version!=null&&Rules.CompareVersions(version.Name,version.Name)!=null)return version.Name.Trim();
        if(project!=null){var config=SuggestedAutomation(project);try{if(config.VersionFile.Length>0)return config.TagPrefix+AutomationRules.ReadVersion(AutomationRules.ResolveFile(config.SourceFolder,config.VersionFile));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or System.Text.Json.JsonException or ArgumentException){}}
        return "";
    }
    private void InvalidateProjectCaches(IEnumerable<string> repositories)
    {
        foreach(var repo in repositories){projectStates.Remove(repo);projectVersions.Remove(repo);}
    }
    private void RememberWorkspace(ManagedProject project,GitProjectState state)
    {
        projectWorkspaces.RemoveAll(w=>w.Repository.Equals(project.FullName,StringComparison.OrdinalIgnoreCase));projectWorkspaces.Add(new(project.FullName,state.Folder,state.Branch));projectStates[project.FullName]=state;SaveProjects();if(projectsPage&&selectedProject==project.FullName)RenderProjectDetails(project);
    }
    private void Projects_Click(object sender,RoutedEventArgs e)
    {
        projectsPage=true;model.SelectionMode=false;UpdatesToolbar.Visibility=UpdatesContent.Visibility=Visibility.Collapsed;ProjectsHost.Visibility=Visibility.Visible;
        RecentButton.Background=LocalButton.Background=(Brush)Resources["Surface"];RecentButton.Foreground=LocalButton.Foreground=(Brush)Resources["Ink"];
        SetPageAccent(2);
        model.Title="我的项目工作台";model.Subtitle="管理仓库与本地源码，提交变更，发布正式版与测试版。";RenderProjects();
    }
    private void ProjectFilter_Changed(object sender,RoutedEventArgs e){if(ready && projectsPage)RenderProjects();}
    private void RenderProjects()
    {
        if(!projectsPage || ProjectList==null || renderingProjects)return;renderingProjects=true;
        try{
            var query=ProjectSearch.Text.Trim();var filter=ProjectFilter.SelectedIndex;
            var visible=myProjects.Where(p=>(p.FullName+" "+p.Description).Contains(query,StringComparison.OrdinalIgnoreCase)).Where(p=>filter switch{1=>!p.Private,2=>p.Private,3=>Workspace(p)!=null,_=>true}).OrderByDescending(p=>p.Updated).ToList();
            model.Projects.Clear();foreach(var p in visible)model.Projects.Add(p);
            var chosen=visible.FirstOrDefault(p=>p.FullName==selectedProject)??visible.FirstOrDefault();ProjectList.SelectedItem=chosen;selectedProject=chosen?.FullName;
            model.CountCaption="项目工作区";model.Count=$"{myProjects.Count} 个项目";CountPanel.Background=(Brush)Resources["AccentSoft"];
            RenderProjectDetails(chosen);
        }finally{renderingProjects=false;}
    }
    private void ProjectSelection_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(renderingProjects)return;var project=ProjectList.SelectedItem as ManagedProject;selectedProject=project?.FullName;RenderProjectDetails(project);
    }
    private TextBlock ProjectText(string text,int size=13,bool muted=false)
    {
        var label=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9)};label.SetResourceReference(TextBlock.ForegroundProperty,muted?"Muted":"Ink");return label;
    }
    private StackPanel ProjectSection(string title)
    {
        var content=new StackPanel();var heading=ProjectText(title,16);heading.FontWeight=FontWeights.SemiBold;content.Children.Add(heading);
        var border=new Border{CornerRadius=new(14),BorderThickness=new(1),Padding=new(18),Margin=new(0,0,0,12),Child=content};border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Edge");ProjectDetails.Children.Add(border);return content;
    }
    private Button ProjectAction(Panel parent,string text,Func<Task> work,bool primary=false,bool enabled=true)
    {
        var button=new Button{Content=text,Padding=new(13,8,13,8),Margin=new(0,4,8,4),IsEnabled=enabled};
        if(!primary && text is "打开 GitHub" or "项目设置" or "打开 Releases" or "同步版本" or "日志" or "管理" or "查看任务")button.SetResourceReference(StyleProperty,"TertiaryButton");
        if(primary){button.SetResourceReference(BackgroundProperty,"Accent");button.Foreground=Brushes.White;button.BorderThickness=new(0);}
        button.Click+=async(_,_)=>{button.IsEnabled=false;try{await work();}catch(Exception ex){DialogMessage.Show(this,ex.Message,"操作未完成");}finally{button.IsEnabled=enabled;}};parent.Children.Add(button);return button;
    }
    private Task ProjectDialog(Action action){action();return Task.CompletedTask;}
    private bool ProjectWritable(ManagedProject p)=>api.Token.Length>0 && p.CanPush && !p.Archived;
    private void RenderProjectDetails(ManagedProject? project)
    {
        if(ProjectDetails==null)return;ProjectDetails.Children.Clear();
        if(project==null){var empty=ProjectSection(api.Token.Length==0?"登录后管理你的项目":"创建你的下一个项目");empty.Children.Add(ProjectText(myProjects.Count>0?"没有符合当前筛选的项目。":"同步你的 GitHub 仓库，或创建公开 / 私有项目。关联源码目录后即可检查变更、提交上传和发布版本。",13,true));var actions=new WrapPanel();empty.Children.Add(actions);ProjectAction(actions,api.Token.Length==0?"登录 GitHub":"同步项目",()=>api.Token.Length==0?ProjectDialog(()=>Login_Click(this,new())):SyncProjects(),true);ProjectAction(actions,"创建项目",()=>ProjectDialog(()=>CreateProjectDialog()));return;}
        var p=project;var writable=ProjectWritable(p);var info=ProjectSection(p.Name);info.Children.Add(ProjectText(p.FullName+"   ·   "+p.Visibility+"   ·   "+p.Access+"   ·   默认分支 "+p.DefaultBranch,12,true));info.Children.Add(ProjectText(p.Description.Length>0?p.Description:"还没有项目说明",13,p.Description.Length==0));
        info.Children.Add(ProjectText("项目同步："+(projectsSynced?.ToLocalTime().ToString("MM-dd HH:mm")??"尚未同步"),11,true));var infoActions=new WrapPanel();info.Children.Add(infoActions);
        ProjectAction(infoActions,"打开 GitHub",()=>ProjectDialog(()=>WindowsServices.Open("https://github.com/"+p.FullName)));ProjectAction(infoActions,"项目设置",()=>OpenProjectSettings(p),enabled:p.CanAdmin&&api.Token.Length>0);
        RenderProjectAutomation(p);
        var workspace=Workspace(p);var source=ProjectSection("源码工作区");
        if(workspace==null)source.Children.Add(ProjectText("关联现有 Git 仓库、初始化已有源码，或克隆远程项目到电脑。",13,true));
        else{
            source.Children.Add(ProjectText(workspace.Folder,12,true));
            var available=Directory.Exists(workspace.Folder);var marker=Path.Combine(workspace.Folder,".git");var initialized=available&&(Directory.Exists(marker)||File.Exists(marker));
            if(!initialized){projectStates.Remove(p.FullName);source.Children.Add(ProjectText(available?"此目录已不再是独立 Git 仓库，旧分支和提交信息已清除。请重新关联或初始化源码目录。":"关联目录已不存在，旧分支和提交信息已清除。请重新关联源码目录。",13,true));}
            else if(projectStates.TryGetValue(p.FullName,out var state)){
                source.Children.Add(ProjectText($"上次检查：{state.Branch}   ·   {state.Changes.Count} 个变更   ·   "+(state.Commit.Length>0?state.Commit[..Math.Min(10,state.Commit.Length)]:"尚无提交")));
                source.Children.Add(ProjectText(state.Remote,11,true));
                foreach(var change in state.Changes.Take(6))source.Children.Add(ProjectText(change.Label,12,true));
                if(state.Changes.Count>6)source.Children.Add(ProjectText($"还有 {state.Changes.Count-6} 个文件，可在提交窗口查看。",11,true));
            }else source.Children.Add(ProjectText("已关联 · 点击检查变更读取当前本地状态",13,true));
        }
        var sourceActions=new WrapPanel();source.Children.Add(sourceActions);
        ProjectAction(sourceActions,"上传更新",()=>ProjectDialog(()=>SourceDialog(p)),true,writable);
        ProjectAction(sourceActions,"检查变更",()=>InspectProject(p),enabled:workspace!=null);
        ProjectAction(sourceActions,"切换 / 关联目录",()=>ProjectDialog(()=>LinkProjectDialog(p)));
        ProjectAction(sourceActions,"克隆项目",()=>ProjectDialog(()=>CloneProjectDialog(p)),enabled:api.Token.Length>0);
        if(workspace!=null){ProjectAction(sourceActions,"打开目录",()=>ProjectDialog(()=>{if(!Directory.Exists(workspace.Folder))throw new IOException("关联目录已不存在。");WindowsServices.Open(workspace.Folder);}));ProjectAction(sourceActions,"解除关联",()=>ProjectDialog(()=>{projectWorkspaces.RemoveAll(w=>w.Repository==p.FullName);projectStates.Remove(p.FullName);SaveProjects();RenderProjectDetails(p);}));}
        var versions=ProjectSection("版本与安装包");var versionActions=new WrapPanel();versions.Children.Add(versionActions);
        if(projectVersions.ContainsKey(p.FullName))versions.Children.Add(ProjectText("上次同步结果 · 点击“同步版本”核实 GitHub 上的当前版本；“同步项目”也会刷新当前项目的版本。",11,true));
        ProjectAction(versionActions,"创建发布版本",()=>ProjectDialog(()=>PublishDialog(p)),true,writable);ProjectAction(versionActions,"同步版本",()=>SyncProjectVersions(p),enabled:api.Token.Length>0);ProjectAction(versionActions,"打开 Releases",()=>ProjectDialog(()=>WindowsServices.Open("https://github.com/"+p.FullName+"/releases")));
        if(!projectVersions.TryGetValue(p.FullName,out var history))versions.Children.Add(ProjectText("同步后查看最近 30 个正式版、测试版及草稿。",12,true));
        else if(history.Count==0)versions.Children.Add(ProjectText("此项目还没有发布版本。",12,true));
        else foreach(var version in history){
            var row=new Grid{Margin=new(0,12,0,0)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var labels=new StackPanel();labels.Children.Add(ProjectText(version.DisplayVersion+"   ·   "+version.Channel,14));labels.Children.Add(ProjectText($"{version.AssetCount} 个安装包 / 附件   ·   {version.Updated.ToLocalTime():yyyy-MM-dd HH:mm}",11,true));row.Children.Add(labels);var actions=new WrapPanel();Grid.SetColumn(actions,1);row.Children.Add(actions);
            ProjectAction(actions,"日志",()=>ProjectDialog(()=>Form.Notes(this,p.Name+" · "+version.Tag,version.Body)));
            ProjectAction(actions,"管理发布包",()=>OpenReleaseManager(p,version),enabled:api.Token.Length>0);
            if(version.Draft){ProjectAction(actions,"核实状态",async()=>{await VerifyProjectVersion(p,version);},enabled:api.Token.Length>0);ProjectAction(actions,"编辑草稿",()=>OpenDraftAction(p,version,true),enabled:writable);ProjectAction(actions,"继续发布",()=>OpenDraftAction(p,version,false),enabled:writable);}
            versions.Children.Add(row);
        }
        var recent=operations.Where(o=>o.Repository.Equals(p.FullName,StringComparison.OrdinalIgnoreCase) || o.Description.StartsWith(p.FullName,StringComparison.OrdinalIgnoreCase)).OrderByDescending(o=>o.Updated).Take(4).ToArray();
        if(recent.Length>0){var activity=ProjectSection("最近任务");foreach(var op in recent)activity.Children.Add(ProjectText($"{op.Updated:MM-dd HH:mm}   {op.Kind}   ·   {op.State}",12,true));var actions=new WrapPanel();activity.Children.Add(actions);ProjectAction(actions,"查看任务",()=>ProjectDialog(()=>Tasks_Click(this,new())));}
    }
    private async void SyncProjects_Click(object sender,RoutedEventArgs e)=>await SyncProjects();
    private async Task SyncProjects()
    {
        if(api.Token.Length==0){Login_Click(this,new());return;}
        await Execute("项目同步","同步我的项目",async ct=>{var data=await api.MyProjects(ct);InvalidateProjectCaches(ProjectRules.InvalidatedProjects(myProjects,data));var names=data.Select(p=>p.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);InvalidateProjectCaches(projectVersions.Keys.Where(name=>!names.Contains(name)).ToArray());myProjects=data;projectsSynced=DateTimeOffset.Now;SaveProjects();RenderProjects();var selected=myProjects.FirstOrDefault(p=>p.FullName.Equals(selectedProject,StringComparison.OrdinalIgnoreCase));if(selected!=null){projectVersions[selected.FullName]=await api.ProjectVersions(selected.FullName,ct);SaveProjects();if(selectedProject==selected.FullName)RenderProjectDetails(selected);}model.Status=$"已同步 {data.Count} 个项目及当前项目的版本。";},resources:["projects:"+settings.Account]);
    }
    private async Task InspectProject(ManagedProject p)
    {
        var workspace=Workspace(p)??throw new IOException("请先关联本地目录。");projectStates.Remove(p.FullName);RenderProjectDetails(p);await Execute("源码检查",p.FullName,async ct=>{var state=await new GitWorkspace("").Inspect(workspace.Folder,ct);if(!ProjectRules.MatchesRemote(state.Remote,p.FullName))throw new IOException("本地 origin 与项目不一致，请重新关联正确的仓库。");RememberWorkspace(p,state);model.Status=$"{p.Name} · {state.Changes.Count} 个变更";},resources:["git:"+Path.GetFullPath(workspace.Folder)]);
    }
    private async Task SyncProjectVersions(ManagedProject p)
    {
        await Execute("版本同步",p.FullName,async ct=>{ManagedProject current;try{current=await api.Project(p.FullName,ct);}catch(GitHubNotFoundException){InvalidateProjectCaches([p.FullName]);SaveProjects();if(selectedProject==p.FullName)RenderProjectDetails(p);throw;}if(!ProjectRules.SameRepository(p,current))InvalidateProjectCaches([p.FullName]);myProjects.RemoveAll(item=>item.FullName.Equals(p.FullName,StringComparison.OrdinalIgnoreCase));myProjects.Add(current);SaveProjects();if(selectedProject==p.FullName)RenderProjectDetails(current);projectVersions[p.FullName]=await api.ProjectVersions(p.FullName,ct);SaveProjects();if(selectedProject==p.FullName)RenderProjectDetails(current);model.Status="版本与草稿已同步。";},resources:["repo:"+p.FullName]);
    }
    private void CreateProject_Click(object sender,RoutedEventArgs e)=>CreateProjectDialog();
    private void CreateProjectDialog(string? previewPath=null)
    {
        if(api.Token.Length==0){Login_Click(this,new());return;}
        var f=new Form(this,"创建项目",760,700);f.Label("所属账号："+settings.Account);var name=f.Text("项目名称");var description=f.Text("项目说明");var privateRepo=f.Check("私有项目（仅授权用户可访问）",true);var folder=f.Text("本地源码目录（可选，创建后自动准备上传）");var advanced=f.Advanced();CheckBox readme=null!;f.Into(advanced,()=>readme=f.Check("无本地源码时创建 README 初始提交"));
        f.Action("选择源码目录",()=>{var d=new OpenFolderDialog();if(d.ShowDialog(f)==true){folder.Text=d.FolderName;if(name.Text.Trim().Length==0)name.Text=Rules.SafeSegment(Path.GetFileName(d.FolderName));}return Task.CompletedTask;});
        f.Action("创建项目",async()=>{var n=name.Text.Trim();if(!Rules.ValidRepository("owner/"+n))throw new IOException("请填写有效的项目名称。");var path=folder.Text.Trim();if(path.Length>0&&!Directory.Exists(path))throw new IOException("源码目录不存在。");var d=description.Text.Trim();var privacy=privateRepo.IsChecked==true;var initialize=path.Length==0&&readme.IsChecked==true;ManagedProject? created=null;f.Close();await Execute("项目管理",settings.Account+"/"+n,async ct=>{created=await api.CreateProject(n,d,privacy,initialize,ct);currentOperation.Value!.Repository=created.FullName;InvalidateProjectCaches([created.FullName]);myProjects.RemoveAll(p=>p.FullName.Equals(created.FullName,StringComparison.OrdinalIgnoreCase));myProjects.Insert(0,created);selectedProject=created.FullName;SaveProjects();RenderProjects();model.Status="项目已创建："+created.FullName;},resources:["projects:"+settings.Account]);if(created!=null&&path.Length>0){var state=await ResolveWorkspaceRoot(this,created,path,created.DefaultBranch.Length>0?created.DefaultBranch:"main",true);if(state!=null){RememberWorkspace(created,state);SourceDialog(created);}}},true);
        if(previewPath!=null){name.Text="GitstarNext";description.Text="自己的 GitHub 项目与版本管理工具";folder.Text=@"D:\Projects\GitstarNext";f.RenderPreview(previewPath);}f.ShowDialog();
    }
    private async Task OpenProjectSettings(ManagedProject p)
    {
        RepositoryOptions? options=null;var accountKey=CacheKey;
        await Execute("项目设置读取",p.FullName,async ct=>{options=await api.ProjectOptions(p.FullName,ct);},resources:["repo:"+p.FullName]);
        if(options!=null && accountKey==CacheKey){if(!options.Project.CanAdmin){DialogMessage.Show(this,"当前账号没有仓库管理权限。","项目设置");return;}EditProjectDialog(options.Project,options:options);}
    }
    private void EditProjectDialog(ManagedProject p,string? previewPath=null,RepositoryOptions? options=null)
    {
        options??=new(p,RepositorySettings.Features.ToDictionary(f=>f.Key,f=>f.Key is "has_issues" or "has_wiki" or "has_projects" or "allow_merge_commit" or "allow_squash_merge" or "allow_rebase_merge"));
        var original=options;var f=new Form(this,"项目设置",840,830);var anchors=new Dictionary<string,FrameworkElement>();void Section(string title){f.Section(title);var heading=(TextBlock)f.Fields.Children[f.Fields.Children.Count-1];if(settings.Dark)heading.Foreground=new SolidColorBrush(Color.FromRgb(170,156,255));anchors[title]=heading;}f.Label(p.FullName+" · 当前为"+p.Visibility+" · "+p.Access);Section("项目资料");var description=f.Text("项目说明",p.Description);var homepage=f.Text("项目主页",p.Homepage);description.IsReadOnly=homepage.IsReadOnly=p.Archived;
        Section("访问与分支");var visibilityNames=new List<string>{"公开 · 所有人可查看","私有 · 仅授权用户可访问"};if(p.RepoVisibility=="internal")visibilityNames.Add("内部 · 企业成员可查看");var visibility=f.Select("仓库可见性",visibilityNames,p.RepoVisibility=="internal"?2:p.Private?1:0);
        var canChange=p.CanAdmin&&!p.Archived&&!p.Fork&&p.RepoVisibility!="internal";visibility.IsEnabled=canChange;
        f.Label(p.Fork?"Fork 的可见性由仓库网络管理，请到 GitHub 设置中处理。":p.RepoVisibility=="internal"?"此仓库为企业内部可见性，请到 GitHub 设置中处理。":"更改可见性需管理员权限，也可能受组织策略限制。");
        var impact=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,0,0,12)};impact.SetResourceReference(TextBlock.ForegroundProperty,"Muted");f.Fields.Children.Add(impact);
        void Describe(){var requestedPrivate=visibility.SelectedIndex==1;impact.Text=!canChange?"保存资料不会改变当前可见性。":requestedPrivate==p.Private?"保存其他设置不会改变当前可见性。":requestedPrivate?"公开 → 私有：限制仓库访问；现有公开 Fork 仍保持公开。GitHub 会清除 Star / Watch，部分功能可能变化。":"私有 → 公开：代码、提交历史及 Actions 历史和日志将向所有人开放。GitHub 会清除 Star / Watch，并停用推送规则集。";}
        visibility.SelectionChanged+=(_,_)=>Describe();Describe();var branch=f.Text("默认分支（填写 GitHub 上已存在的分支）",p.DefaultBranch);branch.IsReadOnly=p.Archived;
        var controls=new Dictionary<string,CheckBox>();
        foreach(var section in new[]{("功能","仓库功能"),("合并","Pull Request 与合并"),("贡献","贡献规则")}){
            Section(section.Item2);var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};f.Fields.Children.Add(grid);
            foreach(var feature in RepositorySettings.Features.Where(x=>x.Section==section.Item1)){
                var known=options.Features.TryGetValue(feature.Key,out var value);var check=f.Check(feature.Label,value);f.Fields.Children.Remove(check);grid.Children.Add(check);check.IsEnabled=known&&!p.Archived;
                if(feature.Key=="allow_forking")check.IsEnabled&=p.Private;
                if(!known)check.ToolTip="当前 GitHub 接口未提供此设置，保持不变。";controls[feature.Key]=check;
            }
        }
        f.Label("自动合并、Wiki、私有 Fork 等功能受仓库类型、订阅与组织策略限制；GitHub 会校验是否允许修改。");
        Section("仓库状态");var archived=f.Check("归档项目（仓库只读）",p.Archived);f.Label(p.Archived?"当前已归档，可先取消归档，随后重新打开设置修改其他选项。":"归档后无法提交源码或发布版本；需要时可以取消归档。");
        f.Action("协作者 / 团队权限",()=>ProjectDialog(()=>WindowsServices.Open("https://github.com/"+p.FullName+"/settings/access")));
        f.Action("分支保护 / 规则",()=>ProjectDialog(()=>WindowsServices.Open("https://github.com/"+p.FullName+"/settings/branches")));
        f.Action("更多 GitHub 设置",()=>ProjectDialog(()=>WindowsServices.Open("https://github.com/"+p.FullName+"/settings")));
        f.Action("可见性说明",()=>ProjectDialog(()=>WindowsServices.Open("https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility")));
        f.Action("保存设置",async()=>{
            var d=p.Archived?p.Description:description.Text.Trim();var h=p.Archived?p.Homepage:homepage.Text.Trim();if(h.Length>0 && (!Uri.TryCreate(h,UriKind.Absolute,out var url)||url.Scheme is not ("https" or "http")))throw new IOException("项目主页需填写有效的 HTTP / HTTPS 地址。");
            var requestedPrivate=canChange?visibility.SelectedIndex==1:p.Private;var selectedBranch=p.Archived?p.DefaultBranch:branch.Text.Trim();var archive=archived.IsChecked==true;var features=controls.Where(x=>original.Features.ContainsKey(x.Key)).ToDictionary(x=>x.Key,x=>x.Value.IsChecked==true);
            var patch=RepositorySettings.Patch(original,d,h,selectedBranch,requestedPrivate,archive,features);if(patch.Count==0){f.Close();return;}
            var effects=new List<string>();if(patch.ContainsKey("private"))effects.Add(impact.Text);if(patch.ContainsKey("archived"))effects.Add(archive?"将归档为只读，源码上传与版本发布将停止。":"将取消归档，恢复仓库的写入功能。");
            if(effects.Count>0){var confirm=new Form(f,"确认项目设置变更",660,560);confirm.Label(p.FullName+"\n\n"+string.Join("\n\n",effects));var name=confirm.Text("输入完整仓库名称以确认","");var approved=false;confirm.Action("取消",()=>ProjectDialog(confirm.Close));confirm.Action("确认变更",()=>{if(!name.Text.Trim().Equals(p.FullName,StringComparison.Ordinal))throw new IOException("仓库名称不匹配。");approved=true;confirm.Close();return Task.CompletedTask;},true);confirm.ShowDialog();if(!approved)return;}
            f.Close();await Execute("项目管理",p.FullName,async ct=>{var updated=await api.UpdateProjectOptions(original,d,h,selectedBranch,requestedPrivate,archive,features,ct);myProjects.RemoveAll(x=>x.FullName==p.FullName);myProjects.Add(updated);SaveProjects();RenderProjects();model.Status="项目设置已保存："+updated.FullName+" · "+updated.Visibility+" · "+updated.Access;},resources:["repo:"+p.FullName]);
        },true);
        void Jump(FrameworkElement anchor){var scroller=((Grid)f.Content).Children.OfType<Border>().Select(b=>b.Child).OfType<ScrollViewer>().First();scroller.ScrollToVerticalOffset(anchor.TransformToAncestor(f.Fields).Transform(new Point(0,0)).Y);}
        var navigation=new WrapPanel{Margin=new Thickness(18,10,18,0)};foreach(var anchor in anchors){var b=new Button{Content=anchor.Key,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,4)};b.Click+=(_,_)=>Jump(anchor.Value);navigation.Children.Add(b);}var layout=(Grid)f.Content;foreach(UIElement child in layout.Children)if(Grid.GetRow(child)>0)Grid.SetRow(child,Grid.GetRow(child)+1);layout.RowDefinitions.Insert(1,new RowDefinition{Height=GridLength.Auto});Grid.SetRow(navigation,1);layout.Children.Add(navigation);
        if(previewPath!=null){if(Environment.GetCommandLineArgs().Contains("--settings-bottom"))f.ContentRendered+=(_,_)=>Jump(anchors["Pull Request 与合并"]);f.RenderPreview(previewPath);}f.ShowDialog();
    }
    private void LinkProjectDialog(ManagedProject p)
    {
        var f=new Form(this,"源码目录",740,530);f.Label("目标项目："+p.FullName);var folder=f.Text("源码目录",Workspace(p)?.Folder??"");f.Label("选择后自动检测 Git 根目录、关联状态与初始化需求。");var advanced=f.Advanced();TextBox branch=null!;f.Into(advanced,()=>branch=f.Text("初始化分支",p.DefaultBranch.Length>0?p.DefaultBranch:"main"));
        f.Action("选择目录",()=>{var d=new OpenFolderDialog();if(d.ShowDialog(f)==true)folder.Text=d.FolderName;return Task.CompletedTask;});
        f.Action("使用此目录",async()=>{var state=await ResolveWorkspaceRoot(f,p,folder.Text,branch.Text.Trim(),true);if(state==null)return;RememberWorkspace(p,state);model.Status="已切换源码根目录："+state.Folder;f.Close();},true);f.ShowDialog();
    }
    private void CloneProjectDialog(ManagedProject p)
    {
        var f=new Form(this,"克隆项目",720,480);f.Label(p.FullName);var folder=f.Text("目标目录",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"source",p.Name));f.Label("目标目录必须不存在或为空；上级文件夹需存在。");
        f.Action("选择上级目录",()=>{var d=new OpenFolderDialog();if(d.ShowDialog(f)==true)folder.Text=Path.Combine(d.FolderName,p.Name);return Task.CompletedTask;});
        f.Action("克隆并关联",async()=>{var path=Path.GetFullPath(folder.Text);f.Close();await Execute("克隆",p.FullName,async ct=>{await api.EnsureFreshToken(ct);var git=new GitWorkspace(api.Token);await git.Clone(p.FullName,path,ct);RememberWorkspace(p,await git.Inspect(path,ct));model.Status="项目已克隆并关联。";},resources:["repo:"+p.FullName,"git:"+path]);},true);f.ShowDialog();
    }
    private void EditDraftDialog(ManagedProject p,ProjectVersion v)
    {
        var f=new Form(this,"编辑版本草稿",760,650);f.Label(p.FullName);var tag=f.Text("版本号",SuggestedReleaseTag(p,v));f.Label("支持 1.0.0.0；这是 GitHub 版本标签，安装包文件名无需包含版本号。");var name=f.Text("版本名称",ReleaseRules.TemporaryTag(v.Tag)&&Rules.CompareVersions(v.Name,v.Name)==null?tag.Text:v.Name);var beta=f.Check("测试版",v.Prerelease);var notes=f.Text("更新日志 Markdown",v.Body,true);
        f.Action("保存草稿",async()=>{var t=tag.Text.Trim();if(!ReleaseRules.ValidTag(t))throw new IOException("请填写有效版本号，例如 1.0.0.0。");var n=name.Text.Trim();var body=notes.Text;var pre=beta.IsChecked==true;f.Close();await Execute("项目管理",p.FullName,async ct=>{await api.EditDraft(p.FullName,v.Id,n,body,pre,ct,t);RememberProjectVersion(p.FullName,await api.ProjectVersionDetails(p.FullName,v.Id,ct));},resources:["repo:"+p.FullName]);},true);f.ShowDialog();
    }
}
