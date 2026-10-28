using Gitstar.Core;
using System.IO;
internal static class SoftwareInventoryChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var folder=Path.Combine(root,"inventory","Editor");var main=Path.Combine(folder,"Editor.exe");var helper=Path.Combine(folder,"editor-launcher.exe");
        var installed=new InstalledApp("registered/editor","Editor","2.0",folder,ExecutablePath:main);var found=installed with{Id="portable/editor"};
        var merged=SoftwareInventory.Combine([installed],[found]);check(merged.Count==1&&merged[0].AliasIds!.Contains(found.Id),"registered and discovered executable share one row and preserve aliases");
        merged=SoftwareInventory.Combine([installed with{ExecutablePath="",Location=""}],[found]);check(merged.Count==1&&merged[0].ExecutablePath==main,"unique version-matched discovery supplies missing registration path");
        var other=found with{Id="other",ExecutablePath=Path.Combine(root,"second","Editor.exe"),Location=Path.Combine(root,"second")};
        merged=SoftwareInventory.Combine([installed with{ExecutablePath="",Location=""}],[found,other]);check(merged.Count==3,"same-name applications in different locations cannot be attached to a pathless registration by guess");
        check(SoftwareInventory.Combine([installed],[found with{Id="helper",ExecutablePath=helper}]).Count==1,"sibling launcher discovery does not create another installed product row");
        var protectedBinding=new Binding{AppId="helper",ExecutablePath=helper,Repository="owner/editor"};check(SoftwareInventory.Combine([installed],[found with{Id="helper",ExecutablePath=helper}],[protectedBinding]).Count==2,"explicitly bound executable remains visible even when sibling product is registered");
        var a=installed with{Id="CurrentUser/Registry64/editor",ExecutablePath=""};var b=a with{Id="CurrentUser/Registry32/editor"};merged=InstalledIdentity.Merge([a,b]);check(merged.Count==1&&merged[0].AliasIds!.Contains(b.Id),"mirrored registry views with identical key location name and version merge without executable evidence");
        check(InstalledIdentity.Merge([a,b with{Location=other.Location}]).Count==2,"registry view records at different locations stay distinct");
        check(SoftwareInventory.Combine([installed],[other]).Count==2,"different physical installation remains a separate product");
        var unknown=installed with{Version="未知",Location="",ExecutablePath=""};merged=SoftwareInventory.Combine([unknown],[found with{Version="未知"},found with{Id="helper",Version="未知",ExecutablePath=helper}]);check(merged.Count==1&&merged[0].ExecutablePath==main,"unique main file is selected within one unambiguous unknown-version installation scope");
        check(SoftwareInventory.Combine([], [found,found with{Id="helper",ExecutablePath=helper}]).Count==1,"unregistered folder prefers uniquely product-named main over sibling launcher");
        check(SoftwareInventory.Combine([installed with{Portable=true}],[found]).Single().Portable,"discovery cannot reset an explicitly selected portable update mode");
        var volume=Path.GetPathRoot(root)!;var atRoot=found with{Location=volume,ExecutablePath=Path.Combine(volume,"Editor.exe")};check(SoftwareInventory.Combine([atRoot],[atRoot with{Id="root-other",ExecutablePath=Path.Combine(volume,"Editor-other.exe")}]).Count==2,"sharing a disk root never proves two executables belong to one installation");
        check(SearchText.Score("Quick-Look","quick look")>0&&SearchText.Score("example/editor","edtr")>0&&SearchText.Score("example/editor","missing") ==0,"local fuzzy search handles spaces separators and ordered abbreviations");
    }
}
