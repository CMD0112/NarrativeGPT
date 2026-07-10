using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ReferenceSourceExportTests
{
    [Fact]
    public void ExportReferenceFiles_writes_all_registered_reference_files()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        try
        {
            var dir = ProjectSourceExportService.SourcesDirectory(bundle);
            foreach (var fileName in SectionSchema.ReferenceSourceFiles)
            {
                var path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                    File.Delete(path);
            }

            bundle.SourceManifest.Entries.RemoveAll(e =>
                SectionSchema.IsReferenceSourceFile(e.RelativePath));

            var changed = ProjectSourceExportService.ExportReferenceFiles(bundle, SourceExportMode.Force);
            Assert.True(changed);

            foreach (var fileName in SectionSchema.ReferenceSourceFiles)
            {
                var path = Path.Combine(dir, fileName);
                Assert.True(File.Exists(path), $"Expected {fileName} on disk");
                Assert.Contains(bundle.SourceManifest.Entries, e =>
                    string.Equals(e.RelativePath, fileName, StringComparison.OrdinalIgnoreCase));
            }

            Assert.Contains("## Quick rules", File.ReadAllText(Path.Combine(dir, SectionSchema.CanonFormatFile)));
            Assert.Contains("# Narrator scales reference", File.ReadAllText(Path.Combine(dir, SectionSchema.NarratorScalesFile)));
            Assert.Contains("# Entity state format reference", File.ReadAllText(Path.Combine(dir, SectionSchema.EntityStateFormatFile)));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void ExportReferenceFiles_preserves_existing_lore_manifest_entries()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 2);
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var loreEntry = bundle.SourceManifest.Entries
                .First(e => e.RelativePath == SectionSchema.CastFile);

            ProjectSourceExportService.ExportReferenceFiles(bundle, SourceExportMode.Force);

            Assert.Contains(bundle.SourceManifest.Entries, e => e.RelativePath == loreEntry.RelativePath);
            Assert.Equal(loreEntry.LocalSha256, bundle.SourceManifest.Entries
                .First(e => e.RelativePath == loreEntry.RelativePath).LocalSha256);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void BuildPipelineChecklist_marks_reference_rows_present_after_export()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        try
        {
            ProjectSourceExportService.ExportReferenceFiles(bundle, SourceExportMode.Force);
            var rows = AdventureDesignSourcePromptService.BuildPipelineChecklist(bundle);

            foreach (var def in AdventureDesignSourcePromptService.ReferenceDefinitions)
            {
                var row = Assert.Single(rows, r =>
                    string.Equals(r.RelativePath, def.RelativePath, StringComparison.OrdinalIgnoreCase));
                Assert.True(row.IsReferenceFile);
                Assert.True(row.PresentOnDisk, def.RelativePath);
            }
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void EntityInternalStateFormatGenerator_includes_play_tracked_kinds()
    {
        var content = EntityInternalStateFormatGenerator.Generate();
        Assert.Contains("### `player`", content, StringComparison.Ordinal);
        Assert.Contains("### `npc`", content, StringComparison.Ordinal);
        Assert.Contains("emotional.mood", content, StringComparison.Ordinal);
    }
}
