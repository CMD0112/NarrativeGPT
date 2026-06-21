using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class RandomTableDialog : Window
{
    private readonly AdventureBundle _bundle;
    private readonly RandomTablesDocument _tables;
    private readonly Random _rng = new();

    public string? LastRoll { get; private set; }

    public bool AppendToComposer => AppendCheckBox.IsChecked == true;

    public RandomTableDialog(AdventureBundle bundle)
    {
        _bundle = bundle;
        InitializeComponent();
        _tables = AdventureRandomTablesStore.Load(bundle);
        RefreshTableList();
    }

    private void RefreshTableList()
    {
        var selected = TableBox.SelectedItem as string;
        TableBox.ItemsSource = _tables.Tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(selected) && _tables.Tables.ContainsKey(selected))
            TableBox.SelectedItem = selected;
        else if (TableBox.Items.Count > 0)
            TableBox.SelectedIndex = 0;
    }

    private void Roll_Click(object sender, RoutedEventArgs e)
    {
        if (TableBox.SelectedItem is not string key
            || !_tables.Tables.TryGetValue(key, out var entries)
            || entries.Count == 0)
        {
            ResultBlock.Text = "No entries in this table.";
            UseButton.IsEnabled = false;
            return;
        }

        LastRoll = entries[_rng.Next(entries.Count)];
        ResultBlock.Text = LastRoll;
        UseButton.IsEnabled = !string.IsNullOrWhiteSpace(LastRoll);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LastRoll))
            return;

        DialogResult = true;
        Close();
    }

    private void ManageTables_Click(object sender, RoutedEventArgs e)
    {
        ManageTableList.ItemsSource = _tables.Tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (ManageTableList.Items.Count > 0)
            ManageTableList.SelectedIndex = 0;
        ManagePanel.Visibility = Visibility.Visible;
    }

    private void ManageTableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ManageTableList.SelectedItem is not string key
            || !_tables.Tables.TryGetValue(key, out var entries))
        {
            ManageEntriesBox.Text = "";
            return;
        }

        ManageEntriesBox.Text = string.Join(Environment.NewLine, entries);
    }

    private void AddTable_Click(object sender, RoutedEventArgs e)
    {
        var baseName = "table";
        var name = baseName;
        var i = 1;
        while (_tables.Tables.ContainsKey(name))
            name = $"{baseName}_{++i}";

        _tables.Tables[name] = ["entry 1", "entry 2"];
        ManageTableList.ItemsSource = _tables.Tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        ManageTableList.SelectedItem = name;
    }

    private void RenameTable_Click(object sender, RoutedEventArgs e)
    {
        if (ManageTableList.SelectedItem is not string key)
            return;

        if (!TextPromptDialog.TryPrompt(this, "Rename table", "New table name:", key, out var trimmed, confirmButtonText: "Rename"))
            return;

        if (string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase))
            return;

        if (_tables.Tables.ContainsKey(trimmed))
        {
            MessageBox.Show(this, "A table with that name already exists.", "Rename table");
            return;
        }

        _tables.Tables[trimmed] = _tables.Tables[key];
        _tables.Tables.Remove(key);
        ManageTableList.ItemsSource = _tables.Tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        ManageTableList.SelectedItem = trimmed;
    }

    private void DeleteTable_Click(object sender, RoutedEventArgs e)
    {
        if (ManageTableList.SelectedItem is not string key)
            return;

        if (MessageBox.Show(this, $"Delete table \"{key}\"?", "Delete table", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        _tables.Tables.Remove(key);
        ManageTableList.ItemsSource = _tables.Tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        ManageEntriesBox.Text = "";
    }

    private void ManageDone_Click(object sender, RoutedEventArgs e)
    {
        if (ManageTableList.SelectedItem is string key)
            SaveManageEntries(key);

        AdventureRandomTablesStore.Save(_bundle, _tables);
        ManagePanel.Visibility = Visibility.Collapsed;
        RefreshTableList();
    }

    private void SaveManageEntries(string key)
    {
        var lines = ManageEntriesBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        _tables.Tables[key] = lines.Count > 0 ? lines : [""];
    }
}
