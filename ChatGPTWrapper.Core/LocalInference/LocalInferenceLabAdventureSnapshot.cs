namespace ChatGPTWrapper.Core.LocalInference;

/// <summary>Catalog entry for a wrapper-hosted adventure file the lab can attach verbatim.</summary>
public sealed class LocalInferenceLabAttachableFileSpec
{
    public required string Id { get; init; }

    public required string RelativePath { get; init; }

    public required string Label { get; init; }

    public bool DefaultForDiagnostic { get; init; }
}

/// <summary>One file's on-disk contents attached to a lab prompt.</summary>
public sealed class LocalInferenceLabFileAttachment
{
    public required string RelativePath { get; init; }

    public required string Content { get; init; }

    public int ByteLength { get; init; }
}

/// <summary>Verbatim adventure files loaded for diagnostic prompts.</summary>
public sealed class LocalInferenceLabAdventureAttachments
{
    public Guid AdventureId { get; init; }

    public string AdventureTitle { get; init; } = "";

    public string DirectoryPath { get; init; } = "";

    public string? JobId { get; init; }

    public Guid? UtilityRunId { get; init; }

    public int? TurnIndex { get; init; }

    public IReadOnlyList<LocalInferenceLabFileAttachment> Files { get; init; } = [];

    public int TotalCharacters => Files.Sum(f => f.Content.Length);
}

public sealed class LocalInferenceLabAdventureRef
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public DateTimeOffset LastPlayedAt { get; init; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Title) ? Id.ToString("N") : Title;
}

public sealed class LocalInferenceLabTurnRef
{
    public required int Index { get; init; }

    public required string Preview { get; init; }

    public string DisplayLabel => $"#{Index} · {Preview}";
}

public sealed class LocalInferenceLabUtilityRunRef
{
    public required Guid RunId { get; init; }

    public required string JobId { get; init; }

    public int? TurnIndex { get; init; }

    public required string Lane { get; init; }

    public int ProposalCount { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public string DisplayLabel
    {
        get
        {
            var turn = TurnIndex is int ti ? $"turn {ti}" : "no turn";
            var err = string.IsNullOrWhiteSpace(Error) ? "" : $" · {Error}";
            return $"{CapturedAt:yyyy-MM-dd HH:mm} · {JobId} · {turn} · {Lane} · {ProposalCount} props{err}";
        }
    }
}

public static class LocalInferenceLabAdventureFileIds
{
    public const string Entities = "entities";
    public const string Memory = "memory";
    public const string Summary = "summary";
    public const string State = "state";
    public const string Cards = "cards";
    public const string Continuity = "continuity";
    public const string LogFull = "log-full";
    public const string AdventureMeta = "adventure-meta";
}
