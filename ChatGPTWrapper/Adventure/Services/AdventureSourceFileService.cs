using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.NarratorScales;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class DesignSourceFileExtract
{
    public required string RelativePath { get; init; }

    public required string Content { get; init; }
}

internal sealed class AdventureSourceFileStatus
{
    public required string RelativePath { get; init; }

    public required string Label { get; init; }

    public bool Present { get; init; }
}

internal sealed class AdventureSourceImportResult
{
    public int Imported { get; init; }

    public int Skipped { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = [];
}

internal static class AdventureSourceFileService
{
    private static readonly Regex BeginEndBlockRegex = new(
        @"---\s*begin\s+(.+?)\s*---\s*\r?\n([\s\S]*?)\r?\n---\s*end\s+\1\s*---",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Fallback when assistant replies were truncated before the closing <c>--- end … ---</c> line.</summary>
    private static readonly Regex TruncatedBeginBlockRegex = new(
        @"---\s*begin\s+(.+?)\s*---\s*\r?\n([\s\S]*?)(?=\r?\n---\s*begin\s+|\z)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string SourcesDirectory(AdventureBundle bundle) =>
        AppDirectories.AdventureSourcesDirectory(bundle.Metadata.Id);

    public static void EnsureLayout(AdventureBundle bundle)
    {
        Directory.CreateDirectory(bundle.DirectoryPath);
        Directory.CreateDirectory(SourcesDirectory(bundle));

        EnsureCanonFormatFile(bundle);
        EnsureNarratorScalesReference(bundle);
    }

    /// <summary>
    /// Ensures the static <see cref="SectionSchema.NarratorScalesFile"/> catalog exists on disk.
    /// Adventure-specific active values are injected per send, not written into this file.
    /// </summary>
    public static void EnsureNarratorScalesReference(AdventureBundle bundle) =>
        EnsureNarratorScalesFile(bundle);

    private static void EnsureCanonFormatFile(AdventureBundle bundle)
    {
        var canonFormatPath = ResolveAbsolutePath(bundle, SectionSchema.CanonFormatFile);
        var generated = CanonFormatGenerator.Generate();
        if (!File.Exists(canonFormatPath))
        {
            File.WriteAllText(canonFormatPath, generated, Encoding.UTF8);
            return;
        }

        var existing = File.ReadAllText(canonFormatPath);
        var existingHash = ProjectSourceExportService.ComputeSha256Bytes(Encoding.UTF8.GetBytes(existing));
        var generatedHash = ProjectSourceExportService.ComputeSha256Bytes(Encoding.UTF8.GetBytes(generated));
        if (!string.Equals(existingHash, generatedHash, StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(canonFormatPath, generated, Encoding.UTF8);
    }

    private static void EnsureNarratorScalesFile(AdventureBundle bundle)
    {
        var path = ResolveAbsolutePath(bundle, SectionSchema.NarratorScalesFile);
        var generated = NarratorScalesGenerator.Generate();
        if (!File.Exists(path))
        {
            WriteNarratorScales(bundle, path, generated);
            return;
        }

        var existing = File.ReadAllText(path);
        var existingHash = ProjectSourceExportService.ComputeSha256Bytes(Encoding.UTF8.GetBytes(existing));
        var generatedHash = ProjectSourceExportService.ComputeSha256Bytes(Encoding.UTF8.GetBytes(generated));
        if (!string.Equals(existingHash, generatedHash, StringComparison.OrdinalIgnoreCase))
            WriteNarratorScales(bundle, path, generated);
    }

    private static void WriteNarratorScales(AdventureBundle bundle, string absolutePath, string content)
    {
        File.WriteAllText(absolutePath, content, Encoding.UTF8);
        NarratorScalesManifestService.RefreshManifestSections(bundle, content);
        var entry = FindOrCreateManifestEntry(bundle, SectionSchema.NarratorScalesFile);
        var hash = ProjectSourceExportService.ComputeSha256Bytes(Encoding.UTF8.GetBytes(content));
        UpdateManifestEntryAfterWrite(entry, hash);
        bundle.SourceManifest.RefreshSyncedFlag();
    }

    public static string ResolveAbsolutePath(AdventureBundle bundle, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return Path.Combine(SourcesDirectory(bundle), normalized);
    }

    public static bool TryWrite(
        AdventureBundle bundle,
        string relativePath,
        string content,
        string reason = "write")
    {
        if (string.IsNullOrWhiteSpace(relativePath) || content is null)
            return false;

        EnsureLayout(bundle);

        var normalized = NormalizeRelativePath(relativePath);
        var sourcesDir = SourcesDirectory(bundle);
        var absolutePath = Path.Combine(sourcesDir, normalized);
        var normalizedContent = SourceMarkdownNormalizer.Normalize(normalized, content.Trim()) + Environment.NewLine;
        var contentHash = ProjectSourceExportService.ComputeSha256Bytes(Encoding.UTF8.GetBytes(normalizedContent));

        var entry = FindOrCreateManifestEntry(bundle, normalized);
        var needsWrite = !File.Exists(absolutePath)
                         || !string.Equals(entry.EffectiveLocalSha256, contentHash, StringComparison.OrdinalIgnoreCase);

        if (needsWrite && File.Exists(absolutePath))
            SourceFileHistoryService.ArchiveBeforeOverwrite(bundle.Metadata.Id, sourcesDir, normalized, reason);

        if (needsWrite)
        {
            var parent = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(absolutePath, normalizedContent, Encoding.UTF8);
        }

        UpdateManifestEntryAfterWrite(entry, contentHash);
        RefreshManifestSectionsIfNeeded(bundle, normalized, normalizedContent, absolutePath, needsWrite, entry);
        bundle.SourceManifest.RefreshSyncedFlag();
        return true;
    }

    public static string? TryRead(AdventureBundle bundle, string relativePath)
    {
        var absolutePath = ResolveAbsolutePath(bundle, relativePath);
        return File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : null;
    }

    public static bool TryDelete(AdventureBundle bundle, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var absolutePath = ResolveAbsolutePath(bundle, normalized);
        if (File.Exists(absolutePath))
            File.Delete(absolutePath);

        var removed = bundle.SourceManifest.Entries.RemoveAll(e =>
            string.Equals(e.RelativePath, normalized, StringComparison.OrdinalIgnoreCase));
        bundle.SourceManifest.RefreshSyncedFlag();
        return removed > 0 || !File.Exists(absolutePath);
    }

    public static bool HasLocalLoreSourceFiles(AdventureBundle bundle)
    {
        EnsureLayout(bundle);
        foreach (var relativePath in ProjectSourceImportService.ImportableLoreFileNames)
        {
            if (File.Exists(ResolveAbsolutePath(bundle, relativePath)))
                return true;
        }

        return false;
    }

    public static IReadOnlyList<string> ListRelativePaths(AdventureBundle bundle)
    {
        var sourcesDir = SourcesDirectory(bundle);
        if (!Directory.Exists(sourcesDir))
            return [];

        return Directory.EnumerateFiles(sourcesDir, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Aligns manifest entries with on-disk <c>sources/*</c> files and re-parses sectioned lore when
    /// content changed or sections are missing.
    /// </summary>
    /// <returns>True when manifest entries, sections, or imported bundle fields were updated.</returns>
    public static bool ReconcileManifest(AdventureBundle bundle)
    {
        EnsureLayout(bundle);
        var sourcesDir = SourcesDirectory(bundle);
        var onDisk = ListRelativePaths(bundle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = bundle.SourceManifest.Entries
            .Select(e => e.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var relativePath in onDisk)
        {
            if (known.Contains(relativePath))
                continue;

            var absolutePath = Path.Combine(sourcesDir, relativePath);
            var entry = new SourceManifestEntry
            {
                RelativePath = relativePath,
                LocalSha256 = ProjectSourceExportService.ComputeSha256(absolutePath),
                SyncState = SourceSyncState.LocalOnly,
            };
            entry.Sha256 = entry.LocalSha256;
            bundle.SourceManifest.Entries.Add(entry);
            changed = true;
        }

        foreach (var entry in bundle.SourceManifest.Entries)
        {
            var absolutePath = Path.Combine(sourcesDir, entry.RelativePath);
            if (!File.Exists(absolutePath))
                continue;

            var hash = ProjectSourceExportService.ComputeManifestLocalSha256(entry.RelativePath, absolutePath);
            var hashChanged = !string.Equals(entry.EffectiveLocalSha256, hash, StringComparison.OrdinalIgnoreCase);
            if (hashChanged)
            {
                entry.LocalSha256 = hash;
                entry.Sha256 = hash;
                entry.RemoteProbeMatch = RemoteProbeMatch.Unknown;
                if (entry.SyncState == SourceSyncState.InSync)
                    entry.SyncState = SourceSyncState.LocalNewer;
                changed = true;
            }

            if (NarratorScalesManifestService.IsNarratorScalesFile(entry.RelativePath))
            {
                if (!hashChanged && entry.Sections.Count > 0)
                    continue;

                var scalesMarkdown = File.ReadAllText(absolutePath);
                if (string.IsNullOrWhiteSpace(scalesMarkdown))
                    continue;

                NarratorScalesManifestService.RefreshManifestSections(bundle, scalesMarkdown);
                changed = true;
                continue;
            }

            if (!ProjectSourceImportService.IsSectionedLoreFile(entry.RelativePath))
                continue;

            if (!hashChanged && entry.Sections.Count > 0)
                continue;

            var markdown = File.ReadAllText(absolutePath);
            if (string.IsNullOrWhiteSpace(markdown))
                continue;

            ProjectSourceImportService.RefreshManifestSectionsFromMarkdown(
                bundle,
                entry.RelativePath,
                markdown,
                importStructuredCanon: false);
            changed = true;
        }

        bundle.SourceManifest.Entries = bundle.SourceManifest.Entries
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        bundle.SourceManifest.RefreshSyncedFlag();
        return changed;
    }

    public static IReadOnlyList<AdventureSourceFileStatus> GetPipelineStatuses(AdventureBundle bundle)
    {
        EnsureLayout(bundle);
        ReconcileManifest(bundle);

        return AdventureDesignSourcePromptService.PromptPipelineOrder
            .Concat(SectionSchema.ReferenceSourceFiles)
            .Select(path =>
            {
                AdventureDesignSourcePromptService.TryGetDefinition(path, out var def);
                var absolutePath = ResolveAbsolutePath(bundle, path);
                return new AdventureSourceFileStatus
                {
                    RelativePath = path,
                    Label = def.ButtonLabel ?? path,
                    Present = File.Exists(absolutePath),
                };
            })
            .ToList();
    }

    public static string? TryResolveCanonicalPath(AdventureBundle bundle, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = Path.GetFileName(fileName.Replace('\\', '/').Trim());
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (AdventureDesignSourcePromptService.TryGetDefinition(name, out _))
            return name;

        if (SectionSchema.IsReferenceSourceFile(name))
            return name;

        if (MapBlockNameToCanonical(bundle, name, expectedPaths: null) is { } mapped)
            return mapped;

        var lower = name.ToLowerInvariant();
        if (lower.EndsWith(".md", StringComparison.Ordinal))
        {
            foreach (var definition in AdventureDesignSourcePromptService.AllDefinitions)
            {
                var prefixed = AdventureDesignSourcePromptService.BuildPrefixedFileName(
                    bundle.Metadata.Title,
                    definition.RelativePath);
                if (string.Equals(prefixed, name, StringComparison.OrdinalIgnoreCase))
                    return definition.RelativePath;
            }
        }

        return null;
    }

    public static AdventureSourceImportResult TryImportFromAbsolutePaths(
        AdventureBundle bundle,
        IEnumerable<string> absolutePaths,
        string reason = "import")
    {
        var imported = 0;
        var skipped = 0;
        var messages = new List<string>();

        foreach (var absolutePath in absolutePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                skipped++;
                messages.Add($"Skipped missing file: {absolutePath}");
                continue;
            }

            var fileName = Path.GetFileName(absolutePath);
            var canonical = TryResolveCanonicalPath(bundle, fileName);
            if (canonical is null)
            {
                skipped++;
                messages.Add($"Unrecognized source file name: {fileName}");
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(absolutePath);
            }
            catch (Exception ex)
            {
                skipped++;
                messages.Add($"Could not read {fileName}: {ex.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                skipped++;
                messages.Add($"Empty file skipped: {fileName}");
                continue;
            }

            if (TryWrite(bundle, canonical, content, reason))
            {
                imported++;
                messages.Add($"{fileName} → {canonical}");
                if (string.Equals(canonical, "instructions-snippet.md", StringComparison.OrdinalIgnoreCase))
                    InstructionContractService.TryApplyFromInstructionsBody(bundle, content);
            }
            else
            {
                skipped++;
                messages.Add($"Failed to write: {canonical}");
            }
        }

        return new AdventureSourceImportResult
        {
            Imported = imported,
            Skipped = skipped,
            Messages = messages,
        };
    }

    public static AdventureSourceImportResult TryImportRecentChatDownloads(
        AdventureBundle bundle,
        TimeSpan maxAge)
    {
        var downloadsDir = ChatGptWebViewFileDiagnostics.DownloadsDirectory;
        if (!Directory.Exists(downloadsDir))
        {
            return new AdventureSourceImportResult
            {
                Skipped = 0,
                Messages = ["No chat downloads folder yet — download a file from the design thread first."],
            };
        }

        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var candidates = Directory.EnumerateFiles(downloadsDir, "*.md", SearchOption.TopDirectoryOnly)
            .Where(path => File.GetLastWriteTimeUtc(path) >= cutoff.UtcDateTime)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            return new AdventureSourceImportResult
            {
                Messages = ["No recent .md downloads in chat-downloads."],
            };
        }

        return TryImportFromAbsolutePaths(bundle, candidates, "chat-download");
    }

    public static IReadOnlyList<DesignSourceFileExtract> ExtractFromDesignReply(
        AdventureBundle bundle,
        string assistantText,
        IReadOnlyList<string>? expectedPaths = null)
    {
        if (string.IsNullOrWhiteSpace(assistantText))
            return [];

        var extracts = new List<DesignSourceFileExtract>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in BeginEndBlockRegex.Matches(assistantText))
        {
            if (!TryAddExtract(bundle, extracts, seen, match.Groups[1].Value, match.Groups[2].Value, expectedPaths))
                continue;
        }

        foreach (Match match in TruncatedBeginBlockRegex.Matches(assistantText))
        {
            if (!TryAddExtract(bundle, extracts, seen, match.Groups[1].Value, match.Groups[2].Value, expectedPaths))
                continue;
        }

        if (extracts.Count == 0 && expectedPaths is { Count: 1 })
        {
            var fallback = ExtractSingleFileFallback(assistantText);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                extracts.Add(new DesignSourceFileExtract
                {
                    RelativePath = expectedPaths[0],
                    Content = fallback,
                });
            }
        }

        return extracts;
    }

    private static bool TryAddExtract(
        AdventureBundle bundle,
        List<DesignSourceFileExtract> extracts,
        HashSet<string> seen,
        string blockName,
        string rawContent,
        IReadOnlyList<string>? expectedPaths)
    {
        var content = rawContent.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var relativePath = MapBlockNameToCanonical(bundle, blockName.Trim(), expectedPaths);
        if (relativePath is null || !seen.Add(relativePath))
            return false;

        extracts.Add(new DesignSourceFileExtract
        {
            RelativePath = relativePath,
            Content = content,
        });
        return true;
    }

    public static int TrySaveFromDesignReply(
        AdventureBundle bundle,
        string assistantText,
        IReadOnlyList<string>? expectedPaths = null,
        string reason = "design-reply")
    {
        var extracts = ExtractFromDesignReply(bundle, assistantText, expectedPaths);
        var saved = 0;
        foreach (var extract in extracts)
        {
            if (TryWrite(bundle, extract.RelativePath, extract.Content, reason))
            {
                if (string.Equals(extract.RelativePath, "instructions-snippet.md", StringComparison.OrdinalIgnoreCase))
                    InstructionContractService.TryApplyFromInstructionsBody(bundle, extract.Content);
                saved++;
            }
        }

        return saved;
    }

    /// <summary>
    /// When lore files are missing but the design workspace captured assistant replies
    /// with inline <c>--- begin … ---</c> blocks, materialize those into <c>sources/</c>.
    /// </summary>
    public static int TryBootstrapLocalSourcesFromDesignWorkspace(AdventureBundle bundle)
    {
        var expected = AdventureDesignSourcePromptService.PromptPipelineOrder.ToList();
        var missing = expected
            .Where(path => !File.Exists(ResolveAbsolutePath(bundle, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (missing.Count == 0)
            return 0;

        var saved = 0;

        foreach (var step in bundle.DesignWorkspace.Steps.Values)
        {
            foreach (var message in step.ChatMessages)
            {
                if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(message.Text))
                {
                    continue;
                }

                foreach (var extract in ExtractFromDesignReply(bundle, message.Text, expected))
                {
                    if (!missing.Contains(extract.RelativePath))
                        continue;

                    if (!TryWrite(bundle, extract.RelativePath, extract.Content, "design-workspace-bootstrap"))
                        continue;

                    if (string.Equals(extract.RelativePath, "instructions-snippet.md", StringComparison.OrdinalIgnoreCase))
                        InstructionContractService.TryApplyFromInstructionsBody(bundle, extract.Content);

                    missing.Remove(extract.RelativePath);
                    saved++;
                }
            }
        }

        if (saved > 0)
        {
            bundle.DesignWorkspace.PendingBootstrapNotice =
                $"Recovered {saved} source file(s) from design workspace history. "
                + "Use Pull from design thread if any files are incomplete.";
            ProjectLinkDiagnostics.Log(
                $"Design workspace bootstrap: adventure={bundle.Metadata.Id} saved={saved} files");
        }

        return saved;
    }

    private static SourceManifestEntry FindOrCreateManifestEntry(AdventureBundle bundle, string relativePath)
    {
        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (entry is not null)
            return entry;

        entry = new SourceManifestEntry
        {
            RelativePath = relativePath,
            SyncState = SourceSyncState.LocalOnly,
        };
        bundle.SourceManifest.Entries.Add(entry);
        return entry;
    }

    private static void RefreshManifestSectionsIfNeeded(
        AdventureBundle bundle,
        string relativePath,
        string normalizedContent,
        string absolutePath,
        bool needsWrite,
        SourceManifestEntry entry)
    {
        if (NarratorScalesManifestService.IsNarratorScalesFile(relativePath))
        {
            if (!needsWrite && entry.Sections.Count > 0)
                return;

            var scalesMarkdown = needsWrite ? normalizedContent : File.ReadAllText(absolutePath);
            NarratorScalesManifestService.RefreshManifestSections(bundle, scalesMarkdown);
            return;
        }

        if (!ProjectSourceImportService.IsSectionedLoreFile(relativePath))
            return;

        if (!needsWrite && entry.Sections.Count > 0)
            return;

        var markdown = needsWrite ? normalizedContent : File.ReadAllText(absolutePath);
        ProjectSourceImportService.RefreshManifestSectionsFromMarkdown(
            bundle,
            relativePath,
            markdown,
            importStructuredCanon: needsWrite);
    }

    private static void UpdateManifestEntryAfterWrite(
        SourceManifestEntry entry,
        string contentHash)
    {
        var priorHash = entry.EffectiveLocalSha256;
        var isFirstWrite = string.IsNullOrEmpty(priorHash);
        var hashChanged = !isFirstWrite
                          && !string.Equals(priorHash, contentHash, StringComparison.OrdinalIgnoreCase);

        entry.LocalSha256 = contentHash;
        entry.Sha256 = contentHash;

        if (isFirstWrite)
        {
            entry.SyncState = SourceSyncState.LocalOnly;
            return;
        }

        if (!hashChanged)
            return;

        entry.RemoteProbeMatch = RemoteProbeMatch.Unknown;
        entry.SyncState = entry.SyncState switch
        {
            SourceSyncState.InSync => SourceSyncState.LocalNewer,
            SourceSyncState.RemoteNewer => SourceSyncState.Conflict,
            SourceSyncState.MissingRemote => SourceSyncState.LocalOnly,
            _ => SourceSyncState.LocalNewer,
        };
    }

    private static string? MapBlockNameToCanonical(
        AdventureBundle bundle,
        string blockName,
        IReadOnlyList<string>? expectedPaths)
    {
        if (AdventureDesignSourcePromptService.TryGetDefinition(blockName, out _))
            return blockName;

        foreach (var definition in AdventureDesignSourcePromptService.AllDefinitions)
        {
            var prefixed = AdventureDesignSourcePromptService.BuildPrefixedFileName(
                bundle.Metadata.Title,
                definition.RelativePath);
            if (string.Equals(prefixed, blockName, StringComparison.OrdinalIgnoreCase))
                return definition.RelativePath;
        }

        if (expectedPaths is not null)
        {
            foreach (var path in expectedPaths)
            {
                var prefixed = AdventureDesignSourcePromptService.BuildPrefixedFileName(bundle.Metadata.Title, path);
                if (string.Equals(prefixed, blockName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, blockName, StringComparison.OrdinalIgnoreCase))
                    return path;
            }
        }

        var separator = " - ";
        var splitIndex = blockName.LastIndexOf(separator, StringComparison.Ordinal);
        if (splitIndex >= 0)
        {
            var suffix = blockName[(splitIndex + separator.Length)..].Trim();
            if (AdventureDesignSourcePromptService.TryGetDefinition(suffix, out _))
                return suffix;
        }

        return null;
    }

    private static string? ExtractSingleFileFallback(string assistantText)
    {
        var fenced = Regex.Match(
            assistantText,
            @"```(?:markdown|md)?\s*\r?\n([\s\S]*?)\r?\n```",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (fenced.Success)
            return fenced.Groups[1].Value.Trim();

        var trimmed = assistantText.Trim();
        return trimmed.StartsWith('#') ? trimmed : null;
    }

    private static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace('\\', '/').Trim().TrimStart('/');
}
