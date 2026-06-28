using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

internal sealed class SourcePublishRowViewModel : INotifyPropertyChanged
{
    private bool _isPublished;

    public SourcePublishRowViewModel(
        SourceManifestEntry entry,
        string sourcesDirectory,
        AdventureBundle bundle)
    {
        Entry = entry;
        Bundle = bundle;
        AdventureId = bundle.Metadata.Id;
        RelativePath = entry.RelativePath;
        AbsolutePath = Path.Combine(sourcesDirectory, entry.RelativePath);
        _isPublished = entry.IsManuallyCurrent();
        RefreshDisplay();
    }

    public SourceManifestEntry Entry { get; }

    public AdventureBundle Bundle { get; }

    public Guid AdventureId { get; }

    public string RelativePath { get; }

    public string AbsolutePath { get; }

    public string LocalStatus { get; private set; } = "";

    public string LastUploaded { get; private set; } = "";

    public string ProjectMatch { get; private set; } = "";

    public bool HasMirror { get; private set; }

    public bool IsPublished
    {
        get => _isPublished;
        set
        {
            if (_isPublished == value)
                return;

            _isPublished = value;
            if (value)
                SourceManifestHelper.MarkManuallyPublished(Entry, AbsolutePath, Bundle);
            else
                SourceManifestHelper.ClearManualPublish(Entry);

            RefreshDisplay();
            OnPropertyChanged();
            ManifestEntryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ManifestEntryChanged;

    public void RefreshDisplay()
    {
        var published = Entry.IsManuallyCurrent();
        if (_isPublished != published)
        {
            _isPublished = published;
            OnPropertyChanged(nameof(IsPublished));
        }

        var sectionHint = SectionDiffService.GetChangedSectionsSincePublish(Entry);
        var sectionSuffix = sectionHint.Count > 0
            ? $" ({sectionHint.Count} sections changed)"
            : "";

        LocalStatus = Entry.IsManuallyCurrent()
            ? "Published" + sectionSuffix
            : Entry.IsManuallyPublished
                ? "Needs republish" + sectionSuffix
                : File.Exists(AbsolutePath) ? "Ready" + sectionSuffix : "Missing";

        LastUploaded = Entry.ManuallyPublishedAt is { } at
            ? at.LocalDateTime.ToString("g")
            : "—";

        ProjectMatch = ProjectSourceProbeService.FormatProbeMatch(Entry.RemoteProbeMatch);
        HasMirror = ProjectSourceProbeService.HasMirrorFile(AdventureId, RelativePath);

        OnPropertyChanged(nameof(LocalStatus));
        OnPropertyChanged(nameof(LastUploaded));
        OnPropertyChanged(nameof(ProjectMatch));
        OnPropertyChanged(nameof(HasMirror));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class SourceHistoryRowViewModel
{
    public SourceHistoryRowViewModel(SourceFileHistoryEntry entry)
    {
        Entry = entry;
        DisplayLabel =
            $"{entry.ArchivedAt.LocalDateTime:g} · {SourceManifestHelper.ShortHash(entry.Sha256)} · {entry.Reason}";
    }

    public SourceFileHistoryEntry Entry { get; }

    public string DisplayLabel { get; }
}
