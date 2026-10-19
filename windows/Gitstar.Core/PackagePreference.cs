using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace Gitstar.Core;

public static class PackagePreference
{
    public static Asset? Select(IEnumerable<Asset> assets,Architecture host,string pattern="",bool portable=false)
    {
        var ranked=assets.Select((asset,index)=>(Asset:asset,Index:index,Score:Score(asset,host,portable))).Where(item=>item.Score>=0).ToArray();
        var matching=ranked.Where(item=>pattern.Length>0&&item.Asset.Name.Contains(pattern,StringComparison.OrdinalIgnoreCase)).ToArray();
        return (matching.Length>0?matching:ranked).OrderByDescending(item=>item.Score).ThenBy(item=>item.Index).Select(item=>item.Asset).FirstOrDefault();
    }
    private static int Score(Asset asset,Architecture host,bool portable)
    {
        if(asset.Size<=0)return -1;
        var name=asset.Name.ToLowerInvariant().Replace("x86_64","x64").Replace("x86-64","x64");
        var extension=Path.GetExtension(name);var type=extension switch{".exe"=>60,".msi"=>50,".msix" or ".msixbundle"=>45,".appx" or ".appxbundle"=>40,".zip"=>20,".7z"=>10,_=>-1};if(type<0)return -1;
        if(portable){type=extension switch{".zip"=>80,".7z"=>70,".exe" when !name.Contains("setup")&&!name.Contains("installer")=>60,_=>-1};if(type<0)return -1;}
        if(Regex.IsMatch(name,@"(?:^|[._\- ])(?:linux|darwin|macos|osx|mac|android|ubuntu|debian|source|sources|src|symbols|debug|pdb)(?:[._\- ]|$)"))return -1;
        var arm64=name.Contains("arm64")||name.Contains("aarch64");var arm32=!arm64&&(name.Contains("armv")||Regex.IsMatch(name,@"(?:^|[._\- ])arm(?:[._\- ]|$)"));
        var x64=name.Contains("x64")||name.Contains("amd64")||name.Contains("win64")||name.Contains("64bit")||name.Contains("64-bit");
        var x86=!x64&&(name.Contains("x86")||name.Contains("i386")||name.Contains("i686")||name.Contains("win32")||name.Contains("ia32")||name.Contains("32bit")||name.Contains("32-bit"));
        if(name.Contains("ia64")||name.Contains("intel64"))return -1;
        var native=host switch{Architecture.Arm64=>arm64,Architecture.Arm=>arm32,Architecture.X64=>x64,Architecture.X86=>x86,_=>false};
        var unknown=!arm64&&!arm32&&!x64&&!x86;
        var compatible=host switch{Architecture.Arm64=>x64||x86,Architecture.X64=>x86,_=>false};
        if(!native&&!unknown&&!compatible)return -1;
        return (native?300:unknown?200:100)+type;
    }
}
