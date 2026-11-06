using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Gitstar.Desktop;

public enum PackageAvailability { Ready, Missing, Unavailable }
public sealed record PackageFileState(PackageAvailability Availability,string Text);

// Operations only accept recorded, unchanged downloads. The open handle pins the file through
// verification and deletion, so a path swap cannot delete a different file after hashing.
public static class ManagedPackageFiles
{
    public static PackageFileState Inspect(DownloadedPackage package)
    {
        try{
            Validate(package);var attributes=File.GetAttributes(package.Path);
            if((attributes&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)return new(PackageAvailability.Unavailable,"文件类型已改变");
            return new FileInfo(package.Path).Length==package.Size?new(PackageAvailability.Ready,"文件可用"):new(PackageAvailability.Unavailable,"文件大小已改变");
        }catch(FileNotFoundException){return new(PackageAvailability.Missing,"文件已不存在");}
        catch(DirectoryNotFoundException){return new(PackageAvailability.Missing,"文件已不存在");}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return new(PackageAvailability.Unavailable,"无法核对文件");}
    }

    public static DownloadedPackage[] ExportSelection(IEnumerable<DownloadedPackage> packages)
    {
        var selected=packages.ToArray();foreach(var package in selected)Validate(package);
        var groups=selected.GroupBy(package=>PackageRules.OriginalFileName(package.AssetName),StringComparer.OrdinalIgnoreCase).ToArray();
        var conflict=groups.FirstOrDefault(group=>group.Select(package=>(package.Size,package.Sha256.ToUpperInvariant())).Distinct().Count()>1);
        if(conflict!=null)throw new IOException($"所选安装包存在不同内容的同名文件：{conflict.Key}。请分批导出到不同目录，文件将保持发行包原名。");
        return groups.Select(group=>group.First()).ToArray();
    }

    public static async Task<bool> Export(DownloadedPackage package,string folder,CancellationToken ct=default,Action<long>? progress=null)
    {
        Validate(package);folder=Path.GetFullPath(folder);
        if(!Directory.Exists(folder))throw new DirectoryNotFoundException("导出目录不存在，请重新选择。");
        var target=Path.Combine(folder,PackageRules.OriginalFileName(package.AssetName));
        using var source=Open(package.Path,false);
        if(source.Length!=package.Size)throw new IOException("安装包大小与下载记录不一致，请重新下载。");
        if(Path.GetFullPath(package.Path).Equals(target,StringComparison.OrdinalIgnoreCase)){await Verify(source,package,ct);return false;}
        try{
            using var existing=Open(target,false);await Verify(source,package,ct);await Verify(existing,package,ct);return false;
        }catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        catch(IOException ex){throw new IOException("导出目录已有同名文件且无法确认内容一致，已保留原文件："+package.AssetName,ex);}
        var temporary=Path.Combine(folder,".gitstar-export-"+Guid.NewGuid().ToString("N")+".partial");var owned=false;
        try{
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true)){
                owned=true;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[131072];long copied=0;int read;var reporting=Stopwatch.StartNew();var first=true;
                while((read=await source.ReadAsync(buffer,ct))>0){await output.WriteAsync(buffer.AsMemory(0,read),ct);hash.AppendData(buffer,0,read);copied+=read;if(first||copied==package.Size||reporting.ElapsedMilliseconds>=150){progress?.Invoke(copied);first=false;reporting.Restart();}}
                if(copied!=package.Size||!Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("复制校验未通过，未生成导出文件。");
                await output.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();File.Move(temporary,target,false);owned=false;return true;
        }finally{if(owned)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    public static async Task<bool> Delete(DownloadedPackage package,IEnumerable<string> protectedPrograms,CancellationToken ct=default)
    {
        Validate(package);var protectedPaths=protectedPrograms.Where(path=>!string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).ToArray();
        if(protectedPaths.Contains(Path.GetFullPath(package.Path),StringComparer.OrdinalIgnoreCase))throw new IOException("此文件是已关联的主程序，不能作为安装包删除。");
        FileStream stream;try{stream=Open(package.Path,true);}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
        using(stream){
            await Verify(stream,package,ct);var identity=Info(stream.SafeFileHandle);
            foreach(var path in protectedPaths){
                using var program=CreateFileW(path,0,7,IntPtr.Zero,3,0,IntPtr.Zero);
                if(!program.IsInvalid){var other=Info(program);if(identity.Volume==other.Volume&&identity.IndexHigh==other.IndexHigh&&identity.IndexLow==other.IndexLow)throw new IOException("此文件与已关联的主程序指向同一文件，不能删除。");}
            }
            ct.ThrowIfCancellationRequested();byte disposition=1;
            if(!SetFileInformationByHandle(stream.SafeFileHandle,4,ref disposition,1))throw new IOException("文件正在使用、只读或没有删除权限。",new Win32Exception(Marshal.GetLastWin32Error()));
        }
        return true;
    }

    public static async Task Verify(DownloadedPackage package,CancellationToken ct=default)
    {
        Validate(package);using var stream=Open(package.Path,false);await Verify(stream,package,ct);
    }
    private static async Task Verify(FileStream stream,DownloadedPackage package,CancellationToken ct)
    {
        if(stream.Length!=package.Size||!Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包内容与下载记录不一致，请重新下载："+package.AssetName);
    }
    private static void Validate(DownloadedPackage package)
    {
        if(!PackageFiles.ExpectedPath(package)||package.Size<0||package.Sha256.Length!=64||!package.Sha256.All(Uri.IsHexDigit))throw new IOException("下载记录的路径或校验值无效，未操作文件。");
    }
    private static FileStream Open(string path,bool delete)
    {
        var handle=CreateFileW(Path.GetFullPath(path),0x80000000u|(delete?0x10000u:0),delete?0u:1u,IntPtr.Zero,3,0x00200000|0x08000000,IntPtr.Zero);
        if(handle.IsInvalid){var code=Marshal.GetLastWin32Error();handle.Dispose();if(code==2)throw new FileNotFoundException("安装包文件已不存在。",path);if(code==3)throw new DirectoryNotFoundException("安装包目录已不存在。");throw new IOException("文件正在使用或无法访问："+Path.GetFileName(path),new Win32Exception(code));}
        try{if((Info(handle).Attributes&0x410)!=0)throw new IOException("不操作符号链接、目录或已改变类型的文件。");return new FileStream(handle,FileAccess.Read,131072,false);}catch{handle.Dispose();throw;}
    }
    private static FileInformation Info(SafeFileHandle handle){if(!GetFileInformationByHandle(handle,out var info))throw new IOException("无法核对文件身份。",new Win32Exception(Marshal.GetLastWin32Error()));return info;}
    [StructLayout(LayoutKind.Sequential)]private struct FileInformation
    {
        public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Created,Accessed,Written;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInformation information);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int kind,ref byte information,uint size);
}
