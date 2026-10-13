namespace Gitstar.Core;

public static class PathPreferences
{
    public static string ProjectPath(string path)
    {
        foreach(var (oldRoot,newRoot) in new[]{(@"D:\Agant\Github-Releases",@"D:\Agant\Github_Releases"),(@"D:\Agant\Github-Source",@"D:\Agant\Github_Source"),(@"D:\Agant\Github",@"D:\Agant\Github_Source")})if(path.Equals(oldRoot,StringComparison.OrdinalIgnoreCase)||path.StartsWith(oldRoot+"\\",StringComparison.OrdinalIgnoreCase))return newRoot+path[oldRoot.Length..];
        return path;
    }
    public static void Migrate(Settings settings)
    {
        var legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","Gitstar");if(settings.DownloadFolder.TrimEnd('\\','/').Equals(legacy,StringComparison.OrdinalIgnoreCase))settings.DownloadFolder=new Settings().DownloadFolder;
        if(settings.SameVersionDownload is <0 or >2)settings.SameVersionDownload=0;
    }
}
