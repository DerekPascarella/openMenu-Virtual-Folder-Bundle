using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MsBox.Avalonia;
using MsBox.Avalonia.Models;
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
            set { _Filter = value; RaisePropertyChanged(); UpdateSearchMatches(); }
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

        private bool _HaveGDIShrinkBlacklist;
        public bool HaveGDIShrinkBlacklist
        {
            get { return _HaveGDIShrinkBlacklist; }
            set { _HaveGDIShrinkBlacklist = value; RaisePropertyChanged(); }
        }

        public bool EnableGDIShrink
        {
            get { return Manager.EnableGDIShrink; }
            set { Manager.EnableGDIShrink = value; RaisePropertyChanged(); RaiseShrinkSubOptions(); }
        }

        public bool EnableGDIShrinkExisting
        {
            get { return Manager.EnableGDIShrinkExisting; }
            set { Manager.EnableGDIShrinkExisting = value; RaisePropertyChanged(); RaiseShrinkSubOptions(); }
        }

        public bool EnableGDIShrinkCompressed
        {
            get { return Manager.EnableGDIShrinkCompressed; }
            set { Manager.EnableGDIShrinkCompressed = value; RaisePropertyChanged(); RaiseShrinkSubOptions(); }
        }

        public bool EnableGDIShrinkBlackList
        {
            get { return Manager.EnableGDIShrinkBlackList; }
            set { Manager.EnableGDIShrinkBlackList = value; RaisePropertyChanged(); RaiseShrinkSubOptions(); }
        }

        // The sub options read as unchecked while the option they depend on is
        // off, and write only their own stored value.
        public bool ShrinkCompressedChecked
        {
            get { return Manager.EnableGDIShrinkCompressed && Manager.EnableGDIShrink; }
            set { EnableGDIShrinkCompressed = value; }
        }

        public bool ShrinkBlacklistChecked
        {
            get { return Manager.EnableGDIShrinkBlackList && ShrinkBlacklistEnabled; }
            set { EnableGDIShrinkBlackList = value; }
        }

        public bool ShrinkBlacklistEnabled
        {
            get { return Manager.EnableGDIShrink || Manager.EnableGDIShrinkExisting; }
        }

        private void RaiseShrinkSubOptions()
        {
            RaisePropertyChanged(nameof(ShrinkCompressedChecked));
            RaisePropertyChanged(nameof(ShrinkBlacklistChecked));
            RaisePropertyChanged(nameof(ShrinkBlacklistEnabled));
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
            set { Manager.EnableLockCheck = value; RaisePropertyChanged(); SaveLockCheckConfig(); }
        }

        private readonly List<FilePickerFileType> fileFilterList;


        #region window controls
        DataGrid dg1;
        Border DropLine;
        Button ButtonSort;

        // Where a drop will land, worked out while the drag hovers and used when it lands.
        // -1 means we have not settled on a spot yet.
        private int _pendingDropIndex = -1;

        // Row reorder drag state. The dragged items ride in _rowDragItems since source
        // and target are the same window, the marker format is there because macOS
        // refuses a drag that declares no types at all.
        private static readonly DataFormat<byte[]> RowDragFormat =
            DataFormat.CreateBytesApplicationFormat("gdmcm-games-row-drag");
        private Avalonia.Input.PointerPressedEventArgs _rowDragTrigger;
        private Point _rowDragStartPoint;
        private GdItem _rowDragPressedItem;
        private List<GdItem> _rowDragItems;
        #endregion

        // Undo tracking for cell edits
        private GdItem _editingItem;
        private string _editingPropertyName;
        private object _editingOldValue;
        private bool _editingOldTitleWasUserEdited;
        private ArchiveMetadataField? _editingArchiveMetadataField;
        private ArchiveMetadataFieldState _editingArchiveMetadataOldState;
        private ArchiveMetadataFieldState _editingArchiveRegionOldState;

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
            fileFilterList = new List<FilePickerFileType>
            {
                new FilePickerFileType($"Dreamcast Game ({string.Join("; ", fullList.Select(x => $"*{x}"))})")
                {
                    Patterns = fullList.Select(x => $"*{x}").ToList()
                }
            };

            // Clean up any leftover staging data from a previous update attempt
            UpdateManager.CleanupStaleStagingData();

            this.Opened += async (ss, ee) =>
            {
                await CheckConfigWritability();

                // On macOS, copy BOX.DAT, ICON.DAT, META.DAT from the bundle into
                // ~/Library/Application Support/GDMENUCardManager/menu_data/ before anything loads.
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

            // Config parsing. All settings are optional and must reverse to default values if missing.
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
            HaveGDIShrinkBlacklist = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.GdiShrinkBlacklistFile));
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
            this.AddHandler(DragDrop.DragOverEvent, WindowDragOver);
            this.AddHandler(DragDrop.DragLeaveEvent, WindowDragLeave);
            dg1 = this.FindControl<DataGrid>("dg1");
            DropLine = this.FindControl<Border>("DropLine");
            ButtonSort = this.FindControl<Button>("ButtonSort");

            // Add tunneling handler to intercept right-clicks before context menu opens
            dg1.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, DataGrid_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            dg1.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, DataGrid_PointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            dg1.PointerMoved += DataGrid_PointerMoved;
        }

        // Track if we should block context menu for current right-click
        private bool _blockContextMenu = false;

        private void DataGrid_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            _blockContextMenu = false;

            // A left press on a row may turn into a reorder drag, remember it until the
            // pointer has moved far enough to count as one.
            if (e.GetCurrentPoint(dg1).Properties.IsLeftButtonPressed)
            {
                var pressSource = e.Source as Avalonia.Controls.Control;
                while (pressSource != null)
                {
                    if (pressSource is Avalonia.Controls.DataGridRow pressRow)
                    {
                        _rowDragPressedItem = pressRow.DataContext as GdItem;
                        _rowDragTrigger = _rowDragPressedItem != null ? e : null;
                        _rowDragStartPoint = e.GetPosition(this);
                        break;
                    }
                    pressSource = pressSource.Parent as Avalonia.Controls.Control;
                }
                return;
            }

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

            // Block context menu on menu entry (folder 01).
            if (clickedItem?.SdNumber == 1)
            {
                _blockContextMenu = true;
                e.Handled = true;
                return;
            }

            // Clicking an already-selected row keeps the multi-selection. Anything else
            // collapses it to one.
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
            // Released without crossing the drag threshold, so no reorder drag.
            _rowDragTrigger = null;
            _rowDragPressedItem = null;

            // Block context menu for menu entry (folder 01) on pointer release too
            if (_blockContextMenu && e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right)
            {
                e.Handled = true;
                _blockContextMenu = false;
            }
        }

        private async void DataGrid_PointerMoved(object sender, Avalonia.Input.PointerEventArgs e)
        {
            if (_rowDragTrigger == null || _rowDragItems != null)
                return;

            if (!e.GetCurrentPoint(dg1).Properties.IsLeftButtonPressed)
            {
                _rowDragTrigger = null;
                _rowDragPressedItem = null;
                return;
            }

            if (IsBusy || IsFilterActive || Manager.sdPath == null || _editingItem != null)
                return;

            var current = e.GetPosition(this);
            if (Math.Abs(current.X - _rowDragStartPoint.X) < 4 &&
                Math.Abs(current.Y - _rowDragStartPoint.Y) < 4)
                return;

            // Dragging a row that is part of the selection moves the whole selection.
            var items = new List<GdItem>();
            if (dg1.SelectedItems != null && dg1.SelectedItems.Contains(_rowDragPressedItem) && dg1.SelectedItems.Count > 1)
                items.AddRange(dg1.SelectedItems.OfType<GdItem>().OrderBy(x => Manager.ItemList.IndexOf(x)));
            else
                items.Add(_rowDragPressedItem);

            // The menu entry stays in slot 0, it does not get dragged.
            if (items.Count == 0 || items.Any(IsMenuEntry))
            {
                _rowDragTrigger = null;
                _rowDragPressedItem = null;
                return;
            }

            var trigger = _rowDragTrigger;
            _rowDragTrigger = null;
            _rowDragPressedItem = null;
            _rowDragItems = items;


            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(RowDragFormat, new byte[] { 1 }));

            try
            {
                await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Move);
            }
            catch (Exception)
            {
                // A failed platform drag just cancels the move.
            }

            _rowDragItems = null;
            _pendingDropIndex = -1;
            HideDropLine();
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
                else if (col.Header?.ToString() == "Artwork")
                    artColumn = col;
            }

            if (folderColumn != null)
            {
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    folderColumn.IsVisible = true;
                    // Setting the same star width back does nothing when the column was
                    // hidden, so bounce it through Auto first to force the widths to redo.
                    folderColumn.Width = DataGridLength.Auto;
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

            // Disc column read-only is handled in BeginningEdit (template column)
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
            if (e.Row?.DataContext is GdItem item)
            {
                bool isMenuItem = item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu";

                if (isMenuItem)
                {
                    e.Cancel = true;
                    ClearEditingCapture();
                    return;
                }

                string header = e.Column.Header?.ToString();
                _editingArchiveMetadataField = null;
                if (item.FileFormat == FileFormat.SevenZip &&
                    TryGetArchiveMetadataField(header, out var archiveField))
                {
                    if (!ArchiveMetadataEditPolicy.CanEdit(
                        item,
                        archiveField,
                        MenuKindSelected))
                    {
                        e.Cancel = true;
                        ClearEditingCapture();
                        return;
                    }

                    _editingArchiveMetadataField = archiveField;
                    _editingArchiveMetadataOldState =
                        item.CaptureArchiveMetadataFieldState(archiveField);
                    if (archiveField == ArchiveMetadataField.Type)
                    {
                        _editingArchiveRegionOldState =
                            item.CaptureArchiveMetadataFieldState(
                                ArchiveMetadataField.Region);
                    }
                }
                else if (header == "Region" && !CanEditRegion(item))
                {
                    e.Cancel = true;
                    ClearEditingCapture();
                    return;
                }
                else if (header == "Disc" && MenuKindSelected != MenuKind.openMenu)
                {
                    e.Cancel = true;
                    ClearEditingCapture();
                    return;
                }

                _editingItem = item;
                _editingOldTitleWasUserEdited = false;
                var column = e.Column;
                if (column.Header?.ToString() == "Title")
                {
                    _editingPropertyName = nameof(GdItem.Name);
                    _editingOldValue = item.Name;
                    _editingOldTitleWasUserEdited = item.HasUserEditedCompressedTitle;
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
                else if (column.Header?.ToString() == "Region")
                {
                    _editingPropertyName = nameof(GdItem.Region);
                    _editingOldValue = item.Region;
                }
                else
                {
                    ClearEditingCapture();
                }
            }
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel)
            {
                ClearEditingCapture();
                return;
            }

            if (_editingItem == null || _editingPropertyName == null)
                return;

            // Capture values in local variables
            var item = _editingItem;
            var propertyName = _editingPropertyName;
            var oldValue = _editingOldValue;
            var archiveField = _editingArchiveMetadataField;
            var archiveOldState = _editingArchiveMetadataOldState;
            var archiveRegionOldState = _editingArchiveRegionOldState;

            // Read the editing element directly, since the binding may not have updated yet.
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
            else if (e.EditingElement is Visual visual)
            {
                var innerComboBox = visual.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .FirstOrDefault();
                if (innerComboBox != null)
                {
                    newValue = innerComboBox.SelectedItem;
                }
                else
                {
                    var innerTextBox = visual.GetVisualDescendants()
                        .OfType<TextBox>()
                        .FirstOrDefault();
                    if (innerTextBox != null)
                        newValue = innerTextBox.Text;
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
                else
                    SetEditingControlValue(e.EditingElement, revertValue);
                // Revert the property in case binding already updated it
                RevertProperty(item, propertyName, oldValue);
                e.Cancel = true;
                // Keep editing state so the next commit attempt can validate.
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await MessageBoxManager.GetMessageBoxStandard("Information",
                        "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                        icon: MsBox.Avalonia.Enums.Icon.Warning).ShowWindowDialogAsync(this);
                });
                return;
            }

            bool oldTitleState = _editingOldTitleWasUserEdited;

            if (propertyName == nameof(GdItem.Name))
            {
                ClearEditingCapture();

                if (newValue is string requestedTitle &&
                    item.CommitUserTitle(oldValue as string, requestedTitle))
                {
                    SetEditingTextBoxText(e.EditingElement, item.Name);
                    var operation = new TitleEditOperation("Edit Title");
                    operation.Add(item, oldValue as string, oldTitleState);
                    Manager.UndoManager.RecordChange(operation);
                }

                return;
            }

            if (archiveField.HasValue)
            {
                ClearEditingCapture();
                string requested = newValue as string;
                if (archiveField.Value == ArchiveMetadataField.Region)
                {
                    requested = GdItem.NormalizeRegion(requested);
                    if (requested == null)
                    {
                        SetEditingControlValue(e.EditingElement, archiveOldState.Value);
                        return;
                    }
                }

                if (!item.CommitUserArchiveMetadata(archiveField.Value, requested))
                {
                    SetEditingControlValue(e.EditingElement, archiveOldState.Value);
                    return;
                }

                var archiveNewState =
                    item.CaptureArchiveMetadataFieldState(archiveField.Value);
                SetEditingControlValue(e.EditingElement, archiveNewState.Value);
                string operationDescription = "Edit " + e.Column.Header;
                if (archiveField.Value == ArchiveMetadataField.Serial)
                {
                    QueueArchiveSerialTranslationOperation(
                        item,
                        archiveOldState,
                        operationDescription);
                    return;
                }

                var operation = new ArchiveMetadataEditOperation(
                    operationDescription);
                operation.Add(
                    item,
                    archiveField.Value,
                    archiveOldState,
                    archiveNewState);

                if (archiveField.Value == ArchiveMetadataField.Type &&
                    item.DiscType != "Game" &&
                    item.CommitUserArchiveMetadata(
                        ArchiveMetadataField.Region,
                        null))
                {
                    operation.Add(
                        item,
                        ArchiveMetadataField.Region,
                        archiveRegionOldState,
                        item.CaptureArchiveMetadataFieldState(
                            ArchiveMetadataField.Region));
                }

                Manager.UndoManager.RecordChange(operation);
                return;
            }

            if (propertyName == nameof(GdItem.Region))
            {
                ClearEditingCapture();

                var oldRegion = oldValue as string;
                var normalized = GdItem.NormalizeRegion(newValue as string);

                if (normalized == null || normalized == oldRegion)
                {
                    // Invalid or unchanged input, silently put the old value back.
                    SetEditingTextBoxText(e.EditingElement, oldRegion ?? "");
                    item.Region = oldRegion;
                    return;
                }

                // push the normalized value so the binding commits it (e.g., "ej" becomes "JE")
                SetEditingTextBoxText(e.EditingElement, normalized);

                // No undo entry if the previous value wasn't a usable region.
                if (oldRegion != null && GdItem.NormalizeRegion(oldRegion) == oldRegion)
                {
                    Manager.UndoManager.RecordChange(new PropertyEditOperation
                    {
                        Item = item,
                        PropertyName = nameof(GdItem.Region),
                        OldValue = oldRegion,
                        NewValue = normalized
                    });
                }

                // Image gets patched to match on save.
                item.Region = normalized;
                return;
            }

            ClearEditingCapture();

            // Only record if we got a new value and it's different from old
            if (newValue != null && !Equals(oldValue, newValue))
            {
                // Check if Folder edit conflicts with an alt folder.
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
                            await MessageBoxManager.GetMessageBoxStandard("Information",
                                "This folder path is already assigned to this disc image as an additional folder path.",
                                icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
                        });
                        return;
                    }
                }

                object committedValue = newValue;
                if (propertyName == nameof(GdItem.ProductNumber))
                {
                    item.ProductNumber = newValue as string;
                    committedValue = item.ProductNumber;
                }
                else if (propertyName == nameof(GdItem.DiscType))
                {
                    item.DiscType = newValue as string;
                    committedValue = item.DiscType;
                }
                else if (propertyName == nameof(GdItem.Disc))
                {
                    item.Disc = newValue as string;
                    committedValue = item.Disc;
                }

                Manager.UndoManager.RecordChange(new PropertyEditOperation
                {
                    Item = item,
                    PropertyName = propertyName,
                    OldValue = oldValue,
                    NewValue = committedValue
                });

                // If Serial column was edited, check for translation after binding updates
                if (propertyName == nameof(GdItem.ProductNumber))
                    QueueSerialTranslationDialog(item);
            }
        }

        private static bool TryGetArchiveMetadataField(
            string header,
            out ArchiveMetadataField field)
        {
            field = header switch
            {
                "Serial" => ArchiveMetadataField.Serial,
                "Type" => ArchiveMetadataField.Type,
                "Disc" => ArchiveMetadataField.Disc,
                "Region" => ArchiveMetadataField.Region,
                _ => ArchiveMetadataField.None
            };
            return field != ArchiveMetadataField.None;
        }

        private void ClearEditingCapture()
        {
            _editingItem = null;
            _editingPropertyName = null;
            _editingOldValue = null;
            _editingOldTitleWasUserEdited = false;
            _editingArchiveMetadataField = null;
        }

        private void QueueSerialTranslationDialog(GdItem item)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (!_handlingSerialTranslation && item.WasSerialTranslated)
                {
                    await Helper.DependencyManager.ShowSerialTranslationDialog(
                        new[] { item });
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private void QueueArchiveSerialTranslationOperation(
            GdItem item,
            ArchiveMetadataFieldState oldState,
            string description)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (!_handlingSerialTranslation && item.WasSerialTranslated)
                {
                    await Helper.DependencyManager.ShowSerialTranslationDialog(
                        new[] { item });
                }

                var operation = new ArchiveMetadataEditOperation(description);
                operation.Add(
                    item,
                    ArchiveMetadataField.Serial,
                    oldState,
                    item.CaptureArchiveMetadataFieldState(
                        ArchiveMetadataField.Serial));
                Manager.UndoManager.RecordChange(operation);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private static void SetEditingControlValue(
            object editingElement,
            string value)
        {
            if (editingElement is ComboBox comboBox)
                comboBox.SelectedItem = value;
            else if (editingElement is TextBox textBox)
                textBox.Text = value ?? "";
            else if (editingElement is Visual visual)
            {
                var innerCombo = visual.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .FirstOrDefault();
                if (innerCombo != null)
                    innerCombo.SelectedItem = value;
                else
                {
                    var innerText = visual.GetVisualDescendants()
                        .OfType<TextBox>()
                        .FirstOrDefault();
                    if (innerText != null)
                        innerText.Text = value ?? "";
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
                case nameof(GdItem.Region):
                    item.Region = oldValue as string;
                    break;
            }
        }

        // Editing elements from template columns can be a bare TextBox or one wrapped in a panel
        private static void SetEditingTextBoxText(object editingElement, string text)
        {
            if (editingElement is TextBox tb)
                tb.Text = text;
            else if (editingElement is Panel panel)
            {
                var innerTb = panel.Children.OfType<TextBox>().FirstOrDefault();
                if (innerTb != null)
                    innerTb.Text = text;
            }
        }

        private static bool CanEditRegion(GdItem item)
        {
            return item.FileFormat == FileFormat.Uncompressed
                && item.DiscType == "Game"
                && item.Ip != null
                && RegionPatcher.CanPatch(item.ImageFile);
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
            UpdateSearchMatches();

            // If filter is active, refresh the filtered view (e.g., after undo re-inserts items)
            if (IsFilterActive && _activeFilterText != null)
            {
                var filteredItems = Manager.ItemList.Where(item => FilterInItem(item, _activeFilterText)).ToList();
                if (filteredItems.Count == 0)
                    ClearFilterFromGrid();
                else
                    dg1.ItemsSource = filteredItems;
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
                        // Quit.
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
                await MessageBoxManager.GetMessageBoxStandard("Information", $"Problem loading the following folder(s):\n\n{ex.Message}", icon: MsBox.Avalonia.Enums.Icon.Warning).ShowWindowDialogAsync(this);
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
            progressWindow.IsIndeterminate = false;
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
                        var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                        {
                            ContentTitle = "Confirmation",
                            ContentMessage = "BOX.DAT and ICON.DAT were not found in the expected location.\n\n" +
                                "These files are required for artwork display in openMenu.\n\n" +
                                "Click Create to create empty DAT files.\n\n" +
                                "Click Close to close and add files manually.\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MsBox.Avalonia.Enums.Icon.Warning,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Create" },
                                new ButtonDefinition { Name = "Close" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowWindowDialogAsync(this);

                        if (result == "Create")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.CreateEmptyDatFiles();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to create DAT files: {error}", icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
                        var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                        {
                            ContentTitle = "Confirmation",
                            ContentMessage = "BOX.DAT was not found but ICON.DAT exists.\n\n" +
                                "BOX.DAT is required for artwork management.\n\n" +
                                "Click Create to create an empty BOX.DAT file.\n\n" +
                                "Click Close to close and add BOX.DAT manually.\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MsBox.Avalonia.Enums.Icon.Warning,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Create" },
                                new ButtonDefinition { Name = "Close" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowWindowDialogAsync(this);

                        if (result == "Create")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.CreateEmptyBoxDat();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to create BOX.DAT: {error}", icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
                        var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                        {
                            ContentTitle = "Confirmation",
                            ContentMessage = "ICON.DAT was not found but BOX.DAT exists.\n\n" +
                                "ICON.DAT can be generated from BOX.DAT by downscaling the artwork.\n\n" +
                                "Click Generate to generate ICON.DAT from BOX.DAT (recommended).\n\n" +
                                "Click Close to close and add ICON.DAT manually.\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MsBox.Avalonia.Enums.Icon.Question,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Generate" },
                                new ButtonDefinition { Name = "Close" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowWindowDialogAsync(this);

                        if (result == "Generate")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) { Manager.ArtworkDisabled = true; break; }
                            var (success, error) = Manager.GenerateIconDatFromBox();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to generate ICON.DAT: {error}", icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
                        var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                        {
                            ContentTitle = "Confirmation",
                            ContentMessage = "ICON.DAT entries don't match BOX.DAT entries.\n\n" +
                                "This can happen if the files were modified independently.\n\n" +
                                "Click Regenerate to regenerate ICON.DAT from BOX.DAT (recommended).\n\n" +
                                "Click Proceed to proceed with mismatched files (some icons may be missing).\n\n" +
                                "Click Skip to proceed without artwork features.",
                            Icon = MsBox.Avalonia.Enums.Icon.Warning,
                            ShowInCenter = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ButtonDefinitions = new ButtonDefinition[]
                            {
                                new ButtonDefinition { Name = "Regenerate" },
                                new ButtonDefinition { Name = "Proceed" },
                                new ButtonDefinition { Name = "Skip" }
                            }
                        }).ShowWindowDialogAsync(this);

                        if (result == "Regenerate")
                        {
                            if (!await Manager.EnsureDatFilesWritable()) break;
                            var (success, error) = Manager.GenerateIconDatFromBox();
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to regenerate ICON.DAT: {error}", icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
                    var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                    {
                        ContentTitle = "Confirmation",
                        ContentMessage = "One or more disc images that are part of multi-disc sets do not have a required Serial value assigned to them, which will break their display in openMenu.\n\nDo you want to proceed and ignore the disc numbers and counts, or return to make edits?",
                        Icon = MsBox.Avalonia.Enums.Icon.Warning,
                        ShowInCenter = true,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ButtonDefinitions = new ButtonDefinition[]
                        {
                            new ButtonDefinition { Name = "Return" },
                            new ButtonDefinition { Name = "Proceed" }
                        }
                    }).ShowWindowDialogAsync(this);

                    if (result == "Return")
                    {
                        IsBusy = false;
                        return;
                    }

                    // Reset disc to 1/1 for items without serial.
                    ResetDiscValuesForItemsWithoutSerial();
                }

                // Check for multi-disc sets exceeding 10 discs (openMenu only)
                if (MenuKindSelected == MenuKind.openMenu && HasMultiDiscSetsExceeding10())
                {
                    var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                    {
                        ContentTitle = "Confirmation",
                        ContentMessage = "One or more multi-disc set exceeds 10 discs total, the maximum supported by openMenu.\n\nDo you want to proceed or return to make edits?",
                        Icon = MsBox.Avalonia.Enums.Icon.Warning,
                        ShowInCenter = true,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ButtonDefinitions = new ButtonDefinition[]
                        {
                            new ButtonDefinition { Name = "Return" },
                            new ButtonDefinition { Name = "Proceed" }
                        }
                    }).ShowWindowDialogAsync(this);

                    if (result == "Return")
                    {
                        IsBusy = false;
                        return;
                    }
                }

                if (await Manager.Save(TempFolder))
                {
                    await MessageBoxManager.GetMessageBoxStandard("Information", "Done!").ShowWindowDialogAsync(this);
                }
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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

                // No RestoreBounds available, so save the current bounds.
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
            HideDropLine();

            int pending = _pendingDropIndex;
            _pendingDropIndex = -1;

            if (IsFilterActive)
                return;
            if (Manager.sdPath == null)
                return;

            if (_rowDragItems != null && e.DataTransfer.Contains(RowDragFormat))
            {
                try
                {
                    // Reorder drop. Remove the dragged rows, walking the target index back.
                    // for each one that sat above it, then put them back at the target spot.
                    int moveIndex = pending >= 0 ? pending : DefaultDropIndex();
                    moveIndex = Math.Min(moveIndex, Manager.ItemList.Count);

                    var oldOrder = new List<GdItem>(Manager.ItemList);

                    foreach (var item in _rowDragItems)
                    {
                        var idx = Manager.ItemList.IndexOf(item);
                        if (idx < 0)
                            continue;
                        Manager.ItemList.RemoveAt(idx);
                        if (idx < moveIndex)
                            moveIndex--;
                    }

                    if (moveIndex == 0 && Manager.ItemList.Count > 0 && IsMenuEntry(Manager.ItemList[0]))
                        moveIndex = 1;
                    moveIndex = Math.Min(moveIndex, Manager.ItemList.Count);

                    foreach (var item in _rowDragItems)
                        Manager.ItemList.Insert(moveIndex++, item);

                    if (!oldOrder.SequenceEqual(Manager.ItemList))
                    {
                        var reorderOp = new ListReorderOperation
                        {
                            ItemList = Manager.ItemList,
                            OldOrder = oldOrder,
                            NewOrder = new List<GdItem>(Manager.ItemList)
                        };
                        Manager.UndoManager.RecordChange(reorderOp);
                    }
                }
                catch (Exception ex)
                {
                    await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
                }
                return;
            }

            if (e.DataTransfer.Contains(DataFormat.File))
            {
                // Land the drop where the guide line settled during the drag. The drop
                // event position is not trustworthy on some setups, the hover position is.
                int insertIndex = pending >= 0 ? pending : DefaultDropIndex();
                insertIndex = Math.Min(insertIndex, Manager.ItemList.Count);

                IsBusy = true;
                var invalid = new List<string>();
                var unsupportedRedumpGdi = new List<string>();

                try
                {
                    // Dropped files arrive as storage items, take the real path off each.
                    var droppedItems = e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>();
                    var companions = await ImageHelper.GetCompanionFilePathsAsync(
                        droppedItems.Select(x => x.TryGetLocalPath()).Where(x => x != null));
                    var addPaths = new List<string>();

                    foreach (var storageItem in droppedItems)
                    {
                        var o = storageItem.TryGetLocalPath();
                        if (o == null)
                        {
                            invalid.Add($"{storageItem.Name} - not a local file");
                            continue;
                        }

                        // Files referenced by an index file in this same drop ride along with it.
                        if (companions.Contains(o))
                            continue;

                        addPaths.Add(o);
                    }

                    ProgressWindow progressWindow = null;
                    if (addPaths.Count > 1)
                    {
                        progressWindow = new ProgressWindow();
                        progressWindow.Title = "Adding Disc Images";
                    }

                    AddGamesResult added;
                    try
                    {
                        // Shown on the first report, so it appears after the archive
                        // add-mode dialog and never at all when the user cancels it.
                        var progress = new Progress<string>(msg =>
                        {
                            if (progressWindow != null)
                            {
                                if (!progressWindow.IsVisible)
                                    progressWindow.Show(this);
                                progressWindow.TextContent = msg;
                            }
                        });

                        added = await Manager.AddGames(
                            addPaths.ToArray(),
                            insertIndex,
                            AddGamesUndoProfile.AvaloniaExternalDrop,
                            progress);
                    }
                    finally
                    {
                        if (progressWindow != null)
                        {
                            progressWindow.AllowClose();
                            progressWindow.Close();
                        }
                    }

                    invalid.AddRange(added.InvalidDetails.Select(failure =>
                        $"{failure.Path} - {failure.Message}"));
                    unsupportedRedumpGdi.AddRange(added.UnsupportedRedumpGdi);

                    if (added.AddedItems.Count > 0)
                    {
                        // Show serial translation dialog if any items were translated
                        await ShowSerialTranslationDialogIfNeeded();
                    }

                    if (invalid.Any())
                        await new TextWindow("Ignored folders/files", string.Join(Environment.NewLine + Environment.NewLine, invalid)).ShowDialog(this);

                    if (unsupportedRedumpGdi.Any())
                        await MessageBoxManager.GetMessageBoxStandard("Information", LegacyRedumpGdiDetector.BuildMessage(unsupportedRedumpGdi), icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
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

        // The menu disc must stay in slot 0, so everything that protects that slot goes
        // through this. IsMenuItem alone is not enough, it reads Ip which stays null for
        // lazy loaded items until a metadata scan runs, so check the cached name and the
        // folder number too, same as the context menu block.
        private static bool IsMenuEntry(GdItem item)
        {
            if (item == null)
                return false;
            if (item.IsMenuItem)
                return true;
            if (item.Name == "GDMENU" || item.Name == "openMenu")
                return true;
            return item.SdNumber == 1;
        }

        // Fallback spot when the pointer is not over a row. Anything we can't place
        // goes to the end of the list.
        private int DefaultDropIndex()
        {
            return Manager.ItemList.Count;
        }

        private void WindowDragOver(object sender, DragEventArgs e)
        {
            bool isFileDrag = e.DataTransfer.Contains(DataFormat.File);
            bool isRowDrag = _rowDragItems != null && e.DataTransfer.Contains(RowDragFormat);

            if (IsFilterActive || Manager.sdPath == null || (!isFileDrag && !isRowDrag))
            {
                _pendingDropIndex = -1;
                HideDropLine();
                return;
            }

            if (isRowDrag)
                e.DragEffects = DragDropEffects.Move;

            var target = HitTestDropRow(e);
            if (target == null)
            {
                _pendingDropIndex = DefaultDropIndex();
                HideDropLine();
            }
            else
            {
                _pendingDropIndex = target.Value.InsertIndex;
                ShowDropLine(target.Value.Row, target.Value.Below);
            }
        }

        private void WindowDragLeave(object sender, RoutedEventArgs e)
        {
            // DragLeave can fire right before Drop, so don't wipe _pendingDropIndex here
            // or the drop snaps to slot 1. just hide the line.
            HideDropLine();
        }

        // Finds the row under the pointer and where an item would go. Upper half of a row
        // means above it, lower half below. The menu entry keeps slot 0, so a drop meant.
        // for the very top lands just under it instead. pointing at the open space under
        // The last row means after that row. Returns null when off the rows.
        private (DataGridRow Row, bool Below, int InsertIndex)? HitTestDropRow(DragEventArgs e)
        {
            try
            {
                var list = Manager.ItemList;

                if (dg1 == null || !dg1.IsVisible)
                    return null;

                var pos = e.GetPosition(dg1);
                double y = pos.Y;

                DataGridRow bottomRow = null;
                GdItem bottomItem = null;
                double bottomEdge = double.MinValue;

                foreach (var row in dg1.GetVisualDescendants().OfType<DataGridRow>())
                {
                    // Recycled rows from a previous card load stay parked in the visual
                    // tree, invisible, holding items that are no longer in the list. They
                    // tile the empty area below the live rows, so they must not count as
                    // drop targets or as the bottom row.
                    if (!row.IsVisible)
                        continue;

                    if (!(row.DataContext is GdItem hoveredItem))
                        continue;

                    int index = list.IndexOf(hoveredItem);
                    if (index < 0)
                        continue;

                    var rowTop = row.TranslatePoint(new Point(0, 0), dg1);
                    if (rowTop == null)
                        continue;

                    double top = rowTop.Value.Y;
                    double height = row.Bounds.Height;

                    if (top + height > bottomEdge)
                    {
                        bottomEdge = top + height;
                        bottomRow = row;
                        bottomItem = hoveredItem;
                    }

                    if (y < top || y >= top + height)
                        continue;

                    bool below = y > top + height / 2;
                    int insertIndex = below ? index + 1 : index;

                    if (insertIndex == 0 && list.Count > 0 && IsMenuEntry(list[0]))
                    {
                        insertIndex = 1;
                        below = true;
                    }

                    return (row, below, Math.Min(insertIndex, list.Count));
                }

                // Pointer is somewhere under the rows, in the grid's empty space or past
                // the bottom of the grid itself, both read as plain white space to the
                // user. Land the drop after the lowest row, which can only be the last
                // item since rows fill the view when scrolled mid list. No lower bound
                // on y, anything below the last row within the grid's width means append.
                if (bottomRow != null && y >= bottomEdge &&
                    pos.X >= 0 && pos.X < dg1.Bounds.Width)
                {
                    int index = list.IndexOf(bottomItem);
                    if (index >= 0)
                        return (bottomRow, true, Math.Min(index + 1, list.Count));
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ShowDropLine(DataGridRow row, bool below)
        {
            if (DropLine == null || dg1 == null)
                return;

            var top = row.TranslatePoint(new Point(0, 0), dg1);
            if (top == null)
            {
                HideDropLine();
                return;
            }

            double y = top.Value.Y;
            if (below)
                y += row.Bounds.Height;

            DropLine.Margin = new Thickness(0, y - 1, 0, 0);
            DropLine.IsVisible = true;
        }

        private void HideDropLine()
        {
            if (DropLine != null)
                DropLine.IsVisible = false;
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
                var msgBox = MessageBoxManager.GetMessageBoxStandard("Error",
                    msg, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                await msgBox.ShowWindowDialogAsync(this);
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
                await MessageBoxManager.GetMessageBoxStandard("Information", string.Join(Environment.NewLine, list), icon: MsBox.Avalonia.Enums.Icon.None).ShowWindowDialogAsync(this);
            }
            await new AboutWindow().ShowDialog(this);
            IsBusy = false;
        }

        private async void ButtonFolder_Click(object sender, RoutedEventArgs e)
        {
            var pickerOptions = new FolderPickerOpenOptions
            {
                Title = "Select Temporary Folder",
                AllowMultiple = false
            };

            if (!string.IsNullOrEmpty(TempFolder))
                pickerOptions.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(TempFolder);

            var folders = await StorageProvider.OpenFolderPickerAsync(pickerOptions);
            var selectedFolder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                TempFolder = selectedFolder;
                SaveTempFolderConfig();
            }
        }

        private async void ButtonResetTempFolder_Click(object sender, RoutedEventArgs e)
        {
            var result = await MessageBoxManager.GetMessageBoxStandard("Confirmation", "Reset the Temporary Folder path to default?", MsBox.Avalonia.Enums.ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Question).ShowWindowDialogAsync(this);
            if (result == MsBox.Avalonia.Enums.ButtonResult.Yes)
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
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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

                // Handle serial translation before opening artwork window.
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
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
            var result = await MessageBoxManager.GetMessageBoxStandard(
                "Confirmation",
                sortDescription,
                MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                MsBox.Avalonia.Enums.Icon.Question).ShowWindowDialogAsync(this);

            if (result != MsBox.Avalonia.Enums.ButtonResult.Yes)
                return;

            IsBusy = true;
            try
            {
                await Manager.SortList();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
                var oldTitles = Manager.ItemList.ToDictionary(
                    item => item,
                    item => (
                        Name: item.Name,
                        WasUserEdited: item.HasUserEditedCompressedTitle));

                var count = await Manager.BatchRenameItems(w.NotOnCard, w.OnCard, w.FolderName, w.ParseTosec);

                // Record undo for items whose names actually changed
                if (count > 0)
                {
                    var undoOp = new TitleEditOperation("Batch Rename");

                    foreach (var item in Manager.ItemList)
                    {
                        if (oldTitles.TryGetValue(item, out var old) && item.Name != old.Name)
                        {
                            undoOp.Add(item, old.Name, old.WasUserEdited);
                        }
                    }

                    if (undoOp.Count > 0)
                    {
                        Manager.UndoManager.RecordChange(undoOp);
                    }
                }

                await MessageBoxManager.GetMessageBoxStandard("Information", $"{count} item(s) renamed").ShowWindowDialogAsync(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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

        private async void ButtonMenuOptions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new MenuOptionsWindow(Manager);
                await window.ShowDialog(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message,
                    icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
            }
        }

        private async void ButtonFolderTools_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Only offer the batch rename tab when the full unfiltered list is loaded and has folders
                Dictionary<string, int> folderCounts = null;
                if (!IsFilterActive && Manager.ItemList.Count > 0)
                {
                    var counts = Manager.GetFolderCounts();
                    if (counts.Count > 0)
                        folderCounts = counts;
                }

                var window = new FolderToolsWindow(Manager, folderCounts, Manager.ItemList.Count);
                await window.ShowDialog(this);

                if (window.FolderMappings != null)
                {
                    // Snapshot before applying.
                    var snapshots = Manager.ItemList.Select(i => new BatchFolderRenameOperation.ItemSnapshot
                    {
                        Item = i,
                        OldFolder = i.Folder,
                        OldAltFolders = new List<string>(i.AlternativeFolders)
                    }).ToList();

                    var (updatedCount, conflictsRemoved) = Manager.ApplyFolderMappings(window.FolderMappings);

                    // Move any folder artwork along with the renamed paths
                    var artRekeys = Manager.RekeyFolderArtForMappings(window.FolderMappings);

                    if (updatedCount > 0 || conflictsRemoved > 0)
                    {
                        // Fill in new values and filter to only changed items.
                        var undoOp = new BatchFolderRenameOperation();
                        foreach (var s in snapshots)
                        {
                            s.NewFolder = s.Item.Folder;
                            s.NewAltFolders = new List<string>(s.Item.AlternativeFolders);
                            if (s.OldFolder != s.NewFolder || !s.OldAltFolders.SequenceEqual(s.NewAltFolders))
                                undoOp.Snapshots.Add(s);
                        }

                        undoOp.FolderArtDat = Manager.FolderArtDat;
                        undoOp.ArtRekeys = artRekeys;

                        if (undoOp.Snapshots.Count > 0 || artRekeys.Count > 0)
                            Manager.UndoManager.RecordChange(undoOp);

                        var msg = $"{updatedCount} disc image(s) updated across {window.FolderMappings.Count} folder(s).";
                        if (conflictsRemoved > 0)
                            msg += $"\n\n{conflictsRemoved} additional folder path(s) were automatically removed because they became duplicates of their disc image's primary folder path after renaming.";
                        msg += "\n\nClick 'Save Changes' to write updates to SD card.";

                        await MessageBoxManager.GetMessageBoxStandard("Information", msg).ShowWindowDialogAsync(this);
                    }
                    else
                    {
                        await MessageBoxManager.GetMessageBoxStandard("Information", "No changes were made.").ShowWindowDialogAsync(this);
                    }
                }
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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

            var previousDrive = SelectedDrive;
            FillDriveList(true);

            // Swapping cards in the same reader keeps the mount point. The refreshed
            // list is identical, leaving no selection change to trigger a reload.
            if (SelectedDrive != null && ReferenceEquals(SelectedDrive, previousDrive)
                && DriveList.Contains(SelectedDrive))
            {
                // DriveInfo raises no change notification. Swapping in fresh instances
                // is what makes the bound volume labels read the card in the reader now.
                var selectedIndex = DriveList.IndexOf(SelectedDrive);
                var scanned = DriveInfo.GetDrives();
                for (int i = 0; i < DriveList.Count; i++)
                {
                    var match = scanned.FirstOrDefault(x => x.Name == DriveList[i].Name);
                    if (match != null)
                        DriveList[i] = match;
                }
                SelectedDrive = DriveList[selectedIndex];
            }
        }

        private async void ButtonBrowseSdPath_Click(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select SD Card Folder",
                AllowMultiple = false
            });
            var selectedPath = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;

            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Check if it looks like a GDEMU SD card
                bool hasGdemuIni = File.Exists(Path.Combine(selectedPath, Constants.MenuConfigTextFile));
                bool has01Folder = Directory.Exists(Path.Combine(selectedPath, "01"));

                if (!hasGdemuIni && !has01Folder)
                {
                    await MessageBoxManager.GetMessageBoxStandard(
                        "Information",
                        "The selected folder does not appear to be a GDEMU SD card.\n\n" +
                        "No GDEMU.INI file or numbered folders (01, 02, etc.) were found.\n\n" +
                        "You may proceed, but the folder may not work as expected.",
                        MsBox.Avalonia.Enums.ButtonEnum.Ok,
                        MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
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
            // Fill drive list and try to find drive with gdemu contents
            //look for GDEMU.INI file.
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

            // Look for 01 folder.
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

            // Look for /media mount.
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
                // we can't easily stop the menu from showing, but the handlers already skip SdNumber == 1
            }
        }

        private void MenuItemRename_Click(object sender, RoutedEventArgs e)
        {
            var menuitem = (MenuItem)sender;
            var item = (GdItem)menuitem.CommandParameter;

            // Protect menu entry (folder 01) from renaming
            if (item?.SdNumber == 1)
                return;

            dg1.SelectedItem = item;
            dg1.CurrentColumn = dg1.Columns[4];
            dg1.BeginEdit();
        }

        private void MenuItemRenameSentence_Click(object sender, RoutedEventArgs e)
        {
            // Filter out menu entry (folder 01) from renaming
            var items = dg1.SelectedItems.Cast<GdItem>().Where(x => x.SdNumber != 1).ToList();

            if (items.Count == 0)
                return;

            var undoOp = new TitleEditOperation("Title Case");

            foreach (var item in items)
            {
                string oldTitle = item.Name;
                bool oldState = item.HasUserEditedCompressedTitle;
                string requestedTitle = TitleCaseHelper.ToTitleCase(item.Name);
                if (item.CommitUserTitle(oldTitle, requestedTitle))
                    undoOp.Add(item, oldTitle, oldState);
            }

            if (undoOp.Count > 0)
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

            var undoOp = new TitleEditOperation("Uppercase");

            foreach (var item in items)
            {
                string oldTitle = item.Name;
                bool oldState = item.HasUserEditedCompressedTitle;
                string requestedTitle = item.Name.ToUpperInvariant();
                if (item.CommitUserTitle(oldTitle, requestedTitle))
                    undoOp.Add(item, oldTitle, oldState);
            }

            if (undoOp.Count > 0)
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

            var undoOp = new TitleEditOperation("Lowercase");

            foreach (var item in items)
            {
                string oldTitle = item.Name;
                bool oldState = item.HasUserEditedCompressedTitle;
                string requestedTitle = item.Name.ToLowerInvariant();
                if (item.CommitUserTitle(oldTitle, requestedTitle))
                    undoOp.Add(item, oldTitle, oldState);
            }

            if (undoOp.Count > 0)
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
                var oldTitles = items.ToDictionary(
                    item => item,
                    item => (
                        Name: item.Name,
                        WasUserEdited: item.HasUserEditedCompressedTitle));

                await Manager.RenameItems(items, renameBy);

                // Record undo for items whose names actually changed
                var undoOp = new TitleEditOperation($"Rename by {renameBy}");

                foreach (var item in items)
                {
                    if (oldTitles.TryGetValue(item, out var old) && item.Name != old.Name)
                    {
                        undoOp.Add(item, old.Name, old.WasUserEdited);
                    }
                }

                if (undoOp.Count > 0)
                {
                    Manager.UndoManager.RecordChange(undoOp);
                }
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
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
                await MessageBoxManager.GetMessageBoxStandard("Information", "Assign Folder Path is only available in openMenu mode.", icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
                return;
            }

            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToList();

            // Filter out menu items
            selectedItems = selectedItems.Where(item =>
                item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu").ToList();

            if (selectedItems.Count == 0)
            {
                await MessageBoxManager.GetMessageBoxStandard("Information", "No valid items selected.", icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
                return;
            }

            // Handle serial translations before proceeding.
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

                // Check if the new primary folder conflicts with any item's alt folders.
                if (!string.IsNullOrEmpty(folderPath))
                {
                    var conflicting = selectedItems.Where(item =>
                        item.AlternativeFolders.Contains(folderPath)).ToList();
                    if (conflicting.Count > 0)
                    {
                        await MessageBoxManager.GetMessageBoxStandard("Information",
                            "This folder path is already assigned to this disc image as an additional folder path.",
                            icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
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
                await MessageBoxManager.GetMessageBoxStandard("Information",
                    "Additional folder paths are only available in openMenu mode.",
                    icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
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
            // Undo/redo buttons are disabled while busy, do the same for the shortcuts
            if (IsBusy)
                return;

            if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
            {
                if (Manager.UndoManager.CanUndo)
                {
                    Manager.UndoManager.Undo();
                    e.Handled = true;
                }
            }
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
                            await MessageBoxManager.GetMessageBoxStandard("Information",
                                "Nothing to show for the currently applied filter.",
                                icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
                            ClearFilterFromGrid();
                        }
                        else
                        {
                            dg1.ItemsSource = filteredItems;
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
            var pickedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select File(s)",
                AllowMultiple = true,
                FileTypeFilter = fileFilterList
            });

            var files = pickedFiles
                .Select(f => f.TryGetLocalPath())
                .Where(p => p != null)
                .ToArray();
            if (files.Any())
            {
                IsBusy = true;

                ProgressWindow progressWindow = null;
                if (files.Length > 1)
                {
                    progressWindow = new ProgressWindow();
                    progressWindow.Title = "Adding Disc Images";
                }

                AddGamesResult added;
                try
                {
                    // Shown on the first report, so it appears after the archive
                    // add-mode dialog and never at all when the user cancels it.
                    var progress = new Progress<string>(msg =>
                    {
                        if (progressWindow != null)
                        {
                            if (!progressWindow.IsVisible)
                                progressWindow.Show(this);
                            progressWindow.TextContent = msg;
                        }
                    });

                    added = await Manager.AddGames(files, progress: progress);
                }
                finally
                {
                    if (progressWindow != null)
                    {
                        progressWindow.AllowClose();
                        progressWindow.Close();
                    }
                }

                var (invalid, unsupportedRedumpGdi) = added;

                if (invalid.Any())
                    await new TextWindow("Ignored folders/files", string.Join(Environment.NewLine + Environment.NewLine, invalid)).ShowDialog(this);

                if (unsupportedRedumpGdi.Any())
                    await MessageBoxManager.GetMessageBoxStandard("Information", LegacyRedumpGdiDetector.BuildMessage(unsupportedRedumpGdi), icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);

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
                    await MessageBoxManager.GetMessageBoxStandard("Information",
                        "Nothing to show for the currently applied filter.",
                        icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
                    ClearFilterFromGrid();
                }
                else
                {
                    dg1.ItemsSource = filteredItems;
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
                    await MessageBoxManager.GetMessageBoxStandard("Information", "No matches found.",
                        icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
            }
        }

        private bool searchInGrid(int start)
        {
            var visibleItems = (dg1.ItemsSource as System.Collections.IEnumerable)?.Cast<GdItem>().ToList()
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

        private void UpdateSearchMatches()
        {
            var text = _Filter?.Trim() ?? string.Empty;
            foreach (var item in Manager.ItemList)
                item.IsMatch = text.Length > 0 && FilterInItem(item, text);
        }

        private void ApplyFilterToGrid(string filterText)
        {
            _activeFilterText = filterText;
            Filter = filterText;
            IsFilterActive = true;

            var filteredItems = Manager.ItemList.Where(item => FilterInItem(item, filterText)).ToList();
            dg1.ItemsSource = filteredItems;

            DragDrop.SetAllowDrop(this, false);
        }

        private void ClearFilterFromGrid()
        {
            dg1.ItemsSource = Manager.ItemList;

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
                await MessageBoxManager.GetMessageBoxStandard("Information", "No matches found.",
                    icon: MsBox.Avalonia.Enums.Icon.Info).ShowWindowDialogAsync(this);
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
