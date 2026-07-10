using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class CastPhraseImportOptions
{
    /// <summary>Canon kind IDs to import. When null, <see cref="IncludePlayer"/> / <see cref="IncludeParty"/> legacy flags apply.</summary>
    public IReadOnlySet<string>? IncludedSourceKeys { get; set; }

    public bool IncludePlayer { get; set; } = true;

    public bool IncludeParty { get; set; } = true;

    public bool IncludeEntityAliases { get; set; } = true;

    public bool IsSourceIncluded(string sourceKey)
    {
        if (IncludedSourceKeys is not null)
            return IncludedSourceKeys.Contains(sourceKey);

        return PhraseHighlightEntitySourceCatalog
            .ResolveLegacyImportSourceKeys(IncludePlayer, IncludeParty)
            .Contains(sourceKey);
    }

    /// <summary>Existing phrase highlight rules — colors are reserved and matching phrases are treated as already imported.</summary>
    public IReadOnlyList<PhraseHighlightRule>? ExistingRules { get; set; }

    /// <summary>Active wrapper theme; defaults to <see cref="ThemeRuntime.Current"/>.</summary>
    public ResolvedTheme? Theme { get; set; }

    /// <summary>Transcript canvas background for contrast checks; defaults to theme <c>BgBase</c>.</summary>
    public string? HighlightCanvasBackground { get; set; }

    /// <summary>Auto-color profile options; defaults to saved chrome settings.</summary>
    public HighlightColorAssignmentOptions? ColorAssignment { get; set; }

    /// <summary>When set, overrides <see cref="HighlightColorAssignmentOptions.AssignmentSalt"/>.</summary>
    public int? AssignmentSalt { get; set; }

    /// <summary>Optional grouping profile for scoped/shared auto colors.</summary>
    public HighlightColorGroupingProfile? GroupingProfile { get; set; }

    /// <summary>Transcript format used to reserve user/narrator body text colors.</summary>
    public ContinuousViewFormatSettings? ContinuousViewFormat { get; set; }
}

public sealed class CastPhraseImportCandidate : INotifyPropertyChanged
{
    public required string Phrase { get; init; }

    public string Role { get; init; } = "";

    private string _color = "#FFD166";

    public string Color
    {
        get => _color;
        set
        {
            if (string.Equals(_color, value, StringComparison.OrdinalIgnoreCase))
                return;
            _color = value;
            OnPropertyChanged();
        }
    }

    public Guid? EntityId { get; init; }

    public string? EntityCategory { get; init; }

    /// <summary>Primary phrase when this candidate is a first-name or entity alias.</summary>
    public string? SyncWithPhrase { get; init; }

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
            .Select(c => new PhraseHighlightRule
            {
                Phrase = c.Phrase.Trim(),
                Color = c.Color,
                EntityId = c.EntityId,
                EntityCategory = c.EntityCategory,
                SyncWithPhrase = string.IsNullOrWhiteSpace(c.SyncWithPhrase) ? null : c.SyncWithPhrase.Trim(),
            })
            .ToList();
}

internal static class PhraseHighlightCastImportService
{
    public static CastPhraseImportResult BuildCandidates(AdventureBundle? bundle, CastPhraseImportOptions? options = null)
    {
        options ??= new CastPhraseImportOptions();
        if (bundle?.Entities is null)
            return new CastPhraseImportResult();

        var theme = options.Theme ?? ThemeRuntime.Current;
        var colorOptions = (options.ColorAssignment
            ?? HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony)).Clone();
        if (options.AssignmentSalt is not null)
            colorOptions.AssignmentSalt = options.AssignmentSalt.Value;
        var canvas = options.HighlightCanvasBackground
            ?? HighlightColorAssignmentEngine.ResolveCanvas(colorOptions, theme);
        var reserved = HighlightColorReservedColors.Resolve(theme, options.ContinuousViewFormat);
        var existingRules = HighlightColorCapacityAnalyzer.IndexExistingRules(options.ExistingRules);
        var existingByEntity = HighlightColorCapacityAnalyzer.IndexExistingRulesByEntity(options.ExistingRules);

        var pending = CollectPendingCandidates(bundle, options, existingRules, existingByEntity);
        var minimumDistinct = HighlightColorCapacityAnalyzer.EstimateNewDistinctColorsNeeded(
            pending.Select(p => (p.Phrase, p.Role, p.AlreadyExists)),
            colorOptions);
        var palette = HighlightColorAssignmentEngine.BuildPalette(colorOptions, theme, canvas, minimumDistinct, reserved);
        var characterColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignmentState = new HighlightColorAssignmentState();
        HighlightColorCapacityAnalyzer.SeedFromExistingRules(options.ExistingRules, usedColors, characterColors);
        foreach (var used in usedColors)
            assignmentState.GlobalUsedColors.Add(used);
        foreach (var color in reserved)
            assignmentState.GlobalUsedColors.Add(color);
        var discoveryIndex = 0;

        var list = new List<CastPhraseImportCandidate>();

        foreach (var item in pending)
        {
            string color;

            if (item.AlreadyExists)
            {
                color = item.ExistingRule?.Color?.Trim() ?? "#FFD166";
                if (string.IsNullOrWhiteSpace(color))
                    color = "#FFD166";
            }
            else
            {
                if (TryResolveParentHighlightColor(item.Role, characterColors, out var parentColor))
                {
                    color = parentColor;
                }
                else
                {
                    var rule = item.EntityId is null && string.IsNullOrWhiteSpace(item.EntityCategory)
                        ? null
                        : new PhraseHighlightRule
                        {
                            Phrase = item.Phrase,
                            EntityId = item.EntityId,
                            EntityCategory = item.EntityCategory,
                        };
                    var grouping = HighlightColorGroupingResolver.Resolve(
                        options.GroupingProfile,
                        rule,
                        item.Role,
                        item.Phrase);
                    if (grouping.IsExcluded)
                    {
                        color = item.ExistingRule?.Color?.Trim() ?? "#FFD166";
                    }
                    else
                    {
                        color = HighlightColorGroupedAssignment.AssignColor(
                            colorOptions,
                            options.GroupingProfile,
                            rule,
                            item.Role,
                            item.Phrase,
                            palette,
                            canvas,
                            characterColors,
                            assignmentState,
                            discoveryIndex++,
                            theme,
                            fallbackColor: item.ExistingRule?.Color,
                            reservedForegroundColors: reserved);
                    }
                }

                if (!item.Role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
                    characterColors[item.Phrase] = color;
            }

            list.Add(new CastPhraseImportCandidate
            {
                Phrase = item.Phrase,
                Role = item.AlreadyExists ? AppendAlreadyAdded(item.Role) : item.Role,
                Color = color,
                EntityId = item.EntityId,
                EntityCategory = item.EntityCategory,
                SyncWithPhrase = item.SyncWithPhrase,
                AlreadyExists = item.AlreadyExists,
                IsSelected = !item.AlreadyExists,
            });
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

    private static bool TryResolveParentHighlightColor(
        string role,
        IReadOnlyDictionary<string, string> characterColors,
        out string color)
    {
        color = "";
        string? parentName = null;
        if (role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
            parentName = role["Alias · ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(parentName))
            return false;

        return characterColors.TryGetValue(parentName, out color!);
    }

    private sealed record PendingCandidate(
        string Phrase,
        string Role,
        Guid? EntityId,
        string? EntityCategory,
        bool AlreadyExists,
        PhraseHighlightRule? ExistingRule,
        string? SyncWithPhrase = null);

    private static List<PendingCandidate> CollectPendingCandidates(
        AdventureBundle bundle,
        CastPhraseImportOptions options,
        Dictionary<string, PhraseHighlightRule> existingRules,
        Dictionary<string, PhraseHighlightRule> existingByEntity)
    {
        var list = new List<PendingCandidate>();
        var entities = bundle.Entities!;

        void AddPhrase(
            string? phrase,
            string role,
            Guid? entityId = null,
            string? entityCategory = null,
            string? syncWithPhrase = null)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return;

            var trimmed = phrase.Trim();
            PhraseHighlightRule? linkedRule = null;
            var alreadyExists = existingRules.TryGetValue(trimmed, out var phraseRule);
            if (!alreadyExists
                && entityId is not null
                && !string.IsNullOrWhiteSpace(entityCategory)
                && existingByEntity.TryGetValue(
                    HighlightColorCapacityAnalyzer.EntityKey(entityCategory, entityId.Value),
                    out linkedRule))
            {
                alreadyExists = true;
                phraseRule = linkedRule;
            }

            list.Add(new PendingCandidate(
                trimmed,
                role,
                entityId,
                entityCategory,
                alreadyExists,
                phraseRule ?? linkedRule,
                string.IsNullOrWhiteSpace(syncWithPhrase) ? null : syncWithPhrase.Trim()));
        }

        void MaybeAddEntityAliases(
            string? primaryName,
            IEnumerable<string>? aliases,
            Guid? entityId,
            string? entityCategory)
        {
            if (!options.IncludeEntityAliases || entityId is null || string.IsNullOrWhiteSpace(primaryName))
                return;

            var primary = primaryName.Trim();
            foreach (var alias in aliases ?? [])
            {
                if (string.IsNullOrWhiteSpace(alias)
                    || alias.Trim().Equals(primary, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddPhrase(alias, $"Alias · {primary}", entityId, entityCategory, syncWithPhrase: primary);
            }
        }

        foreach (var kind in PhraseHighlightEntitySourceCatalog.ListImportKinds())
        {
            if (!options.IsSourceIncluded(kind.KindId))
                continue;

            foreach (var entity in PhraseHighlightEntitySourceCatalog.EnumerateImportEntities(entities, kind))
            {
                if (entity is null)
                    continue;

                var name = PhraseHighlightEntityImportHelper.GetDisplayName(entity, kind);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var entityId = kind.KindId.Equals(CanonSchemaRegistry.PlayerKind, StringComparison.OrdinalIgnoreCase)
                    ? EntityEditMapper.PlayerEntityId
                    : CanonEntityResolver.GetEntityId(entity, kind);
                var role = PhraseHighlightEntitySourceCatalog.ResolveImportRole(entity, kind);

                AddPhrase(name, role, entityId, kind.UiCategory);
                MaybeAddEntityAliases(
                    name,
                    PhraseHighlightEntityImportHelper.GetAliases(entity),
                    entityId,
                    kind.UiCategory);
            }
        }

        return list;
    }
}
