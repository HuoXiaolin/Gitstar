using System.Runtime.InteropServices;
using System.Text;
namespace Gitstar.Desktop;
public static partial class WindowsServices
{
    public static string MsiPlatform(string path)
    {
        var error=MsiGetSummaryInformation(0,Path.GetFullPath(path),0,out var handle);
        if(error!=0)throw new IOException($"无法读取 MSI 架构信息（{error}），安装包已保存，未打开安装器。");
        try{
            var buffer=new StringBuilder(256);uint count=(uint)buffer.Capacity;
            error=MsiSummaryInfoGetProperty(handle,7,out var type,out _,IntPtr.Zero,buffer,ref count);
            if(error==234 && count<4096){buffer=new StringBuilder((int)count+1);count=(uint)buffer.Capacity;error=MsiSummaryInfoGetProperty(handle,7,out type,out _,IntPtr.Zero,buffer,ref count);}
            if(error!=0 || type!=30)throw new IOException("MSI 未提供可识别的架构信息，安装包已保存，未打开安装器。");
            return buffer.ToString();
        }finally{MsiCloseHandle(handle);}
    }
    [DllImport("msi.dll",EntryPoint="MsiGetSummaryInformationW",CharSet=CharSet.Unicode)] private static extern uint MsiGetSummaryInformation(uint database,string path,uint count,out uint summary);
    [DllImport("msi.dll",EntryPoint="MsiSummaryInfoGetPropertyW",CharSet=CharSet.Unicode)] private static extern uint MsiSummaryInfoGetProperty(uint summary,uint property,out uint type,out int value,IntPtr time,StringBuilder text,ref uint count);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
}
