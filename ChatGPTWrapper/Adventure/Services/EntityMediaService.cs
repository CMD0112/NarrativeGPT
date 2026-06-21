using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChatGPTWrapper.Adventure.Services;

public static class EntityMediaService
{
    public const string MediaFolderName = "entity-media";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
    };

    public static string RelativePathFor(Guid entityId, string extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? ".png" : extension.ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;

        return $"{MediaFolderName}/{entityId:D}{ext}";
    }

    public static bool IsSupportedImageFile(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static string? ImportFromFile(Guid adventureId, Guid entityId, string sourcePath)
    {
        if (!File.Exists(sourcePath) || !IsSupportedImageFile(sourcePath))
            return null;

        var adventureDir = AppDirectories.AdventureDirectory(adventureId);
        var mediaDir = Path.Combine(adventureDir, MediaFolderName);
        Directory.CreateDirectory(mediaDir);

        var relative = RelativePathFor(entityId, Path.GetExtension(sourcePath));
        var destination = Path.Combine(adventureDir, relative.Replace('/', Path.DirectorySeparatorChar));
        File.Copy(sourcePath, destination, overwrite: true);
        return relative.Replace('\\', '/');
    }

    public static string? ResolveAbsolutePath(Guid adventureId, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolute = Path.Combine(AppDirectories.AdventureDirectory(adventureId), normalized);
        return File.Exists(absolute) ? absolute : null;
    }

    public static void Delete(Guid adventureId, string? relativePath)
    {
        var absolute = ResolveAbsolutePath(adventureId, relativePath);
        if (absolute is null)
            return;

        try
        {
            File.Delete(absolute);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static ImageSource? TryLoadImage(Guid adventureId, string? relativePath, int decodePixelWidth = 0)
    {
        var absolute = ResolveAbsolutePath(adventureId, relativePath);
        return absolute is null ? null : TryLoadImageFromAbsolute(absolute, decodePixelWidth);
    }

    public static ImageSource? TryLoadImageFromAbsolute(string absolutePath, int decodePixelWidth = 0)
    {
        if (!File.Exists(absolutePath))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(absolutePath), UriKind.Absolute);
            if (decodePixelWidth > 0)
                image.DecodePixelWidth = decodePixelWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
