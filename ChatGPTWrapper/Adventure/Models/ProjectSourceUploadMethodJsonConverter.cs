using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGPTWrapper.Adventure.Models;

/// <summary>
/// Persists <see cref="ProjectSourceUploadMethod"/>; maps legacy DOM values to headless.
/// </summary>
public sealed class ProjectSourceUploadMethodJsonConverter : JsonConverter<ProjectSourceUploadMethod>
{
    public override ProjectSourceUploadMethod Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException();

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return ProjectSourceUploadMethod.HeadlessBrowser;

        return value.Trim() switch
        {
            _ when value.Equals("HeadlessBrowser", StringComparison.OrdinalIgnoreCase)
                => ProjectSourceUploadMethod.HeadlessBrowser,
            _ when value.Equals("PureApi", StringComparison.OrdinalIgnoreCase)
                => ProjectSourceUploadMethod.PureApi,
            _ when value.Equals("ExternalBrowser", StringComparison.OrdinalIgnoreCase)
                => ProjectSourceUploadMethod.HeadlessBrowser,
            _ when value.Equals("WebView2Dom", StringComparison.OrdinalIgnoreCase)
                => ProjectSourceUploadMethod.HeadlessBrowser,
            _ => Enum.TryParse<ProjectSourceUploadMethod>(value, ignoreCase: true, out var parsed)
                ? NormalizeLegacy(parsed)
                : throw new JsonException($"Unknown upload method: {value}"),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ProjectSourceUploadMethod value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(NormalizeLegacy(value).ToString());

    private static ProjectSourceUploadMethod NormalizeLegacy(ProjectSourceUploadMethod method) =>
        method switch
        {
#pragma warning disable CS0618
            ProjectSourceUploadMethod.WebView2Dom => ProjectSourceUploadMethod.HeadlessBrowser,
#pragma warning restore CS0618
            _ => method,
        };
}
