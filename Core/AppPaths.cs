using System;
using System.Diagnostics;
using System.IO;

namespace Ma9_Season_Push.Core;

/// <summary>
/// 실행파일 기준 경로 관리 클래스
/// - 단일 EXE(SelfContained + PublishSingleFile) 환경 대응
/// - AppContext.BaseDirectory(%TEMP% 추출 경로) 사용 금지
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> _exePath = new(GetExePathInternal);
    private static readonly Lazy<string> _exeDir = new(() =>
        Path.GetDirectoryName(_exePath.Value)
        ?? throw new InvalidOperationException("Failed to resolve exe directory.")
    );

    /// <summary>
    /// exe 파일이 위치한 디렉터리
    /// </summary>
    public static string ExeDir => _exeDir.Value;

    /// <summary>
    /// 로그 디렉터리 (exe 기준 고정)
    /// </summary>
    public static string LogsDir => Path.Combine(ExeDir, "logs");

    /// <summary>
    /// fatal 로그 파일 경로
    /// </summary>
    public static string FatalLogPath => Path.Combine(LogsDir, "fatal.log");

    // =========================
    // 내부 구현
    // =========================

    private static string GetExePathInternal()
    {
        // 1순위: Process.MainModule (가장 정확)
        try
        {
            var path = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path))
                return Path.GetFullPath(path);
        }
        catch
        {
            // ignore
        }

        // 2순위: Environment.ProcessPath (.NET 6+)
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                return Path.GetFullPath(Environment.ProcessPath);
        }
        catch
        {
            // ignore
        }

        // 3순위: EntryAssembly.Location (fallback)
        try
        {
            var asmPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(asmPath))
                return Path.GetFullPath(asmPath);
        }
        catch
        {
            // ignore
        }

        throw new InvalidOperationException("Unable to resolve executable path.");
    }
}
