using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Holds the current prepared artifact for an active adventure preview/send contract.
/// </summary>
internal sealed class PreparedSendArtifactStore
{
    private PreparedSendArtifact? _artifact;

    public PreparedSendArtifact? Current => _artifact;

    public bool HasCurrent => _artifact is not null;

    public bool IsStale =>
        _artifact is not null
        && _artifact.IsStale(PreparedSendSettingsFingerprint.Compute(_bundle!));

    public bool CanSend => _artifact is not null && !IsStale;

    private AdventureBundle? _bundle;

    public void Bind(AdventureBundle bundle) => _bundle = bundle;

    public PreparedSendArtifact? Set(PreparedSendArtifact? artifact)
    {
        _artifact = artifact;
        return _artifact;
    }

    public void Invalidate() => _artifact = null;

    public PreparedSendArtifact? RequireForSend()
    {
        if (_artifact is null)
            return null;

        if (_bundle is not null && IsStale)
            return null;

        return _artifact;
    }
}
