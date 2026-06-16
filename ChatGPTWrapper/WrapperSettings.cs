namespace ChatGPTWrapper;

public sealed class WrapperSettings
{
    /// <summary>
    /// When set, adventure folders are stored as {AdventuresDirectoryOverride}/{guid}/.
    /// When null, uses %LocalAppData%\ChatGPTWrapper\adventures\.
    /// </summary>
    public string? AdventuresDirectoryOverride { get; set; }
}
