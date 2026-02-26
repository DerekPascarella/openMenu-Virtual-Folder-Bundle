using System;
using System.ComponentModel;
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
        private bool _saved;
        private readonly Action _saveConfigCallback;

        public DiscImageOptionsWindow()
        {
            InitializeComponent();
            this.Loaded += DiscImageOptionsWindow_Loaded;
            this.Closing += DiscImageOptionsWindow_Closing;
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
        bool EnableGDIShrink { get; set; }
        bool EnableGDIShrinkExisting { get; set; }
        bool EnableGDIShrinkCompressed { get; set; }
        bool EnableGDIShrinkBlackList { get; set; }
        bool EnableVgaPatch { get; set; }
        bool EnableVgaPatchExisting { get; set; }
        bool EnableRegionPatch { get; set; }
        bool EnableRegionPatchExisting { get; set; }
    }
}
