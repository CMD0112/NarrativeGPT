using System.Text.Json;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonSchemaExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Export(CanonSchemaCatalog catalog) =>
        JsonSerializer.Serialize(ToDocument(catalog), JsonOptions);

    public static CanonSchemaDocument ToDocument(CanonSchemaCatalog catalog) =>
        new()
        {
            SchemaVersion = catalog.SchemaVersion,
            Kinds = catalog.AllKinds.Select(ToKindDocument).ToList(),
        };

    private static CanonKindDocument ToKindDocument(CanonEntityKindSpec kind) =>
        new()
        {
            KindId = kind.KindId,
            CollectionKey = kind.CollectionKey,
            SectionId = kind.SectionId,
            SourceFile = kind.SourceFile,
            UiCategory = kind.UiCategory,
            TypeLabel = kind.TypeLabel,
            ShowInPlayGrid = kind.ShowInPlayGrid,
            IsSingleton = kind.IsSingleton,
            TitleProperty = kind.TitleProperty,
            SecondaryProperty = kind.SecondaryProperty,
            SnippetProperty = kind.SnippetProperty,
            Fields = kind.Fields.Select(ToFieldDocument).ToList(),
        };

    private static CanonFieldDocument ToFieldDocument(CanonFieldSpec field) =>
        new()
        {
            Label = field.Label,
            JsonKey = field.JsonKey,
            Format = field.Format.ToString(),
            Role = field.Role.ToString(),
            Multiline = field.Multiline,
            ControlType = field.ControlType.ToString(),
            AlternateLabels = field.AlternateLabels.ToList(),
        };
}
