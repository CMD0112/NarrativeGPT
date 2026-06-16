using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChatGPTWrapper.Views;

internal sealed class TextPromptDialog : Window
{
    private readonly TextBox _input;

    public string ResultText { get; private set; } = "";

    public TextPromptDialog(string title, string prompt, string defaultText = "")
    {
        Title = title;
        Width = 480;
        Height = 240;
        MinWidth = 400;
        MinHeight = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.TryFindResource("DialogBgBrush") is Brush dialogBg)
            Background = dialogBg;
        if (Application.Current.TryFindResource("TextBrush") is Brush textBrush)
            Foreground = textBrush;

        var promptBlock = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _input = new TextBox
        {
            Text = defaultText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var ok = new Button
        {
            Content = "OK",
            MinWidth = 88,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Application.Current.TryFindResource("PrimaryButtonStyle") as Style,
        };
        ok.Click += (_, _) =>
        {
            ResultText = _input.Text.Trim();
            DialogResult = true;
            Close();
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 72, IsCancel = true };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var form = new StackPanel();
        form.Children.Add(promptBlock);
        form.Children.Add(_input);
        form.Children.Add(buttons);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(form);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root,
        };
    }
}
