using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Ma9_Season_Push;
using Ma9_Season_Push.Core;
using Ma9_Season_Push.Logging;

namespace Ma9_Season_Push.Notification;

public sealed class TelegramNotifier
{
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly string _botToken;
    private readonly string _chatId;

    public TelegramNotifier()
    {
        (_botToken, _chatId) = LoadConfig();
    }

    private static (string botToken, string chatId) LoadConfig()
    {
        try
        {
            var configPath = Path.Combine(AppPaths.ExeDir, "telegram.json");
            if (!File.Exists(configPath))
            {
                Logger.Error($"[Telegram] Config file not found: {configPath}");
                return ("", "");
            }

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var botToken = root.TryGetProperty("BotToken", out var bt) ? bt.GetString() ?? "" : "";
            var chatId = root.TryGetProperty("ChatId", out var ci) ? ci.GetString() ?? "" : "";

            return (botToken, chatId);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Telegram] Failed to load config: {ex.Message}");
            return ("", "");
        }
    }

    public async Task SendAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(_botToken) || string.IsNullOrWhiteSpace(_chatId))
        {
            Logger.Error($"[Telegram] BotToken or ChatId is empty. message={message}");
            return;
        }

        // 최소 1회 재시도 보장 (총 2회 이상)
        var maxAttempts = Math.Max(2, AppConfig.TelegramRetryCount);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["chat_id"] = _chatId,
                    ["text"] = message
                });

                using var resp = await _http.PostAsync(url, content).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                    return;

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Logger.Error(
                    $"[Telegram] Send failed (HTTP {(int)resp.StatusCode}) " +
                    $"attempt={attempt}/{maxAttempts} body={body}"
                );
            }
            catch (Exception ex)
            {
                // 예외는 절대 밖으로 던지지 않음
                Logger.Error(
                    $"[Telegram] Send exception attempt={attempt}/{maxAttempts} ex={ex}"
                );
            }

            // 간단한 백오프
            await Task.Delay(250 * attempt).ConfigureAwait(false);
        }

        Logger.Error($"[Telegram] Final send failed. message={message}");
    }
}
