using System.Net;
using System.Text.RegularExpressions;

namespace Gitstar.Core;

public static class ReleasePreview
{
    public static string Create(string body, string tag)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var lines = new List<string>();
        var inCode = false;
        foreach (var raw in body[..Math.Min(body.Length, 32768)].Replace("\r", "").Split('\n'))
        {
            var text = raw.Trim();
            if (text.StartsWith("```") || text.StartsWith("~~~")) { inCode = !inCode; continue; }
            if (inCode) continue;
            text = Regex.Replace(text, @"!\[[^\]\r\n]*\]\([^\)\r\n]*\)", "");
            text = Regex.Replace(text, @"\[([^\]\r\n]+)\]\([^\)\r\n]*\)", "$1");
            text = WebUtility.HtmlDecode(Regex.Replace(text, @"<[^>]*>", ""));
            text = Regex.Replace(text, @"^\s*(?:#{1,6}\s*|[-*+>]\s+|\d+[.)]\s+)", "");
            text = Regex.Replace(text.Replace("**", "").Replace("__", "").Replace("`", ""), @"\s+", " ").Trim();
            if (text.Length == 0 || Regex.IsMatch(text, @"^[-=*_|\s]+$")) continue;
            var metadata = text.Replace(tag, "", StringComparison.OrdinalIgnoreCase);
            metadata = Regex.Replace(metadata, @"\b\d{4}[-/.]\d{1,2}[-/.]\d{1,2}(?:\s+\d{1,2}:\d{2})?\b", "");
            metadata = Regex.Replace(metadata, @"\bv?\d+(?:\.\d+){1,3}(?:-[\w.]+)?(?:\+[\w.]+)?\b", "", RegexOptions.IgnoreCase);
            metadata = Regex.Replace(metadata, @"\b(?:release|version|changelog|update|changes)\b|版本|更新日志|发布说明", "", RegexOptions.IgnoreCase);
            if (!metadata.Any(char.IsLetterOrDigit)) continue;
            if (Regex.IsMatch(text, @"^(?:what['’]?s new|what['’]?s changed|full changelog|更新内容|更新说明|变更内容|更新日志)\s*[:：]?$", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(text, @"^https?://\S+$", RegexOptions.IgnoreCase)) continue;
            if (lines.Contains(text, StringComparer.OrdinalIgnoreCase)) continue;
            lines.Add(text.Length > 200 ? text[..200] + "…" : text);
            if (lines.Count == 3) break;
        }
        return string.Join("\n", lines);
    }
}
