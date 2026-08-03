using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class FolderArtEditorWindow : Window, INotifyPropertyChanged
    {
        private readonly Core.Manager _manager;
        private byte[] _pendingPvrData;
        private byte[] _originalPvrData;

        private readonly IList<string> _navigablePaths;
        private int _currentIndex;
        private int _keyHoldCount;

        // Read-only mode shows an orphaned entry by raw key, no editing
        private readonly string _orphanKey;

        public event PropertyChangedEventHandler PropertyChanged;

        private string _folderPath;
        public string FolderPath
        {
            get => _folderPath;
            private set { _folderPath = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(FolderDisplay)); RaisePropertyChanged(nameof(WindowTitle)); }
        }

        public string FolderDisplay => _orphanKey != null
            ? (_manager.FolderArtDat?.GetPathForKey(_orphanKey) ?? _orphanKey)
            : FolderPath;

        public string WindowTitle => $"Folder Artwork - {FolderDisplay}";

        public bool IsReadOnly => _orphanKey != null;
        public bool CanNavigate => _navigablePaths != null && !IsReadOnly;
        public bool CanNavigatePrev => CanNavigate && _currentIndex > 0;
        public bool CanNavigateNext => CanNavigate && _currentIndex >= 0 && _currentIndex < _navigablePaths.Count - 1;

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

        public bool CanDelete => !HasUnsavedChanges && _manager.FolderArtDat?.HasArtworkForFolder(FolderPath) == true;

        public FolderArtEditorWindow(Core.Manager manager, IList<string> navigablePaths, int index)
        {
            InitializeComponent();

            _manager = manager;
            _navigablePaths = navigablePaths;
            _currentIndex = index;
            FolderPath = navigablePaths[index];

            _originalPvrData = _manager.FolderArtDat?.GetPvrDataForFolder(FolderPath);
            LoadCurrentArtwork();

            this.Closing += EditorWindow_Closing;
            this.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Left || e.Key == System.Windows.Input.Key.Right)
                {
                    _keyHoldCount++;
                    int step = _keyHoldCount > 3 ? 5 : 1;

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

        public FolderArtEditorWindow(Core.Manager manager, string orphanKey)
        {
            InitializeComponent();

            _manager = manager;
            _orphanKey = orphanKey;

            var pvrData = _manager.FolderArtDat?.GetPvrDataForKey(orphanKey);
            if (pvrData != null)
                DisplayPvrData(pvrData);

            this.KeyUp += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
            };
            DataContext = this;
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadCurrentArtwork()
        {
            var pvrData = _manager.FolderArtDat?.GetPvrDataForFolder(FolderPath);
            if (pvrData != null)
            {
                DisplayPvrData(pvrData);
            }
        }

        private void LoadFolder(string folderPath)
        {
            FolderPath = folderPath;

            _pendingPvrData = null;
            HasUnsavedChanges = false;
            PreviewImage = null;

            _originalPvrData = _manager.FolderArtDat?.GetPvrDataForFolder(FolderPath);
            LoadCurrentArtwork();

            RaisePropertyChanged(nameof(CanNavigatePrev));
            RaisePropertyChanged(nameof(CanNavigateNext));
            RaisePropertyChanged(nameof(CanDelete));
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
            if (_navigablePaths == null || !PromptUnsavedChanges())
                return;

            int newIndex = Math.Clamp(_currentIndex + step, 0, _navigablePaths.Count - 1);
            if (newIndex == _currentIndex)
                return;

            _currentIndex = newIndex;
            LoadFolder(_navigablePaths[_currentIndex]);
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
                var boxPvr = await Task.Run(() => PvrEncoder.EncodeFromFile(imagePath));

                _pendingPvrData = boxPvr;
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
                $"Delete artwork for folder '{FolderPath}'?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (_manager.FolderArtDat == null)
                        return;

                    var oldData = _manager.FolderArtDat.GetPvrDataForFolder(FolderPath);

                    _manager.FolderArtDat.DeleteEntryForFolder(FolderPath);

                    _manager.UndoManager.RecordChange(new FolderArtChangeOperation
                    {
                        FolderPath = FolderPath,
                        OldPvrData = oldData,
                        NewPvrData = null,
                        FolderArtDat = _manager.FolderArtDat
                    });

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
            if (_manager.FolderArtDat == null || _pendingPvrData == null)
                return;

            _manager.FolderArtDat.SetArtworkForFolder(FolderPath, _pendingPvrData);

            _manager.UndoManager.RecordChange(new FolderArtChangeOperation
            {
                FolderPath = FolderPath,
                OldPvrData = _originalPvrData,
                NewPvrData = _pendingPvrData,
                FolderArtDat = _manager.FolderArtDat
            });

            _originalPvrData = _pendingPvrData;
            HasUnsavedChanges = false;
            _pendingPvrData = null;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveChanges();
                Close();
            }
            catch (InvalidOperationException ex)
            {
                // Hash collision, show the message.
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // Silently fail
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void EditorWindow_Closing(object sender, CancelEventArgs e)
        {
            if (HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Discard them?",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
                else
                {
                    HasUnsavedChanges = false;
                }
            }
        }
    }
}
