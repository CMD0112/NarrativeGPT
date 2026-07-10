using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class AdventureIndexDirectoryServiceTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public AdventureIndexDirectoryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-Index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
        WrapperSettingsStore.Save(new WrapperSettings
        {
            AdventuresDirectoryOverride = Path.Combine(_tempRoot, "library"),
        });
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void SanitizeLinkName_replaces_invalid_filename_characters()
    {
        Assert.Equal("King_ Red", AdventureIndexDirectoryService.SanitizeLinkName("King: Red"));
    }

    [Fact]
    public void SyncLink_creates_named_symlink_to_guid_folder()
    {
        var bundle = AdventureStore.CreateNew("Indexed Adventure");
        AdventureIndexDirectoryService.SyncLink(bundle.Metadata.Id, bundle.Metadata.Title);

        var linkPath = AdventureIndexDirectoryService.FindLinkPathForAdventure(bundle.Metadata.Id);
        Assert.NotNull(linkPath);
        Assert.Equal("Indexed Adventure", StripShortcutExtension(Path.GetFileName(linkPath!)));
        Assert.True(Directory.Exists(linkPath));

        var resolved = new DirectoryInfo(linkPath).ResolveLinkTarget(true)?.FullName;
        Assert.Equal(
            Path.GetFullPath(AppDirectories.AdventureDirectory(bundle.Metadata.Id)),
            Path.GetFullPath(resolved!));
    }

    [Fact]
    public void SyncLink_renames_symlink_when_title_changes()
    {
        var bundle = AdventureStore.CreateNew("Original Title");
        AdventureIndexDirectoryService.SyncLink(bundle.Metadata.Id, bundle.Metadata.Title);
        Assert.NotNull(AdventureIndexDirectoryService.FindLinkPathForAdventure(bundle.Metadata.Id));

        AdventureIndexDirectoryService.SyncLink(bundle.Metadata.Id, "Renamed Title");

        var linkPath = AdventureIndexDirectoryService.FindLinkPathForAdventure(bundle.Metadata.Id);
        Assert.NotNull(linkPath);
        Assert.Equal("Renamed Title", StripShortcutExtension(Path.GetFileName(linkPath!)));
        Assert.False(Directory.Exists(Path.Combine(AppDirectories.AdventuresIndexDirectory, "Original Title")));
        Assert.False(File.Exists(Path.Combine(AppDirectories.AdventuresIndexDirectory, "Original Title.lnk")));
    }

    [Fact]
    public void ListIndex_skips_reserved_index_directory()
    {
        AdventureStore.CreateNew("Visible Adventure");
        Assert.True(Directory.Exists(AppDirectories.AdventuresIndexDirectory));

        File.WriteAllText(
            Path.Combine(AppDirectories.AdventuresIndexDirectory, "adventure.json"),
            """
            {
              "id": "00000000-0000-0000-0000-000000000099",
              "title": "Index decoy",
              "createdAt": "2026-01-01T00:00:00Z",
              "lastPlayedAt": "2026-01-01T00:00:00Z",
              "status": 0,
              "archived": false,
              "settings": {}
            }
            """);

        var titles = AdventureStore.ListIndex().Select(a => a.Title).ToList();
        Assert.Contains("Visible Adventure", titles);
        Assert.DoesNotContain("Index decoy", titles);
    }

    [Fact]
    public void RemoveLink_deletes_symlink_without_deleting_target()
    {
        var bundle = AdventureStore.CreateNew("Delete Link Only");
        var target = AppDirectories.AdventureDirectory(bundle.Metadata.Id);
        AdventureIndexDirectoryService.SyncLink(bundle.Metadata.Id, bundle.Metadata.Title);
        Assert.NotNull(AdventureIndexDirectoryService.FindLinkPathForAdventure(bundle.Metadata.Id));

        AdventureIndexDirectoryService.RemoveLink(bundle.Metadata.Id);

        Assert.Null(AdventureIndexDirectoryService.FindLinkPathForAdventure(bundle.Metadata.Id));
        Assert.True(Directory.Exists(target));
    }

    private static string StripShortcutExtension(string fileName) =>
        fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName;
}
