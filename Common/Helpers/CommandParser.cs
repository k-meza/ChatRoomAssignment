using System.Text.RegularExpressions;

namespace Common.Helpers;

public static class CommandParser
{
    private static readonly Regex StockCmd = 
        new(@"^\s*/stock=(?<code>[\w\.]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParseStock(string input, out string code)
    {
        var m = StockCmd.Match(input ?? "");
        if (m.Success)
        {
            code = m.Groups["code"].Value.Trim().ToLowerInvariant();
            return true;
        }
        code = "";
        return false;
    }
}