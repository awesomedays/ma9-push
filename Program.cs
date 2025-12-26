using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

// Screen 인덱스 판별용
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// F8 저장용(OpenCvSharp)
using OpenCvSharp;

using Ma9_Season_Push.Core;
using Ma9_Season_Push.Capture;
using Ma9_Season_Push.Detection;
using Ma9_Season_Push.Notification;
using Ma9_Season_Push.Logging;

namespace Ma9_Season_Push;

class Program
{
    // 작업관리자 프로세스명: "Ma9Ma9Remaster.exe"
    // GetProcessesByName에는 확장자 제외한 이름만 넣어야 함.
    private const string Ma9ProcessName = "Ma9Ma9Remaster";

    // ===== DPI aware (Per-monitor v2 우선) =====
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new IntPtr(-3);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

    // Windows 8.1 fallback
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

    // ===== 윈도우 RECT 조회 =====
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    // ===== [추가] FATAL 로그(파일 + 콘솔) =====
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
                    (ex == null ? "" : ex.ToString() + Environment.NewLine) +
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

    // ===== [추가] Unhandled 예외 핸들러 등록 =====
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
        // 반드시 Main 초반(스크린/캡처 관련 API 호출 전)에 실행해야 함
        try
        {
            // 1) Per-monitor v2
            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            {
                Console.WriteLine("[DPI] Per-monitor v2 enabled.");
                return;
            }

            // 2) Per-monitor v1
            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
            {
                Console.WriteLine("[DPI] Per-monitor v1 enabled.");
                return;
            }

            // 3) System aware
            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_SYSTEM_AWARE))
            {
                Console.WriteLine("[DPI] System DPI aware enabled.");
                return;
            }
        }
        catch
        {
            // ignore and fallback below
        }

        try
        {
            // 4) shcore.dll (Windows 8.1+)
            var hr = SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.Process_Per_Monitor_DPI_Aware);
            if (hr == 0)
            {
                Console.WriteLine("[DPI] Per-monitor (shcore) enabled.");
                return;
            }

            hr = SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.Process_System_DPI_Aware);
            if (hr == 0)
            {
                Console.WriteLine("[DPI] System aware (shcore) enabled.");
                return;
            }
        }
        catch
        {
            // ignore and fallback below
        }

        try
        {
            // 5) legacy
            if (SetProcessDPIAware())
            {
                Console.WriteLine("[DPI] Legacy SetProcessDPIAware enabled.");
                return;
            }
        }
        catch
        {
            // ignore
        }

        Console.WriteLine("[DPI] DPI awareness enable failed (continuing).");
    }

    // ===== Ma9Ma9Remaster.exe가 떠 있는 Screen 인덱스 조회 =====
    private static int? TryGetMa9ClientScreenIndex()
    {
        try
        {
            var p = Process.GetProcessesByName(Ma9ProcessName)
                           .FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);

            if (p == null) return null;
            if (!GetWindowRect(p.MainWindowHandle, out var r)) return null;

            var rect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            var screen = Screen.FromRectangle(rect);

            var idx = Array.IndexOf(Screen.AllScreens, screen);
            return (idx >= 0) ? idx : null;
        }
        catch
        {
            return null;
        }
    }

    // ===== End 디버그 라인 구성(Reason에 점수 포함 구조) =====
    private static string BuildEndDebugLine(dynamic endRes)
    {
        return $"[EndDebug] Hit={endRes.Hit}, Reason={endRes.Reason}";
    }

    static async Task Main()
    {
        // ===== [추가] 글로벌 예외 로깅 등록(가장 먼저) =====
        RegisterGlobalExceptionHandlers();

        try
        {
            // ===== DPI aware 설정은 반드시 Main 최상단에서 =====
            TryEnableDpiAwareness();

            var state = new StateMachine();

            // 서브 1 모니터 고정 감시
            var capture = new CaptureService(screenIndex: 1);

            var baseDir = AppContext.BaseDirectory;

            // WatchingEnd 디버그 로그 쓰로틀
            var lastEndDebugLogAt = DateTime.MinValue;
            var endDebugLogIntervalMs = 2000;

            using var endDetector = new EndSignDetector
            (
                tplConfirmPath: Path.Combine(baseDir, "Assets", "tpl_end_confirm_gray.png"),
                tplRewardPath: Path.Combine(baseDir, "Assets", "tpl_end_reward_gray.png"),
                thConfirm: 0.93,
                thReward: 0.88
            );

            using var leagueNewsDetector = new LeagueNewsSignDetector
            (
                tplTitlePath: Path.Combine(baseDir, "Assets", "tpl_lobby_title_gray.png"),
                tplSubtitlePath: Path.Combine(baseDir, "Assets", "tpl_lobby_subtitle_gray.png"),
                tplNextPath: Path.Combine(baseDir, "Assets", "tpl_lobby_next_gray.png"),
                thTitle: 0.93,
                thSubtitle: 0.93,
                thNext: 0.90,
                requireNext: true
            );

            var endDebounce = new Debouncer();
            var leagueNewsDebounce = new Debouncer();

            var notifier = new TelegramNotifier();

            // 단계 로그 중복 방지 플래그
            var sentAppOn = false;
            var sentClientOn = false;
            var sentEndDetected = false;
            var sentWaitLeagueNewsEntered = false;
            var sentLeagueDetected = false;

            // 프로세스 체크 쓰로틀
            var lastProcCheckAt = DateTime.MinValue;
            var procCheckIntervalMs = 2000;

            // (추가) 루프 내부 예외 스팸 방지 쓰로틀
            var lastLoopExceptionAt = DateTime.MinValue;
            var loopExceptionIntervalMs = 2000;

            DateTime? waitLeagueNewsEnteredAt = null;

            state.Start();
            Logger.Info("Ma9_Season_Push started.");

            // (1) 앱 시작 로그: "마구알림 ON"
            if (!sentAppOn)
            {
                sentAppOn = true;
                await notifier.SendAsync("마구알림 ON");
            }

            while (true)
            {
                await Task.Delay(AppConfig.ObserveIntervalMs);

                // (2) 마구마구 클라이언트 시작 로그(프로세스 존재 확인): "마구마구 클라이언트 ON"
                if (!sentClientOn &&
                    (DateTime.Now - lastProcCheckAt).TotalMilliseconds >= procCheckIntervalMs)
                {
                    lastProcCheckAt = DateTime.Now;

                    try
                    {
                        var procs = Process.GetProcessesByName(Ma9ProcessName);
                        if (procs is { Length: > 0 })
                        {
                            // 알림 전송 직전에 Screen 인덱스 콘솔 출력
                            var idx = TryGetMa9ClientScreenIndex();
                            Console.WriteLine(idx.HasValue
                                ? $"[Ma9 Screen] {Ma9ProcessName}.exe is on ScreenIndex = {idx.Value}"
                                : $"[Ma9 Screen] {Ma9ProcessName}.exe ScreenIndex = (unknown)");

                            sentClientOn = true;
                            await notifier.SendAsync("마구마구 클라이언트 ON");
                            Logger.Info($"Client process detected: {Ma9ProcessName}.exe");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Process check failed: {ex}");
                        // 프로세스 체크는 실패해도 앱이 죽으면 안 되므로 계속
                    }
                }

                if (state.State == AppState.Idle)
                    continue;

                // ===== [추가] 캡처/검출 영역 전체 try/catch (여기서 죽는 케이스를 잡기 위해) =====
                try
                {
                    using var frame = capture.CaptureFrame();
                    if (frame.Empty())
                        continue;

                    // F8 수동 캡처 저장 (WatchingEnd에서만)
                    if (state.State == AppState.WatchingEnd)
                    {
                        try
                        {
                            if (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(intercept: true);
                                if (key.Key == ConsoleKey.F8)
                                {
                                    var path = Path.Combine(
                                        baseDir,
                                        $"debug_end_actual_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png"
                                    );

                                    Cv2.ImWrite(path, frame);
                                    Console.WriteLine($"[ManualCapture] Saved => {path}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ManualCapture] Save failed: {ex.Message}");
                            Logger.Error($"[ManualCapture] Save failed: {ex}");
                        }

                        var endRes = endDetector.Detect(frame);

                        // 매칭 디버그 로그(Reason에 점수 포함)
                        if ((DateTime.Now - lastEndDebugLogAt).TotalMilliseconds >= endDebugLogIntervalMs)
                        {
                            lastEndDebugLogAt = DateTime.Now;
                            Console.WriteLine(BuildEndDebugLine(endRes));
                        }

                        if (endDebounce.Check(endRes.Hit))
                        {
                            Logger.Info($"END detected: {endRes.Reason}");

                            // (4) 종료화면 인식 성공: "종료화면 인식성공"
                            if (!sentEndDetected)
                            {
                                sentEndDetected = true;
                                await notifier.SendAsync("종료화면 인식성공");
                            }

                            state.ToWaitLeagueNews();
                            waitLeagueNewsEnteredAt = DateTime.Now;

                            // (5-전이) WatchingEnd → WaitLeagueNews 전이 로그: "리그결과 감시 대기"
                            if (!sentWaitLeagueNewsEntered)
                            {
                                sentWaitLeagueNewsEntered = true;
                                await notifier.SendAsync("리그결과 감시 대기");
                            }

                            leagueNewsDebounce.Reset();
                        }
                    }
                    else if (state.State == AppState.WaitLeagueNews)
                    {
                        if (waitLeagueNewsEnteredAt.HasValue &&
                            (DateTime.Now - waitLeagueNewsEnteredAt.Value).TotalMilliseconds
                                >= AppConfig.WaitLeagueNewsTimeoutMs)
                        {
                            Logger.Error(
                                $"WAIT_LEAGUE_NEWS timeout ({AppConfig.WaitLeagueNewsTimeoutMs}ms). " +
                                "Fallback to WATCHING_END."
                            );

                            state.ToWatchingEnd();
                            waitLeagueNewsEnteredAt = null;

                            // 다음 경기 사이클을 위해 경기 단위 플래그 리셋
                            sentEndDetected = false;
                            sentWaitLeagueNewsEntered = false;
                            sentLeagueDetected = false;

                            endDebounce.Reset();
                            leagueNewsDebounce.Reset();
                            continue;
                        }

                        var leagueRes = leagueNewsDetector.Detect(frame);

                        if (leagueNewsDebounce.Check(leagueRes.Hit))
                        {
                            Logger.Info($"LEAGUE_NEWS detected: {leagueRes.Reason}");

                            // (5-성공) 리그결과 인식 성공
                            if (!sentLeagueDetected)
                            {
                                sentLeagueDetected = true;
                                await notifier.SendAsync("리그결과 인식성공 -- 재시작하세요");
                            }

                            state.ToWatchingEnd();
                            waitLeagueNewsEnteredAt = null;

                            // 다음 경기 사이클을 위해 경기 단위 플래그 리셋
                            sentEndDetected = false;
                            sentWaitLeagueNewsEntered = false;
                            sentLeagueDetected = false;

                            endDebounce.Reset();
                            leagueNewsDebounce.Reset();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 루프 내부 예외는 앱 종료 대신 로그 남기고 계속
                    if ((DateTime.Now - lastLoopExceptionAt).TotalMilliseconds >= loopExceptionIntervalMs)
                    {
                        lastLoopExceptionAt = DateTime.Now;
                        AppendFatal("Loop exception (capture/detect)", ex);
                    }

                    // 계속 루프
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            // 최상위 예외는 반드시 파일로 남기고 종료(또는 필요 시 무한루프 방지)
            AppendFatal("Main() fatal exception", ex);
        }
    }
}
