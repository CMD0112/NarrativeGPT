using System.Text;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Canonical narrator instruction contract: boundaries, portrayal rules, and design/play sync.
/// See docs/instruction-sources-paradigm.md.
/// </summary>
internal static class InstructionContractService
{
    public const string GlobalBoundariesFieldKey = "globalBoundaries";
    public const string CharacterPortrayalFieldKey = "characterPortrayalRules";
    public const string InstructionAddendumFieldKey = "instructionAddendum";
    public const string LegacyNarratorBoundariesFieldKey = "narratorBoundaries";

    public const string InstructionsSnippetFile = "instructions-snippet.md";

    public static string BuildCanonicalInstructionsBody(AdventureBundle bundle) =>
        InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle);

    public static string BuildInstructionsSnippetFileContent(AdventureBundle bundle)
    {
        var title = bundle.Metadata.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = "Adventure";

        return $"# {title} - Instructions Snippet\n\n{BuildCanonicalInstructionsBody(bundle).Trim()}\n";
    }

    public static bool HasCanonicalInstructionsBody(AdventureBundle bundle) =>
        !string.IsNullOrWhiteSpace(BuildCanonicalInstructionsBody(bundle));

    public static bool GenerateInstructionsSnippetFile(AdventureBundle bundle) =>
        AdventureSourceFileService.TryWrite(
            bundle,
            InstructionsSnippetFile,
            BuildInstructionsSnippetFileContent(bundle),
            "instruction-generate");

    public static void ApplyDesignerFields(
        AdventureBundle bundle,
        string perspective,
        string tense,
        string detailLevel,
        string tone,
        string authorsNote,
        IReadOnlyList<string> globalBoundaries,
        IReadOnlyList<CharacterPortrayalRule> portrayalRules,
        string instructionAddendum,
        string difficulty,
        string violenceLevel)
    {
        var settings = bundle.Metadata.Settings;
        settings.Perspective = perspective.Trim();
        settings.Tense = tense.Trim();
        settings.DetailLevel = detailLevel.Trim();
        settings.Tone = tone.Trim();
        settings.Difficulty = difficulty.Trim();
        settings.ViolenceLevel = violenceLevel.Trim();
        bundle.Scenario.AuthorsNote = authorsNote.Trim();
        settings.ContentBoundaries = globalBoundaries
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();
        settings.CharacterPortrayalRules = portrayalRules
            .Where(r => !string.IsNullOrWhiteSpace(r.Subject) && !string.IsNullOrWhiteSpace(r.Rule))
            .ToList();
        settings.InstructionAddendum = instructionAddendum.Trim();
        HydrateDesignInstructionFields(bundle);
    }

    private static readonly Regex SectionHeaderRegex = new(
        @"^(Content boundaries|Character portrayal|Portrayal rules|Instruction addendum)\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static string BuildContractSections(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;
        var parts = new List<string>();

        if (settings.ContentBoundaries.Count > 0)
            parts.Add("Content boundaries:\n" + string.Join("\n", settings.ContentBoundaries));

        var portrayal = FormatCharacterPortrayalRules(settings.CharacterPortrayalRules);
        if (!string.IsNullOrWhiteSpace(portrayal))
            parts.Add("Character portrayal:\n" + portrayal);

        if (!string.IsNullOrWhiteSpace(settings.InstructionAddendum))
            parts.Add("Instruction addendum:\n" + settings.InstructionAddendum.Trim());

        return string.Join("\n\n", parts);
    }

    public static string BuildInstructionDomainCanonical(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;
        return string.Join("\n",
        [
            settings.Perspective ?? "",
            settings.Tense ?? "",
            settings.DetailLevel ?? "",
            settings.Tone ?? "",
            settings.Difficulty ?? "",
            settings.ViolenceLevel ?? "",
            string.Join("|", settings.ContentBoundaries),
            SerializeCharacterPortrayalRules(settings.CharacterPortrayalRules),
            settings.InstructionAddendum ?? "",
            bundle.Scenario.AuthorsNote ?? "",
        ]);
    }

    public static void HydrateDesignInstructionFields(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;

        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            "authorsNote",
            bundle.Scenario.AuthorsNote);

        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            GlobalBoundariesFieldKey,
            string.Join(Environment.NewLine, settings.ContentBoundaries));

        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            CharacterPortrayalFieldKey,
            SerializeCharacterPortrayalRules(settings.CharacterPortrayalRules));

        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            InstructionAddendumFieldKey,
            settings.InstructionAddendum);

        MigrateLegacyNarratorBoundariesField(bundle);
    }

    public static void ApplyFromDesignStep(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;

        bundle.Scenario.AuthorsNote = ReadDesignField(bundle, "authorsNote")
            ?? bundle.Scenario.AuthorsNote
            ?? "";

        settings.ContentBoundaries = ParseGlobalBoundaries(
            ReadDesignField(bundle, GlobalBoundariesFieldKey, null)
            ?? ReadDesignField(bundle, LegacyNarratorBoundariesFieldKey, null)) ?? [];

        settings.CharacterPortrayalRules = ParseCharacterPortrayalRules(
            ReadDesignField(bundle, CharacterPortrayalFieldKey, null)) ?? [];

        settings.InstructionAddendum = ReadDesignField(
            bundle,
            InstructionAddendumFieldKey,
            settings.InstructionAddendum) ?? "";
    }

    public static void ApplyFromSettings(AdventureBundle bundle)
    {
        ApplyFromDesignStep(bundle);
    }

    public static string BuildAuthorDefinedContractBlock(AdventureBundle bundle)
    {
        var authorsNote = EffectiveAuthorsNote(bundle);
        var globalBoundaries = EffectiveGlobalBoundaries(bundle);
        var portrayalRules = EffectiveCharacterPortrayalRules(bundle);
        var addendum = EffectiveInstructionAddendum(bundle);
        var tone = EffectiveTone(bundle);

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(authorsNote))
            sb.AppendLine("Author's note (style only): " + authorsNote.Trim());

        if (!string.IsNullOrWhiteSpace(tone))
            sb.AppendLine("Tone: " + tone.Trim());

        if (globalBoundaries.Count > 0)
        {
            sb.AppendLine("Global content boundaries (use exactly — do not invent others):");
            foreach (var line in globalBoundaries)
                sb.AppendLine("- " + line);
        }

        if (portrayalRules.Count > 0)
        {
            sb.AppendLine("Character / subject portrayal rules (use exactly — do not invent others):");
            foreach (var rule in portrayalRules)
                sb.AppendLine($"- {rule.Subject}: {rule.Rule}");
        }

        if (!string.IsNullOrWhiteSpace(addendum))
            sb.AppendLine("Instruction addendum:\n" + addendum.Trim());

        var text = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(text)
            ? "(No author-defined boundaries yet — leave boundary sections minimal or empty.)"
            : text;
    }

    public static string EffectiveAuthorsNote(AdventureBundle bundle) =>
        ReadDesignField(bundle, "authorsNote")
        ?? bundle.Scenario.AuthorsNote
        ?? "";

    public static string EffectiveTone(AdventureBundle bundle) =>
        ReadDesignField(bundle, AdventureDesignStep.Concept, "tone")
        ?? bundle.Metadata.Settings.Tone
        ?? bundle.Scenario.Tone
        ?? "";

    public static List<string> EffectiveGlobalBoundaries(AdventureBundle bundle)
    {
        if (bundle.Metadata.Settings.ContentBoundaries.Count > 0)
            return bundle.Metadata.Settings.ContentBoundaries;

        return ParseGlobalBoundaries(
            ReadDesignField(bundle, GlobalBoundariesFieldKey)
            ?? ReadDesignField(bundle, LegacyNarratorBoundariesFieldKey)) ?? [];
    }

    public static List<CharacterPortrayalRule> EffectiveCharacterPortrayalRules(AdventureBundle bundle)
    {
        if (bundle.Metadata.Settings.CharacterPortrayalRules.Count > 0)
            return bundle.Metadata.Settings.CharacterPortrayalRules;

        return ParseCharacterPortrayalRules(ReadDesignField(bundle, CharacterPortrayalFieldKey)) ?? [];
    }

    public static string EffectiveInstructionAddendum(AdventureBundle bundle) =>
        !string.IsNullOrWhiteSpace(bundle.Metadata.Settings.InstructionAddendum)
            ? bundle.Metadata.Settings.InstructionAddendum
            : ReadDesignField(bundle, InstructionAddendumFieldKey) ?? "";

    public static bool TryApplyFromInstructionsBody(AdventureBundle bundle, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return false;

        var changed = false;
        var body = StripMarkdownHeader(markdown);

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("Perspective:", StringComparison.OrdinalIgnoreCase))
            {
                bundle.Metadata.Settings.Perspective = line["Perspective:".Length..].Trim();
                changed = true;
            }
            else if (line.StartsWith("Tense:", StringComparison.OrdinalIgnoreCase))
            {
                bundle.Metadata.Settings.Tense = line["Tense:".Length..].Trim();
                changed = true;
            }
            else if (line.StartsWith("Detail:", StringComparison.OrdinalIgnoreCase))
            {
                bundle.Metadata.Settings.DetailLevel = line["Detail:".Length..].Trim();
                changed = true;
            }
            else if (line.StartsWith("Tone:", StringComparison.OrdinalIgnoreCase))
            {
                bundle.Metadata.Settings.Tone = line["Tone:".Length..].Trim();
                changed = true;
            }
            else if (line.StartsWith("Author's note", StringComparison.OrdinalIgnoreCase))
            {
                bundle.Scenario.AuthorsNote = line.Contains(':')
                    ? line[(line.IndexOf(':') + 1)..].Trim()
                    : line;
                changed = true;
            }
        }

        var sections = SplitInstructionSections(body);
        if (sections.TryGetValue("content boundaries", out var global) && !string.IsNullOrWhiteSpace(global))
        {
            bundle.Metadata.Settings.ContentBoundaries = ParseGlobalBoundaries(global) ?? [];
            changed = true;
        }

        if (sections.TryGetValue("character portrayal", out var portrayal) && !string.IsNullOrWhiteSpace(portrayal))
        {
            bundle.Metadata.Settings.CharacterPortrayalRules = ParseCharacterPortrayalRules(portrayal) ?? [];
            changed = true;
        }
        else if (sections.TryGetValue("portrayal rules", out portrayal) && !string.IsNullOrWhiteSpace(portrayal))
        {
            bundle.Metadata.Settings.CharacterPortrayalRules = ParseCharacterPortrayalRules(portrayal) ?? [];
            changed = true;
        }

        if (sections.TryGetValue("instruction addendum", out var addendum) && !string.IsNullOrWhiteSpace(addendum))
        {
            bundle.Metadata.Settings.InstructionAddendum = addendum.Trim();
            changed = true;
        }

        if (changed)
            HydrateDesignInstructionFields(bundle);

        return changed;
    }

    public static List<string>? ParseGlobalBoundaries(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '•', ' ').Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    public static List<CharacterPortrayalRule>? ParseCharacterPortrayalRules(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var rules = new List<CharacterPortrayalRule>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.TrimStart('-', '•', ' ').Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var subject = line[..separator].Trim();
            var rule = line[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(rule))
                continue;

            rules.Add(new CharacterPortrayalRule { Subject = subject, Rule = rule });
        }

        return rules;
    }

    public static string SerializeCharacterPortrayalRules(IEnumerable<CharacterPortrayalRule> rules) =>
        string.Join(
            Environment.NewLine,
            rules
                .Where(r => !string.IsNullOrWhiteSpace(r.Subject) && !string.IsNullOrWhiteSpace(r.Rule))
                .Select(r => $"{r.Subject}: {r.Rule}"));

    public static string FormatCharacterPortrayalRules(IEnumerable<CharacterPortrayalRule> rules) =>
        string.Join(
            "\n",
            rules
                .Where(r => !string.IsNullOrWhiteSpace(r.Subject) && !string.IsNullOrWhiteSpace(r.Rule))
                .Select(r => $"{r.Subject}: {r.Rule}"));

    private static void MigrateLegacyNarratorBoundariesField(AdventureBundle bundle)
    {
        var global = AdventureDesignService.GetField(bundle, AdventureDesignStep.Instructions, GlobalBoundariesFieldKey);
        if (!string.IsNullOrWhiteSpace(global))
            return;

        var legacy = AdventureDesignService.GetField(bundle, AdventureDesignStep.Instructions, LegacyNarratorBoundariesFieldKey);
        if (string.IsNullOrWhiteSpace(legacy))
            return;

        AdventureDesignService.SetField(bundle, AdventureDesignStep.Instructions, GlobalBoundariesFieldKey, legacy);
    }

    private static string? ReadDesignField(AdventureBundle bundle, string key) =>
        AdventureDesignService.GetField(bundle, AdventureDesignStep.Instructions, key) is { } value
        && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? ReadDesignField(AdventureBundle bundle, AdventureDesignStep step, string key) =>
        AdventureDesignService.GetField(bundle, step, key) is { } value
        && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? ReadDesignField(AdventureBundle bundle, string key, string? fallback) =>
        ReadDesignField(bundle, key) ?? fallback;

    private static string StripMarkdownHeader(string markdown)
    {
        var lines = markdown.Split('\n');
        var start = 0;
        if (lines.Length > 0 && lines[0].TrimStart().StartsWith('#'))
            start = 1;

        return string.Join(Environment.NewLine, lines.Skip(start)).Trim();
    }

    private static Dictionary<string, string> SplitInstructionSections(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = SectionHeaderRegex.Matches(body);
        if (matches.Count == 0)
            return result;

        for (var i = 0; i < matches.Count; i++)
        {
            var header = matches[i].Groups[1].Value.Trim().ToLowerInvariant();
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            var content = body[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(content))
                result[header] = content;
        }

        return result;
    }
}
