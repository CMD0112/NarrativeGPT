using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class AdventureThreadManagerDialogTests
{
    [Fact]
    public void Constructor_loads_without_xaml_exception()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new System.Windows.Application();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/ChatGPT Wrapper;component/Themes/WrapperChrome.xaml", UriKind.Relative),
                    });
                    Theme.ThemeApplicationService.ApplyToWpf(
                        Theme.ThemeApplicationService.ResolveEffectiveTheme(
                            Theme.ThemeApplicationService.CreateDefaultSettings()));
                }

                var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
                AdventureThreadRegistryService.EnsureMigrated(bundle);
                AdventureStore.Save(bundle);

                var actions = new AdventureThreadManagerActions
                {
                    StartNarrativeFromSourcesAsync = () => Task.CompletedTask,
                    OpenPlayHandoffWizardAsync = () => Task.CompletedTask,
                    StartNewDesignThreadAsync = () => Task.CompletedTask,
                    ActivateEntryAsync = (_, _) => Task.CompletedTask,
                    OpenEntryAsync = (_, _) => Task.CompletedTask,
                    OpenProjectWorkspaceAsync = () => Task.CompletedTask,
                    PinCurrentTabAsync = _ => Task.CompletedTask,
                    ProbeUtilityWorkerAsync = () => Task.CompletedTask,
                    SetupUtilityWorkerAsync = () => Task.CompletedTask,
                    SetupUtilityWorkerReplaceAsync = _ => Task.CompletedTask,
                    PinCurrentTabAsUtilityWorkerAsync = () => Task.CompletedTask,
                    OpenUtilityWorkerAsync = () => Task.CompletedTask,
                };

                var dlg = new AdventureThreadManagerDialog(bundle.Metadata.Id, actions);
                Assert.Equal("Manage adventure threads", dlg.Title);
                dlg.Show();
                dlg.UpdateLayout();
                dlg.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));
        if (failure is not null)
            throw failure;
    }

    [Fact]
    public void Constructor_loads_with_user_chrome_theme_and_adventure()
    {
        var configRoot = @"C:\Users\Crimi\AppData\Local\ChatGPTWrapper";
        if (!Directory.Exists(configRoot))
            return;

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppDirectories.TestRootOverride = configRoot;
                if (System.Windows.Application.Current is null)
                {
                    var app = new System.Windows.Application();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/ChatGPT Wrapper;component/Themes/WrapperChrome.xaml", UriKind.Relative),
                    });
                }

                var chrome = UiChromeStore.Load();
                var resolved = Theme.ThemeApplicationService.ResolveEffectiveTheme(chrome.Theme);
                Theme.ThemeApplicationService.InvalidateApplyCache();
                Theme.ThemeApplicationService.ApplyToWpf(resolved);

                AppDirectories.ApplyAdventuresDirectoryOverride(@"E:\Documents\ChatGPT Wrapper\Adventures");
                var bundle = AdventureStore.Load(Guid.Parse("d84ea627-2663-478c-ab8b-6021af78d9d3"));
                Assert.NotNull(bundle);

                var actions = new AdventureThreadManagerActions
                {
                    StartNarrativeFromSourcesAsync = () => Task.CompletedTask,
                    OpenPlayHandoffWizardAsync = () => Task.CompletedTask,
                    StartNewDesignThreadAsync = () => Task.CompletedTask,
                    ActivateEntryAsync = (_, _) => Task.CompletedTask,
                    OpenEntryAsync = (_, _) => Task.CompletedTask,
                    OpenProjectWorkspaceAsync = () => Task.CompletedTask,
                    PinCurrentTabAsync = _ => Task.CompletedTask,
                    ProbeUtilityWorkerAsync = () => Task.CompletedTask,
                    SetupUtilityWorkerAsync = () => Task.CompletedTask,
                    SetupUtilityWorkerReplaceAsync = _ => Task.CompletedTask,
                    PinCurrentTabAsUtilityWorkerAsync = () => Task.CompletedTask,
                    OpenUtilityWorkerAsync = () => Task.CompletedTask,
                };

                var dlg = new AdventureThreadManagerDialog(bundle!.Metadata.Id, actions);
                dlg.Show();
                dlg.UpdateLayout();
                dlg.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                AppDirectories.TestRootOverride = null;
                AppDirectories.ApplyAdventuresDirectoryOverride(null);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        if (failure is not null)
            throw failure;
    }
}
