using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
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

        // 0GDTEX probe results per item, so navigating back is instant
        private readonly Dictionary<GdItem, byte[]> _gdTexCache = new Dictionary<GdItem, byte[]>();
        private CancellationTokenSource _gdTexProbeCts;

        private const string ImportTooltipAvailable = "Import this disc's baked in 0GDTEX.PVR file as assigned artwork.";
        private const string ImportTooltipChecking = "Checking this disc for a 0GDTEX.PVR file...";
        private const string ImportTooltipNotFound = "No 0GDTEX.PVR file was found on this disc";
        private const string ImportTooltipNotGame = "Only Dreamcast game discs contain a 0GDTEX.PVR file";
        private const string ImportTooltipNotReadable = "Not available for compressed or unconverted images until they're saved to the SD card";

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

        private bool _canImportFromDisc;
        public bool CanImportFromDisc
        {
            get => _canImportFromDisc;
            private set { _canImportFromDisc = value; RaisePropertyChanged(); }
        }

        private string _importFromDiscTooltip;
        public string ImportFromDiscTooltip
        {
            get => _importFromDiscTooltip;
            private set { _importFromDiscTooltip = value; RaisePropertyChanged(); }
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
            ProbeGdTex();

            // Widen the window if the button row needs more room than the default width offers
            this.Loaded += (s, e) =>
            {
                ButtonRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double chrome = ActualWidth - ((FrameworkElement)Content).ActualWidth - 20;
                Width = Math.Max(420, ButtonRow.DesiredSize.Width + 20 + chrome);
            };

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
            ProbeGdTex();

            RaisePropertyChanged(nameof(CanNavigatePrev));
            RaisePropertyChanged(nameof(CanNavigateNext));
        }

        private async void ProbeGdTex()
        {
            _gdTexProbeCts?.Cancel();
            var cts = new CancellationTokenSource();
            _gdTexProbeCts = cts;
            var item = _item;

            CanImportFromDisc = false;

            if (!ImageHelper.CanExtractGdText(item))
            {
                ImportFromDiscTooltip = item.DiscType != "Game" ? ImportTooltipNotGame : ImportTooltipNotReadable;
                return;
            }

            if (_gdTexCache.TryGetValue(item, out var cached))
            {
                ApplyGdTexProbeResult(cached);
                return;
            }

            ImportFromDiscTooltip = ImportTooltipChecking;

            // Small delay so holding an arrow key doesn't probe every item passed over
            try { await Task.Delay(250, cts.Token); }
            catch (TaskCanceledException) { return; }

            byte[] gdtex = null;
            try
            {
                gdtex = await ImageHelper.GetGdText(Path.Combine(item.FullFolderPath, item.ImageFile));
                if (gdtex != null)
                    new PuyoTools.PvrTexture().GetDecoded(gdtex);
            }
            catch
            {
                gdtex = null;
            }

            _gdTexCache[item] = gdtex;

            if (!cts.IsCancellationRequested)
                ApplyGdTexProbeResult(gdtex);
        }

        private void ApplyGdTexProbeResult(byte[] gdtex)
        {
            CanImportFromDisc = gdtex != null;
            ImportFromDiscTooltip = gdtex != null ? ImportTooltipAvailable : ImportTooltipNotFound;
        }

        private bool PromptUnsavedChanges()
        {
            if (!HasUnsavedChanges)
                return true;

            var result = MessageBox.Show(
                "You have unsaved changes. Save before navigating?",
                "Confirmation",
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
                // Silently fail, so no preview will be shown.
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
                // Silently fail, so the user can try again.
            }
        }

        private async void ImportFromDisc_Click(object sender, RoutedEventArgs e)
        {
            if (!_gdTexCache.TryGetValue(_item, out var gdtex) || gdtex == null)
                return;

            try
            {
                // Decode the disc's texture and reencode it through the same pipeline Browse uses
                var encodingTasks = await Task.Run(() =>
                {
                    var decoded = new PuyoTools.PvrTexture().GetDecoded(gdtex);
                    var boxPvr = PvrEncoder.EncodeFromPixels(decoded.Item1, decoded.Item2, decoded.Item3);
                    var iconPvr = PvrEncoder.EncodeIconFromPixels(decoded.Item1, decoded.Item2, decoded.Item3);
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
                // Silently fail, so the user can try again.
            }
        }

        private void DeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Delete artwork entry for serial '{Serial}'?",
                "Confirmation",
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

                    // Delete from both BOX.DAT and ICON.DAT (in memory only, written during Save Changes)
                    _manager.BoxDat.DeleteEntryForSerial(Serial);
                    _manager.IconDat?.DeleteEntryForSerial(Serial);

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
                // Delete from both DATs (in memory only, written during Save Changes)
                _manager.BoxDat.DeleteEntryForSerial(Serial);
                _manager.IconDat?.DeleteEntryForSerial(Serial);
                // newBoxData and newIconData stay null for delete
            }
            else if (_pendingPvrData != null)
            {
                // Set artwork in both DATs (in memory only, written during Save Changes)
                _manager.BoxDat.SetArtworkForSerial(Serial, _pendingPvrData);
                newBoxData = _pendingPvrData;
                if (_pendingIconPvrData != null && _manager.IconDat != null)
                {
                    _manager.IconDat.SetIconForSerial(Serial, _pendingIconPvrData);
                    newIconData = _pendingIconPvrData;
                }
            }

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
                    "Confirmation",
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
