using System.Text.Json.Serialization;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper;

public sealed class WrapperSettings
{
    /// <summary>
    /// When set, adventure folders are stored as {AdventuresDirectoryOverride}/{guid}/.
    /// When null, uses %LocalAppData%\ChatGPTWrapper\adventures\.
    /// </summary>
    public string? AdventuresDirectoryOverride { get; set; }

    /// <summary>
    /// Default publication lab upload transport when an adventure has no explicit override.
    /// </summary>
    [JsonConverter(typeof(ProjectSourceUploadMethodJsonConverter))]
    public ProjectSourceUploadMethod PublicationLabDomUploadMethod { get; set; } =
        ProjectSourceUploadMethod.HeadlessBrowser;
}
