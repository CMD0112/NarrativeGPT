using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class NotesFindServiceTests
{
    [Fact]
    public void FindMatchOffsets_empty_query_returns_empty()
    {
        Assert.Empty(NotesFindService.FindMatchOffsets("hello world", "", false));
    }

    [Fact]
    public void FindMatchOffsets_case_insensitive_finds_all()
    {
        var matches = NotesFindService.FindMatchOffsets("Foo foo FOO", "foo", caseSensitive: false);
        Assert.Equal([0, 4, 8], matches);
    }

    [Fact]
    public void FindMatchOffsets_case_sensitive_finds_exact()
    {
        var matches = NotesFindService.FindMatchOffsets("Foo foo FOO", "foo", caseSensitive: true);
        Assert.Equal([4], matches);
    }

    [Fact]
    public void FindMatchOffsets_advances_past_match()
    {
        var matches = NotesFindService.FindMatchOffsets("aaa", "aa", caseSensitive: true);
        Assert.Equal([0], matches);
    }

    [Fact]
    public void FindBestMatchIndex_prefers_same_or_next_offset()
    {
        var offsets = new List<int> { 0, 10, 20 };
        Assert.Equal(1, NotesFindService.FindBestMatchIndex(offsets, 10));
        Assert.Equal(2, NotesFindService.FindBestMatchIndex(offsets, 12));
        Assert.Equal(2, NotesFindService.FindBestMatchIndex(offsets, 25));
        Assert.Equal(2, NotesFindService.FindBestMatchIndex(offsets, 20));
    }

    [Fact]
    public void FindBestMatchIndex_before_first_match_returns_first()
    {
        var offsets = new List<int> { 10, 20 };
        Assert.Equal(0, NotesFindService.FindBestMatchIndex(offsets, 0));
    }
}
