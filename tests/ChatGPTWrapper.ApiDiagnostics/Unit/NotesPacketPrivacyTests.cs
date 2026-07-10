using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class NotesPacketPrivacyTests
{
    [Fact]
    public void Build_never_includes_player_notes()
    {
        const string secret = "SECRET_PLAYER_SCRATCHPAD_XYZ";
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata(),
            Notes = secret,
            State = new StateDocument { CurrentLocation = "Harbor" },
        };

        var packet = PromptPacketBuilder.Build(bundle, "look around");
        Assert.DoesNotContain(secret, packet.Text);
    }

    [Fact]
    public void BuildContext_never_includes_player_notes()
    {
        const string secret = "SECRET_PLAYER_SCRATCHPAD_XYZ";
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings { UseContextTags = true } },
            Notes = secret,
            State = new StateDocument { CurrentLocation = "Harbor" },
        };

        var ctx = PromptPacketBuilder.BuildContext(bundle, "player line");
        Assert.DoesNotContain(secret, ctx.ContextText);
    }
}
