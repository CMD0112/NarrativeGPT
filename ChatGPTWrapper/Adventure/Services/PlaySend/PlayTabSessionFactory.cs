using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal static class PlayTabSessionFactory
{
    public static PlayTabSession FromBundle(
        AdventureBundle bundle,
        SessionHealth health = SessionHealth.Ready)
    {
        var profile = ResolveDefaultProfile(bundle);
        return new PlayTabSession(
            bundle.Metadata.Id,
            PlayTabPinService.GetPlayPinKey(bundle),
            PlayThreadBindingService.GetActiveConversationId(bundle),
            AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata),
            profile,
            health);
    }

    private static PlayAutomationProfile ResolveDefaultProfile(AdventureBundle bundle)
    {
        if (ProjectChatDraftService.IsActive(bundle.Metadata.Id))
        {
            return ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id) switch
            {
                ProjectChatDraftKind.Play => PlayAutomationProfile.DraftProjectOnly,
                ProjectChatDraftKind.Utility or ProjectChatDraftKind.Design => PlayAutomationProfile.Disabled,
                _ => PlayAutomationProfile.Full,
            };
        }

        return PlayAutomationProfile.Full;
    }
}
