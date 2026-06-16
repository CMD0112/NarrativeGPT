using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SectionInjectionMigrationService
{
    public static bool TryMigrateIfNeeded(AdventureBundle bundle)
    {
        if (bundle.Metadata.SectionInjectionMigratedAt is not null)
            return false;

        if (bundle.Metadata.Settings.UseSectionInjection)
            return false;

        if (!bundle.Cards.Cards.Any(c => c.Enabled))
            return false;

        StoryCardMigrationService.Migrate(bundle);
        AdventureStore.Save(bundle);
        return true;
    }
}
