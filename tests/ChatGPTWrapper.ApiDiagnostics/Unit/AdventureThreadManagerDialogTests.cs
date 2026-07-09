using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection("WpfUi")]
public sealed class AdventureThreadManagerDialogTests
{
    [Fact]
    public void Constructor_loads_without_xaml_exception()
    {
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureChromeResources();
            Theme.ThemeApplicationService.ApplyToWpf(
                Theme.ThemeApplicationService.ResolveEffectiveTheme(
                    Theme.ThemeApplicationService.CreateDefaultSettings()));

            var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
            AdventureThreadRegistryService.EnsureMigrated(bundle);
            AdventureStore.Save(bundle);

            var actions = CreateNoOpActions();
            var dlg = new AdventureThreadManagerDialog(bundle.Metadata.Id, actions);
            Assert.Equal("Manage adventure threads", dlg.Title);
            dlg.Show();
            dlg.UpdateLayout();
            dlg.Close();
        });
    }

    [Fact]
    public void Constructor_loads_with_user_chrome_theme_and_adventure()
    {
        var configRoot = @"C:\Users\Crimi\AppData\Local\ChatGPTWrapper";
        if (!Directory.Exists(configRoot))
            return;

        WpfStaTestHost.Run(
            () =>
            {
                AppDirectories.ResetStoresForTests();
                AppDirectories.TestRootOverride = configRoot;
                try
                {
                    WpfStaTestHost.EnsureChromeResources();

                    var chrome = UiChromeStore.Load();
                    var resolved = Theme.ThemeApplicationService.ResolveEffectiveTheme(chrome.Theme);
                    Theme.ThemeApplicationService.InvalidateApplyCache();
                    Theme.ThemeApplicationService.ApplyToWpf(resolved);

                    AppDirectories.ApplyAdventuresDirectoryOverride(@"E:\Documents\ChatGPT Wrapper\Adventures");
                    var bundle = AdventureStore.Load(Guid.Parse("d84ea627-2663-478c-ab8b-6021af78d9d3"));
                    if (bundle is null)
                        return;

                    var dlg = new AdventureThreadManagerDialog(bundle!.Metadata.Id, CreateNoOpActions());
                    dlg.Show();
                    dlg.UpdateLayout();
                    dlg.Close();
                }
                finally
                {
                    AppDirectories.ResetStoresForTests();
                    AppDirectories.TestRootOverride = null;
                    AppDirectories.ApplyAdventuresDirectoryOverride(null);
                }
            },
            TimeSpan.FromSeconds(30));
    }

    private static AdventureThreadManagerActions CreateNoOpActions() =>
        new()
        {
            StartNarrativeFromSourcesAsync = () => Task.CompletedTask,
            OpenPlayHandoffWizardAsync = () => Task.CompletedTask,
            StartNewDesignThreadAsync = () => Task.CompletedTask,
            CreateThreadSlotAsync = _ => Task.FromResult<Guid?>(null),
            ActivateEntryAsync = (_, _) => Task.CompletedTask,
            OpenEntryAsync = (_, _) => Task.CompletedTask,
            OpenProjectWorkspaceAsync = () => Task.CompletedTask,
            PinTabToEntryAsync = (_, _, _) => Task.CompletedTask,
            ClearEntryPinAsync = (_, _) => Task.CompletedTask,
            RemoveEntryAsync = _ => Task.CompletedTask,
            ProbeUtilityWorkerAsync = () => Task.CompletedTask,
            SetupUtilityWorkerAsync = () => Task.CompletedTask,
            SetupUtilityWorkerReplaceAsync = _ => Task.CompletedTask,
            PinUtilityWorkerFromCurrentTabAsync = () => Task.CompletedTask,
            OpenUtilityWorkerAsync = () => Task.CompletedTask,
        };
}
