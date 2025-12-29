using System;
using Ma9_Season_Push.Core;

namespace Ma9_Season_Push;

/// <summary>
/// 애플리케이션 전역 설정값
/// </summary>
public static class AppConfig
{
    /// <summary>
    /// 화면 관찰 주기 (ms)
    /// </summary>
    public const int ObserveIntervalMs = 500;

    /// <summary>
    /// 사인 확정에 필요한 연속 감지 횟수
    /// </summary>
    public const int ConfirmCount = 3;

    /// <summary>
    /// 텔레그램 메시지 발송 최대 재시도 횟수
    /// </summary>
    public const int TelegramRetryCount = 5;

    /// <summary>
    /// WaitLeagueNews 상태 최대 대기 시간 (ms)
    /// 기본값: 5분
    /// </summary>
    public const int WaitLeagueNewsTimeoutMs = 300_000;

    /// <summary>
    /// 로그 디렉터리
    /// - 단일 EXE(SelfContained + PublishSingleFile)에서는 AppContext.BaseDirectory가 %TEMP% 추출 경로로 잡힐 수 있으므로,
    ///   "실행파일(.exe) 기준" 경로를 사용한다.
    /// </summary>
    public static string LogsDir => AppPaths.LogsDir;
}
