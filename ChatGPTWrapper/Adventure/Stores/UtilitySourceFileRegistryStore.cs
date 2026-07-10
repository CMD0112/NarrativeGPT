using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Adventure.Stores;

internal static class UtilitySourceFileRegistryStore
{
    public const int SchemaVersion = 1;

    public static TimeSpan DefaultTtlFallback { get; } = TimeSpan.FromDays(7);

    public static string RegistryPath(Guid adventureId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "utility-source-io-registry.json");

    public static UtilitySourceFileRegistryDocument Load(Guid adventureId)
    {
        var path = RegistryPath(adventureId);
        if (!File.Exists(path))
            return new UtilitySourceFileRegistryDocument();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UtilitySourceFileRegistryDocument>(json, AdventureJson.Options)
                   ?? new UtilitySourceFileRegistryDocument();
        }
        catch
        {
            return new UtilitySourceFileRegistryDocument();
        }
    }

    public static void Save(Guid adventureId, UtilitySourceFileRegistryDocument document)
    {
        document.SchemaVersion = SchemaVersion;
        var path = RegistryPath(adventureId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(document, AdventureJson.Options);
        File.WriteAllText(path, json);
    }

    public static void Register(
        Guid adventureId,
        UtilitySourceFileRegistryEntry entry)
    {
        var document = Load(adventureId);
        document.Entries.RemoveAll(e => e.RunId == entry.RunId && string.Equals(e.RemotePath, entry.RemotePath, StringComparison.OrdinalIgnoreCase));
        document.Entries.Add(entry);
        Save(adventureId, document);
    }

    public static IReadOnlyList<UtilitySourceFileRegistryEntry> ListActive(Guid adventureId) =>
        Load(adventureId).Entries.Where(e => e.DeletedAt is null).ToList();

    public static IReadOnlyList<UtilitySourceFileRegistryEntry> ListForRun(Guid adventureId, Guid runId) =>
        Load(adventureId).Entries
            .Where(e => e.RunId == runId && e.DeletedAt is null)
            .ToList();

    public static UtilitySourceFileRegistryEntry? TryFindVerified(
        Guid adventureId,
        Guid runId,
        string remotePath,
        string? contentSha256 = null)
    {
        var normalized = UtilitySourceFileNaming.NormalizeSourcesPath(remotePath);
        return Load(adventureId).Entries.FirstOrDefault(e =>
            e.RunId == runId
            && e.DeletedAt is null
            && !string.IsNullOrWhiteSpace(e.FileId)
            && string.Equals(
                UtilitySourceFileNaming.NormalizeSourcesPath(e.RemotePath),
                normalized,
                StringComparison.OrdinalIgnoreCase)
            && (contentSha256 is null
                || string.Equals(e.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool HasVerifiedEntries(
        Guid adventureId,
        Guid runId,
        IReadOnlyList<string> remotePaths)
    {
        if (remotePaths.Count == 0)
            return true;

        foreach (var remotePath in remotePaths)
        {
            if (TryFindVerified(adventureId, runId, remotePath) is null)
                return false;
        }

        return true;
    }

    public static void MarkDeleted(Guid adventureId, Guid runId, string remotePath, string? error = null)
    {
        var document = Load(adventureId);
        var changed = false;
        foreach (var entry in document.Entries)
        {
            if (entry.RunId != runId
                || !string.Equals(entry.RemotePath, remotePath, StringComparison.OrdinalIgnoreCase)
                || entry.DeletedAt is not null)
            {
                continue;
            }

            entry.DeletedAt = DateTimeOffset.UtcNow;
            entry.DeleteError = error;
            changed = true;
        }

        if (changed)
            Save(adventureId, document);
    }

    public static void PruneDeleted(Guid adventureId, TimeSpan retainDeleted)
    {
        var document = Load(adventureId);
        var cutoff = DateTimeOffset.UtcNow - retainDeleted;
        var before = document.Entries.Count;
        document.Entries.RemoveAll(e =>
            e.DeletedAt is { } deleted && deleted < cutoff);
        if (document.Entries.Count != before)
            Save(adventureId, document);
    }
}
