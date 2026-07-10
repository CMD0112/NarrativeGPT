using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ChatGPTWrapper.WinUI.Helpers;

internal static class WinUiListFlyoutHelper
{
    public static void SelectItemUnderPointer(object sender, RightTappedRoutedEventArgs e)
    {
        if (FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource) is { } item)
        {
            item.IsSelected = true;
            e.Handled = true;
        }
    }

    public static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
                return match;
            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }
}
