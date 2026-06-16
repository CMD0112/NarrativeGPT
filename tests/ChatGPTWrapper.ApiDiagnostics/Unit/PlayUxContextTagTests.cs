using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ContextTagFormatTests
{
    [Fact]
    public void WrapBlock_round_trips_through_extract()
    {
        var wrapped = ContextTagFormat.WrapBlock("state", "Location: tavern");
        Assert.Equal("tavern", ContextTagFormat.ExtractBlock(wrapped, "state")?.Split(':').Last().Trim());
    }

    [Fact]
    public void StripTaggedBlocks_removes_marked_regions()
    {
        var text = ContextTagFormat.WrapBlock("instructions", "secret") + "\n\nVisible prose.";
        var stripped = ContextTagFormat.StripTaggedBlocks(text);
        Assert.DoesNotContain("secret", stripped);
        Assert.Contains("Visible prose.", stripped);
    }

    [Fact]
    public void FormatStructuredPreview_lists_named_sections()
    {
        var packet = ContextTagFormat.WrapBlock("instructions", "Be vivid.")
                     + "\n\n"
                     + "open door";

        var preview = ContextTagFormat.FormatStructuredPreview(packet);
        Assert.Contains("[instructions]", preview);
        Assert.Contains("Be vivid.", preview);
        Assert.Contains("[user]", preview);
        Assert.Contains("open door", preview);
    }

    [Fact]
    public void FormatStructuredPreview_shows_legacy_player_tag()
    {
        var packet = ContextTagFormat.WrapBlock("instructions", "Be vivid.")
                     + "\n"
                     + ContextTagFormat.WrapBlock("player", "open door");

        var preview = ContextTagFormat.FormatStructuredPreview(packet);
        Assert.Contains("[player]", preview);
    }

    [Fact]
    public void ExtractUntaggedSuffix_returns_text_after_tags()
    {
        var packet = ContextTagFormat.WrapBlock("state", "Harbor") + "\n\nlook around";
        Assert.Equal("look around", ContextTagFormat.ExtractUntaggedSuffix(packet));
    }

    [Fact]
    public void Malformed_tags_are_left_unchanged_on_strip()
    {
        const string text = "[[cgw:broken only half";
        Assert.Equal(text, ContextTagFormat.StripTaggedBlocks(text));
    }

    [Fact]
    public void WrapUtilityJob_includes_job_attribute_and_body()
    {
        var wrapped = ContextTagFormat.WrapUtilityJob("propose_memories", "=== MEMORY PROPOSAL JOB ===");
        Assert.Contains("[[cgw:utility", wrapped);
        Assert.Contains("job=\"propose_memories\"", wrapped);
        Assert.Contains("=== MEMORY PROPOSAL JOB ===", wrapped);
        Assert.Equal("propose_memories", ContextTagFormat.ExtractUtilityJobId(wrapped));
        Assert.True(ContextTagFormat.IsUtilityTagged(wrapped));
    }

    [Fact]
    public void WrapUtilityResponse_round_trips_through_unwrap()
    {
        var wrapped = ContextTagFormat.WrapUtilityResponse("generate_recap", "Short recap.");
        Assert.True(ContextTagFormat.IsUtilityResponseTagged(wrapped));
        Assert.Equal("Short recap.", ContextTagFormat.UnwrapUtilityJobResponse(wrapped));
    }

    [Fact]
    public void AppendInlineUtilityResponseContract_includes_wrapper_example()
    {
        var body = ContextTagFormat.AppendInlineUtilityResponseContract(
            "Do the job.",
            GenerationJobId.ProposeMemories,
            expectsJsonArray: true);
        Assert.Contains("INLINE UTILITY RESPONSE", body);
        Assert.Contains("[[cgw:utility-response", body);
        Assert.Contains("job=\"propose_memories\"", body);
    }
}

[Trait("Category", "Unit")]
public sealed class PlayTabPinServiceTests
{
    [Fact]
    public void PreferPinnedPlayWebView_requires_play_mode_and_pin_key()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-x",
                PinnedPlayTabKey = "abc123",
            },
        };

        Assert.True(PlayTabPinService.PreferPinnedPlayWebView(true, bundle));
        Assert.False(PlayTabPinService.PreferPinnedPlayWebView(false, bundle));
        Assert.False(PlayTabPinService.PreferPinnedPlayWebView(true, null));
        Assert.False(PlayTabPinService.PreferPinnedPlayWebView(true, new AdventureBundle { Metadata = new AdventureMetadata() }));
    }

    [Fact]
    public void PreferAdventureWebViewForLinkedProject_delegates_to_pin_helper()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { PinnedPlayTabKey = "pin-1" },
        };

        Assert.True(AdventurePlayContextService.PreferPinnedPlayWebView(true, bundle));
        Assert.False(AdventurePlayContextService.PreferPinnedPlayWebView(true, new AdventureBundle { Metadata = new AdventureMetadata() }));
    }
}

[Trait("Category", "Unit")]
public sealed class PromptPacketBuilderTagTests
{
    [Fact]
    public void Build_wraps_sections_when_UseContextTags_enabled()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings { UseContextTags = true } },
            Summary = new SummaryDocument { RollingSummary = "The hero arrived." },
            State = new StateDocument { CurrentLocation = "Harbor" },
        };

        var packet = PromptPacketBuilder.Build(bundle, "look around");
        Assert.Contains("[[cgw:meta", packet.Text);
        Assert.Contains("[[cgw:instructions", packet.Text);
        Assert.DoesNotContain("[[cgw:player", packet.Text);
        Assert.EndsWith("look around", packet.Text.Trim());
    }

    [Fact]
    public void BuildContext_excludes_player_prose()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings { UseContextTags = true } },
            State = new StateDocument { CurrentLocation = "Harbor" },
        };

        var ctx = PromptPacketBuilder.BuildContext(bundle, "secret word");
        Assert.DoesNotContain("secret word", ctx.ContextText);
        Assert.Contains("[[cgw:state", ctx.ContextText);
    }

    [Fact]
    public void AssembleWithUser_appends_untagged_suffix_after_context()
    {
        var context = ContextTagFormat.WrapBlock("state", "Harbor");
        var merged = PromptPacketBuilder.AssembleWithUser(context, "look around", useContextTags: true);
        Assert.StartsWith("[[cgw:state", merged);
        Assert.EndsWith("look around", merged.Trim());
    }
}

[Trait("Category", "Unit")]
public sealed class PromptInjectionServiceTests
{
    [Fact]
    public void PrepareSend_matches_Build_hash_and_merge()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings { UseContextTags = true } },
            State = new StateDocument { CurrentLocation = "Dock" },
        };

        var direct = PromptPacketBuilder.Build(bundle, "enter the ship");
        var prepared = PromptInjectionService.PrepareSend(bundle, "enter the ship");

        Assert.Equal(direct.Text, prepared.MergedText);
        Assert.Equal(direct.Hash, prepared.Hash);
        Assert.Equal("enter the ship", prepared.UserText);
        Assert.DoesNotContain("enter the ship", prepared.ContextText);
    }
}

[Trait("Category", "Unit")]
public sealed class PlayContextSessionCacheTests
{
    [Fact]
    public void TryBindConversationFromUrl_persists_id_when_missing()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };

        var bound = PlayContextSessionCache.TryBindConversationFromUrl(
            bundle,
            "https://chatgpt.com/c/abc-123?project=g-p-test");

        Assert.True(bound);
        Assert.Equal("abc-123", bundle.Metadata.LinkedConversationId);
    }

    [Fact]
    public void TrySyncConversationFromUrl_updates_stale_linked_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
                LinkedConversationId = "old-thread",
            },
        };

        var url = ChatGptUrls.BuildProjectConversationUrl("new-thread", "g-p-test");
        var synced = PlayContextSessionCache.TrySyncConversationFromUrl(bundle, url);

        Assert.True(synced);
        Assert.Equal("new-thread", bundle.Metadata.LinkedConversationId);
    }

    [Fact]
    public void TryGetFresh_returns_entry_within_max_age()
    {
        var id = Guid.NewGuid();
        PlayContextSessionCache.Record(id, "https://chatgpt.com/c/x", "x", composerFound: true);

        Assert.True(PlayContextSessionCache.TryGetFresh(id, out var entry));
        Assert.Equal("x", entry.ConversationId);
        Assert.True(entry.ComposerFound);
    }

    [Fact]
    public void Invalidate_removes_cached_entry()
    {
        var id = Guid.NewGuid();
        PlayContextSessionCache.Record(id, "https://chatgpt.com/c/x", "x", composerFound: true);
        PlayContextSessionCache.Invalidate(id);

        Assert.False(PlayContextSessionCache.TryGetFresh(id, out _));
    }

    [Fact]
    public void ShouldSkipReensureForSource_false_on_project_page_when_play_thread_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
                LinkedConversationId = "thread-1",
            },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-test");

        Assert.False(PlayContextSessionCache.ShouldSkipReensureForSource(bundle, source));
    }

    [Fact]
    public void ShouldSkipReensureForSource_true_on_project_page_when_no_play_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
            },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-test");

        Assert.True(PlayContextSessionCache.ShouldSkipReensureForSource(bundle, source));
    }

    [Fact]
    public void ShouldSkipReensureForSource_true_on_linked_play_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
                LinkedConversationId = "thread-1",
            },
        };

        var source = ChatGptUrls.BuildProjectConversationUrl("thread-1", "g-p-test");

        Assert.True(PlayContextSessionCache.ShouldSkipReensureForSource(bundle, source));
    }

    [Fact]
    public void Build_legacy_fat_skips_entity_excerpts_without_exported_lore()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { UseSectionInjection = false },
            },
            Entities = new EntitiesDocument
            {
                Locations =
                [
                    new LocationEntry { Name = "The Room", Description = "A plain room." },
                ],
            },
        };

        var packet = PromptPacketBuilder.Build(bundle, "look around the room");
        Assert.DoesNotContain("A plain room", packet.Text);
        Assert.DoesNotContain("=== ENTITIES ===", packet.Text);
    }

    [Fact]
    public void Build_keeps_legacy_headers_when_UseContextTags_disabled()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings { UseContextTags = false } },
        };

        var packet = PromptPacketBuilder.Build(bundle, "hello");
        Assert.Contains("=== PLAYER TURN ===", packet.Text);
        Assert.DoesNotContain("[[cgw:", packet.Text);
    }
}
