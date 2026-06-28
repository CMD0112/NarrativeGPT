using System.IO.Compression;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ExportServiceTests
{
    [Fact]
    public void ExportJsonArchive_includes_entity_media_subdirectory()
    {
        var bundle = AdventureStore.CreateNew("Export media");
        var entityId = Guid.NewGuid();
        var mediaDir = Path.Combine(bundle.DirectoryPath, EntityMediaService.MediaFolderName);
        Directory.CreateDirectory(mediaDir);
        File.WriteAllBytes(Path.Combine(mediaDir, $"{entityId:D}.png"), [0x89, 0x50, 0x4E, 0x47]);

        var zipPath = Path.Combine(Path.GetTempPath(), $"cgw-export-test-{Guid.NewGuid():N}.zip");
        try
        {
            ExportService.ExportJsonArchive(bundle, zipPath);

            using var zip = ZipFile.OpenRead(zipPath);
            Assert.Contains(
                zip.Entries,
                e => e.FullName.Replace('\\', '/').Contains(
                    $"{EntityMediaService.MediaFolderName}/{entityId:D}.png",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }
}
