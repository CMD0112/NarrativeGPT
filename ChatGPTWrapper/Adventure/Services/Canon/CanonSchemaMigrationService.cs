using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonSchemaMigrationService
{
    public const int CurrentCanonSchemaVersion = 1;

    public static bool Migrate(AdventureMetadata metadata)
    {
        if (metadata.CanonSchemaVersion >= CurrentCanonSchemaVersion)
            return false;

        metadata.CanonSchemaVersion = CurrentCanonSchemaVersion;
        return true;
    }
}
