using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChatGPTWrapper.Controls;

public partial class ActionListRow : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ActionListRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(ActionListRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RunCommandProperty =
        DependencyProperty.Register(nameof(RunCommand), typeof(ICommand), typeof(ActionListRow), new PropertyMetadata(null));

    public static readonly DependencyProperty RowEnabledProperty =
        DependencyProperty.Register(nameof(RowEnabled), typeof(bool), typeof(ActionListRow), new PropertyMetadata(true));

    public static readonly DependencyProperty DisabledReasonProperty =
        DependencyProperty.Register(nameof(DisabledReason), typeof(string), typeof(ActionListRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LeadingIconProperty =
        DependencyProperty.Register(nameof(LeadingIcon), typeof(string), typeof(ActionListRow), new PropertyMetadata(string.Empty));

    public ActionListRow()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public ICommand? RunCommand
    {
        get => (ICommand?)GetValue(RunCommandProperty);
        set => SetValue(RunCommandProperty, value);
    }

    public bool RowEnabled
    {
        get => (bool)GetValue(RowEnabledProperty);
        set => SetValue(RowEnabledProperty, value);
    }

    public string DisabledReason
    {
        get => (string)GetValue(DisabledReasonProperty);
        set => SetValue(DisabledReasonProperty, value);
    }

    public string LeadingIcon
    {
        get => (string)GetValue(LeadingIconProperty);
        set => SetValue(LeadingIconProperty, value);
    }
}
