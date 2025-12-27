using Ma9_Season_Push;

namespace Ma9_Season_Push.Detection;

public class Debouncer
{
    private int _currentCount = 0;

    /// <summary>
    /// 감지 결과를 누적하여 연속 감지 확정 여부를 반환한다.
    /// </summary>
    /// <param name="detected">현재 프레임 감지 여부</param>
    /// <returns>확정되면 true, 아니면 false</returns>
    public bool Check(bool detected)
    {
        if (detected)
        {
            _currentCount++;

            if (_currentCount >= AppConfig.ConfirmCount)
            {
                Reset();
                return true;
            }
        }
        else
        {
            Reset();
        }

        return false;
    }

    /// <summary>
    /// 누적 감지 카운트를 초기화한다.
    /// </summary>
    public void Reset()
    {
        _currentCount = 0;
    }
}
