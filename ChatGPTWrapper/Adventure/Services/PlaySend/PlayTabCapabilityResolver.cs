using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Normative play-tab capability matrix. See docs/play-send-orchestration-adr.md.
/// </summary>
internal static class PlayTabCapabilityResolver
{
    public static PlayTabCapabilities Resolve(PlayTabCapabilityContext ctx) =>
        Resolve(ctx, PlayTabSessionFactory.FromBundle(ctx.Bundle!));

    public static PlayTabCapabilities Resolve(PlayTabCapabilityContext ctx, PlayTabSession session)
    {
        if (ctx.Bundle is null || !ctx.IsPlayMode)
        {
            return Disabled(PlayDisarmReason.SessionDegraded);
        }

        if (session.Health is SessionHealth.Broken or SessionHealth.Degraded)
        {
            return Capabilities(
                PlayAutomationProfile.Disabled,
                acceptDraft: false,
                allowSend: false,
                allowNativeInput: true,
                channel: PlayDeliveryChannel.None,
                PlayDisarmReason.SessionDegraded);
        }

        if (ctx.ActiveDraftKind is ProjectChatDraftKind.Utility or ProjectChatDraftKind.Design
            && ctx.IsDraftTab)
        {
            return Disabled(PlayDisarmReason.DraftTab);
        }

        var bundle = ctx.Bundle;
        var source = ctx.SourceUrl;
        var linkedProject = !string.IsNullOrWhiteSpace(session.LinkedProjectId);
        var storedConversationId = session.ConversationId;
        var gizmoId = session.LinkedProjectId;

        if (!linkedProject)
        {
            return ResolveUnlinkedPlay(ctx, session);
        }

        if (!AdventureNavigationService.IsOnLinkedProjectPage(source, bundle)
            && !IsOnStoredPlayConversation(source, storedConversationId, gizmoId))
        {
            return Capabilities(
                PlayAutomationProfile.Disabled,
                acceptDraft: false,
                allowSend: false,
                allowNativeInput: true,
                channel: PlayDeliveryChannel.None,
                PlayDisarmReason.WrongUrl);
        }

        if (ctx.ActiveDraftKind == ProjectChatDraftKind.Play
            && string.IsNullOrWhiteSpace(storedConversationId))
        {
            return Capabilities(
                PlayAutomationProfile.DraftProjectOnly,
                acceptDraft: true,
                allowSend: true,
                allowNativeInput: false,
                channel: PlayDeliveryChannel.DomBootstrap,
                PlayDisarmReason.PlayRotationDraft);
        }

        if (string.IsNullOrWhiteSpace(storedConversationId))
        {
            return Capabilities(
                PlayAutomationProfile.Full,
                acceptDraft: false,
                allowSend: false,
                allowNativeInput: true,
                channel: PlayDeliveryChannel.None,
                PlayDisarmReason.NoLinkedProject);
        }

        if (!string.IsNullOrWhiteSpace(gizmoId)
            && AdventurePlayContextService.IsOnPlayConversationPage(
                source,
                storedConversationId,
                gizmoId))
        {
            return ResolveBoundPlayThread(ctx, session);
        }

        return Capabilities(
            PlayAutomationProfile.Full,
            acceptDraft: false,
            allowSend: false,
            allowNativeInput: true,
            channel: PlayDeliveryChannel.None,
            PlayDisarmReason.ProjectLanding);
    }

    private static PlayTabCapabilities ResolveBoundPlayThread(
        PlayTabCapabilityContext ctx,
        PlayTabSession session)
    {
        if (!session.HasPin)
        {
            return Capabilities(
                PlayAutomationProfile.Full,
                acceptDraft: true,
                allowSend: false,
                allowNativeInput: false,
                channel: PlayDeliveryChannel.None,
                PlayDisarmReason.NoPin);
        }

        if (!TabKeysEqual(ctx.CandidateTabKey, session.PinTabKey)
            && !IsCandidateOnPlayTarget(ctx))
        {
            return Capabilities(
                PlayAutomationProfile.Full,
                acceptDraft: false,
                allowSend: false,
                allowNativeInput: true,
                channel: PlayDeliveryChannel.None,
                PlayDisarmReason.WrongUrl);
        }

        return Capabilities(
            PlayAutomationProfile.Full,
            acceptDraft: true,
            allowSend: true,
            allowNativeInput: false,
            channel: PlayDeliveryChannel.Api,
            PlayDisarmReason.None);
    }

    private static PlayTabCapabilities ResolveUnlinkedPlay(
        PlayTabCapabilityContext ctx,
        PlayTabSession session)
    {
        if (!session.HasPin || !TabKeysEqual(ctx.CandidateTabKey, session.PinTabKey))
        {
            if (session.HasPin && IsCandidateOnPlayTarget(ctx))
            {
                return Capabilities(
                    PlayAutomationProfile.Full,
                    acceptDraft: true,
                    allowSend: true,
                    allowNativeInput: false,
                    channel: PlayDeliveryChannel.DomFallback,
                    PlayDisarmReason.None);
            }

            return Capabilities(
                PlayAutomationProfile.Full,
                acceptDraft: false,
                allowSend: false,
                allowNativeInput: true,
                channel: PlayDeliveryChannel.None,
                session.HasPin ? PlayDisarmReason.WrongUrl : PlayDisarmReason.NoPin);
        }

        return Capabilities(
            PlayAutomationProfile.Full,
            acceptDraft: true,
            allowSend: true,
            allowNativeInput: false,
            channel: PlayDeliveryChannel.DomFallback,
            PlayDisarmReason.None);
    }

    private static PlayTabCapabilities Disabled(PlayDisarmReason reason) =>
        Capabilities(
            PlayAutomationProfile.Disabled,
            acceptDraft: false,
            allowSend: false,
            allowNativeInput: true,
            channel: PlayDeliveryChannel.None,
            reason);

    private static PlayTabCapabilities Capabilities(
        PlayAutomationProfile profile,
        bool acceptDraft,
        bool allowSend,
        bool allowNativeInput,
        PlayDeliveryChannel channel,
        PlayDisarmReason reason) =>
        new(
            profile,
            acceptDraft,
            allowSend,
            allowNativeInput,
            channel,
            reason);

    private static bool TabKeysEqual(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsOnStoredPlayConversation(
        string? source,
        string? conversationId,
        string? gizmoId) =>
        !string.IsNullOrWhiteSpace(conversationId)
        && AdventurePlayContextService.IsOnPlayConversationPage(
            source,
            conversationId,
            gizmoId ?? "");

    private static bool IsCandidateOnPlayTarget(PlayTabCapabilityContext ctx) =>
        ctx.Bundle is not null
        && !string.IsNullOrWhiteSpace(ctx.CandidateTabKey)
        && PlayTabPinService.IsOnPlayTarget(ctx.SourceUrl, ctx.Bundle);
}
