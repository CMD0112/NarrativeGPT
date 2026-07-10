namespace ChatGPTWrapper.Core.LocalInference;

/// <summary>
/// Built-in lab scenarios for local inference testing.
/// Not connected to play send or generation job orchestration.
/// </summary>
public static class LocalInferenceLabScenarios
{
    public const string CustomId = "custom";

    public const string EntityExtractionId = "entity-extraction";

    public const string PronounTrackingId = "pronoun-tracking";

    public const string ProposeMemoriesId = "propose-memories";

    public const string UpdateSummaryId = "update-summary";

    public const string ContinuityCheckId = "continuity-check";

    public const string JsonComplianceId = "json-compliance";

    public const string NarratorVoiceId = "narrator-voice";

    public const string EntityExtractionSystemPrompt = """
        You are a structured entity extractor for a tabletop-style narrative adventure.
        Entities are durable world-model referents (people, places, things, factions, quests, concepts).
        Respond with JSON only — a single JSON array, no markdown fences or commentary.

        Each array element must include:
        - entityType: "person" | "place" | "thing" | "faction" | "quest" | "concept"
        - name: string (required)
        - description: string
        - action: "create" | "update" | "noop" (optional; default create)

        If nothing new or changed, return [].
        """;

    public const string EntityExtractionUserPrompt = """
        Exchange to analyze:
        Player: I slip the rusted key into my pocket and ask the innkeeper about the basement whispers.
        Narrator: Marta pales. "Nobody goes down there since the flood." She slides a candle across the bar.

        Existing entities (names only): Marta, The Crooked Lantern Inn
        Extract new or updated entities from this exchange only.
        """;

    public const string PronounTrackingSystemPrompt = """
        You are a coreference resolver for interactive fiction.
        Your job is to track who or what each pronoun and ambiguous noun phrase refers to in a passage.

        Cast rules:
        - Use exact names from the provided cast list when a referent is clear.
        - Player first-person ("I", "me", "my", "mine", "we" when meaning the player party led by the player) → refersTo: "Player".
        - If a pronoun could point to more than one cast member, list it under "ambiguous" instead of guessing.
        - Include possessive pronouns (her, his, their) and demonstratives (she, he, they, this, that) when they pick out a referent.

        Respond with JSON only — a single object, no markdown fences or commentary:
        {
          "referents": [
            {
              "span": "she",
              "quote": "short phrase containing the pronoun",
              "refersTo": "Character Name or Player",
              "confidence": "high" | "medium" | "low"
            }
          ],
          "ambiguous": [
            {
              "span": "they",
              "quote": "short phrase",
              "candidates": ["Name A", "Name B"],
              "note": "why ambiguous"
            }
          ]
        }

        If there are no pronouns or ambiguities worth recording, return { "referents": [], "ambiguous": [] }.
        """;

    public const string PronounTrackingUserPrompt = """
        Cast:
        - Player (the protagonist)
        - Marta (innkeeper, woman)
        - Sister Caldra (traveling healer, woman)
        - Tomás (dockhand, man)

        Passage:
        Player: I nod to Marta and ask whether Sister Caldra has seen the symbol on the cellar door.
        Narrator: Marta wipes her hands on her apron. "She arrived at dawn, asked the same question, then left with Tomás." She lowers her voice. "He swore he'd never go back down there. They took the river path before I could stop them."

        Resolve pronouns and ambiguous referents in the Narrator lines only.
        """;

    public const string ProposeMemoriesSystemPrompt = """
        You propose discrete story events from a scoped play exchange for an interactive fiction adventure.
        Events are things that happened — not standing world-model definitions (those are entities).
        Respond with JSON only — a single array, no markdown fences or commentary.

        Each element:
        - text: string (required) — one sentence event summary
        - tags: string[] (optional)
        - pinned: boolean (optional; default false)
        - anchor: { pairOffset: number, playerHint: string } (optional)

        If nothing worth recording, return [].
        """;

    public const string ProposeMemoriesUserPrompt = """
        Exchange:
        Player: I show Marta the rusted key and ask what it unlocks.
        Narrator: Her eyes narrow. "That opens the old flood grate—not the basement door." She taps the bar twice, a signal you don't understand.

        Propose memory events for this exchange only.
        """;

    public const string UpdateSummarySystemPrompt = """
        You update the rolling story digest for interactive fiction.
        Respond with plain summary text only — no markdown fences, JSON, or commentary.
        Preserve major events, relationships, conflicts, and consequences.
        Write in third person past tense. Keep under 120 words unless the exchange is unusually dense.
        """;

    public const string UpdateSummaryUserPrompt = """
        Previous summary:
        The player reached The Crooked Lantern Inn and met Marta, who warned about basement whispers after a flood.

        New exchange:
        Player: I show Marta the rusted key and ask what it unlocks.
        Narrator: Her eyes narrow. "That opens the old flood grate—not the basement door." She taps the bar twice, a signal you don't understand.

        Update the rolling summary to include the new exchange.
        """;

    public const string ContinuityCheckSystemPrompt = """
        You are a continuity checker for interactive fiction.
        Compare the new exchange against canon facts and the rolling summary.
        Flag only genuine contradictions, impossibilities, or unexplained teleportation — not stylistic choices or mystery.

        Respond with JSON only — a single object, no markdown fences or commentary:
        {
          "ok": true | false,
          "issues": [
            {
              "severity": "warning" | "error",
              "code": "short_snake_case",
              "message": "human-readable explanation",
              "evidence": "quote or paraphrase"
            }
          ]
        }

        If nothing is wrong, return { "ok": true, "issues": [] }.
        """;

    public const string ContinuityCheckUserPrompt = """
        Canon facts:
        - The basement has been sealed since the flood three years ago.
        - Marta owns The Crooked Lantern Inn.
        - Sister Caldra is a healer, not a priest of the basement cult.

        Rolling summary:
        The player has a rusted key Marta says opens the flood grate, not the basement door.

        New exchange:
        Player: I ask Marta when she last opened the basement door herself.
        Narrator: "Last night," she says flatly. "Sister Caldra and I went down together to bless the latch."

        Check continuity.
        """;

    public const string JsonComplianceSystemPrompt = """
        You are a strict JSON emitter for pipeline testing.
        Always respond with exactly one JSON object matching this schema — no markdown, no prose before or after:
        {
          "status": "ok",
          "items": [
            { "id": "string", "value": number, "flags": ["string"] }
          ],
          "meta": { "count": number }
        }

        Set meta.count to items.length. Use plausible test data derived from the user message.
        """;

    public const string JsonComplianceUserPrompt = """
        Emit sample JSON describing three inventory slots mentioned in this line:
        I pocket the candle, the rusted key, and Sister Caldra's charm.
        """;

    public const string NarratorVoiceSystemPrompt = """
        You are a narrator for grounded, second-person interactive fiction.
        Write one paragraph of scene narration in response to the player's line.
        Rules:
        - Second person ("you") for the player character.
        - Concrete sensory detail; no purple prose.
        - Do not decide the player's emotions or inner monologue.
        - Do not offer choices or ask questions.
        - Maximum 90 words.
        """;

    public const string NarratorVoiceUserPrompt = """
        Player: I lean on the bar and keep my voice low. "Tell me what Sister Caldra was really looking for."
        """;

    private static readonly LocalInferenceLabScenario[] BuiltIn =
    [
        new()
        {
            Id = CustomId,
            Label = "Custom message",
            UserPrompt = "Say hello in one sentence.",
            Temperature = 0.7,
        },
        new()
        {
            Id = EntityExtractionId,
            Label = "Entity extraction",
            SystemPrompt = EntityExtractionSystemPrompt,
            UserPrompt = EntityExtractionUserPrompt,
            Temperature = 0.2,
        },
        new()
        {
            Id = PronounTrackingId,
            Label = "Pronoun / referent tracking",
            SystemPrompt = PronounTrackingSystemPrompt,
            UserPrompt = PronounTrackingUserPrompt,
            Temperature = 0.1,
        },
        new()
        {
            Id = ProposeMemoriesId,
            Label = "Propose memories",
            SystemPrompt = ProposeMemoriesSystemPrompt,
            UserPrompt = ProposeMemoriesUserPrompt,
            Temperature = 0.2,
        },
        new()
        {
            Id = UpdateSummaryId,
            Label = "Update rolling summary",
            SystemPrompt = UpdateSummarySystemPrompt,
            UserPrompt = UpdateSummaryUserPrompt,
            Temperature = 0.3,
        },
        new()
        {
            Id = ContinuityCheckId,
            Label = "Continuity check",
            SystemPrompt = ContinuityCheckSystemPrompt,
            UserPrompt = ContinuityCheckUserPrompt,
            Temperature = 0.1,
        },
        new()
        {
            Id = JsonComplianceId,
            Label = "JSON compliance stress",
            SystemPrompt = JsonComplianceSystemPrompt,
            UserPrompt = JsonComplianceUserPrompt,
            Temperature = 0,
        },
        new()
        {
            Id = NarratorVoiceId,
            Label = "Narrator voice (prose)",
            SystemPrompt = NarratorVoiceSystemPrompt,
            UserPrompt = NarratorVoiceUserPrompt,
            Temperature = 0.8,
        },
        ..LocalInferenceLabDiagnosticScenarios.All,
    ];

    public static IReadOnlyList<LocalInferenceLabScenario> All => BuiltIn;

    public static bool TryGet(string id, out LocalInferenceLabScenario scenario)
    {
        foreach (var item in BuiltIn)
        {
            if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                scenario = item;
                return true;
            }
        }

        scenario = null!;
        return false;
    }

    public static bool IsKnownUserPrompt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        foreach (var item in BuiltIn)
        {
            if (string.Equals(item.UserPrompt.Trim(), trimmed, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool IsDiagnosticScenario(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.StartsWith("diag-", StringComparison.OrdinalIgnoreCase);

    public static ChatCompletionRequest EntityExtractionDemo(string? model = null) =>
        ToRequest(BuiltIn.First(s => s.Id == EntityExtractionId), model);

    public static ChatCompletionRequest ToRequest(LocalInferenceLabScenario scenario, string? model = null)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(scenario.SystemPrompt))
            messages.Add(ChatMessage.System(scenario.SystemPrompt));
        if (!string.IsNullOrWhiteSpace(scenario.UserPrompt))
            messages.Add(ChatMessage.User(scenario.UserPrompt));

        return new ChatCompletionRequest
        {
            Model = model ?? LocalInferenceOptions.DefaultModel,
            Messages = messages,
            Temperature = scenario.Temperature,
            JsonObjectResponse = scenario.JsonObjectResponse,
        };
    }
}
