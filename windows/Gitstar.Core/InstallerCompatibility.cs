using System.Reflection.PortableExecutable;

namespace Gitstar.Core;

public record InstallerPlatform(string Name,bool Compatible);
public static class InstallerCompatibility
{
    public static InstallerPlatform InspectExe(string path,bool host64Bit)
    {
        try{
            using var stream=File.OpenRead(path);using var pe=new PEReader(stream);var headers=pe.PEHeaders;
            if(headers.PEHeader==null)throw new BadImageFormatException();
            var machine=headers.CoffHeader.Machine;
            if(machine==Machine.I386){var flags=headers.CorHeader?.Flags??0;var anyCpu=(flags&CorFlags.ILOnly)!=0&&(flags&CorFlags.Requires32Bit)==0;return new(anyCpu?"Windows AnyCPU":"Windows x86",true);}
            return machine switch{Machine.Amd64=>new("Windows x64",host64Bit),Machine.Arm64=>new("Windows ARM64",false),Machine.Arm=>new("Windows ARM",false),Machine.IA64=>new("Windows Itanium",false),_=>new("未知架构",false)};
        }catch(Exception ex)when(ex is BadImageFormatException or EndOfStreamException){throw new IOException("文件不是可识别的 Windows EXE，已保留下载文件，请核对作者提供的安装包。",ex);}
    }
}
