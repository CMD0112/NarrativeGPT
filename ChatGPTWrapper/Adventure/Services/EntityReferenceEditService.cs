using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.Adventure.Services;

public static class EntityReferenceEditService
{
    public static EntityEditModel? PrepareModel(
        AdventureBundle bundle,
        string categoryFilter,
        EntityReferenceRow? row,
        bool isNew)
    {
        var category = isNew ? categoryFilter : EntityEditMapper.CategoryForEntityKind(row!.Kind);
        return isNew
            ? EntityEditMapper.CreateNew(category, bundle.Metadata.Id)
            : EntityEditMapper.Load(bundle.Entities, row!.Id, category, bundle.Metadata.Id);
    }

    public static string ResolveCategory(string categoryFilter, EntityReferenceRow? row, bool isNew) =>
        isNew ? categoryFilter : EntityEditMapper.CategoryForEntityKind(row!.Kind);

    public static bool TryCommitModel(
        AdventureBundle bundle,
        EntityEditModel model,
        bool deleted,
        string category,
        string? priorName,
        Window? owner,
        EntityReferenceEditCallbacks? callbacks,
        bool promptCanonReconcile,
        bool promptRenameWizard = true)
    {
        if (deleted)
            EntityEditMapper.Delete(bundle.Entities, model);
        else if (!EntityEditMapper.Apply(bundle.Entities, model))
            return false;

        AdventureStore.Save(bundle);

        if (!promptCanonReconcile)
            return true;

        var context = CanonReconciliationPromptService.ForEntityEdit(
            category,
            model.Id,
            priorName,
            deleted ? priorName : model.Name,
            isDelete: deleted);

        EntityChangePlan? renamePlan = null;
        var isRename = !deleted
                       && !string.IsNullOrWhiteSpace(priorName)
                       && !string.IsNullOrWhiteSpace(model.Name)
                       && !string.Equals(priorName, model.Name, StringComparison.OrdinalIgnoreCase);

        if (isRename && promptRenameWizard && owner is not null)
        {
            var terms = CanonMentionIndexService.CollectSearchTerms(bundle, model.Id, category).ToList();
            if (!string.IsNullOrWhiteSpace(priorName)
                && !terms.Contains(priorName, StringComparer.OrdinalIgnoreCase))
                terms.Add(priorName);

            var mentions = CanonMentionIndexService.FindMentions(bundle, terms);
            var wizard = new EntityRenameWizardDialog(bundle, context, mentions) { Owner = owner };
            if (wizard.ShowDialog() == true)
                renamePlan = wizard.ResultPlan;
        }

        var syncResult = renamePlan is not null
            ? EntityEditSourceSyncService.ApplyPlan(bundle, renamePlan, callbacks?.GetPhraseHighlightRules?.Invoke())
            : EntityEditSourceSyncService.TrySyncAfterEntityEdit(
                bundle,
                context,
                callbacks?.GetPhraseHighlightRules?.Invoke());

        AdventureStore.Save(bundle);
        callbacks?.OnSourceSyncCompleted?.Invoke(syncResult);

        if (syncResult.Staged)
        {
            callbacks?.OnStatusRefreshRequested?.Invoke();
            return true;
        }

        if (syncResult.RequiresManualReconcile)
            PromptReconcile(bundle, owner, context, callbacks);
        else
            FinishAfterEntityEdit(bundle, owner, context, callbacks, syncResult);

        return true;
    }

    public static bool TryOpenEditor(
        AdventureBundle bundle,
        Window? owner,
        string categoryFilter,
        EntityReferenceRow? row,
        bool isNew,
        EntityReferenceEditCallbacks? callbacks,
        bool promptCanonReconcile,
        bool promptRenameWizard = true)
    {
        var category = ResolveCategory(categoryFilter, row, isNew);
        var model = PrepareModel(bundle, categoryFilter, row, isNew);
        if (model is null)
            return false;

        var priorName = model.IsNew ? null : model.Name;
        var dlg = new EntityEditDialog(model) { Owner = owner };
        if (dlg.ShowDialog() != true)
            return false;

        return TryCommitModel(
            bundle,
            model,
            dlg.Deleted,
            category,
            priorName,
            owner,
            callbacks,
            promptCanonReconcile,
            promptRenameWizard);
    }

    public static bool TryDelete(
        AdventureBundle bundle,
        Window? owner,
        EntityReferenceRow row,
        EntityReferenceEditCallbacks? callbacks,
        bool promptCanonReconcile)
    {
        if (MessageBox.Show(owner, $"Delete “{row.Name}”?", "Delete entity",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;

        var category = EntityEditMapper.CategoryForEntityKind(row.Kind);
        var model = EntityEditMapper.Load(bundle.Entities, row.Id, category, bundle.Metadata.Id);
        if (model is not null)
        {
            return TryCommitModel(
                bundle,
                model,
                deleted: true,
                category,
                row.Name,
                owner,
                callbacks,
                promptCanonReconcile);
        }

        EntityEditMapper.Delete(bundle.Entities, row.Id, category);
        AdventureStore.Save(bundle);

        if (promptCanonReconcile)
        {
            var context = CanonReconciliationPromptService.ForEntityEdit(
                category,
                row.Id,
                row.Name,
                row.Name,
                isDelete: true);
            var syncResult = EntityEditSourceSyncService.TrySyncAfterEntityEdit(bundle, context);
            AdventureStore.Save(bundle);
            callbacks?.OnSourceSyncCompleted?.Invoke(syncResult);

            if (syncResult.RequiresManualReconcile)
                PromptReconcile(bundle, owner, context, callbacks);
            else
                FinishAfterEntityEdit(bundle, owner, context, callbacks, syncResult);
        }

        return true;
    }

    public static bool TryTogglePin(AdventureBundle bundle, EntityReferenceRow row)
    {
        if (row.Kind == AdventurePlayEntityKind.Quest)
            return false;

        switch (row.Kind)
        {
            case AdventurePlayEntityKind.Location:
                if (bundle.Entities.Locations.FirstOrDefault(e => e.Id == row.Id) is { } location)
                    location.Pinned = !location.Pinned;
                else
                    return false;
                break;
            case AdventurePlayEntityKind.Concept:
                if (bundle.Entities.Concepts.FirstOrDefault(e => e.Id == row.Id) is { } concept)
                    concept.Pinned = !concept.Pinned;
                else
                    return false;
                break;
            default:
                if (bundle.Entities.Characters.FirstOrDefault(e => e.Id == row.Id) is { } character)
                    character.Pinned = !character.Pinned;
                else
                    return false;
                break;
        }

        AdventureStore.Save(bundle);
        return true;
    }

    public static void PromptReconcile(
        AdventureBundle bundle,
        Window? owner,
        CanonEditContext context,
        EntityReferenceEditCallbacks? callbacks)
    {
        var adventureId = bundle.Metadata.Id;
        var result = CanonReconciliationPromptService.TryPromptAfterSave(
            owner,
            bundle,
            context,
            callbacks?.GetPhraseHighlightRules?.Invoke(),
            callbacks?.OpenSourceManagerAsync);

        if (result is CanonReconcileResult.Pushed or CanonReconcileResult.Pulled)
        {
            var reloaded = AdventureStore.Load(adventureId);
            if (reloaded is not null)
                callbacks?.OnBundleReloaded?.Invoke(reloaded);
        }

        if (result is not null
            || CanonReconciliationService.HasUnresolvedDrift(bundle)
            || CanonReconciliationService.HasPendingNotify(bundle))
        {
            callbacks?.OnStatusRefreshRequested?.Invoke();
        }
    }

    private static void FinishAfterEntityEdit(
        AdventureBundle bundle,
        Window? owner,
        CanonEditContext context,
        EntityReferenceEditCallbacks? callbacks,
        EntityEditSourceSyncResult syncResult)
    {
        var adventureId = bundle.Metadata.Id;
        callbacks?.OnStatusRefreshRequested?.Invoke();

        if (callbacks?.OnBundleReloaded is not null)
        {
            var reloaded = AdventureStore.Load(adventureId);
            if (reloaded is not null)
                callbacks.OnBundleReloaded(reloaded);
        }

        if (!syncResult.Synced && CanonReconciliationService.HasUnresolvedDrift(bundle))
            PromptReconcile(bundle, owner, context, callbacks);
    }
}
