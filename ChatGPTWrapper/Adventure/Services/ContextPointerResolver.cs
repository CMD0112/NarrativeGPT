using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ContextPointerResolver
{
    public static ContextResolveResult Resolve(
        AdventureBundle bundle,
        ContextSignalBag signals,
        bool fatFallback)
    {
        var index = new SectionAliasIndex(bundle);
        var candidates = new Dictionary<string, ContextPointer>(StringComparer.OrdinalIgnoreCase);

        void Add(ContextPointer pointer)
        {
            if (candidates.TryGetValue(pointer.MachineId, out var existing))
            {
                if (pointer.Score > existing.Score)
                    candidates[pointer.MachineId] = pointer;
                return;
            }

            candidates[pointer.MachineId] = pointer;
        }

        foreach (var baselinePointer in BuildBaseline(bundle, signals))
            Add(baselinePointer);

        foreach (var indexed in index.All.Where(i => i.Section.Pinned))
        {
            Add(MakePointer(indexed, 40, PointerSource.Pin, signals));
        }

        if (!string.IsNullOrWhiteSpace(signals.StateLocation))
        {
            foreach (var indexed in index.MatchAlias(signals.StateLocation!))
            {
                if (string.Equals(indexed.Section.Kind, "place", StringComparison.OrdinalIgnoreCase))
                    Add(MakePointer(indexed, 35, PointerSource.State, signals));
            }
        }

        var combined = signals.PlayerText + " " + signals.SummaryText;
        foreach (var indexed in index.MatchAlias(combined))
        {
            var inPlayer = indexed.Section.Aliases.Any(a => SectionSlugHelper.ContainsToken(signals.PlayerText, a))
                           || SectionSlugHelper.ContainsToken(signals.PlayerText, indexed.Section.Title);
            var score = inPlayer ? 35 : 15;
            Add(MakePointer(indexed, score, PointerSource.NameMatch, signals));
        }

        foreach (var entry in bundle.ContextIndex.Entries)
        {
            var triggerHit = entry.Triggers.Any(t =>
                SectionSlugHelper.ContainsToken(signals.PlayerText, t));
            var summaryHit = !triggerHit && entry.Triggers.Any(t =>
                SectionSlugHelper.ContainsToken(signals.SummaryText, t));
            if (!triggerHit && !summaryHit)
                continue;

            var indexed = ResolveTarget(index, entry.Target);
            if (indexed is null)
                continue;

            Add(MakePointer(indexed, triggerHit ? 25 : 10, PointerSource.Trigger, signals));
        }

        if (!string.IsNullOrWhiteSpace(signals.AttachmentTokens))
        {
            foreach (var indexed in index.MatchAlias(signals.AttachmentTokens))
                Add(MakePointer(indexed, 20, PointerSource.Attachment, signals));
        }

        var filtered = candidates.Values
            .Where(p => p.Score >= ContextRenderPolicy.ScoreThreshold || p.Source == PointerSource.Baseline)
            .ToList();

        filtered = DedupParents(filtered);
        filtered = ApplyPersonCluster(filtered);
        filtered = filtered.OrderByDescending(p => p.Score).ThenBy(p => p.MachineId, StringComparer.Ordinal).ToList();

        foreach (var p in filtered)
            p.Mode = ContextRenderPolicy.PickRenderMode(p, fatFallback);

        var baseline = filtered.Where(p => p.Source == PointerSource.Baseline).ToList();
        var thisTurn = filtered.Where(p => p.Source != PointerSource.Baseline).ToList();

        return new ContextResolveResult
        {
            Baseline = baseline,
            ThisTurn = thisTurn,
            All = filtered,
        };
    }

    private static List<ContextPointer> BuildBaseline(AdventureBundle bundle, ContextSignalBag signals)
    {
        var list = new List<ContextPointer>();
        var index = new SectionAliasIndex(bundle);

        void TryBaseline(string file, string sectionId)
        {
            var indexed = index.All.FirstOrDefault(i =>
                string.Equals(i.FileName, file, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Section.Id, sectionId, StringComparison.OrdinalIgnoreCase));
            if (indexed is null)
                return;

            if (sectionId == "opening" && !IncludeOpening(bundle, signals, indexed.Section))
                return;
            if (sectionId == "rules" && !IncludeRules(signals, indexed.Section))
                return;

            list.Add(MakePointer(indexed, 100, PointerSource.Baseline, signals));
        }

        TryBaseline(SectionSchema.ScenarioFile, "opening");
        TryBaseline(SectionSchema.WorldFile, "rules");
        TryBaseline(SectionSchema.CastFile, "player");

        return list;
    }

    private static bool IncludeOpening(AdventureBundle bundle, ContextSignalBag signals, SectionManifestEntry section)
    {
        if (signals.AcceptedTurnCount < 3)
            return true;

        if (signals.SummaryText.Length < 200)
            return true;

        var phrase = section.KeyPhrase?.ToLowerInvariant();
        return string.IsNullOrWhiteSpace(phrase)
               || !signals.SummaryText.Contains(phrase, StringComparison.Ordinal);
    }

    private static bool IncludeRules(ContextSignalBag signals, SectionManifestEntry section)
    {
        if (signals.AcceptedTurnCount < 8)
            return true;

        var phrase = section.KeyPhrase?.ToLowerInvariant()
                     ?? section.BodyCache.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .FirstOrDefault()?.ToLowerInvariant();
        return string.IsNullOrWhiteSpace(phrase)
               || !signals.SummaryText.Contains(phrase, StringComparison.Ordinal);
    }

    private static ContextPointer MakePointer(
        IndexedSection indexed,
        int baseScore,
        PointerSource source,
        ContextSignalBag signals)
    {
        var score = baseScore;
        if (signals.AttachmentImageTurn
            && source is not PointerSource.Pin and not PointerSource.State and not PointerSource.Baseline)
            score = (int)(score * 0.75);

        return new ContextPointer
        {
            MachineId = indexed.MachineId,
            FileName = indexed.FileName,
            SectionId = indexed.Section.Id,
            Title = indexed.Section.Title,
            Kind = indexed.Section.Kind,
            Score = score,
            Source = source,
            BodyCache = indexed.Section.BodyCache,
        };
    }

    private static IndexedSection? ResolveTarget(SectionAliasIndex index, string target)
    {
        var hash = target.IndexOf('#');
        if (hash < 0)
            return null;

        var file = target[..hash];
        var sectionId = target[(hash + 1)..];
        return index.All.FirstOrDefault(i =>
            string.Equals(i.FileName, file, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.Section.Id, sectionId, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ContextPointer> DedupParents(List<ContextPointer> list)
    {
        var childParents = list
            .Select(p => p.SectionId.Contains('/') ? p.SectionId[..p.SectionId.IndexOf('/')] : null)
            .Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return list
            .Where(p => !(p.SectionId.IndexOf('/') < 0 && childParents.Contains(p.SectionId)))
            .ToList();
    }

    private static List<ContextPointer> ApplyPersonCluster(List<ContextPointer> list)
    {
        var persons = list
            .Where(p => string.Equals(p.Kind, "person", StringComparison.OrdinalIgnoreCase)
                        && p.Source != PointerSource.Baseline)
            .ToList();
        if (persons.Count < 4)
            return list;

        var cluster = new ContextPointer
        {
            MachineId = $"{SectionSchema.CastFile}#npcs",
            FileName = SectionSchema.CastFile,
            SectionId = "npcs",
            Title = "NPCs",
            Kind = "person",
            Score = persons.Max(p => p.Score),
            Source = PointerSource.Cluster,
            Mode = RenderMode.ClusterSummary,
            ClusterNames = persons.Select(p => p.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };

        var remove = persons.Select(p => p.MachineId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = list.Where(p => !remove.Contains(p.MachineId)).ToList();
        result.Add(cluster);
        return result;
    }
}
