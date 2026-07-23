using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using MsBox.Avalonia;
using MsBox.Avalonia.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class FolderArtEditorWindow : Window, INotifyPropertyChanged
    {
        private readonly Core.Manager _manager;
        private byte[] _pendingPvrData;
        private byte[] _originalPvrData;

        private readonly IList<string> _navigablePaths;
        private int _currentIndex;
        private int _keyHoldCount;
        private bool _isNavigating;

        // Read-only mode shows an orphaned entry by raw key, no editing
        private readonly string _orphanKey;

        public new event PropertyChangedEventHandler PropertyChanged;

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

        public bool CanDelete => !HasUnsavedChanges && _manager.FolderArtDat?.HasArtworkForFolder(FolderPath) == true;

        public FolderArtEditorWindow()
        {
            InitializeComponent();
        }

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
                if (e.Key == Avalonia.Input.Key.Escape) Close();
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

        private async Task<bool> PromptUnsavedChanges()
        {
            if (!HasUnsavedChanges)
                return true;

            var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
            {
                ContentTitle = "Confirmation",
                ContentMessage = "You have unsaved changes. Save before navigating?",
                Icon = MsBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Save" },
                    new ButtonDefinition { Name = "Discard" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowWindowDialogAsync(this);

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
                if (_navigablePaths == null || !await PromptUnsavedChanges())
                    return;

                int newIndex = Math.Clamp(_currentIndex + step, 0, _navigablePaths.Count - 1);
                if (newIndex == _currentIndex)
                    return;

                _currentIndex = newIndex;
                LoadFolder(_navigablePaths[_currentIndex]);
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
                // Silently fail, so no preview will be shown.
            }
        }

        private async void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var pickedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Image",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Image Files")
                    {
                        Patterns = new List<string> { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.tiff", "*.tga" }
                    }
                }
            });

            var imagePath = pickedFiles.Count > 0 ? pickedFiles[0].TryGetLocalPath() : null;
            if (imagePath != null)
            {
                await LoadAndPreviewImage(imagePath);
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

        private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
            {
                ContentTitle = "Confirmation",
                ContentMessage = $"Delete artwork for folder '{FolderPath}'?",
                Icon = MsBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Delete" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowWindowDialogAsync(this);

            if (result == "Delete")
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

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveChanges();
                Close();
            }
            catch (InvalidOperationException ex)
            {
                // hash collision, show the message
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message,
                    icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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

        private async void EditorWindow_Closing(object sender, CancelEventArgs e)
        {
            if (HasUnsavedChanges)
            {
                e.Cancel = true;

                var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                {
                    ContentTitle = "Confirmation",
                    ContentMessage = "You have unsaved changes. Discard them?",
                    Icon = MsBox.Avalonia.Enums.Icon.Warning,
                    ShowInCenter = true,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ButtonDefinitions = new ButtonDefinition[]
                    {
                        new ButtonDefinition { Name = "Discard" },
                        new ButtonDefinition { Name = "Cancel" }
                    }
                }).ShowWindowDialogAsync(this);

                if (result == "Discard")
                {
                    HasUnsavedChanges = false;
                    Close();
                }
            }
        }
    }
}
