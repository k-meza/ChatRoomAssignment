namespace Bot.Worker.Services.Interfaces;

public interface IStockApiService
{
    Task<string> GetStockQuoteAsync(string stockCode);
}