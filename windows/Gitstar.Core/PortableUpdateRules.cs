namespace Gitstar.Core;

public static class PortableUpdateRules
{
    public static bool ReplaceFiles(InstalledApp app,string package)=>app.Portable||Path.GetExtension(package).Equals(".zip",StringComparison.OrdinalIgnoreCase);
    public static string Instructions(string package,bool extracted=false)
    {
        var name=Path.GetFileName(package).ToLowerInvariant();var extension=Path.GetExtension(name);
        if(extension==".msi"||extension==".exe"&&(name.Contains("setup")||name.Contains("installer")))return "所选文件看起来是安装器，不适合直接替换便携软件。建议选择便携 ZIP 或主程序 EXE；本次不会启动安装器。";
        if(extracted)return "新版已解压。更新原软件目录中的程序文件，保留配置和个人数据；可选择“自动替换并启动”完成关闭、替换和重新打开。请先保存未保存的内容。";
        if(extension==".exe")return "便携软件无需安装。用新版 EXE 替换原主程序并沿用原文件名；可选择“自动替换并启动”完成关闭、替换和重新打开。请先保存未保存的内容。";
        return "这是便携软件更新。请先解压或打开下载文件所在目录，退出旧版软件后，将新版程序文件复制到原软件目录进行替换，保留配置和个人数据。";
    }
}
