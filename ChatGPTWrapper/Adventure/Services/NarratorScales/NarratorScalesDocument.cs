namespace ChatGPTWrapper.Adventure.Services.NarratorScales;

internal sealed class NarratorScalesDocument
{
    public int Version { get; set; } = 1;

    public List<NarratorScaleDimensionDocument> Dimensions { get; set; } = [];

    public List<NarratorSceneProfileDocument> SceneProfiles { get; set; } = [];
}

internal sealed class NarratorScaleDimensionDocument
{
    public string Id { get; set; } = "";

    public string Category { get; set; } = "narration";

    public string PacketLabel { get; set; } = "";

    public string? NarratorParameter { get; set; }

    public List<NarratorScalePresetDocument> Presets { get; set; } = [];
}

internal sealed class NarratorScalePresetDocument
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string PacketValue { get; set; } = "";

    public string Summary { get; set; } = "";

    public List<string> Behavior { get; set; } = [];

    public List<string> Avoid { get; set; } = [];

    public List<string> PairsWellWith { get; set; } = [];
}

internal sealed class NarratorSceneProfileDocument
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
