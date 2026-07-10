using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed class PlaySettingsNavTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplate? ItemTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        Select(item);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        Select(item);

    private DataTemplate? Select(object item) =>
        item is PlaySettingsNavItem nav && nav.IsHeader
            ? HeaderTemplate
            : ItemTemplate;
}
