using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Ma9_Season_Push.Core;
using Ma9_Season_Push.Capture;
using Ma9_Season_Push.Detection;
using Ma9_Season_Push.Notification;
using Ma9_Season_Push.Logging;

namespace Ma9_Season_Push;

class Program
{
    private const string Ma9ProcessName = "Ma9Ma9Remaster";

    // ===== DPI Awareness 설정 =====
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new(-2);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new(-3);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

    private enum PROCESS_DPI_AWARENESS
    {
        Process_DPI_Unaware = 0,
        Process_System_DPI_Aware = 1,
        Process_Per_Monitor_DPI_Aware = 2
    }

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(PROCESS_DPI_AWARENESS value);

    // Vista+ fallback
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    // ===== FATAL 로그(파일 + 콘솔) =====
    private static readonly object _fatalLock = new();

    private static string GetFatalLogPath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "fatal.log");
    }

    private static void AppendFatal(string title, Exception? ex = null)
    {
        try
        {
            lock (_fatalLock)
            {
                var path = GetFatalLogPath();
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                var msg =
                    $"[{ts}] {title}{Environment.NewLine}" +
                    (ex == null ? "" : ex + Environment.NewLine) +
                    new string('-', 120) + Environment.NewLine;

                File.AppendAllText(path, msg);

                Console.Error.WriteLine($"[FATAL] {title}");
                if (ex != null) Console.Error.WriteLine(ex.ToString());
            }
        }
        catch
        {
            // 로깅 실패로 또 죽지 않게 무시
        }
    }

    // ===== Unhandled 예외 핸들러 등록 =====
    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                    AppendFatal("AppDomain.UnhandledException", ex);
                else
                    AppendFatal("AppDomain.UnhandledException (non-Exception object)");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                AppendFatal("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            }
            catch { }
        };
    }

    private static void TryEnableDpiAwareness()
    {
        // Windows 10+ (Per-monitor v2)
        try
        {
            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            {
                Logger.Info("[DPI] Per-monitor v2 enabled.");
                return;
            }

            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
            {
                Logger.Info("[DPI] Per-monitor v1 enabled.");
                return;
            }

            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_SYSTEM_AWARE))
            {
                Logger.Info("[DPI] System DPI aware enabled.");
                return;
            }
        }
        catch
        {
            // ignore and fallback
        }

        // Windows 8.1+
        try
        {
            var hr = SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.Process_Per_Monitor_DPI_Aware);
            if (hr == 0)
            {
                Logger.Info("[DPI] Per-monitor (shcore) enabled.");
                return;
            }

            hr = SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.Process_System_DPI_Aware);
            if (hr == 0)
            {
                Logger.Info("[DPI] System aware (shcore) enabled.");
                return;
            }
        }
        catch
        {
            // ignore and fallback
        }

        // Vista+
        try
        {
            if (SetProcessDPIAware())
            {
                Logger.Info("[DPI] Legacy SetProcessDPIAware enabled.");
                return;
            }
        }
        catch
        {
            // ignore
        }

        Logger.Info("[DPI] DPI awareness enable failed (continuing).");
    }

    static async Task Main()
    {
        // 글로벌 예외 로깅 등록(가장 먼저)
        RegisterGlobalExceptionHandlers();

        try
        {
            // DPI aware 설정은 반드시 Main 최상단에서
            TryEnableDpiAwareness();

            var state = new StateMachine();

            // 서브 1 모니터 고정 감시
            var capture = new CaptureService(screenIndex: 1);

            var baseDir = AppContext.BaseDirectory;

            using var endDetector = new EndSignDetector
            (
                tplConfirmPath: Path.Combine(baseDir, "Assets", "tpl_end_confirm_gray.png"),
                tplRewardPath: Path.Combine(baseDir, "Assets", "tpl_end_reward_gray.png"),
                thConfirm: 0.93,
                thReward: 0.93
            );

            // NOTE: 아래 호출은 'LeagueNewsSignDetector 생성자 시그니처 정리(B안)'이 반영되어 있어야 컴파일됩니다.
            using var leagueNewsDetector = new LeagueNewsSignDetector
            (
                tplTitlePath: Path.Combine(baseDir, "Assets", "tpl_lobby_title_gray_new.png"),
                tplSubtitlePath: Path.Combine(baseDir, "Assets", "tpl_lobby_subtitle_gray_new.png"),
                thTitle: 0.93,
                thSubtitle: 0.93,
                thNext: 0.90,
                requireNext: false
            );

            var endDebounce = new Debouncer();
            var leagueNewsDebounce = new Debouncer();

            var notifier = new TelegramNotifier();

            // 단계 알림 중복 방지 플래그
            var sentAppOn = false;
            var sentClientOn = false;
            var sentEndDetected = false;
            var sentLeagueDetected = false;

            // 프로세스 체크 쓰로틀
            var lastProcCheckAt = DateTime.MinValue;
            var procCheckIntervalMs = 2000;

            // 루프 내부 예외 스팸 방지 쓰로틀
            var lastLoopExceptionAt = DateTime.MinValue;
            var loopExceptionIntervalMs = 2000;

            DateTime? waitLeagueNewsEnteredAt = null;

            state.Start();
            Logger.Info("Ma9_Season_Push started.");

            // (1) 앱 시작 알림
            if (!sentAppOn)
            {
                sentAppOn = true;
                await notifier.SendAsync("마구알림 ON");
            }

            while (true)
            {
                await Task.Delay(AppConfig.ObserveIntervalMs);

                // (2) 마구마구 클라이언트 시작 감지 알림
                if (!sentClientOn &&
                    (DateTime.Now - lastProcCheckAt).TotalMilliseconds >= procCheckIntervalMs)
                {
                    lastProcCheckAt = DateTime.Now;

                    try
                    {
                        var procs = Process.GetProcessesByName(Ma9ProcessName);
                        if (procs is { Length: > 0 })
                        {
                            sentClientOn = true;
                            await notifier.SendAsync("마구마구 클라이언트 ON");
                            Logger.Info($"Client process detected: {Ma9ProcessName}.exe");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Process check failed: {ex}");
                    }
                }

                if (state.State == AppState.Idle)
                    continue;

                try
                {
                    using var frame = capture.CaptureFrame();
                    if (frame.Empty())
                        continue;

                    if (state.State == AppState.WatchingEnd)
                    {
                        var endRes = endDetector.Detect(frame);

                        if (endDebounce.Check(endRes.Hit))
                        {
                            Logger.Info($"END detected: {endRes.Reason}");

                            // (3) 경기 종료 알림
                            if (!sentEndDetected)
                            {
                                sentEndDetected = true;
                                await notifier.SendAsync("경기종료");
                            }

                            state.ToWaitLeagueNews();
                            waitLeagueNewsEnteredAt = DateTime.Now;

                            leagueNewsDebounce.Reset();
                        }
                    }
                    else if (state.State == AppState.WaitLeagueNews)
                    {
                        // 타임아웃 체크
                        if (waitLeagueNewsEnteredAt.HasValue &&
                            (DateTime.Now - waitLeagueNewsEnteredAt.Value).TotalMilliseconds >= AppConfig.WaitLeagueNewsTimeoutMs)
                        {
                            state.ToWatchingEnd();
                            waitLeagueNewsEnteredAt = null;

                            Logger.Info("WaitLeagueNews timeout -> WatchingEnd");

                            // 다음 경기 사이클을 위해 경기 단위 플래그 리셋
                            sentEndDetected = false;
                            sentLeagueDetected = false;

                            endDebounce.Reset();
                            leagueNewsDebounce.Reset();
                            continue;
                        }

                        var leagueRes = leagueNewsDetector.Detect(frame);

                        // WaitLeagueNews에서는 1-hit 즉시 확정이 핵심
                        if (leagueNewsDebounce.Check(leagueRes.Hit))
                        {
                            Logger.Info($"LEAGUE detected: {leagueRes.Reason}");

                            if (!sentLeagueDetected)
                            {
                                sentLeagueDetected = true;
                                await notifier.SendAsync("대기모드 전환");
                            }

                            state.ToWatchingEnd();
                            waitLeagueNewsEnteredAt = null;

                            Logger.Info("LeagueDetected -> WatchingEnd");

                            // 다음 경기 대비
                            sentEndDetected = false;
                            sentLeagueDetected = false;

                            endDebounce.Reset();
                            leagueNewsDebounce.Reset();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 루프 내부에서 죽는 예외를 잡고 계속 실행 (운영 안정성)
                    if ((DateTime.Now - lastLoopExceptionAt).TotalMilliseconds >= loopExceptionIntervalMs)
                    {
                        lastLoopExceptionAt = DateTime.Now;
                        Logger.Error($"Loop exception: {ex}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendFatal("Main() fatal exception", ex);
        }
    }
}
