using Gitstar.Core;
using Gitstar.Desktop;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.IO.Compression;

try {
if(args.FirstOrDefault()=="--portable-process-fixture"&&args.Length==2){File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(new{Pid=Environment.ProcessId,WorkingDirectory=Environment.CurrentDirectory}));await Task.Delay(TimeSpan.FromSeconds(45));return;}
if(args.FirstOrDefault()=="--set-binding-program"&&args.Length==3){
    using var activation=new AppActivation(null);if(!activation.IsPrimary)throw new IOException("请先从 Gitstar 托盘彻底退出，避免覆盖正在修改的设置。");
    var repository=Rules.RepositoryName(args[1]);if(!Rules.ValidRepository(repository))throw new IOException("仓库地址无效。");
    var path=Path.GetFullPath(args[2]);if(!File.Exists(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("主程序路径不存在。");
    using var db=new LocalStore();var settings=db.Get("settings",new Settings());var selected=settings.Bindings.Where(binding=>binding.Repository.Equals(repository,StringComparison.OrdinalIgnoreCase)).ToList();
    if(selected.Count==0)throw new IOException("未找到已有软件关联。");
    if(selected.Select(binding=>binding.ExecutablePath).Where(value=>value.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1)throw new IOException("此仓库关联了多个安装位置，请在软件管理中逐项选择。");
    var cacheKey="cache/"+(settings.Account.Length>0?settings.Account:"guest")+"/installed";var old=db.Get(cacheKey,new InstalledSnapshot(DateTimeOffset.MinValue,[]));var oldIds=selected.Select(binding=>binding.AppId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach(var entry in selected){entry.ExecutablePath=path;entry.ConfirmedVersion="";entry.ConfirmedVersionPath="";entry.ConfirmedVersionHash="";}
    var focus=new Settings{Bindings=selected};var detected=WindowsServices.ScanMatched(selected,settings.PortableApps,old.Apps);var repair=InstalledIdentity.Reconcile(focus,detected,old.Apps);
    var confirmed=focus.Bindings.Select(binding=>InstalledVersion.Find(repair.Apps,binding.AppId)).ToArray();
    if(confirmed.Any(app=>app==null||!app.ExecutablePath.Equals(path,StringComparison.OrdinalIgnoreCase)||app.Version=="未知"))throw new IOException("无法核对所选位置的实际程序版本，未保存。");
    settings.Bindings.RemoveAll(binding=>binding.Repository.Equals(repository,StringComparison.OrdinalIgnoreCase));settings.Bindings.AddRange(focus.Bindings);
    var replacedIds=repair.Apps.SelectMany(app=>(app.AliasIds??[]).Append(app.Id)).Concat(oldIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var combined=old.Apps.Where(app=>!replacedIds.Contains(app.Id)).Concat(repair.Apps);
    db.Set("settings",settings);db.Set(cacheKey,InstalledSnapshot.Capture(combined,settings.Bindings));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Repository=repository,Path=path,Version=confirmed[0]!.Version,Bindings=focus.Bindings.Count}));return;
}
if(args.FirstOrDefault()=="--inspect-installed-identity"){
    using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(args[1]));
    var settings=System.Text.Json.JsonSerializer.Deserialize<Settings>(document.RootElement.GetProperty("Settings"))!;
    var known=System.Text.Json.JsonSerializer.Deserialize<List<InstalledApp>>(document.RootElement.GetProperty("Apps"))!;
    var before=settings.Bindings.Count;var detected=WindowsServices.ScanMatched(settings.Bindings,settings.PortableApps,known);
    var repair=InstalledIdentity.Reconcile(settings,detected,known);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Before=before,After=settings.Bindings.Count,repair.Migrated,repair.Merged,Programs=settings.Bindings.Select(binding=>new{binding.Repository,App=InstalledVersion.Find(repair.Apps,binding.AppId)?.Name,Version=InstalledVersion.Find(repair.Apps,binding.AppId)?.Version,NeedsReview=InstalledVersion.Find(repair.Apps,binding.AppId)?.VersionNeedsReview,Details=InstalledVersion.Find(repair.Apps,binding.AppId)?.VersionDetails,ProgramPath=binding.ExecutablePath,HasProgramPath=binding.ExecutablePath.Length>0,binding.Disabled})}));return;
}
if(args.FirstOrDefault()=="--inspect-git-auth"){
    var repository=args[1];GitHubClient.Validate(repository);using var db=new LocalStore();using var client=new GitHubClient(db.Get("settings",new Settings()));client.Token=CredentialStorage.Load(db).AccessToken;if(client.Token.Length==0)throw new IOException("应用没有已保存的登录授权。");
    var project=await client.Project(repository,CancellationToken.None);var git=new GitWorkspace(client.Token);var start=git.CreateStartInfo(Environment.CurrentDirectory,"ls-remote","https://github.com/"+repository+".git","refs/heads/"+project.DefaultBranch);start.Environment["GIT_ASKPASS"]=args[2];
    using var process=System.Diagnostics.Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch{try{process.Kill(true);}catch{}throw;}
    var gitOutput=await output;await errors;Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Project=project.FullName,project.Private,project.CanPush,GitReadSucceeded=process.ExitCode==0,BranchConfirmed=gitOutput.Contains("refs/heads/"+project.DefaultBranch)}));return;
}
if(args.FirstOrDefault()=="--inspect-release"){
    using var db=new LocalStore();using var client=new GitHubClient(db.Get("settings",new Settings()));client.Token=CredentialStorage.Load(db).AccessToken;if(client.Token.Length==0)throw new IOException("请先在 Gitstar 登录。");
    var release=await client.ReleaseDetails(args[1],long.Parse(args[2]),CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Id=release.GetProperty("id").GetInt64(),Tag=release.GetProperty("tag_name").GetString(),Name=release.GetProperty("name").GetString(),Draft=release.GetProperty("draft").GetBoolean(),Assets=release.GetProperty("assets").EnumerateArray().Select(asset=>new{Id=asset.GetProperty("id").GetInt64(),Name=asset.GetProperty("name").GetString(),Size=asset.GetProperty("size").GetInt64()})}));return;
}
if(args.FirstOrDefault()=="--inspect-msi"){Console.WriteLine(WindowsServices.MsiPlatform(args[1]));return;}
if(args.FirstOrDefault()=="--inspect-feed"){
    var repository=args[1];GitHubClient.Validate(repository);using var db=new LocalStore();using var client=new GitHubClient(db.Get("settings",new Settings()));client.Token=CredentialStorage.Load(db).AccessToken;
    var pages=new Dictionary<string,GitHubClient.ReleasePage>();client.ReadReleasePage=key=>pages.GetValueOrDefault(key);client.SaveReleasePage=(key,page)=>pages[key]=page;
    using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var releases=await client.Releases(repository,timeout.Token);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Repository=repository,Stable=Rules.SelectRelease(releases,new Binding())?.Tag,Combined=Rules.SelectRelease(releases,new Binding{IncludePrerelease=true})?.Tag,PageCount=pages.Count,RetainedCandidates=releases.Select(r=>new{r.Tag,r.Prerelease,r.IsLatest,r.Published})}));return;
}
if(args.Contains("--scan-installed")){var scanned=WindowsServices.Scan();foreach(var entry in scanned.Where(a=>a.Name.Equals("Lertaro",StringComparison.OrdinalIgnoreCase)))Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(entry));return;}
var count=0;
void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
StartupAndReminderChecks.Run(Check);
BindingManagementChecks.Run(Check);
FollowHistoryChecks.Run(Check);
ProgramVersion Executable(string name,string product,string version,string fileVersion="")=>new(Path.Combine("C:\\Apps\\Example",name),product,product,version,fileVersion);
var mainExe=Executable("Example.App.exe","Example.App","5.9.2+buildhash","5.9.2.0");var oldUninstaller=Executable("unins000.exe","Example","5.9.1","51.1055.0.0");var serviceExe=Executable("Example.Service.exe","Example.Service","1.2.0");
var actualEvidence=InstalledVersion.Resolve("Example","5.9.1",[oldUninstaller,serviceExe,mainExe]);Check(actualEvidence.Version=="5.9.2" && actualEvidence.Path==mainExe.Path,"actual main program product version supersedes stale registry and uninstaller versions");
Check(InstalledVersion.NormalizeVersion("v5.9.2-beta.1+commit")=="5.9.2-beta.1","local prerelease identity preserved without build metadata");
Check(InstalledVersion.Resolve("Example","5.9.1",[Executable("Example.exe","Example","","5, 9, 2, 0")]).Version=="5.9.2","file version used when product version absent");
Check(InstalledVersion.Resolve("Example","5.9.1",[oldUninstaller,serviceExe]).Path=="","helper and uninstaller are not mistaken for main program");
var duplicateExe=mainExe with{Path=Path.Combine("C:\\Apps\\Second", "Example.App.exe"),ProductVersion="9.0.0"};Check(InstalledVersion.Resolve("Example","5.9.1",[mainExe,duplicateExe]).Path=="","ambiguous executable versions require confirmation rather than selecting highest");
Check(InstalledVersion.Resolve("Example","5.9.1",[mainExe,duplicateExe],mainExe.Path).Version=="5.9.2","explicit known main executable resolves ambiguity");
var custom=Executable("custom.exe","DifferentProduct","2.3.0");Check(InstalledVersion.Resolve("My portable app","1.0",[custom],custom.Path,true).Version=="2.3.0","user-selected portable executable can use a custom display name");
Check(InstalledVersion.Resolve("Example","5.9.1",[Executable("Example.App.exe","Example","5.9.3")]).Version=="5.9.3","each scan reads changed program metadata without remembered version");
var aliased=new InstalledApp("current","Example","5.9.2","C:\\Apps",AliasIds:["old-registry-id"]);Check(InstalledVersion.Find([aliased],"old-registry-id")==aliased,"merged identical installation keeps old binding IDs resolvable");
var coordinator=new TaskCoordinator();using(var a=coordinator.Start("a",["file:a"]))using(var b=coordinator.Start("b",["file:b"])){
 await a.WaitAsync();await b.WaitAsync();Check(coordinator.Count==2,"independent task resources execute concurrently");coordinator.Pause("a");Check(a.Token.IsCancellationRequested && !b.Token.IsCancellationRequested && a.StopReason=="暂停","pausing one task does not stop another");coordinator.Cancel("b");Check(b.Token.IsCancellationRequested && b.StopReason=="取消","cancelling a task is distinguished from pausing");
}Check(coordinator.Count==0,"completed tasks release coordinator registrations");
var first=coordinator.Start("first",["file:same"]);await first.WaitAsync();var second=coordinator.Start("second",["FILE:SAME"]);var queued=second.WaitAsync();Check(!queued.IsCompleted,"same file tasks wait instead of overwriting concurrent partial files");first.Dispose();await queued;second.Dispose();
var holder=coordinator.Start("holder",["repo:app"]);await holder.WaitAsync();using(var waiting=coordinator.Start("waiting",["repo:app"])){var wait=waiting.WaitAsync();coordinator.Cancel("waiting");var aborted=false;try{await wait;}catch(OperationCanceledException){aborted=true;}Check(aborted && !holder.Token.IsCancellationRequested,"queued task cancellation leaves resource owner running");}holder.Dispose();
using(var reusable=coordinator.Start("reuse",["repo:app"])){await reusable.WaitAsync();Check(!reusable.Token.IsCancellationRequested,"resource remains usable after cancelled waiter");}
var context=new AsyncLocal<string?>();async Task<string> Isolated(string value){context.Value=value;await Task.Delay(10);return context.Value!;}Check((await Task.WhenAll(Isolated("one"),Isolated("two"))).SequenceEqual(new[]{"one","two"}),"concurrent operation contexts retain their own result and release IDs");
using(var scopeClient=new GitHubClient(new(),new FakeHttp((req,ct)=>{
 if(req.RequestUri!.AbsolutePath.EndsWith("/device/code"))return ScopeCode();
 return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"access_token\":\"fixture-only-token\"}")});
 async Task<HttpResponseMessage> ScopeCode(){var fields=await req.Content!.ReadAsStringAsync(ct);Check(fields.Contains("scope=repo+user+workflow"),"device authorization requests Star Lists write scope");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"device_code\":\"fixture\",\"user_code\":\"fixture\",\"verification_uri\":\"https://github.com/login/device\",\"interval\":0,\"expires_in\":60}")};}
 }))){Check(await scopeClient.DeviceLogin("fixture-client",(_,_)=>{},CancellationToken.None)=="fixture-only-token","new-scope device authorization completes");}
var deniedWrites=0;
using(var readOnlyClient=new GitHubClient(new(),new FakeHttp((req,ct)=>{if(req.Method!=HttpMethod.Get)deniedWrites++;var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")};response.Headers.Add("X-OAuth-Scopes","repo, read:user, workflow");return Task.FromResult(response);})) {Token="fixture-only-token"}){var denied=false;try{await readOnlyClient.CreateList("name","",false);}catch(IOException ex){denied=ex.Message.Contains("重新授权");}Check(denied && deniedWrites==0,"read-only user scope rejected before remote writes with reauthorization guidance");}
using(var mismatchClient=new GitHubClient(new(),new FakeHttp((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(req.Method==HttpMethod.Get?"{}":"{\"data\":{\"updateUserListsForItem\":{\"lists\":[]}}}")}))) {Token="fixture-only-token"}){var mismatch=false;try{await mismatchClient.UpdateMembership("repo-node",["chosen"]);}catch(IOException){mismatch=true;}Check(mismatch,"missing requested group cannot be reported as successful membership write");}
var batchNames=Enumerable.Range(0,60).Select(i=>"owner/app"+i).ToArray();var runningChecks=0;var maxChecks=0;var checkedNames=new System.Collections.Concurrent.ConcurrentBag<string>();
await RepositoryChecks.Run(batchNames,async(name,ct)=>{var active=Interlocked.Increment(ref runningChecks);maxChecks=Math.Max(maxChecks,active);try{await Task.Delay(25,ct);checkedNames.Add(name);}finally{Interlocked.Decrement(ref runningChecks);}},CancellationToken.None);
Check(maxChecks==24 && runningChecks==0,"repository checks overlap with bounded concurrency");
Check(checkedNames.Count==60 && checkedNames.Distinct().Count()==60,"every repository checked exactly once");
var stoppedCalls=0;var batchFailed=false;
try{await RepositoryChecks.Run(batchNames,(_,_)=>{Interlocked.Increment(ref stoppedCalls);throw new IOException("fixture rate limit");},CancellationToken.None);}catch(IOException){batchFailed=true;}
Check(batchFailed && stoppedCalls==1,"batch stops scheduling after fatal failure");
using(var cancelledBatch=new CancellationTokenSource()){var cancelCalls=0;var pending=RepositoryChecks.Run(batchNames,async(_,ct)=>{Interlocked.Increment(ref cancelCalls);await Task.Delay(Timeout.Infinite,ct);},cancelledBatch.Token);cancelledBatch.Cancel();var wasCancelled=false;try{await pending;}catch(OperationCanceledException){wasCancelled=true;}Check(wasCancelled && cancelCalls==24,"cancellation stops active workers and prevents new checks");}
using(var limitedClient=new GitHubClient(new(),new FakeHttp((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)429){Content=new StringContent("rate limit")})))){var limited=false;try{await limitedClient.Releases("owner/app",CancellationToken.None);}catch(GitHubRateLimitException){limited=true;}Check(limited,"rate limiting is distinct from a single-repository failure");}
Check(Rules.MergeMembership(["existing","added-on-web"],["chosen"],["existing"]).ToHashSet().SetEquals(["added-on-web","chosen"]),"membership delta preserves unrelated web changes");
var groupedReadCalls=0;
using(var groupedRead=new GitHubClient(new(),new FakeHttp(async(req,ct)=>{
 groupedReadCalls++;using var input=System.Text.Json.JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));var query=input.RootElement.GetProperty("query").GetString()!;
 object payload;
 if(query.Contains("viewer{lists")){if(!query.Contains("items(first:100)"))throw new Exception("Separate first-page fetch");var members=new{nodes=new[]{new{nameWithOwner="owner/first"}},pageInfo=new{hasNextPage=true,endCursor="member-next"}};payload=new{data=new{viewer=new{lists=new{nodes=new[]{new{id="list-id",name="group",description="fixture",isPrivate=false,items=members}},pageInfo=new{hasNextPage=false,endCursor=(string?)null}}}}};}
 else{if(input.RootElement.GetProperty("variables").GetProperty("after").GetString()!="member-next")throw new Exception("Lost member cursor");payload=new{data=new{node=new{items=new{nodes=new[]{new{nameWithOwner="owner/second"}},pageInfo=new{hasNextPage=false,endCursor=(string?)null}}}}};}
 return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(payload))};
 }))){var lists=await groupedRead.Lists(CancellationToken.None);Check(groupedReadCalls==2 && lists.Single().Repositories.SequenceEqual(new[]{"owner/first","owner/second"}),"group metadata and initial membership fetched together while overflow pages remain complete");}
Check(System.Text.Json.JsonSerializer.Deserialize<Repository>("{\"FullName\":\"owner/app\",\"Description\":\"\",\"Private\":false}")!.NodeId=="","legacy repository cache remains readable");
var starred=false;var starWrites=0;
using(var starClient=new GitHubClient(new(),new FakeHttp(async(req,ct)=>{
 Check(req.Headers.Authorization?.Parameter=="fixture-only-token","Star management authenticated");
 if(req.Method==HttpMethod.Put){starred=true;starWrites++;Check(req.Content!=null && (await req.Content.ReadAsByteArrayAsync(ct)).Length==0,"Star PUT sends empty content");return new HttpResponseMessage(HttpStatusCode.NoContent);}
 if(req.Method==HttpMethod.Delete){starred=false;starWrites++;return new HttpResponseMessage(HttpStatusCode.NoContent);}
 return new HttpResponseMessage(starred?HttpStatusCode.NoContent:HttpStatusCode.NotFound);
 })) {Token="fixture-only-token"}){await starClient.SetStar("owner/app",true);Check(await starClient.IsStarred("owner/app"),"Star addition verified after 204 response");await starClient.SetStar("owner/app",false);Check(!await starClient.IsStarred("owner/app") && starWrites==2,"Star removal verified without JSON response");}
using(var uncertainClient=new GitHubClient(new(),new FakeHttp((req,ct)=>Task.FromResult(new HttpResponseMessage(req.Method==HttpMethod.Put?HttpStatusCode.InternalServerError:HttpStatusCode.NoContent))))) {uncertainClient.Token="fixture-only-token";await uncertainClient.SetStar("owner/app",true);Check(true,"uncertain Star write reconciles against remote state");}
var membershipPayload=false;
using(var listClient=new GitHubClient(new(),new FakeHttp(async(req,ct)=>{
 if(req.Method==HttpMethod.Get)return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")};
 using var payload=System.Text.Json.JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));var query=payload.RootElement.GetProperty("query").GetString()!;var input=payload.RootElement.GetProperty("variables").GetProperty("input");
 string json;
 if(query.Contains("createUserList")){Check(input.GetProperty("isPrivate").GetBoolean() && input.GetProperty("name").GetString()=="中文分组","create list sends name and privacy");json="{\"data\":{\"createUserList\":{\"list\":{\"id\":\"list-id\",\"name\":\"中文分组\",\"description\":\"说明\",\"isPrivate\":true}}}}";}
 else if(query.Contains("updateUserListsForItem")){membershipPayload=input.GetProperty("itemId").GetString()=="repo-node" && input.GetProperty("listIds").GetArrayLength()==2;json="{\"data\":{\"updateUserListsForItem\":{\"lists\":[{\"id\":\"existing\"},{\"id\":\"chosen\"}]}}}";}
 else if(query.Contains("updateUserList")){Check(input.GetProperty("listId").GetString()=="list-id","rename addresses list ID");json="{\"data\":{\"updateUserList\":{\"list\":{\"id\":\"list-id\"}}}}";}
 else {Check(input.GetProperty("listId").GetString()=="list-id","delete addresses list ID only");json="{\"data\":{\"deleteUserList\":{\"clientMutationId\":null}}}";}
 return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)};
 })) {Token="fixture-only-token"}){var list=await listClient.CreateList("中文分组","说明",true);Check(list.Source=="GitHub" && list.IsPrivate,"created group metadata preserved");await listClient.UpdateList(list,"新名称","新说明",true);var actual=await listClient.UpdateMembership("repo-node",["existing","chosen","chosen"]);Check(membershipPayload && actual.Length==2,"membership sends complete unique list IDs and reads confirmed membership");await listClient.DeleteList(list.Id);}
using(var errorClient=new GitHubClient(new(),new FakeHttp((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"errors\":[{\"message\":\"fixture denied\"}],\"data\":null}")})))) {errorClient.Token="fixture-only-token";var graphRejected=false;try{await errorClient.CreateList("name","",false);}catch(IOException){graphRejected=true;}Check(graphRejected,"GraphQL errors cannot be reported as successful local changes");}
var scheduleNow=DateTimeOffset.Parse("2026-10-08T12:00:00Z");
Check(System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!.RefreshIntervalHours==24,"existing settings default to daily refresh");
Check(!RefreshSchedule.IsDue(24,scheduleNow.AddHours(-23),null,scheduleNow) && RefreshSchedule.IsDue(24,scheduleNow.AddHours(-24),null,scheduleNow),"daily refresh boundary");
Check(!RefreshSchedule.IsDue(0,null,null,scheduleNow),"manual-only disables startup and scheduled refresh");
Check(RefreshSchedule.IsDue(6,scheduleNow.AddHours(-7),null,scheduleNow) && !RefreshSchedule.IsDue(12,scheduleNow.AddHours(-7),null,scheduleNow),"selected frequency controls refresh");
Check(!RefreshSchedule.IsDue(1,null,scheduleNow.AddMinutes(-30),scheduleNow) && RefreshSchedule.IsDue(1,null,scheduleNow.AddHours(-1),scheduleNow),"failed checks use bounded retry after wake");
Check(RefreshSchedule.CanStartAutomatic(false,false,false,false,false),"automatic scan remains available when application is idle");
Check(!RefreshSchedule.CanStartAutomatic(false,true,false,false,false)&&!RefreshSchedule.CanStartAutomatic(false,false,true,false,false),"automatic scans defer during active tasks and batch publishing without serializing project workers");
Check(!RefreshSchedule.CanStartAutomatic(true,false,false,false,false)&&!RefreshSchedule.CanStartAutomatic(false,false,false,true,false)&&!RefreshSchedule.CanStartAutomatic(false,false,false,false,true),"automatic scan does not overlap refresh shutdown or cache cleanup");
Check(ReleasePreview.Create("2026-09-29 · v2.7.0","v2.7.0")=="","duplicate version and date preview hidden");
Check(ReleasePreview.Create("# v2.7.0\n## What's Changed\n- **修复**窗口恢复\n- 新增[搜索](https://example.test)\n- 改善性能\n- 第四项","v2.7.0")=="修复窗口恢复\n新增搜索\n改善性能","preview skips metadata and keeps three meaningful changes");
Check(ReleasePreview.Create("```sh\ninstall tool\n```\n![badge](https://example.test/badge)\n- 修复崩溃","v1.0")=="修复崩溃","preview omits code blocks and image markup");
Check(ReleasePreview.Create("\n","v1.0")=="" && ReleasePreview.Create("# 更新日志\n2026-09-29 v1.0","v1.0")=="","empty release notes do not create filler text");
Check(Rules.CompareVersions("v1.10.0","1.9.9")>0,"numeric version ordering");
Check(Rules.CompareVersions("1.0.0","1.0")==0,"normalized version equality");
Check(Rules.CompareVersions("2.0.0-rc.10","2.0.0-rc.2")>0,"numeric prerelease identifiers");
Check(Rules.CompareVersions("2.0.0-beta","2.0.0")<0,"stable version precedence");
Check(Rules.CompareVersions("2026 nightly","2025.1")==null,"ambiguous version rejected");
Check(Rules.CompareVersions("99999999999999999999999999","1")==null,"large version rejected safely");
Check(Rules.CompareVersions("v1.0.0+build.8","1.0.0")==0,"build metadata ignored");
Check(Rules.CompareVersions("1.9","2.0-beta")<0,"major version upgrades");
Check(Rules.CompareVersions("2.0","2.1-beta")<0,"beta upgrade requires subscription");
Check(Rules.ValidRepository("owner/a.b-c_d"),"valid repository");
Check(!Rules.ValidRepository("../evil")&&!Rules.ValidRepository("a/b/c"),"invalid repositories rejected");
Check(AppMatcher.GitHubRepository("https://github.com/owner/editor/releases/latest")=="owner/editor","registry GitHub URL repository extracted");
Check(AppMatcher.GitHubRepository("https://github.com.attacker.invalid/owner/editor")=="","repository hint rejects lookalike host");
Check(AppMatcher.GitHubRepository("https://github.com/settings/applications")=="","GitHub settings URL is not a repository");
var matcherRepos=new[]{new Repository("owner/my-editor","",false),new Repository("other/my-editor","",false)};
Check(AppMatcher.Suggest(new("a","My Editor (x64)","1.0","",RepositoryHint:"owner/my-editor"),matcherRepos).Single().Reliable,"explicit metadata takes precedence");
Check(AppMatcher.Suggest(new("a","My Editor (x64)","1.0",""),matcherRepos).All(m=>!m.Reliable),"duplicate-name authors require confirmation");
Check(AppMatcher.Suggest(new("a","My Editor 1.0.0","1.0",""),matcherRepos.Take(1)).Single().Reliable,"unique normalized name bulk candidate");
Check(!AppMatcher.Suggest(new("a","My Editor Pro","1.0",""),matcherRepos.Take(1)).Single().Reliable,"approximate name never preselected");
Check(AppMatcher.Suggest(new("a","Git","1.0",""),[new("someone/git","",false)]).Count==0,"generic short names not automatically bound");
Check(Rules.CredentialAnswer("Password for 'https://x-access-token@github.com':", "fixture")=="fixture","GitHub askpass scoped");
Check(Rules.CredentialAnswer("Password for 'https://github.com.attacker.invalid':", "fixture")=="","askpass rejects lookalike host");
Check(Rules.CredentialAnswer("Password for 'https://attacker.invalid':", "fixture")=="","askpass rejects remote host");
Check(Rules.SafeSegment("../../evil.exe")=="_.._evil.exe","sanitized filenames cannot escape");
Check(Rules.SafeSegment("CON.txt")=="_CON.txt","Windows device name neutralized");
Asset A(string n)=>new(1,n,3,"https://example.invalid/asset",null);
Check(Rules.IsWindowsX64(A("app-windows-x86_64.zip")),"x86_64 asset supported");
Check(Rules.IsWindowsX64(A("app-win-x64.exe")),"Windows x64 asset");
Check(!Rules.IsWindowsX64(A("app-linux-x64.zip")),"Linux package excluded");
Check(!Rules.IsWindowsX64(A("app-arm64.exe")),"ARM64 installer excluded");
Check(!Rules.IsWindowsX64(A("app.exe")),"unknown architecture requires manual save");
Check(!Rules.IsWindowsX64(A("app-x64-symbols.zip")),"debug package excluded");
Check(Rules.IsWindowsInstallCandidate(A("CC-Switch-v4.0.9-Windows.msi")),"architecture-neutral Windows MSI allowed for content inspection");
Check(!Rules.IsWindowsInstallCandidate(A("CC-Switch-v4.0.9-Windows-arm64.msi")),"explicit ARM64 MSI not selected for x64 host");
Check(Rules.IsWindowsInstallCandidate(A("app.exe")),"unmarked EXE is allowed for architecture detection after download");
Check(Rules.IsX64MsiTemplate("x64;1033") && !Rules.IsX64MsiTemplate("Intel64;1033"),"x64 MSI template distinct from Itanium");
Check(!Rules.IsX64MsiTemplate("Arm64;1033") && !Rules.IsX64MsiTemplate(";1033"),"ARM64 or blank template not misclassified as x64");
Check(Rules.IsWindowsInstallCandidate(A("app-win-x86.exe"),true) && Rules.IsWindowsInstallCandidate(A("app-win-x64.exe"),true),"64-bit Windows accepts x86 and x64 installers");
Check(Rules.IsWindowsInstallCandidate(A("app-win32.zip"),false) && !Rules.IsWindowsInstallCandidate(A("app-x86_64.zip"),false),"32-bit Windows accepts x86 but rejects x64");
Check(Rules.IsWindowsInstallCandidate(A("app-i686.exe"),true) && Rules.IsWindowsInstallCandidate(A("app-32-bit.exe"),true),"common 32-bit filename markers supported");
Check(!Rules.IsWindowsInstallCandidate(A("app-linux-x86.zip"),true) && !Rules.IsWindowsInstallCandidate(A("app-arm64.exe"),true),"foreign platforms remain excluded for x86 selection");
Check(Rules.IsCompatibleMsiTemplate("Intel;1033",true) && Rules.IsCompatibleMsiTemplate("x64;1033",true),"64-bit host accepts Intel x86 and x64 MSI summaries");
Check(Rules.IsCompatibleMsiTemplate("Intel;1033",false) && !Rules.IsCompatibleMsiTemplate("x64;1033",false),"32-bit host rejects 64-bit MSI summary");
Check(!Rules.IsCompatibleMsiTemplate("Intel64;1033",true) && !Rules.IsCompatibleMsiTemplate("Arm64;1033",true) && !Rules.IsCompatibleMsiTemplate(";1033",true),"unsupported or blank MSI platforms rejected");
var packageDate=DateTimeOffset.UtcNow;DownloadedPackage Package(string repo,string tag,bool beta,string family,DateTimeOffset published)=>new(repo,1,tag,beta,published,"tool-x64.exe",Path.Combine(Path.GetTempPath(),"package.exe"),Path.GetTempPath(),1,"fixture-hash",family);
var oldPackage=Package("owner/tool","v1.0",false,"windows/x64",packageDate.AddDays(-1));var latestPackage=oldPackage with{ReleaseId=2,Tag="v2.0",Published=packageDate};var betaPackage=oldPackage with{Prerelease=true};var androidPackage=oldPackage with{Family="android/unknown"};var otherRepoPackage=oldPackage with{Repository="other/tool"};
Check(PackageRules.Outdated([oldPackage,latestPackage,betaPackage,androidPackage,otherRepoPackage]).SequenceEqual(new[]{oldPackage}),"latest package cleanup isolates repository platform and release channel");
Check(PackageRules.Outdated([latestPackage,latestPackage with{AssetName="tool-x64.zip"}]).Length==0,"all assets of same latest release are retained");
var packageBinding=new Binding{AppId="tool-id",Repository="owner/tool"};var installedTool=new InstalledApp("tool-id","Tool","2.0",Path.GetTempPath(),ExecutablePath:Path.Combine(Path.GetTempPath(),"main.exe"),VersionSource:"程序产品版本");
Check(PackageRules.Installed(latestPackage,[packageBinding],[installedTool]),"installed-package cleanup requires verified target version");
Check(!PackageRules.Installed(latestPackage,[packageBinding],[installedTool with{Version="1.0"}]) && !PackageRules.Installed(latestPackage,[packageBinding],[installedTool with{VersionSource="注册表"}]),"old or unverified installed version cannot trigger package deletion");
Check(!PackageRules.Installed(androidPackage,[packageBinding],[installedTool]),"desktop installation does not imply Android package installed");
Check(!PackageRules.Installed(latestPackage,[packageBinding],[installedTool with{ExecutablePath=latestPackage.Path}]),"portable main program cannot be cleaned as an installer package");
Check(new Settings().KeepLatestPackages && !new Settings().DeleteInstalledPackages && new Settings().PromptInstalledPackageCleanup,"latest package retention defaults on while post-install deletion stays opt-in and cleanup prompt stays on");
var now=DateTimeOffset.UtcNow;Release R(string tag,bool beta,params Asset[] assets)=>new(1,"owner/app",tag,tag,"log",beta,now,"https://github.com/owner/app",assets.ToList());
var history=Enumerable.Range(0,10000).Select(i=>R((i%3==0?"desktop-":"mobile-")+(10000-i),i%2==0) with{Id=i+1,Published=now.AddMinutes(-i),Body=new string('x',4096)}).ToArray();var line=new Binding{Repository="owner/app",TagPrefix="desktop-"};var compact=ReleaseRetention.Compact(history,[line]);
Check(compact.Count<=4 && compact.Sum(r=>r.Body.Length)<20000,"ten thousand releases compact to feed and subscribed-line candidates");
Check(Rules.SelectRelease(history,line)?.Id==Rules.SelectRelease(compact,line)?.Id,"compaction preserves subscribed stable release");
var betaLine=new Binding{Repository="owner/app",TagPrefix="desktop-",IncludePrerelease=true};Check(Rules.SelectRelease(history,betaLine)?.Id==Rules.SelectRelease(compact,betaLine)?.Id,"compaction preserves subscribed beta release");
Check(new[]{false,true}.All(beta=>history.First(r=>r.Prerelease==beta).Id==compact.Where(r=>r.Prerelease==beta).OrderByDescending(r=>r.Published).First().Id),"compaction preserves both feed channels and full release content");
var releasePageCalls=0;using(var pagedClient=new GitHubClient(new(),new FakeHttp((req,ct)=>{
 if(req.RequestUri!.AbsolutePath.EndsWith("/latest"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
 var page=Interlocked.Increment(ref releasePageCalls);if(!req.RequestUri!.Query.Contains("per_page=30"))throw new Exception("Unbounded release page");
 var payload=Enumerable.Range(0,page==1?30:1).Select(i=>new{id=(page-1)*30+i+1,tag_name=page==2?"desktop-v1":"mobile-v"+i,name="fixture",body="完整日志",draft=false,prerelease=false,published_at=now.AddMinutes(-((page-1)*30+i)),html_url="",assets=Array.Empty<object>()});
 return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(payload))});
 }))){var candidates=await pagedClient.Releases("owner/app",CancellationToken.None,[line]);Check(releasePageCalls==2 && candidates.Count==2 && candidates.Any(r=>r.Tag=="desktop-v1" && r.Body=="完整日志"),"streamed paged scan retains older subscribed line without all historical records");}
var cachedPages=new Dictionary<string,GitHubClient.ReleasePage>();var cachePhase=0;var cacheRequests=0;var cacheBodies=0;var conditionalRequests=0;
HttpMessageHandler CachedTransport()=>new FakeHttp((req,ct)=>{
 if(req.RequestUri!.AbsolutePath.EndsWith("/latest"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
 cacheRequests++;var page=int.Parse(req.RequestUri!.Query.Split("page=").Last());var etag=$"\"fixture-{page}\"";
 if(req.Headers.IfNoneMatch.Count>0)conditionalRequests++;
 if(cachePhase==1){if(req.Headers.IfNoneMatch.Single().Tag!=etag)throw new Exception("Missing validator");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));}
 if(cachePhase==3 && req.Headers.IfNoneMatch.Count>0)throw new Exception("Binding change reused incompatible candidates");
 cacheBodies++;var items=Enumerable.Range(0,cachePhase==2&&page==2?0:page==1?30:1).Select(i=>new{id=(page-1)*30+i+1,tag_name=page==2?"desktop-v1":"mobile-v"+i,name="fixture",body="complete-body",draft=false,prerelease=false,published_at=now.AddMinutes(-((page-1)*30+i)),html_url="",assets=Array.Empty<object>()});
 var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(items))};response.Headers.ETag=new(etag);return Task.FromResult(response);
 });
using(var cachedClient=new GitHubClient(new(),CachedTransport())){cachedClient.ReadReleasePage=key=>cachedPages.GetValueOrDefault(key);cachedClient.SaveReleasePage=(key,page)=>cachedPages[key]=page;var original=await cachedClient.Releases("owner/app",CancellationToken.None,[line]);cachePhase=1;var warm=await cachedClient.Releases("owner/app",CancellationToken.None,[line]);Check(cacheRequests==4 && cacheBodies==2 && conditionalRequests==2 && original.Select(r=>r.Id).SequenceEqual(warm.Select(r=>r.Id)),"unchanged pages verified without downloading or reparsing bodies");}
using(var restartedCacheClient=new GitHubClient(new(),CachedTransport())){restartedCacheClient.ReadReleasePage=key=>cachedPages.GetValueOrDefault(key);restartedCacheClient.SaveReleasePage=(key,page)=>cachedPages[key]=page;var warm=await restartedCacheClient.Releases("owner/app",CancellationToken.None,[line]);Check(warm.Count==2 && cacheBodies==2,"new client reuses persisted page candidates after restart");cachePhase=2;var deleted=await restartedCacheClient.Releases("owner/app",CancellationToken.None,[line]);Check(deleted.Count==1 && Rules.SelectRelease(deleted,line)==null,"historical release deletion is detected despite a cached first page");cachePhase=3;await restartedCacheClient.Releases("owner/app",CancellationToken.None,[new Binding{Repository="owner/app",TagPrefix="other-"}]);Check(cachedPages.Values.All(p=>p.Signature.Contains("other-")),"changing subscribed line invalidates page candidates before conditional fetch");}
var selection=new ReleaseCandidates("owner/app",[line]);var parsedBodies=0;foreach(var r in history){if(selection.Needs(r.Tag,r.Prerelease,r.Published)){parsedBodies++;selection.Add(r);}}Check(parsedBodies<=4,"old release bodies and assets need not be materialized for discarded versions");
Check(Rules.RecentProjectCount([R("v1",false) with{Published=now.AddDays(-7)},R("v2",true) with{Published=now.AddDays(-1)},R("v1",false) with{Repository="owner/old",Published=now.AddDays(-7).AddSeconds(-1)},R("v1",false) with{Repository="owner/future",Published=now.AddSeconds(1)}],now)==1,"seven-day project count deduplicates repositories and excludes older or future releases");
var binding=new Binding();Check(Rules.SelectAsset(R("v1",false,A("app-x64.zip"),A("app-x64.exe")),binding)==null,"ambiguous assets require selection");
binding.AssetPattern=".exe";Check(Rules.SelectAsset(R("v1",false,A("app-x64.zip"),A("app-x64.exe")),binding)?.Name=="app-x64.exe","explicit asset pattern");
Check(Rules.SelectRelease([R("v1.0",false),R("v2.0-beta",true)],new Binding())?.Tag=="v1.0","stable-only default");
Check(Rules.SelectRelease([R("mobile-v5",false),R("desktop-v2",false)],new Binding {TagPrefix="desktop-"})?.Tag=="desktop-v2","release line isolation");
Check(!new Binding {PauseUntil=now.AddDays(1)}.Enabled(now),"pause schedule");
Check(new Binding {PauseUntil=now.AddDays(-1)}.Enabled(now),"expired pause resumes");
Check(!new Binding {Disabled=true}.Enabled(now),"disabled subscription");
var root=Path.Combine(Path.GetTempPath(),"Gitstar-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
await ProjectChecks.Run(Check,root);
await RepositorySettingsChecks.Run(Check);
await WorkspaceChecks.Run(Check,root);
await WorkflowChecks.Run(Check,root);
await SourceSyncChecks.Run(Check,root);
ReleaseNotesChecks.Run(Check,root);
RepositoryMetadataChecks.Run(Check);
SoftwareEntryChecks.Run(Check,root);
InstalledIdentityChecks.Run(Check,root);
VersionEvidenceChecks.Run(Check,root);
SpecialFollowChecks.Run(Check,root);
ActivationChecks.Run(Check);
PortableReplacementChecks.Run(Check,root);
RootPortableReplacementChecks.Run(Check,root);
await PortableProcessChecks.Run(Check,root);
await NotificationChecks.Run(Check);
await RepositoryIssueChecks.Run(Check);
await UploadProgressChecks.Run(Check,root);
DownloadDefaultsChecks.Run(Check);
SoftwareAuditChecks.Run(Check,root);
await ReleaseStateChecks.Run(Check,root);
await SelfUpdateChecks.Run(Check);
await UpdateSortingChecks.Run(Check);
await ReleaseSelectionChecks.Run(Check);
await ReleaseFileSelectionChecks.Run(Check,root);
await DownloadVersionChecks.Run(Check);
await NewFeatureChecks.Run(Check,root);
await ProjectStatusChecks.Run(Check);
await ProjectDeletionChecks.Run(Check);
await ReleaseManagementChecks.Run(Check);
await CacheCleanupChecks.Run(Check,root);
await AutomationChecks.Run(Check,root);
await DownloadPreferencesChecks.Run(Check,root);
var trackedPath=Path.Combine(root,"owner_tool","setup-x64.exe");Directory.CreateDirectory(Path.GetDirectoryName(trackedPath)!);File.WriteAllText(trackedPath,"owned");var trackedPackage=latestPackage with{Root=root,Path=trackedPath,AssetName="setup-x64.exe",Size=5,Sha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("owned")))};
Check(await PackageFiles.Matches(trackedPackage),"owned package path size and digest verified before cleanup");
File.WriteAllText(trackedPath,"other");Check(!await PackageFiles.Matches(trackedPackage),"modified same-size package is preserved");
Check(!await PackageFiles.Matches(trackedPackage with{AssetName="different.exe"}),"record path must match repository and asset layout");
File.Delete(trackedPath);Check(!await PackageFiles.Matches(trackedPackage),"missing file is not treated as a verified deletion target");
foreach(var template in new[]{"x64;1033","Arm64;1033","Intel64;1033","Intel;1033"}){var path=Path.Combine(root,template.Split(';')[0]+".msi");MsiFixture.Create(path,template);Check(WindowsServices.MsiPlatform(path)==template,"MSI native read-only platform detection: "+template);}
var invalidMsi=Path.Combine(root,"invalid.msi");File.WriteAllText(invalidMsi,"not a package");var invalidRejected=false;try{WindowsServices.MsiPlatform(invalidMsi);}catch(IOException){invalidRejected=true;}Check(invalidRejected,"invalid MSI cannot reach installer launch");
var chosenPortable=new InstalledApp("portable/chosen","Chosen tool","1.0",root,true);var otherPortable=new InstalledApp("portable/other","Other tool","9.0",root,true);var scopedApps=WindowsServices.ScanMatched([new Binding{AppId=chosenPortable.Id,Repository="owner/chosen"}],[chosenPortable,otherPortable]);Check(scopedApps.Count==1 && scopedApps[0].Id==chosenPortable.Id,"matched scan includes only bound portable software");
Check(WindowsServices.ScanMatched([], [chosenPortable,otherPortable]).Count==0,"empty matching scope does not trigger full registry or portable scan");
var snapshotRoot=Path.Combine(root,"installed-snapshot");
var snapshotBindings=new[]{new Binding{AppId="bound/alias",Repository="owner/tool"}};
var snapshotApp=new InstalledApp("bound/main","Tool","1.0",root,AliasIds:["bound/alias"]);
using(var db=new LocalStore(snapshotRoot)){db.Set("cache/user-a/installed",InstalledSnapshot.Capture([snapshotApp,otherPortable],snapshotBindings));}
using(var db=new LocalStore(snapshotRoot)){
 db.ReleaseMemory();Check(db.Get("cache/user-a/installed",new InstalledSnapshot(DateTimeOffset.MinValue,[])).Apps.Count==1,"idle memory reclamation preserves stored software snapshot");
 var snapshot=db.Get("cache/user-a/installed",new InstalledSnapshot(DateTimeOffset.MinValue,[]));
 Check(snapshot.Apps.Count==1 && InstalledVersion.Find(snapshot.Apps,"bound/alias")?.Version=="1.0" && snapshot.CheckedAt>DateTimeOffset.MinValue,"restart restores matched software including merged registry aliases");
 Check(db.Get("cache/user-b/installed",new InstalledSnapshot(DateTimeOffset.MinValue,[])).Apps.Count==0,"installed snapshot is isolated per account");
 Check(InstalledSnapshot.Capture(snapshot.Apps,[]).Apps.Count==0,"removed bindings are not restored from previous snapshot");
 db.Set("cache/user-a/installed",InstalledSnapshot.Capture([snapshotApp with{Version="2.0"}],snapshotBindings));
}
using(var db=new LocalStore(snapshotRoot)){Check(db.Get("cache/user-a/installed",new InstalledSnapshot(DateTimeOffset.MinValue,[])).Apps.Single().Version=="2.0","fresh version replaces saved installed version");db.Set("cache/user-a/installed",InstalledSnapshot.Capture([],snapshotBindings));}
using(var db=new LocalStore(snapshotRoot))Check(db.Get("cache/user-a/installed",new InstalledSnapshot(DateTimeOffset.MinValue,[])).Apps.Count==0,"confirmed empty scan clears obsolete update snapshot across restart");
var storedAuth=new GitHubCredential("fixture-access","fixture-refresh",DateTimeOffset.UtcNow.AddSeconds(-1),DateTimeOffset.UtcNow.AddDays(30),"repo,user,workflow","fixture-client");
using(var authDb=new LocalStore(Path.Combine(root,"auth"))){authDb.Set("token",SecretStore.Protect("legacy-fixture"));Check(CredentialStorage.Load(authDb).AccessToken=="legacy-fixture","legacy saved authorization remains readable");CredentialStorage.Save(authDb,storedAuth);var protectedAuth=authDb.Get("auth","");Check(!protectedAuth.Contains("fixture-refresh") && !protectedAuth.Contains("fixture-access"),"access and refresh credentials are encrypted at rest");}
using(var authDb=new LocalStore(Path.Combine(root,"auth"))){Check(CredentialStorage.Load(authDb)==storedAuth,"restarting preserves tokens expiry scopes and client binding");}
var refreshCalls=0;GitHubCredential? persistedRefresh=null;
using(var renewingClient=new GitHubClient(new(),new FakeHttp(async(req,ct)=>{
 if(req.RequestUri!.Host=="github.com"){Interlocked.Increment(ref refreshCalls);var body=await req.Content!.ReadAsStringAsync(ct);if(!body.Contains("grant_type=refresh_token") || !body.Contains("client_id=fixture-client") || body.Contains("client_secret") || body.Contains("scope="))throw new Exception("Invalid device refresh payload");await Task.Delay(20,ct);return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"access_token\":\"fixture-new-access\",\"refresh_token\":\"fixture-new-refresh\",\"expires_in\":28800,\"refresh_token_expires_in\":15897600,\"scope\":\"repo,user,workflow\"}")};}
 if(req.Headers.Authorization?.Parameter!="fixture-new-access")throw new Exception("API used expired credential");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"login\":\"fixture-user\"}")};
 }))){renewingClient.SetCredentials(storedAuth);renewingClient.CredentialsChanged=auth=>persistedRefresh=auth;await Task.WhenAll(Enumerable.Range(0,24).Select(_=>renewingClient.Account(CancellationToken.None)));Check(refreshCalls==1 && persistedRefresh?.RefreshToken=="fixture-new-refresh","24 concurrent requests refresh once and persist rotated credentials");using(var authDb=new LocalStore(Path.Combine(root,"auth")))CredentialStorage.Save(authDb,renewingClient.Credentials);}
using(var resumedClient=new GitHubClient(new(),new FakeHttp((req,ct)=>{if(req.RequestUri!.Host!="api.github.com" || req.Headers.Authorization?.Parameter!="fixture-new-access")throw new Exception("Unexpected reauthorization after restart");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"login\":\"fixture-user\"}")});}))){using(var authDb=new LocalStore(Path.Combine(root,"auth")))resumedClient.SetCredentials(CredentialStorage.Load(authDb));Check(await resumedClient.Account(CancellationToken.None)=="fixture-user","reloaded valid credentials use API without browser authorization or refresh");}
using(var failingRenewal=new GitHubClient(new(),new FakeHttp((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))){failingRenewal.SetCredentials(storedAuth);var kept=false;try{await failingRenewal.EnsureFreshToken();}catch(IOException){kept=failingRenewal.Credentials==storedAuth;}Check(kept,"temporary refresh failure preserves saved authorization");}
using(var authDb=new LocalStore(Path.Combine(root,"auth"))){CredentialStorage.Clear(authDb);Check(CredentialStorage.Load(authDb).AccessToken=="","logout clears new and legacy saved credentials");}
using(var db=new LocalStore(root)) {db.Set("quote'key",new[]{"中文","x';DROP TABLE documents;--"});Check(db.Get("quote'key",Array.Empty<string>()).Length==2,"SQLite parameterized Unicode persistence");}
using(var db=new LocalStore(root))Check(db.Get("quote'key",Array.Empty<string>())[0]=="中文","SQLite reopening");
var protectedValue=SecretStore.Protect("fixture-only-token");Check(protectedValue!="fixture-only-token"&&SecretStore.Unprotect(protectedValue)=="fixture-only-token","DPAPI roundtrip");
var bytes=Encoding.UTF8.GetBytes("fixture-payload");var digest="sha256:"+Convert.ToHexString(SHA256.HashData(bytes));var asset=new Asset(23,"app-x64.exe",bytes.Length,"",digest);
var fixture=new FakeHttp(async(req,ct)=>{
 if(req.RequestUri!.Host=="api.github.com") {Check(req.Headers.Authorization?.Parameter=="fixture-only-token","private download API auth");var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new Uri("https://signed.example.invalid/asset");return redirect;}
 Check(req.Headers.Authorization==null,"redirect credential isolation");await Task.CompletedTask;return new HttpResponseMessage(HttpStatusCode.OK) {Content=new ByteArrayContent(bytes)};
});
using(var client=new GitHubClient(new(),fixture) {Token="fixture-only-token"}) {
 var received=0L;var verifyingAfterComplete=false;var verificationStarted=false;
 var path=await WindowsServices.Download(client,R("v1",false,asset),asset,root,(count,total)=>{if(verificationStarted)throw new Exception("Late download progress after verification");received=count;},CancellationToken.None,()=>{verificationStarted=true;verifyingAfterComplete=received==asset.Size;});Check(File.ReadAllBytes(path).SequenceEqual(bytes),"streamed verified download");
 Check(verifyingAfterComplete,"verification phase follows final byte progress");
 var reuseVerified=false;var reused=await WindowsServices.Download(client,R("v1",false,asset),asset,root,(_,_)=>throw new Exception("Verified existing file should not redownload"),CancellationToken.None,()=>reuseVerified=true);Check(reuseVerified && reused==path,"existing installer shows verification before reuse");
 Check(await WindowsServices.Verify(path,asset,CancellationToken.None),"SHA256 verified");await File.WriteAllTextAsync(path,"corrupted");Check(!await WindowsServices.Verify(path,asset,CancellationToken.None),"corruption detected");
 var managed=new DownloadedPackage("owner/app",1,"v1",false,now.AddDays(-1),asset.Name,path,root,9,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("corrupted"))),"windows/x64");var newBytes=Encoding.UTF8.GetBytes("new-installer");var updatedAsset=asset with{Size=newBytes.Length,Digest="sha256:"+Convert.ToHexString(SHA256.HashData(newBytes))};
 using(var replacementClient=new GitHubClient(new(),new FakeHttp((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(newBytes)})))){
 var oldPreserved=true;var replacementChecks=0;await WindowsServices.Download(replacementClient,R("v2",false,updatedAsset),updatedAsset,root,(_,_)=>{},CancellationToken.None,replaceManaged:async ct=>{replacementChecks++;oldPreserved&=File.ReadAllText(path)=="corrupted";return await PackageFiles.Matches(managed,ct);},targetPath:path);Check(oldPreserved && replacementChecks==2 && File.ReadAllBytes(path).SequenceEqual(newBytes),"managed same-name installer retained until verified replacement commits");}
 File.WriteAllText(path,"corrupted");using(var badReplacement=new GitHubClient(new(),new FakeHttp((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(newBytes)})))){var badAsset=updatedAsset with{Digest="sha256:"+new string('0',64)};var refused=false;try{await WindowsServices.Download(badReplacement,R("v2",false,badAsset),badAsset,root,(_,_)=>{},CancellationToken.None,replaceManaged:ct=>PackageFiles.Matches(managed,ct),targetPath:path);}catch(IOException){refused=true;}Check(refused && File.ReadAllText(path)=="corrupted","failed new-package verification preserves old managed installer");}
}
var zip=Path.Combine(root,"malicious.zip");using(var a=ZipFile.Open(zip,ZipArchiveMode.Create)){var entry=a.CreateEntry("../outside.exe");using var w=new StreamWriter(entry.Open());w.Write("evil");}
var rejected=false;try{WindowsServices.ExtractPortable(zip);}catch(IOException){rejected=true;}Check(rejected,"ZIP traversal preflight");Check(!File.Exists(Path.Combine(root,"outside.exe")),"no traversal file created");
var goodZip=Path.Combine(root,"portable.zip");using(var a=ZipFile.Open(goodZip,ZipArchiveMode.Create)){var entry=a.CreateEntry("bin/app.txt");using var w=new StreamWriter(entry.Open());w.Write("ok");}
var destination=WindowsServices.ExtractPortable(goodZip);Check(File.ReadAllText(Path.Combine(destination,"bin","app.txt"))=="ok","portable extraction into new folder");
Console.WriteLine($"All {count} checks passed. Fixtures: {root}");
if(args.Contains("--avatar-live")){AvatarCache.Configure(new());var avatar=await AvatarCache.Load("x6nux");Check(avatar!=null && avatar.PixelWidth>0,"live author avatar downloaded and decoded");Check(ReferenceEquals(avatar,await AvatarCache.Load("x6nux")),"author avatar reused without a second download");}
if(args.Contains("--live-public")){using var real=new GitHubClient(new());var repository=await real.Repo("komi-store/komi-store");var realReleases=await real.Releases(repository.FullName,CancellationToken.None);Check(realReleases.Count>0,"live public GitHub release query");Console.WriteLine($"Live: {repository.FullName}, {realReleases.Count} candidates, selected {Rules.SelectRelease(realReleases,new Binding())?.Tag}");}
} catch(Exception e){Console.Error.WriteLine(e.ToString());Environment.ExitCode=1;}

sealed class FakeHttp(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>action(r,ct);}

static class MsiFixture
{
    public static void Create(string path,string template)
    {
        void Ok(uint code){if(code!=0)throw new IOException("MSI fixture creation failed: "+code);}
        Ok(MsiOpenDatabase(path,new IntPtr(3),out var database));uint summary=0;
        try{Ok(MsiGetSummaryInformation(database,null,1,out summary));Ok(MsiSummaryInfoSetProperty(summary,7,30,0,IntPtr.Zero,template));Ok(MsiSummaryInfoPersist(summary));Ok(MsiDatabaseCommit(database));}
        finally{if(summary!=0)MsiCloseHandle(summary);MsiCloseHandle(database);}
    }
    [System.Runtime.InteropServices.DllImport("msi.dll",EntryPoint="MsiOpenDatabaseW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] private static extern uint MsiOpenDatabase(string path,IntPtr mode,out uint database);
    [System.Runtime.InteropServices.DllImport("msi.dll",EntryPoint="MsiGetSummaryInformationW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] private static extern uint MsiGetSummaryInformation(uint database,string? path,uint count,out uint summary);
    [System.Runtime.InteropServices.DllImport("msi.dll",EntryPoint="MsiSummaryInfoSetPropertyW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] private static extern uint MsiSummaryInfoSetProperty(uint summary,uint property,uint type,int value,IntPtr time,string text);
    [System.Runtime.InteropServices.DllImport("msi.dll")] private static extern uint MsiSummaryInfoPersist(uint summary);
    [System.Runtime.InteropServices.DllImport("msi.dll")] private static extern uint MsiDatabaseCommit(uint database);
    [System.Runtime.InteropServices.DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
}



