using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InjectionPolicyGoldenTests
{
    private static AdventureBundle CreateThinLinkedBundle()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-golden", inSync: true, entryCount: 5);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.LocalSha256 = "hash";
            SourceManifestHelper.MarkManuallyPublished(entry);
        }
        bundle.Metadata.Settings.ContentBoundaries = ["No explicit gore."];
        bundle.Metadata.Settings.CharacterPortrayalRules =
        [
            new CharacterPortrayalRule { Rule = "NPCs speak in dialect." },
        ];
        PopulateSectionManifest(bundle);
        bundle.SourceManifest.RefreshSyncedFlag();
        return bundle;
    }

    [Fact]
    public void ThinLinkedPublished_noInlineContract()
    {
        var bundle = CreateThinLinkedBundle();

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around the room.");

        Assert.Equal(PacketProfile.SourceDelegated, prepared.Profile);
        Assert.Equal(PacketMode.Thin, prepared.Mode);
        Assert.Contains("[[cgw:sources", prepared.MergedText);
        Assert.DoesNotContain("Content boundaries:", prepared.MergedText);
        Assert.DoesNotContain("=== SCENARIO ===", prepared.MergedText);
        Assert.DoesNotContain("A haunted castle on the moor", prepared.MergedText);
        InjectionPolicyGuard.AssertThinDelegationPolicy(prepared.MergedText);
    }

    private static AdventureBundle CreateMinimalUnlinkedBundle()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null, inSync: false, entryCount: 0);
        bundle.SourceManifest.Entries.Clear();
        bundle.Metadata.Settings.UseContextTags = true;
        return bundle;
    }

    [Fact]
    public void MinimalUnlinked_noInlineContract()
    {
        var bundle = CreateMinimalUnlinkedBundle();

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around.");

        Assert.Equal(PacketProfile.MinimalLocal, prepared.Profile);
        Assert.Equal(PacketMode.Thin, prepared.Mode);
        Assert.Contains("mode=\"minimal\"", prepared.MergedText);
        Assert.DoesNotContain("Content boundaries:", prepared.MergedText);
        Assert.DoesNotContain("=== WORLD RULES ===", prepared.MergedText);
    }

    [Fact]
    public void FatUnlinked_hasContract()
    {
        var bundle = CreateMinimalUnlinkedBundle();
        bundle.Metadata.Settings.ForceInlineLore = true;
        bundle.Metadata.Settings.ContentBoundaries = ["No explicit gore."];

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around.");

        Assert.Equal(PacketProfile.InlineFallback, prepared.Profile);
        Assert.Equal(PacketMode.Fat, prepared.Mode);
        Assert.Contains("Content boundaries:", prepared.MergedText);
        Assert.Contains("mode=\"inline\"", prepared.MergedText);
    }

    [Fact]
    public void OverridesInherit_noOverrideBlock()
    {
        var bundle = CreateThinLinkedBundle();
        bundle.Metadata.Settings.DetailLevel = "medium";
        bundle.Metadata.Settings.Tone = "neutral";
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            DetailLevel = "medium",
            Tone = null,
            ResponseLength = null,
        };

        var prepared = PromptInjectionService.PrepareSend(bundle, "Listen at the door.");

        Assert.DoesNotContain("=== TURN OVERRIDES ===", prepared.MergedText);
    }

    [Fact]
    public void OverridesToneShift_onlyToneLine()
    {
        var bundle = CreateThinLinkedBundle();
        bundle.Metadata.Settings.Tone = "neutral";
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            Tone = "grim",
        };

        var prepared = PromptInjectionService.PrepareSend(bundle, "Enter the crypt.");

        Assert.Contains("=== TURN OVERRIDES ===", prepared.MergedText);
        var overrideBlock = ExtractBlock(prepared.MergedText, "=== TURN OVERRIDES ===", "===");
        Assert.Contains("Tone: grim", overrideBlock);
        Assert.DoesNotContain("Detail level:", overrideBlock);
        Assert.DoesNotContain("Response length:", overrideBlock);
        Assert.DoesNotContain("Difficulty:", overrideBlock);
    }

    [Fact]
    public void HandoffMidAdventure_continuationMeta_pointerFirstWhenPublished()
    {
        var bundle = CreateThinLinkedBundle();
        bundle.Metadata.LinkedConversationId = "conv-handoff";
        AdventureSessionService.EnsureSession(bundle);
        for (var i = 0; i < 3; i++)
        {
            bundle.Log.Turns.Add(new TurnRecord
            {
                Index = i + 1,
                PlayerText = $"Player line {i + 1}",
                NarratorText = $"Narrator line {i + 1}",
                Status = TurnStatus.Accepted,
                ConversationId = "conv-handoff",
                SessionId = bundle.CurrentSessionId,
            });
        }

        bundle.Summary.RollingSummary = "The party reached the old mill.";
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var packet = PlayHandoffService.BuildHandoffPacket(bundle, snapshot, new PlayHandoffOptions());

        Assert.Contains("continuation=\"true\"", packet, StringComparison.Ordinal);
        Assert.Contains("[[cgw:sources", packet);
        Assert.DoesNotContain("=== SCENARIO ===", packet);
        Assert.DoesNotContain("A haunted castle on the moor", packet);
        InjectionPolicyGuard.AssertThinDelegationPolicy(packet);
    }

    [Fact]
    public void StartPacket_sectionInjection_omitsFileListFromPlayerDirective()
    {
        var bundle = CreateThinLinkedBundle();

        var directive = AdventureBootstrapService.BuildStartPlayerDirective(bundle);
        var packet = AdventureBootstrapService.BuildStartPacket(bundle);

        Assert.DoesNotContain("Project sources to retrieve:", directive, StringComparison.Ordinal);
        Assert.DoesNotContain("Adventure source files:", directive, StringComparison.Ordinal);
        Assert.DoesNotContain("scenario.md", directive, StringComparison.Ordinal);
        Assert.Contains("Your reply is the opening scene", directive, StringComparison.Ordinal);
        Assert.Contains("[[cgw:sources", packet);
        Assert.Contains("ALWAYS RETRIEVE", packet);
    }

    private static string ExtractBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            return "";

        start += startMarker.Length;
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        return end < 0 ? text[start..].Trim() : text[start..end].Trim();
    }

    private static void PopulateSectionManifest(AdventureBundle bundle)
    {
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.Sections = entry.RelativePath switch
            {
                "scenario.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "opening",
                        Kind = "scenario",
                        Title = "Opening",
                        BodyCache = bundle.Scenario.OpeningSituation,
                        KeyPhrase = "Rain",
                    },
                ],
                "world.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "rules",
                        Kind = "rule",
                        Title = "Rules",
                        BodyCache = bundle.Scenario.WorldRules,
                        KeyPhrase = "Magic",
                    },
                ],
                "plot.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "plot",
                        Kind = "plot",
                        Title = "Plot",
                        BodyCache = bundle.Scenario.PlotEssentials,
                        KeyPhrase = "lord",
                    },
                ],
                "cast.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "player",
                        Kind = "character",
                        Title = "Player",
                        BodyCache = bundle.Scenario.PlayerRole,
                        KeyPhrase = "Investigator",
                    },
                ],
                _ => [],
            };
        }
    }

    [Fact]
    public void CompactPreset_limits_transcript_turns()
    {
        var bundle = CreateThinLinkedBundle();
        PlayInjectionPolicyService.ApplyPreset(bundle.Metadata.Settings, InjectionPresetIds.Compact);
        bundle.Log.Turns.AddRange(Enumerable.Range(1, 5).Select(i => new TurnRecord
        {
            Index = i,
            PlayerText = $"Line {i}",
            NarratorText = $"Reply {i}",
            Status = TurnStatus.Accepted,
        }));

        var prepared = PromptInjectionService.PrepareSend(bundle, "Continue.");

        if (prepared.MergedText.Contains("=== RECENT TRANSCRIPT ===", StringComparison.Ordinal))
        {
            var section = prepared.MergedText.Split("=== RECENT TRANSCRIPT ===")[1];
            Assert.Contains("Line 4", section);
            Assert.Contains("Line 5", section);
            Assert.DoesNotContain("Line 1", section);
        }
    }
}
