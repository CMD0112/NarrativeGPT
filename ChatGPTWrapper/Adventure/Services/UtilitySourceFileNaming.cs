using System.IO;
using System.Text.RegularExpressions;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Canonical Project <c>sources/</c> paths for utility source-reference file I/O (CMD-441).
/// Pattern: <c>sources/cgw-utility-io/{adventureKey}/{jobKey}/{runKey}/in/{fileName}</c>
/// </summary>
public static class UtilitySourceFileNaming
{
    public const string RootPrefix = "sources/cgw-utility-io";

    public const string DiagnosticAdventureKey = "diag";

    public const string InputSegment = "in";

    private static readonly Regex CanonicalPathRegex = new(
        @"^sources/cgw-utility-io/(?<adventure>[^/]+)/(?<job>[^/]+)/(?<run>[^/]+)/in/(?<file>[^/]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string BuildAdventureKey(Guid adventureId) =>
        adventureId.ToString("N")[..8];

    public static string BuildRunKey(Guid runId) =>
        runId.ToString("N")[..12];

    public static string NormalizeJobKey(string jobId) =>
        jobId.Trim().Replace('_', '-');

    public static string BuildInputRemotePath(
        Guid adventureId,
        string jobId,
        Guid runId,
        string fileName) =>
        $"{RootPrefix}/{BuildAdventureKey(adventureId)}/{NormalizeJobKey(jobId)}/{BuildRunKey(runId)}/{InputSegment}/{SanitizeFileName(fileName)}";

    public static string BuildDiagnosticInputRemotePath(string jobId, string runToken, string fileName) =>
        $"{RootPrefix}/{DiagnosticAdventureKey}/{NormalizeJobKey(jobId)}/{runToken}/{InputSegment}/{SanitizeFileName(fileName)}";

    public static bool IsCanonicalPath(string? remotePath) =>
        !string.IsNullOrWhiteSpace(remotePath)
        && CanonicalPathRegex.IsMatch(NormalizeSourcesPath(remotePath));

    public static bool TryParse(string? remotePath, out UtilitySourceFilePathParts parts)
    {
        parts = default;
        if (string.IsNullOrWhiteSpace(remotePath))
            return false;

        var match = CanonicalPathRegex.Match(NormalizeSourcesPath(remotePath));
        if (!match.Success)
            return false;

        parts = new UtilitySourceFilePathParts(
            match.Groups["adventure"].Value,
            match.Groups["job"].Value,
            match.Groups["run"].Value,
            match.Groups["file"].Value);
        return true;
    }

    public static string NormalizeSourcesPath(string relativePath) =>
        relativePath.Replace('\\', '/').Trim().TrimStart('/');

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("fileName is required", nameof(fileName));

        return name;
    }
}

public readonly record struct UtilitySourceFilePathParts(
    string AdventureKey,
    string JobKey,
    string RunKey,
    string FileName);
