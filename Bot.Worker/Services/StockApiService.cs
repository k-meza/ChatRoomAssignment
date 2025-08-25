using System.Globalization;
using Bot.Worker.Services.Interfaces;

namespace Bot.Worker.Services;

public class StockApiService : IStockApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockApiService> _logger;

    public StockApiService(HttpClient httpClient, ILogger<StockApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> GetStockQuoteAsync(string stockCode)
    {
        try
        {
            var url = $"https://stooq.com/q/l/?s={stockCode}&f=sd2t2ohlcv&h&e=csv";
            _logger.LogInformation("Fetching stock data for {StockCode} from {Url}", stockCode, url);

            var response = await _httpClient.GetStringAsync(url);
            _logger.LogDebug("Raw CSV response: {Response}", response);

            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                _logger.LogWarning("Invalid CSV response for {StockCode}: {Response}", stockCode, response);
                return $"Unable to get quote for {stockCode.ToUpperInvariant()}: Invalid data received";
            }

            // Skip header line and get data line
            var dataLine = lines[1];
            var values = dataLine.Split(',');

            if (values.Length < 4)
            {
                _logger.LogWarning("Insufficient data in CSV for {StockCode}: {DataLine}", stockCode, dataLine);
                return $"Unable to get quote for {stockCode.ToUpperInvariant()}: Insufficient data";
            }

            // CSV format: Symbol,Date,Time,Open,High,Low,Close,Volume
            var symbol = values[0].Trim();
            var closePrice = values[6].Trim();

            // Handle "N/D" values (No Data)
            if (closePrice.Equals("N/D", StringComparison.OrdinalIgnoreCase) ||
                !decimal.TryParse(closePrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                _logger.LogWarning("No valid price data for {StockCode}", stockCode);
                return $"No quote available for {stockCode.ToUpperInvariant()}";
            }

            var formattedPrice = price.ToString("F2", CultureInfo.InvariantCulture);
            var message = $"{symbol.ToUpperInvariant()} quote is ${formattedPrice} per share";

            _logger.LogInformation("Successfully formatted quote for {StockCode}: {Message}", stockCode, message);
            return message;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP error fetching stock data for {StockCode}", stockCode);
            return $"Error fetching quote for {stockCode.ToUpperInvariant()}: Network error";
        }
        catch (TaskCanceledException timeoutEx)
        {
            _logger.LogError(timeoutEx, "Timeout fetching stock data for {StockCode}", stockCode);
            return $"Error fetching quote for {stockCode.ToUpperInvariant()}: Request timeout";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching stock data for {StockCode}", stockCode);
            return $"Error fetching quote for {stockCode.ToUpperInvariant()}: Unexpected error";
        }
    }
}