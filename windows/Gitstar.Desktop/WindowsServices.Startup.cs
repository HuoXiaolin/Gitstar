using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Gitstar.Desktop;

internal record StartupEntry(string Command,string ExpectedCommand,string Status,bool Matches);

public static partial class WindowsServices
{
    internal const string StartupKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string StartupLocation=@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string StartupCommand(string executable)=>$"\"{Path.GetFullPath(executable)}\" --tray";
    internal static StartupEntry InspectStartupCommand(string command,string executable,bool exists)
    {
        var expected=StartupCommand(executable);
        if(!exists)return new(command,expected,"当前程序文件不存在，无法修复；请从有效的 Gitstar.exe 重新打开。",false);
        if(string.IsNullOrWhiteSpace(command))return new(command,expected,"未找到 Gitstar 启动项。",false);
        var match=Regex.Match(command,"^\\s*(?:\"(?<exe>[^\"]+)\"|(?<exe>\\S+))\\s*(?<args>.*?)\\s*$");
        string actual="";try{if(match.Success)actual=Path.GetFullPath(Environment.ExpandEnvironmentVariables(match.Groups["exe"].Value));}catch(ArgumentException){}catch(NotSupportedException){}
        if(!actual.Equals(Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))return new(command,expected,"启动项指向其他位置或命令格式不正确，可修复为当前程序位置。",false);
        if(match.Groups["args"].Value.Trim()!="--tray")return new(command,expected,"启动命令缺少静默参数，或包含多余参数，可修复为 --tray。",false);
        return new(command,expected,"启动项位置与静默参数正确。登录 Windows 后将以 --tray 启动。",true);
    }
    internal static StartupEntry ReadStartupEntry(RegistryKey hive,string key,string executable)
    {
        using var run=hive.OpenSubKey(key);
        var command=run?.GetValue("Gitstar",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string??"";
        return InspectStartupCommand(command,executable,File.Exists(executable));
    }
    internal static void SetStartupEntry(RegistryKey hive,string key,bool enabled,string executable)
    {
        if(enabled&&!File.Exists(executable))throw new IOException("当前程序文件不存在，未更改启动项。");
        if(enabled){using var run=hive.CreateSubKey(key);run.SetValue("Gitstar",StartupCommand(executable),RegistryValueKind.String);}
        else {using var run=hive.OpenSubKey(key,true);run?.DeleteValue("Gitstar",false);}
    }
}
