using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Refinement-only design prompts for instructions-snippet.md (CMD-23).
/// AI tightens the author-assembled canonical body — does not invent from lore.
/// </summary>
internal static class InstructionRefinementPromptService
{
    public static string BuildRefinementPrompt(AdventureBundle bundle, string? refinementRequest = null)
    {
        var title = bundle.Metadata.Title;
        var prefixedName = AdventureDesignSourcePromptService.BuildPrefixedFileName(
            title,
            InstructionContractService.InstructionsSnippetFile);
        var prefixedPath = AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
            title,
            InstructionContractService.InstructionsSnippetFile);
        var canonical = InstructionContractService.BuildInstructionsSnippetFileContent(bundle);
        var notes = string.IsNullOrWhiteSpace(refinementRequest)
            ? "(none — improve clarity and flow only; do not change meaning.)"
            : refinementRequest.Trim();

        return $"""
            === DELIVERABLE: refine instructions-snippet.md ===
            Adventure: {title}

            {AdventureDesignSourcePromptService.BuildAdventureTitleBlockForPrompt(title)}

            **Task — refinement only**
            You are refining an **author-assembled narrator instruction contract**. This is not world lore.
            The canonical manual version is below. Your job is to improve wording and clarity **without changing meaning or adding content**.

            **Author refinement request**
            {notes}

            **Rules (mandatory)**
            - Preserve every boundary, portrayal rule, and constraint — same meaning, clearer prose if needed.
            - Do **not** add new content boundaries, portrayal rules, plot facts, character framing, or world lore.
            - Do **not** remove or soften any boundary or portrayal rule unless the author request explicitly asks.
            - Keep these lines verbatim (same meaning, minor punctuation OK):
              - "You are the narrator for an interactive fiction adventure in this Project."
              - "Use uploaded project sources as canonical world material."
              - "Do not break character or mention being an AI."
            - Keep `Perspective:`, `Tense:`, `Detail:` values unchanged.
            - Do not repeat cast, scenario, world, or plot source material.
            - No JSON, no meta commentary about the instructions.

            **DELIVERABLE — one Project source file**
            - File: `{prefixedName}` (`{prefixedPath}`)
            - Purpose: RAG mirror of the narrator contract (refined wording only)

            **Two-part response (both required)**
            1. **Downloadable file** — `{prefixedName}` exactly.
            2. **Inline block** — identical markdown:
               ```
               --- begin {prefixedName} ---
               (full refined file)
               --- end {prefixedName} ---
               ```

            **CANONICAL MANUAL VERSION (refine this — do not replace with new content)**
            ```
            {canonical.Trim()}
            ```

            Deliver the refined `{prefixedPath}` now.
            """;
    }
}
