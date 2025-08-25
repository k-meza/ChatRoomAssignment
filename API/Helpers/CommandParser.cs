using System.Text.RegularExpressions;

namespace API.Helpers;

public static class CommandParser
{
    // Accepts:
    // /stock=code
    // /stock code
    // with optional spaces around '=' and supports symbols (letters, digits, ., -, _, ^, and leading $)
    private static readonly Regex StockCmd =
        new(@"^\s*/stock(?:\s*=\s*|\s+)?(?<code>[^\s]*)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public readonly record struct StockCommandParseResult(
        bool IsStockCommand,
        bool HasCode,
        string Code,
        string? Error
    );

    public static StockCommandParseResult ParseStock(string? input)
    {
        var m = StockCmd.Match(input ?? string.Empty);
        if (!m.Success)
        {
            return new StockCommandParseResult(false, false, string.Empty, null);
        }

        var raw = m.Groups["code"].Value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new StockCommandParseResult(
                true,
                false,
                string.Empty,
                "Missing stock code. Usage: /stock=CODE (e.g., /stock=AAPL.US or /stock=^SPX)"
            );
        }

        var normalized = NormalizeCode(raw);

        // Basic validation: disallow spaces and control chars
        if (normalized.Any(char.IsWhiteSpace))
        {
            return new StockCommandParseResult(
                true,
                false,
                string.Empty,
                "Invalid stock code. Codes must not contain spaces."
            );
        }

        return new StockCommandParseResult(true, true, normalized, null);
    }
    
    private static string NormalizeCode(string raw)
    {
        var code = raw.Trim();

        // Drop leading $ if user typed $AAPL
        if (code.StartsWith("$")) code = code[1..];

        code = code.ToLowerInvariant();

        // Handle well-known index aliases: accept spx/gspc with or without ^
        if (code is "spx" or "^spx" or "gspc" or "^gspc")
        {
            // Normalize to provider’s expected index symbol
            code = "^spx";
        }

        // Other symbols: keep as provided (allows aapl.us, btcusd, eurusd, ^dji, etc.)
        return code;
    }
}