using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class EntityRetirePage : UserControl
{
    public EntityRetirePage(EntityReferenceRow row)
    {
        InitializeComponent();
        RetireSummaryLine.Text = $"Retire “{row.Name}” from active cast/lore?";
    }

    public bool AliasOnly => AliasOnlyCheck.IsChecked == true;
}
