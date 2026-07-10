namespace ChatGPTWrapper.Theme;

/// <summary>Tracks used and shared colors while assigning with optional grouping profiles.</summary>
public sealed class HighlightColorAssignmentState
{
    private readonly HashSet<string> _globalUsedColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _usedColorsByGroup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sharedGroupColors = new(StringComparer.OrdinalIgnoreCase);

    public ISet<string> GlobalUsedColors => _globalUsedColors;

    public IReadOnlyDictionary<string, string> SharedGroupColors => _sharedGroupColors;

    public void SeedColor(string color, HighlightColorGroupingResolution grouping)
    {
        if (string.IsNullOrWhiteSpace(color))
            return;

        _globalUsedColors.Add(color);
        if (grouping.IsEnabled && !string.IsNullOrWhiteSpace(grouping.GroupKey))
            GetUsedColorsForGroup(grouping.GroupKey).Add(color);
    }

    public bool TryGetSharedGroupColor(string groupKey, out string color) =>
        _sharedGroupColors.TryGetValue(groupKey, out color!);

    public ISet<string> GetScopedUsedColors(HighlightColorGroupingResolution grouping)
    {
        if (!grouping.IsEnabled || string.IsNullOrWhiteSpace(grouping.GroupKey))
            return _globalUsedColors;

        return GetUsedColorsForGroup(grouping.GroupKey);
    }

    public void RegisterAssignedColor(string color, HighlightColorGroupingResolution grouping)
    {
        if (string.IsNullOrWhiteSpace(color))
            return;

        _globalUsedColors.Add(color);
        if (!grouping.IsEnabled || string.IsNullOrWhiteSpace(grouping.GroupKey))
            return;

        GetUsedColorsForGroup(grouping.GroupKey).Add(color);
        if (grouping.ShareColorWithinGroup)
            _sharedGroupColors[grouping.GroupKey] = color;
    }

    private HashSet<string> GetUsedColorsForGroup(string groupKey)
    {
        if (!_usedColorsByGroup.TryGetValue(groupKey, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _usedColorsByGroup[groupKey] = set;
        }

        return set;
    }
}
