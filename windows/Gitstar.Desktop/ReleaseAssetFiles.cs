using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace Gitstar.Desktop;

public record PreparedReleaseAsset(string Name,string Path,long Size,string Sha,string OriginalName);
public static class ReleaseAssetFiles
{
    public static async Task<List<PreparedReleaseAsset>> Stage(IEnumerable<string> paths,string stage,CancellationToken ct,Action<string>? progress=null)
    {
        Directory.CreateDirectory(stage);var files=new List<PreparedReleaseAsset>();
        foreach(var path in paths){
            ct.ThrowIfCancellationRequested();var file=new FileInfo(path);if(file.Length==0||file.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("附件为空或是文件链接："+file.Name);
            progress?.Invoke(file.Name);var copy=System.IO.Path.Combine(stage,file.Name);
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true))using(var output=new FileStream(copy,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))await input.CopyToAsync(output,ct);
            using var data=File.OpenRead(copy);files.Add(new(ReleaseRules.AssetName(file.Name),copy,data.Length,Convert.ToHexString(await SHA256.HashDataAsync(data,ct)),file.Name));
        }
        if(files.GroupBy(item=>item.Name,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()>1))throw new IOException("附件的兼容名称重复，请调整文件名后重试。");
        var manifest=files.FirstOrDefault(item=>item.Name.Equals("SHA256SUMS.txt",StringComparison.OrdinalIgnoreCase));
        if(manifest!=null){
            var verified=0;var lines=await File.ReadAllLinesAsync(manifest.Path,ct);var renamed=false;
            for(var index=0;index<lines.Length;index++){
                var line=lines[index];if(string.IsNullOrWhiteSpace(line)||line.TrimStart().StartsWith('#'))continue;
                var match=Regex.Match(line,@"^([A-Fa-f0-9]{64})\s+\*?(.+)$");if(!match.Success)throw new IOException("SHA256SUMS.txt 格式无效。");
                var item=files.FirstOrDefault(item=>item.OriginalName.Equals(match.Groups[2].Value,StringComparison.OrdinalIgnoreCase));if(item==null)continue;
                if(!item.Sha.Equals(match.Groups[1].Value,StringComparison.OrdinalIgnoreCase))throw new IOException("发行文件与校验清单不一致，可能仍在构建："+item.OriginalName);verified++;
                if(item.Name!=item.OriginalName){lines[index]=item.Sha+"  "+item.Name;renamed=true;}
            }
            if(verified==0&&files.Count>1)throw new IOException("校验清单没有覆盖所选安装包，请完成构建后重试。");
            if(renamed){await File.WriteAllTextAsync(manifest.Path,string.Join("\n",lines)+"\n",new UTF8Encoding(false),ct);using var input=File.OpenRead(manifest.Path);files[files.IndexOf(manifest)]=manifest with{Size=input.Length,Sha=Convert.ToHexString(await SHA256.HashDataAsync(input,ct))};}
        }
        return files;
    }
}
