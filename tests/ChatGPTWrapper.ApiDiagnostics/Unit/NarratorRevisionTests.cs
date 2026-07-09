using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class NarratorRevisionTests : IDisposable
{
    private readonly string _tempRoot;

    public NarratorRevisionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-revision-" + Guid.NewGuid().ToString("N"));
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void IsRevisionPromptUserMessage_matches_prompt_after_invalidation_marker()
    {
        const string prompt =
            "[[cgw:invalidation turn=\"1\"]]\n"
            + "For play turn 1 only: disregard your prior assistant reply for this turn "
            + "and any later play turns in the thread. Output ONLY the replacement narrator text "
            + "below with no preamble or commentary.\n\n"
            + "Test received, friendo";

        Assert.True(NarratorRevisionPrompt.IsRevisionPromptUserMessage(prompt));
    }

    [Fact]
    public void IsRevisionPromptUserMessage_rejects_play_user_line()
    {
        Assert.False(NarratorRevisionPrompt.IsRevisionPromptUserMessage("Test"));
    }

    [Fact]
    public void RecordNarratorComposerRevision_writes_hide_entries_for_reload()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);

        ThreadMetadataService.RecordNarratorComposerRevision(
            bundle,
            logTurnIndex: 0,
            revisionGroupId: "rev-test",
            revisionPromptText: "For play turn 1 only: ...",
            assistantDomTurnId: "42",
            replacementText: "Test received, friendo");

        var entries = ThreadMetadataService.BuildRevisionHideEntries(bundle);

        Assert.Contains(entries, e => e.AssistantDomTurnId == "42");
        Assert.Contains(entries, e => e.MessageKind == ThreadMessageKind.NarratorRevisionPrompt);
        Assert.Contains(bundle.ThreadMetadata.Messages, m => m.MessageKind == ThreadMessageKind.NarratorReplacement);
    }

    [Fact]
    public void ToTranscriptPairs_uses_replacement_narrator_after_revision_prompt()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new()
                {
                    NodeId = "u1",
                    Role = "user",
                    RawText = "Test",
                    DisplayText = "Test",
                    BranchIndex = 0,
                },
                new()
                {
                    NodeId = "a1",
                    Role = "assistant",
                    RawText = "Test received.",
                    DisplayText = "Test received.",
                    BranchIndex = 1,
                },
                new()
                {
                    NodeId = "u2",
                    Role = "user",
                    RawText =
                        "[[cgw:invalidation turn=\"1\"]]\n"
                        + "For play turn 1 only: disregard your prior assistant reply for this turn "
                        + "and any later play turns in the thread. Output ONLY the replacement narrator text "
                        + "below with no preamble or commentary.\n\n"
                        + "Test received, friendo",
                    DisplayText =
                        "[[cgw:invalidation turn=\"1\"]]\n"
                        + "For play turn 1 only: disregard your prior assistant reply for this turn "
                        + "and any later play turns in the thread. Output ONLY the replacement narrator text "
                        + "below with no preamble or commentary.\n\n"
                        + "Test received, friendo",
                    BranchIndex = 2,
                },
                new()
                {
                    NodeId = "a2",
                    Role = "assistant",
                    RawText = "Test received, friendo",
                    DisplayText = "Test received, friendo",
                    BranchIndex = 3,
                },
            ],
            ThreadConversationLogCaptureSource.Api);

        var pairs = ThreadConversationLogService.ToTranscriptPairs(bundle.Metadata.Id, entry.Id);

        Assert.Single(pairs);
        Assert.Equal("Test", pairs[0].PlayerText);
        Assert.Equal("Test received, friendo", pairs[0].NarratorText);
    }

    [Fact]
    public void BuildRevisionHideEntries_includes_revision_prompts_from_thread_log()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new()
                {
                    NodeId = "u-rev",
                    Role = "user",
                    RawText =
                        "[[cgw:invalidation turn=\"1\"]]\n"
                        + "For play turn 1 only: disregard your prior assistant reply for this turn "
                        + "and any later play turns in the thread. Output ONLY the replacement narrator text "
                        + "below with no preamble or commentary.\n\n"
                        + "Replacement",
                    DisplayText =
                        "[[cgw:invalidation turn=\"1\"]]\n"
                        + "For play turn 1 only: disregard your prior assistant reply for this turn "
                        + "and any later play turns in the thread. Output ONLY the replacement narrator text "
                        + "below with no preamble or commentary.\n\n"
                        + "Replacement",
                    BranchIndex = 0,
                },
            ],
            ThreadConversationLogCaptureSource.Api);

        var entries = ThreadMetadataService.BuildRevisionHideEntries(bundle);

        Assert.Contains(entries, e => e.MessageKind == ThreadMessageKind.NarratorRevisionPrompt);
    }

    private static AdventureThreadEntry RegisterPlayThread(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)
                    ?? AdventureThreadRegistryService.RegisterEntry(
                        bundle,
                        AdventureThreadKind.Play,
                        conversationId: "test-conversation-id",
                        label: "Play");

        if (string.IsNullOrWhiteSpace(entry.ConversationId))
            entry.ConversationId = "test-conversation-id";

        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);
        return entry;
    }
}
