using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class DesignSessionCockpit : UserControl
{
    private WinUiPlaySessionService? _session;

    public DesignSessionCockpit()
    {
        InitializeComponent();
    }

    public event EventHandler? ManageThreadsRequested;

    public event EventHandler? LinkProjectRequested;

    public event EventHandler<string>? StatusChanged;

    public string BriefNoteText => BriefNoteBox.Text ?? string.Empty;

    public void Bind(WinUiPlaySessionService session)
    {
        _session = session;
        ResyncFromStore();
        session.StatusChanged += (_, _) => ResyncFromStore();
    }

    public void ResyncFromStore()
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        AdventureNavigationService.SyncLinkedFields(bundle);
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        var hasProject = AdventureProjectBindingService.HasLinkedProject(bundle);
        LinkProjectBanner.Visibility = hasProject ? Visibility.Collapsed : Visibility.Visible;

        ThreadStatusLink.Content = AdventureThreadRegistryService.FormatConnectionSummary(bundle);

        var draftBanner = DesignTabPinService.FormatDesignDraftBanner(bundle);
        if (string.IsNullOrWhiteSpace(draftBanner))
        {
            DraftModeBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            DraftModeBanner.Text = draftBanner;
            DraftModeBanner.Visibility = Visibility.Visible;
        }

        var canUseAi = AdventureProjectBindingService.HasLinkedProject(bundle);
        SendStepBriefButton.IsEnabled = canUseAi;
        ExtractButton.IsEnabled = canUseAi;
    }

    private void ThreadStatusLink_Click(object sender, RoutedEventArgs e) =>
        ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

    private void LinkProject_Click(object sender, RoutedEventArgs e) =>
        LinkProjectRequested?.Invoke(this, EventArgs.Empty);

    private async void SendStepBrief_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        SendStepBriefButton.IsEnabled = false;
        try
        {
            var result = await WinUiDesignChatService.SendStepBriefAsync(
                bundle.Metadata.Id,
                BriefNoteText,
                _session);
            StatusChanged?.Invoke(this, WinUiDesignChatService.FormatSendStatus(result));
        }
        finally
        {
            ResyncFromStore();
        }
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        ExtractButton.IsEnabled = false;
        try
        {
            var step = bundle.DesignWorkspace.CurrentStep;
            var result = await WinUiDesignChatService.ExtractStepAsync(
                bundle.Metadata.Id,
                step,
                _session);

            if (result is null)
                StatusChanged?.Invoke(this, "Extract failed.");
            else if (result.Success)
                StatusChanged?.Invoke(this, $"Extracted {result.ProposalCount} proposal(s) — review and accept below.");
            else
                StatusChanged?.Invoke(this, result.Error ?? "Extract failed.");
        }
        finally
        {
            ResyncFromStore();
        }
    }
}
