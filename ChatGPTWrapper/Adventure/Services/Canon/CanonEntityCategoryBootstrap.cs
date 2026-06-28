namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonEntityCategoryBootstrap
{
    public static IReadOnlyList<CanonEntityCategorySpec> All =>
    [
        new()
        {
            CategoryId = CanonEntityCategoryRegistry.Cast,
            DisplayLabel = "Cast",
            ShowTags = true,
            ShowAliases = true,
            ShowImage = true,
            ListShellFields = CastListShellFields,
            SingletonShellFields = CastSingletonShellFields,
        },
        new()
        {
            CategoryId = CanonEntityCategoryRegistry.Place,
            DisplayLabel = "Place",
            ShowTags = false,
            ShowAliases = true,
            ShowImage = true,
            ListShellFields = PlaceListShellFields,
            SingletonShellFields = [],
        },
        new()
        {
            CategoryId = CanonEntityCategoryRegistry.Lore,
            DisplayLabel = "Lore",
            ShowTags = true,
            ShowAliases = false,
            ShowImage = true,
            ListShellFields = LoreListShellFields,
            SingletonShellFields = [],
        },
    ];

    private static readonly CanonFieldSpec[] CastListShellFields =
    [
        Shell("Id", "id"),
        Shell("Aliases", "aliases", CanonFieldGroup.Identity),
        Shell("Tags", "tags", CanonFieldGroup.Identity),
        Shell("ImagePath", "imagePath", CanonFieldGroup.Identity),
    ];

    private static readonly CanonFieldSpec[] CastSingletonShellFields =
    [
        Shell("Tags", "tags", CanonFieldGroup.Identity),
        Shell("Aliases", "aliases", CanonFieldGroup.Identity),
        Shell("ImagePath", "imagePath", CanonFieldGroup.Identity),
    ];

    private static readonly CanonFieldSpec[] PlaceListShellFields =
    [
        Shell("Id", "id"),
        Shell("Aliases", "aliases", CanonFieldGroup.Identity),
        Shell("ImagePath", "imagePath", CanonFieldGroup.Identity),
        Shell("Pinned", "pinned"),
    ];

    private static readonly CanonFieldSpec[] LoreListShellFields =
    [
        Shell("Id", "id"),
        Shell("Tags", "tags", CanonFieldGroup.Identity),
        Shell("ImagePath", "imagePath", CanonFieldGroup.Identity),
        Shell("Pinned", "pinned"),
    ];

    private static CanonFieldSpec Shell(string label, string jsonKey, string group = CanonFieldGroup.Identity) =>
        new()
        {
            Label = label,
            JsonKey = jsonKey,
            Format = CanonFieldFormat.PlainLine,
            Role = CanonFieldRole.Shell,
            FieldGroup = group,
        };
}
