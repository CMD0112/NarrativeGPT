using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class EntityEditDialog : Window
{
    public bool Deleted { get; private set; }

    public EntityEditDialog(EntityEditModel model)
    {
        InitializeComponent();
        Title = model.IsNew ? $"New {model.TypeLabel.ToLowerInvariant()}" : model.TypeLabel;
        HeaderTitleBlock.Text = model.IsNew
            ? $"Add {model.TypeLabel.ToLowerInvariant()}"
            : model.Name;
        DeleteButton.Visibility = model.IsNew ? Visibility.Collapsed : Visibility.Visible;

        BuildHeaderLabels(model);
        FormHost.LoadModel(model);
    }

    private void BuildHeaderLabels(EntityEditModel model)
    {
        HeaderLabelsPanel.Children.Clear();
        foreach (var label in model.HeaderLabels)
        {
            HeaderLabelsPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("ShellBadgeStyle"),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                },
            });
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!FormHost.TryHarvestModel(out var validationMessage))
        {
            MessageBox.Show(this, validationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var model = FormHost.Model;
        if (model is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete “{model.Name}”? This cannot be undone.",
                $"Delete {model.TypeLabel.ToLowerInvariant()}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Deleted = true;
        DialogResult = true;
        Close();
    }
}
