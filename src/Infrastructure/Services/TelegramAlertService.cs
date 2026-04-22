using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Infrastructure.Services;

/// <summary>
/// Sends failure alerts to a Telegram chat via the Bot API.
/// Failures in this service are always swallowed — never propagated to the worker.
/// </summary>
public class TelegramAlertService
{
    private readonly string? _botToken;
    private readonly string? _chatId;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelegramAlertService> _logger;

    public TelegramAlertService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<TelegramAlertService> logger)
    {
        _botToken = configuration["Telegram:BotToken"];
        _chatId = configuration["Telegram:ChatId"];
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendAlertAsync(long runId, Exception ex)
    {
        if (string.IsNullOrWhiteSpace(_botToken) || string.IsNullOrWhiteSpace(_chatId))
        {
            _logger.LogWarning("Telegram alert skipped — BotToken or ChatId not configured.");
            return;
        }

        try
        {
            var workerName = "unknown";
            var shortError = ex.Message.Length > 300 ? ex.Message[..300] + "…" : ex.Message;

            var text = $"🔴 *GBW Worker Failed*\n" +
                       $"RunId: `{runId}`\n" +
                       $"Error: `{EscapeMarkdown(shortError)}`\n" +
                       $"Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";

            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            var payload = new Dictionary<string, string>
            {
                ["chat_id"] = _chatId,
                ["text"] = text,
                ["parse_mode"] = "Markdown"
            };

            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(url, new FormUrlEncodedContent(payload));

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Telegram alert HTTP {Status}", response.StatusCode);
            else
                _logger.LogInformation("Telegram alert sent for RunId={RunId}", runId);
        }
        catch (Exception alertEx)
        {
            _logger.LogWarning(alertEx, "Telegram alert failed — swallowing to protect worker");
        }
    }

    public async Task SendCustomMessageAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(_botToken) || string.IsNullOrWhiteSpace(_chatId))
            return;

        try
        {
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            var payload = new Dictionary<string, string>
            {
                ["chat_id"] = _chatId,
                ["text"] = message
            };
            var client = _httpClientFactory.CreateClient();
            await client.PostAsync(url, new FormUrlEncodedContent(payload));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram custom message failed");
        }
    }

    private static string EscapeMarkdown(string text) =>
        text.Replace("_", "\\_").Replace("*", "\\*").Replace("`", "\\`").Replace("[", "\\[");
}
