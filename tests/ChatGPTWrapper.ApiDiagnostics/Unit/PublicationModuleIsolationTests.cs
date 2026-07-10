using System.Reflection;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PublicationModuleIsolationTests
{
    [Fact]
    public void Publication_lane_types_do_not_reference_detail_upsert_bind()
    {
        var assembly = typeof(RegisterProjectFilesPublicationLane).Assembly;
        var publicationTypes = assembly.GetTypes()
            .Where(t => t.Namespace?.Contains("ProjectSource.Publication", StringComparison.Ordinal) == true)
            .ToList();

        foreach (var type in publicationTypes)
        {
            var source = type.FullName ?? type.Name;
            Assert.DoesNotContain(
                "BindSnorlaxSourceFileViaDetailUpsert",
                source,
                StringComparison.Ordinal);
        }

        Assert.Contains(publicationTypes, t => t == typeof(ProjectFilePublicationService));
    }
}
