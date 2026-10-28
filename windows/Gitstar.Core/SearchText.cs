namespace Gitstar.Core;
public static class SearchText
{
    public static int Score(string value,string query)
    {
        static string Normalize(string text)=>new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var hay=Normalize(value);var tokens=query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(tokens.Length==0)return 1;var score=0;
        foreach(var token in tokens){var needle=Normalize(token);if(needle.Length==0)continue;if(hay==needle){score+=100;continue;}if(hay.Contains(needle)){score+=50;continue;}var offset=0;foreach(var c in hay)if(offset<needle.Length&&c==needle[offset])offset++;if(offset!=needle.Length)return 0;score+=5;}
        return score;
    }
}
