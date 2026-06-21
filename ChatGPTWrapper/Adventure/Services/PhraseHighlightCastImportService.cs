using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class CastPhraseImportOptions
{
    public bool IncludePlayer { get; set; } = true;

    public bool IncludeParty { get; set; } = true;

    public bool IncludeAliases { get; set; } = true;

    /// <summary>Existing phrase highlight rules — colors are reserved and matching phrases are treated as already imported.</summary>
    public IReadOnlyList<PhraseHighlightRule>? ExistingRules { get; set; }

    /// <summary>Active wrapper theme; defaults to <see cref="ThemeRuntime.Current"/>.</summary>
    public ResolvedTheme? Theme { get; set; }

    /// <summary>Transcript canvas background for contrast checks; defaults to theme <c>BgBase</c>.</summary>
    public string? HighlightCanvasBackground { get; set; }

    /// <summary>Auto-color profile options; defaults to saved chrome settings.</summary>
    public HighlightColorAssignmentOptions? ColorAssignment { get; set; }
}

public sealed class CastPhraseImportCandidate : INotifyPropertyChanged
{
    public required string Phrase { get; init; }

    public string Role { get; init; } = "";

    public int AliasCount { get; init; }

    public string Color { get; init; } = "#FFD166";

    public bool AlreadyExists { get; init; }

    public bool IsSelectable => !AlreadyExists;

    private bool _isSelected = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class CastPhraseImportResult
{
    public IReadOnlyList<CastPhraseImportCandidate> Candidates { get; init; } = [];

    public HighlightColorCapacityAnalysis? ColorAnalysis { get; init; }

    public IReadOnlyList<PhraseHighlightRule> ToRules() =>
        Candidates
            .Where(c => c.IsSelected && !c.AlreadyExists && !string.IsNullOrWhiteSpace(c.Phrase))
            .Select(c => new PhraseHighlightRule { Phrase = c.Phrase.Trim(), Color = c.Color })
            .ToList();
}

internal static class PhraseHighlightCastImportService
{
    public static CastPhraseImportResult BuildCandidates(AdventureBundle? bundle, CastPhraseImportOptions? options = null)
    {
        options ??= new CastPhraseImportOptions();
        if (bundle?.Entities is not { } entities)
            return new CastPhraseImportResult();

        var theme = options.Theme ?? ThemeRuntime.Current;
        var colorOptions = options.ColorAssignment
            ?? HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var canvas = options.HighlightCanvasBackground
            ?? HighlightColorAssignmentEngine.ResolveCanvas(colorOptions, theme);
        var palette = HighlightColorAssignmentEngine.BuildPalette(colorOptions, theme, canvas);
        var existingRules = HighlightColorCapacityAnalyzer.IndexExistingRules(options.ExistingRules);
        var characterColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HighlightColorCapacityAnalyzer.SeedFromExistingRules(options.ExistingRules, usedColors, characterColors);
        var discoveryIndex = 0;

        var list = new List<CastPhraseImportCandidate>();

        void AddPhrase(string? phrase, string role, int aliasCount = 0)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return;

            var trimmed = phrase.Trim();
            var alreadyExists = existingRules.ContainsKey(trimmed);
            string color;

            if (alreadyExists)
            {
                color = existingRules[trimmed].Color?.Trim() ?? "#FFD166";
                if (string.IsNullOrWhiteSpace(color))
                    color = "#FFD166";
            }
            else
            {
                color = CastHighlightColorAssignment.AssignColor(
                    colorOptions,
                    role,
                    trimmed,
                    palette,
                    canvas,
                    characterColors,
                    usedColors,
                    discoveryIndex++,
                    theme);

                if (!role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
                    characterColors[trimmed] = color;
            }

            list.Add(new CastPhraseImportCandidate
            {
                Phrase = trimmed,
                Role = alreadyExists ? AppendAlreadyAdded(role) : role,
                AliasCount = aliasCount,
                Color = color,
                AlreadyExists = alreadyExists,
                IsSelected = !alreadyExists,
            });
        }

        if (options.IncludePlayer)
        {
            var player = entities.Player?.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(player))
                AddPhrase(player, "Player");
        }

        if (options.IncludeParty)
        {
            foreach (var companion in entities.Party ?? [])
            {
                if (companion is null)
                    continue;

                AddPhrase(companion.Name, companion.Relationship ?? "Party");
            }

            foreach (var character in entities.Characters ?? [])
            {
                if (character is null)
                    continue;

                AddPhrase(character.Name, character.Role ?? "Character", options.IncludeAliases ? character.Aliases?.Count ?? 0 : 0);
                if (options.IncludeAliases)
                {
                    foreach (var alias in character.Aliases ?? [])
                        AddPhrase(alias, $"Alias · {character.Name}", 0);
                }
            }
        }

        var candidates = list
            .GroupBy(c => c.Phrase, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => c.AlreadyExists)
            .ThenBy(c => c.Phrase, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var analysis = HighlightColorCapacityAnalyzer.Analyze(
            options.ExistingRules,
            candidates,
            colorOptions,
            palette);

        return new CastPhraseImportResult
        {
            Candidates = candidates,
            ColorAnalysis = analysis,
        };
    }

    private static string AppendAlreadyAdded(string role) =>
        role.Contains("already added", StringComparison.OrdinalIgnoreCase)
            ? role
            : $"{role} · already added";
}
