using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Ma9_Season_Push.Logging;

namespace Ma9_Season_Push.Notification;

public sealed class TelegramNotifier
{
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // ===============================
    // ▼▼ 여기만 수정하면 됩니다 ▼▼
    // ===============================

    private const string BOT_TOKEN = "***REMOVED***";
    private const string CHAT_ID = "***REMOVED***";
    // 예)
    // private const string BOT_TOKEN = "123456789:AAxxxxxxxxxxxxxxxxxxxxx";
    // private const string CHAT_ID   = "-1001234567890";
    // ===============================

    public async Task SendAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(BOT_TOKEN) || string.IsNullOrWhiteSpace(CHAT_ID))
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
                var url = $"https://api.telegram.org/bot{BOT_TOKEN}/sendMessage";
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["chat_id"] = CHAT_ID,
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
