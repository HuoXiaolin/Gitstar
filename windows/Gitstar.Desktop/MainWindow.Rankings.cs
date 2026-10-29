using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Gitstar.Desktop;
public partial class MainWindow
{
    private bool rankingsPage,rankingControls;
    private RankingKind rankingKind=RankingKind.Trending;
    private int rankingPeriod,rankingPage=1,rankingGeneration;
    private string rankingQuery="";
    private RankingPage? rankingData;
    private CancellationTokenSource? rankingRequest;
    private StackPanel rankingCards=null!;
    private TextBlock rankingHint=null!,rankingPageLabel=null!;
    private TextBox rankingSearch=null!;
    private ComboBox rankingPeriodChoice=null!;
    private Button rankingPrevious=null!,rankingNext=null!,rankingRefresh=null!,rankingReturn=null!;
    private readonly List<Button> rankingTabs=[];
    private void InitializeRankings()
    {
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());grid.RowDefinitions.Add(new(){Height=GridLength.Auto});RankingsHost.Children.Add(grid);
        var toolbar=new DockPanel{Margin=new Thickness(0,0,0,10)};grid.Children.Add(toolbar);
        rankingRefresh=new Button{Content="刷新榜单",Margin=new Thickness(8,0,0,0)};rankingRefresh.Click+=async(_,_)=>await LoadRankings(true);DockPanel.SetDock(rankingRefresh,Dock.Right);toolbar.Children.Add(rankingRefresh);
        rankingPeriodChoice=new ComboBox{MinWidth=108,Margin=new Thickness(8,0,0,0)};rankingPeriodChoice.SelectionChanged+=async(_,_)=>{if(rankingControls||rankingPeriodChoice.SelectedIndex<0)return;rankingPeriod=rankingPeriodChoice.SelectedIndex;rankingPage=1;rankingData=null;await LoadRankings();};DockPanel.SetDock(rankingPeriodChoice,Dock.Right);toolbar.Children.Add(rankingPeriodChoice);
        var tabs=new StackPanel{Orientation=Orientation.Horizontal};toolbar.Children.Add(tabs);foreach(var kind in new[]{RankingKind.Trending,RankingKind.New,RankingKind.Popular}){var button=new Button{Content=kind switch{RankingKind.Trending=>"趋势",RankingKind.New=>"新发布",_=>"热门"},Tag=kind,Margin=new Thickness(0,0,8,0),Padding=new Thickness(18,9,18,9)};button.Click+=async(_,_)=>{rankingKind=kind;rankingPage=1;rankingPeriod=kind==RankingKind.New?1:0;rankingQuery="";rankingSearch.Clear();rankingData=null;await LoadRankings();};tabs.Children.Add(button);rankingTabs.Add(button);}
        var searchRow=new DockPanel{Margin=new Thickness(0,0,0,10)};Grid.SetRow(searchRow,1);grid.Children.Add(searchRow);var find=new Button{Content="搜索项目",Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(find,Dock.Right);searchRow.Children.Add(find);var clear=new Button{Content="返回榜单",Margin=new Thickness(8,0,0,0)};rankingReturn=clear;DockPanel.SetDock(clear,Dock.Right);searchRow.Children.Add(clear);rankingSearch=new TextBox{MinHeight=38,ToolTip="输入项目名称、说明关键词或 GitHub 仓库地址，搜索公开项目"};var input=new Grid();input.Children.Add(rankingSearch);var placeholder=new TextBlock{Text="搜索项目名称、介绍关键词或 GitHub 仓库地址",Margin=new Thickness(12,0,8,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false,FontSize=12};placeholder.SetResourceReference(TextBlock.ForegroundProperty,"Muted");input.Children.Add(placeholder);rankingSearch.TextChanged+=(_,_)=>placeholder.Visibility=rankingSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;searchRow.Children.Add(input);
        async Task Search(){var text=rankingSearch.Text.Trim();if(text.Length<2||text.Length>120){model.Status="请输入 2 至 120 个字符的项目关键词。";return;}rankingQuery=text;rankingKind=RankingKind.Search;rankingPage=1;rankingData=null;await LoadRankings();}
        find.Click+=async(_,_)=>await Search();rankingSearch.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Search();}};clear.Click+=async(_,_)=>{rankingKind=RankingKind.Trending;rankingPeriod=0;rankingPage=1;rankingQuery="";rankingSearch.Clear();rankingData=null;await LoadRankings();};
        rankingCards=new StackPanel();var viewport=new ScrollViewer{Content=rankingCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(viewport,2);grid.Children.Add(viewport);
        var footer=new DockPanel{Margin=new Thickness(0,9,0,0)};Grid.SetRow(footer,3);grid.Children.Add(footer);var pages=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(pages,Dock.Right);footer.Children.Add(pages);rankingPrevious=new Button{Content="上一页",Margin=new Thickness(4,0,0,0)};rankingNext=new Button{Content="下一页",Margin=new Thickness(4,0,0,0)};rankingPageLabel=ProjectText("",12,true);rankingPageLabel.VerticalAlignment=VerticalAlignment.Center;rankingPageLabel.Margin=new Thickness(10,0,10,0);pages.Children.Add(rankingPrevious);pages.Children.Add(rankingPageLabel);pages.Children.Add(rankingNext);var browser=new Button{Content="在 GitHub 查看",Margin=new Thickness(8,0,0,0)};pages.Children.Add(browser);browser.Click+=(_,_)=>{var url=rankingKind==RankingKind.Trending?"https://github.com/trending?since="+RepositoryRankings.Period(rankingPeriod):"https://github.com/search?type=repositories&q="+Uri.EscapeDataString((RepositoryRankings.SearchQuery(rankingKind,rankingPeriod,rankingQuery,DateTimeOffset.UtcNow)+(rankingData?.StarsFloor>0?" stars:>="+rankingData.StarsFloor:"")))+(rankingKind==RankingKind.Search?"":"&s=stars&o=desc");WindowsServices.Open(url);};rankingPrevious.Click+=async(_,_)=>{rankingPage--;rankingData=null;await LoadRankings();};rankingNext.Click+=async(_,_)=>{rankingPage++;rankingData=null;await LoadRankings();};rankingHint=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};rankingHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");footer.Children.Add(rankingHint);
    }
    private async void Rankings_Click(object sender,RoutedEventArgs e)
    {
        rankingsPage=true;projectsPage=false;localPage=false;model.SelectionMode=false;UpdatesToolbar.Visibility=UpdatesContent.Visibility=ProjectsHost.Visibility=Visibility.Collapsed;RankingsHost.Visibility=Visibility.Visible;SetPageAccent(3);UseFeature(UsageFeature.Rankings);await LoadRankings();
    }
    private void LeaveRankings(){rankingsPage=false;RankingsHost.Visibility=Visibility.Collapsed;rankingGeneration++;rankingRequest?.Cancel();}
    private async Task LoadRankings(bool force=false)
    {
        if(!rankingsPage||quitting)return;var generation=++rankingGeneration;rankingRequest?.Cancel();using var request=new CancellationTokenSource();rankingRequest=request;var account=CacheKey;var client=api;var kind=rankingKind;var period=rankingPeriod;var page=rankingPage;var query=rankingQuery;
        var key=$"rankings/v1/{kind}/{period}";
        if(rankingData==null&&kind!=RankingKind.Search&&page==1)try{var cached=store.Get<RankingPage?>(key,null);if(cached?.Kind==kind&&cached.Period==period&&cached.Page==page)rankingData=cached;}catch(Exception error)when(error is JsonException or IOException){}
        RenderRankings();if(!force&&rankingData is {} fresh&&fresh.Kind==kind&&fresh.Period==period&&fresh.Page==page&&!fresh.Incomplete&&DateTimeOffset.UtcNow-fresh.Fetched is var age&&age>=TimeSpan.Zero&&age<TimeSpan.FromHours(1)){rankingRequest=null;rankingRefresh.IsEnabled=true;return;}
        rankingRefresh.IsEnabled=rankingPrevious.IsEnabled=rankingNext.IsEnabled=false;rankingHint.Text=rankingData==null?"正在读取 GitHub 项目…":"正在刷新，暂时显示上次结果…";
        try{var data=await client.Rankings(kind,period,page,query,request.Token);if(request.IsCancellationRequested||generation!=rankingGeneration||account!=CacheKey||!rankingsPage||quitting)return;rankingData=data;if(kind!=RankingKind.Search&&page==1)store.Set(key,data);RenderRankings();}
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch(Exception error){if(generation==rankingGeneration&&rankingsPage&&!quitting){RenderRankings();rankingHint.Text="读取未完成："+error.Message+(rankingData!=null?" 已保留上次结果。":" 可点击刷新重试。");}}
        finally{if(ReferenceEquals(rankingRequest,request)){rankingRequest=null;rankingRefresh.IsEnabled=true;rankingPrevious.IsEnabled=rankingPage>1;rankingNext.IsEnabled=rankingData?.More==true;}}
    }
    private void RenderRankings()
    {
        if(!rankingsPage||rankingCards==null||IsInBackground)return;
        model.Title=rankingKind==RankingKind.Search?"搜索公开项目":"GitHub 项目排行榜";model.Subtitle=rankingKind switch{RankingKind.Trending=>"趋势：GitHub 官方 Trending · "+new[]{"今日","本周","本月"}[rankingPeriod],RankingKind.New=>$"新发布：最近 {RepositoryRankings.NewDays(rankingPeriod)} 天新建的公开项目，按 Stars 排序。",RankingKind.Popular=>"热门：公开项目按累计 Stars 排序。",_=>"按名称与项目介绍检索 GitHub 公开仓库，结果按相关度排序。"};model.CountCaption=rankingKind==RankingKind.Search?"搜索结果":"当前榜单";model.Count=$"{rankingData?.Items.Count??0} 个项目";
        rankingReturn.Visibility=rankingKind==RankingKind.Search?Visibility.Visible:Visibility.Collapsed;rankingControls=true;rankingPeriodChoice.ItemsSource=rankingKind==RankingKind.New?new[]{"近 7 天","近 30 天","近 90 天"}:new[]{"今日","本周","本月"};rankingPeriodChoice.SelectedIndex=rankingPeriod;rankingPeriodChoice.Visibility=rankingKind is RankingKind.Trending or RankingKind.New?Visibility.Visible:Visibility.Collapsed;rankingControls=false;
        foreach(var button in rankingTabs){var selected=(RankingKind)button.Tag==rankingKind;button.SetResourceReference(Control.BackgroundProperty,selected?"AccentSoft":"Surface");button.SetResourceReference(Control.ForegroundProperty,selected?"AccentInk":"Ink");}
        rankingCards.Children.Clear();if(rankingData==null||rankingData.Items.Count==0)rankingCards.Children.Add(ProjectText(rankingKind==RankingKind.Search?"没有匹配的公开项目，可换个关键词再搜索。":"暂未读取到榜单，点击刷新获取项目。",15,true));
        foreach(var item in rankingData?.Items??[]){
            var repo=item.Repository;var starred=repositories.Any(known=>known.Starred&&known.FullName.Equals(repo.FullName,StringComparison.OrdinalIgnoreCase));var local=HasLocalFollow(repo.FullName);
            var card=new Grid();card.ColumnDefinitions.Add(new(){Width=new GridLength(58)});card.ColumnDefinitions.Add(new());card.ColumnDefinitions.Add(new(){Width=new GridLength(154)});var leading=new StackPanel{Margin=new Thickness(0,3,14,0),HorizontalAlignment=HorizontalAlignment.Center};leading.Children.Add(new OwnerAvatar{Repository=repo.FullName});var ordinal=ProjectText(item.Position.ToString("00"),12,true);ordinal.HorizontalAlignment=HorizontalAlignment.Center;ordinal.Margin=new Thickness(0,8,0,0);leading.Children.Add(ordinal);card.Children.Add(leading);
            var content=new StackPanel{Margin=new Thickness(0,0,16,0)};Grid.SetColumn(content,1);card.Children.Add(content);var title=ProjectText(repo.FullName.Split('/').Last(),19);title.FontWeight=FontWeights.SemiBold;content.Children.Add(title);
            content.Children.Add(RankingAddressRow(repo.FullName));
            content.Children.Add(ProjectText(string.IsNullOrWhiteSpace(repo.Description)?"作者暂未填写项目介绍。":repo.Description,13));var facts=new List<string>();if(item.Language.Length>0)facts.Add(item.Language);if(repo.Metadata.Stars is {} stars)facts.Add($"★ {stars:N0}");if(item.GainedStars is {} gained)facts.Add($"本期新增 {gained:N0} Stars");if(item.Created is {} created)facts.Add("创建于 "+created.ToLocalTime().ToString("yyyy-MM-dd"));content.Children.Add(ProjectText(string.Join("  ·  ",facts),12,true));
            var actions=new StackPanel();Grid.SetColumn(actions,2);card.Children.Add(actions);ProjectAction(actions,starred?"GitHub 已关注":local?"已本地关注":"本地关注",()=>{FollowRankingLocal(repo);return Task.CompletedTask;},enabled:!starred&&!local);
            ProjectAction(actions,starred?"★ 已 Star":"☆ Star",()=>FollowRankingStar(repo),primary:!starred,enabled:!starred);
            ProjectAction(actions,"关注与分组",()=>{FollowManagementDialog(repo.FullName);RenderRankings();return Task.CompletedTask;});var border=new Border{Child=card,CornerRadius=new(13),BorderThickness=new(1),Padding=new(18),Margin=new(0,0,0,12)};border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Edge");rankingCards.Children.Add(border);
        }
        rankingPrevious.IsEnabled=rankingPage>1;rankingNext.IsEnabled=rankingData?.More==true;rankingPageLabel.Text=$"第 {rankingPage} 页";rankingHint.Text=rankingData==null?"趋势来自 GitHub Trending，其他榜单与搜索来自 GitHub Search。":$"GitHub · {rankingData.Fetched.ToLocalTime():MM-dd HH:mm} 更新"+(rankingKind==RankingKind.Search?$" · 共 {rankingData.Total:N0} 项（最多浏览前 1000 项）":rankingKind==RankingKind.Trending?"":" · 按 Stars 排序，最多浏览前 1000 项")+(rankingData.Incomplete?" · 返回结果可能不完整，可刷新重试。":"");
    }
    private DockPanel RankingAddressRow(string repository)
    {
        var row=new DockPanel{HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,0,8)};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,0,0)};DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
        foreach(var copy in new[]{true,false}){
            var title=copy?"复制项目地址":"打开项目主页";var button=new Button{ToolTip=title,Style=(Style)FindResource("ProjectAddressButton")};System.Windows.Automation.AutomationProperties.SetName(button,title);
            var icon=new System.Windows.Shapes.Path{Data=Geometry.Parse(copy?"M 5,2 L 14,2 L 14,11 M 2,5 L 11,5 L 11,14 L 2,14 Z":"M 8,2 L 14,2 L 14,8 M 14,2 L 7,9 M 5,3 L 2,3 L 2,14 L 13,14 L 13,11"),StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round,Stretch=Stretch.Uniform,Width=13,Height=13};icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=button});button.Content=icon;
            button.Click+=(_,_)=>{if(copy){Clipboard.SetText("https://github.com/"+repository);model.Status="已复制项目地址。";}else WindowsServices.Open("https://github.com/"+repository);};actions.Children.Add(button);
        }
        var address=new TextBlock{Text=repository,ToolTip=repository,FontSize=12,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};address.SetResourceReference(TextBlock.ForegroundProperty,"Muted");row.Children.Add(address);return row;
    }
    private void FollowRankingLocal(Repository repo)
    {
        if(!repositories.Any(known=>known.FullName.Equals(repo.FullName,StringComparison.OrdinalIgnoreCase)))PutRepo(repo);SetLocalFollow(repo.FullName,true,false);RebuildGroups();RenderRankings();model.Status="已加入本地关注，可检查更新获取发行版本。";
    }
    private async Task FollowRankingStar(Repository repo)
    {
        if(!ManageReady(true))return;await Execute("Star管理","Star · "+repo.FullName,async ct=>{await SetManagedStar(repo.FullName,true,ct);RebuildGroups();model.Status="GitHub Star 已保存，可在关注与分组中管理。";},resources:["repo:"+repo.FullName]);RenderRankings();
    }
    private async Task RankingsSmoke(string path)
    {
        rankingsPage=true;projectsPage=false;localPage=false;UpdatesToolbar.Visibility=UpdatesContent.Visibility=ProjectsHost.Visibility=Visibility.Collapsed;RankingsHost.Visibility=Visibility.Visible;SetPageAccent(3);
        var examples=new[]{new RankingEntry(new("example/desk-tools","一套简洁的桌面工具，让日常窗口管理、文件处理和快捷操作更顺手。",false){Metadata=new(25431,"MIT",null)},1,"C#",512),new RankingEntry(new("sample/local-notes","Local-first notes with Markdown, fast search and synchronization. 项目介绍显示在这里。",false){Metadata=new(12830,"Apache-2.0",null)},2,"TypeScript",231),new RankingEntry(new("demo/industrial-view","用于设备状态展示与数据分析的可视化工具，提供图表和可配置的工作区。",false){Metadata=new(7621,"GPL-3.0",null)},3,"Python",128)};
        settings.Account="example-user";AccountLabel();var transport=new RankingSmokeTransport();api.Dispose();api=new(settings,transport){Token="fixture"};
        rankingData=new(RankingKind.Trending,0,1,DateTimeOffset.UtcNow,examples.ToList(),3,false);RenderRankings();FollowRankingLocal(examples[0].Repository);var localSaved=settings.ExtraRepositories.Contains("example/desk-tools");await FollowRankingStar(examples[0].Repository);var starSaved=transport.Starred&&repositories.Any(repo=>repo.FullName=="example/desk-tools"&&repo.Starred);var precedence=!FollowLists.Local(repositories,settings.ExtraRepositories,settings.HiddenRepositories).Contains("example/desk-tools");
        rankingKind=RankingKind.Search;rankingQuery="desk";rankingData=null;await LoadRankings(true);var searchWorks=rankingData?.Kind==RankingKind.Search&&rankingData.Items.Count==1&&rankingData.Items[0].Repository.Description=="Search result project introduction";
        Page(false);var left=!rankingsPage&&RankingsHost.Visibility==Visibility.Collapsed;Projects_Click(this,new RoutedEventArgs());var projects=!rankingsPage&&projectsPage;rankingsPage=true;projectsPage=false;UpdatesToolbar.Visibility=UpdatesContent.Visibility=ProjectsHost.Visibility=Visibility.Collapsed;RankingsHost.Visibility=Visibility.Visible;rankingKind=RankingKind.Trending;rankingPeriod=0;rankingData=new(RankingKind.Trending,0,1,DateTimeOffset.UtcNow,examples.ToList(),3,false);SetPageAccent(3);RenderRankings();if(!localSaved||!starSaved||!precedence||!searchWorks||!left||!projects)throw new IOException("Rankings interaction regression");
        UpdateLayout();var avatarCount=rankingCards.Children.OfType<Border>().Select(border=>(Grid)border.Child).SelectMany(card=>card.Children.OfType<StackPanel>().Where(panel=>Grid.GetColumn(panel)==0)).SelectMany(panel=>panel.Children.OfType<OwnerAvatar>()).Count();var iconCount=0;var aligned=true;
        foreach(var card in rankingCards.Children.OfType<Border>().Select(border=>(Grid)border.Child)){var content=card.Children.OfType<StackPanel>().Single(panel=>Grid.GetColumn(panel)==1);var address=content.Children.OfType<DockPanel>().Single();var text=address.Children.OfType<TextBlock>().Single();var center=text.TranslatePoint(new Point(0,text.ActualHeight/2),address).Y;foreach(var button in address.Children.OfType<StackPanel>().Single().Children.OfType<Button>()){if(button.Content is System.Windows.Shapes.Path)iconCount++;aligned&=Math.Abs(button.TranslatePoint(new Point(0,button.ActualHeight/2),address).Y-center)<=1;}}
        if(avatarCount!=3||iconCount!=6||!aligned)throw new IOException("Ranking avatar or address icon alignment regression");
        File.WriteAllText(Path.ChangeExtension(path,".rankings.json"),JsonSerializer.Serialize(new{AvatarCount=avatarCount,VectorIconCount=iconCount,AddressIconsAligned=aligned,LocalFollowSaved=localSaved,StarSaved=starSaved,GitHubFollowPrecedence=precedence,SearchVerified=searchWorks,NavigationVerified=left&&projects,TopLevelVisible=RankingsButton.IsVisible,OnlyIntroductions=rankingCards.Children.Count==3,SearchAvailable=rankingSearch.IsVisible,FollowAndStar=true}));
        _=Dispatcher.BeginInvoke(()=>{UpdateLayout();var root=(FrameworkElement)Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);exit=true;tray.Dispose();api.Dispose();store.Dispose();Application.Current.Shutdown();},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
