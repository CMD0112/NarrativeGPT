using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>
/// One-shot reconstruction for in-flight adventures missing ingest layers.
/// Set <c>CGW_RECONSTRUCT_ADVENTURE_DIR</c> to the adventure folder path before running.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class ThreadLogReconstructionDiagnosticTests
{
    [Fact]
    public void Reconstruct_adventure_when_env_dir_set()
    {
        var dir = Environment.GetEnvironmentVariable("CGW_RECONSTRUCT_ADVENTURE_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        dir = Path.GetFullPath(dir);
        Assert.True(Directory.Exists(dir), $"Adventure directory not found: {dir}");

        var adventureJson = Path.Combine(dir, "adventure.json");
        Assert.True(File.Exists(adventureJson), $"Missing adventure.json in {dir}");

        var meta = JsonSerializer.Deserialize<AdventureMetadata>(
            File.ReadAllText(adventureJson),
            AdventureJson.Options);
        Assert.NotNull(meta);

        AdventureLocationStore.Set(meta!.Id, dir);
        var bundle = AdventureStore.Load(meta.Id);
        Assert.NotNull(bundle);

        var result = ThreadLogReconstructionService.ReconstructAdventure(bundle!);
        Assert.True(result.Success, result.Error ?? "reconstruction failed");

        AdventureStore.Save(bundle!, AdventureSaveScope.PromptHistory);

        var reportPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "thread-log-reconstruction-report.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(
            reportPath,
            $"""
             Adventure: {bundle!.Metadata.Id:D}
             Directory: {dir}
             Threads reconstructed: {result.ThreadsReconstructed}
             Ingest events written: {result.IngestEventsWritten}
             Flight records linked: {result.FlightRecordsLinked}
             Completed: {DateTimeOffset.UtcNow:O}
             """);
    }
}
