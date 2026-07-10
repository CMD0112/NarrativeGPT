using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureBootstrapService
{
    public static int AcceptedTurnCount(AdventureBundle bundle) =>
        PlayTurnScopeService.GetPacketAcceptedTurns(bundle).Count;

    public static bool IsFreshAdventure(AdventureBundle bundle) =>
        PlayTurnScopeService.IsFreshPlayThread(bundle);

    /// <summary>
    /// Legacy scenario opening text for Design / scenario.json. Not used as the start-packet player line.
    /// </summary>
    public static string GetOpeningPlayerLine(ScenarioDocument scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.OpeningSituation))
            return scenario.OpeningSituation.Trim();

        if (!string.IsNullOrWhiteSpace(scenario.Setting))
            return $"The story begins. {scenario.Setting.Trim()}";

        return "Begin the adventure.";
    }

    /// <summary>
    /// Player line for turn 1 on a fresh narrative thread. Directs the model to read all sources and
    /// supply the opening scene as its reply.
    /// </summary>
    public static string BuildStartPlayerDirective(AdventureBundle bundle)
    {
        const string intro = """
            Start the story. Before you narrate, retrieve and review every adventure source file listed below — each is relevant for opening the narrative.
            Open with vivid in-character narration that establishes the scene and situation from that canon.
            Do not ask the player questions yet — set the stage.
            Your reply is the opening scene.
            """;

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        if (readiness.CanDelegateStaticContent
            && bundle.Metadata.Settings.UseSectionInjection
            && readiness.SyncedFiles.Count > 0)
        {
            // Section-injection v2: ALWAYS RETRIEVE pointers in the context block carry retrieval intent.
            return intro;
        }

        if (readiness.CanDelegateStaticContent && readiness.SyncedFiles.Count > 0)
        {
            var lines = readiness.SyncedFiles.Select(f => $"- {f.RelativePath}");
            return intro
                   + Environment.NewLine
                   + Environment.NewLine
                   + "Project sources to retrieve:"
                   + Environment.NewLine
                   + string.Join(Environment.NewLine, lines);
        }

        var local = ListNarrativeStartSourceFileNames(bundle);
        if (local.Count > 0)
        {
            return intro
                   + Environment.NewLine
                   + Environment.NewLine
                   + "Adventure source files:"
                   + Environment.NewLine
                   + string.Join(Environment.NewLine, local.Select(f => $"- {f}"));
        }

        return intro
               + Environment.NewLine
               + Environment.NewLine
               + "Adventure source files: scenario.md, world.md, plot.md, cast.md, lexicon.md (retrieve all that exist in the Project).";
    }

    internal static IReadOnlyList<string> ListNarrativeStartSourceFileNames(AdventureBundle bundle)
    {
        var files = new List<string>();
        foreach (var path in NarrativeStartSourcePaths())
        {
            if (AdventureSourceFileService.TryRead(bundle, path) is not null
                || bundle.SourceManifest.Entries.Any(e =>
                    string.Equals(e.RelativePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                files.Add(path);
            }
        }

        return files;
    }

    internal static IEnumerable<string> NarrativeStartSourcePaths() =>
        SectionSchema.CoreLoreFiles.Append(SectionSchema.LexiconFile);

    public static string BuildStartPacket(AdventureBundle bundle)
    {
        var prompt = BuildStartPlayerDirective(bundle);
        return PromptPacketBuilder.Build(
            bundle,
            prompt,
            packetTurnIndexOverride: 1,
            freshNarrativeBootstrap: true).Text;
    }

    public static string BuildHandoffPacket(
        AdventureBundle bundle,
        PlayHandoffSnapshot snapshot,
        PlayHandoffOptions options) =>
        PlayHandoffService.BuildHandoffPacket(bundle, snapshot, options);
}
