using System.Windows;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class RandomTableDialog : Window
{
    private readonly RandomTablesDocument _tables;
    private readonly Random _rng = new();

    public string? LastRoll { get; private set; }

    public RandomTableDialog()
    {
        InitializeComponent();
        _tables = RandomTablesStore.Load();
        TableBox.ItemsSource = _tables.Tables.Keys.ToList();
        if (TableBox.Items.Count > 0)
            TableBox.SelectedIndex = 0;
    }

    private void Roll_Click(object sender, RoutedEventArgs e)
    {
        if (TableBox.SelectedItem is not string key || !_tables.Tables.TryGetValue(key, out var entries) || entries.Count == 0)
            return;

        LastRoll = entries[_rng.Next(entries.Count)];
        ResultBlock.Text = LastRoll;
    }
}
