using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow : Window, INotifyPropertyChanged, GDMENUCardManager.Core.Interface.IProgressWindow
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBTN = 0x10000;
        private const int WS_MINIMIZEBTN = 0x20000;
        private const int WS_SYSMENU = 0x80000;

        private bool _allowClose = false;
        private IntPtr _hwnd = IntPtr.Zero;

        private int _TotalItems;
        public int TotalItems
        {
            get { return _TotalItems; }
            set { _TotalItems = value; RaisePropertyChanged(); }
        }


        private int _ProcessedItems;
        public int ProcessedItems
        {
            get { return _ProcessedItems; }
            set { _ProcessedItems = value; RaisePropertyChanged(); }
        }


        private string _TextContent;
        public string TextContent
        {
            get { return _TextContent; }
            set { _TextContent = value; RaisePropertyChanged(); }
        }


        // Endless scrolling bar by default.
        private bool _IsIndeterminate = true;
        public bool IsIndeterminate
        {
            get { return _IsIndeterminate; }
            set { _IsIndeterminate = value; RaisePropertyChanged(); }
        }


        public ProgressWindow()
        {
            InitializeComponent();
            DataContext = this;

            this.SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                // Remove maximize, minimize, and system menu (close button)
                SetWindowLong(_hwnd, GWL_STYLE, GetWindowLong(_hwnd, GWL_STYLE) & ~(WS_MAXIMIZEBTN | WS_MINIMIZEBTN | WS_SYSMENU));
            };

            this.Closing += (s, e) =>
            {
                // Prevent closing unless explicitly allowed
                if (!_allowClose)
                    e.Cancel = true;
            };
        }

        /// <summary>
        /// Allow the window to be closed (call before Close()).
        /// Also restores the close button so user can dismiss errors.
        /// </summary>
        public void AllowClose()
        {
            _allowClose = true;

            // Restore the system menu (close button) so user can close the window
            // Use Dispatcher to ensure Win32 call runs on UI thread
            if (_hwnd != IntPtr.Zero)
            {
                Dispatcher.Invoke(() =>
                {
                    SetWindowLong(_hwnd, GWL_STYLE, GetWindowLong(_hwnd, GWL_STYLE) | WS_SYSMENU);
                });
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
