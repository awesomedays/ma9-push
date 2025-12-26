using System;
using System.IO;

namespace Ma9_Season_Push.Logging;

public static class Logger
{
    private static readonly object _lock = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(AppConfig.LogsDir);

                var logLine =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " +
                    $"[{level}] {message}{Environment.NewLine}";

                File.AppendAllText(AppConfig.LogFilePath, logLine);
            }
        }
        catch (Exception ex)
        {
            // 운영 중에도 예외로 앱이 죽지 않게만 보장
            Console.WriteLine($"[Logger Error] {ex}");
        }
    }
}
