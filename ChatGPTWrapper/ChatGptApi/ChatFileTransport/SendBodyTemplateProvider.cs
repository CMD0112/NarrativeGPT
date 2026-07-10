using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class SendBodyTemplateProvider
{
    public const string GoldenAttachSampleKey = "POST_backend-api_f_conversation_attachments";
    public const string GoldenConversationSampleKey = "POST_backend-api_f_conversation";

    public bool TryLoadAttachmentTemplate(out JsonElement template) =>
        ChatGptApiSendSampleCapture.TryLoadSuccessfulRequestTemplate(GoldenAttachSampleKey, out template)
        || ChatGptApiSendSampleCapture.TryLoadSuccessfulRequestTemplate(GoldenConversationSampleKey, out template);

    public (object Body, string MessageId) BuildSendBodyWithAttachments(
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments) =>
        ChatGptConversationSendService.BuildSendBodyWithAttachmentsInternal(
            conversationId,
            parentMessageId,
            gizmoId,
            messageText,
            attachments);
}
