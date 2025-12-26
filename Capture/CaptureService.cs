using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace Ma9_Season_Push.Capture
{
    /// <summary>
    /// 지정된 모니터 화면 캡처 → OpenCvSharp Mat 변환
    /// - 중요: BitmapConverter.ToMat(bitmap)은 bitmap 메모리를 참조하는 Mat(헤더)일 수 있음
    ///         따라서 반환 전에 반드시 Clone()하여 Mat이 자기 메모리를 소유하게 해야 함
    /// </summary>
    public sealed class CaptureService
    {
        private readonly Screen _targetScreen;

        public CaptureService(int screenIndex = 1)
        {
            var screens = Screen.AllScreens;
            if (screenIndex < 0 || screenIndex >= screens.Length)
                screenIndex = 0;

            _targetScreen = screens[screenIndex];
        }

        public Mat CaptureFrame()
        {
            var bounds = _targetScreen.Bounds;

            // bounds가 비정상일 때 방어
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return new Mat();

            try
            {
                // Bitmap을 만들고, CopyFromScreen으로 픽셀 채운 뒤
                // ToMat -> Clone -> Bitmap Dispose 순서를 보장해야 함
                using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(
                        bounds.X,
                        bounds.Y,
                        0,
                        0,
                        bounds.Size,
                        CopyPixelOperation.SourceCopy
                    );
                }

                // ToMat이 bitmap 메모리를 참조할 수 있으므로, 반드시 Clone()해서 반환
                using var tmp = BitmapConverter.ToMat(bitmap);   // tmp는 bitmap 메모리 참조 가능
                return tmp.Clone();                               // 반환 Mat은 독립 메모리 소유
            }
            catch
            {
                // 여기서 예외가 나면 managed 예외로 잡히지만,
                // 현재 문제는 “즉사” 케이스라 우선 안전하게 빈 Mat 반환
                return new Mat();
            }
        }
    }
}
