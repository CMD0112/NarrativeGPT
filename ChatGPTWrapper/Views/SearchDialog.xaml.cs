using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class SearchDialog : Window
{
    private readonly AdventureBundle _bundle;

    public SearchDialog(AdventureBundle bundle)
    {
        InitializeComponent();
        _bundle = bundle;
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ResultsList.ItemsSource = SearchService.Search(_bundle, QueryBox.Text);
    }
}
