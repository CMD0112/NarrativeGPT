namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityReferencePanelOptions
{
    public bool ShowPinToggle { get; init; } = true;

    public bool ShowAiActions { get; init; } = true;

    public bool ShowMoreMenu { get; init; } = true;

    public IReadOnlyList<string>? CategoryFilters { get; init; }

    public string DefaultFilter { get; init; } = "Characters";

    public bool PromptCanonReconcile { get; init; } = true;

    public bool PromptRenameWizard { get; init; } = true;

    public EntityReferenceEditMode EditMode { get; init; } = EntityReferenceEditMode.Modal;
}
