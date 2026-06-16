using System.Text;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class AdventureDesignFinalizeResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public int CharactersAdded { get; init; }

    public bool SourcesExported { get; init; }
}

internal static class AdventureDesignFinalizeService
{
    public static AdventureDesignFinalizeResult Finalize(AdventureBundle bundle)
    {
        if (bundle.Metadata.Status != AdventureStatus.Designing)
            return new AdventureDesignFinalizeResult { Success = false, Error = "not_designing" };

        AdventureDesignService.ApplySetupToMetadata(bundle);
        ApplyConcept(bundle);
        ApplyWorld(bundle);
        ApplyPlot(bundle);
        ApplyInstructions(bundle);
        ApplyLexicon(bundle);

        var charactersAdded = ApplyCast(bundle);
        ApplySourcesOutline(bundle);

        bundle.Metadata.Status = AdventureStatus.Active;
        bundle.Metadata.ScenarioSummary =
            bundle.Scenario.OpeningSituation
            ?? AdventureDesignService.GetField(bundle, AdventureDesignStep.Concept, "openingSituation")
            ?? "";

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.Genre))
            bundle.Metadata.Genre = bundle.Scenario.Genre;

        var sourcesExported = ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        return new AdventureDesignFinalizeResult
        {
            Success = true,
            CharactersAdded = charactersAdded,
            SourcesExported = sourcesExported,
        };
    }

    private static void ApplyConcept(AdventureBundle bundle)
    {
        var s = bundle.Scenario;
        s.Setting = Field(bundle, AdventureDesignStep.Concept, "setting", s.Setting);
        s.PlayerRole = Field(bundle, AdventureDesignStep.Concept, "playerRole", s.PlayerRole);
        s.Genre = Field(bundle, AdventureDesignStep.Concept, "genre", s.Genre);
        s.Tone = Field(bundle, AdventureDesignStep.Concept, "tone", s.Tone);
        s.OpeningSituation = Field(bundle, AdventureDesignStep.Concept, "openingSituation", s.OpeningSituation);
    }

    private static void ApplyWorld(AdventureBundle bundle)
    {
        var s = bundle.Scenario;
        s.WorldRules = Field(bundle, AdventureDesignStep.World, "worldRules", s.WorldRules);
        s.StartingConstraints = Field(bundle, AdventureDesignStep.World, "startingConstraints", s.StartingConstraints);
    }

    private static void ApplyPlot(AdventureBundle bundle)
    {
        var s = bundle.Scenario;
        s.PlotEssentials = Field(bundle, AdventureDesignStep.Plot, "plotEssentials", s.PlotEssentials);
        s.MajorConflicts = Field(bundle, AdventureDesignStep.Plot, "majorConflicts", s.MajorConflicts);
    }

    private static void ApplyInstructions(AdventureBundle bundle) =>
        InstructionContractService.ApplyFromDesignStep(bundle);

    private static void ApplyLexicon(AdventureBundle bundle)
    {
        var s = bundle.Scenario;
        s.LexiconRules = Field(bundle, AdventureDesignStep.Lexicon, "lexiconRules", s.LexiconRules);
        s.LexiconPools = Field(bundle, AdventureDesignStep.Lexicon, "lexiconPools", s.LexiconPools);
        s.LexiconAvoid = Field(bundle, AdventureDesignStep.Lexicon, "lexiconAvoid", s.LexiconAvoid);
    }

    private static int ApplyCast(AdventureBundle bundle)
    {
        var notes = AdventureDesignService.GetField(bundle, AdventureDesignStep.Cast, "castNotes") ?? "";
        if (string.IsNullOrWhiteSpace(notes))
            return 0;

        var added = 0;
        foreach (var line in notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(line, @"^\-\s*\*\*(.+?)\*\*\s*\((.+?)\):\s*(.+)$");
            if (!match.Success)
                continue;

            var name = match.Groups[1].Value.Trim();
            var role = match.Groups[2].Value.Trim();
            var description = match.Groups[3].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (bundle.Entities.Characters.Any(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            bundle.Entities.Characters.Add(new CharacterEntry
            {
                Name = name,
                Role = role,
                Description = description,
            });
            added++;
        }

        return added;
    }

    private static void ApplySourcesOutline(AdventureBundle bundle)
    {
        var outline = AdventureDesignService.GetField(bundle, AdventureDesignStep.Sources, "sourceOutline");
        if (string.IsNullOrWhiteSpace(outline))
            return;

        foreach (var section in SplitSourceSections(outline))
        {
            if (string.IsNullOrWhiteSpace(section.Path) || string.IsNullOrWhiteSpace(section.Content))
                continue;

            SourceSynthesisService.WriteSynthesizedFile(bundle, section.Path, section.Content);
        }
    }

    private static IEnumerable<(string Path, string Content)> SplitSourceSections(string outline)
    {
        var pattern = new Regex(@"^###\s+(.+\.md)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var matches = pattern.Matches(outline);
        if (matches.Count == 0)
            yield break;

        for (var i = 0; i < matches.Count; i++)
        {
            var path = matches[i].Groups[1].Value.Trim().Replace('\\', '/');
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : outline.Length;
            var content = outline[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(content))
                yield return (path, content);
        }
    }

    private static string Field(AdventureBundle bundle, AdventureDesignStep step, string key, string fallback)
    {
        var value = AdventureDesignService.GetField(bundle, step, key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public static string BuildReviewSummary(AdventureBundle bundle)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {bundle.Metadata.Title}");
        sb.AppendLine();

        foreach (var step in AdventureDesignService.OrderedSteps.Where(s => s != AdventureDesignStep.Review))
        {
            sb.AppendLine($"## {AdventureDesignService.GetStepDisplayName(step)}");
            sb.AppendLine(AdventureDesignService.BuildStepDraftSummary(bundle, step));
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }
}
