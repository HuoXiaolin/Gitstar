using Gitstar.Core;
using Gitstar.Desktop;
using System.IO;

internal static class SpecialFollowChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var settings=new Settings{ExtraRepositories=["example/tool"],LocalGroups=[new("tools","工具","本地",["example/tool"])]};
        check(!SpecialFollows.Contains(settings,"example/tool")&&settings.SpecialRepositories.Count==0,"existing settings start with no special follows");
        SpecialFollows.Set(settings,"https://github.com/example/tool",true);SpecialFollows.Set(settings,"EXAMPLE/TOOL",true);
        check(settings.SpecialRepositories.Count==1&&SpecialFollows.Contains(settings,"example/tool"),"heart selection is case-insensitive and duplicate-free");
        check(SpecialFollows.Visible(settings,["example/tool","EXAMPLE/TOOL","other/tool"]).SequenceEqual(["example/tool"]),"special group includes only marked available repositories once");
        check(settings.ExtraRepositories.SequenceEqual(["example/tool"])&&settings.LocalGroups.Single().Repositories.SequenceEqual(["example/tool"])&&settings.FollowHistory.Count==0,"special follows do not change normal follows group memberships or unfollow history");
        var path=Path.Combine(root,"special-follows");using(var store=new LocalStore(path)){store.Set("settings",settings);store.Set("profile/other",new Settings());}
        using(var store=new LocalStore(path)){var restored=store.Get("settings",new Settings());check(SpecialFollows.Contains(restored,"example/tool"),"red heart selection persists across database reopening");check(!SpecialFollows.Contains(store.Get("profile/other",new Settings()),"example/tool"),"another saved account profile does not inherit special follows");}
        var rejected=false;try{SpecialFollows.Set(settings,"bad address",true);}catch(IOException){rejected=true;}check(rejected&&settings.SpecialRepositories.Count==1,"invalid project address does not damage existing special follows");
        SpecialFollows.Set(settings,"example/tool",false);check(settings.SpecialRepositories.Count==0&&!SpecialFollows.Contains(settings,"EXAMPLE/TOOL"),"second heart click removes all spelling variants from special follows");
    }
}
