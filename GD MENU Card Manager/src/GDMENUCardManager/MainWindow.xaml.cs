using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GDMENUCardManager.Core;
using GongSolutions.Wpf.DragDrop;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDropTarget, INotifyPropertyChanged, IDiscImageOptionsViewModel
    {
        private Core.Manager _ManagerInstance;
        public Core.Manager Manager { get { return _ManagerInstance; } }

        private readonly bool showAllDrives = false;
        private string _originalFolderValue;
        private string _rawFolderText;

        // Undo tracking for cell edits
        private GdItem _editingItem;
        private string _editingPropertyName;
        private object _editingOldValue;

        // Flag to prevent duplicate serial translation dialogs
        private bool _handlingSerialTranslation;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<DriveInfo> DriveList { get; } = new ObservableCollection<DriveInfo>();



        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            set { _IsBusy = value; RaisePropertyChanged(); }
        }

        private DriveInfo _DriveInfo;
        public DriveInfo SelectedDrive
        {
            get { return _DriveInfo; }
            set
            {
                _DriveInfo = value;
                Manager.ItemList.Clear();
                if (value != null)
                {
                    // Clear custom path when selecting a drive
                    if (IsUsingCustomPath)
                    {
                        CustomSdPath = null;
                    }
                    Manager.sdPath = value.RootDirectory.ToString();
                }
                else if (!IsUsingCustomPath)
                {
                    Manager.sdPath = null;
                }
                if (IsFilterActive)
                    ClearFilterFromGrid();
                else
                    Filter = null;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasSdPath));
            }
        }

        private string _TempFolder;
        public string TempFolder
        {
            get { return _TempFolder; }
            set { _TempFolder = value; RaisePropertyChanged(); }
        }

        private string _CustomSdPath;
        public string CustomSdPath
        {
            get { return _CustomSdPath; }
            set
            {
                _CustomSdPath = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsUsingCustomPath));
                RaisePropertyChanged(nameof(HasSdPath));
            }
        }

        public bool IsUsingCustomPath => !string.IsNullOrEmpty(CustomSdPath);

        public bool HasSdPath => SelectedDrive != null || IsUsingCustomPath;

        private string _TotalFilesLength = "N/A";
        public string TotalFilesLength
        {
            get { return _TotalFilesLength; }
            private set { _TotalFilesLength = value; RaisePropertyChanged(); }
        }

        private bool _HaveGDIShrinkBlacklist;
        public bool HaveGDIShrinkBlacklist
        {
            get { return _HaveGDIShrinkBlacklist; }
            set { _HaveGDIShrinkBlacklist = value; RaisePropertyChanged(); }
        }

        //private bool _EnableGDIShrink;
        public bool EnableGDIShrink
        {
            get { return Manager.EnableGDIShrink; }
            set { Manager.EnableGDIShrink = value; RaisePropertyChanged(); }
        }

        //private bool _EnableGDIShrinkCompressed;
        public bool EnableGDIShrinkCompressed
        {
            get { return Manager.EnableGDIShrinkCompressed; }
            set { Manager.EnableGDIShrinkCompressed = value; RaisePropertyChanged(); }
        }

        //private bool _EnableGDIShrinkBlackList = true;
        public bool EnableGDIShrinkBlackList
        {
            get { return Manager.EnableGDIShrinkBlackList; }
            set { Manager.EnableGDIShrinkBlackList = value; RaisePropertyChanged(); }
        }

        public bool EnableGDIShrinkExisting
        {
            get { return Manager.EnableGDIShrinkExisting; }
            set { Manager.EnableGDIShrinkExisting = value; RaisePropertyChanged(); }
        }

        public bool EnableRegionPatch
        {
            get { return Manager.EnableRegionPatch; }
            set { Manager.EnableRegionPatch = value; RaisePropertyChanged(); }
        }

        public bool EnableRegionPatchExisting
        {
            get { return Manager.EnableRegionPatchExisting; }
            set { Manager.EnableRegionPatchExisting = value; RaisePropertyChanged(); }
        }

        public bool EnableVgaPatch
        {
            get { return Manager.EnableVgaPatch; }
            set { Manager.EnableVgaPatch = value; RaisePropertyChanged(); }
        }

        public bool EnableVgaPatchExisting
        {
            get { return Manager.EnableVgaPatchExisting; }
            set { Manager.EnableVgaPatchExisting = value; RaisePropertyChanged(); }
        }

        public MenuKind MenuKindSelected
        {
            get { return Manager.MenuKindSelected; }
            set
            {
                Manager.MenuKindSelected = value;
                RaisePropertyChanged();
                UpdateFolderColumnVisibility();
                UpdateSortButtonTooltip();
            }
        }

        private string _Filter;
        public string Filter
        {
            get { return _Filter; }
            set { _Filter = value; RaisePropertyChanged(); }
        }

        private bool _IsFilterActive;
        public bool IsFilterActive
        {
            get { return _IsFilterActive; }
            set { _IsFilterActive = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(IsNotFilterActive)); }
        }
        public bool IsNotFilterActive => !IsFilterActive;

        private string _activeFilterText;

        public bool IsArtworkEnabled
        {
            get { return !Manager.ArtworkDisabled; }
        }

        public bool EnableLockCheck
        {
            get { return Manager.EnableLockCheck; }
            set { Manager.EnableLockCheck = value; RaisePropertyChanged(); }
        }

        private readonly string fileFilterList;

        public MainWindow()
        {
            InitializeComponent();

            var compressedFileFormats = new string[] { ".7z", ".rar", ".zip" };
            _ManagerInstance = Core.Manager.CreateInstance(new DependencyManager(), compressedFileFormats);
            var fullList = Manager.supportedImageFormats.Concat(compressedFileFormats).Select(x => $"*{x}").ToArray();
            fileFilterList = $"Dreamcast Game ({string.Join("; ", fullList)})|{string.Join(';', fullList)}";

            // Clean up any leftover staging data from a previous update attempt
            UpdateManager.CleanupStaleStagingData();

            this.Loaded += async (ss, ee) =>
            {
                await CheckConfigWritability();

                HaveGDIShrinkBlacklist = File.Exists(Constants.GdiShrinkBlacklistFile);

                // If custom path is set, load from it instead of searching for drives
                if (IsUsingCustomPath)
                {
                    await LoadItemsFromCard();
                }
                else
                {
                    FillDriveList();
                }

                // Defer column visibility update until DataGrid is fully loaded
                _ = Dispatcher.BeginInvoke(new Action(() => UpdateFolderColumnVisibility()), System.Windows.Threading.DispatcherPriority.Loaded);

                // Check for updates (non-blocking, silent on failure)
                _ = CheckForUpdateAsync();
            };
            this.Closing += MainWindow_Closing;
            this.PropertyChanged += MainWindow_PropertyChanged;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            Manager.ItemList.CollectionChanged += ItemList_CollectionChanged;
            Manager.MenuKindChanged += Manager_MenuKindChanged;

            SevenZip.SevenZipExtractor.SetLibraryPath(Environment.Is64BitProcess ? "7z64.dll" : "7z.dll");

            //config parsing. all settings are optional and must reverse to default values if missing
            bool.TryParse(ConfigurationManager.AppSettings["ShowAllDrives"], out showAllDrives);
            bool.TryParse(ConfigurationManager.AppSettings["Debug"], out Manager.debugEnabled);
            if (bool.TryParse(ConfigurationManager.AppSettings["UseBinaryString"], out bool useBinaryString))
                Converter.ByteSizeToStringConverter.UseBinaryString = useBinaryString;
            if (int.TryParse(ConfigurationManager.AppSettings["CharLimit"], out int charLimit))
                GdItem.namemaxlen = Math.Min(256, Math.Max(charLimit, 1));
            if (int.TryParse(ConfigurationManager.AppSettings["ProductIdMaxLength"], out int productIdMaxLength))
                GdItem.serialmaxlen = Math.Min(32, Math.Max(productIdMaxLength, 1));
            if (bool.TryParse(ConfigurationManager.AppSettings["TruncateMenuGDI"], out bool truncateMenuGDI))
                Manager.TruncateMenuGDI = truncateMenuGDI;
            if (bool.TryParse(ConfigurationManager.AppSettings["LockCheck"], out bool lockCheck))
                Manager.EnableLockCheck = lockCheck;

            // Disc Image Options
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableGDIShrink"], out bool gdiShrink))
                Manager.EnableGDIShrink = gdiShrink;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableGDIShrinkCompressed"], out bool gdiShrinkCompressed))
                Manager.EnableGDIShrinkCompressed = gdiShrinkCompressed;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableGDIShrinkBlackList"], out bool gdiShrinkBlackList))
                Manager.EnableGDIShrinkBlackList = gdiShrinkBlackList;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableGDIShrinkExisting"], out bool gdiShrinkExisting))
                Manager.EnableGDIShrinkExisting = gdiShrinkExisting;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableRegionPatch"], out bool regionPatch))
                Manager.EnableRegionPatch = regionPatch;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableRegionPatchExisting"], out bool regionPatchExisting))
                Manager.EnableRegionPatchExisting = regionPatchExisting;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableVgaPatch"], out bool vgaPatch))
                Manager.EnableVgaPatch = vgaPatch;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableVgaPatchExisting"], out bool vgaPatchExisting))
                Manager.EnableVgaPatchExisting = vgaPatchExisting;

            var tempFolderConfig = ConfigurationManager.AppSettings["TempFolder"];
            if (!string.IsNullOrEmpty(tempFolderConfig) && Directory.Exists(tempFolderConfig))
                TempFolder = tempFolderConfig;
            else
                TempFolder = Path.GetTempPath();

            // Update repo override (for testing)
            UpdateManager.RepoOverride = ConfigurationManager.AppSettings["UpdateRepoOverride"];

            Title = "GD MENU Card Manager " + Constants.Version;

            // Restore window position and size from config
            RestoreWindowBounds();

            //showAllDrives = true;

            DataContext = this;
        }

        private async void MainWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedDrive) && SelectedDrive != null)
                await LoadItemsFromCard();
            else if (e.PropertyName == nameof(MenuKindSelected))
            {
                UpdateFolderColumnVisibility();
                UpdateSortButtonTooltip();
            }
        }

        private void Manager_MenuKindChanged(object sender, EventArgs e)
        {
            // Update column visibility and sort tooltip immediately when menu kind is detected during loading
            Dispatcher.Invoke(new Action(() =>
            {
                RaisePropertyChanged(nameof(MenuKindSelected));
                UpdateFolderColumnVisibility();
                UpdateSortButtonTooltip();
            }));
        }

        private void UpdateFolderColumnVisibility()
        {
            if (dg1?.Columns == null)
                return;

            // Find columns by iterating and checking their Header
            DataGridColumn folderColumn = null;
            DataGridColumn typeColumn = null;
            DataGridColumn artColumn = null;
            DataGridTextColumn discColumn = null;

            foreach (var col in dg1.Columns)
            {
                if (col.Header?.ToString() == "Folder")
                    folderColumn = col;
                else if (col is DataGridTemplateColumn templateCol && templateCol.Header?.ToString() == "Type")
                    typeColumn = col;
                else if (col is DataGridTextColumn discTextCol && discTextCol.Header?.ToString() == "Disc")
                    discColumn = discTextCol;
                else if (col.Header?.ToString() == "Art")
                    artColumn = col;
            }

            if (folderColumn != null)
            {
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    folderColumn.Visibility = Visibility.Visible;
                    folderColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
                else
                {
                    folderColumn.Visibility = Visibility.Collapsed;
                }
            }

            if (typeColumn != null)
            {
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    typeColumn.Visibility = Visibility.Visible;
                }
                else
                {
                    typeColumn.Visibility = Visibility.Collapsed;
                }
            }

            // Art column: only visible in openMenu mode
            if (artColumn != null)
            {
                bool showArt = MenuKindSelected == MenuKind.openMenu;
                artColumn.Visibility = showArt ? Visibility.Visible : Visibility.Collapsed;
            }

            if (discColumn != null)
            {
                // Make Disc column editable only in openMenu mode
                discColumn.IsReadOnly = (MenuKindSelected != MenuKind.openMenu);
            }
        }

        private void UpdateSortButtonTooltip()
        {
            if (ButtonSort == null) return;
            ButtonSort.ToolTip = MenuKindSelected == MenuKind.openMenu
                ? "Sort list by folder path + title"
                : "Sort list by title";
        }

        private void ItemList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            updateTotalSize();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (IsBusy)
                e.Cancel = true;
            else
            {
                Manager.ItemList.CollectionChanged -= ItemList_CollectionChanged;//release events
                SaveWindowBounds();
            }
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void updateTotalSize()
        {
            var bsize = ByteSizeLib.ByteSize.FromBytes(Manager.ItemList.Sum(x => x.Length.Bytes));
            TotalFilesLength = Converter.ByteSizeToStringConverter.UseBinaryString ? bsize.ToBinaryString() : bsize.ToString();
        }


        private async Task CheckForUpdateAsync()
        {
            try
            {
                var result = await UpdateManager.CheckForUpdateAsync();
                if (result.ManualUpdateRequired && !UpdateAvailableDialog.ShouldSkipVersion(result.LatestTag))
                {
                    var manualDialog = new ManualUpdateDialog(result.LatestTag, result.LatestVersion, result.ManualReason);
                    manualDialog.Owner = this;
                    manualDialog.ShowDialog();
                }
                else if (result.UpdateAvailable && !UpdateAvailableDialog.ShouldSkipVersion(result.LatestTag))
                {
                    var dialog = new UpdateAvailableDialog(result.LatestTag, result.LatestVersion);
                    dialog.Owner = this;
                    dialog.ShowDialog();

                    if (dialog.UserWantsUpdate)
                    {
                        var wizard = new UpdateWizardWindow(result.LatestTag, result.LatestVersion);
                        wizard.Owner = this;
                        wizard.ShowDialog();
                    }
                }
            }
            catch
            {
                // Silently ignore any update check errors
            }
        }

        private async Task LoadItemsFromCard()
        {
            IsBusy = true;

            try
            {
                await Manager.LoadItemsFromCard();

                // Check if any items need metadata scan (old SD cards without cache files)
                var itemsNeedingScan = Manager.GetItemsNeedingMetadataScan();
                if (itemsNeedingScan.Any())
                {
                    var scanDialog = new MetadataScanDialog(itemsNeedingScan.Count);
                    scanDialog.Owner = this;
                    var result = scanDialog.ShowDialog();

                    if (scanDialog.StartScan)
                    {
                        // Perform the metadata scan with progress window
                        await PerformMetadataScan(itemsNeedingScan);
                    }
                    else
                    {
                        // quit
                        Application.Current.Shutdown();
                        return;
                    }
                }

                // Initialize BoxDat for artwork management (openMenu only)
                Manager.InitializeBoxDat();

                // Check DAT file status for openMenu
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    await HandleDatFileStatus();
                }

                // Check for serial translations that were applied
                await ShowSerialTranslationDialogIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Problem loading the following folder(s):\n\n{ex.Message}", "Invalid Folders", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                RaisePropertyChanged(nameof(MenuKindSelected));
                UpdateFolderColumnVisibility();
                IsBusy = false;
            }
        }

        /// <summary>
        /// Checks if any items have had serial translations applied and shows the dialog if so.
        /// </summary>
        private async Task ShowSerialTranslationDialogIfNeeded()
        {
            var translatedItems = Manager.ItemList.Where(item => item.WasSerialTranslated).ToList();
            if (translatedItems.Count > 0)
            {
                await Helper.DependencyManager.ShowSerialTranslationDialog(translatedItems);
            }
        }

        private async Task PerformMetadataScan(List<GdItem> items)
        {
            var progressWindow = new ProgressWindow();
            progressWindow.Owner = this;
            progressWindow.Title = "Scanning Disc Images";
            progressWindow.TotalItems = items.Count;
            progressWindow.Show();

            var progress = new Progress<(int current, int total, string name)>(p =>
            {
                progressWindow.ProcessedItems = p.current;
                progressWindow.TextContent = $"Caching metadata: {p.name}";
            });

            try
            {
                await Manager.PerformMetadataScan(items, progress);
            }
            finally
            {
                progressWindow.AllowClose();
                progressWindow.Close();
            }
        }

        private async Task HandleDatFileStatus()
        {
            var status = Manager.CheckDatFilesStatus();

            switch (status)
            {
                case DatFileStatus.BothMissing:
                    {
                        var result = MessageBox.Show(
                            "BOX.DAT and ICON.DAT were not found in the expected location.\n\n" +
                            "These files are required for artwork display in openMenu.\n\n" +
                            "Click Yes to create empty DAT files.\n\n" +
                            "Click No to close and add files manually.\n\n" +
                            "Click Cancel to proceed without artwork features.",
                            "DAT Files Missing",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.CreateEmptyDatFiles();
                            if (!success)
                            {
                                MessageBox.Show($"Failed to create DAT files: {error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                Manager.ArtworkDisabled = true;
                            }
                        }
                        else if (result == MessageBoxResult.No)
                        {
                            // close and let user add manually
                            SelectedDrive = null;
                        }
                        else
                        {
                            // Cancel = skip artwork features
                            Manager.ArtworkDisabled = true;
                        }
                        break;
                    }

                case DatFileStatus.BoxMissingIconExists:
                    {
                        var result = MessageBox.Show(
                            "BOX.DAT was not found but ICON.DAT exists.\n\n" +
                            "BOX.DAT is required for artwork management.\n\n" +
                            "Click Yes to create an empty BOX.DAT file.\n\n" +
                            "Click No to close and add BOX.DAT manually.\n\n" +
                            "Click Cancel to proceed without artwork features.",
                            "BOX.DAT Missing",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.CreateEmptyBoxDat();
                            if (!success)
                            {
                                MessageBox.Show($"Failed to create BOX.DAT: {error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                Manager.ArtworkDisabled = true;
                            }
                        }
                        else if (result == MessageBoxResult.No)
                        {
                            SelectedDrive = null;
                        }
                        else
                        {
                            Manager.ArtworkDisabled = true;
                        }
                        break;
                    }

                case DatFileStatus.BoxExistsIconMissing:
                    {
                        var result = MessageBox.Show(
                            "ICON.DAT was not found but BOX.DAT exists.\n\n" +
                            "ICON.DAT can be generated from BOX.DAT by downscaling the artwork.\n\n" +
                            "Click Yes to generate ICON.DAT from BOX.DAT (recommended).\n\n" +
                            "Click No to close and add ICON.DAT manually.\n\n" +
                            "Click Cancel to proceed without artwork features.",
                            "ICON.DAT Missing",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.GenerateIconDatFromBox();
                            if (!success)
                            {
                                MessageBox.Show($"Failed to generate ICON.DAT: {error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                Manager.ArtworkDisabled = true;
                            }
                        }
                        else if (result == MessageBoxResult.No)
                        {
                            SelectedDrive = null;
                        }
                        else
                        {
                            Manager.ArtworkDisabled = true;
                        }
                        break;
                    }

                case DatFileStatus.SerialsMismatch:
                    {
                        var result = MessageBox.Show(
                            "ICON.DAT entries don't match BOX.DAT entries.\n\n" +
                            "This can happen if the files were modified independently.\n\n" +
                            "Click Yes to regenerate ICON.DAT from BOX.DAT (recommended).\n\n" +
                            "Click No to proceed with mismatched files (some icons may be missing).\n\n" +
                            "Click Cancel to proceed without artwork features.",
                            "DAT File Mismatch",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            if (!await Manager.EnsureDatFilesWritable()) break;
                            var (success, error) = Manager.GenerateIconDatFromBox();
                            if (!success)
                            {
                                MessageBox.Show($"Failed to regenerate ICON.DAT: {error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else if (result == MessageBoxResult.Cancel)
                        {
                            Manager.ArtworkDisabled = true;
                        }
                        // No = proceed with mismatched files, do nothing
                        break;
                    }

                case DatFileStatus.OK:
                default:
                    // All good, nothing to do
                    break;
            }

            // Update UI based on artwork disabled state
            RaisePropertyChanged(nameof(IsArtworkEnabled));
            UpdateFolderColumnVisibility();
        }

        private async Task Save()
        {
            IsBusy = true;
            try
            {
                // Check for multi-disc items without serial (openMenu only)
                if (MenuKindSelected == MenuKind.openMenu && HasMultiDiscItemsWithoutSerial())
                {
                    var dialog = new WarningDialog(
                        "One or more disc images that are part of multi-disc sets do not have a required Serial value assigned to them, which will break their display in openMenu.\n\nDo you want to proceed and ignore the disc numbers and counts, or return to make edits?");
                    dialog.Owner = this;

                    if (dialog.ShowDialog() != true || !dialog.Proceed)
                    {
                        IsBusy = false;
                        return;
                    }

                    // reset disc to 1/1 for items without serial
                    ResetDiscValuesForItemsWithoutSerial();
                }

                // Check for multi-disc sets exceeding 10 discs (openMenu only)
                if (MenuKindSelected == MenuKind.openMenu && HasMultiDiscSetsExceeding10())
                {
                    var dialog = new WarningDialog(
                        "One or more multi-disc set exceeds 10 discs total, the maximum supported by openMenu.\n\nDo you want to proceed or return to make edits?");
                    dialog.Owner = this;

                    if (dialog.ShowDialog() != true || !dialog.Proceed)
                    {
                        IsBusy = false;
                        return;
                    }
                }

                if (await Manager.Save(TempFolder))
                {
                    SaveTempFolderConfig();
                    SaveLockCheckConfig();
                    MessageBox.Show(this, "Done!", "Message", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                updateTotalSize();
            }
        }

        private bool HasMultiDiscItemsWithoutSerial()
        {
            return Manager.ItemList.Any(item =>
            {
                // Skip menu items and compressed files (serial assigned during extraction)
                if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                    return false;
                if (item.FileFormat == Core.FileFormat.SevenZip || item.FileFormat == Core.FileFormat.CueBinNonGame)
                    return false;

                if (string.IsNullOrWhiteSpace(item.ProductNumber))
                {
                    var disc = item.Ip?.Disc;
                    if (!string.IsNullOrEmpty(disc))
                    {
                        var parts = disc.Split('/');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[1], out int totalDiscs) &&
                            totalDiscs > 1)
                        {
                            return true;
                        }
                    }
                }
                return false;
            });
        }

        private bool HasMultiDiscSetsExceeding10()
        {
            return Manager.ItemList.Any(item =>
            {
                // Skip menu items
                if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                    return false;

                var disc = item.Ip?.Disc;
                if (!string.IsNullOrEmpty(disc))
                {
                    var parts = disc.Split('/');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[1], out int totalDiscs) &&
                        totalDiscs > 10)
                    {
                        return true;
                    }
                }
                return false;
            });
        }

        private void ResetDiscValuesForItemsWithoutSerial()
        {
            foreach (var item in Manager.ItemList)
            {
                // Skip menu items
                if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                    continue;

                // If no serial and has multi-disc value, reset to 1/1
                if (string.IsNullOrWhiteSpace(item.ProductNumber) && item.Ip != null)
                {
                    var disc = item.Ip.Disc;
                    if (!string.IsNullOrEmpty(disc))
                    {
                        var parts = disc.Split('/');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[1], out int totalDiscs) &&
                            totalDiscs > 1)
                        {
                            item.Ip.Disc = "1/1";
                            // Trigger UI update
                            item.NotifyIpChanged();
                        }
                    }
                }
            }
        }

        private async Task CheckConfigWritability()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
                var configPath = config.FilePath;

                if (!File.Exists(configPath))
                    return; // nothing to check

                while (true)
                {
                    Core.Helper.TryMakeWritable(configPath);
                    var error = Core.Helper.CheckFileAccessibility(configPath);
                    if (error == null) break; // writable

                    // true=retry, false=proceed without saving
                    if (!await Core.Helper.DependencyManager.ShowConfigReadOnlyDialog(configPath, error))
                    {
                        Core.Manager.ConfigReadOnly = true;
                        break;
                    }
                }
            }
            catch { }
        }

        private void SaveTempFolderConfig()
        {
            if (Core.Manager.ConfigReadOnly) return;
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
                var systemDefault = Path.GetTempPath();
                var normalized = Path.GetFullPath(TempFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var normalizedDefault = Path.GetFullPath(systemDefault.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(normalized, normalizedDefault, StringComparison.OrdinalIgnoreCase))
                    SetOrAddSetting(config, "TempFolder", "");
                else
                    SetOrAddSetting(config, "TempFolder", TempFolder);
                config.Save(System.Configuration.ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch { }
        }

        private void SaveDiscImageOptionsConfig()
        {
            if (Core.Manager.ConfigReadOnly) return;
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
                SetOrAddSetting(config, "EnableGDIShrink", Manager.EnableGDIShrink.ToString());
                SetOrAddSetting(config, "EnableGDIShrinkCompressed", Manager.EnableGDIShrinkCompressed.ToString());
                SetOrAddSetting(config, "EnableGDIShrinkBlackList", Manager.EnableGDIShrinkBlackList.ToString());
                SetOrAddSetting(config, "EnableGDIShrinkExisting", Manager.EnableGDIShrinkExisting.ToString());
                SetOrAddSetting(config, "EnableRegionPatch", Manager.EnableRegionPatch.ToString());
                SetOrAddSetting(config, "EnableRegionPatchExisting", Manager.EnableRegionPatchExisting.ToString());
                SetOrAddSetting(config, "EnableVgaPatch", Manager.EnableVgaPatch.ToString());
                SetOrAddSetting(config, "EnableVgaPatchExisting", Manager.EnableVgaPatchExisting.ToString());
                config.Save(System.Configuration.ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch { }
        }

        private void SaveLockCheckConfig()
        {
            if (Core.Manager.ConfigReadOnly) return;
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
                SetOrAddSetting(config, "LockCheck", Manager.EnableLockCheck.ToString());
                config.Save(System.Configuration.ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch
            {
                // Ignore errors saving config
            }
        }

        private void RestoreWindowBounds()
        {
            try
            {
                if (double.TryParse(ConfigurationManager.AppSettings["WindowLeft"], out double left)
                    && double.TryParse(ConfigurationManager.AppSettings["WindowTop"], out double top)
                    && double.TryParse(ConfigurationManager.AppSettings["WindowWidth"], out double width)
                    && double.TryParse(ConfigurationManager.AppSettings["WindowHeight"], out double height))
                {
                    // Validate saved size against minimums
                    if (width < MinWidth) width = MinWidth;
                    if (height < MinHeight) height = MinHeight;

                    // Check that at least part of the window is visible on some screen
                    bool isOnScreen = false;
                    foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                    {
                        var bounds = screen.WorkingArea;
                        if (left + width > bounds.Left && left < bounds.Right
                            && top + height > bounds.Top && top < bounds.Bottom)
                        {
                            isOnScreen = true;
                            break;
                        }
                    }

                    if (isOnScreen)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Left = left;
                        Top = top;
                        Width = width;
                        Height = height;
                    }
                }
            }
            catch { }
        }

        private static void SetOrAddSetting(System.Configuration.Configuration config, string key, string value)
        {
            if (config.AppSettings.Settings[key] != null)
                config.AppSettings.Settings[key].Value = value;
            else
                config.AppSettings.Settings.Add(key, value);
        }

        private void SaveWindowBounds()
        {
            if (Core.Manager.ConfigReadOnly) return;
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);

                // Save normal (non-maximized) bounds
                var bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;

                SetOrAddSetting(config, "WindowLeft", bounds.Left.ToString());
                SetOrAddSetting(config, "WindowTop", bounds.Top.ToString());
                SetOrAddSetting(config, "WindowWidth", bounds.Width.ToString());
                SetOrAddSetting(config, "WindowHeight", bounds.Height.ToString());
                config.Save(System.Configuration.ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch { }
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo == null)
                return;

            if (IsFilterActive)
            {
                dropInfo.Effects = System.Windows.DragDropEffects.None;
                return;
            }

            DragDropHandler.DragOver(dropInfo);
        }

        async void IDropTarget.Drop(IDropInfo dropInfo)
        {
            if (dropInfo == null)
                return;

            if (IsFilterActive)
                return;

            IsBusy = true;
            try
            {
                var result = await DragDropHandler.Drop(dropInfo);

                // Record undo operation based on what happened
                if (result != null)
                {
                    if (result.IsReorder && result.OldOrder != null && result.NewOrder != null)
                    {
                        Manager.UndoManager.RecordChange(new ListReorderOperation("Move Items")
                        {
                            ItemList = Manager.ItemList,
                            OldOrder = result.OldOrder,
                            NewOrder = result.NewOrder
                        });
                    }
                    else if (result.IsAdd && result.AddedItems.Count > 0)
                    {
                        var undoOp = new MultiItemAddOperation { ItemList = Manager.ItemList };
                        undoOp.Items.AddRange(result.AddedItems);
                        Manager.UndoManager.RecordChange(undoOp);

                        // Check for serial translations that were applied to added items
                        await ShowSerialTranslationDialogIfNeeded();
                    }
                }
            }
            catch (InvalidDropException ex)
            {
                var w = new TextWindow("Ignored folders/files", ex.Message);
                w.Owner = this;
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }



        private async void ButtonSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            if (IsFilterActive)
                return;

            var emptySerials = Manager.ItemList
                .Where(x => x.Ip?.Name != "GDMENU" && x.Ip?.Name != "openMenu"
                    && x.FileFormat != Core.FileFormat.SevenZip
                    && x.FileFormat != Core.FileFormat.CueBinNonGame
                    && string.IsNullOrWhiteSpace(x.ProductNumber))
                .ToList();

            if (emptySerials.Count > 0)
            {
                var count = emptySerials.Count;
                var msg = count == 1
                    ? "1 disc image doesn't have a Serial ID assigned to it."
                    : $"{count} disc images don't have Serial IDs assigned to them.";
                msg += "\n\nA valid openMenu configuration requires all disc images are assigned a Serial ID.";
                MessageBox.Show(this, msg, "Missing Serial IDs", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await Save();
        }

        private void ButtonAbout_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            new AboutWindow { Owner = this }.ShowDialog();
            IsBusy = false;
        }

        private void ButtonFolder_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if ((string)btn.CommandParameter == nameof(TempFolder) && !string.IsNullOrEmpty(TempFolder))
                    dialog.SelectedPath = TempFolder;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TempFolder = dialog.SelectedPath;
            }
        }

        private void ButtonResetTempFolder_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this, "Reset the Temporary Folder path to default?", "Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                TempFolder = Path.GetTempPath();
                SaveTempFolderConfig();
            }
        }

        //private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        //{
        //    var grid = sender as DataGridRow;
        //    GdItem model;
        //    if (grid != null && grid.DataContext != null && (model = grid.DataContext as GdItem) != null)
        //    {
        //        IsBusy = true;

        //        var helptext = $"{model.Ip.Name}\n{model.Ip.Version}\n{model.Ip.Disc}";

        //        MessageBox.Show(helptext, "IP.BIN Info", MessageBoxButton.OK, MessageBoxImage.Information);
        //        IsBusy = false;
        //    }
        //}

        private async void ButtonInfo_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            try
            {
                var btn = (Button)sender;
                var item = (GdItem)btn.CommandParameter;

                if (item.Ip == null)
                    await Manager.LoadIP(item);

                new InfoWindow(item) { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error Loading data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            IsBusy = false;
        }

        private async void ButtonArtwork_Click(object sender, RoutedEventArgs e)
        {
            // Commit any pending cell edits to ensure we read the current Serial value
            dg1.CommitEdit(DataGridEditingUnit.Cell, true);
            dg1.CommitEdit(DataGridEditingUnit.Row, true);

            IsBusy = true;
            try
            {
                var btn = (Button)sender;
                var item = (GdItem)btn.CommandParameter;

                if (item == null || !item.CanManageArtwork)
                    return;

                // handle serial translation before opening artwork window
                if (item.WasSerialTranslated)
                {
                    _handlingSerialTranslation = true;
                    try
                    {
                        await Helper.DependencyManager.ShowSerialTranslationDialog(new[] { item });
                    }
                    finally
                    {
                        _handlingSerialTranslation = false;
                    }
                }

                var navigableItems = Manager.ItemList.Where(i => i.CanManageArtwork).ToList();
                new ArtworkWindow(item, Manager, navigableItems) { Owner = this }.ShowDialog();

                // Refresh column visibility in case BoxDat state changed
                UpdateFolderColumnVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ButtonUndo_Click(object sender, RoutedEventArgs e)
        {
            Manager.UndoManager.Undo();
        }

        private void ButtonRedo_Click(object sender, RoutedEventArgs e)
        {
            Manager.UndoManager.Redo();
        }

        private async void ButtonSort_Click(object sender, RoutedEventArgs e)
        {
            if (IsFilterActive)
                return;
            var sortDescription = MenuKindSelected == MenuKind.openMenu
                ? "Your disc images will be automatically sorted in alphanumeric order based on a combination of Folder and Title.\n\nDo you want to continue?"
                : "Your disc images will be automatically sorted in alphanumeric order based on Title.\n\nDo you want to continue?";
            var result = MessageBox.Show(
                sortDescription,
                "Sort List",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            try
            {
                await Manager.SortList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error Loading data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            IsBusy = false;
        }

        private async void ButtonBatchRename_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0)
                return;

            IsBusy = true;
            try
            {
                var w = new CopyNameWindow();
                w.Owner = this;

                if (!w.ShowDialog().GetValueOrDefault())
                    return;

                // Capture old names before batch rename
                var oldNames = Manager.ItemList.ToDictionary(i => i, i => i.Name);

                var count = await Manager.BatchRenameItems(w.NotOnCard, w.OnCard, w.FolderName, w.ParseTosec);

                // Record undo for items whose names actually changed
                if (count > 0)
                {
                    var undoOp = new MultiPropertyEditOperation("Batch Rename")
                    {
                        PropertyName = nameof(GdItem.Name)
                    };

                    foreach (var item in Manager.ItemList)
                    {
                        if (oldNames.TryGetValue(item, out var oldName) && item.Name != oldName)
                        {
                            undoOp.Edits.Add((item, oldName, item.Name));
                        }
                    }

                    if (undoOp.Edits.Count > 0)
                    {
                        Manager.UndoManager.RecordChange(undoOp);
                    }
                }

                MessageBox.Show($"{count} item(s) renamed", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ButtonDiscImageOptions_Click(object sender, RoutedEventArgs e)
        {
            var window = new DiscImageOptionsWindow(SaveDiscImageOptionsConfig);
            window.DataContext = this;
            window.Owner = this;
            window.ShowDialog();
        }

        private void ButtonDatTools_Click(object sender, RoutedEventArgs e)
        {
            var window = new DatToolsWindow(Manager, async () => await LoadItemsFromCard());
            window.Owner = this;
            window.ShowDialog();
        }

        private void ButtonBatchFolderRename_Click(object sender, RoutedEventArgs e)
        {
            if (IsFilterActive)
                return;
            if (Manager.ItemList.Count == 0)
                return;

            try
            {
                var folderCounts = Manager.GetFolderCounts();

                if (folderCounts.Count == 0)
                {
                    MessageBox.Show("No folders found in the current game list.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var window = new BatchFolderRenameWindow(folderCounts, Manager.ItemList.Count);
                window.Owner = this;

                if (window.ShowDialog() == true && window.FolderMappings != null)
                {
                    // snapshot before applying
                    var snapshots = Manager.ItemList.Select(i => new BatchFolderRenameOperation.ItemSnapshot
                    {
                        Item = i,
                        OldFolder = i.Folder,
                        OldAltFolders = new List<string>(i.AlternativeFolders)
                    }).ToList();

                    var (updatedCount, conflictsRemoved) = Manager.ApplyFolderMappings(window.FolderMappings);

                    if (updatedCount > 0 || conflictsRemoved > 0)
                    {
                        // fill in new values and filter to only changed items
                        var undoOp = new BatchFolderRenameOperation();
                        foreach (var s in snapshots)
                        {
                            s.NewFolder = s.Item.Folder;
                            s.NewAltFolders = new List<string>(s.Item.AlternativeFolders);
                            if (s.OldFolder != s.NewFolder || !s.OldAltFolders.SequenceEqual(s.NewAltFolders))
                                undoOp.Snapshots.Add(s);
                        }

                        if (undoOp.Snapshots.Count > 0)
                            Manager.UndoManager.RecordChange(undoOp);

                        var msg = $"{updatedCount} disc image(s) updated across {window.FolderMappings.Count} folder(s).";
                        if (conflictsRemoved > 0)
                            msg += $"\n\n{conflictsRemoved} additional folder path(s) were automatically removed because they became duplicates of their disc image's primary folder path after renaming.";
                        msg += "\n\nClick 'Save Changes' to write updates to SD card.";

                        MessageBox.Show(msg, "Folders Renamed", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No changes were made.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ButtonPreload_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0)
                return;

            IsBusy = true;
            try
            {
                await Manager.LoadIpAll();
            }
            catch (ProgressWindowClosedException) { }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ButtonRefreshDrive_Click(object sender, RoutedEventArgs e)
        {
            // Clear custom path if set
            if (IsUsingCustomPath)
            {
                CustomSdPath = null;
                Manager.sdPath = null;
                Manager.ItemList.Clear();
            }
            FillDriveList(true);
        }

        private async void ButtonBrowseSdPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select SD Card Folder";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selectedPath = dialog.SelectedPath;

                    // Check if it looks like a GDEMU SD card
                    bool hasGdemuIni = File.Exists(Path.Combine(selectedPath, Constants.MenuConfigTextFile));
                    bool has01Folder = Directory.Exists(Path.Combine(selectedPath, "01"));

                    if (!hasGdemuIni && !has01Folder)
                    {
                        MessageBox.Show(this,
                            "The selected folder does not appear to be a GDEMU SD card.\n\n" +
                            "No GDEMU.INI file or numbered folders (01, 02, etc.) were found.\n\n" +
                            "You may proceed, but the folder may not work as expected.",
                            "Notice",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    // Set the custom path
                    CustomSdPath = selectedPath;
                    Manager.sdPath = selectedPath;
                    SelectedDrive = null; // Clear drive selection

                    // Load items from the custom path
                    await LoadItemsFromCard();
                }
            }
        }

        private void FillDriveList(bool isRefreshing = false)
        {
            var list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || (x.DriveType == DriveType.Removable && x.DriveFormat.StartsWith("FAT")))).ToArray();

            if (isRefreshing)
            {
                if (DriveList.Select(x => x.Name).SequenceEqual(list.Select(x => x.Name)))
                    return;

                DriveList.Clear();
            }
            //fill drive list and try to find drive with gdemu contents
            foreach (DriveInfo drive in list)
            {
                DriveList.Add(drive);
                //look for GDEMU.INI file
                if (SelectedDrive == null && File.Exists(Path.Combine(drive.RootDirectory.FullName, Constants.MenuConfigTextFile)))
                    SelectedDrive = drive;
            }

            //look for 01 folder
            if (SelectedDrive == null)
            {
                foreach (DriveInfo drive in list)
                    if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "01")))
                    {
                        SelectedDrive = drive;
                        break;
                    }
            }


            if (!DriveList.Any())
                return;

            if (SelectedDrive == null)
                SelectedDrive = DriveList.LastOrDefault();
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
            {
                // Exclude menu entry (folder 01) from count
                int count = dg1.SelectedItems.Cast<GdItem>().Count(x => x.SdNumber != 1);
                bool isMultiple = count > 1;

                // Update title header
                var titleItem = menu.Items.OfType<MenuItem>()
                    .FirstOrDefault(m => m.Name == "MenuItemTitle");
                if (titleItem != null)
                {
                    titleItem.Header = isMultiple ? $"{count} Disc Images" : ((GdItem)dg1.SelectedItem)?.Name;
                }

                // Update auto rename header and folder/file sub-items
                var autoRenameItem = menu.Items.OfType<MenuItem>()
                    .FirstOrDefault(m => m.Name == "MenuItemAutoRename");
                if (autoRenameItem != null)
                {
                    autoRenameItem.Header = isMultiple ? "Automatically Rename Titles" : "Automatically Rename Title";

                    // Folder/file rename only available when ALL selected non-menu items are off the SD card
                    bool allOffSdCard = dg1.SelectedItems.Cast<GdItem>()
                        .Where(g => g.SdNumber != 1)
                        .All(g => g.IsNotOnSdCard);

                    var renameFolderItem = autoRenameItem.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Name == "MenuItemRenameFolder");
                    if (renameFolderItem != null)
                        renameFolderItem.IsEnabled = allOffSdCard;

                    var renameFileItem = autoRenameItem.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Name == "MenuItemRenameFile");
                    if (renameFileItem != null)
                        renameFileItem.IsEnabled = allOffSdCard;
                }

                // Update assign folder header
                var assignFolderItem = menu.Items.OfType<MenuItem>()
                    .FirstOrDefault(m => m.Name == "MenuItemAssignFolder");
                if (assignFolderItem != null)
                {
                    assignFolderItem.Header = isMultiple ? "Assign Folder Paths" : "Assign Folder Path";
                }

                var assignAltItem = menu.Items.OfType<MenuItem>()
                    .FirstOrDefault(m => m.Name == "MenuItemAssignAltFolders");
                if (assignAltItem != null)
                {
                    assignAltItem.Header = "Assign Additional Folder Paths";
                    assignAltItem.IsEnabled = !isMultiple;
                }
            }
        }

        private void MenuItemRename_Click(object sender, RoutedEventArgs e)
        {
            // Protect menu entry (folder 01) from renaming
            var selectedItem = dg1.SelectedItem as GdItem;
            if (selectedItem?.SdNumber == 1)
                return;

            dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
            dg1.BeginEdit();
        }

        private void MenuItemRenameSentence_Click(object sender, RoutedEventArgs e)
        {
            dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
            // Filter out menu entry (folder 01) from renaming
            var items = dg1.SelectedItems.Cast<GdItem>().Where(x => x.SdNumber != 1).ToList();

            if (items.Count == 0)
                return;

            var undoOp = new MultiPropertyEditOperation("Title Case")
            {
                PropertyName = nameof(GdItem.Name)
            };

            foreach (var item in items)
            {
                var oldName = item.Name;
                var newName = TitleCaseHelper.ToTitleCase(item.Name);
                if (newName != oldName)
                {
                    undoOp.Edits.Add((item, oldName, newName));
                    item.Name = newName;
                }
            }

            if (undoOp.Edits.Count > 0)
            {
                Manager.UndoManager.RecordChange(undoOp);
            }
        }

        private void MenuItemRenameUppercase_Click(object sender, RoutedEventArgs e)
        {
            dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
            // Filter out menu entry (folder 01) from renaming
            var items = dg1.SelectedItems.Cast<GdItem>().Where(x => x.SdNumber != 1).ToList();

            if (items.Count == 0)
                return;

            var undoOp = new MultiPropertyEditOperation("Uppercase")
            {
                PropertyName = nameof(GdItem.Name)
            };

            foreach (var item in items)
            {
                var oldName = item.Name;
                var newName = item.Name.ToUpperInvariant();
                if (newName != oldName)
                {
                    undoOp.Edits.Add((item, oldName, newName));
                    item.Name = newName;
                }
            }

            if (undoOp.Edits.Count > 0)
            {
                Manager.UndoManager.RecordChange(undoOp);
            }
        }

        private void MenuItemRenameLowercase_Click(object sender, RoutedEventArgs e)
        {
            dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
            // Filter out menu entry (folder 01) from renaming
            var items = dg1.SelectedItems.Cast<GdItem>().Where(x => x.SdNumber != 1).ToList();

            if (items.Count == 0)
                return;

            var undoOp = new MultiPropertyEditOperation("Lowercase")
            {
                PropertyName = nameof(GdItem.Name)
            };

            foreach (var item in items)
            {
                var oldName = item.Name;
                var newName = item.Name.ToLowerInvariant();
                if (newName != oldName)
                {
                    undoOp.Edits.Add((item, oldName, newName));
                    item.Name = newName;
                }
            }

            if (undoOp.Edits.Count > 0)
            {
                Manager.UndoManager.RecordChange(undoOp);
            }
        }

        private async void MenuItemRenameIP_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.Ip);
        }
        private async void MenuItemRenameFolder_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.Folder);
        }
        private async void MenuItemRenameFile_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.File);
        }

        private async Task renameSelection(RenameBy renameBy)
        {
            IsBusy = true;
            try
            {
                // Filter out menu entry (folder 01) from renaming
                var items = dg1.SelectedItems.Cast<GdItem>().Where(x => x.SdNumber != 1).ToList();

                if (items.Count == 0)
                {
                    IsBusy = false;
                    return;
                }

                // Capture old names before rename
                var oldNames = items.ToDictionary(i => i, i => i.Name);

                await Manager.RenameItems(items, renameBy);

                // Record undo for items whose names actually changed
                var undoOp = new MultiPropertyEditOperation($"Rename by {renameBy}")
                {
                    PropertyName = nameof(GdItem.Name)
                };

                foreach (var item in items)
                {
                    if (oldNames.TryGetValue(item, out var oldName) && item.Name != oldName)
                    {
                        undoOp.Edits.Add((item, oldName, item.Name));
                    }
                }

                if (undoOp.Edits.Count > 0)
                {
                    Manager.UndoManager.RecordChange(undoOp);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            IsBusy = false;
        }

        private async void MenuItemAssignFolder_Click(object sender, RoutedEventArgs e)
        {
            // Commit any pending cell edits
            dg1.CommitEdit(DataGridEditingUnit.Cell, true);
            dg1.CommitEdit(DataGridEditingUnit.Row, true);

            // Only allow in openMenu mode
            if (MenuKindSelected != MenuKind.openMenu)
            {
                MessageBox.Show("Assign Folder Path is only available in openMenu mode.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToList();

            // Filter out menu items
            selectedItems = selectedItems.Where(item =>
                item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu").ToList();

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No valid items selected.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // handle serial translations before proceeding
            var translatedItems = selectedItems.Where(item => item.WasSerialTranslated).ToList();
            if (translatedItems.Count > 0)
            {
                _handlingSerialTranslation = true;
                try
                {
                    await Helper.DependencyManager.ShowSerialTranslationDialog(translatedItems);
                }
                finally
                {
                    _handlingSerialTranslation = false;
                }
            }

            Manager.InitializeKnownFolders();
            var dialog = new AssignFolderWindow(selectedItems.Count, Manager.KnownFolders);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                var folderPath = dialog.FolderPath?.Trim() ?? string.Empty;

                // check if the new primary folder conflicts with any item's alt folders
                if (!string.IsNullOrEmpty(folderPath))
                {
                    var conflicting = selectedItems.Where(item =>
                        item.AlternativeFolders.Contains(folderPath)).ToList();
                    if (conflicting.Count > 0)
                    {
                        MessageBox.Show("This folder path is already assigned to this disc image as an additional folder path.",
                            "Duplicate Folder Path", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                var undoOp = new MultiPropertyEditOperation("Assign Folder Path")
                {
                    PropertyName = nameof(GdItem.Folder)
                };

                foreach (var item in selectedItems)
                {
                    var oldFolder = item.Folder;
                    if (oldFolder != folderPath)
                    {
                        undoOp.Edits.Add((item, oldFolder, folderPath));
                        item.Folder = folderPath;
                    }
                }

                if (undoOp.Edits.Count > 0)
                {
                    Manager.UndoManager.RecordChange(undoOp);
                }
            }
        }

        private void MenuItemAssignAltFolders_Click(object sender, RoutedEventArgs e)
        {
            dg1.CommitEdit(DataGridEditingUnit.Cell, true);
            dg1.CommitEdit(DataGridEditingUnit.Row, true);

            if (MenuKindSelected != MenuKind.openMenu)
            {
                MessageBox.Show("Additional folder paths are only available in openMenu mode.",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var item = dg1.SelectedItems.Cast<GdItem>()
                .FirstOrDefault(x => x.SdNumber != 1);

            if (item == null)
                return;

            Manager.InitializeKnownFolders();
            var dlg = new AssignAltFoldersWindow(item, Manager.KnownFolders);
            dlg.Owner = this;

            if (dlg.ShowDialog() == true)
            {
                var oldAltFolders = new List<string>(item.AlternativeFolders);
                var newAltFolders = dlg.GetAltFolders();

                if (!oldAltFolders.SequenceEqual(newAltFolders))
                {
                    item.AlternativeFolders = newAltFolders;
                    Manager.UndoManager.RecordChange(new AltFoldersChangeOperation
                    {
                        Item = item,
                        OldAltFolders = oldAltFolders,
                        NewAltFolders = new List<string>(item.AlternativeFolders)
                    });
                }
            }
        }

        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            // Check if this is a menu item
            if (e.Row?.DataContext is GdItem item)
            {
                bool isMenuItem = item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu";

                if (isMenuItem)
                {
                    // Prevent editing ANY cell for menu items
                    e.Cancel = true;
                    return;
                }

                if (item.FileFormat == FileFormat.SevenZip)
                {
                    var lockedHeader = e.Column.Header?.ToString();
                    if (lockedHeader == "Serial" || lockedHeader == "Type" || lockedHeader == "Disc")
                    {
                        e.Cancel = true;
                        return;
                    }
                }

                // Capture old value for undo
                _editingItem = item;
                var column = e.Column;
                if (column.Header?.ToString() == "Title")
                {
                    _editingPropertyName = nameof(GdItem.Name);
                    _editingOldValue = item.Name;
                }
                else if (column.Header?.ToString() == "Serial")
                {
                    _editingPropertyName = nameof(GdItem.ProductNumber);
                    _editingOldValue = item.ProductNumber;
                }
                else if (column.Header?.ToString() == "Folder")
                {
                    _editingPropertyName = nameof(GdItem.Folder);
                    _editingOldValue = item.Folder;
                }
                else if (column.Header?.ToString() == "Type")
                {
                    _editingPropertyName = nameof(GdItem.DiscType);
                    _editingOldValue = item.DiscType;
                }
                else if (column.Header?.ToString() == "Disc")
                {
                    _editingPropertyName = nameof(GdItem.Disc);
                    _editingOldValue = item.Disc;
                }
                else
                {
                    _editingItem = null;
                    _editingPropertyName = null;
                    _editingOldValue = null;
                }
            }
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel)
            {
                // Edit was cancelled, no undo needed
                _editingItem = null;
                _editingPropertyName = null;
                _editingOldValue = null;
                return;
            }

            if (_editingItem == null || _editingPropertyName == null)
                return;

            // Capture values in local variables
            var item = _editingItem;
            var propertyName = _editingPropertyName;
            var oldValue = _editingOldValue;

            // Try to get the new value directly from the editing element
            // This is more reliable than waiting for binding to update
            object newValue = null;
            if (e.EditingElement is TextBox textBox)
            {
                newValue = textBox.Text;
            }
            else if (e.EditingElement is ComboBox comboBox)
            {
                // For ComboBox, check if it's text-based (IsEditable) or selection-based
                if (comboBox.IsEditable)
                    newValue = comboBox.Text;
                else
                    newValue = comboBox.SelectedItem;
            }
            else
            {
                // For template columns, the editing element might be a container
                // Try to find the actual control within
                var comboBoxInTemplate = FindVisualChild<ComboBox>(e.EditingElement);
                if (comboBoxInTemplate != null)
                {
                    if (comboBoxInTemplate.IsEditable)
                        newValue = comboBoxInTemplate.Text;
                    else
                        newValue = comboBoxInTemplate.SelectedItem;
                }
                else
                {
                    var textBoxInTemplate = FindVisualChild<TextBox>(e.EditingElement);
                    if (textBoxInTemplate != null)
                    {
                        newValue = textBoxInTemplate.Text;
                    }
                }
            }

            // Validate printable ASCII for Title, Serial, and Folder columns
            if (newValue is string newStr &&
                (propertyName == nameof(GdItem.Name) || propertyName == nameof(GdItem.ProductNumber) || propertyName == nameof(GdItem.Folder)) &&
                !Helper.IsValidPrintableAscii(newStr))
            {
                MessageBox.Show(
                    "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                    "Invalid Characters", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Revert the editing element
                var revertValue = oldValue as string ?? "";
                if (e.EditingElement is TextBox revertTb)
                    revertTb.Text = revertValue;
                else if (e.EditingElement is ComboBox revertCb)
                    revertCb.Text = revertValue;
                else
                {
                    var comboInTemplate = FindVisualChild<ComboBox>(e.EditingElement);
                    if (comboInTemplate != null)
                        comboInTemplate.Text = revertValue;
                    else
                    {
                        var tbInTemplate = FindVisualChild<TextBox>(e.EditingElement);
                        if (tbInTemplate != null)
                            tbInTemplate.Text = revertValue;
                    }
                }
                e.Cancel = true;
                // keep editing state so the next commit attempt can validate
                return;
            }

            // Clear immediately so next edit can capture its own values
            _editingItem = null;
            _editingPropertyName = null;
            _editingOldValue = null;

            // Only record if we got a new value and it's different from old
            if (newValue != null && !Equals(oldValue, newValue))
            {
                Manager.UndoManager.RecordChange(new PropertyEditOperation
                {
                    Item = item,
                    PropertyName = propertyName,
                    OldValue = oldValue,
                    NewValue = newValue
                });

                // If Serial column was edited, check for translation after binding updates
                // Skip if a button handler is already handling the translation
                if (propertyName == nameof(GdItem.ProductNumber))
                {
                    // Post to dispatcher so the binding has time to update the property
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        if (!_handlingSerialTranslation && item.WasSerialTranslated)
                        {
                            await Helper.DependencyManager.ShowSerialTranslationDialog(new[] { item });
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle Ctrl+Z for Undo
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Manager.UndoManager.CanUndo)
                {
                    Manager.UndoManager.Undo();
                    e.Handled = true;
                }
            }
            // Handle Ctrl+Y for Redo
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Manager.UndoManager.CanRedo)
                {
                    Manager.UndoManager.Redo();
                    e.Handled = true;
                }
            }
        }

        private async void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && !(e.OriginalSource is TextBox))
            {
                dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
                dg1.BeginEdit();
            }
            else if (e.Key == Key.Delete && !(e.OriginalSource is TextBox))
            {
                var grid = (DataGrid)sender;
                List<GdItem> toRemove = new List<GdItem>();
                foreach (GdItem item in grid.SelectedItems)
                {
                    if (item.SdNumber == 1)
                    {
                        if (item.Ip == null)
                        {
                            IsBusy = true;
                            await Manager.LoadIP(item);
                            IsBusy = false;
                        }
                        if (item.Ip.Name != "GDMENU" && item.Ip.Name != "openMenu")//dont let the user exclude GDMENU
                            toRemove.Add(item);
                    }
                    else
                    {
                        toRemove.Add(item);
                    }
                }

                if (toRemove.Count > 0)
                {
                    // Record undo operation with indices before removal
                    var undoOp = new MultiItemRemoveOperation { ItemList = Manager.ItemList };
                    foreach (var item in toRemove)
                    {
                        undoOp.Items.Add((item, Manager.ItemList.IndexOf(item)));
                    }

                    foreach (var item in toRemove)
                        Manager.ItemList.Remove(item);

                    Manager.UndoManager.RecordChange(undoOp);

                    if (IsFilterActive)
                    {
                        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Manager.ItemList);
                        if (!view.Cast<object>().Any())
                        {
                            MessageBox.Show("Nothing to show for the currently applied filter.", "Filter", MessageBoxButton.OK, MessageBoxImage.Information);
                            ClearFilterFromGrid();
                        }
                    }
                }

                e.Handled = true;
            }
        }

        private async void ButtonAddGames_Click(object sender, RoutedEventArgs e)
        {
            if (IsFilterActive)
                return;
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Filter = fileFilterList;
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    IsBusy = true;

                    var invalid = await Manager.AddGames(dialog.FileNames);

                    if (invalid.Any())
                    {
                        var w = new TextWindow("Ignored folders/files", string.Join(Environment.NewLine, invalid));
                        w.Owner = this;
                        w.ShowDialog();
                    }

                    // Check for serial translations that were applied
                    await ShowSerialTranslationDialogIfNeeded();

                    IsBusy = false;
                }
            }
        }

        private void ButtonRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            if (dg1.SelectedItems.Count == 0)
                return;

            // Collect items and indices before removal for undo
            var undoOp = new MultiItemRemoveOperation { ItemList = Manager.ItemList };
            foreach (GdItem item in dg1.SelectedItems)
            {
                undoOp.Items.Add((item, Manager.ItemList.IndexOf(item)));
            }

            while (dg1.SelectedItems.Count > 0)
                Manager.ItemList.Remove((GdItem)dg1.SelectedItems[0]);

            Manager.UndoManager.RecordChange(undoOp);

            if (IsFilterActive)
            {
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Manager.ItemList);
                if (!view.Cast<object>().Any())
                {
                    MessageBox.Show("Nothing to show for the currently applied filter.", "Filter", MessageBoxButton.OK, MessageBoxImage.Information);
                    ClearFilterFromGrid();
                }
            }
        }

        private async void ButtonSearch_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0 || string.IsNullOrWhiteSpace(Filter))
                return;

            try
            {
                IsBusy = true;
                await Manager.LoadIpAll();
                IsBusy = false;
            }
            catch (ProgressWindowClosedException)
            {

            }

            if (dg1.SelectedIndex == -1 || !searchInGrid(dg1.SelectedIndex))
            {
                if (!searchInGrid(0))
                    MessageBox.Show("No matches found.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool searchInGrid(int start)
        {
            var visibleItems = System.Windows.Data.CollectionViewSource.GetDefaultView(Manager.ItemList).Cast<GdItem>().ToList();

            for (int i = start; i < visibleItems.Count; i++)
            {
                var item = visibleItems[i];
                if (dg1.SelectedItem != item && Manager.SearchInItem(item, Filter))
                {
                    dg1.SelectedItem = item;
                    dg1.ScrollIntoView(item);
                    return true;
                }
            }
            return false;
        }

        private bool FilterInItem(GdItem item, string text)
        {
            if (item.Name?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) >= 0)
                return true;
            if (item.ProductNumber?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) >= 0)
                return true;
            return false;
        }

        private void ApplyFilterToGrid(string filterText)
        {
            _activeFilterText = filterText;
            Filter = filterText;
            IsFilterActive = true;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Manager.ItemList);
            view.Filter = obj => obj is GdItem item && FilterInItem(item, filterText);
        }

        private void ClearFilterFromGrid()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Manager.ItemList);
            view.Filter = null;

            _activeFilterText = null;
            Filter = null;
            IsFilterActive = false;
        }

        private async void ButtonFilter_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0 || string.IsNullOrWhiteSpace(Filter))
                return;

            var filterText = Filter;

            try
            {
                IsBusy = true;
                await Manager.LoadIpAll();
                IsBusy = false;
            }
            catch (ProgressWindowClosedException) { }

            bool hasMatches = Manager.ItemList.Any(item => FilterInItem(item, filterText));
            if (!hasMatches)
            {
                MessageBox.Show("No matches found.", "Filter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ApplyFilterToGrid(filterText);

            Manager.UndoManager.RecordChange(new FilterApplyOperation
            {
                FilterText = filterText,
                ApplyFilter = text => ApplyFilterToGrid(text),
                ClearFilter = () => ClearFilterFromGrid()
            });
        }

        private void ButtonFilterReset_Click(object sender, RoutedEventArgs e)
        {
            if (!IsFilterActive)
                return;
            ClearFilterFromGrid();
        }

        private void FolderComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Refresh known folders list to include any newly typed values
            Manager.InitializeKnownFolders();

            // Store the original folder value
            if (sender is ComboBox comboBox && comboBox.DataContext is GdItem item)
            {
                _originalFolderValue = item.Folder;
            }
        }

        private void FolderComboBox_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Capture the raw text BEFORE the binding pushes (UpdateSourceTrigger=LostFocus
            // will strip non-ASCII via CleanFolderPath before FolderComboBox_LostFocus fires)
            if (sender is ComboBox comboBox)
            {
                _rawFolderText = comboBox.Text;
            }
        }

        private void FolderComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is ComboBox comboBox && comboBox.DataContext is GdItem item)
            {
                // Validate printable ASCII
                if (!string.IsNullOrWhiteSpace(comboBox.Text) && !Helper.IsValidPrintableAscii(comboBox.Text))
                {
                    MessageBox.Show(
                        "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                        "Invalid Characters", MessageBoxButton.OK, MessageBoxImage.Warning);
                    comboBox.Text = _originalFolderValue ?? string.Empty;
                    e.Handled = true;
                    return;
                }

                // If user presses Enter on empty text, clear the folder value
                if (string.IsNullOrWhiteSpace(comboBox.Text))
                {
                    item.Folder = string.Empty;
                    _originalFolderValue = null;
                    dg1.Focus();
                    e.Handled = true;
                }
            }
        }

        private void FolderComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.DataContext is GdItem item)
            {
                // Use raw text captured before the binding pushed (the binding's
                // UpdateSourceTrigger=LostFocus strips non-ASCII via CleanFolderPath
                // before this handler fires, so comboBox.Text is already clean)
                var textBeforeBinding = _rawFolderText ?? comboBox.Text;
                _rawFolderText = null;

                // If empty, restore original
                if (string.IsNullOrWhiteSpace(textBeforeBinding) && !string.IsNullOrWhiteSpace(_originalFolderValue))
                {
                    item.Folder = _originalFolderValue;
                    _originalFolderValue = null;
                    return;
                }

                // Validate printable ASCII against the raw text
                if (!Helper.IsValidPrintableAscii(textBeforeBinding))
                {
                    MessageBox.Show(
                        "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                        "Invalid Characters", MessageBoxButton.OK, MessageBoxImage.Warning);
                    item.Folder = _originalFolderValue ?? string.Empty;
                    comboBox.Text = _originalFolderValue ?? string.Empty;
                    _originalFolderValue = null;
                    return;
                }

                // Check if new folder value conflicts with an alt folder on this item
                var newFolder = comboBox.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(newFolder) && item.AlternativeFolders.Contains(newFolder))
                {
                    MessageBox.Show("This folder path is already assigned to this disc image as an additional folder path.",
                        "Duplicate Folder Path", MessageBoxButton.OK, MessageBoxImage.Information);
                    item.Folder = _originalFolderValue ?? string.Empty;
                    comboBox.Text = _originalFolderValue ?? string.Empty;
                }

                _originalFolderValue = null;
            }
        }

        /// <summary>
        /// Finds the first child of the specified type in the visual tree.
        /// </summary>
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found)
                    return found;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

    }
}
