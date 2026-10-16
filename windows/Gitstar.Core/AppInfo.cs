using System.Reflection;
namespace Gitstar.Core;

public static class AppInfo
{
    public static string Version=>typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??typeof(AppInfo).Assembly.GetName().Version?.ToString(3)??"2.0.0";
    public const string Author="HuoXiaolin";
    public const string Repository="HuoXiaolin/Gitstar";
    public const string ProjectUrl="https://github.com/"+Repository;
    public const string ReleasesUrl=ProjectUrl+"/releases";
    public const string Description="收藏的好项目，值得常回来看看。按分组发现新版本、翻阅更新日志，顺手下载所需安装包；本机软件更新，也能一目了然。自己的项目，从提交源码到更新发布，都能在这里完成。";
}
