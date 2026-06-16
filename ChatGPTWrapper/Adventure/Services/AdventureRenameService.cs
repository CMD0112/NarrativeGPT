using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureRenameService
{
    public static bool TryRename(AdventureBundle bundle, string newTitle, out string? error)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        error = null;

        var trimmed = string.IsNullOrWhiteSpace(newTitle) ? "Untitled adventure" : newTitle.Trim();
        if (string.Equals(trimmed, bundle.Metadata.Title, StringComparison.Ordinal))
            return true;

        bundle.Metadata.Title = trimmed;

        if (bundle.DesignWorkspace is not null
            || bundle.Metadata.Status == AdventureStatus.Designing)
        {
            AdventureDesignService.EnsureWorkspace(bundle);
            AdventureDesignService.SyncSetupFromMetadata(bundle);
        }

        AdventureStore.Save(bundle);
        return true;
    }
}
