using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.Adventure.Services;

public static class EntityReferenceEditModeResolver
{
    public static EntityReferenceEditMode Resolve(
        EntityReferenceEditMode configured,
        PlayLayoutCapabilities _)
    {
        if (configured != EntityReferenceEditMode.Auto)
            return configured;

        // Adventure side panels (Play Reference, Design Cast) stay list-only; edit in a modal dialog.
        return EntityReferenceEditMode.Modal;
    }
}
