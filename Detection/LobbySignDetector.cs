using OpenCvSharp;

namespace Ma9_Season_Push.Detection;

public class LobbySignDetector : ISignDetector
{
    public bool Detect(Mat frame)
    {
        // TODO: 로비 ROI 템플릿 매칭 구현
        if (frame is null || frame.Empty())
            return false;

        return false;
    }
}
