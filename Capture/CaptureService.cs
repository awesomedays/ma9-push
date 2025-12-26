using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using OpenCvSharp;

using Ma9_Season_Push.Logging;

namespace Ma9_Season_Push.Capture
{
    /// <summary>
    /// 지정된 모니터 화면 캡처 → OpenCvSharp Mat(CV_8UC4) 반환
    /// - unsafe 미사용
    /// - Stride 음수(bottom-up DIB)까지 완전 대응
    /// - OpenCvSharp.Extensions 의존 제거
    /// </summary>
    public sealed class CaptureService
    {
        private readonly Screen _targetScreen;

        // CaptureFrame 내부 예외 스팸 방지
        private DateTime _lastCaptureErrorAt = DateTime.MinValue;
        private const int CaptureErrorLogIntervalMs = 2000;

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
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return new Mat();

            try
            {
                using var bitmap = new Bitmap(
                    bounds.Width,
                    bounds.Height,
                    PixelFormat.Format32bppArgb
                );

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

                var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4);

                var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var bmpData = bitmap.LockBits(
                    rect,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb
                );

                try
                {
                    int width = bitmap.Width;
                    int height = bitmap.Height;
                    int rowBytes = width * 4;

                    int srcStride = bmpData.Stride;          // 부호 중요
                    int dstStride = (int)mat.Step();         // 보통 양수
                    IntPtr scan0 = bmpData.Scan0;

                    // Stride 부호에 따라 실제 첫 행 포인터 계산
                    // - srcStride > 0 : scan0가 첫 행(상단)
                    // - srcStride < 0 : scan0가 마지막 행(하단)
                    IntPtr firstRowPtr = scan0;
                    if (srcStride < 0)
                    {
                        firstRowPtr = scan0 + srcStride * (height - 1);
                        srcStride = -srcStride; // 이후 계산은 양수 stride로
                    }

                    // 행 단위 복사 (상단 → 하단 순서 보장)
                    var buffer = new byte[rowBytes];
                    IntPtr dstBase = mat.Data;

                    for (int y = 0; y < height; y++)
                    {
                        IntPtr srcRow = firstRowPtr + (y * srcStride);
                        IntPtr dstRow = dstBase + (y * dstStride);

                        Marshal.Copy(srcRow, buffer, 0, rowBytes);
                        Marshal.Copy(buffer, 0, dstRow, rowBytes);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bmpData);
                }

                return mat;
            }
            catch (Exception ex)
            {
                // 운영 안정성: 빈 Mat 반환 유지 + 최소 로그
                if ((DateTime.Now - _lastCaptureErrorAt).TotalMilliseconds >= CaptureErrorLogIntervalMs)
                {
                    _lastCaptureErrorAt = DateTime.Now;
                    Logger.Error($"CaptureFrame failed: {ex}");
                }
                return new Mat();
            }
        }
    }
}
