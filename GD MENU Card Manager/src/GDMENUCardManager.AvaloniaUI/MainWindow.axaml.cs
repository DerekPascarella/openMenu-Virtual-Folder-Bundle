using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GDMENUCardManager.Core;
using System.Configuration;

namespace GDMENUCardManager
{
    public class MainWindow : Window, INotifyPropertyChanged, IDiscImageOptionsViewModel
    {
        private GDMENUCardManager.Core.Manager _ManagerInstance;
        public GDMENUCardManager.Core.Manager Manager { get { return _ManagerInstance; } }

        private readonly bool showAllDrives = false;

        public new event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<DriveInfo> DriveList { get; } = new ObservableCollection<DriveInfo>();

        public static List<string> DiscTypes { get; } = new List<string> { "Game", "Other", "PSX" };

        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            private set { _IsBusy = value; RaisePropertyChanged(); }
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

        public bool EnableLockCheck
        {
            get { return Manager.EnableLockCheck; }
            set { Manager.EnableLockCheck = value; RaisePropertyChanged(); }
        }

        private readonly List<FileDialogFilter> fileFilterList;


        #region window controls
        DataGrid dg1;
        Button ButtonSort;
        #endregion

        // Undo tracking for cell edits
        private GdItem _editingItem;
        private string _editingPropertyName;
        private object _editingOldValue;

        // Flag to prevent duplicate serial translation dialogs
        private bool _handlingSerialTranslation;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
            //this.OpenDevTools();
#endif

            var compressedFileFormats = new string[] { ".7z", ".rar", ".zip" };
            _ManagerInstance = GDMENUCardManager.Core.Manager.CreateInstance(new DependencyManager(), compressedFileFormats);
            var fullList = Manager.supportedImageFormats.Concat(compressedFileFormats).ToArray();
            fileFilterList = new List<FileDialogFilter>
            {
                new FileDialogFilter
                {
                    Name = $"Dreamcast Game ({string.Join("; ", fullList.Select(x => $"*{x}"))})",
                    Extensions = fullList.Select(x => x.Substring(1)).ToList()
                }
            };

            // Clean up any leftover staging data from a previous update attempt
            UpdateManager.CleanupStaleStagingData();

            this.Opened += async (ss, ee) =>
            {
                await CheckConfigWritability();

                // macOS first-time setup: copy BOX.DAT, ICON.DAT, META.DAT from the bundle to
                // ~/Library/Application Support/GDMENUCardManager/menu_data/ with a progress bar.
                // This runs fully before anything else so the DAT files are in place before any
                // card loading or artwork operations occur.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    && MacOsDataMigration.NeedsFirstTimeDatSetup())
                {
                    var progressWindow = new ProgressWindow();
                    progressWindow.Title = "First-Time Setup";
                    progressWindow.TotalItems = 3;
                    progressWindow.TextContent = "Performing first-time setup...";
                    progressWindow.Show(this);

                    var progress = new Progress<(int current, int total, string name)>(p =>
                    {
                        progressWindow.ProcessedItems = p.current;
                        progressWindow.TextContent =
                            $"Performing first-time DAT copying to Application Support ({p.current} of {p.total}): {p.name}";
                    });

                    await Task.Run(() =>
                        MacOsDataMigration.PerformFirstTimeDatCopy(
                            AppDomain.CurrentDomain.BaseDirectory, progress));

                    progressWindow.AllowClose();
                    progressWindow.Close();
                }

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
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => UpdateFolderColumnVisibility(), Avalonia.Threading.DispatcherPriority.Loaded);

                // Check for updates (non-blocking, silent on failure)
                _ = CheckForUpdateAsync();
            };

            this.Closing += MainWindow_Closing;
            this.PropertyChanged += MainWindow_PropertyChanged;
            this.KeyDown += MainWindow_KeyDown;
            Manager.ItemList.CollectionChanged += ItemList_CollectionChanged;
            Manager.MenuKindChanged += Manager_MenuKindChanged;

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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            this.AddHandler(DragDrop.DropEvent, WindowDrop);
            dg1 = this.FindControl<DataGrid>("dg1");
            ButtonSort = this.FindControl<Button>("ButtonSort");

            // Add tunneling handler to intercept right-clicks before context menu opens
            dg1.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, DataGrid_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            dg1.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, DataGrid_PointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        // Track if we should block context menu for current right-click
        private bool _blockContextMenu = false;

        private void DataGrid_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            _blockContextMenu = false;

            // Only handle right-clicks
            if (!e.GetCurrentPoint(dg1).Properties.IsRightButtonPressed)
                return;

            // Find the DataGridRow under the pointer
            var source = e.Source as Avalonia.Controls.Control;
            Avalonia.Controls.DataGridRow clickedRow = null;
            GdItem clickedItem = null;
            while (source != null)
            {
                if (source is Avalonia.Controls.DataGridRow row)
                {
                    clickedRow = row;
                    clickedItem = row.DataContext as GdItem;
                    break;
                }
                source = source.Parent as Avalonia.Controls.Control;
            }

            // block context menu on menu entry (folder 01)
            if (clickedItem?.SdNumber == 1)
            {
                _blockContextMenu = true;
                e.Handled = true;
                return;
            }

            // Determine if multiple items will be selected after this click
            // If clicked item is already in selection, use current selection count (excluding menu entry)
            // If not, it will become a single selection
            int count;
            if (dg1.SelectedItems.Contains(clickedItem))
            {
                // Exclude menu entry (folder 01) from count
                count = dg1.SelectedItems.Cast<GdItem>().Count(x => x.SdNumber != 1);
            }
            else
            {
                count = 1;
            }
            bool isMultiple = count > 1;
            string singleItemName = isMultiple ? null : (clickedItem?.Name ?? ((GdItem)dg1.SelectedItem)?.Name ?? "");

            // Update context menu headers before it opens
            if (dg1.TryFindResource("rowmenu", out var resource) && resource is ContextMenu menu)
            {
                // Update title header
                var titleItem = menu.Items.OfType<MenuItem>()
                    .FirstOrDefault(m => m.Name == "MenuItemTitle");
                if (titleItem != null)
                {
                    titleItem.Header = isMultiple ? $"{count} Disc Images" : singleItemName;
                }

                // Update auto rename header and folder/file sub-items
                var autoRenameItem = menu.Items.OfType<MenuItem>()
                    .FirstOrDefault(m => m.Name == "MenuItemAutoRename");
                if (autoRenameItem != null)
                {
                    autoRenameItem.Header = isMultiple ? "Automatically Rename Titles" : "Automatically Rename Title";

                    // Folder/file rename only available when ALL selected non-menu items are off the SD card
                    // When right-clicking an unselected item, SelectedItems hasn't updated yet,
                    // so use clickedItem directly for the single-selection case
                    bool allOffSdCard;
                    if (dg1.SelectedItems.Contains(clickedItem))
                    {
                        allOffSdCard = dg1.SelectedItems.Cast<GdItem>()
                            .Where(g => g.SdNumber != 1)
                            .All(g => g.IsNotOnSdCard);
                    }
                    else
                    {
                        allOffSdCard = clickedItem?.IsNotOnSdCard ?? true;
                    }

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

        private void DataGrid_PointerReleased(object sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            // Block context menu for menu entry (folder 01) on pointer release too
            if (_blockContextMenu && e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right)
            {
                e.Handled = true;
                _blockContextMenu = false;
            }
        }

        private void UpdateFolderColumnVisibility()
        {
            if (dg1?.Columns == null)
                return;

            // Find columns by iterating and checking their Header
            DataGridColumn folderColumn = null;
            DataGridColumn typeColumn = null;
            DataGridColumn artColumn = null;
            DataGridTemplateColumn discColumn = null;

            foreach (var col in dg1.Columns)
            {
                if (col.Header?.ToString() == "Folder")
                    folderColumn = col;
                else if (col is DataGridTemplateColumn templateCol && templateCol.Header?.ToString() == "Type")
                    typeColumn = col;
                else if (col is DataGridTemplateColumn discTemplateCol && discTemplateCol.Header?.ToString() == "Disc")
                    discColumn = discTemplateCol;
                else if (col.Header?.ToString() == "Art")
                    artColumn = col;
            }

            if (folderColumn != null)
            {
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    folderColumn.IsVisible = true;
                    folderColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
                else
                {
                    folderColumn.IsVisible = false;
                }
            }

            if (typeColumn != null)
            {
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    typeColumn.IsVisible = true;
                }
                else
                {
                    typeColumn.IsVisible = false;
                }
            }

            // Art column: only visible in openMenu mode
            if (artColumn != null)
            {
                bool showArt = MenuKindSelected == MenuKind.openMenu;
                artColumn.IsVisible = showArt;
            }

            // Disc column read-only handling is now done via BeginningEdit event
            // since it's a template column
        }

        private void UpdateSortButtonTooltip()
        {
            if (ButtonSort == null) return;
            ToolTip.SetTip(ButtonSort, MenuKindSelected == MenuKind.openMenu
                ? "Sort list by folder path + title"
                : "Sort list by title");
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

            // Prevent editing the Disc column when not in openMenu mode (for non-menu items)
            if (e.Column?.Header?.ToString() == "Disc" && MenuKindSelected != MenuKind.openMenu)
            {
                e.Cancel = true;
                _editingItem = null;
                _editingPropertyName = null;
                _editingOldValue = null;
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
                // Avalonia ComboBox uses SelectedItem
                newValue = comboBox.SelectedItem;
            }
            else if (e.EditingElement is Panel panel)
            {
                // Template columns may wrap the actual control in a panel
                var innerTextBox = panel.Children.OfType<TextBox>().FirstOrDefault();
                if (innerTextBox != null)
                {
                    newValue = innerTextBox.Text;
                }
                else
                {
                    var innerComboBox = panel.Children.OfType<ComboBox>().FirstOrDefault();
                    if (innerComboBox != null)
                    {
                        newValue = innerComboBox.SelectedItem;
                    }
                }
            }

            // Validate printable ASCII for Title, Serial, and Folder columns
            if (newValue is string newStr &&
                (propertyName == nameof(GdItem.Name) || propertyName == nameof(GdItem.ProductNumber) || propertyName == nameof(GdItem.Folder)) &&
                !Helper.IsValidPrintableAscii(newStr))
            {
                // Revert the editing element and the property (binding may have already pushed)
                var revertValue = oldValue as string ?? "";
                if (e.EditingElement is TextBox revertTb)
                    revertTb.Text = revertValue;
                else if (e.EditingElement is Panel revertPanel)
                {
                    var innerTb = revertPanel.Children.OfType<TextBox>().FirstOrDefault();
                    if (innerTb != null) innerTb.Text = revertValue;
                }
                // Revert the property in case binding already updated it
                RevertProperty(item, propertyName, oldValue);
                e.Cancel = true;
                // keep editing state so the next commit attempt can validate
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await MessageBoxManager.GetMessageBoxStandardWindow("Invalid Characters",
                        "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                        icon: MessageBox.Avalonia.Enums.Icon.Warning).ShowDialog(this);
                });
                return;
            }

            // Clear immediately so next edit can capture its own values
            _editingItem = null;
            _editingPropertyName = null;
            _editingOldValue = null;

            // Only record if we got a new value and it's different from old
            if (newValue != null && !Equals(oldValue, newValue))
            {
                // check if Folder edit conflicts with an alt folder
                if (propertyName == nameof(GdItem.Folder) && newValue is string newFolder)
                {
                    var trimmed = newFolder.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && item.AlternativeFolders.Contains(trimmed))
                    {
                        // Revert the property in case binding already updated it
                        RevertProperty(item, propertyName, oldValue);
                        e.Cancel = true;
                        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                        {
                            await MessageBoxManager.GetMessageBoxStandardWindow("Duplicate Folder Path",
                                "This folder path is already assigned to this disc image as an additional folder path.",
                                icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
                        });
                        return;
                    }
                }

                Manager.UndoManager.RecordChange(new PropertyEditOperation
                {
                    Item = item,
                    PropertyName = propertyName,
                    OldValue = oldValue,
                    NewValue = newValue
                });

                // If Serial column was edited, check for translation after binding updates
                if (propertyName == nameof(GdItem.ProductNumber))
                {
                    // Post to dispatcher so the binding has time to update the property
                    // Skip if a button handler is already handling the translation
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        if (!_handlingSerialTranslation && item.WasSerialTranslated)
                        {
                            await Helper.DependencyManager.ShowSerialTranslationDialog(new[] { item });
                        }
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void RevertProperty(GdItem item, string propertyName, object oldValue)
        {
            switch (propertyName)
            {
                case nameof(GdItem.Name):
                    item.Name = oldValue as string;
                    break;
                case nameof(GdItem.ProductNumber):
                    item.ProductNumber = oldValue as string;
                    break;
                case nameof(GdItem.Folder):
                    item.Folder = oldValue as string;
                    break;
            }
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
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RaisePropertyChanged(nameof(MenuKindSelected));
                UpdateFolderColumnVisibility();
                UpdateSortButtonTooltip();
            }, Avalonia.Threading.DispatcherPriority.Send);
        }

        private void ItemList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            updateTotalSize();

            // If filter is active, refresh the filtered view (e.g., after undo re-inserts items)
            if (IsFilterActive && _activeFilterText != null)
            {
                var filteredItems = Manager.ItemList.Where(item => FilterInItem(item, _activeFilterText)).ToList();
                if (filteredItems.Count == 0)
                    ClearFilterFromGrid();
                else
                    dg1.Items = filteredItems;
            }
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
                    await manualDialog.ShowDialog(this);
                }
                else if (result.UpdateAvailable && !UpdateAvailableDialog.ShouldSkipVersion(result.LatestTag))
                {
                    var dialog = new UpdateAvailableDialog(result.LatestTag, result.LatestVersion);
                    await dialog.ShowDialog(this);

                    if (dialog.UserWantsUpdate)
                    {
                        var wizard = new UpdateWizardWindow(result.LatestTag, result.LatestVersion);
                        await wizard.ShowDialog(this);
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
                    await scanDialog.ShowDialog(this);

                    if (scanDialog.StartScan)
                    {
                        // Perform the metadata scan with progress window
                        await PerformMetadataScan(itemsNeedingScan);
                    }
                    else
                    {
                        // quit
                        Close();
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

                // Show serial translation dialog if any items were translated
                await ShowSerialTranslationDialogIfNeeded();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Invalid Folders", $"Problem loading the following folder(s):\n\n{ex.Message}", icon: MessageBox.Avalonia.Enums.Icon.Warning).ShowDialog(this);
            }
            finally
            {
                RaisePropertyChanged(nameof(MenuKindSelected));
                UpdateFolderColumnVisibility();
                IsBusy = false;
            }
        }

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
                        var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
                        {
                            ContentTitle = "DAT Files Missing",
                            ContentMessage = "BOX.DAT and ICON.DAT were not found in the expected location.\n\n" +
                                "These files are required for artwork display in openMenu.\n\n" +
                                "Click Create to create empty DAT files.\n\n" +
                                "Click Close to close and add files manually.\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Create" },
                                new ButtonDefinition { Name = "Close" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowDialog(this);

                        if (result == "Create")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.CreateEmptyDatFiles();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandardWindow("Error", $"Failed to create DAT files: {error}", icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
                                Manager.ArtworkDisabled = true;
                            }
                        }
                        else if (result == "Close")
                        {
                            SelectedDrive = null;
                        }
                        else
                        {
                            Manager.ArtworkDisabled = true;
                        }
                        break;
                    }

                case DatFileStatus.BoxMissingIconExists:
                    {
                        var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
                        {
                            ContentTitle = "BOX.DAT Missing",
                            ContentMessage = "BOX.DAT was not found but ICON.DAT exists.\n\n" +
                                "BOX.DAT is required for artwork management.\n\n" +
                                "Click Create to create an empty BOX.DAT file.\n\n" +
                                "Click Close to close and add BOX.DAT manually.\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Create" },
                                new ButtonDefinition { Name = "Close" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowDialog(this);

                        if (result == "Create")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.CreateEmptyBoxDat();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandardWindow("Error", $"Failed to create BOX.DAT: {error}", icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
                                Manager.ArtworkDisabled = true;
                            }
                        }
                        else if (result == "Close")
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
                        var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
                        {
                            ContentTitle = "ICON.DAT Missing",
                            ContentMessage = "ICON.DAT was not found but BOX.DAT exists.\n\n" +
                                "ICON.DAT can be generated from BOX.DAT by downscaling the artwork.\n\n" +
                                "Click Generate to generate ICON.DAT from BOX.DAT (recommended).\n\n" +
                                "Click Close to close and add ICON.DAT manually.\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MessageBox.Avalonia.Enums.Icon.Question,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Generate" },
                                new ButtonDefinition { Name = "Close" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowDialog(this);

                        if (result == "Generate")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.GenerateIconDatFromBox();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandardWindow("Error", $"Failed to generate ICON.DAT: {error}", icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
                                Manager.ArtworkDisabled = true;
                            }
                        }
                        else if (result == "Close")
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
                        var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
                        {
                            ContentTitle = "DAT File Mismatch",
                            ContentMessage = "ICON.DAT entries don't match BOX.DAT entries.\n\n" +
                                "This can happen if the files were modified independently.\n\n" +
                                "Click Regenerate to regenerate ICON.DAT from BOX.DAT (recommended).\n\n" +
                                "Click Proceed to proceed with mismatched files (some icons may be missing).\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Regenerate" },
                                new ButtonDefinition { Name = "Proceed" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowDialog(this);

                        if (result == "Regenerate")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) break;
                            var (success, error) = Manager.GenerateIconDatFromBox();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandardWindow("Error", $"Failed to regenerate ICON.DAT: {error}", icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
                            }
                        }
                        else if (result == "Skip")
                        {
                            Manager.ArtworkDisabled = true;
                        }
                        // Proceed = continue with mismatched files, do nothing
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
                    var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
                    {
                        ContentTitle = "Warning",
                        ContentMessage = "One or more disc images that are part of multi-disc sets do not have a required Serial value assigned to them, which will break their display in openMenu.\n\nDo you want to proceed and ignore the disc numbers and counts, or return to make edits?",
                        Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                        ShowInCenter = true,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ButtonDefinitions = new ButtonDefinition[]
                        {
                            new ButtonDefinition { Name = "Return" },
                            new ButtonDefinition { Name = "Proceed" }
                        }
                    }).ShowDialog(this);

                    if (result == "Return")
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
                    var result = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
                    {
                        ContentTitle = "Warning",
                        ContentMessage = "One or more multi-disc set exceeds 10 discs total, the maximum supported by openMenu.\n\nDo you want to proceed or return to make edits?",
                        Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                        ShowInCenter = true,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ButtonDefinitions = new ButtonDefinition[]
                        {
                            new ButtonDefinition { Name = "Return" },
                            new ButtonDefinition { Name = "Proceed" }
                        }
                    }).ShowDialog(this);

                    if (result == "Return")
                    {
                        IsBusy = false;
                        return;
                    }
                }

                if (await Manager.Save(TempFolder))
                {
                    SaveTempFolderConfig();
                    SaveLockCheckConfig();
                    await MessageBoxManager.GetMessageBoxStandardWindow("Message", "Done!").ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Error", ex.Message, icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
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
                    foreach (var screen in Screens.All)
                    {
                        var bounds = screen.WorkingArea;
                        if (left + width > bounds.X && left < bounds.X + bounds.Width
                            && top + height > bounds.Y && top < bounds.Y + bounds.Height)
                        {
                            isOnScreen = true;
                            break;
                        }
                    }

                    if (isOnScreen)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Position = new Avalonia.PixelPoint((int)left, (int)top);
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

                // Save current bounds (Avalonia doesn't have RestoreBounds, use current values)
                SetOrAddSetting(config, "WindowLeft", Position.X.ToString());
                SetOrAddSetting(config, "WindowTop", Position.Y.ToString());
                SetOrAddSetting(config, "WindowWidth", Width.ToString());
                SetOrAddSetting(config, "WindowHeight", Height.ToString());
                config.Save(System.Configuration.ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch { }
        }

        private async void WindowDrop(object sender, DragEventArgs e)
        {
            if (IsFilterActive)
                return;
            if (Manager.sdPath == null)
                return;

            if (e.Data.Contains(DataFormats.FileNames))
            {
                IsBusy = true;
                var invalid = new List<string>();
                var addedItems = new List<(GdItem Item, int Index)>();

                try
                {
                    foreach (var o in e.Data.GetFileNames())
                    {
                        try
                        {
                            var gdItem = await ImageHelper.CreateGdItemAsync(o);
                            int index = Manager.ItemList.Count;
                            Manager.ItemList.Add(gdItem);
                            addedItems.Add((gdItem, index));
                        }
                        catch (Exception ex)
                        {
                            invalid.Add($"{o} - {ex.Message}");
                        }
                    }

                    // Record undo operation if any items were added
                    if (addedItems.Count > 0)
                    {
                        var undoOp = new MultiItemAddOperation { ItemList = Manager.ItemList };
                        undoOp.Items.AddRange(addedItems);
                        Manager.UndoManager.RecordChange(undoOp);

                        // Show serial translation dialog if any items were translated
                        await ShowSerialTranslationDialogIfNeeded();
                    }

                    if (invalid.Any())
                        await MessageBoxManager.GetMessageBoxStandardWindow("Ignored folders/files", string.Join(Environment.NewLine, invalid), icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
                }
                catch (Exception)
                {
                }
                finally
                {
                    IsBusy = false;
                }
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
                var msgBox = MessageBoxManager.GetMessageBoxStandardWindow("Missing Serial IDs",
                    msg, MessageBox.Avalonia.Enums.ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error);
                await msgBox.ShowDialog(this);
                return;
            }

            await Save();
        }

        private async void ButtonAbout_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            if (Manager.debugEnabled)
            {
                var list = DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => $"{x.DriveType}; {x.DriveFormat}; {x.Name}").ToArray();
                await MessageBoxManager.GetMessageBoxStandardWindow("Debug", string.Join(Environment.NewLine, list), icon: MessageBox.Avalonia.Enums.Icon.None).ShowDialog(this);
            }
            await new AboutWindow().ShowDialog(this);
            IsBusy = false;
        }

        private async void ButtonFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new OpenFolderDialog { Title = "Select Temporary Folder" };

            if (!string.IsNullOrEmpty(TempFolder))
                folderDialog.Directory = TempFolder;

            var selectedFolder = await folderDialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(selectedFolder))
                TempFolder = selectedFolder;
        }

        private async void ButtonResetTempFolder_Click(object sender, RoutedEventArgs e)
        {
            var result = await MessageBoxManager.GetMessageBoxStandardWindow("Reset", "Reset the Temporary Folder path to default?", MessageBox.Avalonia.Enums.ButtonEnum.YesNo, MessageBox.Avalonia.Enums.Icon.Question).ShowDialog(this);
            if (result == MessageBox.Avalonia.Enums.ButtonResult.Yes)
            {
                TempFolder = Path.GetTempPath();
                SaveTempFolderConfig();
            }
        }

        private async void ButtonInfo_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            try
            {
                var btn = (Button)sender;
                var item = (GdItem)btn.CommandParameter;

                if (item.Ip == null)
                    await Manager.LoadIP(item);

                await new InfoWindow(item).ShowDialog(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Error", ex.Message, icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            }
            IsBusy = false;
        }

        private async void ButtonArtwork_Click(object sender, RoutedEventArgs e)
        {
            // Commit any pending cell edits to ensure we read the current Serial value
            dg1.CommitEdit();

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
                await new ArtworkWindow(item, Manager, navigableItems).ShowDialog(this);

                // Refresh column visibility in case BoxDat state changed
                UpdateFolderColumnVisibility();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Error", ex.Message, icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
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
            var result = await MessageBoxManager.GetMessageBoxStandardWindow(
                "Sort List",
                sortDescription,
                MessageBox.Avalonia.Enums.ButtonEnum.YesNo,
                MessageBox.Avalonia.Enums.Icon.Question).ShowDialog(this);

            if (result != MessageBox.Avalonia.Enums.ButtonResult.Yes)
                return;

            IsBusy = true;
            try
            {
                await Manager.SortList();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Error", ex.Message, icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
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
                if (!await w.ShowDialog<bool>(this))
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

                await MessageBoxManager.GetMessageBoxStandardWindow("Done", $"{count} item(s) renamed").ShowDialog(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Error", ex.Message, icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ButtonDiscImageOptions_Click(object sender, RoutedEventArgs e)
        {
            var window = new DiscImageOptionsWindow(SaveDiscImageOptionsConfig);
            window.DataContext = this;
            await window.ShowDialog(this);
        }

        private async void ButtonDatTools_Click(object sender, RoutedEventArgs e)
        {
            var window = new DatToolsWindow(Manager, async () => await LoadItemsFromCard());
            await window.ShowDialog(this);
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
                await MessageBoxManager.GetMessageBoxStandardWindow("Error", ex.Message, icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
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
            var folderDialog = new OpenFolderDialog { Title = "Select SD Card Folder" };
            var selectedPath = await folderDialog.ShowAsync(this);

            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Check if it looks like a GDEMU SD card
                bool hasGdemuIni = File.Exists(Path.Combine(selectedPath, Constants.MenuConfigTextFile));
                bool has01Folder = Directory.Exists(Path.Combine(selectedPath, "01"));

                if (!hasGdemuIni && !has01Folder)
                {
                    await MessageBoxManager.GetMessageBoxStandardWindow(
                        "Notice",
                        "The selected folder does not appear to be a GDEMU SD card.\n\n" +
                        "No GDEMU.INI file or numbered folders (01, 02, etc.) were found.\n\n" +
                        "You may proceed, but the folder may not work as expected.",
                        MessageBox.Avalonia.Enums.ButtonEnum.Ok,
                        MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
                }

                // Set the custom path
                CustomSdPath = selectedPath;
                Manager.sdPath = selectedPath;
                SelectedDrive = null; // Clear drive selection

                // Load items from the custom path
                await LoadItemsFromCard();
            }
        }

        private void FillDriveList(bool isRefreshing = false)
        {
            DriveInfo[] list;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || (x.DriveType == DriveType.Removable && x.DriveFormat.StartsWith("FAT")))).ToArray();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                //list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || x.DriveType == DriveType.Removable || x.DriveType == DriveType.Fixed)).ToArray();//todo need to test
                list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || x.DriveType == DriveType.Removable || x.DriveType == DriveType.Fixed || (x.DriveType == DriveType.Unknown && x.DriveFormat.Equals("lifs", StringComparison.InvariantCultureIgnoreCase)))).ToArray();//todo need to test
            else//linux
                list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || ((x.DriveType == DriveType.Removable || x.DriveType == DriveType.Fixed) && x.DriveFormat.Equals("msdos", StringComparison.InvariantCultureIgnoreCase) && (x.Name.StartsWith("/media/", StringComparison.InvariantCultureIgnoreCase) || x.Name.StartsWith("/run/media/", StringComparison.InvariantCultureIgnoreCase))))).ToArray();


            if (isRefreshing)
            {
                if (DriveList.Select(x => x.Name).SequenceEqual(list.Select(x => x.Name)))
                    return;

                DriveList.Clear();
            }
            //fill drive list and try to find drive with gdemu contents
            //look for GDEMU.INI file
            foreach (DriveInfo drive in list)
            {
                try
                {
                    DriveList.Add(drive);
                    if (SelectedDrive == null && File.Exists(Path.Combine(drive.RootDirectory.FullName, Constants.MenuConfigTextFile)))
                        SelectedDrive = drive;
                }
                catch { }
            }

            //look for 01 folder
            if (SelectedDrive == null)
            {
                foreach (DriveInfo drive in list)
                {
                    try
                    {
                        if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "01")))
                        {
                            SelectedDrive = drive;
                            break;
                        }
                    }
                    catch { }
                }
            }

            //look for /media mount
            if (SelectedDrive == null)
            {
                foreach (DriveInfo drive in list)
                {
                    try
                    {
                        if (drive.Name.StartsWith("/media/", StringComparison.InvariantCultureIgnoreCase))
                        {
                            SelectedDrive = drive;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (!DriveList.Any())
                return;

            if (SelectedDrive == null)
                SelectedDrive = DriveList.LastOrDefault();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update context menu headers based on selection
            if (dg1.TryFindResource("rowmenu", out var resource) && resource is ContextMenu menu)
            {
                int count = dg1.SelectedItems.Count;
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

                // Disable context menu for menu entry (folder 01) by setting all items disabled
                // Note: In Avalonia, we can't easily prevent context menu from showing,
                // but individual handlers already filter out SdNumber == 1
            }
        }

        private async void MenuItemRename_Click(object sender, RoutedEventArgs e)
        {
            var menuitem = (MenuItem)sender;
            var item = (GdItem)menuitem.CommandParameter;

            // Protect menu entry (folder 01) from renaming
            if (item?.SdNumber == 1)
                return;

            var oldName = item.Name;

            var result = await MessageBoxManager.GetMessageBoxInputWindow(new MessageBox.Avalonia.DTO.MessageBoxInputParams
            {
                ContentTitle = "Rename",
                ContentHeader = "inform new name",
                ContentMessage = "Name",
                WatermarkText = item.Name,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[] { new ButtonDefinition { Name = "Ok" }, new ButtonDefinition { Name = "Cancel" } },
            }).ShowDialog(this);

            if (result?.Button == "Ok" && !string.IsNullOrWhiteSpace(result.Message))
            {
                var newName = result.Message.Trim();
                if (newName != oldName)
                {
                    item.Name = newName;
                    Manager.UndoManager.RecordChange(new PropertyEditOperation
                    {
                        Item = item,
                        PropertyName = nameof(GdItem.Name),
                        OldValue = oldName,
                        NewValue = newName
                    });
                }
            }
        }

        private void MenuItemRenameSentence_Click(object sender, RoutedEventArgs e)
        {
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
                await MessageBoxManager.GetMessageBoxStandardWindow("Error", ex.Message, icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            }
            IsBusy = false;
        }

        private async void MenuItemAssignFolder_Click(object sender, RoutedEventArgs e)
        {
            // Commit any pending cell edits
            dg1.CommitEdit();

            // Only allow in openMenu mode
            if (MenuKindSelected != MenuKind.openMenu)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Info", "Assign Folder Path is only available in openMenu mode.", icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
                return;
            }

            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToList();

            // Filter out menu items
            selectedItems = selectedItems.Where(item =>
                item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu").ToList();

            if (selectedItems.Count == 0)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Info", "No valid items selected.", icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
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
            var result = await dialog.ShowDialog<bool?>(this);

            if (result == true)
            {
                var folderPath = dialog.FolderPath?.Trim() ?? string.Empty;

                // check if the new primary folder conflicts with any item's alt folders
                if (!string.IsNullOrEmpty(folderPath))
                {
                    var conflicting = selectedItems.Where(item =>
                        item.AlternativeFolders.Contains(folderPath)).ToList();
                    if (conflicting.Count > 0)
                    {
                        await MessageBoxManager.GetMessageBoxStandardWindow("Duplicate Folder Path",
                            "This folder path is already assigned to this disc image as an additional folder path.",
                            icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
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

        private async void MenuItemAssignAltFolders_Click(object sender, RoutedEventArgs e)
        {
            dg1.CommitEdit();

            if (MenuKindSelected != MenuKind.openMenu)
            {
                await MessageBoxManager.GetMessageBoxStandardWindow("Info",
                    "Additional folder paths are only available in openMenu mode.",
                    icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
                return;
            }

            var item = dg1.SelectedItems.Cast<GdItem>()
                .FirstOrDefault(x => x.SdNumber != 1);

            if (item == null)
                return;

            Manager.InitializeKnownFolders();
            var dlg = new AssignAltFoldersWindow(item, Manager.KnownFolders);
            var dlgResult = await dlg.ShowDialog<bool?>(this);

            if (dlgResult == true)
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

        //private void rename(GdItem item, short index)
        //{
        //    string name;

        //    if (index == 0)//ip.bin
        //    {
        //        name = item.Ip.Name;
        //    }
        //    else
        //    {
        //        if (index == 1)//folder
        //            name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
        //        else//file
        //            name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();
        //        var m = RegularExpressions.TosecnNameRegexp.Match(name);
        //        if (m.Success)
        //            name = name.Substring(0, m.Index);
        //    }
        //    item.Name = name;
        //}

        //private void rename(object sender, short index)
        //{
        //    var menuItem = (MenuItem)sender;
        //    var item = (GdItem)menuItem.CommandParameter;

        //    string name;

        //    if (index == 0)//ip.bin
        //    {
        //        name = item.Ip.Name;
        //    }
        //    else
        //    {
        //        if (index == 1)//folder
        //            name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
        //        else//file
        //            name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();
        //        var m = RegularExpressions.TosecnNameRegexp.Match(name);
        //        if (m.Success)
        //            name = name.Substring(0, m.Index);
        //    }
        //    item.Name = name;
        //}

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Handle Ctrl+Z for Undo
            if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
            {
                if (Manager.UndoManager.CanUndo)
                {
                    Manager.UndoManager.Undo();
                    e.Handled = true;
                }
            }
            // Handle Ctrl+Y for Redo
            else if (e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control)
            {
                if (Manager.UndoManager.CanRedo)
                {
                    Manager.UndoManager.Redo();
                    e.Handled = true;
                }
            }
        }

        private async void GridOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && !(e.Source is TextBox))
            {
                List<GdItem> toRemove = new List<GdItem>();
                foreach (GdItem item in dg1.SelectedItems)
                {
                    if (item.SdNumber == 1)
                    {
                        if (item.Ip == null)
                        {
                            IsBusy = true;
                            await Manager.LoadIP(item);
                            IsBusy = false;
                        }
                        if (item.Ip.Name != "GDMENU" && item.Ip.Name != "openMenu")//dont let the user exclude GDMENU, openMenu
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
                        var filteredItems = Manager.ItemList.Where(item => FilterInItem(item, _activeFilterText)).ToList();
                        if (filteredItems.Count == 0)
                        {
                            await MessageBoxManager.GetMessageBoxStandardWindow("Filter",
                                "Nothing to show for the currently applied filter.",
                                icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
                            ClearFilterFromGrid();
                        }
                        else
                        {
                            dg1.Items = filteredItems;
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
            var fileDialog = new OpenFileDialog
            {
                Title = "Select File(s)",
                AllowMultiple = true,
                Filters = fileFilterList
            };

            var files = await fileDialog.ShowAsync(this);
            if (files != null && files.Any())
            {
                IsBusy = true;

                var invalid = await Manager.AddGames(files);

                if (invalid.Any())
                    await MessageBoxManager.GetMessageBoxStandardWindow("Ignored folders/files", string.Join(Environment.NewLine, invalid), icon: MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);

                // Show serial translation dialog if any items were translated
                await ShowSerialTranslationDialogIfNeeded();

                IsBusy = false;
            }
        }

        private async void ButtonRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToArray();
            if (selectedItems.Length == 0)
                return;

            // Collect items and indices before removal for undo
            var undoOp = new MultiItemRemoveOperation { ItemList = Manager.ItemList };
            foreach (var item in selectedItems)
            {
                undoOp.Items.Add((item, Manager.ItemList.IndexOf(item)));
            }

            foreach (var item in selectedItems)
                Manager.ItemList.Remove(item);

            Manager.UndoManager.RecordChange(undoOp);

            if (IsFilterActive)
            {
                var filteredItems = Manager.ItemList.Where(item => FilterInItem(item, _activeFilterText)).ToList();
                if (filteredItems.Count == 0)
                {
                    await MessageBoxManager.GetMessageBoxStandardWindow("Filter",
                        "Nothing to show for the currently applied filter.",
                        icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
                    ClearFilterFromGrid();
                }
                else
                {
                    dg1.Items = filteredItems;
                }
            }
        }

        private void ButtonMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (IsFilterActive)
                return;
            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToArray();

            if (!selectedItems.Any())
                return;

            // Don't allow moving menu items
            if (selectedItems.Any(item => item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu"))
                return;

            int moveTo = Manager.ItemList.IndexOf(selectedItems.First()) - 1;

            // Don't allow moving items above the menu (position 0)
            if (moveTo < 1)
                return;

            // Capture order before move for undo
            var oldOrder = new List<GdItem>(Manager.ItemList);

            foreach (var item in selectedItems)
                Manager.ItemList.Remove(item);

            foreach (var item in selectedItems)
                Manager.ItemList.Insert(moveTo++, item);

            // Record undo operation
            Manager.UndoManager.RecordChange(new ListReorderOperation("Move Up")
            {
                ItemList = Manager.ItemList,
                OldOrder = oldOrder,
                NewOrder = new List<GdItem>(Manager.ItemList)
            });

            dg1.SelectedItems.Clear();
            foreach (var item in selectedItems)
                dg1.SelectedItems.Add(item);
        }

        private void ButtonMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (IsFilterActive)
                return;
            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToArray();

            if (!selectedItems.Any())
                return;

            // Don't allow moving menu items
            if (selectedItems.Any(item => item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu"))
                return;

            int moveTo = Manager.ItemList.IndexOf(selectedItems.Last()) - selectedItems.Length + 2;

            if (moveTo > Manager.ItemList.Count - selectedItems.Length)
                return;

            // Capture order before move for undo
            var oldOrder = new List<GdItem>(Manager.ItemList);

            foreach (var item in selectedItems)
                Manager.ItemList.Remove(item);

            foreach (var item in selectedItems)
                Manager.ItemList.Insert(moveTo++, item);

            // Record undo operation
            Manager.UndoManager.RecordChange(new ListReorderOperation("Move Down")
            {
                ItemList = Manager.ItemList,
                OldOrder = oldOrder,
                NewOrder = new List<GdItem>(Manager.ItemList)
            });

            dg1.SelectedItems.Clear();
            foreach (var item in selectedItems)
                dg1.SelectedItems.Add(item);
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
                    await MessageBoxManager.GetMessageBoxStandardWindow("Search", "No matches found.",
                        icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
            }
        }

        private bool searchInGrid(int start)
        {
            var visibleItems = (dg1.Items as System.Collections.IEnumerable)?.Cast<GdItem>().ToList()
                               ?? Manager.ItemList.ToList();

            for (int i = start; i < visibleItems.Count; i++)
            {
                var item = visibleItems[i];
                if (dg1.SelectedItem != item && Manager.SearchInItem(item, Filter))
                {
                    dg1.SelectedItem = item;
                    dg1.ScrollIntoView(item, null);
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

            var filteredItems = Manager.ItemList.Where(item => FilterInItem(item, filterText)).ToList();
            dg1.Items = filteredItems;

            DragDrop.SetAllowDrop(this, false);
        }

        private void ClearFilterFromGrid()
        {
            dg1.Items = Manager.ItemList;

            _activeFilterText = null;
            Filter = null;
            IsFilterActive = false;

            DragDrop.SetAllowDrop(this, !IsBusy);
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
                await MessageBoxManager.GetMessageBoxStandardWindow("Filter", "No matches found.",
                    icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
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


    }
}
