using System.Windows;

namespace ChatGPTWrapper.Views;

public partial class EntityEditDialog : Window
{
    public string EntityName { get; private set; } = "";

    public string EntityRole { get; private set; } = "";

    public string EntityDescription { get; private set; } = "";

    public bool EntityPinned { get; private set; }

    public EntityEditDialog(string name, string role, string description, bool pinned, bool showPinned)
    {
        InitializeComponent();
        NameBox.Text = name;
        RoleBox.Text = role;
        DescriptionBox.Text = description;
        PinnedCheck.IsChecked = pinned;
        PinnedCheck.Visibility = showPinned ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        EntityName = NameBox.Text.Trim();
        EntityRole = RoleBox.Text.Trim();
        EntityDescription = DescriptionBox.Text.Trim();
        EntityPinned = PinnedCheck.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
