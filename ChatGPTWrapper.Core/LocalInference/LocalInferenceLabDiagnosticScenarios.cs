namespace ChatGPTWrapper.Core.LocalInference;

/// <summary>
/// Lab scenarios that audit pasted ChatGPT utility-worker proposals (Track B diagnostics).
/// Replace the sample CHATGPT RESPONSE block with output from review hub, flight recorder, or dual-run compare.
/// </summary>
public static class LocalInferenceLabDiagnosticScenarios
{
    public const string DiagEntityProposalsId = "diag-audit-entities";

    public const string DiagMemoryProposalsId = "diag-audit-memories";

    public const string DiagSummaryProposalId = "diag-audit-summary";

    public const string DiagProcessTurnBundleId = "diag-audit-process-turn";

    public const string DiagRawResponseSchemaId = "diag-raw-schema-check";

    /// <summary>Shared JSON report shape for all diagnostic scenarios.</summary>
    public const string DiagReportOutputContract = """
        Respond with JSON only — a single object, no markdown fences or commentary:
        {
          "jobId": "string — job under review",
          "verdict": "acceptable" | "needs_review" | "reject",
          "compliance": "compliant" | "schema_mismatch" | "parse_failed" | "empty",
          "scores": {
            "recall": 0-5,
            "correctness": 0-5,
            "specificity": 0-5
          },
          "proposalCounts": {
            "parsed": number,
            "actionable": number
          },
          "issues": [
            {
              "severity": "warning" | "error",
              "code": "short_snake_case",
              "message": "human-readable explanation",
              "evidence": "quote from response or exchange"
            }
          ],
          "missing": ["facts or referents that should appear but do not"],
          "extras": ["proposals that are redundant, wrong type, or unsupported by the exchange"],
          "notes": "one short paragraph for the author — what to accept, edit, or re-run"
        }

        Scoring guide:
        - recall: coverage of salient facts, referents, and state changes in the scoped exchange
        - correctness: names match cast/canon; entity vs memory distinction; no contradictions
        - specificity: anchors, outcomes, descriptions, and summary depth where the job expects them

        compliance rules:
        - compliant: payload parses and matches the job's expected top-level shape
        - schema_mismatch: parses as JSON but wrong shape (e.g. object vs array, wrong keys, prose instead of JSON)
        - parse_failed: not valid JSON after stripping fences/wrapper tags
        - empty: valid but no actionable proposals when the exchange clearly warrants some
        """;

    public const string DiagEntityProposalsSystemPrompt =
        """
        You are a utility proposal auditor for interactive fiction.
        You review ChatGPT worker output for the extract_entities job — you do NOT generate new entities.

        Expected worker response: a JSON array of objects, each with at least:
        - entityType: person | place | thing | faction | quest | concept
        - name: string (required)
        - description: string
        - action: create | update | noop (optional)

        Audit checklist:
        1. Schema — array of objects with required fields; events disguised as entities
        2. Recall — every new durable referent in the exchange appears (people, places, items, factions)
        3. Naming — names match surface forms in the exchange; no wrong characters; no duplicate near-matches
        4. Typing — entityType appropriate; plot beats belong in memories, not entities
        5. Updates — existing index entries updated instead of duplicated when clearly the same referent

        """ + "\n\n" + DiagReportOutputContract;

    public const string DiagEntityProposalsUserPrompt = """
        jobId: extract_entities

        === SCOPED EXCHANGE ===
        Player: I slip the rusted key into my pocket and ask the innkeeper about the basement whispers.
        Narrator: Marta pales. "Nobody goes down there since the flood." She slides a candle across the bar and nods toward a chalk mark on the floor — three linked circles.

        === EXISTING ENTITY INDEX (names only) ===
        Marta, The Crooked Lantern Inn

        === CHATGPT WORKER RESPONSE (replace with pasted output) ===
        [
          {
            "entityType": "thing",
            "name": "Rusted key",
            "description": "A corroded iron key the player pocketed; Marta may know what it opens.",
            "action": "create"
          },
          {
            "entityType": "concept",
            "name": "Basement whispers",
            "description": "Unexplained sounds from the inn basement since the flood.",
            "action": "create"
          },
          {
            "entityType": "thing",
            "name": "Candle",
            "description": "A bar candle Marta slid across the counter.",
            "action": "create"
          },
          {
            "entityType": "concept",
            "name": "Three linked circles",
            "description": "Chalk mark on the floor — symbol meaning unknown.",
            "action": "create"
          }
        ]

        Audit the ChatGPT response against the exchange and entity index.
        """;

    public const string DiagMemoryProposalsSystemPrompt =
        """
        You are a utility proposal auditor for interactive fiction.
        You review ChatGPT worker output for the propose_memories job — you do NOT generate new memories.

        Expected worker response: a JSON array of memory objects, each with at least:
        - text: string (required) — one discrete event sentence
        Optional: tags[], pinned, outcome, anchor object with pairOffset and playerHint

        Audit checklist:
        1. Schema — array of objects with non-empty text fields
        2. Recall — separate events for distinct beats (key shown, warning given, object exchanged, symbol noticed)
        3. Event vs entity — no standing world definitions; each line is something that happened
        4. Anchors — when the exchange is dense, good proposals include anchor hints tied to the player line
        5. Outcomes — consequential beats note result or state change when obvious

        """ + "\n\n" + DiagReportOutputContract;

    public const string DiagMemoryProposalsUserPrompt = """
        jobId: propose_memories

        === SCOPED EXCHANGE ===
        Player: I show Marta the rusted key and ask what it unlocks.
        Narrator: Her eyes narrow. "That opens the old flood grate—not the basement door." She taps the bar twice, a signal you don't understand. Behind her, Sister Caldra sets down a travel pack without a word.

        === EXISTING MEMORY TAGS (context only) ===
        flood, inn, Marta

        === CHATGPT WORKER RESPONSE (replace with pasted output) ===
        [
          {
            "text": "Marta identified the rusted key as opening the old flood grate, not the basement door.",
            "tags": ["key", "Marta", "flood-grate"],
            "anchor": { "pairOffset": 0, "playerHint": "showed Marta the rusted key" },
            "outcome": "Player learns the key target is the flood grate."
          },
          {
            "text": "Marta tapped the bar twice in an unexplained signal.",
            "tags": ["Marta", "inn"],
            "anchor": { "pairOffset": 0, "playerHint": "asked what it unlocks" }
          },
          {
            "text": "Sister Caldra arrived at the bar and set down a travel pack silently.",
            "tags": ["Sister Caldra"],
            "anchor": { "pairOffset": 0, "playerHint": "conversation with Marta" }
          }
        ]

        Audit the ChatGPT response against the exchange.
        """;

    public const string DiagSummaryProposalSystemPrompt =
        """
        You are a utility proposal auditor for interactive fiction.
        You review ChatGPT worker output for the update_summary job — you do NOT rewrite the summary.

        Expected worker response: plain summary text only (no JSON wrapper, no markdown fences).

        Audit checklist:
        1. compliance — prose digest, not JSON/array/object
        2. Recall — new exchange facts woven in (who, what changed, open questions)
        3. Depth — comparable detail to source material; not a one-line stub when exchange is rich
        4. Continuity — consistent with previous summary; no contradictions
        5. Style — third person past tense; no player-facing meta commentary

        For proposalCounts: parsed=1 if non-empty prose, actionable=1 if it would replace the rolling digest usefully.

        """ + "\n\n" + DiagReportOutputContract;

    public const string DiagSummaryProposalUserPrompt = """
        jobId: update_summary

        === PREVIOUS ROLLING SUMMARY ===
        The player reached The Crooked Lantern Inn and met Marta, who warned about basement whispers after a flood. The player carries a rusted key of unknown purpose.

        === NEW EXCHANGE ===
        Player: I show Marta the rusted key and ask what it unlocks.
        Narrator: Her eyes narrow. "That opens the old flood grate—not the basement door." She taps the bar twice, a signal you don't understand. Behind her, Sister Caldra sets down a travel pack without a word.

        === CHATGPT WORKER RESPONSE (replace with pasted output) ===
        The player showed Marta the rusted key at The Crooked Lantern Inn. Marta clarified that it opens the old flood grate rather than the sealed basement door, and tapped the bar twice in a gesture whose meaning remains unclear. Sister Caldra appeared and set down a travel pack without speaking, adding an unexplained presence to the scene. The player still lacks access to the basement proper, but now knows the key's true target and that others in the inn may communicate through coded signals.

        Audit the ChatGPT summary proposal.
        """;

    public const string DiagProcessTurnBundleSystemPrompt =
        """
        You are a utility proposal auditor for interactive fiction.
        You review ChatGPT worker output for the process_turn bundled job — you do NOT generate proposals.

        Expected worker response: a single JSON object with optional keys:
        - memories: array (same shape as propose_memories)
        - entities: array (same shape as extract_entities)
        - summary: string (same shape as update_summary)

        Audit each section independently, then holistically:
        1. Schema — one object; arrays under correct keys; summary is string not nested object
        2. Cross-type hygiene — no duplicate fact as both entity and memory; entities are referents, memories are events
        3. Recall — combined coverage across all three channels
        4. Naming — cast names consistent across entities, memories, and summary
        5. Actionability — would an author accept this bundle without heavy edits?

        In issues, prefix code with section when helpful (e.g. memories_under_recall, entities_wrong_name).

        """ + "\n\n" + DiagReportOutputContract;

    public const string DiagProcessTurnBundleUserPrompt = """
        jobId: process_turn

        === SCOPED EXCHANGE ===
        Player: I ask Marta whether Sister Caldra came for the symbol on the cellar door.
        Narrator: Marta wipes her hands on her apron. "She arrived at dawn, asked the same question, then left with Tomás." She lowers her voice. "He swore he'd never go back down there. They took the river path before I could stop them."

        === CAST / ENTITY INDEX ===
        Player, Marta, Sister Caldra, Tomás, The Crooked Lantern Inn, cellar door symbol (concept)

        === PREVIOUS SUMMARY (abbreviated) ===
        The player investigates a chalk symbol and a rusted key tied to the inn's flooded basement.

        === CHATGPT WORKER RESPONSE (replace with pasted output) ===
        {
          "memories": [
            {
              "text": "Marta reported Sister Caldra arrived at dawn asking about the cellar door symbol.",
              "tags": ["Sister Caldra", "symbol"],
              "anchor": { "pairOffset": 0, "playerHint": "asked about Sister Caldra and the symbol" }
            },
            {
              "text": "Sister Caldra left with Tomás via the river path before Marta could stop them.",
              "tags": ["Sister Caldra", "Tomás"],
              "outcome": "Both departed together toward the river."
            },
            {
              "text": "Marta said Tomás swore he would never go back down to the basement.",
              "tags": ["Tomás", "basement"]
            }
          ],
          "entities": [
            {
              "entityType": "concept",
              "name": "Cellar door symbol",
              "description": "Mark on the cellar door that Sister Caldra asked about.",
              "action": "update"
            },
            {
              "entityType": "place",
              "name": "River path",
              "description": "Route Sister Caldra and Tomás took when leaving the inn.",
              "action": "create"
            }
          ],
          "summary": "At The Crooked Lantern Inn, the player asked Marta about Sister Caldra's interest in the cellar door symbol. Marta revealed Caldra had already come at dawn with the same question, then departed with Tomás along the river path despite Marta's attempt to stop them. Marta added that Tomás had sworn never to return to the basement, deepening the mystery around who is willing to descend and why."
        }

        Audit the full ChatGPT process_turn bundle.
        """;

    public const string DiagRawResponseSchemaSystemPrompt =
        """
        You are a utility response schema checker for interactive fiction pipeline debugging.
        Given a jobId and raw model text (as returned by ChatGPT, including possible fences or wrapper tags),
        determine whether the app would accept it after JSON repair and normalization.

        Job shape reference:
        - extract_entities, propose_memories, expand_entity, bootstrap_sections, expand_section: JSON array
        - process_turn, continuity_check: JSON object
        - update_summary: plain text (not JSON)
        - bootstrap_lore, expand_story_card: JSON array of card objects

        Check:
        1. Wrapper noise — markdown fences, [[cgw:utility-response]] blocks, leading commentary
        2. JSON validity — would parse after stripping wrappers; note unescaped quotes in dialogue strings
        3. Top-level shape — array vs object vs prose matches jobId
        4. Minimum actionable fields — at least one element with required fields, or non-empty summary string

        Set compliance to schema_mismatch when shape is wrong even if JSON parses
        (e.g. bootstrap_sections returning a canon field sheet object instead of an entity array).

        """ + "\n\n" + DiagReportOutputContract;

    public const string DiagRawResponseSchemaUserPrompt = """
        jobId: bootstrap_sections

        === CHATGPT RAW RESPONSE (replace with pasted output — include fences if present) ===
        ```json
        {
          "canonFieldSheet": {
            "title": "Greyford Gate",
            "sections": [
              { "id": "locations", "entries": ["The Crooked Lantern Inn", "River path"] },
              { "id": "factions", "entries": ["Dock guild"] }
            ]
          }
        }
        ```

        Check whether this raw response matches the bootstrap_sections contract (entity proposal array).
        """;

    private static readonly LocalInferenceLabScenario[] AllScenarios =
    [
        new()
        {
            Id = DiagEntityProposalsId,
            Label = "Diag: audit entity proposals",
            SystemPrompt = DiagEntityProposalsSystemPrompt,
            UserPrompt = DiagEntityProposalsUserPrompt,
            Temperature = 0.1,
            JsonObjectResponse = true,
        },
        new()
        {
            Id = DiagMemoryProposalsId,
            Label = "Diag: audit memory proposals",
            SystemPrompt = DiagMemoryProposalsSystemPrompt,
            UserPrompt = DiagMemoryProposalsUserPrompt,
            Temperature = 0.1,
            JsonObjectResponse = true,
        },
        new()
        {
            Id = DiagSummaryProposalId,
            Label = "Diag: audit summary proposal",
            SystemPrompt = DiagSummaryProposalSystemPrompt,
            UserPrompt = DiagSummaryProposalUserPrompt,
            Temperature = 0.1,
            JsonObjectResponse = true,
        },
        new()
        {
            Id = DiagProcessTurnBundleId,
            Label = "Diag: audit process_turn bundle",
            SystemPrompt = DiagProcessTurnBundleSystemPrompt,
            UserPrompt = DiagProcessTurnBundleUserPrompt,
            Temperature = 0.1,
            JsonObjectResponse = true,
        },
        new()
        {
            Id = DiagRawResponseSchemaId,
            Label = "Diag: raw response schema check",
            SystemPrompt = DiagRawResponseSchemaSystemPrompt,
            UserPrompt = DiagRawResponseSchemaUserPrompt,
            Temperature = 0,
            JsonObjectResponse = true,
        },
    ];

    public static IReadOnlyList<LocalInferenceLabScenario> All => AllScenarios;
}
