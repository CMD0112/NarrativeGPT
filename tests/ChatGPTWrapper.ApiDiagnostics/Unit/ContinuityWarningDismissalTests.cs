using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ContinuityWarningDismissalTests
{
    [Fact]
    public void Dismiss_filters_warning_on_next_bind()
    {
        var continuity = new ContinuityDocument
        {
            Warnings =
            [
                new ContinuityWarningEntry { Message = "Item lost but used again." },
            ],
        };

        ContinuityWarningDismissalService.Dismiss(continuity, "Item lost but used again.");

        Assert.Empty(ContinuityWarningDismissalService.FilterActive(continuity));
        Assert.True(ContinuityWarningDismissalService.IsDismissed(continuity, "Item lost but used again."));
    }
}
