using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChatGPTWrapper.Format;

public partial class FormatSystemFontPickerWindow : Window
{
    private readonly List<FontFamily> _allFonts;

    public string? SelectedFontFamilyName { get; private set; }

    public FormatSystemFontPickerWindow()
    {
        InitializeComponent();
        _allFonts = Fonts.SystemFontFamilies
            .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FontList.ItemsSource = _allFonts;
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        FontList.ItemsSource = string.IsNullOrEmpty(query)
            ? _allFonts
            : _allFonts.Where(f => f.Source.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontList.SelectedItem is not FontFamily family)
            return;

        PreviewText.FontFamily = family;
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (FontList.SelectedItem is not FontFamily family)
        {
            DialogResult = false;
            Close();
            return;
        }

        SelectedFontFamilyName = family.Source;
        DialogResult = true;
        Close();
    }
}
