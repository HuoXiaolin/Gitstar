using System.Net;
using System.Net.Http.Headers;
using System.Text;
namespace Gitstar.Desktop;

public sealed partial class GitHubClient : IDisposable
{
    public sealed record ReleasePage(string ETag,string Signature,int Count,List<Release> Candidates);
    public Func<string,ReleasePage?>? ReadReleasePage {get;set;}
    public Action<string,ReleasePage>? SaveReleasePage {get;set;}
    private readonly HttpClient http;
    private readonly string clientId;
    private string token="";
    private string[]? oauthScopes;
    public const string OAuthScopes="repo user workflow";
    public string Token { get=>token;set{if(token!=value){oauthScopes=null;credential=null;}token=value;} }
    public GitHubClient(Settings settings,HttpMessageHandler? transport=null)
    {
        clientId=settings.ClientId;
        var handler = new HttpClientHandler { AllowAutoRedirect=false, UseProxy=!settings.DirectConnection };
        if (settings.Proxy.Length > 0) { var u = new Uri(settings.Proxy); if (u.UserInfo.Length > 0) throw new ArgumentException("代理地址中不要包含密码。"); handler.Proxy = new WebProxy(u); }
        http = new HttpClient(transport??handler) { Timeout=TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Gitstar/"+(typeof(Program).Assembly.GetName().Version?.ToString()??"1.0.0.0"));
    }
    private async Task<JsonElement> Request(string url, HttpMethod? method=null, object? body=null, CancellationToken ct=default,Action<JsonElement>? consume=null,string? etag=null,Action<string,bool>? cacheResult=null)
    {
        await EnsureFreshToken(ct);
        using var msg = new HttpRequestMessage(method ?? HttpMethod.Get, url.StartsWith("https://",StringComparison.Ordinal) ? url : "https://api.github.com/" + url);
        if (msg.RequestUri!.Host is not ("api.github.com" or "uploads.github.com")) throw new ArgumentException("无效的 GitHub API 主机。");
        msg.Headers.Accept.ParseAdd("application/vnd.github+json"); msg.Headers.Add("X-GitHub-Api-Version","2022-11-28");
        if(etag!=null && EntityTagHeaderValue.TryParse(etag,out var tag))msg.Headers.IfNoneMatch.Add(tag);
        if (Token.Length > 0) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer",Token);
        if (body != null) msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,"application/json");
        else if(msg.Method==HttpMethod.Put)msg.Content=new ByteArrayContent([]);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        if(msg.Method==HttpMethod.Get)deadline.CancelAfter(TimeSpan.FromSeconds(30));
        HttpResponseMessage received;
        try{received=await http.SendAsync(msg,HttpCompletionOption.ResponseHeadersRead,deadline.Token);}catch(OperationCanceledException ex)when(!ct.IsCancellationRequested){throw new IOException("GitHub 请求超时，请检查网络或代理后重试。",ex);}
        using var response = received;
        if(response.Headers.TryGetValues("X-OAuth-Scopes",out var scopes))oauthScopes=string.Join(",",scopes).Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(response.StatusCode==HttpStatusCode.NotModified && msg.Headers.IfNoneMatch.Count>0){cacheResult?.Invoke(etag!,true);return default;}
        if (!response.IsSuccessStatusCode) {
            var content=await response.Content.ReadAsStringAsync(deadline.Token);
            var reason = response.StatusCode switch { HttpStatusCode.Unauthorized=>"登录已失效，请重新授权。", HttpStatusCode.Forbidden=>"权限不足或请求额度已用尽，请稍后重试。", HttpStatusCode.NotFound=>"仓库/版本不存在，或当前账号没有权限。", _=>$"GitHub 请求失败：{(int)response.StatusCode}" };
            if(response.Headers.TryGetValues("X-RateLimit-Remaining",out var remaining)&&remaining.FirstOrDefault()=="0"&&response.Headers.TryGetValues("X-RateLimit-Reset",out var reset)&&long.TryParse(reset.FirstOrDefault(),out var seconds))reason=$"GitHub 请求额度已用尽，可在 {DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime():MM-dd HH:mm} 后重试。";
            if(response.StatusCode==(HttpStatusCode)429 || (response.StatusCode==HttpStatusCode.Forbidden && (content.Contains("rate limit",StringComparison.OrdinalIgnoreCase)||response.Headers.Contains("Retry-After")||response.Headers.TryGetValues("X-RateLimit-Remaining",out var quota)&&quota.FirstOrDefault()=="0")))throw new GitHubRateLimitException(reason);
            if(response.StatusCode==HttpStatusCode.NotFound)throw new GitHubNotFoundException(reason);
            throw new IOException(reason);
        }
        cacheResult?.Invoke(response.Headers.ETag?.ToString()??"",false);
        if(response.StatusCode==HttpStatusCode.NoContent || response.Content.Headers.ContentLength==0)return default;
        try{using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var doc=await JsonDocument.ParseAsync(input,cancellationToken:deadline.Token);if(consume!=null){consume(doc.RootElement);return default;}return doc.RootElement.Clone();}
        catch(OperationCanceledException ex)when(!ct.IsCancellationRequested){throw new IOException("GitHub 响应读取超时，请稍后重试。",ex);}
    }
    private static string S(JsonElement e,string key) => e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String ? v.GetString()! : "";
    public async Task<string> Account(CancellationToken ct) => S(await Request("user",ct:ct),"login");
    public async Task<List<Repository>> Stars(CancellationToken ct)
    {
        List<Repository> list=[];
        for (var page=1;;page++) { var data=await Request($"user/starred?per_page=100&page={page}",ct:ct); var items=data.EnumerateArray().ToArray();
            list.AddRange(items.Select(e=>new Repository(S(e,"full_name"),S(e,"description"),e.GetProperty("private").GetBoolean(),true,S(e,"node_id")))); if(items.Length<100) return list;
        }
    }
    public async Task<Repository> Repo(string repo,CancellationToken ct=default)
    {
        Validate(repo); var e=await Request("repos/"+repo,ct:ct); return new(S(e,"full_name"),S(e,"description"),e.GetProperty("private").GetBoolean(),false,S(e,"node_id"));
    }
    public async Task<List<Release>> Releases(string repo,CancellationToken ct,IEnumerable<Binding>? bindings=null)
    {
        Validate(repo);var subscriptions=bindings?.ToArray()??[];var selected=new ReleaseCandidates(repo,subscriptions);
        var signature=string.Join("\n",subscriptions.Where(b=>b.Repository.Equals(repo,StringComparison.OrdinalIgnoreCase)).Select(b=>b.TagPrefix).Append("").Distinct().Order(StringComparer.Ordinal));
        // Each page is verified remotely, including historical deletion/editing, without retaining raw JSON.
        for(var page=1;;page++) {
            var key=$"{repo.ToLowerInvariant()}/{page}";var cached=ReadReleasePage?.Invoke(key);if(cached?.Signature!=signature)cached=null;
            var count=0;var unchanged=false;var nextTag="";var pageCandidates=new ReleaseCandidates(repo,subscriptions);
            await Request($"repos/{repo}/releases?per_page=30&page={page}",ct:ct,etag:cached?.ETag,cacheResult:(tag,same)=>{nextTag=tag;unchanged=same;},consume:data=>{
                count=data.GetArrayLength();foreach(var e in data.EnumerateArray())if(!e.GetProperty("draft").GetBoolean()){
                    var tag=S(e,"tag_name");var beta=e.GetProperty("prerelease").GetBoolean();var published=DateTimeOffset.TryParse(S(e,"published_at"),out var date)?date:DateTimeOffset.MinValue;
                    if(pageCandidates.Needs(tag,beta,published))pageCandidates.Add(ParseRelease(repo,e));
                }
            });
            if(unchanged){count=cached!.Count;foreach(var item in cached.Candidates)selected.Add(item);}
            else{var values=pageCandidates.Values;foreach(var item in values)selected.Add(item);SaveReleasePage?.Invoke(key,new(nextTag,signature,count,values));}
            if(count<30)return selected.Values;
        }
    }
    private static Release ParseRelease(string repo,JsonElement e) => new(e.GetProperty("id").GetInt64(),repo,S(e,"tag_name"),S(e,"name"),S(e,"body"),e.GetProperty("prerelease").GetBoolean(),DateTimeOffset.TryParse(S(e,"published_at"),out var date)?date:DateTimeOffset.MinValue,S(e,"html_url"),e.GetProperty("assets").EnumerateArray().Select(a=>new Asset(a.GetProperty("id").GetInt64(),S(a,"name"),a.GetProperty("size").GetInt64(),S(a,"browser_download_url"),S(a,"digest"))).ToList());
    public async Task<List<StarGroup>> Lists(CancellationToken ct)
    {
        List<StarGroup> groups=[]; string? cursor=null;
        do {
            var result=await Request("graphql",HttpMethod.Post,new { query="query($cursor:String){viewer{lists(first:100,after:$cursor){nodes{id name description isPrivate items(first:100){nodes{... on Repository{nameWithOwner}} pageInfo{hasNextPage endCursor}}} pageInfo{hasNextPage endCursor}}}}",variables=new {cursor}},ct);
            if(result.TryGetProperty("errors",out _)) throw new IOException("GitHub Lists 读取受限，可导入或补充本地分组。");
            var conn=result.GetProperty("data").GetProperty("viewer").GetProperty("lists");
            foreach(var g in conn.GetProperty("nodes").EnumerateArray()) {
                List<string> repos=[]; string? after=null;
                var items=g.GetProperty("items");
                do {
                    repos.AddRange(items.GetProperty("nodes").EnumerateArray().Select(x=>S(x,"nameWithOwner")).Where(Rules.ValidRepository));
                    after=items.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()?S(items.GetProperty("pageInfo"),"endCursor"):null;
                    if(after!=null){var r=await Request("graphql",HttpMethod.Post,new {query="query($id:ID!,$after:String){node(id:$id){... on UserList{items(first:100,after:$after){nodes{... on Repository{nameWithOwner}} pageInfo{hasNextPage endCursor}}}}}",variables=new {id=S(g,"id"),after}},ct);
                        if(r.TryGetProperty("errors",out _))throw new IOException("GitHub Lists 内容读取受限。");items=r.GetProperty("data").GetProperty("node").GetProperty("items");}
                } while(after!=null);
                groups.Add(new(S(g,"id"),S(g,"name"),"GitHub",repos,S(g,"description"),g.TryGetProperty("isPrivate",out var privacy)&&privacy.GetBoolean()));
            }
            cursor=conn.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()?S(conn.GetProperty("pageInfo"),"endCursor"):null;
        } while(cursor!=null); return groups;
    }
    public async Task<string> DeviceLogin(string clientId,Action<string,string> show,CancellationToken ct)=>(await DeviceAuthorization(clientId,show,ct)).AccessToken;
    public async Task<GitHubCredential> DeviceAuthorization(string clientId,Action<string,string> show,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("请先在设置填写 OAuth App Client ID，并在 GitHub 启用 Device Flow。");
        var d=await OAuthPost("device/code",new() {{"client_id",clientId},{"scope",OAuthScopes}},ct);
        if(!d.TryGetProperty("device_code",out _)) throw new IOException("OAuth App 未启用 Device Flow，或 Client ID 无效。");
        show(S(d,"user_code"),S(d,"verification_uri"));
        var interval=d.GetProperty("interval").GetInt32(); var expires=DateTimeOffset.UtcNow.AddSeconds(d.GetProperty("expires_in").GetInt32());
        while(DateTimeOffset.UtcNow<expires) {
            await Task.Delay(TimeSpan.FromSeconds(interval),ct);
            var token=await OAuthPost("oauth/access_token",new() {{"client_id",clientId},{"device_code",S(d,"device_code")},{"grant_type","urn:ietf:params:oauth:grant-type:device_code"}},ct);
            if(token.TryGetProperty("access_token",out _)) return ParseCredential(token,clientId);
            var error=S(token,"error"); if(error=="slow_down") interval+=5; else if(error!="authorization_pending") throw new IOException("授权未完成："+error);
        }
        throw new IOException("授权码已过期，请重新登录。");
    }
    public async Task<HttpResponseMessage> AssetResponse(string repo,Asset asset,CancellationToken ct)
    {
        await EnsureFreshToken(ct);Validate(repo); Uri url=new($"https://api.github.com/repos/{repo}/releases/assets/{asset.Id}");
        for(var i=0;i<6;i++) {
            using var req=new HttpRequestMessage(HttpMethod.Get,url);
            if(url.Host=="api.github.com") { req.Headers.Accept.ParseAdd("application/octet-stream"); if(Token.Length>0)req.Headers.Authorization=new("Bearer",Token); }
            var res=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)res.StatusCode is 301 or 302 or 303 or 307 or 308) { var next=res.Headers.Location; res.Dispose(); if(next==null)throw new IOException("下载重定向为空。"); url=next.IsAbsoluteUri?next:new Uri(url,next); if(url.Scheme!="https")throw new IOException("拒绝不安全的下载地址。"); continue; }
            if(!res.IsSuccessStatusCode) { var code=(int)res.StatusCode; res.Dispose(); throw new IOException($"安装包下载失败：{code}"); } return res;
        } throw new IOException("下载重定向次数过多。");
    }
    public async Task<string> CreateRepository(string name,bool privateRepo,CancellationToken ct)
    {
        if(!Rules.ValidRepository("owner/"+name))throw new ArgumentException("仓库名称不合法。");
        var e=await Request("user/repos",HttpMethod.Post,new {name,@private=privateRepo,auto_init=false},ct); return S(e,"full_name");
    }
    public async Task<JsonElement> CreateDraft(string repo,string tag,string commit,string notes,bool beta,CancellationToken ct)
    { Validate(repo); return await Request($"repos/{repo}/releases",HttpMethod.Post,new {tag_name=tag,target_commitish=commit,name=tag,body=notes,draft=true,prerelease=beta},ct); }
    public async Task VerifyCommit(string repo,string sha,CancellationToken ct)
    { Validate(repo); var e=await Request($"repos/{repo}/commits/{Uri.EscapeDataString(sha)}",ct:ct);if(!string.Equals(S(e,"sha"),sha,StringComparison.OrdinalIgnoreCase))throw new IOException("目标 commit 未能在远程确认。"); }
    public async Task Upload(string repo,long id,string path,CancellationToken ct)
    {
        await EnsureFreshToken(ct);Validate(repo); using var stream=File.OpenRead(path); using var req=new HttpRequestMessage(HttpMethod.Post,$"https://uploads.github.com/repos/{repo}/releases/{id}/assets?name={Uri.EscapeDataString(Path.GetFileName(path))}") {Content=new StreamContent(stream)};
        req.Headers.Authorization=new("Bearer",Token); req.Content.Headers.ContentType=new("application/octet-stream");
        using var res=await http.SendAsync(req,ct); if(!res.IsSuccessStatusCode)throw new IOException($"上传安装包失败：{(int)res.StatusCode}。草稿已保留。");
        using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)); if(doc.RootElement.GetProperty("size").GetInt64()!=stream.Length || S(doc.RootElement,"state")!="uploaded")throw new IOException("上传结果待核实。");
    }
    public Task<JsonElement> ReleaseDetails(string repo,long id,CancellationToken ct)=>Request($"repos/{repo}/releases/{id}",ct:ct);
    public Task<JsonElement> Publish(string repo,long id,CancellationToken ct)=>Request($"repos/{repo}/releases/{id}",HttpMethod.Patch,new {draft=false},ct);
    public static void Validate(string repo) { if(!Rules.ValidRepository(repo))throw new ArgumentException("请输入 owner/repository 格式的 GitHub 仓库。"); }
    public void Dispose()=>http.Dispose();
}

public sealed class GitHubRateLimitException(string message):IOException(message);

