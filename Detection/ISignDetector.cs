using OpenCvSharp;

namespace Ma9_Season_Push.Detection;

/// <summary>
/// 화면 프레임에서 특정 사인을 감지하는 인터페이스
/// </summary>
public interface ISignDetector
{
    bool Detect(Mat frame);
}
