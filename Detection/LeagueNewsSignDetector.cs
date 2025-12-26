// LeagueNewsSignDetector.cs
using System;
using System.IO;
using OpenCvSharp;

namespace Ma9_Season_Push.Detection
{
    /// <summary>
    /// '리그 결과 - 전체 구장 소식' 화면 감지기
    /// - ROI 템플릿 매칭 기반
    /// - (리그 결과) + (전체 구장 소식) 필수 + (다음 버튼) 보조(기본: 필수)
    /// </summary>
    public sealed class LeagueNewsSignDetector : IDisposable
    {
        // 정규화 ROI (0~1) - 2048x1152 캡처 기준으로 튜닝됨
        // - 리그 결과(title): x=0.42, y=0.17, w=0.26, h=0.12
        // - 전체 구장 소식(subtitle): x=0.40, y=0.23, w=0.30, h=0.10
        // - 다음 버튼(next): x=0.44, y=0.70, w=0.18, h=0.12
        private static readonly Rect2d RoiTitle = new(0.42, 0.17, 0.26, 0.12);
        private static readonly Rect2d RoiSubtitle = new(0.40, 0.23, 0.30, 0.10);
        private static readonly Rect2d RoiNext = new(0.44, 0.70, 0.18, 0.12);

        private readonly Mat _tplTitle;
        private readonly Mat _tplSubtitle;
        private readonly Mat _tplNext;

        private readonly double _thTitle;
        private readonly double _thSubtitle;
        private readonly double _thNext;

        private readonly bool _requireNext;
        private bool _disposed;

        public LeagueNewsSignDetector(
            string tplTitlePath,
            string tplSubtitlePath,
            double thTitle = 0.93,
            double thSubtitle = 0.93,
            string? tplNextPath = null,
            double thNext = 0.90,
            bool requireNext = false)
        {
            _tplTitle = LoadTemplateGray(tplTitlePath);
            _tplSubtitle = LoadTemplateGray(tplSubtitlePath);

            _requireNext = requireNext;

            if (_requireNext)
            {
                if (string.IsNullOrWhiteSpace(tplNextPath))
                    throw new ArgumentException("requireNext=true 인 경우 tplNextPath가 필요합니다.", nameof(tplNextPath));

                _tplNext = LoadTemplateGray(tplNextPath);
                if (_tplNext.Empty())
                    throw new InvalidOperationException("LeagueNewsSignDetector: next 템플릿 로드 실패(경로/임베디드 리소스 확인 필요)");
            }
            else
            {
                _tplNext = new Mat(); // 미사용
            }

            if (_tplTitle.Empty() || _tplSubtitle.Empty())
                throw new InvalidOperationException("LeagueNewsSignDetector: title/subtitle 템플릿 로드 실패(경로/임베디드 리소스 확인 필요)");

            _thTitle = thTitle;
            _thSubtitle = thSubtitle;
            _thNext = thNext;
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

            using var color = ResourceLoader.LoadPngAsMat(key);
            if (color.Empty())
                throw new InvalidOperationException($"LeagueNewsSignDetector: 임베디드 리소스 디코드 실패: {key}");

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
            var frameSize = $"{gray.Width}x{gray.Height}";

            var titleScore = MatchInRoi(gray, RoiTitle, _tplTitle, out var titleDiag);
            if (titleScore < _thTitle)
                return DetectionResult.Fail($"title<{titleScore:0.000}> {titleDiag} frame={frameSize}");

            var subtitleScore = MatchInRoi(gray, RoiSubtitle, _tplSubtitle, out var subDiag);
            if (subtitleScore < _thSubtitle)
                return DetectionResult.Fail($"subtitle<{subtitleScore:0.000}> {subDiag} frame={frameSize}");

            if (_requireNext)
            {
                var nextScore = MatchInRoi(gray, RoiNext, _tplNext, out var nextDiag);
                if (nextScore < _thNext)
                    return DetectionResult.Fail(
                        $"next<{nextScore:0.000}> (title={titleScore:0.000}, subtitle={subtitleScore:0.000}) {nextDiag} frame={frameSize}"
                    );

                return DetectionResult.Ok(
                    $"hit title={titleScore:0.000}, subtitle={subtitleScore:0.000}, next={nextScore:0.000} frame={frameSize}"
                );
            }

            return DetectionResult.Ok($"hit title={titleScore:0.000}, subtitle={subtitleScore:0.000} frame={frameSize}");
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

        // ===== [변경] diag(out) 포함 + ROI < Template 방어 =====
        private static double MatchInRoi(Mat grayFrame, Rect2d roiNorm, Mat tplGray, out string diag)
        {
            var roi = NormalizeToPixelRect(grayFrame, roiNorm);

            diag = $"roi={roi.Width}x{roi.Height}, tpl={tplGray.Width}x{tplGray.Height}";

            using var cropped = new Mat(grayFrame, roi);

            // ROI가 템플릿보다 작으면 MatchTemplate 예외 가능성이 있으니 0으로 처리
            if (cropped.Width < tplGray.Width || cropped.Height < tplGray.Height)
                return 0.0;

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

            _tplTitle.Dispose();
            _tplSubtitle.Dispose();
            _tplNext.Dispose();
        }
    }
}
