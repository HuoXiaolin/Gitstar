using Gitstar.Desktop;
using System.IO;
using System.Net;
using System.Net.Http;

internal static class AuthenticationChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        foreach(var replaceAccount in new[]{false,true}){
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var responseReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var saved=0;
            using var handler=new FakeHttp(async(request,ct)=>{
                if(request.RequestUri!.AbsoluteUri!="https://github.com/login/oauth/access_token")throw new Exception("Unexpected authentication request");
                entered.TrySetResult();await responseReady.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"access_token\":\"old-account-refreshed\",\"refresh_token\":\"old-account-next-refresh\",\"expires_in\":3600}")};
            });
            using var api=new GitHubClient(new(),handler);
            api.SetCredentials(new("old-account","old-refresh",DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddDays(1),"repo user","fixture-client"));
            api.CredentialsChanged=_=>saved++;
            var refresh=api.EnsureFreshToken();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var replacement=new GitHubCredential("different-account","different-refresh",DateTimeOffset.UtcNow.AddHours(1),DateTimeOffset.UtcNow.AddDays(1),"read:user","fixture-client");
            if(replaceAccount)api.SetCredentials(replacement);else api.Token="";
            responseReady.TrySetResult();await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            check(saved==0&&(replaceAccount?api.Credentials==replacement:api.Token.Length==0&&api.Credentials.RefreshToken.Length==0),replaceAccount?"late token refresh cannot overwrite a newly authorized account or persist old credentials":"late token refresh cannot restore credentials after logout or invoke persistence");
        }
        var profileEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var profileReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
        using(var handler=new FakeHttp(async(request,ct)=>{calls++;profileEntered.TrySetResult();await profileReady.Task.WaitAsync(ct);var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"login\":\"old-account\"}")};response.Headers.Add("X-OAuth-Scopes","repo, user");return response;}))using(var api=new GitHubClient(new(),handler)){
            api.SetCredentials(new("old-account",Scope:"repo user"));var profile=api.Account(CancellationToken.None);await profileEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            api.SetCredentials(new("new-account",Scope:"repo read:user"));profileReady.TrySetResult();await profile.WaitAsync(TimeSpan.FromSeconds(5));
            var refused=false;try{await api.EnsureListWritePermission();}catch(IOException error){refused=error.Message.Contains("重新授权");}
            check(refused&&calls==1,"late response scope headers cannot grant the new account permissions from the old account");
        }
    }
}
