using Gitstar.Core;
using System.Text.Json;

internal static class DownloadDefaultsChecks
{
    public static void Run(Action<bool,string> check)
    {
        check(new Settings().KeepLatestPackages,"fresh installation defaults to keeping only latest managed packages");
        var saved=JsonSerializer.Deserialize<Settings>("{\"KeepLatestPackages\":false,\"DownloadFolder\":\"D:\\\\Packages\"}")!;
        var reloaded=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(saved))!;
        check(!saved.KeepLatestPackages&&!reloaded.KeepLatestPackages&&reloaded.DownloadFolder==saved.DownloadFolder,"explicit existing disabled retention and custom download folder survive new default and serialization");
        check(!new Settings().DeleteInstalledPackages&&new Settings().PromptInstalledPackageCleanup,"latest-only default does not enable automatic deletion immediately after installation");
    }
}
