using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.ComponentModel;

namespace GDMENUCardManager
{
    public partial class DiscImageOptionsWindow : Window
    {
        // Store original values to restore on Cancel
        private bool _originalEnableVgaPatch;
        private bool _originalEnableVgaPatchExisting;
        private bool _originalEnableRegionPatch;
        private bool _originalEnableRegionPatchExisting;
        private bool _saved;
        private readonly Action _saveConfigCallback;

        public DiscImageOptionsWindow()
        {
            InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
#endif
            this.Opened += DiscImageOptionsWindow_Opened;
            this.Closing += DiscImageOptionsWindow_Closing;
        }

        public DiscImageOptionsWindow(Action saveConfigCallback) : this()
        {
            _saveConfigCallback = saveConfigCallback;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void DiscImageOptionsWindow_Opened(object sender, EventArgs e)
        {
            // Capture original values when window opens
            if (DataContext is IDiscImageOptionsViewModel vm)
            {
                _originalEnableVgaPatch = vm.EnableVgaPatch;
                _originalEnableVgaPatchExisting = vm.EnableVgaPatchExisting;
                _originalEnableRegionPatch = vm.EnableRegionPatch;
                _originalEnableRegionPatchExisting = vm.EnableRegionPatchExisting;
            }
        }

        private void DiscImageOptionsWindow_Closing(object sender, CancelEventArgs e)
        {
            // If not saved, restore original values
            if (!_saved)
            {
                RestoreOriginalValues();
            }
        }

        private void RestoreOriginalValues()
        {
            if (DataContext is IDiscImageOptionsViewModel vm)
            {
                vm.EnableVgaPatch = _originalEnableVgaPatch;
                vm.EnableVgaPatchExisting = _originalEnableVgaPatchExisting;
                vm.EnableRegionPatch = _originalEnableRegionPatch;
                vm.EnableRegionPatchExisting = _originalEnableRegionPatchExisting;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _saved = true;
            _saveConfigCallback?.Invoke();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _saved = false;
            Close();
        }
    }

    // Interface for the view model to allow accessing the properties
    public interface IDiscImageOptionsViewModel
    {
        bool EnableVgaPatch { get; set; }
        bool EnableVgaPatchExisting { get; set; }
        bool EnableRegionPatch { get; set; }
        bool EnableRegionPatchExisting { get; set; }
    }
}
