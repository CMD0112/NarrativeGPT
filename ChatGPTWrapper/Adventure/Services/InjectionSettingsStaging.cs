using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Applies pending UI values to an in-memory staging bundle for live injection preview.
/// </summary>
public sealed class InjectionSettingsStaging
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AdventureBundle Bundle { get; }

    public InjectionSettingsStaging(AdventureBundle source)
    {
        Bundle = CloneBundleForStaging(source);
        PlayInjectionPolicyService.EnsureDefaults(Bundle.Metadata);
    }

    public static AdventureBundle CloneBundleForStaging(AdventureBundle source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<AdventureBundle>(json, JsonOptions)!;
    }
}

public static class InjectionSettingsUiHelper
{
    public static string FormatCharBudget(int used, int max) =>
        max > 0 ? $"{used:N0} / {max:N0} chars" : $"{used:N0} chars";

    public static double CharBudgetRatio(int used, int max) =>
        max <= 0 ? 0 : Math.Min(1.0, (double)used / max);
}
