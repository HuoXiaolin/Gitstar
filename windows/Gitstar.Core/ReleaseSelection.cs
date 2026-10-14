namespace Gitstar.Core;

public static class ReleaseSelection
{
    // Compare version identities, independently from the list's chosen time sort.
    public static int Compare(string tag,DateTimeOffset published,string otherTag,DateTimeOffset otherPublished,string prefix="")
    {
        var version=tag[prefix.Length..];var other=otherTag[prefix.Length..];
        var known=Rules.CompareVersions(version,version)!=null;var otherKnown=Rules.CompareVersions(other,other)!=null;
        if(known!=otherKnown)return known?1:-1;
        if(known){var difference=Rules.CompareVersions(version,other)!.Value;if(difference!=0)return difference;}
        var time=published.CompareTo(otherPublished);return time!=0?time:string.CompareOrdinal(tag,otherTag);
    }
    public static bool Preferred(string tag,bool prerelease,DateTimeOffset published,bool latest,Release current,string prefix="")
    {
        if(!prerelease&&!current.Prerelease&&latest!=current.IsLatest)return latest;
        return Compare(tag,published,current.Tag,current.Published,prefix)>0;
    }
    public static Release? Select(IEnumerable<Release> source,Binding binding)
    {
        Release? stable=null;Release? beta=null;
        foreach(var item in source.Where(r=>r.Tag.StartsWith(binding.TagPrefix,StringComparison.Ordinal))){
            if(item.Prerelease){if(binding.IncludePrerelease&&(beta==null||Preferred(item.Tag,true,item.Published,false,beta,binding.TagPrefix)))beta=item;}
            else if(stable==null||Preferred(item.Tag,false,item.Published,item.IsLatest,stable,binding.TagPrefix))stable=item;
        }
        if(stable==null)return beta;if(beta==null)return stable;
        return Compare(beta.Tag,beta.Published,stable.Tag,stable.Published,binding.TagPrefix)>0?beta:stable;
    }
}
