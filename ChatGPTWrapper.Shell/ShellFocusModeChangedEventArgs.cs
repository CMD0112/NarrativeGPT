namespace ChatGPTWrapper.Shell;

public sealed class ShellFocusModeChangedEventArgs(bool focused) : EventArgs
{
    public bool FocusChat { get; } = focused;
}
