using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Views;

public partial class ProjectLinkWizard : Window
{
    private readonly Guid _adventureId;
    private readonly Func<WebView2?> _getWebView;
    private readonly Func<AdventureProjectBindingService?> _getBindingService;
    private readonly Func<Task>? _prepareSession;
    private List<GizmoSummary> _projects = [];

    public bool LinkedSuccessfully { get; private set; }

    public ProjectLinkWizard(
        Guid adventureId,
        Func<WebView2?> getWebView,
        Func<AdventureProjectBindingService?> getBindingService,
        Func<Task>? prepareSession = null)
    {
        _adventureId = adventureId;
        _getWebView = getWebView;
        _getBindingService = getBindingService;
        _prepareSession = prepareSession;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RefreshProjectsAsync();
    }

    private bool _refreshInProgress;
    private bool _linkInFlight;

    private async Task RefreshProjectsAsync()
    {
        if (_refreshInProgress)
            return;

        _refreshInProgress = true;
        RefreshButton.IsEnabled = false;
        StatusLine.Text = "Loading projects…";
        ErrorLine.Text = "";

        try
        {
            if (_prepareSession is not null)
            {
                StatusLine.Text = "Connecting to ChatGPT…";
                await _prepareSession();
            }

            var wv = _getWebView();
            var binding = _getBindingService();
            if (wv?.CoreWebView2 is not { } core || binding is null)
            {
                StatusLine.Text = "ChatGPT tab not ready. Sign in on the ChatGPT tab, then click Refresh.";
                return;
            }

            if (wv.CoreWebView2 is null)
                await wv.EnsureCoreWebView2Async();

            _projects = (await binding.ListProjectsAsync(core)).ToList();
            ProjectList.ItemsSource = _projects;
            StatusLine.Text = _projects.Count == 0
                ? "No projects found. Use the ChatGPT tab (signed in), create a Project at chatgpt.com, then Refresh. Diagnostics: %LocalAppData%\\ChatGPTWrapper\\last-sidebar-probe.json"
                : $"{_projects.Count} project(s) loaded.";
        }
        catch (ChatGptApiException ex)
        {
            StatusLine.Text = "API error.";
            ErrorLine.Text = ex.Message;
        }
        catch (Exception ex)
        {
            StatusLine.Text = "Failed to load projects.";
            ErrorLine.Text = ex.Message;
        }
        finally
        {
            _refreshInProgress = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshProjectsAsync();

    private async void Link_Click(object sender, RoutedEventArgs e)
    {
        if (_linkInFlight)
            return;

        _linkInFlight = true;
        ErrorLine.Text = "";
        LinkButton.IsEnabled = false;

        try
        {
            var bundle = AdventureStore.Load(_adventureId);
            if (bundle is null)
            {
                ErrorLine.Text = "Adventure not found.";
                return;
            }

            if (ModeTabs.SelectedIndex == 1
                && AdventureProjectBindingService.BlocksCreateWhenAlreadyLinked(bundle.Metadata.LinkedProjectId))
            {
                ErrorLine.Text =
                    $"Already linked to {bundle.Metadata.LinkedProjectId}. Select the existing project instead of creating a new one.";
                return;
            }

            var wv = _getWebView();
            var binding = _getBindingService();
            if (wv?.CoreWebView2 is not { } core || binding is null)
            {
                ErrorLine.Text = "ChatGPT WebView is not ready. Start Play mode and log in to chatgpt.com.";
                return;
            }

            if (wv.CoreWebView2 is null)
                await wv.EnsureCoreWebView2Async();

            const bool sync = false;
            var createThread = CreateThreadCheck.IsChecked == true;
            ProjectBindingResult result;

            if (ModeTabs.SelectedIndex == 1)
            {
                var name = string.IsNullOrWhiteSpace(NewProjectNameBox.Text)
                    ? bundle.Metadata.Title
                    : NewProjectNameBox.Text.Trim();
                result = await binding.CreateAndLinkAsync(core, bundle, name, sync, createThread);
            }
            else
            {
                if (ProjectList.SelectedItem is not GizmoSummary picked)
                {
                    ErrorLine.Text = "Select a project from the list.";
                    return;
                }

                var updateInstr = UpdateInstructionsCheck.IsChecked == true;
                result = await binding.LinkExistingAsync(
                    core,
                    bundle,
                    picked.Id,
                    sync,
                    updateInstr,
                    createThread,
                    projectTitle: picked.Title,
                    existingProjectFiles: picked.Files);
            }

            if (!result.Success)
            {
                ErrorLine.Text = result.Error ?? "Link failed.";
                return;
            }

            LinkedSuccessfully = true;
            if (!string.IsNullOrWhiteSpace(result.Error))
                MessageBox.Show(this, result.Error, "Linked with warnings", MessageBoxButton.OK, MessageBoxImage.Warning);

            DialogResult = true;
            Close();
        }
        catch (ChatGptApiException ex)
        {
            ErrorLine.Text = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
        }
        finally
        {
            _linkInFlight = false;
            LinkButton.IsEnabled = true;
        }
    }
}
