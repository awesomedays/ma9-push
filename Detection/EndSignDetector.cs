// EndSignDetector.cs
using System;
using System.IO;
using OpenCvSharp;

namespace Ma9_Season_Push.Detection
{
    /// <summary>
    /// 시즌모드 종료(결과) 화면 감지기 - Confirm + Reward bar
    /// - ROI 템플릿 매칭 기반
    /// - Confirm(확인 버튼) + Reward bar(상단 REWARD 라벨) 동시 매칭으로 Hit 판정
    /// - WIN/LOSE 변동 요소는 사용하지 않음
    /// </summary>
    public sealed class EndSignDetector : IDisposable
    {
        // 정규화 ROI (0~1)
        // Confirm: 하단 "확인" 버튼 영역 (이미 동작 검증됨)
        private static readonly Rect2d RoiConfirm = new(0.44, 0.905, 0.14, 0.07);

        // Reward: "REWARD" 라벨(노란 바) + 그 주변 상단 프레임 일부까지 포함하도록 보수적으로 크게 잡음
        // 기존(0.085)은 상단부라 실제 REWARD 영역과 어긋날 가능성이 높았고, height도 작아 ROI<Template 문제가 발생했음.
        // 1920x1080 기준 대략 y≈0.47~0.63 영역이 REWARD 라벨/상단 프레임과 잘 겹침.
        private static readonly Rect2d RoiReward = new(0.33, 0.47, 0.34, 0.16);

        private readonly Mat _tplConfirm;
        private readonly Mat _tplReward;

        private readonly double _thConfirm;
        private readonly double _thReward;

        private bool _disposed;

        public EndSignDetector(
            string tplConfirmPath,
            string tplRewardPath,
            double thConfirm = 0.92,
            double thReward = 0.86)
        {
            _tplConfirm = LoadTemplateGray(tplConfirmPath);
            _tplReward = LoadTemplateGray(tplRewardPath);

            if (_tplConfirm.Empty() || _tplReward.Empty())
                throw new InvalidOperationException("EndSignDetector: 템플릿 로드 실패(임베디드 리소스/경로 확인 필요)");

            _thConfirm = thConfirm;
            _thReward = thReward;
        }

        private static Mat LoadTemplateGray(string pathOrResource)
        {
            // 1) 파일이 실제로 존재하면 파일을 우선 사용 (개발/디버깅 편의)
            if (!string.IsNullOrWhiteSpace(pathOrResource) && File.Exists(pathOrResource))
                return Cv2.ImRead(pathOrResource, ImreadModes.Grayscale);

            // 2) 임베디드 리소스에서 로드 (완전 단일 exe)
            var key = string.IsNullOrWhiteSpace(pathOrResource)
                ? throw new ArgumentException("template path/resource is empty", nameof(pathOrResource))
                : Path.GetFileName(pathOrResource.Replace('\\', '/'));

            using var color = ResourceLoader.LoadPngAsMat(key); // 보통 Color로 디코드됨
            if (color.Empty())
                throw new InvalidOperationException($"EndSignDetector: 임베디드 리소스 디코드 실패: {key}");

            // 그레이스케일 템플릿으로 정규화
            if (color.Channels() == 1)
                return color.Clone();

            var gray = new Mat();
            Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

        public DetectionResult Detect(Mat frame)
        {
            if (_disposed)
                return DetectionResult.Fail("detector disposed");

            if (frame is null || frame.Empty())
                return DetectionResult.Fail("frame empty");

            using var gray = ToGray(frame);

            // 프레임 사이즈를 Reason에 함께 실어 디버깅에 활용
            var frameSize = $"{gray.Width}x{gray.Height}";

            // 1) Confirm (1차 게이트)
            var confirmScore = MatchInRoi(gray, RoiConfirm, _tplConfirm, out var confirmDiag);
            if (confirmScore < _thConfirm)
                return DetectionResult.Fail($"confirm<{confirmScore:0.000}> {confirmDiag} frame={frameSize}");

            // 2) Reward (2차 검증)
            var rewardScore = MatchInRoi(gray, RoiReward, _tplReward, out var rewardDiag);
            if (rewardScore < _thReward)
            {
                // 로그 혼선 종결: reward 실패 시에도 "이 프레임에서 confirm이 얼마였는지"를 함께 출력
                return DetectionResult.Fail(
                    $"reward<{rewardScore:0.000}> (confirm={confirmScore:0.000}) {rewardDiag} frame={frameSize}"
                );
            }

            return DetectionResult.Ok($"hit confirm={confirmScore:0.000}, reward={rewardScore:0.000} frame={frameSize}");
        }

        private static Mat ToGray(Mat src)
        {
            if (src.Channels() == 1)
                return src.Clone();

            var gray = new Mat();

            if (src.Channels() == 3)
            {
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                return gray;
            }

            if (src.Channels() == 4)
            {
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY);
                return gray;
            }

            throw new InvalidOperationException($"Unsupported channel count: {src.Channels()}");
        }

        /// <summary>
        /// ROI 내에서 템플릿 매칭. ROI가 템플릿보다 작으면 예외 대신 낮은 점수로 Fail 처리.
        /// 진단을 위해 ROI/템플릿/프레임 크기 정보를 diag로 반환.
        /// </summary>
        private static double MatchInRoi(Mat grayFrame, Rect2d roiNorm, Mat tplGray, out string diag)
        {
            var roi = NormalizeToPixelRect(grayFrame, roiNorm);

            // 템플릿/ROI 유효성 가드 (MatchTemplate 예외 방지)
            // OpenCV 요구 조건: image(ROI) >= template 이어야 함.
            int roiW = roi.Width;
            int roiH = roi.Height;

            int tplW = tplGray?.Width ?? 0;
            int tplH = tplGray?.Height ?? 0;

            diag = $"roi={roiW}x{roiH}, tpl={tplW}x{tplH}";

            if (tplGray is null || tplGray.Empty())
                return -1.0; // 템플릿 이상

            if (roiW <= 0 || roiH <= 0)
                return -1.0;

            if (roiW < tplW || roiH < tplH)
            {
                // MatchTemplate 예외 대신 점수 -1로 반환하고 caller가 Fail 처리
                return -1.0;
            }

            using var cropped = new Mat(grayFrame, roi);
            using var result = new Mat();

            Cv2.MatchTemplate(cropped, tplGray, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
            return maxVal;
        }

        private static Rect NormalizeToPixelRect(Mat frame, Rect2d roiNorm)
        {
            int x = (int)Math.Round(frame.Width * roiNorm.X);
            int y = (int)Math.Round(frame.Height * roiNorm.Y);
            int w = (int)Math.Round(frame.Width * roiNorm.Width);
            int h = (int)Math.Round(frame.Height * roiNorm.Height);

            x = Clamp(x, 0, frame.Width - 1);
            y = Clamp(y, 0, frame.Height - 1);
            w = Clamp(w, 1, frame.Width - x);
            h = Clamp(h, 1, frame.Height - y);

            return new Rect(x, y, w, h);
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _tplConfirm.Dispose();
            _tplReward.Dispose();
        }
    }

    public sealed class DetectionResult
    {
        public bool Hit { get; }
        public string Reason { get; }

        private DetectionResult(bool hit, string reason)
        {
            Hit = hit;
            Reason = reason;
        }

        public static DetectionResult Ok(string reason) => new(true, reason);
        public static DetectionResult Fail(string reason) => new(false, reason);
    }
}
