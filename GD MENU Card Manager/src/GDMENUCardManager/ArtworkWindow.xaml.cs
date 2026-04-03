using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GDMENUCardManager.Core;
using Microsoft.Win32;

namespace GDMENUCardManager
{
    public partial class ArtworkWindow : Window, INotifyPropertyChanged
    {
        private GdItem _item;
        private readonly Core.Manager _manager;
        private byte[] _pendingPvrData;      // 256x256 for BOX.DAT
        private byte[] _pendingIconPvrData;  // 128x128 for ICON.DAT
        private bool _deleteRequested;

        // Original artwork data for undo
        private byte[] _originalBoxPvrData;
        private byte[] _originalIconPvrData;

        // Navigation
        private readonly IList<GdItem> _navigableItems;
        private int _currentIndex;
        private int _keyHoldCount;

        public event PropertyChangedEventHandler PropertyChanged;

        private string _serial;
        public string Serial
        {
            get => _serial;
            private set { _serial = value; RaisePropertyChanged(); }
        }
        public string WindowTitle => $"Artwork - {_item.Name}";

        public bool CanNavigatePrev => _navigableItems != null && _currentIndex > 0;
        public bool CanNavigateNext => _navigableItems != null && _currentIndex >= 0 && _currentIndex < _navigableItems.Count - 1;

        private BitmapSource _previewImage;
        public BitmapSource PreviewImage
        {
            get => _previewImage;
            set { _previewImage = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasPreviewImage)); }
        }

        public bool HasPreviewImage => _previewImage != null;

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { _hasUnsavedChanges = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(CanDelete)); }
        }

        public bool CanDelete => !HasUnsavedChanges && _manager.BoxDat?.HasArtworkForSerial(Serial) == true;

        public ArtworkWindow(GdItem item, Core.Manager manager, IList<GdItem> navigableItems = null)
        {
            InitializeComponent();

            _manager = manager;
            _navigableItems = navigableItems;
            _currentIndex = navigableItems?.IndexOf(item) ?? -1;

            _item = item;
            Serial = BoxDatManager.NormalizeSerial(item.ProductNumber);

            // Capture original artwork data for undo
            _originalBoxPvrData = _manager.BoxDat?.GetPvrDataForSerial(Serial);
            _originalIconPvrData = _manager.IconDat?.GetPvrDataForSerial(Serial);

            LoadCurrentArtwork();

            this.Closing += ArtworkWindow_Closing;
            this.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Left || e.Key == System.Windows.Input.Key.Right)
                {
                    int step = 1;
                    if (e.IsRepeat)
                    {
                        _keyHoldCount++;
                        if (_keyHoldCount > 3) step = 5;
                    }
                    else
                    {
                        _keyHoldCount = 0;
                    }

                    if (e.Key == System.Windows.Input.Key.Left) Navigate(-step);
                    else Navigate(step);
                    e.Handled = true;
                }
            };
            this.KeyUp += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
                else if (e.Key == System.Windows.Input.Key.Left || e.Key == System.Windows.Input.Key.Right)
                    _keyHoldCount = 0;
            };
            DataContext = this;
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadCurrentArtwork()
        {
            if (_manager.BoxDat == null || !_manager.BoxDat.IsLoaded)
                return;

            var pvrData = _manager.BoxDat.GetPvrDataForSerial(Serial);
            if (pvrData != null)
            {
                DisplayPvrData(pvrData);
            }
        }

        private void LoadItem(GdItem item)
        {
            _item = item;
            Serial = BoxDatManager.NormalizeSerial(item.ProductNumber);
            RaisePropertyChanged(nameof(WindowTitle));

            // Reset pending state
            _pendingPvrData = null;
            _pendingIconPvrData = null;
            _deleteRequested = false;
            HasUnsavedChanges = false;
            PreviewImage = null;

            // Capture original artwork data for undo
            _originalBoxPvrData = _manager.BoxDat?.GetPvrDataForSerial(Serial);
            _originalIconPvrData = _manager.IconDat?.GetPvrDataForSerial(Serial);

            LoadCurrentArtwork();

            RaisePropertyChanged(nameof(CanNavigatePrev));
            RaisePropertyChanged(nameof(CanNavigateNext));
        }

        private bool PromptUnsavedChanges()
        {
            if (!HasUnsavedChanges)
                return true;

            var result = MessageBox.Show(
                "You have unsaved changes. Save before navigating?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.Yes)
                SaveChanges();
            return true;
        }

        private void Navigate(int step)
        {
            if (_navigableItems == null || !PromptUnsavedChanges())
                return;

            int newIndex = Math.Clamp(_currentIndex + step, 0, _navigableItems.Count - 1);
            if (newIndex == _currentIndex)
                return;

            _currentIndex = newIndex;
            LoadItem(_navigableItems[_currentIndex]);
        }

        private void NavigatePrev_Click(object sender, RoutedEventArgs e)
        {
            Navigate(-1);
        }

        private void NavigateNext_Click(object sender, RoutedEventArgs e)
        {
            Navigate(1);
        }

        private void DisplayPvrData(byte[] pvrData)
        {
            try
            {
                var decoded = PvrEncoder.DecodePvr(pvrData);
                if (decoded.HasValue)
                {
                    var (pixels, width, height) = decoded.Value;

                    var bitmap = BitmapSource.Create(
                        width, height,
                        96, 96,
                        PixelFormats.Bgra32,
                        null,
                        pixels,
                        width * 4);

                    bitmap.Freeze();
                    PreviewImage = bitmap;
                }
            }
            catch
            {
                // Silently fail - no preview will be shown
            }
        }

        private async void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Select Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.tiff;*.tga|All Files|*.*"
            };

            if (fileDialog.ShowDialog() == true)
            {
                await LoadAndPreviewImage(fileDialog.FileName);
            }
        }

        private async Task LoadAndPreviewImage(string imagePath)
        {
            try
            {
                // Generate both 256x256 (BOX.DAT) and 128x128 (ICON.DAT) PVRs
                var encodingTasks = await Task.Run(() =>
                {
                    var boxPvr = PvrEncoder.EncodeFromFile(imagePath);
                    var iconPvr = PvrEncoder.EncodeIconFromFile(imagePath);
                    return (boxPvr, iconPvr);
                });

                _pendingPvrData = encodingTasks.boxPvr;
                _pendingIconPvrData = encodingTasks.iconPvr;
                _deleteRequested = false;

                DisplayPvrData(_pendingPvrData);
                HasUnsavedChanges = true;
            }
            catch
            {
                // Silently fail - user can try again
            }
        }

        private void DeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Delete artwork entry for serial '{Serial}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (_manager.BoxDat == null)
                        return;

                    // Capture current data for undo before deleting
                    var oldBoxData = _manager.BoxDat.GetPvrDataForSerial(Serial);
                    var oldIconData = _manager.IconDat?.GetPvrDataForSerial(Serial);

                    // Delete from both BOX.DAT and ICON.DAT (in memory only - written during Save Changes)
                    _manager.BoxDat.DeleteEntryForSerial(Serial);
                    _manager.IconDat?.DeleteEntryForSerial(Serial);

                    // Record undo operation
                    _manager.UndoManager.RecordChange(new ArtworkChangeOperation
                    {
                        Serial = Serial,
                        OldBoxPvrData = oldBoxData,
                        NewBoxPvrData = null,
                        OldIconPvrData = oldIconData,
                        NewIconPvrData = null,
                        BoxDat = _manager.BoxDat,
                        IconDat = _manager.IconDat,
                        RefreshArtworkStatus = _manager.RefreshArtworkStatusForSerial
                    });

                    // Refresh the item's artwork status
                    _manager.RefreshArtworkStatusForSerial(Serial);
                    Close();
                }
                catch
                {
                    // Silently fail
                }
            }
        }

        private void SaveChanges()
        {
            if (_manager.BoxDat == null)
                return;

            byte[] newBoxData = null;
            byte[] newIconData = null;

            if (_deleteRequested)
            {
                // Delete from both DATs (in memory only - written during Save Changes)
                _manager.BoxDat.DeleteEntryForSerial(Serial);
                _manager.IconDat?.DeleteEntryForSerial(Serial);
                // newBoxData and newIconData stay null for delete
            }
            else if (_pendingPvrData != null)
            {
                // Set artwork in both DATs (in memory only - written during Save Changes)
                _manager.BoxDat.SetArtworkForSerial(Serial, _pendingPvrData);
                newBoxData = _pendingPvrData;
                if (_pendingIconPvrData != null && _manager.IconDat != null)
                {
                    _manager.IconDat.SetIconForSerial(Serial, _pendingIconPvrData);
                    newIconData = _pendingIconPvrData;
                }
            }

            // Record undo operation
            _manager.UndoManager.RecordChange(new ArtworkChangeOperation
            {
                Serial = Serial,
                OldBoxPvrData = _originalBoxPvrData,
                NewBoxPvrData = newBoxData,
                OldIconPvrData = _originalIconPvrData,
                NewIconPvrData = newIconData,
                BoxDat = _manager.BoxDat,
                IconDat = _manager.IconDat,
                RefreshArtworkStatus = _manager.RefreshArtworkStatusForSerial
            });

            HasUnsavedChanges = false;
            _pendingPvrData = null;
            _pendingIconPvrData = null;
            _deleteRequested = false;

            // Refresh the item's artwork status
            _manager.RefreshArtworkStatusForSerial(Serial);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveChanges();
                Close();
            }
            catch
            {
                // Silently fail
            }
        }

        private void ArtworkWindow_Closing(object sender, CancelEventArgs e)
        {
            if (HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Discard them?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
