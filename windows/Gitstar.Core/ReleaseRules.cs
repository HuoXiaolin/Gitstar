using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace Gitstar.Core;

public static class ReleaseRules
{
    public static bool TemporaryTag(string tag)=>Regex.IsMatch(tag,@"^untagged-[0-9a-f]+$",RegexOptions.IgnoreCase);
    public static bool ValidTag(string tag)=>tag.Length>0&&!TemporaryTag(tag)&&!tag.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)||"~^:?*[\\".Contains(c))&&!tag.Contains("..")&&!tag.Contains("@{")&&!tag.Contains("//")&&tag!="@"&&!tag.EndsWith('.')&&!tag.EndsWith('/')&&tag.Split('/').All(part=>part.Length>0&&!part.StartsWith('.')&&!part.EndsWith(".lock",StringComparison.OrdinalIgnoreCase));
    public static string AssetName(string name)
    {
        if(Regex.IsMatch(name,@"^[A-Za-z0-9_-][A-Za-z0-9._-]*$")&&!name.EndsWith('.'))return name;
        if(name=="使用说明.md")return "USAGE.md";
        var extension=Path.GetExtension(name);if(!Regex.IsMatch(extension,@"^\.[A-Za-z0-9]+$"))extension="";
        return "asset-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16].ToLowerInvariant()+extension;
    }
}
