using System;
using System.IO;
using System.Threading;

using Ma9_Season_Push.Core;

namespace Ma9_Season_Push.Logging;

/// <summary>
/// 파일 기반 로거 (WinExe 대응)
/// - Console.WriteLine 제거
/// - exe 기준 logs 디렉터리에 일자별 로그 기록
/// - 로깅 실패 시 앱 동작에 영향 주지 않음
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();

    public static void Info(string message)
        => Write("INFO", message);

    public static void Error(string message)
        => Write("ERROR", message);

    public static void Warn(string message)
        => Write("WARN", message);

    private static void Write(string level, string message)
    {
        try
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{ts}] [{level}] {message}{Environment.NewLine}";

            var logDir = AppPaths.LogsDir;
            var logFile = Path.Combine(logDir, $"app-{DateTime.Now:yyyyMMdd}.log");

            lock (_lock)
            {
                Directory.CreateDirectory(logDir);
                File.AppendAllText(logFile, line);
            }
        }
        catch
        {
            // 로깅 실패로 앱이 종료되면 안 되므로 완전 무시
        }
    }
}
