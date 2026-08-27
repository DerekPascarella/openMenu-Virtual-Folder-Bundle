using System;
using System.Windows;
using System.Windows.Interop;

namespace GDMENUCardManager
{
    internal sealed class Win32Window : System.Windows.Forms.IWin32Window
    {
        public Win32Window(Window window)
        {
            Handle = new WindowInteropHelper(window).Handle;
        }

        public IntPtr Handle { get; }
    }
}
