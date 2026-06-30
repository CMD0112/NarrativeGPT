using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PromptHistoryMigration
{
    public const int CurrentSchemaVersion = 2;

    public static bool Migrate(PromptHistoryDocument document)
    {
        if (document.SchemaVersion >= CurrentSchemaVersion)
            return false;

        document.SchemaVersion = CurrentSchemaVersion;

        foreach (var entry in document.Entries)
        {
            if (entry.Kind == default)
                entry.Kind = FlightRecordKind.PlaySend;
        }

        return true;
    }

    public static void EnsureCurrentSchema(PromptHistoryDocument document)
    {
        if (document.SchemaVersion < CurrentSchemaVersion)
            document.SchemaVersion = CurrentSchemaVersion;
    }
}
