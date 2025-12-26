using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenCvSharp;

namespace Ma9_Season_Push.Detection;

internal static class ResourceLoader
{
    public static Mat LoadPngAsMat(string resourceEndsWith)
    {
        var asm = Assembly.GetExecutingAssembly();

        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceEndsWith, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            throw new FileNotFoundException($"Embedded resource not found: *{resourceEndsWith}");

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Failed to open embedded resource stream: {resourceName}");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        var bytes = ms.ToArray();
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    // [테스트용] 임베디드 리소스 전체 목록 출력
    public static string[] ListAllResourceNames()
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceNames();
    }
}
