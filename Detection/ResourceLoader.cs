using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using OpenCvSharp;

namespace Ma9_Season_Push.Detection;

internal static class ResourceLoader
{
    private static readonly Assembly _asm = Assembly.GetExecutingAssembly();

    // key: endsWith(보통 파일명) -> resolved full resource name
    private static readonly ConcurrentDictionary<string, string> _resolvedNameCache = new(StringComparer.OrdinalIgnoreCase);

    // key: resolved full resource name -> decoded COLOR Mat (원본 보관)
    // 주의: 호출자에서 Dispose하면 안 되므로, 반환은 항상 Clone()
    private static readonly ConcurrentDictionary<string, Mat> _decodedColorCache = new();

    public static Mat LoadPngAsMat(string resourceEndsWith)
    {
        if (string.IsNullOrWhiteSpace(resourceEndsWith))
            throw new ArgumentException("resourceEndsWith is empty.", nameof(resourceEndsWith));

        // 혹시 경로 형태로 들어오면 파일명만 사용(호환성 강화)
        var endsWith = Path.GetFileName(resourceEndsWith.Replace('\\', '/'));

        var fullName = _resolvedNameCache.GetOrAdd(endsWith, ResolveFullResourceName);

        // 캐시에는 "원본 Mat"을 보관하고, 외부에는 Clone을 넘겨 Dispose 충돌을 방지
        var cached = _decodedColorCache.GetOrAdd(fullName, DecodeColorMatFromResource);

        // caller가 using Dispose해도 cache는 살아있어야 함
        return cached.Clone();
    }

    private static string ResolveFullResourceName(string endsWith)
    {
        var names = _asm.GetManifestResourceNames();

        var match = names.FirstOrDefault(n =>
            n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));

        if (match != null)
            return match;

        // 실패 시: 운영에서 원인 추적이 가능하도록 정보를 충분히 제공
        var pngs = names.Where(n => n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToArray();
        var available = string.Join(Environment.NewLine, pngs);

        throw new FileNotFoundException(
            $"Embedded resource not found (EndsWith match failed)." + Environment.NewLine +
            $"- requested endsWith: {endsWith}" + Environment.NewLine +
            $"- available png resources:" + Environment.NewLine +
            available
        );
    }

    private static Mat DecodeColorMatFromResource(string fullResourceName)
    {
        using var stream = _asm.GetManifestResourceStream(fullResourceName);
        if (stream == null)
            throw new FileNotFoundException($"GetManifestResourceStream returned null: {fullResourceName}");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        var bytes = ms.ToArray();
        var mat = Cv2.ImDecode(bytes, ImreadModes.Color);

        if (mat.Empty())
            throw new InvalidOperationException($"ImDecode returned empty Mat: {fullResourceName}");

        return mat;
    }
}
