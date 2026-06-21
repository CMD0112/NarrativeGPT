using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.Adventure.Services;

public static class CanonReconciliationPromptService
{
    public static CanonReconcileResult? TryPromptAfterSave(
        Window? owner,
        AdventureBundle bundle,
        CanonEditContext context,
        IReadOnlyList<PhraseHighlightRule>? phraseRules = null,
        Func<Task>? openSourceManagerAsync = null)
    {
        var report = CanonReconciliationService.DetectDrift(bundle, context);
        if (!report.HasDrift)
            return null;

        var dlg = new CanonReconcileDialog(bundle, context, report, phraseRules, openSourceManagerAsync)
        {
            Owner = owner,
        };

        dlg.ShowDialog();
        return dlg.Result;
    }

    public static CanonEditContext ForEntityEdit(
        string category,
        Guid entityId,
        string? priorName,
        string? newName,
        bool isDelete = false) =>
        new()
        {
            Category = category,
            EntityId = entityId,
            PriorName = priorName,
            NewName = newName,
            IsDelete = isDelete,
        };

    public static string? FormatUnresolvedStatus(AdventureBundle bundle)
    {
        if (CanonReconciliationService.HasUnresolvedDrift(bundle))
            return "Sources out of sync — click to repair";

        if (CanonReconciliationService.HasPendingNotify(bundle))
            return "Canon update pending — next send will notify narrator";

        return null;
    }
}
