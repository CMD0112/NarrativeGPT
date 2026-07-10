using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Resolves publication-lab upload transport from UI, adventure metadata, or wrapper defaults.
/// </summary>
internal static class ProjectSourceUploadMethodResolver
{
    public static ProjectSourceUploadMethod Resolve(
        AdventureBundle? bundle = null,
        ProjectSourceUploadMethod? uiSelection = null)
    {
        if (uiSelection is { } selected)
            return selected;

        if (bundle is not null)
            return bundle.Metadata.Settings.ProjectSourceUploadMethod;

        return WrapperSettingsStore.Current.PublicationLabDomUploadMethod;
    }

    public static void PersistSelection(AdventureBundle bundle, ProjectSourceUploadMethod method)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        bundle.Metadata.Settings.ProjectSourceUploadMethod = method;
        AdventureStore.Save(bundle, AdventureSaveScope.Metadata);

        var wrapper = WrapperSettingsStore.Current;
        if (wrapper.PublicationLabDomUploadMethod == method)
            return;

        wrapper.PublicationLabDomUploadMethod = method;
        WrapperSettingsStore.Save(wrapper);

        ProjectLinkDiagnostics.Log(
            $"Publication lab upload method set to {method} adventure={bundle.Metadata.Id}");
    }
}
