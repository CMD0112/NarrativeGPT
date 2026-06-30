using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>Diagnostic against the user's real adventure folder when present locally.</summary>
[Collection(FileLockAwareCollectionNames.Name)]
[Trait("Category", "Unit")]
public sealed class KingInRedBootstrapDiagnosticTests : IClassFixture<FileLockAwareFixture>
{
    private static readonly Guid KingInRedId = Guid.Parse("b9233735-fdfa-47fe-8f2c-e7122d562f83");

    private static string? UserAdventureDir
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChatGPTWrapper",
                "adventures",
                KingInRedId.ToString("D"));
            return Directory.Exists(path) ? path : null;
        }
    }

    [Fact]
    public void Bootstrap_user_king_in_red_adventure_materializes_sources()
    {
        var src = UserAdventureDir;
        Assert.NotNull(src);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-KingBootstrap-" + Guid.NewGuid().ToString("N"));
        var dest = Path.Combine(tempRoot, "adventures", KingInRedId.ToString("D"));
        Directory.CreateDirectory(dest);

        foreach (var name in new[]
                 {
                     "adventure.json",
                     "design-workspace.json",
                     "entities.json",
                     "scenario.json",
                     "source-manifest.json",
                 })
        {
            var from = Path.Combine(src, name);
            if (File.Exists(from))
                File.Copy(from, Path.Combine(dest, name));
        }

        var priorOverride = AppDirectories.TestRootOverride;
        AppDirectories.TestRootOverride = tempRoot;
        AppDirectories.EnsureCreated();

        try
        {
            var bundle = AdventureStore.Load(KingInRedId);
            Assert.NotNull(bundle);

            ProjectSourceInjectionService.EnsureLoreSourcesMaterialized(bundle!);

            Assert.True(
                AdventureSourceFileService.HasLocalLoreSourceFiles(bundle!),
                "Load + materialize should produce lore sources for linked adventures");

            var loreEntries = bundle!.SourceManifest.Entries
                .Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath))
                .ToList();
            Assert.NotEmpty(loreEntries);

            var sourcesDir = AdventureSourceFileService.SourcesDirectory(bundle!);
            Assert.True(File.Exists(Path.Combine(sourcesDir, "cast.md")), "cast.md missing after materialize");
        }
        finally
        {
            AppDirectories.TestRootOverride = priorOverride;
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                /* best effort */
            }
        }
    }
}
