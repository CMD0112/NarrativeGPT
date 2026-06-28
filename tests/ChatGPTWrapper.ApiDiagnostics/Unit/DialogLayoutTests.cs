using ChatGPTWrapper.Shell;
using System.Windows;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class DialogLayoutStoreTests : IDisposable
{
    private readonly string _tempFile;

    public DialogLayoutStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"cgw-dialog-layout-{Guid.NewGuid():N}.json");
        DialogLayoutStore.TestOverridePath = _tempFile;
        DialogLayoutStore.ResetForTests();
    }

    public void Dispose()
    {
        DialogLayoutStore.TestOverridePath = null;
        DialogLayoutStore.ResetForTests();
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    [Fact]
    public void Save_and_Initialize_round_trip_persisted_bounds()
    {
        DialogLayoutStore.Save("PlayPromptInjectionDialog", 920, 760);

        DialogLayoutStore.ResetForTests();
        DialogLayoutStore.Initialize();

        Assert.True(DialogLayoutStore.TryGet("PlayPromptInjectionDialog", out var record));
        Assert.NotNull(record);
        Assert.Equal(920, record!.Width);
        Assert.Equal(760, record.Height);
    }

    [Fact]
    public void TryGet_returns_false_for_unknown_key()
    {
        Assert.False(DialogLayoutStore.TryGet("MissingDialog", out var record));
        Assert.Null(record);
    }

    [Fact]
    public void Save_ignores_blank_layout_key()
    {
        DialogLayoutStore.Save("  ", 640, 480);
        DialogLayoutStore.Initialize();
        Assert.False(DialogLayoutStore.TryGet("  ", out _));
    }
}

[Trait("Category", "Unit")]
public sealed class DialogViewportLayoutTests
{
    private static readonly Rect WorkArea = new(0, 0, 1280, 800);

    [Fact]
    public void ValidatePersistedSize_rejects_below_minimum()
    {
        Assert.False(DialogViewportLayout.ValidatePersistedSize(400, 500, 480, 400, WorkArea));
    }

    [Fact]
    public void ValidatePersistedSize_rejects_taller_than_work_area()
    {
        Assert.False(DialogViewportLayout.ValidatePersistedSize(900, 900, 640, 520, WorkArea));
    }

    [Fact]
    public void ValidatePersistedSize_accepts_in_range_bounds()
    {
        Assert.True(DialogViewportLayout.ValidatePersistedSize(920, 760, 640, 520, WorkArea));
    }

    [Fact]
    public void ClampDimensions_shrinks_oversized_window_to_work_area()
    {
        var (width, height) = DialogViewportLayout.ClampDimensions(2000, 1500, WorkArea);

        Assert.Equal(WorkArea.Width - DialogViewportLayout.EdgeInset * 2, width);
        Assert.Equal(WorkArea.Height - DialogViewportLayout.EdgeInset * 2, height);
    }

    [Fact]
    public void ShouldPersistSize_detects_user_resize_from_design_defaults()
    {
        Assert.True(DialogViewportLayout.ShouldPersistDimensions(900, 700, 920, 760));
        Assert.False(DialogViewportLayout.ShouldPersistDimensions(900, 700, 900, 700));
    }
}
