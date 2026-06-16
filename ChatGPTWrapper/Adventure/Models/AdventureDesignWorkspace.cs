namespace ChatGPTWrapper.Adventure.Models;

public sealed class AdventureDesignWorkspace
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public AdventureDesignStep CurrentStep { get; set; } = AdventureDesignStep.Setup;

    public Dictionary<string, DesignStepState> Steps { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool LaunchBootstrapLore { get; set; } = true;

    public bool LaunchStartPlay { get; set; } = true;

    public Dictionary<string, DesignSourceFilePromptState> SourceFilesPrompted { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DesignSourceFilePromptState
{
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    public string? AssistantExcerpt { get; set; }
}

public sealed class DesignStepState
{
    public Dictionary<string, string> Fields { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string FreeformDraft { get; set; } = "";

    public DateTimeOffset? AcceptedAt { get; set; }

    public bool StepSeedSent { get; set; }

    public List<DesignChatMessage> ChatMessages { get; set; } = [];

    public List<DesignStepProposal> PendingProposals { get; set; } = [];
}

public sealed class DesignChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Role { get; set; } = "user";

    public string Text { get; set; } = "";

    public AdventureDesignStep Step { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DesignStepProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FieldKey { get; set; } = "";

    public string ProposedValue { get; set; } = "";

    public string? CurrentValue { get; set; }

    public string Rationale { get; set; } = "";

    public DesignProposalStatus Status { get; set; } = DesignProposalStatus.Pending;
}

public enum DesignProposalStatus
{
    Pending,
    Accepted,
    Rejected,
}
