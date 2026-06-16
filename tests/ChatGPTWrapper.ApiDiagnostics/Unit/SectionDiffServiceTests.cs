using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class SectionDiffServiceTests
{
    [Fact]
    public void GetChangedSections_detects_body_change_after_publish()
    {
        var entry = new SourceManifestEntry
        {
            RelativePath = "cast.md",
            LocalSha256 = "abc",
            ManuallyPublishedAt = DateTimeOffset.UtcNow,
            ManuallyPublishedSha256 = "abc",
            Sections =
            [
                new SectionManifestEntry
                {
                    Id = "npcs/mara",
                    Title = "Mara",
                    BodyCache = "Updated body",
                },
            ],
            PublishedSectionHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["npcs/mara"] = ProjectSourceExportService.ComputeSha256Bytes(
                    System.Text.Encoding.UTF8.GetBytes("Old body")),
            },
        };

        var hints = SectionDiffService.GetChangedSectionsSincePublish(entry);
        Assert.Single(hints);
        Assert.Equal("Mara", hints[0].Title);
    }
}
