using System.IO;

namespace ChatGPTWrapper;

/// <summary>Invalidates cached wrapper asset payloads when bundled files change on disk.</summary>
internal static class WrapperAssetCache
{
    public static long ComputeStamp(params string[] paths)
    {
        long stamp = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                stamp ^= File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                /* ignore */
            }
        }

        return stamp;
    }
}
