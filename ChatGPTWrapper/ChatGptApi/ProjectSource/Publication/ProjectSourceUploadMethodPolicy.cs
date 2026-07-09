using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

internal static class ProjectSourceUploadMethodPolicy
{
    public static bool IsHeadlessBrowser(ProjectSourceUploadMethod method) =>
        method == ProjectSourceUploadMethod.HeadlessBrowser;

    public static bool IsPureApi(ProjectSourceUploadMethod method) =>
        method == ProjectSourceUploadMethod.PureApi;
}
