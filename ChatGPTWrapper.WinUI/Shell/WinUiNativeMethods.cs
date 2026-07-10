using System.Runtime.InteropServices;

namespace ChatGPTWrapper.WinUI.Shell;

internal static class WinUiNativeMethods
{
    internal const int GwlHwndParent = -8;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(IntPtr hWnd, bool enable);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
