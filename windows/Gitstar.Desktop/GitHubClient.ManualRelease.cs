namespace Gitstar.Desktop;

public sealed partial class GitHubClient
{
    public async Task<JsonElement> PrepareManualRelease(string repo,string tag,string commit,string body,bool beta,long? resume,CancellationToken ct,bool overwrite=false)
    {
        if(!ReleaseRules.ValidTag(tag))throw new IOException("请填写有效版本号。");await EnsureFreshToken(ct);
        var found=await AutomationRelease(repo,tag,"",beta,ct);JsonElement? release=found.Release;
        if(release==null&&resume is {} id){var previous=await ReleaseDetails(repo,id,ct);if(previous.GetProperty("draft").GetBoolean()&&(ReleaseRules.TemporaryTag(S(previous,"tag_name"))||Rules.CompareVersions(S(previous,"tag_name"),tag)==0))release=previous;}
        if(release is {} current){
            if(!overwrite)throw new IOException("同版本已存在，默认跳过上传。请重新点击“更新发布版本”并确认强制覆盖。");
            current=await ReleaseDetails(repo,current.GetProperty("id").GetInt64(),ct);
            if(!ReleaseRules.TemporaryTag(S(current,"tag_name"))&&S(current,"tag_name")!=tag&&Rules.CompareVersions(S(current,"tag_name"),tag)!=0)throw new IOException("远程版本号已经改变，请同步版本后重试。");
            if(current.TryGetProperty("immutable",out var locked)&&locked.GetBoolean())throw new IOException("此发布版本已锁定，无法覆盖安装包。请填写新版本号。");
            // Existing published tags retain their commit and visibility throughout attachment updates.
            if(current.GetProperty("draft").GetBoolean()){
                var existingTag=S(current,"tag_name");var target=ReleaseRules.TemporaryTag(existingTag)?tag:existingTag;
                await EditDraft(repo,current.GetProperty("id").GetInt64(),target,body.Length>0?body:S(current,"body"),beta,ct,target);
                current=await ReleaseDetails(repo,current.GetProperty("id").GetInt64(),ct);
            }
            return current;
        }
        if(!System.Text.RegularExpressions.Regex.IsMatch(commit,@"^[a-fA-F0-9]{40}$"))throw new IOException("无法确认新版本对应的源码提交，请先上传源码，或在高级操作填写已推送的提交 SHA。");
        await VerifyCommit(repo,commit,ct);return await CreateDraft(repo,tag,commit,body,beta,ct);
    }
}
