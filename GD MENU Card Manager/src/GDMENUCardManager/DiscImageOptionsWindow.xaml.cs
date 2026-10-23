using System;
using System.ComponentModel;
using GDMENUCardManager.Core;
using System.Windows;

namespace GDMENUCardManager
{
    public partial class DiscImageOptionsWindow : Window
    {
        // Store original values to restore on Cancel
        private bool _originalEnableGDIShrink;
        private bool _originalEnableGDIShrinkExisting;
        private bool _originalEnableGDIShrinkCompressed;
        private bool _originalEnableGDIShrinkBlackList;
        private bool _originalEnableVgaPatch;
        private bool _originalEnableVgaPatchExisting;
        private bool _originalEnableRegionPatch;
        private bool _originalEnableRegionPatchExisting;
        private DiscConversionFormat _originalConversionFormat;
        private bool _saved;
        private readonly Action _saveConfigCallback;

        public DiscImageOptionsWindow()
        {
            InitializeComponent();
            this.Loaded += DiscImageOptionsWindow_Loaded;
            this.Closing += DiscImageOptionsWindow_Closing;
            this.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        public DiscImageOptionsWindow(Action saveConfigCallback) : this()
        {
            _saveConfigCallback = saveConfigCallback;
        }

        private void DiscImageOptionsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Capture original values when window opens
            if (DataContext is IDiscImageOptionsViewModel vm)
            {
                _originalEnableGDIShrink = vm.EnableGDIShrink;
                _originalEnableGDIShrinkExisting = vm.EnableGDIShrinkExisting;
                _originalEnableGDIShrinkCompressed = vm.EnableGDIShrinkCompressed;
                _originalEnableGDIShrinkBlackList = vm.EnableGDIShrinkBlackList;
                _originalEnableVgaPatch = vm.EnableVgaPatch;
                _originalEnableVgaPatchExisting = vm.EnableVgaPatchExisting;
                _originalEnableRegionPatch = vm.EnableRegionPatch;
                _originalEnableRegionPatchExisting = vm.EnableRegionPatchExisting;
                _originalConversionFormat = vm.ConversionFormat;
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
                vm.EnableGDIShrink = _originalEnableGDIShrink;
                vm.EnableGDIShrinkExisting = _originalEnableGDIShrinkExisting;
                vm.EnableGDIShrinkCompressed = _originalEnableGDIShrinkCompressed;
                vm.EnableGDIShrinkBlackList = _originalEnableGDIShrinkBlackList;
                vm.EnableVgaPatch = _originalEnableVgaPatch;
                vm.EnableVgaPatchExisting = _originalEnableVgaPatchExisting;
                vm.EnableRegionPatch = _originalEnableRegionPatch;
                vm.EnableRegionPatchExisting = _originalEnableRegionPatchExisting;
                vm.ConversionFormat = _originalConversionFormat;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _saved = true;
            _saveConfigCallback?.Invoke();
            Close();
        }

    }

    // Shared by the WPF and Avalonia windows so Core can read the options back.
    public interface IDiscImageOptionsViewModel
    {
        bool EnableGDIShrink { get; set; }
        bool EnableGDIShrinkExisting { get; set; }
        bool EnableGDIShrinkCompressed { get; set; }
        bool EnableGDIShrinkBlackList { get; set; }
        bool EnableVgaPatch { get; set; }
        bool EnableVgaPatchExisting { get; set; }
        bool EnableRegionPatch { get; set; }
        bool EnableRegionPatchExisting { get; set; }
        DiscConversionFormat ConversionFormat { get; set; }
        bool UseCcdConversion { get; set; }
        bool UseCdiConversion { get; set; }
    }
}
