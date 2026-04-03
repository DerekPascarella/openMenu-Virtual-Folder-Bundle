using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class ArtworkWindow : Window, INotifyPropertyChanged
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
        private bool _isNavigating;

        public new event PropertyChangedEventHandler PropertyChanged;

        private string _serial;
        public string Serial
        {
            get => _serial;
            private set { _serial = value; RaisePropertyChanged(); }
        }
        public string WindowTitle => $"Artwork - {_item.Name}";

        public bool CanNavigatePrev => _navigableItems != null && _currentIndex > 0;
        public bool CanNavigateNext => _navigableItems != null && _currentIndex >= 0 && _currentIndex < _navigableItems.Count - 1;

        private Bitmap _previewImage;
        public Bitmap PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage?.Dispose();
                _previewImage = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasPreviewImage));
            }
        }

        public bool HasPreviewImage => _previewImage != null;

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { _hasUnsavedChanges = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(CanDelete)); }
        }

        public bool CanDelete => !HasUnsavedChanges && _manager.BoxDat?.HasArtworkForSerial(Serial) == true;

        public ArtworkWindow()
        {
            InitializeComponent();
        }

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
                if (e.Key == Avalonia.Input.Key.Left || e.Key == Avalonia.Input.Key.Right)
                {
                    _keyHoldCount++;
                    int step = _keyHoldCount > 4 ? 5 : 1;

                    if (e.Key == Avalonia.Input.Key.Left) Navigate(-step);
                    else Navigate(step);
                    e.Handled = true;
                }
            };
            this.KeyUp += (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Escape) Close();
                else if (e.Key == Avalonia.Input.Key.Left || e.Key == Avalonia.Input.Key.Right)
                    _keyHoldCount = 0;
            };
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
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

        private async Task<bool> PromptUnsavedChanges()
        {
            if (!HasUnsavedChanges)
                return true;

            var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
            {
                ContentTitle = "Unsaved Changes",
                ContentMessage = "You have unsaved changes. Save before navigating?",
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Save" },
                    new ButtonDefinition { Name = "Discard" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowDialog(this);

            if (result == "Cancel")
                return false;

            if (result == "Save")
                SaveChanges();

            return true;
        }

        private async void Navigate(int step)
        {
            if (_isNavigating) return;
            _isNavigating = true;
            try
            {
                if (_navigableItems == null || !await PromptUnsavedChanges())
                    return;

                int newIndex = Math.Clamp(_currentIndex + step, 0, _navigableItems.Count - 1);
                if (newIndex == _currentIndex)
                    return;

                _currentIndex = newIndex;
                LoadItem(_navigableItems[_currentIndex]);
            }
            finally
            {
                _isNavigating = false;
            }
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

                    using var writeableBitmap = new WriteableBitmap(
                        new PixelSize(width, height),
                        new Vector(96, 96),
                        Avalonia.Platform.PixelFormat.Bgra8888,
                        Avalonia.Platform.AlphaFormat.Unpremul);

                    using (var l = writeableBitmap.Lock())
                    {
                        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, l.Address, pixels.Length);
                    }

                    using var memory = new MemoryStream();
                    writeableBitmap.Save(memory);
                    memory.Position = 0;
                    PreviewImage = new Bitmap(memory);
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
                AllowMultiple = false,
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = "Image Files",
                        Extensions = new List<string> { "png", "jpg", "jpeg", "gif", "webp", "bmp", "tiff", "tga" }
                    }
                }
            };

            var result = await fileDialog.ShowAsync(this);
            if (result != null && result.Length > 0)
            {
                await LoadAndPreviewImage(result[0]);
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

        private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
            {
                ContentTitle = "Confirm Delete",
                ContentMessage = $"Delete artwork entry for serial '{Serial}'?",
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Delete" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowDialog(this);

            if (result == "Delete")
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

        private async void ArtworkWindow_Closing(object sender, CancelEventArgs e)
        {
            if (HasUnsavedChanges)
            {
                e.Cancel = true;

                var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
                {
                    ContentTitle = "Unsaved Changes",
                    ContentMessage = "You have unsaved changes. Discard them?",
                    Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                    ShowInCenter = true,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ButtonDefinitions = new ButtonDefinition[]
                    {
                        new ButtonDefinition { Name = "Discard" },
                        new ButtonDefinition { Name = "Cancel" }
                    }
                }).ShowDialog(this);

                if (result == "Discard")
                {
                    HasUnsavedChanges = false;
                    Close();
                }
            }
        }
    }
}
