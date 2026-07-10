namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

public static class ProjectPublicationLaneRegistry
{
    public static IReadOnlyList<IProjectPublicationLane> ForGizmo(
        string gizmoId,
        ProjectPublicationProfile profile,
        ChatGptProjectApiService api)
    {
        var dom = new BrowserNativePublicationLane(api);
        var library = new LibraryPublicationLane(api);
        var register = new RegisterProjectFilesPublicationLane(api);

        if (profile == ProjectPublicationProfile.BatchSync)
            return [register, library, dom];

        if (ChatGptProjectApiService.IsSnorlaxProjectId(gizmoId))
            return [dom, library, register];

        return [register, library, dom];
    }
}
