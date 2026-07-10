using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class FlightRecordCompareServiceTests
{
    [Fact]
    public void Compare_marks_new_this_turn_pointer()
    {
        var baseline = CreateEntry(
            thisTurn: [Pointer("mara", "Mara")]);
        var current = CreateEntry(
            thisTurn: [Pointer("mara", "Mara"), Pointer("gate", "Gate")]);

        var result = FlightRecordCompareService.Compare(current, baseline);

        Assert.Equal(2, result.ThisTurnPointers.Count);
        Assert.False(result.ThisTurnPointers[0].IsNew);
        Assert.True(result.ThisTurnPointers[1].IsNew);
        Assert.Contains(result.DeltaMessages, m => m.Contains("THIS TURN pointer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_marks_section_added_and_trimmed()
    {
        var baseline = CreateEntry(sections:
        [
            Section("sources", included: true),
            Section("summary", included: true),
        ]);
        var current = CreateEntry(
            sections:
            [
                Section("sources", included: true),
                Section("summary", included: false),
                Section("utility", included: true),
            ],
            trimmed: [new FlightTrimmedSectionRecord { Id = "summary", Reason = "budget" }]);

        var result = FlightRecordCompareService.Compare(current, baseline);

        Assert.Contains(result.SectionRows, r => r.Id == "utility" && r.ChangeBadge == "Added");
        Assert.Contains(result.SectionRows, r => r.Id == "summary" && r.ChangeBadge == "Trimmed");
        Assert.Contains(result.DeltaMessages, m => m.Contains("Trimmed: summary", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatPacketDiff_emits_unified_diff()
    {
        var baseline = CreateEntry(packetText: "line one\nline two\n");
        var current = CreateEntry(packetText: "line one\nline changed\n");

        var diff = FlightRecordCompareService.FormatPacketDiff(current, baseline, "prior", "current");

        Assert.Contains("--- prior", diff, StringComparison.Ordinal);
        Assert.Contains("+++ current", diff, StringComparison.Ordinal);
        Assert.Contains("line changed", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void FindPreviousEntry_returns_latest_older_entry()
    {
        var older = CreateEntry(atOffsetMinutes: -10);
        var middle = CreateEntry(atOffsetMinutes: -5);
        var current = CreateEntry(atOffsetMinutes: 0);
        var entries = new List<PromptHistoryEntry> { older, middle, current };

        var found = FlightRecordCompareService.FindPreviousEntry(entries, current);

        Assert.NotNull(found);
        Assert.Equal(middle.Id, found!.Id);
    }

    private static PromptHistoryEntry CreateEntry(
        string packetText = "packet",
        IReadOnlyList<FlightInjectionSectionRecord>? sections = null,
        IReadOnlyList<FlightTrimmedSectionRecord>? trimmed = null,
        IReadOnlyList<FlightContextPointerRecord>? thisTurn = null,
        int atOffsetMinutes = 0)
    {
        return new PromptHistoryEntry
        {
            At = DateTimeOffset.UtcNow.AddMinutes(atOffsetMinutes),
            PacketText = packetText,
            Injection = new FlightInjectionSnapshot
            {
                Sections = sections?.ToList() ?? [],
                Trimmed = trimmed?.ToList() ?? [],
                ThisTurnPointers = thisTurn?.ToList() ?? [],
            },
        };
    }

    private static FlightInjectionSectionRecord Section(string id, bool included) =>
        new()
        {
            Id = id,
            Kind = "Reference",
            Included = included,
            CharEstimate = 100,
        };

    private static FlightContextPointerRecord Pointer(string machineId, string title) =>
        new()
        {
            MachineId = machineId,
            Title = title,
            Source = "NameMatch",
            Score = 80,
            Mode = "PointerOnly",
        };
}
