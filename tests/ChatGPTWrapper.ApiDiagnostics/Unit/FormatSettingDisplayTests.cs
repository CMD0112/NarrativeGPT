using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatSettingDisplayTests
{
    [Fact]
    public void Essential_tier_has_at_least_ten_settings()
    {
        Assert.True(FormatSettingDisplay.EssentialSettings.Count >= 10);
    }

    [Theory]
    [InlineData(FormatSettingKeys.ContentMaxWidthRem)]
    [InlineData(FormatSettingKeys.UserFontSizeRem)]
    [InlineData(FormatSettingKeys.AssistantLineHeight)]
    [InlineData(FormatSettingKeys.HeadingH2ScaleRem)]
    public void Known_keys_have_display_metadata(string key)
    {
        var def = FormatSettingDisplay.Get(key);
        Assert.False(string.IsNullOrWhiteSpace(def.DisplayLabel));
        Assert.Equal(key, def.Key);
        Assert.False(string.IsNullOrWhiteSpace(def.SearchText));
    }
}
