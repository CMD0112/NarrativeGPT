using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.Adventure.Services;

public static class EntityReferenceEditModeResolver
{
    public static EntityReferenceEditMode Resolve(
        EntityReferenceEditMode configured,
        PlayLayoutCapabilities layout)
    {
        if (configured == EntityReferenceEditMode.Auto)
            return EntityReferenceEditMode.Modal;

        return configured;
    }
}
