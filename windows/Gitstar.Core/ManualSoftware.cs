using System.Security.Cryptography;
using System.Text;
namespace Gitstar.Core;

public static class ManualSoftware
{
    public static string PortableId(string path)=>"portable/"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..24];
    public static InstalledApp Save(Settings settings,InstalledApp selected,IEnumerable<InstalledApp> known,string repository,bool beta,string prefix,string pattern,bool? portableOverride=null)
    {
        var repo=Rules.RepositoryName(repository);if(!Rules.ValidRepository(repo))throw new IOException("请填写 GitHub 仓库地址，例如 https://github.com/owner/repo 或 owner/repo。");
        if(string.IsNullOrWhiteSpace(selected.Id)||string.IsNullOrWhiteSpace(selected.Name))throw new IOException("请先选择软件主程序或已安装的软件。");
        var app=selected;
        if(app.ExecutablePath.Length>0){
            var path=Path.GetFullPath(app.ExecutablePath);
            var previous=settings.PortableApps.Concat(known).FirstOrDefault(item=>item.ExecutablePath.Length>0&&Path.GetFullPath(item.ExecutablePath).Equals(path,StringComparison.OrdinalIgnoreCase));
            previous??=settings.PortableApps.FirstOrDefault(item=>item.ExecutablePath.Length==0&&item.Location.Equals(app.Location,StringComparison.OrdinalIgnoreCase)&&item.Name.Equals(app.Name,StringComparison.OrdinalIgnoreCase));
            if(previous!=null)app=app with{Id=previous.Id,Portable=previous.Portable,AliasIds=previous.AliasIds};
        }
        if(portableOverride.HasValue)app=app with{Portable=portableOverride.Value};
        var ids=(app.AliasIds??[]).Append(app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);var bindings=settings.Bindings.Where(binding=>ids.Contains(binding.AppId)).ToArray();
        if(bindings.Length>1)throw new IOException("该软件已有多个关联，请先在软件管理中保留一个关联。");
        if(bindings.FirstOrDefault() is {} binding)BindingManagement.Update(settings,binding,app,repo,app.ExecutablePath,beta,prefix,pattern,known);
        else settings.Bindings.Add(new(){AppId=app.Id,Repository=repo,ExecutablePath=app.ExecutablePath,IncludePrerelease=beta,TagPrefix=prefix.Trim(),AssetPattern=pattern.Trim()});
        InstalledIdentity.Remember(settings.Bindings.Single(binding=>binding.AppId.Equals(app.Id,StringComparison.OrdinalIgnoreCase)),app);
        settings.PortableApps.RemoveAll(item=>item.Id==app.Id);if(app.Portable)settings.PortableApps.Add(app);
        return app;
    }
}
