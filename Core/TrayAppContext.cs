using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Ma9_Season_Push.Logging;
using Ma9_Season_Push.Notification;

namespace Ma9_Season_Push.Core;

/// <summary>
/// 시스템 트레이 상주 앱 컨텍스트
/// - NotifyIcon + ContextMenu
/// - 워커(Task) 생명주기 관리
/// - 정상 종료 시퀀스 책임
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private const string TrayIcoFileName = "Ma9_Season_Push_AppIcon.ico";

    private readonly NotifyIcon _trayIcon;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private bool _exitRequested;

    // 트레이 아이콘을 Dispose하지 않도록 수명 유지
    private Icon? _loadedIcon;

    public TrayAppContext(Func<CancellationToken, Task> workerFactory)
    {
        if (workerFactory == null)
            throw new ArgumentNullException(nameof(workerFactory));

        // ===== 트레이 메뉴(항상 다크 / 커스텀 렌더링) =====
        var menu = TrayMenuStyle.CreateDarkMenu();
        menu.Items.Add(TrayMenuStyle.CreateItem("실행폴더", OpenExeFolder));
        menu.Items.Add(TrayMenuStyle.CreateItem("로그폴더", OpenLogsFolder));
        menu.Items.Add(TrayMenuStyle.CreateSeparator());
        menu.Items.Add(TrayMenuStyle.CreateItemAsync("종료", ExitAsync));

        // ===== 트레이 아이콘(리소스에서 로드) =====
        _loadedIcon = LoadTrayIconFromEmbeddedResourceOrNull();

        _trayIcon = new NotifyIcon
        {
            Text = "Ma9 Season Push",
            Icon = _loadedIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };

        // ===== 워커 시작 =====
        _workerTask = Task.Run(() => workerFactory(_cts.Token));

        Logger.Info("TrayAppContext initialized. Worker started.");
    }

    private static void OpenExeFolder()
    {
        try
        {
            var dir = AppPaths.ExeDir;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open exe folder: {ex}");
        }
    }

    private static void OpenLogsFolder()
    {
        try
        {
            var dir = AppPaths.LogsDir;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open logs folder: {ex}");
        }
    }

    /// <summary>
    /// EmbeddedResource(ICO)에서 트레이 아이콘을 로드한다.
    /// - 단일 EXE 내부 포함 요구사항 충족
    /// - 리소스명은 환경에 따라 다를 수 있으므로, 파일명 suffix로 탐색한다.
    /// </summary>
    private static Icon? LoadTrayIconFromEmbeddedResourceOrNull()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();

            // 보통: "Ma9_Season_Push.Assets.Ma9_Season_Push_AppIcon.ico"
            // 프로젝트 기본 네임스페이스/폴더에 따라 prefix는 달라질 수 있으므로 suffix 탐색이 안전함.
            string? resName = null;
            foreach (var n in names)
            {
                if (n.EndsWith(".Assets." + TrayIcoFileName, StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith("." + TrayIcoFileName, StringComparison.OrdinalIgnoreCase))
                {
                    resName = n;
                    break;
                }
            }

            if (resName == null)
            {
                Logger.Error($"[TrayIcon] Embedded resource not found: {TrayIcoFileName}");
                return null;
            }

            using var s = asm.GetManifestResourceStream(resName);
            if (s == null)
            {
                Logger.Error($"[TrayIcon] Resource stream is null: {resName}");
                return null;
            }

            // Stream에서 Icon 생성
            var icon = new Icon(s);
            Logger.Info($"[TrayIcon] Loaded from resource: {resName}");
            return icon;
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayIcon] Failed to load embedded icon (fallback to default): {ex}");
            return null;
        }
    }

    private async Task ExitAsync()
    {
        if (_exitRequested)
            return;

        _exitRequested = true;
        Logger.Info("Exit requested from tray menu.");

        try
        {
            // 1) 워커 취소
            _cts.Cancel();

            // 2) OFF 알림 (best-effort)
            try
            {
                var notifier = new TelegramNotifier();
                await notifier.SendAsync("마구알림 OFF");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send OFF telegram (ignored): {ex}");
            }

            // 3) 워커 종료 대기 (짧게)
            try
            {
                await Task.WhenAny(_workerTask, Task.Delay(3000));
            }
            catch
            {
                // ignore
            }
        }
        finally
        {
            // 4) 리소스 정리 및 종료
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            _loadedIcon?.Dispose();
            _loadedIcon = null;

            _cts.Dispose();

            Logger.Info("TrayAppContext exiting.");
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _trayIcon?.Dispose(); } catch { }
            try { _loadedIcon?.Dispose(); } catch { }
            try { _cts?.Dispose(); } catch { }
        }

        base.Dispose(disposing);
    }
}
