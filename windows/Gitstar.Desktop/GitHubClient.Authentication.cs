using System.Net.Http.Headers;
namespace Gitstar.Desktop;

public record GitHubCredential(string AccessToken,string RefreshToken="",DateTimeOffset? ExpiresAt=null,DateTimeOffset? RefreshExpiresAt=null,string Scope="",string ClientId="");

public static class CredentialStorage
{
    public static GitHubCredential Load(LocalStore store)
    {
        var encrypted=store.Get("auth","");
        return encrypted.Length>0?JsonSerializer.Deserialize<GitHubCredential>(SecretStore.Unprotect(encrypted))!:new(SecretStore.Unprotect(store.Get("token","")));
    }
    public static void Save(LocalStore store,GitHubCredential auth)=>store.Set("auth",SecretStore.Protect(JsonSerializer.Serialize(auth)));
    public static void Clear(LocalStore store){store.Set("auth","");store.Set("token","");}
}

public sealed partial class GitHubClient
{
    private GitHubCredential? credential;
    private readonly SemaphoreSlim refreshGate=new(1,1);
    public GitHubCredential Credentials=>credential??new(Token);
    public Action<GitHubCredential>? CredentialsChanged {get;set;}
    public void SetCredentials(GitHubCredential auth){Token=auth.AccessToken;credential=auth;if(auth.Scope.Length>0)oauthScopes=auth.Scope.Split([',',' '],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);}
    private static GitHubCredential ParseCredential(JsonElement data,string clientId)
    {
        var now=DateTimeOffset.UtcNow;
        DateTimeOffset? Expiry(string field)=>data.TryGetProperty(field,out var value)&&value.TryGetInt64(out var seconds)?now.AddSeconds(seconds):null;
        var access=S(data,"access_token");if(access.Length==0)throw new IOException("GitHub 未返回访问令牌，请重新授权。");
        return new(access,S(data,"refresh_token"),Expiry("expires_in"),Expiry("refresh_token_expires_in"),S(data,"scope"),clientId);
    }
    private async Task<JsonElement> OAuthPost(string endpoint,Dictionary<string,string> fields,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://github.com/login/"+endpoint){Content=new FormUrlEncodedContent(fields)};request.Headers.Accept.ParseAdd("application/json");
        try{using var response=await http.SendAsync(request,deadline.Token);if(!response.IsSuccessStatusCode)throw new IOException($"GitHub 授权服务暂不可用（HTTP {(int)response.StatusCode}），已保留凭据，请稍后重试。");using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));return doc.RootElement.Clone();}
        catch(OperationCanceledException ex)when(!ct.IsCancellationRequested){throw new IOException("GitHub 授权服务连接超时，已保留凭据，请稍后重试。",ex);}
    }
    public async Task EnsureFreshToken(CancellationToken ct=default)
    {
        bool Due()=>credential?.ExpiresAt is {} expiry && expiry<=DateTimeOffset.UtcNow.AddMinutes(2);
        if(!Due())return;
        await refreshGate.WaitAsync(ct);
        try{
            if(!Due())return;var previous=credential!;
            if(previous.RefreshToken.Length==0 || previous.RefreshExpiresAt<=DateTimeOffset.UtcNow)throw new IOException("旧授权已到期且没有可用的续期令牌，请在设置重新授权一次。新版会保存续期令牌，之后自动续期。");
            var appId=previous.ClientId.Length>0?previous.ClientId:clientId;
            var result=await OAuthPost("oauth/access_token",new(){{"client_id",appId},{"grant_type","refresh_token"},{"refresh_token",previous.RefreshToken}},ct);
            if(result.TryGetProperty("error",out var error)){var code=error.GetString();throw new IOException(code is "bad_refresh_token" or "invalid_grant"?"GitHub 续期授权已失效，请在设置重新授权。":"GitHub 续期暂未完成，已保留凭据，请稍后重试。");}
            var next=ParseCredential(result,appId);if(next.Scope.Length==0)next=next with{Scope=previous.Scope};SetCredentials(next);CredentialsChanged?.Invoke(next);
        }finally{refreshGate.Release();}
    }
}
