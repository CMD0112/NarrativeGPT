using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

/// <summary>
/// Arranges workbench section cards into a single column or paired 2-up rows.
/// </summary>
internal static class PlaySettingsCardGridLayout
{
    public static void Apply(
        Grid grid,
        IReadOnlyList<FrameworkElement> cards,
        IReadOnlyList<bool> spanFullWidth,
        double availableWidth,
        double? twoUpBreakpoint = null)
    {
        var breakpoint = twoUpBreakpoint ?? PlaySettingsWorkbenchLayout.CurrentViewport.CardGridTwoUpBreakpoint;
        if (cards.Count == 0)
            return;

        var twoUp = availableWidth >= breakpoint;
        if (grid.ColumnDefinitions.Count < 2)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        grid.RowDefinitions.Clear();

        var row = 0;
        for (var i = 0; i < cards.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var card = cards[i];
            var spansFull = i < spanFullWidth.Count && spanFullWidth[i];

            if (twoUp
                && !spansFull
                && i + 1 < cards.Count
                && (i + 1 >= spanFullWidth.Count || !spanFullWidth[i + 1]))
            {
                Grid.SetRow(card, row);
                Grid.SetColumn(card, 0);
                Grid.SetColumnSpan(card, 1);

                var next = cards[i + 1];
                Grid.SetRow(next, row);
                Grid.SetColumn(next, 1);
                Grid.SetColumnSpan(next, 1);
                i++;
            }
            else
            {
                Grid.SetRow(card, row);
                Grid.SetColumn(card, 0);
                Grid.SetColumnSpan(card, twoUp ? 2 : 1);
            }

            row++;
        }
    }
}
