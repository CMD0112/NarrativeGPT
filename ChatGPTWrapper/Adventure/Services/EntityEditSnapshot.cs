using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Point-in-time entity field values for dirty detection (excludes phrase highlights — chrome settings).</summary>
public sealed class EntityEditSnapshot
{
    public required string Name { get; init; }

    public required string SecondaryValue { get; init; }

    public required string Description { get; init; }

    public required bool Pinned { get; init; }

    public required string TagsText { get; init; }

    public required string AliasesText { get; init; }

    public required QuestStatus QuestStatus { get; init; }

    public required string ImagePath { get; init; }

    public required string? PendingImageSourcePath { get; init; }

    public required bool ClearImage { get; init; }

    public required IReadOnlyDictionary<string, string> FieldValues { get; init; }

    public static EntityEditSnapshot Capture(EntityEditModel model) =>
        new()
        {
            Name = model.Name.Trim(),
            SecondaryValue = model.SecondaryValue.Trim(),
            Description = model.Description.Trim(),
            Pinned = model.Pinned,
            TagsText = model.TagsText.Trim(),
            AliasesText = model.AliasesText.Trim(),
            QuestStatus = model.QuestStatus,
            ImagePath = model.ImagePath.Trim(),
            PendingImageSourcePath = model.PendingImageSourcePath,
            ClearImage = model.ClearImage,
            FieldValues = model.Fields.ToDictionary(
                f => f.Key,
                f => f.Value,
                StringComparer.OrdinalIgnoreCase),
        };

    public bool Matches(EntityEditModel model)
    {
        if (!string.Equals(Name, model.Name.Trim(), StringComparison.Ordinal))
            return false;
        if (!string.Equals(SecondaryValue, model.SecondaryValue.Trim(), StringComparison.Ordinal))
            return false;
        if (!string.Equals(Description, model.Description.Trim(), StringComparison.Ordinal))
            return false;
        if (Pinned != model.Pinned)
            return false;
        if (!string.Equals(TagsText, model.TagsText.Trim(), StringComparison.Ordinal))
            return false;
        if (!string.Equals(AliasesText, model.AliasesText.Trim(), StringComparison.Ordinal))
            return false;
        if (model.ShowQuestStatus && QuestStatus != model.QuestStatus)
            return false;
        if (!string.Equals(ImagePath, model.ImagePath.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(PendingImageSourcePath, model.PendingImageSourcePath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (ClearImage != model.ClearImage)
            return false;

        foreach (var field in model.Fields)
        {
            if (!FieldValues.TryGetValue(field.Key, out var prior)
                || !string.Equals(prior, field.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return FieldValues.Count == model.Fields.Count;
    }
}
