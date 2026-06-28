using System.Windows;
using System.Windows.Controls;

namespace ChatGPTWrapper.Views;

public partial class EntityExtendedFieldsEditor : UserControl
{
    private Dictionary<string, string>? _fields;

    public EntityExtendedFieldsEditor()
    {
        InitializeComponent();
    }

    public void LoadFields(Dictionary<string, string> fields)
    {
        _fields = fields;
        RebuildRows();
    }

    public bool TryHarvest(out string? validationMessage)
    {
        validationMessage = null;
        if (_fields is null)
            return true;

        _fields.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in RowsPanel.Children.OfType<Grid>())
        {
            var keyBox = row.Children.OfType<TextBox>().FirstOrDefault(b => ReferenceEquals(b.Tag, "key"));
            var valueBox = row.Children.OfType<TextBox>().FirstOrDefault(b => ReferenceEquals(b.Tag, "value"));
            if (keyBox is null || valueBox is null)
                continue;

            var key = keyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!seen.Add(key))
            {
                validationMessage = $"Duplicate extended field key “{key}”.";
                keyBox.Focus();
                return false;
            }

            _fields[key] = valueBox.Text.Trim();
        }

        return true;
    }

    private void RebuildRows()
    {
        RowsPanel.Children.Clear();
        if (_fields is null)
            return;

        foreach (var (key, value) in _fields.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            RowsPanel.Children.Add(CreateRow(key, value));
    }

    private void AddField_Click(object sender, RoutedEventArgs e) =>
        RowsPanel.Children.Add(CreateRow("", ""));

    private Grid CreateRow(string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keyBox = new TextBox
        {
            Text = key,
            Tag = "key",
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Field key",
        };
        Grid.SetColumn(keyBox, 0);

        var valueBox = new TextBox
        {
            Text = value,
            Tag = "value",
            Margin = new Thickness(0, 0, 6, 0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 28,
            ToolTip = "Field value",
        };
        Grid.SetColumn(valueBox, 1);

        var remove = new Button
        {
            Content = "Remove",
            Padding = new Thickness(8, 2, 8, 2),
            Tag = grid,
        };
        remove.Click += (_, _) => RowsPanel.Children.Remove(grid);
        Grid.SetColumn(remove, 2);

        grid.Children.Add(keyBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(remove);
        return grid;
    }
}
