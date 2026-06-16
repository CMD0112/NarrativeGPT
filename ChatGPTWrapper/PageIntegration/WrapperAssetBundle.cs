using System.IO;
using System.Text;
using System.Text.Json;

namespace ChatGPTWrapper.PageIntegration;

/// <summary>
/// Builds ordered wrapper asset payloads with shared kernel bootstrap and CSS injection helpers.
/// </summary>
public static class WrapperAssetBundle
{
    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "wrapper-assets");

    private static string? _cachedKernelPayload;
    private static long _cachedKernelStamp;

    public static string AssetsDirectory => AssetsDir;

    public static string AssetPath(string fileName) => Path.Combine(AssetsDir, fileName);

    public static bool AssetExists(string fileName) => File.Exists(AssetPath(fileName));

    public static string ReadAsset(string fileName)
    {
        var path = AssetPath(fileName);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    public static string GetKernelPayload()
    {
        var kernelPath = AssetPath("cgw-page-kernel.js");
        var composerPath = AssetPath("cgw-composer-dom.js");
        var stamp = WrapperAssetCache.ComputeStamp(kernelPath, composerPath);
        if (_cachedKernelPayload is not null && stamp == _cachedKernelStamp)
            return _cachedKernelPayload;

        if (!File.Exists(kernelPath))
            return "";

        try
        {
            var sb = new StringBuilder();
            sb.Append(File.ReadAllText(kernelPath));
            sb.Append('\n');
            if (File.Exists(composerPath))
            {
                sb.Append(File.ReadAllText(composerPath));
                sb.Append('\n');
            }

            _cachedKernelPayload = sb.ToString();
            _cachedKernelStamp = stamp;
            return _cachedKernelPayload;
        }
        catch
        {
            return "";
        }
    }

    public static string BuildCssJsBundle(
        string cssFileName,
        string cssGlobalKey,
        string styleElementId,
        params string[] jsFileNames)
    {
        var cssPath = AssetPath(cssFileName);
        var jsPaths = jsFileNames.Select(AssetPath).ToArray();
        var stampPaths = new List<string> { cssPath };
        stampPaths.AddRange(jsPaths.Where(File.Exists));
        var stamp = WrapperAssetCache.ComputeStamp(stampPaths.ToArray());

        var cacheKey = $"{cssFileName}|{cssGlobalKey}|{styleElementId}|{string.Join(",", jsFileNames)}";
        return BuildCssJsBundleCached(cacheKey, stamp, cssPath, cssGlobalKey, styleElementId, jsFileNames);
    }

    private static readonly Dictionary<string, (long Stamp, string Payload)> CssJsCache = new();

    private static string BuildCssJsBundleCached(
        string cacheKey,
        long stamp,
        string cssPath,
        string cssGlobalKey,
        string styleElementId,
        string[] jsFileNames)
    {
        if (CssJsCache.TryGetValue(cacheKey, out var cached) && cached.Stamp == stamp)
            return cached.Payload;

        var primaryJs = jsFileNames.Length > 0 ? AssetPath(jsFileNames[0]) : "";
        if (jsFileNames.Length > 0 && !File.Exists(primaryJs))
            return "";

        try
        {
            var cssText = File.Exists(cssPath) ? File.ReadAllText(cssPath) : "";
            var sb = new StringBuilder();
            sb.Append(GetKernelPayload());
            sb.Append("globalThis.");
            sb.Append(cssGlobalKey);
            sb.Append(" = ");
            sb.Append(JsonSerializer.Serialize(cssText));
            sb.Append(";\n");
            sb.Append("(function(){try{var id=");
            sb.Append(JsonSerializer.Serialize(styleElementId));
            sb.Append(";var el=document.getElementById(id);if(!el){el=document.createElement('style');el.id=id;document.head.appendChild(el);}el.textContent=globalThis.");
            sb.Append(cssGlobalKey);
            sb.Append("||'';}catch(e){}})();\n");

            foreach (var jsFile in jsFileNames)
            {
                var jsPath = AssetPath(jsFile);
                if (File.Exists(jsPath))
                {
                    sb.Append(File.ReadAllText(jsPath));
                    sb.Append('\n');
                }
            }

            var payload = sb.ToString();
            CssJsCache[cacheKey] = (stamp, payload);
            return payload;
        }
        catch
        {
            return "";
        }
    }

    public static string BuildJsBundle(params string[] jsFileNames)
    {
        var paths = jsFileNames.Select(AssetPath).Where(File.Exists).ToArray();
        var stamp = WrapperAssetCache.ComputeStamp(paths);
        var cacheKey = "js|" + string.Join(",", jsFileNames);
        if (CssJsCache.TryGetValue(cacheKey, out var cached) && cached.Stamp == stamp)
            return cached.Payload;

        if (paths.Length == 0)
            return "";

        try
        {
            var sb = new StringBuilder();
            sb.Append(GetKernelPayload());
            foreach (var path in paths)
            {
                sb.Append(File.ReadAllText(path));
                sb.Append('\n');
            }

            var payload = sb.ToString();
            CssJsCache[cacheKey] = (stamp, payload);
            return payload;
        }
        catch
        {
            return "";
        }
    }
}
