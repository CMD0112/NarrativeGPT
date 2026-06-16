using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ProjectSourceProbeService
{
    public static string ProbeMetaPath(Guid adventureId) =>
        Path.Combine(SourceFileHistoryService.ProjectMirrorDirectory(adventureId), "probe-meta.json");

    public static async Task ProbeAllAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId))
            throw new InvalidOperationException("No linked ChatGPT project.");

        SourceManifestHelper.MigrateManifest(bundle.SourceManifest);
        ProjectSourceExportService.ExportIfStale(bundle);

        progress?.Report("Fetching project file list…");
        var remoteFiles = await api.GetProjectFilesDirectAsync(core, gizmoId, cancellationToken);
        ProjectRemoteListCache.Set(gizmoId, remoteFiles);

        var meta = new ProbeMetaDocument { ProbedAt = DateTimeOffset.UtcNow };
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Probing {entry.RelativePath}…");
            var fileMeta = await ProbeEntryAsync(
                core, bundle, api, entry, remoteFiles, cancellationToken);
            meta.Files.Add(fileMeta);
        }

        SaveProbeMeta(bundle.Metadata.Id, meta);
        AdventureStore.Save(bundle);
    }

    public static async Task<ProbeMetaFileEntry> ProbeFileAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = bundle.Metadata.LinkedProjectId
                      ?? throw new InvalidOperationException("No linked ChatGPT project.");

        SourceManifestHelper.MigrateManifest(bundle.SourceManifest);
        var entry = bundle.SourceManifest.Entries
                        .FirstOrDefault(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Unknown source file: {relativePath}");

        var remoteFiles = await api.GetProjectFilesDirectAsync(core, gizmoId, cancellationToken);
        var fileMeta = await ProbeEntryAsync(core, bundle, api, entry, remoteFiles, cancellationToken);

        var meta = LoadProbeMeta(bundle.Metadata.Id) ?? new ProbeMetaDocument();
        meta.ProbedAt = DateTimeOffset.UtcNow;
        meta.Files.RemoveAll(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        meta.Files.Add(fileMeta);
        SaveProbeMeta(bundle.Metadata.Id, meta);
        AdventureStore.Save(bundle);
        return fileMeta;
    }

    public static bool HasMirrorFile(Guid adventureId, string relativePath)
    {
        var path = Path.Combine(SourceFileHistoryService.ProjectMirrorDirectory(adventureId), relativePath);
        return File.Exists(path);
    }

    public static string MirrorFilePath(Guid adventureId, string relativePath) =>
        Path.Combine(SourceFileHistoryService.ProjectMirrorDirectory(adventureId), relativePath);

    public static RemoteProbeMatch ClassifyMatch(string? localHash, string? remoteHash, bool hasRemote)
    {
        if (!hasRemote)
            return RemoteProbeMatch.MissingOnProject;

        if (string.IsNullOrWhiteSpace(remoteHash))
            return RemoteProbeMatch.NotDownloadable;

        if (string.IsNullOrWhiteSpace(localHash))
            return RemoteProbeMatch.Differ;

        return string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase)
            ? RemoteProbeMatch.Match
            : RemoteProbeMatch.Differ;
    }

    public static string FormatProbeMatch(RemoteProbeMatch match) => match switch
    {
        RemoteProbeMatch.Match => "Match",
        RemoteProbeMatch.Differ => "Differ",
        RemoteProbeMatch.MissingOnProject => "Missing",
        RemoteProbeMatch.NotDownloadable => "Not downloadable",
        _ => "Unknown",
    };

    private static async Task<ProbeMetaFileEntry> ProbeEntryAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        SourceManifestEntry entry,
        IReadOnlyList<GizmoFileRef> remoteFiles,
        CancellationToken cancellationToken)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var localPath = Path.Combine(sourcesDir, entry.RelativePath);
        if (File.Exists(localPath))
        {
            entry.LocalSha256 = ProjectSourceExportService.ComputeSha256(localPath);
            entry.Sha256 = entry.LocalSha256;
        }

        var remote = ProjectFileSyncPlanner.TryMatchRemoteFile(entry, remoteFiles);
        var mirrorDir = SourceFileHistoryService.ProjectMirrorDirectory(bundle.Metadata.Id);
        Directory.CreateDirectory(mirrorDir);
        var mirrorPath = Path.Combine(mirrorDir, entry.RelativePath);

        string? remoteHash = null;
        if (remote is null)
        {
            entry.RemoteProbeMatch = RemoteProbeMatch.MissingOnProject;
        }
        else
        {
            entry.RemoteProbeFileId = remote.FileId;
            try
            {
                var failFast = string.Equals(remote.Location, "fs", StringComparison.OrdinalIgnoreCase);
                await api.DownloadFileToPathAsync(
                    core,
                    remote.FileId,
                    mirrorPath,
                    cancellationToken,
                    bundle.Metadata.LinkedProjectId,
                    remote.Location,
                    failFast);
                remoteHash = ProjectSourceExportService.ComputeSha256(mirrorPath);
            }
            catch (ChatGptApiException ex) when (ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex))
            {
                remoteHash = null;
                entry.RemoteProbeMatch = RemoteProbeMatch.NotDownloadable;
            }
            catch
            {
                remoteHash = null;
                entry.RemoteProbeMatch = RemoteProbeMatch.NotDownloadable;
            }
        }

        if (remote is not null && entry.RemoteProbeMatch != RemoteProbeMatch.NotDownloadable)
        {
            entry.LastRemoteProbedAt = DateTimeOffset.UtcNow;
            entry.LastRemoteProbeSha256 = remoteHash;
            entry.RemoteProbeMatch = ClassifyMatch(entry.EffectiveLocalSha256, remoteHash, hasRemote: true);
        }
        else if (remote is null)
        {
            entry.LastRemoteProbedAt = DateTimeOffset.UtcNow;
            entry.LastRemoteProbeSha256 = null;
        }

        return new ProbeMetaFileEntry
        {
            RelativePath = entry.RelativePath,
            FileId = remote?.FileId,
            Sha256 = remoteHash,
            Match = entry.RemoteProbeMatch,
        };
    }

    private static ProbeMetaDocument? LoadProbeMeta(Guid adventureId)
    {
        var path = ProbeMetaPath(adventureId);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ProbeMetaDocument>(File.ReadAllText(path), AdventureJson.Options);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveProbeMeta(Guid adventureId, ProbeMetaDocument meta)
    {
        var path = ProbeMetaPath(adventureId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(meta, AdventureJson.Options));
    }
}
