using System.IO;
using System.IO.Compression;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUI.Models;
using ChatGPTWrapper.WinUI.Shell;
using ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;
using ChatGPTWrapper.WinUI.Views.Dialogs;
using ChatGPTWrapper.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Native WinUI dialogs using wrapper theme tokens. Complex editors still route through WPF STA bridge.</summary>
internal static class WinUiDialogHostService
{
    public static async Task<bool> ShowRenameAsync(Window? owner, Guid adventureId)
    {
        var meta = AdventureStore.ListIndex().FirstOrDefault(a => a.Id == adventureId);
        if (meta is null)
            return false;

        var box = new TextBox
        {
            Text = meta.Title,
            SelectionStart = 0,
            SelectionLength = meta.Title.Length,
            MinWidth = 320,
        };

        var dialog = new ContentDialog
        {
            Title = "Rename adventure",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Title", Style = GetStyle("ShellFormFieldLabelStyle") },
                    box,
                },
            },
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await WinUiDialogHelper.ShowAsync(dialog, owner) != ContentDialogResult.Primary)
            return false;

        var newTitle = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(newTitle))
            return false;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return false;

        return AdventureRenameService.TryRename(bundle, newTitle, out _);
    }

    private static async Task SavePlaySettingsWorkbenchAsync(
        PlaySettingsWorkbenchPage page,
        WinUiShellDialogHostWindow window)
    {
        if (await page.CommitAsync())
        {
            WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
            window.ForceClose();
            window.CloseDialog(true);
        }
    }

    private static async Task CancelPlaySettingsWorkbenchAsync(
        PlaySettingsWorkbenchPage page,
        WinUiShellDialogHostWindow window)
    {
        if (!await ConfirmDiscardPlaySettingsAsync(page, window))
            return;

        window.ForceClose();
        window.CloseDialog(false);
    }

    private static async Task<bool> ConfirmDiscardPlaySettingsAsync(
        PlaySettingsWorkbenchPage page,
        WinUiShellDialogHostWindow window)
    {
        if (!page.HasUnsavedPlaySettings())
            return true;

        return await WinUiDialogService.ShowConfirmAsync(
            window,
            "Play settings",
            "Discard unsaved changes?",
            confirmText: "Discard",
            cancelText: "Keep editing");
    }

    public static async Task ShowRecapAsync(Window? owner, Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var recap = string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary)
            ? "(No recap yet.)"
            : bundle.Summary.RollingSummary;

        var page = new RecapPage(recap);
        var dialog = new ContentDialog
        {
            Title = "Recap",
            Content = page,
            CloseButtonText = "Close",
        };

        page.CloseRequested += (_, _) => dialog.Hide();
        await WinUiDialogHelper.ShowAsync(dialog, owner);
    }

    public static async Task ShowThreadManagerAsync(
        Window? owner,
        Guid adventureId,
        AdventureThreadKind? initialKind = null)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var actions = WinUiThreadManagerBridge.CreateActions(adventureId);
        var page = new ThreadManagerPage(adventureId, actions, initialKind);
        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            ThreadManagerCopy.DialogTitle,
            page,
            layoutKey: "AdventureThreadManagerDialog",
            designWidth: 980,
            designHeight: 720,
            configure: window =>
            {
                WinUiDialogService.AddCloseFooter(window, () =>
                {
                    if (page.Changed)
                        WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
                });
            });
    }

    public static async Task ShowProposalReviewAsync(
        Window? owner,
        Guid adventureId,
        ProposalReviewCategory? initialCategory = null)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var page = new ProposalReviewHubPage(adventureId, initialCategory);
        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Review proposals",
            page,
            layoutKey: "ProposalReviewHubDialog",
            designWidth: 1040,
            designHeight: 720,
            configure: window =>
            {
                WinUiDialogService.AddCloseFooter(window, () =>
                {
                    if (page.ChangesSaved)
                        WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
                });
            });
    }

    public static async Task ShowPlaySettingsAsync(
        Window? owner,
        Guid adventureId,
        PlaySettingsTab initialTab = PlaySettingsTab.Injection)
    {
        try
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return;

            var preview = WinUiShellHost.RunOnUiThreadSync(
                () => WinUiShellHost.Session?.GetActiveComposeInjection()?.GetText(),
                fallback: null);

            var ownerWindow = owner ?? App.CurrentMainWindow;
            var workbenchViewport = WorkbenchViewportDesign.Resolve(
                WorkbenchTier.T4Session,
                WinUiDialogViewportLayout.GetWorkAreaBounds(ownerWindow));
            var playViewport = PlaySettingsViewportMetrics.FromWorkbench(workbenchViewport);
            PlaySettingsWorkbenchLayout.ApplyViewport(playViewport);

            var page = new PlaySettingsWorkbenchPage(bundle, preview, initialTab);
            WinUiPlaySettingsBridge.Wire(page, adventureId, WinUiShellHost.Session);
            page.RefreshHostDelegates();

            var result = await WinUiDialogService.ShowWorkbenchAsync(
                ownerWindow,
                "Play settings",
                page,
                layoutKey: "PlayPromptInjectionDialog",
                designWidth: workbenchViewport.DesignWidth,
                designHeight: workbenchViewport.DesignHeight,
                configure: window =>
                {
                    window.SetDialogSizeConstraints(workbenchViewport.MinWidth, workbenchViewport.MinHeight);
                    WinUiDialogService.AddSaveCancelFooter(
                        window,
                        onSave: () => _ = SavePlaySettingsWorkbenchAsync(page, window),
                        onCancel: () => _ = CancelPlaySettingsWorkbenchAsync(page, window));
                    window.SetCloseConfirmation(() => ConfirmDiscardPlaySettingsAsync(page, window));
                });

            if (result == true)
                WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("play_settings_failed", ex);
            await WinUiShellHost.RunOnUiThreadAsync(() =>
                WinUiDialogHelper.ShowInfoAsync(
                    App.CurrentMainWindow,
                    "Play settings",
                    ex.Message));
        }
    }

    public static Task ShowSourceManagerAsync(Window? owner, Guid adventureId) =>
        ShowPlaySettingsAsync(owner, adventureId, PlaySettingsTab.Sources);

    public static async Task ShowSourceSyncWorkbenchAsync(Window? owner, Guid adventureId)
    {
        var page = new SourceSyncPage(adventureId);
        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Publication lab",
            page,
            layoutKey: "SourceSyncDialog",
            designWidth: 900,
            designHeight: 560,
            configure: window =>
            {
                WinUiDialogService.AddCloseFooter(window, () =>
                {
                    if (page.SyncCompleted)
                        WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
                });
            });
    }

    public static async Task ShowProjectWorkspaceAsync(Window? owner, Guid adventureId)
    {
        var page = new ProjectWorkspacePage(adventureId);
        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "ChatGPT Project",
            page,
            layoutKey: "ProjectWorkspaceDialog",
            designWidth: 720,
            designHeight: 580,
            configure: window => WinUiDialogService.AddCloseFooter(window));

        if (page.LinkStateChanged || page.SyncCompleted)
        {
            await WinUiShellHost.RunOnUiThreadAsync(async () =>
            {
                if (WinUiShellHost.Session is not null)
                    await WinUiShellHost.Session.LoadAdventureAsync(adventureId);
                WinUiShellHost.RefreshSessionChrome();
                if (App.CurrentMainWindow is { } mainWindow)
                    await mainWindow.ApplyShellRefreshAsync();
            });
        }
    }

    public static async Task ShowSearchAsync(Window? owner, Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var page = new SearchPage(bundle);
        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Search adventure",
            page,
            layoutKey: "SearchDialog",
            designWidth: 520,
            designHeight: 420,
            configure: window => WinUiDialogService.AddCloseFooter(window));
    }

    public static async Task ShowPlayHandoffAsync(Window? owner, Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var checkpoint = PlayHandoffService.BuildCheckpoint(bundle, snapshot, new PlayHandoffOptions());
        var page = new PlayHandoffPage(bundle, snapshot, checkpoint)
        {
            StartNewPlayThreadAsync = request =>
                WinUiThreadManagerBridge.StartNewPlayThreadAsync(adventureId, request),
        };

        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Hand off to new chat",
            page,
            layoutKey: "PlayHandoffDialog",
            designWidth: 720,
            designHeight: 520,
            configure: window => WinUiDialogService.AddCloseFooter(window));
    }

    public static async Task ShowJsonImportReviewAsync(Window? owner, Guid adventureId)
    {
        var page = new JsonImportReviewPage(adventureId);
        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "JSON import review",
            page,
            layoutKey: "JsonImportReviewDialog",
            designWidth: 920,
            designHeight: 640,
            configure: window =>
            {
                WinUiDialogService.AddCloseFooter(window, () =>
                {
                    if (page.ChangesSaved)
                        WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
                });
            });
    }

    public static async Task ShowThemeCustomizationAsync(Window? owner)
    {
        var chrome = UiChromeStore.Load();
        var page = new AppearanceThemePage(
            chrome.Theme.Clone(),
            WinUiShellCoordinator.CreateThemeApplyHandler());

        var result = await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Appearance & theme",
            page,
            layoutKey: "ThemeCustomizationDialog",
            designWidth: 920,
            designHeight: 680,
            configure: window =>
            {
                WinUiDialogService.AddSaveCancelFooter(
                    window,
                    onSave: () =>
                    {
                        page.Commit();
                        window.CloseDialog(true);
                    },
                    onCancel: () =>
                    {
                        page.RevertPreview();
                        window.CloseDialog(false);
                    });
            });

        if (result != true)
            page.RevertPreview();
    }

    public static async Task ShowFormatDialogAsync(Window? owner, Guid? adventureId = null)
    {
        _ = adventureId;
        var chrome = UiChromeStore.Load();
        var apply = WinUiShellCoordinator.CreateChromeApplyHandler();
        var essentials = new FormatEssentialsPage(chrome, apply);
        var refinement = new FormatRefinementPage(essentials.WorkingSettings, apply);

        var tabs = new TabView { TabWidthMode = TabViewWidthMode.Equal };
        tabs.TabItems.Add(new TabViewItem { Header = "Essentials", Content = essentials });
        tabs.TabItems.Add(new TabViewItem { Header = "Refine", Content = refinement });

        var result = await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Format",
            tabs,
            layoutKey: "ContinuousViewFormatDialog",
            designWidth: 960,
            designHeight: 720,
            configure: window =>
            {
                WinUiDialogService.AddSaveCancelFooter(
                    window,
                    onSave: () =>
                    {
                        essentials.Commit();
                        window.CloseDialog(true);
                    },
                    onCancel: () =>
                    {
                        essentials.RevertPreview();
                        window.CloseDialog(false);
                    });
            });

        if (result != true)
            essentials.RevertPreview();
    }

    public static async Task ShowEntityEditAsync(
        Window? owner,
        Guid adventureId,
        EntityReferenceRow? row = null)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var isNew = row is null;
        var categoryFilter = row is null
            ? "Characters"
            : EntityEditMapper.CategoryForEntityKind(row.Kind);
        var category = EntityReferenceEditService.ResolveCategory(categoryFilter, row, isNew);
        var model = EntityReferenceEditService.PrepareModel(bundle, categoryFilter, row, isNew);
        if (model is null)
            return;

        var priorName = model.IsNew ? null : model.Name;
        var page = new EntityEditPage(model);
        var title = model.IsNew ? $"New {model.TypeLabel.ToLowerInvariant()}" : model.Name;

        await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            title,
            page,
            layoutKey: "EntityEditDialog",
            designWidth: 560,
            designHeight: 520,
            configure: window =>
            {
                if (!model.IsNew)
                {
                    var delete = new Button
                    {
                        Content = "Delete…",
                        Style = GetStyle("ShellGhostButtonStyle"),
                    };
                    delete.Click += async (_, _) =>
                    {
                        if (!await WinUiDialogHelper.ConfirmAsync(
                                owner,
                                $"Delete {model.TypeLabel.ToLowerInvariant()}",
                                $"Delete “{model.Name}”? This cannot be undone.",
                                confirmText: "Delete"))
                        {
                            return;
                        }

                        if (EntityReferenceEditService.TryCommitEntityEditor(
                                bundle,
                                model,
                                deleted: true,
                                category,
                                priorName))
                        {
                            WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
                            window.CloseDialog(true);
                        }
                    };
                    window.AddFooterButton(delete);
                }

                WinUiDialogService.AddSaveCancelFooter(
                    window,
                    onSave: () =>
                    {
                        if (!page.TryHarvest(out var validationMessage))
                        {
                            if (!string.IsNullOrWhiteSpace(validationMessage))
                            {
                                _ = WinUiDialogHelper.ShowInfoAsync(owner, "Entity editor", validationMessage);
                                return;
                            }
                        }

                        if (EntityReferenceEditService.TryCommitEntityEditor(
                                bundle,
                                model,
                                deleted: false,
                                category,
                                priorName))
                        {
                            WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
                            window.CloseDialog(true);
                        }
                    },
                    onCancel: () => window.CloseDialog(false));
            });
    }

    public static async Task<bool> ShowEntityMergeAsync(
        Window? owner,
        Guid adventureId,
        EntityReferenceRow row,
        string categoryFilter)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return false;

        var page = new EntityMergePage(bundle, row, categoryFilter);
        var result = await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Merge entity",
            page,
            layoutKey: "EntityMergeDialog",
            designWidth: 520,
            designHeight: 420,
            configure: window =>
            {
                WinUiDialogService.AddSaveCancelFooter(
                    window,
                    onSave: () =>
                    {
                        if (page.SelectedTarget is not EntityReferenceRow target)
                        {
                            _ = WinUiDialogHelper.ShowInfoAsync(owner, "Merge entity", "Select a target entity.");
                            return;
                        }

                        var plan = EntityChangePlanBuilder.BuildMergePlan(
                            bundle,
                            row.Id,
                            target.Id,
                            categoryFilter,
                            row.Name,
                            target.Name);
                        EntityEditSourceSyncService.ApplyPlan(bundle, plan);
                        AdventureStore.Save(bundle);
                        window.CloseDialog(true);
                    },
                    onCancel: () => window.CloseDialog(false));
            });

        if (result == true)
            WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);

        return result == true;
    }

    public static async Task<bool> ShowEntityRetireAsync(
        Window? owner,
        Guid adventureId,
        EntityReferenceRow row,
        string categoryFilter)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return false;

        var page = new EntityRetirePage(row);
        var result = await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Retire entity",
            page,
            layoutKey: "EntityRetireDialog",
            designWidth: 520,
            designHeight: 420,
            configure: window =>
            {
                WinUiDialogService.AddSaveCancelFooter(
                    window,
                    onSave: () =>
                    {
                        var plan = EntityChangePlanBuilder.BuildRetirePlan(
                            bundle,
                            row.Id,
                            categoryFilter,
                            row.Name,
                            page.AliasOnly);
                        EntityEditSourceSyncService.ApplyPlan(bundle, plan);
                        AdventureStore.Save(bundle);
                        window.CloseDialog(true);
                    },
                    onCancel: () => window.CloseDialog(false));
            });

        if (result == true)
            WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);

        return result == true;
    }

    public static async Task<bool> ShowEntityDeleteAsync(
        Window? owner,
        Guid adventureId,
        EntityReferenceRow row)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return false;

        if (!await WinUiDialogHelper.ConfirmAsync(
                owner,
                "Delete entity",
                $"Delete “{row.Name}”?",
                confirmText: "Delete"))
        {
            return false;
        }

        var category = EntityEditMapper.CategoryForEntityKind(row.Kind);
        var model = EntityEditMapper.Load(bundle.Entities, row.Id, category, bundle.Metadata.Id);
        if (model is not null)
        {
            if (!EntityReferenceEditService.TryCommitEntityEditor(
                    bundle,
                    model,
                    deleted: true,
                    category,
                    row.Name,
                    promptCanonReconcile: false))
            {
                return false;
            }
        }
        else
        {
            EntityEditMapper.Delete(bundle.Entities, row.Id, category);
            AdventureStore.Save(bundle);
        }

        var context = CanonReconciliationPromptService.ForEntityEdit(
            category,
            row.Id,
            row.Name,
            row.Name,
            isDelete: true);
        var syncResult = EntityEditSourceSyncService.TrySyncAfterEntityEdit(bundle, context);
        AdventureStore.Save(bundle);

        if (syncResult.RequiresManualReconcile)
        {
            await WinUiDialogHelper.ShowInfoAsync(
                owner,
                "Canon reconcile",
                "Entity deleted. Source canon may need manual reconcile — open Sources or Review when ready.");
        }

        WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
        return true;
    }

    public static async Task ShowWrapperSettingsAsync(Window? owner)
    {
        if (owner is null)
            return;

        var page = new WrapperSettingsPage();
        page.SetOwnerWindow(owner);

        var result = await WinUiDialogService.ShowWorkbenchAsync(
            owner,
            "Wrapper settings",
            page,
            layoutKey: "WrapperSettingsDialog",
            designWidth: 560,
            designHeight: 320,
            configure: window =>
            {
                WinUiDialogService.AddSaveCancelFooter(
                    window,
                    onSave: () => _ = SaveWrapperSettingsAsync(owner, page, window),
                    onCancel: () => window.CloseDialog(false));
            });

        _ = result;
    }

    private static async Task SaveWrapperSettingsAsync(
        Window owner,
        WrapperSettingsPage page,
        WinUiShellDialogHostWindow window)
    {
        var path = page.AdventuresPathText;
        string? normalized = null;

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!WrapperSettingsStore.TryValidateAdventuresDirectory(path, out normalized, out var error))
            {
                await WinUiDialogHelper.ShowInfoAsync(owner, "Adventures folder", error ?? "Invalid folder.");
                return;
            }

            var currentRoot = Path.GetFullPath(AppDirectories.AdventuresDirectory);
            if (!string.Equals(normalized, currentRoot, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(currentRoot)
                && Directory.EnumerateDirectories(currentRoot).Any(d =>
                    !AppDirectories.IsReservedAdventuresDirectory(Path.GetFileName(d))))
            {
                var proceed = await WinUiDialogHelper.ConfirmAsync(
                    owner,
                    "Change adventures folder",
                    $"Adventures already exist under the current folder:\n{currentRoot}\n\n"
                    + $"New adventures will use:\n{normalized}\n\n"
                    + "Existing adventures at the old location remain there until you move them or use "
                    + "\"Create folder on disk\" / import.\n\nContinue?",
                    confirmText: "Continue");
                if (!proceed)
                    return;
            }
        }

        var settings = new WrapperSettings
        {
            AdventuresDirectoryOverride = normalized,
            PublicationLabDomUploadMethod = WrapperSettingsStore.Current.PublicationLabDomUploadMethod,
        };
        WrapperSettingsStore.Save(settings);
        window.CloseDialog(true);
    }

    public static async Task<ScenarioCreationOutcome> ShowScenarioCreationAsync(Window? owner)
    {
        var page = new ScenarioCreationPage();
        var dialog = new ContentDialog
        {
            Title = "New adventure",
            Content = page,
            PrimaryButtonText = "Create",
            SecondaryButtonText = "Design with AI instead…",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await WinUiDialogHelper.ShowAsync(dialog, owner);
        return result switch
        {
            ContentDialogResult.Secondary => new ScenarioCreationOutcome
            {
                Confirmed = true,
                RequestDesignWithAi = true,
            },
            ContentDialogResult.Primary => new ScenarioCreationOutcome
            {
                Confirmed = true,
                Scenario = page.BuildScenario(),
                AdventureTitle = page.AdventureTitle,
                StartWithOpeningNarration = page.StartWithOpeningNarration,
            },
            _ => new ScenarioCreationOutcome(),
        };
    }

    public static async Task ShowExportAsync(Window? owner, Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || owner is null)
            return;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = bundle.Metadata.Title,
        };
        picker.FileTypeChoices.Add("Markdown", [".md"]);
        picker.FileTypeChoices.Add("Plain text", [".txt"]);
        picker.FileTypeChoices.Add("HTML", [".html"]);
        picker.FileTypeChoices.Add("JSON", [".json"]);
        picker.FileTypeChoices.Add("Archive", [".zip"]);
        WinUiDialogHelper.InitializeWithOwner(picker, owner);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        var path = file.Path;
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ExportService.ExportJsonArchive(bundle, path);
        else if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            await File.WriteAllTextAsync(path, ExportService.ExportPlainText(bundle, polishedOnly: true));
        else if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            await File.WriteAllTextAsync(path, ExportService.ExportHtml(bundle, polishedOnly: true));
        else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            await File.WriteAllTextAsync(path, ExportService.ExportFullJson(bundle));
        else
            await File.WriteAllTextAsync(path, ExportService.ExportStoryMarkdown(bundle, polishedOnly: true));
    }

    public static async Task<bool> ShowImportBackupAsync(Window? owner)
    {
        if (owner is null)
            return false;

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".zip");
        WinUiDialogHelper.InitializeWithOwner(picker, owner);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return false;

        var temp = Path.Combine(Path.GetTempPath(), "cgw-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(file.Path, temp);
            AdventureStore.ImportFromDirectory(temp);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("import_backup_failed", ex);
            await WinUiDialogHelper.ShowInfoAsync(owner, "Import failed", ex.Message);
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, recursive: true);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    public static async Task<(bool Success, string Result)> PromptAsync(
        Window? owner,
        string title,
        string prompt,
        string defaultText,
        string confirmButtonText = "OK",
        bool multiline = false)
    {
        var currentDefault = defaultText;
        while (true)
        {
            var box = new TextBox
            {
                Text = currentDefault,
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinWidth = 320,
                MinHeight = multiline ? 96 : 32,
            };

            var validation = new TextBlock
            {
                Foreground = GetBrush("TextMutedBrush"),
                Visibility = Visibility.Collapsed,
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = prompt, TextWrapping = TextWrapping.WrapWholeWords },
                        box,
                        validation,
                    },
                },
                PrimaryButtonText = confirmButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await WinUiDialogHelper.ShowAsync(dialog, owner) != ContentDialogResult.Primary)
                return (false, string.Empty);

            var trimmed = box.Text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                currentDefault = box.Text;
                continue;
            }

            return (true, trimmed);
        }
    }

    private static Microsoft.UI.Xaml.Style? GetStyle(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Microsoft.UI.Xaml.Style style
            ? style
            : null;

    private static Microsoft.UI.Xaml.Media.Brush GetBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Microsoft.UI.Xaml.Media.Brush brush
            ? brush
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
}
