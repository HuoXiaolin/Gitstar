using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Gitstar.Core;
using Gitstar.Desktop;

internal static class PackageManagementChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var source=Path.Combine(root,"package-manager");var target=Path.Combine(source,"export");Directory.CreateDirectory(target);
        var bytes=Enumerable.Range(0,300000).Select(i=>(byte)(i%251)).ToArray();
        DownloadedPackage Make(string name="Original Setup-x64.exe"){
            var path=Path.Combine(source,name);File.WriteAllBytes(path,bytes);
            return new("example/editor",1,"v2.0.0",false,DateTimeOffset.UtcNow,name,path,source,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),"windows/x64"){DownloadedAt=DateTimeOffset.Now};
        }
        async Task<bool> Refused(Func<Task> action){try{await action();return false;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return true;}}
        var package=Make();check(ManagedPackageFiles.Inspect(package).Availability==PackageAvailability.Ready,"package manager inspects recorded file without hashing on list load");
        var copied=await ManagedPackageFiles.Export(package,target);var destination=Path.Combine(target,package.AssetName);
        check(copied&&File.Exists(destination)&&File.ReadAllBytes(destination).SequenceEqual(bytes),"export copies verified package under exact Release filename");
        check(File.ReadAllBytes(package.Path).SequenceEqual(bytes),"export preserves source installer");
        check(!await ManagedPackageFiles.Export(package,target),"identical existing export is verified and reused without overwrite");
        check(!await ManagedPackageFiles.Export(package,source),"export to source directory is verified no-op");
        File.WriteAllText(destination,"existing user's file");
        check(await Refused(()=>ManagedPackageFiles.Export(package,target))&&File.ReadAllText(destination)=="existing user's file","different destination file preserved on export name collision");
        var conflicting=package with{Repository="other/editor",Sha256=new string('A',64)};
        check(await Refused(()=>Task.Run(()=>ManagedPackageFiles.ExportSelection([package,conflicting]))),"different same-name packages require separate export folders");
        check(ManagedPackageFiles.ExportSelection([package,package with{Repository="other/editor"}]).Length==1,"identical same-name exports deduplicate safely");
        var corrupt=Make("Changed-x64.exe");var changed=bytes.ToArray();changed[22]^=1;File.WriteAllBytes(corrupt.Path,changed);
        check(await Refused(()=>ManagedPackageFiles.Export(corrupt,target))&&!File.Exists(Path.Combine(target,corrupt.AssetName)),"same-size source tampering cannot be exported");
        check(await Refused(()=>ManagedPackageFiles.Delete(corrupt,[]))&&File.ReadAllBytes(corrupt.Path).SequenceEqual(changed),"same-size tampering cannot be deleted by download record");
        var protectedFile=Make("Protected-x64.exe");
        check(await Refused(()=>ManagedPackageFiles.Delete(protectedFile,[protectedFile.Path]))&&File.Exists(protectedFile.Path),"associated program executable protected from explicit package deletion");
        var alias=Path.Combine(source,"InstalledProgram.exe");if(!CreateHardLinkW(alias,protectedFile.Path,IntPtr.Zero))throw new IOException("Unable to create isolated hardlink fixture");
        check(await Refused(()=>ManagedPackageFiles.Delete(protectedFile,[alias]))&&File.Exists(protectedFile.Path),"program hardlink alias protected by actual file identity");
        using(var held=File.Open(protectedFile.Path,FileMode.Open,FileAccess.Read,FileShare.Read))check(await Refused(()=>ManagedPackageFiles.Delete(protectedFile,[])),"busy package cannot be removed while open elsewhere");
        var missing=package with{Path=Path.Combine(source,"Missing.exe"),AssetName="Missing.exe"};
        check(ManagedPackageFiles.Inspect(missing).Availability==PackageAvailability.Missing&&!await ManagedPackageFiles.Delete(missing,[]),"missing managed file can clear stale record without deleting a path");
        var unsafePath=package with{Path=Path.Combine(target,package.AssetName)};
        check(await Refused(()=>ManagedPackageFiles.Delete(unsafePath,[]))&&File.Exists(destination),"unrecorded location cannot be deleted even when filename is known");
        check(await Refused(()=>ManagedPackageFiles.Export(package with{AssetName="../Escape.exe"},target)),"export rejects unsafe asset filename rather than escaping destination");
        var cancelPackage=Make("Cancelled-x64.exe");var userPartial=Path.Combine(target,".gitstar-export-user.partial");File.WriteAllText(userPartial,"keep");
        using(var cancel=new CancellationTokenSource()){
            var stopped=false;try{await ManagedPackageFiles.Export(cancelPackage,target,cancel.Token,_=>cancel.Cancel());}catch(OperationCanceledException){stopped=true;}
            check(stopped&&!File.Exists(Path.Combine(target,cancelPackage.AssetName))&&Directory.GetFiles(target,"*.partial").SequenceEqual([userPartial]),"cancelled export removes only its own partial file and never commits incomplete output");
        }
        using(var cancel=new CancellationTokenSource()){
            cancel.Cancel();var stopped=false;try{await ManagedPackageFiles.Delete(cancelPackage,[],cancel.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped&&File.Exists(cancelPackage.Path),"cancelled deletion preserves verified package");
        }
        var readOnly=Make("ReadOnly-x64.exe");File.SetAttributes(readOnly.Path,FileAttributes.ReadOnly);
        try{check(await Refused(()=>ManagedPackageFiles.Delete(readOnly,[]))&&File.Exists(readOnly.Path),"read-only package is preserved without changing attributes");}finally{File.SetAttributes(readOnly.Path,FileAttributes.Normal);}
        var deletable=Make("Deletable-x64.exe");check(await ManagedPackageFiles.Delete(deletable,[])&&!File.Exists(deletable.Path),"explicit delete removes only unchanged verified file");
        var legacy=JsonSerializer.Deserialize<DownloadedPackage>(JsonSerializer.Serialize(package with{DownloadedAt=null}))!;
        check(legacy.DownloadedAt==null&&JsonSerializer.Deserialize<DownloadedPackage>(JsonSerializer.Serialize(package))!.DownloadedAt==package.DownloadedAt,"download time roundtrips while legacy records retain unknown time");
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateHardLinkW(string link,string existing,IntPtr security);
}
