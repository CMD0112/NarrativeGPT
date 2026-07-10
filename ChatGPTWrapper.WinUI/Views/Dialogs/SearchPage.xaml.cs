using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class SearchPage : UserControl
{
    private readonly AdventureBundle _bundle;

    public SearchPage(AdventureBundle bundle)
    {
        _bundle = bundle;
        InitializeComponent();
    }

    private void QueryBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        ResultsList.ItemsSource = SearchService.Search(_bundle, QueryBox.Text);
    }
}
