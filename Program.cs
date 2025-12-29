using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Ma9_Season_Push.Core;
using Ma9_Season_Push.Capture;
using Ma9_Season_Push.Detection;
using Ma9_Season_Push.Logging;
using Ma9_Season_Push.Notification;

namespace Ma9_Season_Push;

internal static class Program
{
    // 작업관리자 프로세스명: "Ma9Ma9Remaster.exe"
    // GetProcessesByName에는 확장자 제외한 이름만 넣어야 함.
    private const string Ma9ProcessName = "Ma9Ma9Remaster";

    // ===== DPI aware (Per-monitor v2 우선) =====
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new(-3);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new(-2);

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

    // ===== 윈도우 RECT 조회(스크린 인덱스 판별용) =====
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private static readonly object _fatalLock = new();

    private static void AppendFatal(string title, Exception? ex = null)
    {
        try
        {
            lock (_fatalLock)
            {
                var path = AppPaths.FatalLogPath;
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                var msg =
                    $"[{ts}] {title}{Environment.NewLine}" +
                    (ex == null ? "" : ex + Environment.NewLine) +
                    new string('-', 120) + Environment.NewLine;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, msg);
            }
        }
        catch
        {
            // 로깅 실패로 또 죽지 않게 무시
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                if (e.ExceptionObject is Exception ex) AppendFatal("AppDomain.UnhandledException", ex);
                else AppendFatal("AppDomain.UnhandledException (non-Exception object)");
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
            // ignore and fallback below
        }

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
            // ignore and fallback below
        }

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

    [STAThread]
    private static void Main()
    {
        RegisterGlobalExceptionHandlers();
        TryEnableDpiAwareness();

        ApplicationConfiguration.Initialize();

        // 트레이 앱 컨텍스트 실행
        Application.Run(new TrayAppContext(RunWatcherAsync));
    }

    /// <summary>
    /// 트레이에서 구동되는 워커(감시 루프)
    /// </summary>
    internal static async Task RunWatcherAsync(CancellationToken token)
    {
        try
        {
            var state = new StateMachine();

            // 사양 확정: 서브1(ScreenIndex=1) 고정 감시
            var capture = new CaptureService(screenIndex: 1);

            var baseDir = AppPaths.ExeDir;

            using var endDetector = new EndSignDetector(
                tplConfirmPath: Path.Combine(baseDir, "Assets", "tpl_end_confirm_gray.png"),
                tplRewardPath: Path.Combine(baseDir, "Assets", "tpl_end_reward_gray.png"),
                thConfirm: 0.93,
                thReward: 0.88
            );

            using var leagueNewsDetector = new LeagueNewsSignDetector(
                tplTitlePath: Path.Combine(baseDir, "Assets", "tpl_lobby_title_gray_new.png"),
                tplSubtitlePath: Path.Combine(baseDir, "Assets", "tpl_lobby_subtitle_gray_new.png"),
                tplNextPath: Path.Combine(baseDir, "Assets", "tpl_lobby_next_gray.png"),
                thTitle: 0.93,
                thSubtitle: 0.93,
                thNext: 0.90,
                requireNext: false
            );

            var endDebounce = new Debouncer();
            var notifier = new TelegramNotifier();

            var sentAppOn = false;
            var sentClientOn = false;
            var sentEndDetected = false;
            var sentLeagueDetected = false;

            var lastProcCheckAt = DateTime.MinValue;
            var procCheckIntervalMs = 2000;

            var lastLoopExceptionAt = DateTime.MinValue;
            var loopExceptionIntervalMs = 2000;

            DateTime? waitLeagueNewsEnteredAt = null;

            var beforeStart = state.State;
            state.Start();
            Logger.Info("Ma9_Season_Push started.");
            Logger.Info($"[State] {beforeStart} -> {state.State} | trigger=AppStart");

            if (!sentAppOn)
            {
                sentAppOn = true;
                await notifier.SendAsync("마구알림 ON");
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(AppConfig.ObserveIntervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // 클라이언트 프로세스 감지(1회)
                if (!sentClientOn &&
                    (DateTime.Now - lastProcCheckAt).TotalMilliseconds >= procCheckIntervalMs)
                {
                    lastProcCheckAt = DateTime.Now;

                    try
                    {
                        var procs = Process.GetProcessesByName(Ma9ProcessName);
                        if (procs is { Length: > 0 })
                        {
                            var idx = TryGetMa9ClientScreenIndex();
                            Logger.Info(idx.HasValue
                                ? $"[Ma9 Screen] {Ma9ProcessName}.exe ScreenIndex={idx.Value}"
                                : $"[Ma9 Screen] {Ma9ProcessName}.exe ScreenIndex=(unknown)");

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

                            if (!sentEndDetected)
                            {
                                sentEndDetected = true;
                                await notifier.SendAsync("경기종료");
                            }

                            var from = state.State;
                            state.ToWaitLeagueNews();
                            waitLeagueNewsEnteredAt = DateTime.Now;

                            Logger.Info($"[State] {from} -> {state.State} | trigger=EndDetected->WaitLeagueNews | detail={endRes.Reason}");
                        }
                    }
                    else if (state.State == AppState.WaitLeagueNews)
                    {
                        if (waitLeagueNewsEnteredAt.HasValue &&
                            (DateTime.Now - waitLeagueNewsEnteredAt.Value).TotalMilliseconds >= AppConfig.WaitLeagueNewsTimeoutMs)
                        {
                            Logger.Error($"WAIT_LEAGUE_NEWS timeout ({AppConfig.WaitLeagueNewsTimeoutMs}ms). Fallback to WATCHING_END.");

                            try
                            {
                                await notifier.SendAsync("대기모드 전환 : 타임아웃");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to send timeout telegram (ignored): {ex}");
                            }

                            var from = state.State;
                            state.ToWatchingEnd();
                            waitLeagueNewsEnteredAt = null;

                            Logger.Info($"[State] {from} -> {state.State} | trigger=WaitLeagueNewsTimeout");

                            sentEndDetected = false;
                            sentLeagueDetected = false;

                            endDebounce.Reset();
                            continue;
                        }

                        var leagueRes = leagueNewsDetector.Detect(frame);

                        // WaitLeagueNews에서는 1-hit 즉시 확정
                        if (leagueRes.Hit)
                        {
                            Logger.Info($"LEAGUE_NEWS detected (instant): {leagueRes.Reason}");

                            if (!sentLeagueDetected)
                            {
                                sentLeagueDetected = true;
                                await notifier.SendAsync("대기모드 전환");
                            }

                            var from = state.State;
                            state.ToWatchingEnd();
                            waitLeagueNewsEnteredAt = null;

                            Logger.Info($"[State] {from} -> {state.State} | trigger=LeagueDetected->WatchingEnd | detail={leagueRes.Reason}");

                            sentEndDetected = false;
                            sentLeagueDetected = false;

                            endDebounce.Reset();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if ((DateTime.Now - lastLoopExceptionAt).TotalMilliseconds >= loopExceptionIntervalMs)
                    {
                        lastLoopExceptionAt = DateTime.Now;
                        AppendFatal("Loop exception (capture/detect)", ex);
                    }
                }
            }

            Logger.Info("Watcher loop cancelled. Exiting worker.");
        }
        catch (Exception ex)
        {
            AppendFatal("RunWatcherAsync fatal exception", ex);
        }
    }
}
