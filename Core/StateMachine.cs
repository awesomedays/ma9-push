using Ma9_Season_Push.Logging;

namespace Ma9_Season_Push.Core;

public class StateMachine
{
    public AppState State { get; private set; } = AppState.Idle;

    /// <summary>
    /// 시즌모드 알림 시작 (IDLE → WATCHING_END)
    /// </summary>
    public void Start()
    {
        State = AppState.WatchingEnd;
        Logger.Info("State -> WATCHING_END");
    }

    /// <summary>
    /// 시즌모드 알림 종료 (ANY → IDLE)
    /// </summary>
    public void Stop()
    {
        State = AppState.Idle;
        Logger.Info("State -> IDLE");
    }

    /// <summary>
    /// 경기 종료 화면 검증 후, 전체구장소식 대기 상태로 전이 (WATCHING_END → WAIT_LEAGUE_NEWS)
    /// </summary>
    public void ToWaitLeagueNews()
    {
        State = AppState.WaitLeagueNews;
        Logger.Info("State -> WAIT_LEAGUE_NEWS");
    }

    /// <summary>
    /// 전체구장소식 검증 후, 다시 경기 종료 화면 감시로 복귀 (WAIT_LEAGUE_NEWS → WATCHING_END)
    /// </summary>
    public void ToWatchingEnd()
    {
        State = AppState.WatchingEnd;
        Logger.Info("State -> WATCHING_END");
    }
}
