using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using GDMENUCardManager.Core.Interface;

namespace GDMENUCardManager.Core
{
    public enum DatFileStatus
    {
        OK,
        BothMissing,
        BoxMissingIconExists,
        BoxExistsIconMissing,
        SerialsMismatch
    }

    /// <summary>
    /// Preserves the established undo behavior of each add entry point.
    /// </summary>
    public enum AddGamesUndoProfile
    {
        Picker,
        WpfExternalDrop,
        AvaloniaExternalDrop
    }

    /// <summary>
    /// Captures one add batch without losing partial successes.
    /// </summary>
    public sealed class AddGamesResult
    {
        public ArchiveAddMode Mode { get; internal set; } = ArchiveAddMode.ParseNow;
        public List<string> Invalid { get; } = new List<string>();
        public List<(string Path, string Message)> InvalidDetails { get; } =
            new List<(string Path, string Message)>();
        public List<string> UnsupportedRedumpGdi { get; } = new List<string>();
        public List<(GdItem Item, int Index)> AddedItems { get; } =
            new List<(GdItem Item, int Index)>();

        public void Deconstruct(
            out List<string> invalid,
            out List<string> unsupportedRedumpGdi)
        {
            invalid = Invalid;
            unsupportedRedumpGdi = UnsupportedRedumpGdi;
        }
    }

    public class SpaceCheckResult
    {
        public long AvailableSpace { get; set; }
        public long SpaceToBeFreed { get; set; }
        public long NewItemsSize { get; set; }
        public long MenuWiggleRoom { get; set; } // 50MB for openMenu, 5MB for gdMenu
        public long MenuBaseSize { get; set; }
        public long MetadataBuffer { get; set; }
        public long TotalNeeded { get; set; }
        public long EffectiveAvailable { get; set; } // AvailableSpace + SpaceToBeFreed
        public long Shortfall { get; set; }
        public bool HasSufficientSpace { get; set; }
        public bool ContainsCompressedFiles { get; set; }
        public bool ShrinkingEnabled { get; set; }
        public int NewItemCount { get; set; }
        public bool MenuFolderExists { get; set; }
    }

    public partial class Manager
    {
        public static readonly string[] supportedImageFormats = new string[] { ".gdi", ".cdi", ".mds", ".ccd", ".cue", ".chd" };

        public static string sdPath = null;
        public static bool debugEnabled = false;

        private static MenuKind _menuKindSelected = MenuKind.None;
        public static MenuKind MenuKindSelected
        {
            get => _menuKindSelected;
            set
            {
                if (_menuKindSelected != value)
                {
                    _menuKindSelected = value;
                    MenuKindChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public static event EventHandler MenuKindChanged;

        private readonly string currentAppPath = AppDomain.CurrentDomain.BaseDirectory;

        private string ipbinPath
        {
            get
            {
                if (MenuKindSelected == MenuKind.None)
                    throw new Exception("Menu not selected on Settings");
                return Path.Combine(currentAppPath, "tools", MenuKindSelected.ToString(), "IP.BIN");
            }
        }

        public readonly bool EnableLazyLoading = true;
        public bool EnableGDIShrink;
        public bool EnableGDIShrinkCompressed = true;
        public bool EnableGDIShrinkBlackList = true;
        public bool EnableGDIShrinkExisting;
        public bool TruncateMenuGDI = true;

        // Region and VGA patching options
        public bool EnableRegionPatch;
        public bool EnableRegionPatchExisting;
        public bool EnableVgaPatch;
        public bool EnableVgaPatchExisting;

        // When true, checks for locked files/folders before save
        /// <summary>
        /// Set false to skip the pre-save locked-file scan.
        /// </summary>
        public bool EnableLockCheck = true;

        // set during save when patching changes a flag after the list text was built
        private bool savePatchChangedFlags;
        private readonly List<string> savePatchFailures = new List<string>();
        internal Func<string, Task<GdItem>> ArchiveImageRecognizerOverride { get; set; }
        internal Action<string, string, bool> MenuProjectionGenerated { get; set; }

        // Items patched to a manually edited region this save, the blanket region-free pass must not override them.
        private readonly HashSet<GdItem> saveManualRegionItems = new HashSet<GdItem>();


        /// <summary>
        /// Display order is the order written to the card. Index 0 is the menu when present.
        /// </summary>
        public ObservableCollection<GdItem> ItemList { get; } = new ObservableCollection<GdItem>();

        public ObservableCollection<string> KnownFolders { get; } = new ObservableCollection<string>();

        public BoxDatManager BoxDat { get; private set; }
        public IconDatManager IconDat { get; private set; }
        public MetaDatManager MetaDat { get; private set; }
        public FolderArtDatManager FolderArtDat { get; private set; }

        public UndoManager UndoManager { get; } = new UndoManager();

        public string GetBoxDatPath() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(MacOsDataMigration.GetUserMenuDataDir(), "BOX.DAT")
                : Path.Combine(currentAppPath, "tools", "openMenu", "menu_data", "BOX.DAT");

        public string GetIconDatPath() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(MacOsDataMigration.GetUserMenuDataDir(), "ICON.DAT")
                : Path.Combine(currentAppPath, "tools", "openMenu", "menu_data", "ICON.DAT");

        public string GetMetaDatPath() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(MacOsDataMigration.GetUserMenuDataDir(), "META.DAT")
                : Path.Combine(currentAppPath, "tools", "openMenu", "menu_data", "META.DAT");

        public string GetFolderArtDatPath() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(MacOsDataMigration.GetUserMenuDataDir(), "FOLDRART.DAT")
                : Path.Combine(currentAppPath, "tools", "openMenu", "menu_data", "FOLDRART.DAT");

        public string GetFolderArtMapPath() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(MacOsDataMigration.GetUserMenuDataDir(), "FOLDRART.MAP")
                : Path.Combine(currentAppPath, "tools", "openMenu", "menu_data", "FOLDRART.MAP");

        public string GetMenuDataPath() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? MacOsDataMigration.GetUserMenuDataDir()
                : Path.Combine(currentAppPath, "tools", "openMenu", "menu_data");

        public string GetDatBackupFolder() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? MacOsDataMigration.GetUserDatBackupsDir()
                : Path.Combine(currentAppPath, "dat_backups");

        public string GetDefaultsIniPath() =>
            Path.Combine(GetMenuDataPath(), MenuOptions.MenuOptionsManager.DefaultsIniFileName);

        public string GetBgmPath() =>
            Path.Combine(GetMenuDataPath(), MenuOptions.MenuOptionsManager.BgmFileName);

        // Themes always ship inside the app, even on macOS where user data lives in Application Support.
        public MenuOptions.MenuOptionsManager CreateMenuOptionsManager() =>
            new MenuOptions.MenuOptionsManager(
                GetMenuDataPath(),
                Path.Combine(currentAppPath, "tools", "openMenu", "menu_data", "theme"));

        public void InitializeBoxDat()
        {
            if (BoxDat == null)
            {
                BoxDat = new BoxDatManager();
            }

            if (IconDat == null)
            {
                IconDat = new IconDatManager();
            }

            if (MetaDat == null)
            {
                MetaDat = new MetaDatManager();
            }

            if (FolderArtDat == null)
            {
                FolderArtDat = new FolderArtDatManager();
            }

            var boxDatPath = GetBoxDatPath();
            if (File.Exists(boxDatPath))
            {
                BoxDat.Load(boxDatPath);
            }

            var folderArtDatPath = GetFolderArtDatPath();
            if (File.Exists(folderArtDatPath))
            {
                FolderArtDat.Load(folderArtDatPath, GetFolderArtMapPath());
            }

            var iconDatPath = GetIconDatPath();
            if (File.Exists(iconDatPath))
            {
                IconDat.Load(iconDatPath);
            }

            var metaDatPath = GetMetaDatPath();
            if (File.Exists(metaDatPath))
            {
                MetaDat.Load(metaDatPath);
            }

            GdItem.BoxDatManagerInstance = BoxDat;

            foreach (var item in ItemList)
            {
                item.RefreshArtworkStatus();
            }
        }

        public (bool success, string errorMessage) SaveBoxDat(bool proceedWithoutBackupOnFailure = false)
        {
            if (BoxDat == null)
                return (false, "BoxDatManager not initialized");

            var boxDatPath = GetBoxDatPath();
            var backupFolder = GetDatBackupFolder();

            var result = BoxDat.BackupAndSave(boxDatPath, backupFolder, proceedWithoutBackupOnFailure);

            if (result.success)
            {
                foreach (var item in ItemList)
                {
                    item.RefreshArtworkStatus();
                }
            }

            return result;
        }

        public (bool success, string errorMessage) SaveIconDat(bool proceedWithoutBackupOnFailure = false)
        {
            if (IconDat == null)
                return (false, "IconDatManager not initialized");

            var iconDatPath = GetIconDatPath();
            var backupFolder = GetDatBackupFolder();

            return IconDat.BackupAndSave(iconDatPath, backupFolder, proceedWithoutBackupOnFailure);
        }

        public (bool success, string errorMessage) SaveFolderArtDat(bool proceedWithoutBackupOnFailure = false)
        {
            if (FolderArtDat == null)
                return (false, "FolderArtDatManager not initialized");

            return FolderArtDat.BackupAndSave(GetFolderArtDatPath(), GetFolderArtMapPath(),
                GetDatBackupFolder(), proceedWithoutBackupOnFailure);
        }

        // Every folder path plus its ancestor prefixes, ordered depth first so each
        // folder's children follow it (the artwork list needs that ordering).
        public List<string> GetAllFolderArtPaths()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in ItemList)
            {
                void addWithAncestors(string path)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        return;

                    var segments = path.Split('\\');
                    var current = string.Empty;
                    foreach (var segment in segments)
                    {
                        current = current.Length == 0 ? segment : current + "\\" + segment;
                        paths.Add(current);
                    }
                }

                addWithAncestors(item.Folder);

                if (item.AlternativeFolders != null)
                    foreach (var alt in item.AlternativeFolders)
                        addWithAncestors(alt);
            }

            var ordered = new List<string>(paths.Count);

            void emitChildren(string parent)
            {
                var prefix = parent == null ? string.Empty : parent + "\\";

                var children = paths
                    .Where(p => p.StartsWith(prefix, StringComparison.Ordinal)
                        && p.Length > prefix.Length
                        && p.IndexOf('\\', prefix.Length) < 0)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

                foreach (var child in children)
                {
                    ordered.Add(child);
                    emitChildren(child);
                }
            }

            emitChildren(null);
            return ordered;
        }

        // Refreshes HasArtwork for all items sharing the same artwork serial (via Table 2 translation)
        public void RefreshArtworkStatusForSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return;

            var artworkSerial = SerialTranslator.TranslateForArtwork(serial);
            var normalizedArtworkSerial = BoxDatManager.NormalizeSerial(artworkSerial);

            foreach (var item in ItemList)
            {
                var itemArtworkSerial = SerialTranslator.TranslateForArtwork(item.ProductNumber);
                if (BoxDatManager.NormalizeSerial(itemArtworkSerial) == normalizedArtworkSerial)
                {
                    item.RefreshArtworkStatus();
                }
            }
        }

        public (bool success, string errorMessage) SaveBothDats(bool proceedWithoutBackupOnFailure = false)
        {
            var boxResult = SaveBoxDat(proceedWithoutBackupOnFailure);
            if (!boxResult.success && !proceedWithoutBackupOnFailure)
                return boxResult;

            var iconResult = SaveIconDat(proceedWithoutBackupOnFailure);
            if (!iconResult.success && !proceedWithoutBackupOnFailure)
                return iconResult;

            var errors = new List<string>();
            if (!string.IsNullOrEmpty(boxResult.errorMessage))
                errors.Add($"BOX.DAT: {boxResult.errorMessage}");
            if (!string.IsNullOrEmpty(iconResult.errorMessage))
                errors.Add($"ICON.DAT: {iconResult.errorMessage}");

            return (boxResult.success && iconResult.success, string.Join("\n", errors));
        }

        public (bool success, string errorMessage) SaveMetaDat(bool proceedWithoutBackupOnFailure = false)
        {
            if (MetaDat == null)
                return (false, "MetaDatManager not initialized");

            var metaDatPath = GetMetaDatPath();
            var backupFolder = GetDatBackupFolder();

            return MetaDat.BackupAndSave(metaDatPath, backupFolder, proceedWithoutBackupOnFailure);
        }

        public (bool success, string errorMessage) BackupAllDats()
        {
            var backupFolder = GetDatBackupFolder();
            var errors = new List<string>();

            try
            {
                if (!Directory.Exists(backupFolder))
                    Directory.CreateDirectory(backupFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

                var boxDatPath = GetBoxDatPath();
                if (File.Exists(boxDatPath))
                {
                    File.Copy(boxDatPath, Path.Combine(backupFolder, $"BOX_{timestamp}.DAT"));
                }

                var iconDatPath = GetIconDatPath();
                if (File.Exists(iconDatPath))
                {
                    File.Copy(iconDatPath, Path.Combine(backupFolder, $"ICON_{timestamp}.DAT"));
                }

                var metaDatPath = GetMetaDatPath();
                if (File.Exists(metaDatPath))
                {
                    File.Copy(metaDatPath, Path.Combine(backupFolder, $"META_{timestamp}.DAT"));
                }

                var folderArtDatPath = GetFolderArtDatPath();
                if (File.Exists(folderArtDatPath))
                {
                    File.Copy(folderArtDatPath, Path.Combine(backupFolder, $"FOLDRART_{timestamp}.DAT"));
                }

                var folderArtMapPath = GetFolderArtMapPath();
                if (File.Exists(folderArtMapPath))
                {
                    File.Copy(folderArtMapPath, Path.Combine(backupFolder, $"FOLDRART_{timestamp}.MAP"));
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to create backup: {ex.Message}");
            }
        }

        public void RegenerateIconDatFromBoxDat()
        {
            if (BoxDat == null || IconDat == null)
                return;

            IconDat = new IconDatManager();

            // Downscale each BOX.DAT entry to 128x128 for ICON.DAT
            foreach (var entry in BoxDat.GetAllEntries())
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var iconData = PvrEncoder.DownscaleBoxPvrToIcon(entry.Data);
                if (iconData != null)
                {
                    IconDat.SetIconForSerial(entry.Name, iconData);
                }
            }
        }

        public static (bool isValid, string errorMessage) ValidateDatFile(string filePath, uint expectedEntrySize)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "File not found");

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fs);

                if (fs.Length < 16)
                    return (false, "File too small for header");

                // Check magic
                byte[] magic = reader.ReadBytes(4);
                if (magic[0] != 'D' || magic[1] != 'A' || magic[2] != 'T' || magic[3] != 0x01)
                    return (false, "Invalid magic header (expected DAT\\x01)");

                // Check entry size
                uint entrySize = reader.ReadUInt32();
                if (entrySize != expectedEntrySize)
                    return (false, $"Unexpected entry size 0x{entrySize:X} (expected 0x{expectedEntrySize:X})");

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Error reading file: {ex.Message}");
            }
        }

        public (bool success, string errorMessage) ClearAllDatEntries()
        {
            try
            {
                // Backup first
                var backupResult = BackupAllDats();
                if (!backupResult.success)
                    return backupResult;

                // Create bare minimum DAT files
                BoxDatManager.CreateEmptyFile(GetBoxDatPath());
                IconDatManager.CreateEmptyFile(GetIconDatPath());
                MetaDatManager.CreateEmptyFile(GetMetaDatPath());

                // No empty-file state for folder art, just delete the files
                if (File.Exists(GetFolderArtDatPath()))
                    File.Delete(GetFolderArtDatPath());
                if (File.Exists(GetFolderArtMapPath()))
                    File.Delete(GetFolderArtMapPath());

                // Reinitialize the managers
                BoxDat = new BoxDatManager();
                IconDat = new IconDatManager();
                MetaDat = new MetaDatManager();
                FolderArtDat = new FolderArtDatManager();

                // Link BoxDatManager to GdItem
                GdItem.BoxDatManagerInstance = BoxDat;

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to clear DAT entries: {ex.Message}");
            }
        }

        public (bool success, string errorMessage) OverwriteDatsFromSdCard()
        {
            const string notFoundError = "Could not find openMenu DAT files on the SD card. Ensure the SD card contains an openMenu disc image in the 01 folder.";

            try
            {
                if (string.IsNullOrEmpty(sdPath) || !Directory.Exists(sdPath))
                    return (false, "No SD card path is set.");

                var gdiPath = Path.Combine(sdPath, "01", "disc.gdi");
                if (!File.Exists(gdiPath))
                    return (false, notFoundError);

                // Open the GDI and extract DAT files
                var filtersList = new Aaru.CommonTypes.FiltersList();
                Aaru.CommonTypes.Interfaces.IFilter inputFilter = null;

                try
                {
                    inputFilter = filtersList.GetFilter(gdiPath);
                    if (inputFilter == null)
                        return (false, notFoundError);

                    var opticalImage = new Aaru.DiscImages.Gdi();
                    if (!opticalImage.Open(inputFilter))
                        return (false, notFoundError);

                    try
                    {
                        // Get the high-density partition (skip audio, skip first non-audio)
                        var nonAudioPartitions = opticalImage.Partitions.Where(x => x.Type != "Audio").ToList();
                        if (nonAudioPartitions.Count < 2)
                            return (false, notFoundError);

                        var partition = nonAudioPartitions[1];

                        // Mount ISO9660 filesystem
                        var iso = new Aaru.Filesystems.ISO9660();
                        var dict = new Dictionary<string, string>();
                        iso.Mount(opticalImage, partition, Encoding.ASCII, dict, "normal");

                        try
                        {
                            var boxData = ExtractFileFromIso(iso, "/BOX.DAT");
                            var iconData = ExtractFileFromIso(iso, "/ICON.DAT");
                            var metaData = ExtractFileFromIso(iso, "/META.DAT");

                            if (boxData == null || iconData == null || metaData == null)
                                return (false, notFoundError);

                            // Cards built before the folder art feature won't have these
                            var folderArtData = ExtractFileFromIso(iso, "/FOLDRART.DAT");
                            var folderArtMap = ExtractFileFromIso(iso, "/FOLDRART.MAP");

                            var backupResult = BackupAllDats();
                            if (!backupResult.success)
                                return backupResult;

                            File.WriteAllBytes(GetBoxDatPath(), boxData);
                            File.WriteAllBytes(GetIconDatPath(), iconData);
                            File.WriteAllBytes(GetMetaDatPath(), metaData);

                            // Mirror the card's folder art state, present or not
                            if (folderArtData != null)
                            {
                                File.WriteAllBytes(GetFolderArtDatPath(), folderArtData);
                                if (folderArtMap != null)
                                    File.WriteAllBytes(GetFolderArtMapPath(), folderArtMap);
                                else if (File.Exists(GetFolderArtMapPath()))
                                    File.Delete(GetFolderArtMapPath());
                            }
                            else
                            {
                                if (File.Exists(GetFolderArtDatPath()))
                                    File.Delete(GetFolderArtDatPath());
                                if (File.Exists(GetFolderArtMapPath()))
                                    File.Delete(GetFolderArtMapPath());
                            }

                            BoxDat = new BoxDatManager();
                            BoxDat.Load(GetBoxDatPath());
                            IconDat = new IconDatManager();
                            IconDat.Load(GetIconDatPath());
                            MetaDat = new MetaDatManager();
                            MetaDat.Load(GetMetaDatPath());

                            FolderArtDat = new FolderArtDatManager();
                            if (File.Exists(GetFolderArtDatPath()))
                                FolderArtDat.Load(GetFolderArtDatPath(), GetFolderArtMapPath());

                            GdItem.BoxDatManagerInstance = BoxDat;

                            return (true, string.Empty);
                        }
                        finally
                        {
                            iso.Unmount();
                        }
                    }
                    finally
                    {
                        opticalImage.Close();
                    }
                }
                finally
                {
                    if (inputFilter != null && inputFilter.IsOpened())
                        inputFilter.Close();
                }
            }
            catch (Exception ex)
            {
                return (false, $"Failed to overwrite DATs from SD card: {ex.Message}");
            }
        }

        private static byte[] ExtractFileFromIso(Aaru.Filesystems.ISO9660 iso, string fileName)
        {
            if (iso.Stat(fileName, out var stat) == Aaru.CommonTypes.Structs.Errno.NoError && stat.Length > 0)
            {
                var buff = new byte[stat.Length];
                iso.Read(fileName, 0, stat.Length, ref buff);
                return buff;
            }
            return null;
        }

        public (bool success, string errorMessage, int boxEntriesMerged, int metaEntriesMerged) ImportDatEntries(
            string sourceFolderPath,
            bool overwriteExisting,
            Action<double> progress = null)
        {
            int boxMerged = 0;
            int metaMerged = 0;

            try
            {
                progress?.Invoke(0.0);

                var sourceBoxPath = Path.Combine(sourceFolderPath, "BOX.DAT");
                var sourceMetaPath = Path.Combine(sourceFolderPath, "META.DAT");

                bool hasSourceBox = File.Exists(sourceBoxPath);
                bool hasSourceMeta = File.Exists(sourceMetaPath);

                if (!hasSourceBox && !hasSourceMeta)
                    return (false, "Selected folder does not contain BOX.DAT or META.DAT", 0, 0);

                // Validate source files
                if (hasSourceBox)
                {
                    var validation = ValidateDatFile(sourceBoxPath, BoxDatManager.EntrySize);
                    if (!validation.isValid)
                        return (false, $"Source BOX.DAT is invalid: {validation.errorMessage}", 0, 0);
                }

                if (hasSourceMeta)
                {
                    var validation = ValidateDatFile(sourceMetaPath, MetaDatManager.EntrySize);
                    if (!validation.isValid)
                        return (false, $"Source META.DAT is invalid: {validation.errorMessage}", 0, 0);
                }

                progress?.Invoke(0.1);

                // Backup current DATs
                var backupResult = BackupAllDats();
                if (!backupResult.success)
                    return (false, backupResult.errorMessage, 0, 0);

                progress?.Invoke(0.2);

                // Import BOX.DAT entries
                if (hasSourceBox)
                {
                    var sourceBoxDat = new BoxDatManager();
                    sourceBoxDat.Load(sourceBoxPath);

                    if (!sourceBoxDat.IsLoaded)
                        return (false, $"Failed to load source BOX.DAT: {sourceBoxDat.LoadError}", 0, 0);

                    var sourceSerials = sourceBoxDat.GetAllSerials();
                    int total = sourceSerials.Count;
                    int current = 0;

                    foreach (var serial in sourceSerials)
                    {
                        bool exists = BoxDat.HasArtworkForSerial(serial);

                        if (!exists || overwriteExisting)
                        {
                            var pvrData = sourceBoxDat.GetPvrDataForSerial(serial);
                            if (pvrData != null)
                            {
                                BoxDat.SetArtworkForSerial(serial, pvrData);
                                boxMerged++;
                            }
                        }

                        current++;
                        progress?.Invoke(0.2 + (0.35 * current / Math.Max(1, total)));
                    }
                }

                progress?.Invoke(0.55);

                // Import META.DAT entries
                if (hasSourceMeta)
                {
                    var sourceMetaDat = new MetaDatManager();
                    sourceMetaDat.Load(sourceMetaPath);

                    if (!sourceMetaDat.IsLoaded)
                        return (false, $"Failed to load source META.DAT: {sourceMetaDat.LoadError}", 0, 0);

                    // Make sure current MetaDat is loaded
                    if (MetaDat == null)
                    {
                        MetaDat = new MetaDatManager();
                    }

                    var metaDatPath = GetMetaDatPath();
                    if (!MetaDat.IsLoaded && File.Exists(metaDatPath))
                    {
                        MetaDat.Load(metaDatPath);
                    }

                    metaMerged = MetaDat.MergeFrom(sourceMetaDat, overwriteExisting);
                }

                progress?.Invoke(0.7);

                // Save merged BOX.DAT
                BoxDat.Save(GetBoxDatPath());

                progress?.Invoke(0.8);

                // Regenerate ICON.DAT from merged BOX.DAT
                RegenerateIconDatFromBoxDat();
                IconDat.Save(GetIconDatPath());

                progress?.Invoke(0.9);

                // Save merged META.DAT
                if (MetaDat != null && MetaDat.HasUnsavedChanges)
                {
                    MetaDat.Save(GetMetaDatPath());
                }

                progress?.Invoke(1.0);

                // Refresh artwork status for all items
                foreach (var item in ItemList)
                {
                    item.RefreshArtworkStatus();
                }

                return (true, string.Empty, boxMerged, metaMerged);
            }
            catch (Exception ex)
            {
                return (false, $"Import failed: {ex.Message}", boxMerged, metaMerged);
            }
        }

        // Only exports artwork for items currently in the list
        public (bool success, string errorMessage, int exportedCount) ExportArtworkToPngs(
            string outputFolderPath,
            Action<double> progress = null)
        {
            int exported = 0;

            try
            {
                progress?.Invoke(0.0);

                if (!Directory.Exists(outputFolderPath))
                    Directory.CreateDirectory(outputFolderPath);

                // Build unique (Title, Serial) pairs from items with artwork
                var uniquePairs = new Dictionary<(string Title, string Serial), GdItem>();

                foreach (var item in ItemList)
                {
                    if (!item.HasArtwork || string.IsNullOrWhiteSpace(item.ProductNumber))
                        continue;

                    var key = (item.Name ?? "", BoxDatManager.NormalizeSerial(item.ProductNumber));
                    if (!uniquePairs.ContainsKey(key))
                    {
                        uniquePairs[key] = item;
                    }
                }

                int total = uniquePairs.Count;
                int current = 0;

                var folderArtKeys = FolderArtDat?.GetAllKeys() ?? new List<string>();
                total += folderArtKeys.Count;

                foreach (var kvp in uniquePairs)
                {
                    var (title, serial) = kvp.Key;
                    var item = kvp.Value;

                    // Get PVR data from BoxDat (includes in-memory changes)
                    var pvrData = BoxDat.GetPvrDataForSerial(serial);
                    if (pvrData != null)
                    {
                        // Sanitize filename
                        string sanitizedTitle = SanitizeFileName(title);
                        string fileName = $"{sanitizedTitle} [{serial}].png";
                        string outputPath = Path.Combine(outputFolderPath, fileName);

                        // Convert PVR to PNG and save
                        if (PvrEncoder.SavePvrAsPng(pvrData, outputPath))
                        {
                            exported++;
                        }
                    }

                    current++;
                    progress?.Invoke((double)current / Math.Max(1, total));
                }

                // Folder art is named "Folder-Path [key].png" with dashes replacing backslashes
                foreach (var key in folderArtKeys)
                {
                    var pvrData = FolderArtDat.GetPvrDataForKey(key);
                    if (pvrData != null)
                    {
                        var folderPath = FolderArtDat.GetPathForKey(key) ?? "Unknown Folder";
                        string sanitizedPath = SanitizeFileName(folderPath.Replace('\\', '-'));
                        string fileName = $"{sanitizedPath} [{key}].png";
                        string outputPath = Path.Combine(outputFolderPath, fileName);

                        if (PvrEncoder.SavePvrAsPng(pvrData, outputPath))
                        {
                            exported++;
                        }
                    }

                    current++;
                    progress?.Invoke((double)current / Math.Max(1, total));
                }

                return (true, string.Empty, exported);
            }
            catch (Exception ex)
            {
                return (false, $"Export failed: {ex.Message}", exported);
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Unknown";

            // First, handle colon specially by replacing it with " - " for readability.
            // Handle variations like "Title: Subtitle", "Title : Subtitle", "Title:Subtitle"
            var result = System.Text.RegularExpressions.Regex.Replace(fileName, @"\s*:\s*", " - ");

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(result);

            foreach (var c in invalidChars)
            {
                sanitized.Replace(c, '_');
            }

            // Also replace some other problematic characters
            sanitized.Replace('?', '_');
            sanitized.Replace('*', '_');
            sanitized.Replace('<', '_');
            sanitized.Replace('>', '_');
            sanitized.Replace('|', '_');
            sanitized.Replace('"', '_');

            result = sanitized.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "Unknown" : result;
        }

        public DatFileStatus CheckDatFilesStatus()
        {
            var boxPath = GetBoxDatPath();
            var iconPath = GetIconDatPath();

            bool boxExists = File.Exists(boxPath);
            bool iconExists = File.Exists(iconPath);

            if (!boxExists && !iconExists)
                return DatFileStatus.BothMissing;

            if (!boxExists && iconExists)
                return DatFileStatus.BoxMissingIconExists;

            if (boxExists && !iconExists)
                return DatFileStatus.BoxExistsIconMissing;

            // Both exist, check if serials match.
            if (BoxDat != null && BoxDat.IsLoaded && IconDat != null && IconDat.IsLoaded)
            {
                var boxSerials = BoxDat.GetAllSerials();
                var iconSerials = IconDat.GetAllSerials();

                // Check if they have the same entries
                if (boxSerials.Count != iconSerials.Count || !boxSerials.SetEquals(iconSerials))
                    return DatFileStatus.SerialsMismatch;
            }

            return DatFileStatus.OK;
        }

        public (bool success, string errorMessage) CreateEmptyDatFiles()
        {
            try
            {
                var boxPath = GetBoxDatPath();
                var iconPath = GetIconDatPath();

                // Ensure directory exists
                var menuDataDir = Path.GetDirectoryName(boxPath);
                if (!Directory.Exists(menuDataDir))
                    Directory.CreateDirectory(menuDataDir);

                BoxDatManager.CreateEmptyFile(boxPath);
                IconDatManager.CreateEmptyFile(iconPath);

                // Reload the managers
                BoxDat?.Load(boxPath);
                IconDat?.Load(iconPath);

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public (bool success, string errorMessage) CreateEmptyBoxDat()
        {
            try
            {
                var boxPath = GetBoxDatPath();

                // Ensure directory exists
                var menuDataDir = Path.GetDirectoryName(boxPath);
                if (!Directory.Exists(menuDataDir))
                    Directory.CreateDirectory(menuDataDir);

                BoxDatManager.CreateEmptyFile(boxPath);

                // Reload the manager
                BoxDat?.Load(boxPath);

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public (bool success, string errorMessage) GenerateIconDatFromBox()
        {
            try
            {
                if (BoxDat == null || !BoxDat.IsLoaded)
                    return (false, "BOX.DAT is not loaded");

                var iconPath = GetIconDatPath();

                // Generate ICON.DAT from BOX.DAT
                IconDatManager.GenerateFromBoxDat(BoxDat, iconPath);

                // Reload the manager
                IconDat?.Load(iconPath);

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public bool ArtworkDisabled { get; set; }

        /// <summary>
        /// When true, the config file is read-only and settings should not be saved.
        /// Set at startup if the config file cannot be made writable.
        /// </summary>
        public static bool ConfigReadOnly { get; set; }

        /// <summary>
        /// Checks that all existing DAT files (BOX.DAT, ICON.DAT, META.DAT) are writable.
        /// Attempts TryMakeWritable first, then returns a dictionary of any files that are
        /// still inaccessible. Returns an empty dictionary if all files are writable.
        /// </summary>
        public Dictionary<string, string> CheckDatFilesAccessibility()
        {
            var lockedFiles = new Dictionary<string, string>();

            var datPaths = new[] { GetBoxDatPath(), GetIconDatPath(), GetMetaDatPath(), GetFolderArtDatPath(), GetFolderArtMapPath() };

            foreach (var path in datPaths)
            {
                if (!File.Exists(path)) continue;

                Helper.TryMakeWritable(path);
                var error = Helper.CheckFileAccessibility(path);
                if (error != null)
                    lockedFiles[path] = error;
            }

            return lockedFiles;
        }

        /// <summary>
        /// Checks that all existing DAT files (BOX.DAT, ICON.DAT, META.DAT) are writable.
        /// Attempts TryMakeWritable first. If any are still locked, shows the LockedFilesDialog
        /// with Retry/Cancel. Returns true if all files are writable (or don't exist yet),
        /// false if user canceled.
        /// </summary>
        public async Task<bool> EnsureDatFilesWritable()
        {
            while (true)
            {
                var lockedFiles = CheckDatFilesAccessibility();
                if (lockedFiles.Count == 0) return true;

                if (!await Helper.DependencyManager.ShowLockedFilesDialog(lockedFiles))
                    return false; // user canceled
            }
        }

        /// <summary>
        /// Call once at startup. Wires up the dependency manager the whole Core relies on.
        /// </summary>
        public static Manager CreateInstance(IDependencyManager m, string[] compressedFileExtensions)
        {
            Helper.DependencyManager = m;
            Helper.CompressedFileExpression = new Func<string, bool>(x => compressedFileExtensions.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase)));

            return new Manager();
        }

        private Manager()
        {
            //ipbinPath = Path.Combine(currentAppPath, "tools", "IP.BIN");
            PlayStationDB.LoadFrom(Path.Combine(currentAppPath, Constants.PS1GameDBFile));
        }

        // The loaded card's DISCDB.JSON. Null means a legacy session with no
        // database, in which case folders are read from their sidecar files.
        private DiscDatabase discDb;
        public bool IsDiscDbMode => discDb != null;

        public async Task LoadItemsFromCard()
        {
            ItemList.Clear();
            UndoManager.Clear();  // Clear undo history when loading new SD card
            MenuKindSelected = MenuKind.None;

            discDb = await DiscDatabase.LoadAsync(sdPath);

            var toAdd = new List<Tuple<int, string>>();
            var rootDirs = await Helper.GetDirectoriesAsync(sdPath);
            foreach (var item in rootDirs)//.OrderBy(x => x))
            {
                if (int.TryParse(Path.GetFileName(item), out int number))
                {
                    toAdd.Add(new Tuple<int, string>(number, item));
                }
            }

            // A DISCDB.JSON that exists but failed to parse could mean a migrated
            // card whose database was damaged after its sidecar files were already
            // deleted, or a legacy card that just happens to have a stray corrupt
            // file sitting next to its sidecars. Only the first case can self-heal:
            // if the lowest-numbered game folder has no name.txt, the card has
            // already been migrated, so this session starts in database mode with
            // an empty database. Every folder full-parses once, and the next save
            // rewrites a complete file. A legacy card is left in legacy mode, same
            // as today.
            bool rebuildDbAfterLoad = false;
            if (discDb == null && await Helper.FileExistsAsync(DiscDatabase.GetPath(sdPath)))
            {
                var firstGameFolder = toAdd.Where(x => x.Item1 > 1).OrderBy(x => x.Item1).FirstOrDefault();
                if (firstGameFolder != null && !await Helper.FileExistsAsync(Path.Combine(firstGameFolder.Item2, Constants.NameTextFile)))
                {
                    discDb = new DiscDatabase();
                }
                else if (firstGameFolder != null)
                {
                    // The database exists but cannot be read while the text files
                    // still can. The rebuild itself runs after the load completes,
                    // once ItemList holds the legacy-loaded items.
                    if (await Helper.DependencyManager.ShowYesNoDialog("Disc Database", "The DISCDB.JSON database on this card could not be read. Rebuild it from the text files?"))
                    {
                        try
                        {
                            await Helper.DeleteFileAsync(DiscDatabase.GetPath(sdPath));
                        }
                        catch
                        {
                        }
                        rebuildDbAfterLoad = true;
                    }
                }
            }

            // Captured once, after the corrupt-database healing check above, so a
            // re-entrant call to this method (the drive-change handler can call it
            // again before this call finishes) reassigning the discDb field mid-loop
            // never makes this call resolve later folders against a different
            // card's database. Mirrors the toAdd capture above, for the same reason.
            var db = discDb;

            var invalid = new List<string>();
            bool isFirstItem = true;

            foreach (var item in toAdd.OrderBy(x => x.Item1))
                try
                {
                    GdItem itemToAdd = null;

                    DiscDbEntry dbEntry = null;
                    if (db != null)
                        db.Items.TryGetValue(Path.GetFileName(item.Item2), out dbEntry);

                    if (dbEntry != null)
                        try
                        {
                            itemToAdd = await LoadItemFromDb(item.Item1, item.Item2, dbEntry);
                        }
                        catch { }
                    else if (EnableLazyLoading)//load item without reading ip.bin. only read name.txt+serial.txt. will be null if no name.txt or empty
                        try
                        {
                            itemToAdd = await LazyLoadItemFromCard(item.Item1, item.Item2);
                        }
                        catch { }

                    // Not lazyloaded. Force full reading.
                    if (itemToAdd == null)
                        itemToAdd = await ImageHelper.CreateGdItemAsync(item.Item2);

                    ItemList.Add(itemToAdd);

                    // Detect menu kind immediately after loading the first item
                    if (isFirstItem)
                    {
                        isFirstItem = false;

                        //try to detect using name.txt info
                        MenuKindSelected = getMenuKindFromName(itemToAdd.Name);

                        // Not detected using name.txt. Try to load from ip.bin.
                        if (MenuKindSelected == MenuKind.None)
                        {
                            await LoadIP(itemToAdd);
                            MenuKindSelected = getMenuKindFromName(itemToAdd.Ip?.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    invalid.Add($"{item.Item2} {ex.Message}");
                }

            if (invalid.Any())
                throw new Exception(string.Join(Environment.NewLine, invalid));

            // A card with no game folders and no database starts in database
            // mode. The file is created on first save.
            if (discDb == null && !toAdd.Any(x => x.Item1 > 1))
                discDb = new DiscDatabase();

            // Consented rebuild of an unreadable database, now that this load has
            // populated ItemList from the (legacy) text files. A failure here is
            // safe to swallow: the bad file is already deleted, so the next load
            // takes the normal migration-prompt path instead.
            if (rebuildDbAfterLoad)
                try { await PerformDiscDbMigration(); } catch { }

            //todo implement menu fallback? to default or forced mode (in config)
            //if (MenuKindSelected == MenuKind.None) { }

            // Initialize known folders from current items
            InitializeKnownFolders();
        }

        private async ValueTask loadIP(IEnumerable<GdItem> items)
        {
            // Deferred archive rows are resolved during Save.
            var query = items
                .Where(x => x.Ip == null && !x.IsArchiveMetadataPending)
                .ToArray();
            if (query.Length == 0)
                return;

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = query.Length;
            progress.TextContent = "Loading file info...";

            do { await Task.Delay(50); } while (!progress.IsInitialized);

            try
            {
                foreach (var item in query)
                {
                    await LoadIP(item);
                    progress.ProcessedItems++;
                    if (!progress.IsVisible)//user closed window
                        throw new ProgressWindowClosedException();
                }
                await Task.Delay(100);
            }
            finally
            {
                progress.AllowClose();
                progress.Close();
            }
        }

        public ValueTask LoadIpAll()
        {
            return loadIP(ItemList);
        }

        public async Task LoadIP(GdItem item)
        {
            //await Task.Delay(2000);

            string filePath = string.Empty;
            try
            {
                filePath = Path.Combine(item.FullFolderPath, item.ImageFile);

                var i = await ImageHelper.CreateGdItemAsync(filePath);
                item.Ip = i.Ip;
                item.CanApplyGDIShrink = i.CanApplyGDIShrink;
                item.ImageFiles.Clear();
                item.ImageFiles.AddRange(i.ImageFiles);

                // Re-trigger serial translation now that Ip is populated
                item.ProductNumber = item.ProductNumber;
            }
            catch (Exception)
            {
                throw new Exception("Error loading file " + filePath);
            }
        }

        public List<GdItem> GetItemsNeedingMetadataScan()
        {
            return ItemList.Where(x => x.Ip == null && x.SdNumber > 0).ToList();
        }

        // Parses disc images for items missing cache files and writes the cache out
        public async Task PerformMetadataScan(List<GdItem> items, IProgress<(int current, int total, string name)> progress)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                progress?.Report((i + 1, items.Count, item.Name));

                try
                {
                    await LoadIP(item);
                }
                catch (Exception ex)
                {
                    // Give it a default Ip so we can still write cache files
                    System.Diagnostics.Debug.WriteLine($"Error scanning {item.Name}: {ex.Message}");
                    if (item.Ip == null)
                        item.Ip = new IpBin();
                }

                // Write cache even on failure so we don't re-scan every launch
                await WriteCacheFiles(item);

                // Re-read cache to pick up any user-customized values LoadIP may have clobbered
                await SyncIpFromCacheFiles(item);

                if (discDb != null)
                    MergeScanResultIntoDb(item);
            }

            if (discDb != null)
                await discDb.SaveAsync(sdPath);
        }

        // Database counterpart of WriteCacheFiles + SyncIpFromCacheFiles:
        // values already in the entry win over freshly parsed ones, then the
        // entry is refreshed from the item.
        private void MergeScanResultIntoDb(GdItem item)
        {
            // The menu folder never carries metadata (mirrors
            // PerformDiscDbMigration's own menuAtIndexZero check). A
            // re-added folder 01 can still queue for a scan (name.txt and
            // serial.txt present, one of the five IP-data files missing),
            // and its parsed result must never reach the database, or a
            // later load would try to serve the menu from a cached entry.
            if (MenuKindSelected != MenuKind.None && ItemList.Count > 0 && item == ItemList[0] && item.SdNumber == 1)
                return;

            var key = Path.GetFileName(item.FullFolderPath);
            discDb.Items.TryGetValue(key, out var entry);

            if (entry != null && item.Ip != null)
            {
                if (entry.Disc != null) item.Ip.Disc = entry.Disc;
                if (entry.Vga != null) item.Ip.Vga = entry.Vga.Value;
                if (entry.Version != null) item.Ip.Version = entry.Version;
                if (entry.Date != null) item.Ip.ReleaseDate = entry.Date;
                if (entry.Region != null)
                {
                    item.Ip.Region = entry.Region;
                    item.ImageRegion = GdItem.NormalizeRegion(entry.Region);
                }
                item.NotifyIpChanged();
            }

            var newEntry = CreateDbEntry(item);
            if (newEntry.IsUsable)
                discDb.Items[key] = newEntry;
            else
                discDb.Items.Remove(key);

            // Coalesces the final entry's values back into the live item.Ip,
            // the same way SyncIpFromCacheFiles does for the legacy branch,
            // so the grid shows the same values legacy mode would show
            // after a scan (e.g., Disc "1/1" instead of a blank field left by
            // a failed parse) rather than the raw, uncoalesced defaults.
            item.Ip.Disc = newEntry.Disc;
            item.Ip.Vga = newEntry.Vga.Value;
            item.Ip.Version = newEntry.Version;
            item.Ip.ReleaseDate = newEntry.Date;
            item.Ip.Region = newEntry.Region;
            item.NotifyIpChanged();
        }

        // True when the card is old-format: no database file and at least one
        // numbered game folder. Folder 01 alone does not count. The menu
        // folder never carries metadata.
        public async Task<bool> CheckDiscDbMigrationNeeded()
        {
            if (string.IsNullOrEmpty(sdPath) || !Directory.Exists(sdPath))
                return false;

            if (await Helper.FileExistsAsync(DiscDatabase.GetPath(sdPath)))
                return false;

            var dirs = await Helper.GetDirectoriesAsync(sdPath);
            return dirs.Any(x => int.TryParse(Path.GetFileName(x), out var n) && n > 1);
        }

        private DiscDbEntry CreateDbEntry(GdItem item)
        {
            return new DiscDbEntry
            {
                Name = item.Name,
                Serial = item.ProductNumber?.Trim(),
                Type = item.GetDiscTypeFileValue(),
                Disc = item.Ip != null ? (item.Ip.Disc ?? "1/1") : null,
                Vga = item.Ip != null ? (bool?)item.Ip.Vga : null,
                Region = item.Ip != null ? (item.Ip.Region ?? string.Empty) : null,
                Version = item.Ip != null ? (item.Ip.Version ?? string.Empty) : null,
                Date = item.Ip != null ? (item.Ip.ReleaseDate ?? string.Empty) : null,
                Folder = item.Folder ?? string.Empty,
                AltFolders = item.AlternativeFolders?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                Shrunk = item.WasShrunk
            };
        }

        // One-time migration. Sidecar text files are never touched, so an
        // older version of the app can still read the card.
        public async Task PerformDiscDbMigration()
        {
            if (!Directory.Exists(sdPath))
                throw new Exception($"The SD card is no longer accessible at \"{sdPath}\".\n\nPlease reconnect the SD card and try again.");

            bool menuAtIndexZero = MenuKindSelected != MenuKind.None && ItemList.Count > 0 && ItemList[0].SdNumber == 1;

            // A folder with no serial (or no name) produces an entry that
            // LoadAsync would drop as unusable on the very next read. Such an
            // entry is never written at all. The folder simply has no
            // database entry and full-parses on every load, exactly like it
            // did before migration existed.
            var db = new DiscDatabase();
            foreach (var item in ItemList.Skip(menuAtIndexZero ? 1 : 0))
                if (item.SdNumber > 0)
                {
                    var entry = CreateDbEntry(item);
                    if (entry.IsUsable)
                        db.Items[Path.GetFileName(item.FullFolderPath)] = entry;
                }

            await db.SaveAsync(sdPath);

            // Read the file back to confirm the write actually landed before
            // switching the card into database mode.
            var verifyDb = await DiscDatabase.LoadAsync(sdPath);
            if (verifyDb == null || verifyDb.Items.Count != db.Items.Count)
            {
                // Leaving a valid-looking file in place would stop
                // CheckDiscDbMigrationNeeded from re-prompting on the next
                // load, even though migration did not actually succeed.
                // Best-effort only: the card may be misbehaving, so a
                // failure here must not mask the verify failure below.
                try
                {
                    await Helper.DeleteFileAsync(DiscDatabase.GetPath(sdPath));
                }
                catch
                {
                }

                throw new Exception($"The database file at \"{DiscDatabase.GetPath(sdPath)}\" did not verify after being written.\n\nMigration was aborted.");
            }

            discDb = db;
        }

        // Only writes files that don't already exist (preserves user edits)
        private async Task WriteCacheFiles(GdItem item)
        {
            if (string.IsNullOrEmpty(item.FullFolderPath))
                return;

            var itemSerialPath = Path.Combine(item.FullFolderPath, Constants.SerialTextFile);
            if (!File.Exists(itemSerialPath))
                await Helper.WriteTextFileAsync(itemSerialPath, item.ProductNumber?.Trim() ?? string.Empty);

            var itemDiscPath = Path.Combine(item.FullFolderPath, Constants.DiscTextFile);
            if (!File.Exists(itemDiscPath))
                await Helper.WriteTextFileAsync(itemDiscPath, item.Ip?.Disc ?? "1/1");

            var itemVgaPath = Path.Combine(item.FullFolderPath, Constants.VgaTextFile);
            if (!File.Exists(itemVgaPath))
                await Helper.WriteTextFileAsync(itemVgaPath, (item.Ip?.Vga ?? false) ? "1" : "0");

            var itemVersionPath = Path.Combine(item.FullFolderPath, Constants.VersionTextFile);
            if (!File.Exists(itemVersionPath))
                await Helper.WriteTextFileAsync(itemVersionPath, item.Ip?.Version ?? string.Empty);

            var itemDatePath = Path.Combine(item.FullFolderPath, Constants.DateTextFile);
            if (!File.Exists(itemDatePath))
                await Helper.WriteTextFileAsync(itemDatePath, item.Ip?.ReleaseDate ?? string.Empty);

            var itemRegionPath = Path.Combine(item.FullFolderPath, Constants.RegionTextFile);
            if (!File.Exists(itemRegionPath))
                await Helper.WriteTextFileAsync(itemRegionPath, item.Ip?.Region ?? string.Empty);
        }

        // Re-reads cache files into item.Ip (cache wins over in-memory values)
        private async Task SyncIpFromCacheFiles(GdItem item)
        {
            if (item.Ip == null || string.IsNullOrEmpty(item.FullFolderPath))
                return;

            var discPath = Path.Combine(item.FullFolderPath, Constants.DiscTextFile);
            if (File.Exists(discPath))
                item.Ip.Disc = (await Helper.ReadAllTextAsync(discPath))?.Trim() ?? "1/1";

            var vgaPath = Path.Combine(item.FullFolderPath, Constants.VgaTextFile);
            if (File.Exists(vgaPath))
            {
                var vgaVal = (await Helper.ReadAllTextAsync(vgaPath))?.Trim() ?? "";
                item.Ip.Vga = vgaVal == "1" || vgaVal.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            var versionPath = Path.Combine(item.FullFolderPath, Constants.VersionTextFile);
            if (File.Exists(versionPath))
                item.Ip.Version = (await Helper.ReadAllTextAsync(versionPath))?.Trim() ?? string.Empty;

            var datePath = Path.Combine(item.FullFolderPath, Constants.DateTextFile);
            if (File.Exists(datePath))
                item.Ip.ReleaseDate = (await Helper.ReadAllTextAsync(datePath))?.Trim() ?? string.Empty;

            var regionPath = Path.Combine(item.FullFolderPath, Constants.RegionTextFile);
            if (File.Exists(regionPath))
            {
                item.Ip.Region = (await Helper.ReadAllTextAsync(regionPath))?.Trim().Replace(" ", string.Empty) ?? string.Empty;
                item.ImageRegion = GdItem.NormalizeRegion(item.Ip.Region);
            }

            // Notify UI that Ip-derived values have changed
            item.NotifyIpChanged();
        }

        public async Task RenameItems(IEnumerable<GdItem> items, RenameBy renameBy)
        {
            var itemList = items.ToList();

            if (renameBy == RenameBy.Ip)
            {
                // Parse IP.BIN on-the-fly for each item (like InfoWindow does)
                // This works for both items on SD card and items being added
                var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
                progress.TotalItems = itemList.Count;
                progress.TextContent = "Reading IP.BIN info...";

                do { await Task.Delay(50); } while (!progress.IsInitialized);

                try
                {
                    foreach (var item in itemList)
                    {
                        string name = null;

                        // Only re-parse IP.BIN for uncompressed native formats
                        if (item.FileFormat == FileFormat.Uncompressed)
                        {
                            var filePath = Path.Combine(item.FullFolderPath, item.ImageFile);
                            var ip = await ImageHelper.GetIpBinFromImage(filePath);
                            name = ip?.Name;
                        }

                        // Fallback to image filename if parsing failed or file was compressed
                        if (string.IsNullOrEmpty(name))
                            name = Path.GetFileNameWithoutExtension(item.ImageFile);

                        string oldTitle = item.Name;
                        item.CommitUserTitle(oldTitle, name);

                        progress.ProcessedItems++;
                        if (!progress.IsVisible)
                            return; // User closed window
                    }
                    await Task.Delay(100);
                }
                finally
                {
                    progress.AllowClose();
                    progress.Close();
                }
            }
            else
            {
                foreach (var item in itemList)
                {
                    string name;
                    if (renameBy == RenameBy.Folder)
                        name = Path.GetFileName(item.FullFolderPath);
                    else // file
                        name = Path.GetFileNameWithoutExtension(item.ImageFile);
                    var m = RegularExpressions.TosecnNameRegexp.Match(name);
                    if (m.Success)
                        name = name.Substring(0, m.Index);
                    string oldTitle = item.Name;
                    item.CommitUserTitle(oldTitle, name);
                }
            }
        }

        public async Task<int> BatchRenameItems(bool NotOnCard, bool OnCard, bool FolderName, bool ParseTosec)
        {
            int count = 0;

            foreach (var item in ItemList)
            {
                if (item.SdNumber == 1)
                {
                    if (item.Ip == null)
                        await LoadIP(item);

                    if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                        continue;
                }

                if ((item.SdNumber == 0 && NotOnCard) || (item.SdNumber != 0 && OnCard))
                {
                    string name;

                    if (FolderName)
                        name = Path.GetFileName(item.FullFolderPath);
                    else//file name
                        name = Path.GetFileNameWithoutExtension(item.ImageFile);

                    if (ParseTosec)
                    {
                        var m = RegularExpressions.TosecnNameRegexp.Match(name);
                        if (m.Success)
                            name = name.Substring(0, m.Index);
                    }

                    string oldTitle = item.Name;
                    item.CommitUserTitle(oldTitle, name);
                    count++;
                }
            }
            return count;
        }


        // Database-mode counterpart of LazyLoadItemFromCard. Metadata comes
        // from the entry. Image files and sizes always come from disk so an
        // out-of-band image replacement is picked up on the next load.
        private async Task<GdItem> LoadItemFromDb(int sdNumber, string folderPath, DiscDbEntry entry)
        {
            var fileInfos = await Task.Run(() => new DirectoryInfo(folderPath).GetFiles());

            FileInfo imageFile = null;
            foreach (var file in fileInfos)
            {
                if (file.Name.StartsWith("."))
                    continue;

                if (supportedImageFormats.Any(x => x.Equals(file.Extension, StringComparison.OrdinalIgnoreCase)))
                {
                    imageFile = file;
                    break;
                }
            }

            if (imageFile == null)
                throw new Exception("No valid image found on folder");

            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath,
                FileFormat = FileFormat.Uncompressed,
                SdNumber = sdNumber,
                Name = entry.Name.Trim(),
                Folder = entry.Folder ?? string.Empty,
                AlternativeFolders = entry.AltFolders?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>(),
                DiscType = GdItem.GetDiscTypeDisplayValue(entry.Type),
                Length = ByteSizeLib.ByteSize.FromBytes(fileInfos.Sum(x => x.Length)),
                CanApplyGDIShrink = imageFile.Extension.Equals(".gdi", StringComparison.InvariantCultureIgnoreCase),
                WasShrunk = entry.Shrunk,
            };

            if (entry.HasIpData)
            {
                item.Ip = new IpBin
                {
                    Disc = !string.IsNullOrWhiteSpace(entry.Disc) ? entry.Disc : "1/1",
                    Vga = entry.Vga.Value,
                    Version = entry.Version,
                    ReleaseDate = entry.Date,
                    Region = entry.Region
                };
            }
            // ProductNumber set after Ip so serial translation can see ReleaseDate
            item.ProductNumber = entry.Serial;

            item.ImageFiles.Add(imageFile.Name);

            return item;
        }

        private async Task<GdItem> LazyLoadItemFromCard(int sdNumber, string folderPath)
        {
            var files = await Helper.GetFilesAsync(folderPath);

            var itemName = string.Empty;
            var nameFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.NameTextFile, StringComparison.OrdinalIgnoreCase));
            if (nameFile != null)
                itemName = await Helper.ReadAllTextAsync(nameFile);

            // Cached "name.txt" file is required.
            if (string.IsNullOrWhiteSpace(itemName))
                return null;

            var itemSerial = string.Empty;
            var serialFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.SerialTextFile, StringComparison.OrdinalIgnoreCase));
            if (serialFile != null)
                itemSerial = await Helper.ReadAllTextAsync(serialFile);

            // Cached "serial.txt" file is required.
            if (string.IsNullOrWhiteSpace(itemSerial))
                return null;

            itemName = itemName.Trim();
            itemSerial = itemSerial.Trim();

            var itemFolder = string.Empty;
            var folderFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.FolderTextFile, StringComparison.OrdinalIgnoreCase));
            if (folderFile != null)
            {
                itemFolder = await Helper.ReadAllTextAsync(folderFile);
                itemFolder = itemFolder?.Trim() ?? string.Empty;
            }

            var itemAltFolders = new List<string>();
            foreach (var altFileName in Constants.FolderAltTextFiles)
            {
                var altFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(altFileName, StringComparison.OrdinalIgnoreCase));
                if (altFile != null)
                {
                    var altValue = await Helper.ReadAllTextAsync(altFile);
                    altValue = altValue?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(altValue))
                        itemAltFolders.Add(altValue);
                }
            }

            var itemType = "Game";
            var typeFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.TypeTextFile, StringComparison.OrdinalIgnoreCase));
            if (typeFile != null)
            {
                var typeFileValue = await Helper.ReadAllTextAsync(typeFile);
                itemType = GdItem.GetDiscTypeDisplayValue(typeFileValue);
            }

            var itemDisc = string.Empty;
            var discFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.DiscTextFile, StringComparison.OrdinalIgnoreCase));
            if (discFile != null)
            {
                itemDisc = await Helper.ReadAllTextAsync(discFile);
                itemDisc = itemDisc?.Trim() ?? string.Empty;
            }

            // Read vga.txt if it exists
            var itemVga = string.Empty;
            var vgaFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.VgaTextFile, StringComparison.OrdinalIgnoreCase));
            if (vgaFile != null)
            {
                itemVga = await Helper.ReadAllTextAsync(vgaFile);
                itemVga = itemVga?.Trim() ?? string.Empty;
            }

            // Read version.txt if it exists
            var itemVersion = string.Empty;
            var versionFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.VersionTextFile, StringComparison.OrdinalIgnoreCase));
            if (versionFile != null)
            {
                itemVersion = await Helper.ReadAllTextAsync(versionFile);
                itemVersion = itemVersion?.Trim() ?? string.Empty;
            }

            // Read date.txt if it exists
            var itemDate = string.Empty;
            var dateFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.DateTextFile, StringComparison.OrdinalIgnoreCase));
            if (dateFile != null)
            {
                itemDate = await Helper.ReadAllTextAsync(dateFile);
                itemDate = itemDate?.Trim() ?? string.Empty;
            }

            // Read region.txt if it exists
            var itemRegion = string.Empty;
            var regionFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.RegionTextFile, StringComparison.OrdinalIgnoreCase));
            if (regionFile != null)
            {
                itemRegion = await Helper.ReadAllTextAsync(regionFile);
                itemRegion = itemRegion?.Trim().Replace(" ", string.Empty) ?? string.Empty;
            }

            string itemImageFile = null;

            // Is uncompressed?
            foreach (var file in files)
            {
                if (supportedImageFormats.Any(x => x.Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase)))
                {
                    itemImageFile = file;
                    break;
                }
            }

            if (itemImageFile == null)
                throw new Exception("No valid image found on folder");

            // ProductNumber set after Ip so serial translation can see ReleaseDate
            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath,
                FileFormat = FileFormat.Uncompressed,
                SdNumber = sdNumber,
                Name = itemName,
                // set below after Ip
                Folder = itemFolder,
                AlternativeFolders = itemAltFolders,
                DiscType = itemType,
                Length = ByteSizeLib.ByteSize.FromBytes(new DirectoryInfo(folderPath).GetFiles().Sum(x => x.Length)),
                CanApplyGDIShrink = Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase),
                WasShrunk = files.Any(x => Path.GetFileName(x).Equals(Constants.ShrunkTextFile, StringComparison.OrdinalIgnoreCase)),
            };

            // Need all cache files present. If any are missing, Ip stays null
            // and the metadata scan will parse from the disc image later.
            bool hasCachedIpData = discFile != null && vgaFile != null && versionFile != null && dateFile != null && regionFile != null;

            if (hasCachedIpData)
            {
                // "1" or "true" = VGA capable
                bool vgaValue = itemVga == "1" || itemVga.Equals("true", StringComparison.OrdinalIgnoreCase);

                item.Ip = new IpBin
                {
                    Disc = !string.IsNullOrWhiteSpace(itemDisc) ? itemDisc : "1/1",
                    Vga = vgaValue,
                    Version = itemVersion,
                    ReleaseDate = itemDate,
                    Region = itemRegion
                };
            }
            // Now safe to set ProductNumber (Ip is populated if cache existed)
            item.ProductNumber = itemSerial;

            item.ImageFiles.Add(Path.GetFileName(itemImageFile));

            return item;
        }

        public async Task<SpaceCheckResult> CalculateRequiredSpace()
        {
            var result = new SpaceCheckResult
            {
                MetadataBuffer = 1 * 1024 * 1024, // 1MB for metadata files
                ShrinkingEnabled = EnableGDIShrink
            };

            // Validate required state
            if (string.IsNullOrEmpty(sdPath) || !Directory.Exists(sdPath))
            {
                result.HasSufficientSpace = true; // Can't check, assume OK
                return result;
            }

            if (MenuKindSelected == MenuKind.None)
            {
                result.HasSufficientSpace = true; // Can't check without menu type
                return result;
            }

            // Get available space on SD card
            try
            {
                // On Windows, Path.GetPathRoot works correctly (returns "D:\" etc.)
                // On Linux/macOS, we need to find the drive that contains the path
                DriveInfo driveInfo = null;
                var pathRoot = Path.GetPathRoot(sdPath);

                if (!string.IsNullOrEmpty(pathRoot) && pathRoot != "/" && pathRoot != "\\")
                {
                    // Windows-style path (or UNC path which may fail but is caught)
                    driveInfo = new DriveInfo(pathRoot);
                }
                else
                {
                    // Linux/macOS: find the mount that contains this path
                    var fullPath = Path.GetFullPath(sdPath);
                    // Normalize path with trailing separator to prevent /mnt/sd matching /mnt/sdcard
                    if (!fullPath.EndsWith(Path.DirectorySeparatorChar))
                        fullPath += Path.DirectorySeparatorChar;

                    // Use case-sensitive comparison on Linux/macOS, case-insensitive on Windows
                    var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;

                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady)
                        {
                            var mountPath = drive.RootDirectory.FullName;
                            if (!mountPath.EndsWith(Path.DirectorySeparatorChar))
                                mountPath += Path.DirectorySeparatorChar;

                            if (fullPath.StartsWith(mountPath, comparison))
                            {
                                // Find the longest matching mount point (most specific)
                                if (driveInfo == null || mountPath.Length > driveInfo.RootDirectory.FullName.Length)
                                {
                                    driveInfo = drive;
                                }
                            }
                        }
                    }
                }

                result.AvailableSpace = driveInfo?.AvailableFreeSpace ?? 0;
            }
            catch
            {
                result.AvailableSpace = 0;
            }

            // Calculate menu wiggle room based on menu type
            result.MenuWiggleRoom = MenuKindSelected == MenuKind.openMenu
                ? 50L * 1024 * 1024  // 50MB for openMenu
                : 5L * 1024 * 1024;  // 5MB for gdMenu

            // Get size of existing 01 folder or template
            var folder01 = Path.Combine(sdPath, "01");
            bool folder01Exists = Directory.Exists(folder01);
            result.MenuFolderExists = folder01Exists;
            if (folder01Exists)
            {
                result.MenuBaseSize = Helper.GetDirectorySize(folder01);
                result.SpaceToBeFreed += result.MenuBaseSize; // Old 01 will be deleted
            }
            else
            {
                // No existing 01 folder, use template size
                // The menu is built from both menu_gdi and menu_data folders
                var menuGdiPath = Path.Combine(currentAppPath, "tools", MenuKindSelected.ToString(), "menu_gdi");
                var menuDataPath = Path.Combine(currentAppPath, "tools", MenuKindSelected.ToString(), "menu_data");
                result.MenuBaseSize = Helper.GetDirectorySize(menuGdiPath) + Helper.GetDirectorySize(menuDataPath);
            }

            // Find folders that will be deleted (unused numbered folders)
            foreach (var item in await Helper.GetDirectoriesAsync(sdPath))
            {
                if (int.TryParse(Path.GetFileName(item), out int number))
                {
                    if (number > 1 && !ItemList.Any(x => x.SdNumber == number))
                    {
                        result.SpaceToBeFreed += Helper.GetDirectorySize(item);
                    }
                }
            }

            // Calculate size of new items to be added
            // Determine which items will be "New" (SdNumber == 0 means not yet on card)
            bool menuAtIndexZero = ItemList.Count > 0 && (ItemList[0].Ip?.Name == "GDMENU" || ItemList[0].Ip?.Name == "openMenu");
            int startIndex = menuAtIndexZero ? 1 : 0;

            for (int i = startIndex; i < ItemList.Count; i++)
            {
                var item = ItemList[i];

                // Item will be new if SdNumber is 0 (not yet on card)
                if (item.SdNumber == 0)
                {
                    if (item.FileFormat == FileFormat.Uncompressed || item.FileFormat == FileFormat.RedumpCueBin || item.FileFormat == FileFormat.CueBinNonGame)
                    {
                        // Sum actual file sizes
                        if (string.IsNullOrEmpty(item.FullFolderPath) || item.ImageFiles == null)
                            continue;

                        result.NewItemCount++;
                        foreach (var f in item.ImageFiles)
                        {
                            var filePath = Path.Combine(item.FullFolderPath, f);
                            if (File.Exists(filePath))
                            {
                                result.NewItemsSize += new FileInfo(filePath).Length;
                            }
                        }
                    }
                    else if (item.FileFormat == FileFormat.Chd)
                    {
                        // CHD: use LogicalBytes (uncompressed size) from header
                        result.NewItemCount++;
                        result.ContainsCompressedFiles = true;
                        result.NewItemsSize += (long)item.Length.Bytes;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(item.FullFolderPath) || string.IsNullOrEmpty(item.ImageFile))
                            continue;

                        result.NewItemCount++;
                        result.ContainsCompressedFiles = true;
                        try
                        {
                            var archivePath = Path.Combine(item.FullFolderPath, item.ImageFile);
                            var archiveEntries = Helper.DependencyManager.GetArchiveEntries(archivePath);
                            result.NewItemsSize += archiveEntries.Sum(entry => entry.Size);
                        }
                        catch
                        {
                            // If we can't read the archive, estimate based on compressed size * 2
                            var archivePath = Path.Combine(item.FullFolderPath, item.ImageFile);
                            if (File.Exists(archivePath))
                            {
                                result.NewItemsSize += new FileInfo(archivePath).Length * 2;
                            }
                        }
                    }
                }
            }

            // Calculate totals
            // When folder01 exists: old is deleted before new is created, so net menu impact is just wiggle room
            // When folder01 doesn't exist: we need the full MenuBaseSize + wiggle room
            long menuSpaceNeeded = folder01Exists ? result.MenuWiggleRoom : (result.MenuBaseSize + result.MenuWiggleRoom);
            result.TotalNeeded = result.NewItemsSize + menuSpaceNeeded + result.MetadataBuffer;
            result.EffectiveAvailable = result.AvailableSpace + result.SpaceToBeFreed;
            result.Shortfall = result.TotalNeeded - result.EffectiveAvailable;
            result.HasSufficientSpace = result.Shortfall <= 0;

            return result;
        }

        private async Task<List<string>> CollectPathsToModify()
        {
            var paths = new List<string>();

            // Menu folder (01), always gets modified
            var folder01 = Path.Combine(sdPath, "01");
            if (Directory.Exists(folder01))
            {
                paths.Add(folder01);
            }

            // Find folders that will be deleted (numbered folders not in ItemList)
            foreach (var item in await Helper.GetDirectoriesAsync(sdPath))
            {
                if (int.TryParse(Path.GetFileName(item), out int number))
                {
                    if (number > 1 && !ItemList.Any(x => x.SdNumber == number))
                    {
                        paths.Add(item);
                    }
                }
            }

            // Find folders that will be moved (items where SdNumber doesn't match position)
            bool menuAtIndexZero = ItemList.Count > 0 && (ItemList[0].Ip?.Name == "GDMENU" || ItemList[0].Ip?.Name == "openMenu");
            int startIndex = menuAtIndexZero ? 1 : 0;
            for (int i = startIndex; i < ItemList.Count; i++)
            {
                int expectedFolderNumber = i + 1;
                var item = ItemList[i];

                // If item is already on SD card and needs to move
                if (item.SdNumber > 0 && item.SdNumber != expectedFolderNumber)
                {
                    if (Directory.Exists(item.FullFolderPath))
                    {
                        paths.Add(item.FullFolderPath);
                    }
                }
            }

            // Add all existing folders on SD card (for patching, shrinking, or other modifications)
            foreach (var item in ItemList)
            {
                if (item.SdNumber > 0 && Directory.Exists(item.FullFolderPath))
                {
                    if (!paths.Contains(item.FullFolderPath))
                    {
                        paths.Add(item.FullFolderPath);
                    }
                }
            }

            return paths;
        }

        public async Task<Dictionary<string, string>> CheckForLockedFiles()
        {
            var pathsToCheck = await CollectPathsToModify();
            return await Helper.CheckPathsAccessibilityAsync(pathsToCheck);
        }

        /// <summary>
        /// Mutates the card in place. Partial failure can leave numbered folders renamed.
        /// </summary>
        public async Task<bool> Save(string tempFolderRoot)
        {
            string tempDirectory = null;
            var containsCompressedFile = false;

            try
            {
                if (MenuKindSelected == MenuKind.None)
                {
                    throw new Exception("Menu not selected on Settings");
                }

                if (!Directory.Exists(sdPath))
                {
                    throw new Exception($"The SD card is no longer accessible at \"{sdPath}\".\n\nPlease reconnect the SD card and try again.");
                }

                if (ItemList.Count == 0 || await Helper.DependencyManager.ShowYesNoDialog("Confirmation", $"Save changes to \"{sdPath}\" drive?") == false)
                {
                    return false;
                }

                // Check if GDEMU.INI needs to be created and prompt for device type
                bool? gdemuIsAuthentic = null;
                var menuConfigPath = Path.Combine(sdPath, Constants.MenuConfigTextFile);
                if (!await Helper.FileExistsAsync(menuConfigPath))
                {
                    gdemuIsAuthentic = await Helper.DependencyManager.ShowGdemuTypeDialog();
                }

                containsCompressedFile = ItemList.Any(item =>
                    item.SdNumber == 0 &&
                    item.FileFormat == FileFormat.SevenZip);

                var unsupportedArchiveGdiTitles = new List<string>();
                foreach (var item in ItemList.Where(item =>
                    item.SdNumber == 0 &&
                    item.FileFormat == FileFormat.SevenZip &&
                    item.SelectedArchiveEntry != null &&
                    Path.GetExtension(item.SelectedArchiveEntry.FullName)
                        .Equals(".gdi", StringComparison.OrdinalIgnoreCase)))
                {
                    var archivePath = Path.Combine(
                        item.FullFolderPath,
                        item.ImageFile);
                    var archiveEntries = await Task.Run(() =>
                        Helper.DependencyManager.GetArchiveEntries(archivePath));
                    var inspection = await LegacyRedumpGdiDetector.InspectGdiInArchiveAsync(
                        archivePath,
                        archiveEntries,
                        item.SelectedArchiveEntry);
                    if (inspection.IsLegacy)
                        unsupportedArchiveGdiTitles.Add(item.Name);
                }

                if (unsupportedArchiveGdiTitles.Count == 1)
                {
                    throw new UnsupportedDiscFormatException(
                        "This disc image uses Redump's unsupported legacy GDI format and cannot be saved:\n\n" +
                        unsupportedArchiveGdiTitles[0] + "\n\n" +
                        "Remove it from the Games List and try again.");
                }

                if (unsupportedArchiveGdiTitles.Count > 1)
                {
                    throw new UnsupportedDiscFormatException(
                        "These disc images use Redump's unsupported legacy GDI format and cannot be saved:\n\n" +
                        string.Join(Environment.NewLine, unsupportedArchiveGdiTitles) + "\n\n" +
                        "Remove them from the Games List and try again.");
                }

                var spaceCheck = await CalculateRequiredSpace();
                if (!spaceCheck.HasSufficientSpace)
                {
                    var proceed = await Helper.DependencyManager
                        .ShowSpaceWarningDialog(spaceCheck);
                    if (!proceed)
                        return false;
                }

                try
                {
                    await LoadIpAll();
                }
                catch (ProgressWindowClosedException)
                {
                    return false;
                }

                // Check for locked files/folders before making any modifications (if enabled)
                if (EnableLockCheck)
                {
                    while (true)
                    {
                        // First collect paths to check
                        var pathsToCheck = await CollectPathsToModify();

                        var lockCheckProgress = Helper.DependencyManager.CreateAndShowProgressWindow();
                        lockCheckProgress.TextContent = "Checking for locked files and folders...";
                        lockCheckProgress.IsIndeterminate = false;
                        do { await Task.Delay(50); } while (!lockCheckProgress.IsInitialized);

                        Dictionary<string, string> lockedFiles;
                        try
                        {
                            lockedFiles = await Helper.CheckPathsAccessibilityAsync(pathsToCheck, lockCheckProgress);
                        }
                        finally
                        {
                            lockCheckProgress.AllowClose();
                            lockCheckProgress.Close();
                        }

                        if (discDb != null)
                        {
                            var dbPath = DiscDatabase.GetPath(sdPath);
                            if (File.Exists(dbPath))
                            {
                                var dbLock = Helper.CheckFileAccessibility(dbPath);
                                if (dbLock != null)
                                    lockedFiles[dbPath] = dbLock;
                            }
                        }

                        if (lockedFiles.Count == 0)
                            break; // All files accessible, proceed with save

                        // true = retry, false = cancel
                        if (!await Helper.DependencyManager.ShowLockedFilesDialog(lockedFiles))
                        {
                            return false; // User canceled
                        }
                        // User clicked retry, loop continues to check again
                    }
                }

                savePatchChangedFlags = false;
                savePatchFailures.Clear();
                saveManualRegionItems.Clear();

                StringBuilder sb = new StringBuilder();
                StringBuilder sb_open = new StringBuilder();

                // Delete unused folders that are numbers (but skip 01 as it's the menu folder).
                List<string> foldersToDelete = new List<string>();
                foreach (var item in await Helper.GetDirectoriesAsync(sdPath))
                    if (int.TryParse(Path.GetFileName(item), out int number))
                        if (number > 1 && !ItemList.Any(x => x.SdNumber == number))
                            foldersToDelete.Add(item);

                if (foldersToDelete.Any())
                {
                    foldersToDelete.Sort();
                    var max = 15;
                    sb.AppendLine(string.Join(Environment.NewLine, foldersToDelete.Take(max)));
                    var more = foldersToDelete.Count - max;
                    if (more > 0)
                        sb.AppendLine($"[and {more} more folders]");

                    if (await Helper.DependencyManager.ShowYesNoDialog("Confirmation", $"The following folders need to be deleted.\nConfirm deletion?\n\n{sb.ToString()}") == false)
                    {
                        return false;
                    }

                    foreach (var item in foldersToDelete)
                        if (Directory.Exists(item))
                        {
                            await Helper.DeleteDirectoryAsync(item);
                        }
                }
                sb.Clear();


                if (tempDirectory == null)
                {
                    if (!tempFolderRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        tempFolderRoot += Path.DirectorySeparatorChar.ToString();

                    tempDirectory = Path.Combine(tempFolderRoot, Guid.NewGuid().ToString());

                    if (!await Helper.DirectoryExistsAsync(tempDirectory))
                        await Helper.CreateDirectoryAsync(tempDirectory);
                }

                var menuProjection = CreateMenuProjection();
                var menuIpBin = menuProjection.MenuIpBin;

                var folder01 = Path.Combine(sdPath, "01");

                if (await Helper.DirectoryExistsAsync(folder01))
                {
                    try
                    {
                        var ip01 = await ImageHelper.CreateGdItemAsync(folder01);

                        if (ip01 != null && (ip01.Ip?.Name == "GDMENU" || ip01.Ip?.Name == "openMenu"))
                        {
                            // Delete sdcard menu folder 01.
                            await Helper.DeleteDirectoryAsync(folder01);

                            //if user changed between GDMENU <> openMenu
                            // Reload name and serial from ip.bin.
                            var menu = ItemList.FirstOrDefault(x => x.Ip?.Name == "GDMENU" || x.Ip?.Name == "openMenu");

                            if ((ip01.Ip?.Name == "GDMENU" && MenuKindSelected != MenuKind.gdMenu) || ip01.Ip?.Name == "openMenu" && MenuKindSelected != MenuKind.openMenu)
                            {
                                menu.Name = menuIpBin.Name;
                                menu.ProductNumber = menuIpBin.ProductNumber;
                                menu.Ip = menuIpBin;
                            }

                            // GenerateMenuImageAsync will insert a fresh one
                            ItemList.Remove(menu);
                        }
                    }
                    catch
                    {
                        throw;//todo check?

                    }
                }

                sb.Append(menuProjection.ListText);
                sb_open.Append(menuProjection.OpenMenuListText);

                // Save DAT files if there are unsaved changes (only for openMenu)
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    bool hasBoxChanges = BoxDat?.HasUnsavedChanges == true;
                    bool hasIconChanges = IconDat?.HasUnsavedChanges == true;
                    bool hasFolderArtChanges = FolderArtDat?.HasUnsavedChanges == true;

                    if (hasBoxChanges || hasIconChanges || hasFolderArtChanges)
                    {
                        // If DAT files aren't writable and user cancels, skip DAT update
                        // but continue with save anyway
                        if (await EnsureDatFilesWritable())
                        {
                            var datProgress = Helper.DependencyManager.CreateAndShowProgressWindow();
                            datProgress.TotalItems = 1;
                            datProgress.TextContent = "Updating DAT files...";
                            do { await Task.Delay(50); } while (!datProgress.IsInitialized);

                            try
                            {
                                if (hasBoxChanges || hasIconChanges)
                                {
                                    var (success, errorMessage) = SaveBothDats(true); // Proceed without backup prompt
                                    if (!success)
                                    {
                                        // Non-fatal, continue.
                                    }
                                }

                                if (hasFolderArtChanges)
                                {
                                    SaveFolderArtDat(true); // non-fatal as well
                                }
                            }
                            finally
                            {
                                datProgress.ProcessedItems = 1;
                                await Task.Delay(100);
                                datProgress.AllowClose();
                                datProgress.Close();
                            }
                        }
                    }
                }

                await GenerateMenuImageAsync(
                    tempDirectory,
                    sb.ToString(),
                    sb_open.ToString());
                sb.Clear();
                sb_open.Clear();

                // Ensure menu item at position 0 has correct Work mode
                bool menuCurrentlyAtIndexZero = ItemList.Count > 0 && (ItemList[0].Ip?.Name == "GDMENU" || ItemList[0].Ip?.Name == "openMenu");
                if (menuCurrentlyAtIndexZero)
                {
                    ItemList[0].SdNumber = 1;
                    ItemList[0].Work = WorkMode.New;
                }

                // Define what to do with each folder (skip first item if it's the menu).
                int startIndex = menuCurrentlyAtIndexZero ? 1 : 0;
                for (int i = startIndex; i < ItemList.Count; i++)
                {
                    int folderNumber = i + 1;
                    var item = ItemList[i];

                    if (item.SdNumber == 0)
                        item.Work = WorkMode.New;
                    else if (item.SdNumber != folderNumber)
                        item.Work = WorkMode.Move;
                }

                //set correct folder numbers (skip first item if it's the menu)
                for (int i = startIndex; i < ItemList.Count; i++)
                {
                    var item = ItemList[i];
                    item.SdNumber = i + 1;
                }

                //rename numbers to guid
                var itemsToMove = ItemList.Where(x => x.Work == WorkMode.Move).ToList();
                foreach (var item in itemsToMove)
                {
                    var fromPath = item.FullFolderPath;
                    var toPath = Path.Combine(sdPath, item.Guid);
                    await Helper.MoveDirectoryAsync(fromPath, toPath);
                }

                // Rename guid to number.
                await MoveCardItems();

                // Copy new folders.
                await CopyNewItems(tempDirectory);

                // Shrink existing items if option is enabled (Windows only)
                if (EnableGDIShrinkExisting)
                {
                    await ShrinkExistingItemsAsync(tempDirectory);
                }

                // Patch existing items if option is enabled or region cells were edited
                if (EnableRegionPatchExisting || EnableVgaPatchExisting || ItemList.Any(x => x.PendingRegionChange != null))
                {
                    await PatchExistingItemsAsync();
                }

                // Region edits the patch pass didn't reach get reverted so the card stays consistent.
                RevertSkippedRegionEdits();

                //finally rename disc images, write name text file (skip menu if it's at index 0)
                foreach (var item in ItemList.Skip(menuCurrentlyAtIndexZero ? 1 : 0))
                {
                    // Rename image file.
                    if (Path.GetFileNameWithoutExtension(item.ImageFile) != Constants.DefaultImageFileName)
                    {
                        var originalExt = Path.GetExtension(item.ImageFile).ToLower();

                        if (originalExt == ".gdi")
                        {
                            var newImageFile = Constants.DefaultImageFileName + originalExt;
                            await Helper.MoveFileAsync(Path.Combine(item.FullFolderPath, item.ImageFile), Path.Combine(item.FullFolderPath, newImageFile));
                            item.ImageFiles[0] = newImageFile;
                        }
                        else
                        {
                            for (int i = 0; i < item.ImageFiles.Count; i++)
                            {
                                var oldFileName = item.ImageFiles[i];
                                var newfilename = Constants.DefaultImageFileName + Path.GetExtension(oldFileName);
                                await Helper.MoveFileAsync(Path.Combine(item.FullFolderPath, oldFileName), Path.Combine(item.FullFolderPath, newfilename));
                                item.ImageFiles[i] = newfilename;
                            }
                        }
                    }

                    // Write text name into folder.
                    var itemNamePath = Path.Combine(item.FullFolderPath, Constants.NameTextFile);
                    if (!await Helper.FileExistsAsync(itemNamePath) || (await Helper.ReadAllTextAsync(itemNamePath)).Trim() != item.Name)
                        await Helper.WriteTextFileAsync(itemNamePath, item.Name);

                    // Write serial number into folder.
                    var itemSerialPath = Path.Combine(item.FullFolderPath, Constants.SerialTextFile);
                    if (!await Helper.FileExistsAsync(itemSerialPath) || (await Helper.ReadAllTextAsync(itemSerialPath)).Trim() != item.ProductNumber)
                        await Helper.WriteTextFileAsync(itemSerialPath, item.ProductNumber?.Trim() ?? string.Empty);

                    // Marks folders whose disc was shrunk so later saves do not offer
                    // them again. A stale marker under an unshrunk disc gets removed.
                    var itemShrunkPath = Path.Combine(item.FullFolderPath, Constants.ShrunkTextFile);
                    if (item.WasShrunk)
                    {
                        if (!await Helper.FileExistsAsync(itemShrunkPath))
                            await Helper.WriteTextFileAsync(itemShrunkPath, string.Empty);
                    }
                    else if (await Helper.FileExistsAsync(itemShrunkPath))
                    {
                        await Helper.DeleteFileAsync(itemShrunkPath);
                    }

                    // Write folder path into folder.
                    var itemFolderPath = Path.Combine(item.FullFolderPath, Constants.FolderTextFile);
                    var folderValue = item.Folder ?? string.Empty;
                    if (!await Helper.FileExistsAsync(itemFolderPath) || (await Helper.ReadAllTextAsync(itemFolderPath)).Trim() != folderValue)
                        await Helper.WriteTextFileAsync(itemFolderPath, folderValue);

                    // Write alt folder paths.
                    for (int altIdx = 0; altIdx < Constants.FolderAltTextFiles.Length; altIdx++)
                    {
                        var altFilePath = Path.Combine(item.FullFolderPath, Constants.FolderAltTextFiles[altIdx]);
                        var altValue = (altIdx < item.AlternativeFolders.Count) ? item.AlternativeFolders[altIdx] : string.Empty;

                        if (string.IsNullOrEmpty(altValue))
                        {
                            if (await Helper.FileExistsAsync(altFilePath))
                                await Helper.DeleteFileAsync(altFilePath);
                        }
                        else
                        {
                            if (!await Helper.FileExistsAsync(altFilePath) || (await Helper.ReadAllTextAsync(altFilePath)).Trim() != altValue)
                                await Helper.WriteTextFileAsync(altFilePath, altValue);
                        }
                    }

                    // Write disc type into folder (openMenu only). Database-mode cards
                    // get these six files refreshed for both menu kinds, since the
                    // database itself always carries them regardless of menu kind.
                    if (MenuKindSelected == MenuKind.openMenu || discDb != null)
                    {
                        var itemTypePath = Path.Combine(item.FullFolderPath, Constants.TypeTextFile);
                        var typeValue = item.GetDiscTypeFileValue();
                        if (!await Helper.FileExistsAsync(itemTypePath) || (await Helper.ReadAllTextAsync(itemTypePath)).Trim() != typeValue)
                            await Helper.WriteTextFileAsync(itemTypePath, typeValue);

                        // Write disc number into folder.
                        var itemDiscPath = Path.Combine(item.FullFolderPath, Constants.DiscTextFile);
                        var discValue = item.Ip?.Disc ?? "1/1";
                        if (!await Helper.FileExistsAsync(itemDiscPath) || (await Helper.ReadAllTextAsync(itemDiscPath)).Trim() != discValue)
                            await Helper.WriteTextFileAsync(itemDiscPath, discValue);

                        // Write vga into folder.
                        var itemVgaPath = Path.Combine(item.FullFolderPath, Constants.VgaTextFile);
                        var vgaValue = (item.Ip?.Vga ?? false) ? "1" : "0";
                        if (!await Helper.FileExistsAsync(itemVgaPath) || (await Helper.ReadAllTextAsync(itemVgaPath)).Trim() != vgaValue)
                            await Helper.WriteTextFileAsync(itemVgaPath, vgaValue);

                        // Write version into folder.
                        var itemVersionPath = Path.Combine(item.FullFolderPath, Constants.VersionTextFile);
                        var versionValue = item.Ip?.Version ?? string.Empty;
                        if (!await Helper.FileExistsAsync(itemVersionPath) || (await Helper.ReadAllTextAsync(itemVersionPath)).Trim() != versionValue)
                            await Helper.WriteTextFileAsync(itemVersionPath, versionValue);

                        // Write date into folder.
                        var itemDatePath = Path.Combine(item.FullFolderPath, Constants.DateTextFile);
                        var dateValue = item.Ip?.ReleaseDate ?? string.Empty;
                        if (!await Helper.FileExistsAsync(itemDatePath) || (await Helper.ReadAllTextAsync(itemDatePath)).Trim() != dateValue)
                            await Helper.WriteTextFileAsync(itemDatePath, dateValue);

                        // Write region into folder.
                        var itemRegionPath = Path.Combine(item.FullFolderPath, Constants.RegionTextFile);
                        var regionValue = item.Ip?.Region ?? string.Empty;
                        if (!await Helper.FileExistsAsync(itemRegionPath) || (await Helper.ReadAllTextAsync(itemRegionPath)).Trim() != regionValue)
                            await Helper.WriteTextFileAsync(itemRegionPath, regionValue);
                    }

                    // Write info text into folder for cdi files.
                    //var itemInfoPath = Path.Combine(item.FullFolderPath, infotextfile);
                    //if (item.CdiTarget > 0)
                    //{
                    //    var newTarget = $"target|{item.CdiTarget}";
                    //    if (!await Helper.FileExistsAsync(itemInfoPath) || (await Helper.ReadAllTextAsync(itemInfoPath)).Trim() != newTarget)
                    //        await Helper.WriteTextFileAsync(itemInfoPath, newTarget);
                    //}
                }

                if (discDb != null)
                {
                    discDb.Items.Clear();
                    foreach (var item in ItemList.Skip(menuCurrentlyAtIndexZero ? 1 : 0))
                        if (item.SdNumber > 0)
                        {
                            var dbEntry = CreateDbEntry(item);
                            if (dbEntry.IsUsable)
                                discDb.Items[Path.GetFileName(item.FullFolderPath)] = dbEntry;
                        }
                    await discDb.SaveAsync(sdPath);
                }

                if (containsCompressedFile || savePatchChangedFlags)
                {
                    // Build the menu again.

                    var orderedList = ItemList.OrderBy(x => x.SdNumber);

                    sb.AppendLine("[GDMENU]");
                    sb_open.AppendLine("[OPENMENU]");
                    sb_open.AppendLine($"num_items={ItemList.Count}");
                    sb_open.AppendLine();
                    sb_open.AppendLine("[ITEMS]");

                    foreach (var item in orderedList)
                    {
                        FillListText(sb, item.Ip, item.Name, item.ProductNumber, item.SdNumber);
                        FillListText(sb_open, item.Ip, item.Name, item.ProductNumber, item.SdNumber, true, item.Folder, item.GetDiscTypeFileValue(), item.AlternativeFolders);
                    }

                    //generate iso and save in temp
                    await GenerateMenuImageAsync(tempDirectory, sb.ToString(), sb_open.ToString(), true);

                    // Move to card.
                    var menuitem = orderedList.First();

                    if (await Helper.DirectoryExistsAsync(menuitem.FullFolderPath))
                        await Helper.DeleteDirectoryAsync(menuitem.FullFolderPath);

                    //await Helper.MoveDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);
                    await Helper.CopyDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);

                    sb.Clear();
                    sb_open.Clear();
                }

                // Update menu item length.
                UpdateItemLength(ItemList.OrderBy(x => x.SdNumber).First());

                // Write menu config to root of sdcard.
                if (gdemuIsAuthentic.HasValue)
                {
                    int openTime = gdemuIsAuthentic.Value ? 500 : 1000;
                    int detectTime = gdemuIsAuthentic.Value ? 150 : 1000;
                    sb.AppendLine($"open_time = {openTime}");
                    sb.AppendLine($"detect_time = {detectTime}");
                    sb.AppendLine("reset_goto = 1");
                    sb.AppendLine("image_tests = 0");
                    await Helper.WriteTextFileAsync(menuConfigPath, sb.ToString());
                    sb.Clear();
                }

                if (debugEnabled)
                {
                    var originFile = Path.Combine(tempDirectory, "MENU_DEBUG.TXT");
                    if (File.Exists(originFile))
                        File.Copy(originFile, Path.Combine(sdPath, "MENU_DEBUG.TXT"), true);
                }

                // Write disc list to root of sdcard.
                var discListPath = Path.Combine(sdPath, "DISCLIST.TXT");
                sb.Clear();
                var sortedItems = ItemList.OrderBy(x => x.SdNumber).ToList();
                var maxSdNumber = sortedItems.Max(x => x.SdNumber);

                // Calculate column widths (minimum width = header length)
                // # column: minimum 2 digits, otherwise actual digit count
                var colNum = Math.Max(2, maxSdNumber.ToString().Length);
                var colFolder = Math.Max(6, sortedItems.Max(x => (x.Folder ?? "").Length));
                var colTitle = Math.Max(5, sortedItems.Max(x => (x.Name ?? "").Length));
                var colDisc = Math.Max(4, sortedItems.Max(x => (x.Ip?.Disc ?? "1/1").Length));
                var colSerial = Math.Max(6, sortedItems.Max(x => (x.ProductNumber ?? "").Length));
                var colRegion = Math.Max(6, sortedItems.Max(x => (x.Ip?.Region ?? "").Length));
                var colArt = 3; // "Yes" or "No"
                var colType = Math.Max(4, sortedItems.Max(x => (x.DiscType ?? "Game").Length));

                // Box-drawing characters
                string TopLine() => $"┌{"".PadRight(colNum + 2, '─')}┬{"".PadRight(colFolder + 2, '─')}┬{"".PadRight(colTitle + 2, '─')}┬{"".PadRight(colDisc + 2, '─')}┬{"".PadRight(colSerial + 2, '─')}┬{"".PadRight(colRegion + 2, '─')}┬{"".PadRight(colArt + 2, '─')}┬{"".PadRight(colType + 2, '─')}┐";
                string MidLine() => $"├{"".PadRight(colNum + 2, '─')}┼{"".PadRight(colFolder + 2, '─')}┼{"".PadRight(colTitle + 2, '─')}┼{"".PadRight(colDisc + 2, '─')}┼{"".PadRight(colSerial + 2, '─')}┼{"".PadRight(colRegion + 2, '─')}┼{"".PadRight(colArt + 2, '─')}┼{"".PadRight(colType + 2, '─')}┤";
                string BottomLine() => $"└{"".PadRight(colNum + 2, '─')}┴{"".PadRight(colFolder + 2, '─')}┴{"".PadRight(colTitle + 2, '─')}┴{"".PadRight(colDisc + 2, '─')}┴{"".PadRight(colSerial + 2, '─')}┴{"".PadRight(colRegion + 2, '─')}┴{"".PadRight(colArt + 2, '─')}┴{"".PadRight(colType + 2, '─')}┘";
                string DataRow(string num, string folder, string title, string disc, string serial, string region, string art, string type) =>
                    $"│ {num.PadLeft(colNum)} │ {folder.PadRight(colFolder)} │ {title.PadRight(colTitle)} │ {disc.PadRight(colDisc)} │ {serial.PadRight(colSerial)} │ {region.PadRight(colRegion)} │ {art.PadRight(colArt)} │ {type.PadRight(colType)} │";

                // Build table
                sb.AppendLine(TopLine());
                sb.AppendLine(DataRow("#", "Folder", "Title", "Disc", "Serial", "Region", "Art", "Type"));
                sb.AppendLine(MidLine());

                for (int i = 0; i < sortedItems.Count; i++)
                {
                    var item = sortedItems[i];
                    var num = item.SdNumber.ToString().PadLeft(2, '0');
                    var folder = item.Folder ?? "";
                    var title = item.Name ?? "";
                    var disc = item.Ip?.Disc ?? "1/1";
                    var serial = item.ProductNumber ?? "";
                    var region = !string.IsNullOrWhiteSpace(item.Ip?.Region) ? item.Ip.Region : "N/A";
                    var art = item.HasArtwork ? "Yes" : "No";
                    var type = item.DiscType ?? "Game";
                    sb.AppendLine(DataRow(num, folder, title, disc, serial, region, art, type));

                    // Add separator line between rows (but not after the last row)
                    if (i < sortedItems.Count - 1)
                        sb.AppendLine(MidLine());
                }

                sb.AppendLine(BottomLine());
                await Helper.WriteTextFileAsync(discListPath, sb.ToString());

                // Write XLSX version of disc list (cross-platform compatible spreadsheet format)
                var discListXlsxPath = Path.Combine(sdPath, "DISCLIST.XLSX");
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("DISCLIST");

                    // Set all columns to text format to prevent any auto-formatting
                    for (int col = 1; col <= 8; col++)
                        worksheet.Column(col).Style.NumberFormat.Format = "@";

                    // Headers
                    worksheet.Cell(1, 1).Value = "#";
                    worksheet.Cell(1, 2).Value = "Folder";
                    worksheet.Cell(1, 3).Value = "Title";
                    worksheet.Cell(1, 4).Value = "Disc";
                    worksheet.Cell(1, 5).Value = "Serial";
                    worksheet.Cell(1, 6).Value = "Region";
                    worksheet.Cell(1, 7).Value = "Art";
                    worksheet.Cell(1, 8).Value = "Type";

                    // Style header row: bold, background color #d6d4d4
                    var headerRange = worksheet.Range(1, 1, 1, 8);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#d6d4d4");

                    // Data rows (all text)
                    int row = 2;
                    foreach (var item in sortedItems)
                    {
                        worksheet.Cell(row, 1).Value = "'" + item.SdNumber.ToString().PadLeft(2, '0');
                        worksheet.Cell(row, 2).Value = "'" + (item.Folder ?? "");
                        worksheet.Cell(row, 3).Value = "'" + (item.Name ?? "");
                        worksheet.Cell(row, 4).Value = "'" + (item.Ip?.Disc ?? "1/1");
                        worksheet.Cell(row, 5).Value = "'" + (item.ProductNumber ?? "");
                        worksheet.Cell(row, 6).Value = "'" + (!string.IsNullOrWhiteSpace(item.Ip?.Region) ? item.Ip.Region : "N/A");
                        worksheet.Cell(row, 7).Value = "'" + (item.HasArtwork ? "Yes" : "No");
                        worksheet.Cell(row, 8).Value = "'" + (item.DiscType ?? "Game");
                        row++;
                    }

                    // Add thin black border around all cells (header + data)
                    var allDataRange = worksheet.Range(1, 1, row - 1, 8);
                    allDataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    allDataRange.Style.Border.OutsideBorderColor = XLColor.Black;
                    allDataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    allDataRange.Style.Border.InsideBorderColor = XLColor.Black;

                    // Auto-fit columns for better readability
                    worksheet.Columns().AdjustToContents();

                    workbook.SaveAs(discListXlsxPath);
                }

                if (savePatchFailures.Count > 0)
                {
                    await Helper.DependencyManager.ShowWarningDialog("Information",
                        "The following disc images could not be region patched, so their region values were reverted:\n\n"
                        + string.Join(Environment.NewLine, savePatchFailures));
                }

                return true;
            }
            catch (IOException ioEx) when (Helper.IsDiskFullException(ioEx))
            {
                // Show disk full error and exit application
                await Helper.DependencyManager.ShowDiskFullError(
                    $"Failed while saving to the SD card.\n\nError: {ioEx.Message}",
                    null);
                throw;
            }
            catch
            {
                throw;
            }
            finally
            {
                try
                {
                    if (tempDirectory != null &&
                        await Helper.DirectoryExistsAsync(tempDirectory))
                        await Helper.DeleteDirectoryAsync(tempDirectory);
                }
                catch
                {
                }

                if (discDb != null)
                {
                    try
                    {
                        var dbTmpPath = DiscDatabase.GetPath(sdPath) + ".tmp";
                        if (File.Exists(dbTmpPath))
                            File.Delete(dbTmpPath);
                    }
                    catch
                    {
                        // The card may be gone, or the file may be locked. Left behind,
                        // it is only litter: the next successful save overwrites it.
                    }
                }
            }
        }

        private (IpBin MenuIpBin, string ListText, string OpenMenuListText)
            CreateMenuProjection()
        {
            var list = new StringBuilder();
            var openMenuList = new StringBuilder();
            list.AppendLine("[GDMENU]");
            openMenuList.AppendLine("[OPENMENU]");
            openMenuList.AppendLine($"num_items={ItemList.Count}");
            openMenuList.AppendLine();
            openMenuList.AppendLine("[ITEMS]");

            var menuIpBin = ImageHelper.GetIpData(File.ReadAllBytes(ipbinPath));
            FillListText(
                list,
                menuIpBin,
                menuIpBin.ProductNumber,
                menuIpBin.Name,
                1);
            FillListText(
                openMenuList,
                menuIpBin,
                menuIpBin.Name,
                menuIpBin.ProductNumber,
                1,
                true,
                null,
                null);

            bool menuAtIndexZero = ItemList.Count > 0 &&
                (ItemList[0].Ip?.Name == "GDMENU" ||
                    ItemList[0].Ip?.Name == "openMenu");
            int gameStartIndex = menuAtIndexZero ? 1 : 0;
            for (int index = gameStartIndex; index < ItemList.Count; index++)
            {
                int entryNumber = menuAtIndexZero ? index + 1 : index + 2;
                GdItem menuState = ItemList[index];
                FillListText(
                    list,
                    menuState.Ip,
                    menuState.Name,
                    menuState.ProductNumber,
                    entryNumber);
                FillListText(
                    openMenuList,
                    menuState.Ip,
                    menuState.Name,
                    menuState.ProductNumber,
                    entryNumber,
                    true,
                    menuState.Folder,
                    menuState.GetDiscTypeFileValue(),
                    menuState.AlternativeFolders);
            }

            return (menuIpBin, list.ToString(), openMenuList.ToString());
        }

        private async Task<GdItem> GenerateMenuImageAsync(
            string tempDirectory,
            string listText,
            string openmenuListText,
            bool isRebuilding = false,
            bool publishMenuItem = true,
            bool stageUnsavedMenuData = false)
        {
            MenuProjectionGenerated?.Invoke(
                listText,
                openmenuListText,
                isRebuilding);

            // Create low density track.
            var lowdataPath = Path.Combine(tempDirectory, "lowdensity_data");
            if (!await Helper.DirectoryExistsAsync(lowdataPath))
                await Helper.CreateDirectoryAsync(lowdataPath);

            // Create hi density track.
            var dataPath = Path.Combine(tempDirectory, "data");
            if (!await Helper.DirectoryExistsAsync(dataPath))
                await Helper.CreateDirectoryAsync(dataPath);

            //var isoPath = Path.Combine(tempDirectory, "iso");
            //if (!await Helper.DirectoryExistsAsync(isoPath))
            //    await Helper.CreateDirectoryAsync(isoPath);

            //var isoFilePath = Path.Combine(isoPath, "menu.iso");
            //var isoFilePath = Path.Combine(isoPath, "menu.iso");

            var cdiPath = Path.Combine(tempDirectory, "menu_gdi");//var destinationFolder = Path.Combine(sdPath, "01");
            if (await Helper.DirectoryExistsAsync(cdiPath))
            {
                await Helper.DeleteDirectoryAsync(cdiPath);
            }

            await Helper.CreateDirectoryAsync(cdiPath);
            var cdiFilePath = Path.Combine(cdiPath, "disc.gdi");

            var menuToolsPath = Path.Combine(currentAppPath, "tools", MenuKindSelected.ToString());

            if (MenuKindSelected == MenuKind.gdMenu)
            {
                var menuDataSrc = Path.Combine(currentAppPath, "tools", "gdMenu", "menu_data");
                var menuGdiSrc = Path.Combine(currentAppPath, "tools", "gdMenu", "menu_gdi");
                var menuLowSrc = Path.Combine(currentAppPath, "tools", "gdMenu", "menu_low_data");

                await Helper.CopyDirectoryAsync(menuDataSrc, dataPath);
                await Helper.CopyDirectoryAsync(menuGdiSrc, cdiPath);
                /* Copy to low density */
                if (await Helper.DirectoryExistsAsync(menuLowSrc))
                {
                    await Helper.CopyDirectoryAsync(menuLowSrc, lowdataPath);
                }
                /* Write to low density */
                await Helper.WriteTextFileAsync(Path.Combine(lowdataPath, "LIST.INI"), listText);
                /* Write to high density */
                await Helper.WriteTextFileAsync(Path.Combine(dataPath, "LIST.INI"), listText);
                /*@Debug*/
                if (debugEnabled)
                    await Helper.WriteTextFileAsync(Path.Combine(tempDirectory, "MENU_DEBUG.TXT"), listText);
                //await Helper.WriteTextFileAsync(Path.Combine(currentAppPath, "LIST.INI"), listText);
            }
            else if (MenuKindSelected == MenuKind.openMenu)
            {
                var menuDataSrc = Path.Combine(currentAppPath, "tools", "openMenu", "menu_data");
                var menuGdiSrc = Path.Combine(currentAppPath, "tools", "openMenu", "menu_gdi");
                var menuLowSrc = Path.Combine(currentAppPath, "tools", "openMenu", "menu_low_data");

                // On macOS, user data files live in Application Support, not the bundle.
                // Exclude them from the bulk bundle copy so they don't overwrite user data.
                var excludeFromBundle = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BOX.DAT", "ICON.DAT", "META.DAT", "FOLDRART.DAT", "FOLDRART.MAP", "DEFAULTS.INI", "BGM.ADP" }
                    : null;
                await Helper.CopyDirectoryAsync(menuDataSrc, dataPath, excludeFromBundle);

                // Copy DATs from their authoritative source (Application Support on macOS, bundle on others).
                if (File.Exists(GetBoxDatPath()))
                    File.Copy(GetBoxDatPath(), Path.Combine(dataPath, "BOX.DAT"), overwrite: true);
                if (File.Exists(GetIconDatPath()))
                    File.Copy(GetIconDatPath(), Path.Combine(dataPath, "ICON.DAT"), overwrite: true);
                if (File.Exists(GetMetaDatPath()))
                    File.Copy(GetMetaDatPath(), Path.Combine(dataPath, "META.DAT"), overwrite: true);
                if (File.Exists(GetFolderArtDatPath()))
                {
                    File.Copy(GetFolderArtDatPath(), Path.Combine(dataPath, "FOLDRART.DAT"), overwrite: true);
                    if (File.Exists(GetFolderArtMapPath()))
                        File.Copy(GetFolderArtMapPath(), Path.Combine(dataPath, "FOLDRART.MAP"), overwrite: true);
                }
                if (File.Exists(GetDefaultsIniPath()))
                    File.Copy(GetDefaultsIniPath(), Path.Combine(dataPath, "DEFAULTS.INI"), overwrite: true);
                if (File.Exists(GetBgmPath()))
                    File.Copy(GetBgmPath(), Path.Combine(dataPath, "BGM.ADP"), overwrite: true);

                if (stageUnsavedMenuData)
                    StageUnsavedMenuData(dataPath);

                await Helper.CopyDirectoryAsync(menuGdiSrc, cdiPath);
                /* Copy to low density */
                if (await Helper.DirectoryExistsAsync(menuLowSrc))
                {
                    await Helper.CopyDirectoryAsync(menuLowSrc, lowdataPath);
                }
                /* Write to low density */
                await Helper.WriteTextFileAsync(Path.Combine(lowdataPath, "OPENMENU.INI"), openmenuListText);
                /* Write to high density */
                await Helper.WriteTextFileAsync(Path.Combine(dataPath, "OPENMENU.INI"), openmenuListText);
                /*@Debug*/
                if (debugEnabled)
                    await Helper.WriteTextFileAsync(Path.Combine(tempDirectory, "MENU_DEBUG.TXT"), openmenuListText);
                //await Helper.WriteTextFileAsync(Path.Combine(currentAppPath, "OPENMENU.INI"), openmenuListText);
            }
            else
            {
                throw new Exception("Menu not selected on Settings");
            }


            // Generate menu gdi.
            var builder = new DiscUtils.Gdrom.GDromBuilder()
            {
                RawMode = false,
                TruncateData = TruncateMenuGDI,
                VolumeIdentifier = MenuKindSelected == MenuKind.gdMenu ? "GDMENU" : "OPENMENU"
            };
            //builder.ReportProgress += ProgressReport;

            // Create low density track.
            List<FileInfo> fileList = new List<FileInfo>();
            // Add additional files, like themes.
            fileList.AddRange(new DirectoryInfo(lowdataPath).GetFiles());

            var track01Path = Path.Combine(cdiPath, "track01.iso");
            builder.CreateFirstTrack(track01Path, fileList);

            var track04Path = Path.Combine(cdiPath, "track04.raw");

            var updatetDiscTracks = builder.BuildGDROM(dataPath, ipbinPath, new List<string> { track04Path }, cdiPath);//todo await

            builder.UpdateGdiFile(updatetDiscTracks, cdiFilePath);

            if (isRebuilding)
                return null;

            GdItem generatedMenuItem = await ImageHelper.CreateGdItemAsync(cdiPath);
            if (generatedMenuItem == null)
                throw new InvalidDataException("The generated menu image was not recognized.");
            if (publishMenuItem)
                PublishGeneratedMenuItem(generatedMenuItem);
            return generatedMenuItem;
        }

        private void StageUnsavedMenuData(string dataPath)
        {
            bool boxChanged = BoxDat?.HasUnsavedChanges == true;
            bool iconChanged = IconDat?.HasUnsavedChanges == true;
            bool folderArtChanged = FolderArtDat?.HasUnsavedChanges == true;
            try
            {
                if (boxChanged)
                    BoxDat.Save(Path.Combine(dataPath, "BOX.DAT"));
                if (iconChanged)
                    IconDat.Save(Path.Combine(dataPath, "ICON.DAT"));
                if (folderArtChanged)
                    FolderArtDat.Save(
                        Path.Combine(dataPath, "FOLDRART.DAT"),
                        Path.Combine(dataPath, "FOLDRART.MAP"));
            }
            finally
            {
                if (boxChanged)
                    BoxDat.HasUnsavedChanges = true;
                if (iconChanged)
                    IconDat.HasUnsavedChanges = true;
                if (folderArtChanged)
                    FolderArtDat.HasUnsavedChanges = true;
            }
        }

        private void PublishGeneratedMenuItem(GdItem generatedMenuItem)
        {
            bool firstItemIsMenu = ItemList.Count > 0 &&
                (ItemList[0].Ip?.Name == "GDMENU" ||
                    ItemList[0].Ip?.Name == "openMenu");
            if (!firstItemIsMenu)
            {
                ItemList.Insert(0, generatedMenuItem);
                return;
            }

            var item = ItemList[0];
            if (!Path.GetExtension(item.ImageFile)
                .Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            {
                item.ImageFiles.Clear();
                item.ImageFiles.AddRange(generatedMenuItem.ImageFiles);
            }
            item.FullFolderPath = generatedMenuItem.FullFolderPath;
            if (item.ImageFiles.Count == 0)
                item.ImageFiles.Add(generatedMenuItem.ImageFile);
            else
                item.ImageFiles[0] = generatedMenuItem.ImageFile;
            item.SdNumber = 0;
            item.Work = WorkMode.New;
        }

        private void FillListText(StringBuilder sb, IpBin ip, string name, string serial, int number, bool is_openmenu = false, string folder = null, string type = null, List<string> altFolders = null)
        {
            string strnumber = FormatFolderNumber(number);

            sb.AppendLine($"{strnumber}.name={name}");
            if (ip?.SpecialDisc == SpecialDisc.CodeBreaker)
                sb.AppendLine($"{strnumber}.disc=");
            else
                sb.AppendLine($"{strnumber}.disc={ip?.Disc ?? "1/1"}");
            sb.AppendLine($"{strnumber}.vga={(ip?.Vga ?? true ? '1' : '0')}");
            sb.AppendLine($"{strnumber}.region={(!string.IsNullOrWhiteSpace(ip?.Region) ? ip.Region : "JUE")}");

            // Use "N/A" as default for version and date if empty or null
            var versionValue = string.IsNullOrWhiteSpace(ip?.Version) ? "N/A" : ip.Version;
            var dateValue = string.IsNullOrWhiteSpace(ip?.ReleaseDate) ? "N/A" : ip.ReleaseDate;
            sb.AppendLine($"{strnumber}.version={versionValue}");
            sb.AppendLine($"{strnumber}.date={dateValue}");

            if (is_openmenu)
            {
                string productid = GdItem.CleanSerial(serial);
                sb.AppendLine($"{strnumber}.product={productid}");
                sb.AppendLine($"{strnumber}.folder={folder ?? string.Empty}");
                if (altFolders != null)
                {
                    for (int i = 0; i < altFolders.Count; i++)
                        sb.AppendLine($"{strnumber}.folder_alt{i + 1}={altFolders[i]}");
                }
                sb.AppendLine($"{strnumber}.type={type ?? "game"}");
            }
            sb.AppendLine();
        }

        private string FormatFolderNumber(int number)
        {
            string strnumber;
            if (number < 100)
                strnumber = number.ToString("00");
            else if (number < 1000)
                strnumber = number.ToString("000");
            else if (number < 10000)
                strnumber = number.ToString("0000");
            else
                throw new Exception();
            return strnumber;
        }

        private async Task MoveCardItems()
        {
            for (int i = 0; i < ItemList.Count; i++)
            {
                var item = ItemList[i];
                if (item.Work == WorkMode.Move)
                {
                    await MoveOrCopyFolder(item, false, i + 1);//+ ammountToIncrement
                }
            }
        }

        private async Task MoveOrCopyFolder(GdItem item, bool shrink, int folderNumber)
        {
            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

            if (item.Work == WorkMode.Move)
            {
                var guidPath = Path.Combine(sdPath, item.Guid);
                await Helper.MoveDirectoryAsync(guidPath, newPath);
            }
            else if (item.Work == WorkMode.New)
            {
                if (shrink)
                {
                    var (success, message) = await GdiShrinker.Shrink(
                        Path.Combine(item.FullFolderPath, item.ImageFile), newPath);
                    if (!success)
                        throw new Exception($"Failed to shrink {item.ImageFile}: {message}");
                }
                else
                {
                    // If the destination directory exist, delete it.
                    if (Directory.Exists(newPath))
                    {
                        await Helper.DeleteDirectoryAsync(newPath);
                    }
                    // Then create a new one.
                    await Helper.CreateDirectoryAsync(newPath);

                    foreach (var f in item.ImageFiles)
                    {
                        //todo async!
                        await Task.Run(() => File.Copy(Path.Combine(item.FullFolderPath, f), Path.Combine(newPath, f)));
                    }
                }
            }

            item.FullFolderPath = newPath;
            item.SdNumber = folderNumber;

            if (item.Work == WorkMode.New && shrink)
            {
                //get the new filenames
                var gdi = await ImageHelper.CreateGdItemAsync(newPath);
                item.ImageFiles.Clear();
                item.ImageFiles.AddRange(gdi.ImageFiles);
                UpdateItemLength(item);
                item.CanApplyGDIShrink = false;
                item.WasShrunk = true;
            }

            // Apply region/VGA patches to newly copied items
            if (item.Work == WorkMode.New && (EnableRegionPatch || EnableVgaPatch || item.PendingRegionChange != null))
            {
                // Skip menu items
                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu" && item.DiscType == "Game")
                {
                    await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                }
            }

            item.Work = WorkMode.None;
        }

        private async Task CopyNewItems(string tempdir)
        {
            var total = ItemList.Count(x => x.Work == WorkMode.New);
            if (total == 0)
            {
                return;
            }

            var preExtractedPaths = new Dictionary<GdItem, string>();
            if (EnableGDIShrink && EnableGDIShrinkCompressed)
            {
                var ambiguousList = new List<(GdItem item, int folderNumber)>();
                for (int i = 0; i < ItemList.Count; i++)
                {
                    var it = ItemList[i];
                    if (it.Work == WorkMode.New
                        && it.FileFormat == FileFormat.SevenZip
                        && !it.CanApplyGDIShrink)
                    {
                        ambiguousList.Add((it, i + 1));
                    }
                }

                if (ambiguousList.Count > 0)
                {
                    var preExtractProgress = Helper.DependencyManager.CreateAndShowProgressWindow();
                    preExtractProgress.TotalItems = ambiguousList.Count;
                    preExtractProgress.TextContent = "Preparing archive contents...";
                    do { await Task.Delay(50); } while (!preExtractProgress.IsInitialized);

                    try
                    {
                        foreach (var (it, folderNumber) in ambiguousList)
                        {
                            if (!preExtractProgress.IsVisible)
                                throw new ProgressWindowClosedException();

                            preExtractProgress.TextContent = $"Preparing {it.Name}...";

                            var preExtractDir = Path.Combine(tempdir, $"ext_{folderNumber}");

                            try
                            {
                                if (!await Helper.DirectoryExistsAsync(preExtractDir))
                                    await Helper.CreateDirectoryAsync(preExtractDir);

                                var selectedExtractedPath = await ExtractSelectedArchiveAsync(
                                    it,
                                    preExtractDir);

                                var extracted = await ImageHelper.CreateGdItemAsync(selectedExtractedPath);

                                it.Ip = extracted.Ip;
                                if (string.IsNullOrWhiteSpace(it.ProductNumber))
                                    it.ProductNumber = extracted.ProductNumber;
                                if (extracted.DiscType != "Game")
                                    it.DiscType = extracted.DiscType;
                                if (extracted.CanApplyGDIShrink)
                                    it.CanApplyGDIShrink = true;

                                preExtractedPaths[it] = selectedExtractedPath;
                            }
                            catch (ProgressWindowClosedException)
                            {
                                throw;
                            }
                            catch
                            {
                                if (await Helper.DirectoryExistsAsync(preExtractDir))
                                    await Helper.DeleteDirectoryAsync(preExtractDir);
                            }

                            preExtractProgress.ProcessedItems++;
                        }
                    }
                    finally
                    {
                        preExtractProgress.AllowClose();
                        preExtractProgress.Close();
                    }
                }
            }

            // Gdishrink.
            var itemsToShrink = new List<GdItem>();
            var ignoreShrinkList = new List<string>();
            if (EnableGDIShrink)
            {
                if (EnableGDIShrinkBlackList)
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(Path.Combine(currentAppPath, Constants.GdiShrinkBlacklistFile)))
                        {
                            var split = line.Split(';');
                            if (split.Length > 2 && !string.IsNullOrWhiteSpace(split[1]))
                                ignoreShrinkList.Add(split[1].Trim());
                        }
                    }
                    catch { }
                }

                var shrinkableItems = ItemList.Where(x =>
                    x.Work == WorkMode.New && x.Ip?.Name != "GDMENU" && x.Ip?.Name != "openMenu" && x.CanApplyGDIShrink && !x.WasShrunk
                        && x.DiscType == "Game"
                        && (x.FileFormat == FileFormat.Uncompressed || x.FileFormat == FileFormat.Chd || x.FileFormat == FileFormat.RedumpCueBin || EnableGDIShrinkCompressed)
                        && !ignoreShrinkList.Contains(x.Ip?.ProductNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    ).OrderBy(x => x.Name).ThenBy(x => x.Ip?.Disc ?? "1/1").ToArray();
                if (shrinkableItems.Any())
                {
                    var result = await Helper.DependencyManager.GdiShrinkWindowShowDialog(shrinkableItems, "GDI Shrink Selector for New Games");
                    if (result != null)
                        itemsToShrink.AddRange(result);
                }
            }

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = total;
            //progress.Show();

            do { await Task.Delay(50); } while (!progress.IsInitialized);

            try
            {
                for (int i = 0; i < ItemList.Count; i++)
                {
                    var item = ItemList[i];
                    if (item.Work == WorkMode.New)
                    {
                        bool shrink;
                        if (item.FileFormat == FileFormat.Uncompressed)
                        {
                            if (EnableGDIShrink && itemsToShrink.Contains(item))
                            {
                                progress.TextContent = $"Copying/Shrinking {item.Name}...";
                                shrink = true;
                            }
                            else
                            {
                                progress.TextContent = $"Copying {item.Name}...";
                                shrink = false;
                            }

                            await MoveOrCopyFolder(item, shrink, i + 1);//+ ammountToIncrement
                        }
                        else if (item.FileFormat == FileFormat.RedumpCueBin)
                        {
                            var folderNumber = i + 1;
                            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
                            var originalFolderPath = item.FullFolderPath;

                            // Get the CUE file path
                            if (item.ImageFiles == null || !item.ImageFiles.Any())
                                throw new Exception("Image files list is empty for CUE/BIN item");

                            var cueFile = item.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrEmpty(cueFile))
                                throw new Exception("CUE file not found in image files list");

                            var cuePath = Path.Combine(originalFolderPath, cueFile);

                            // Create target directory
                            if (!await Helper.DirectoryExistsAsync(newPath))
                                await Helper.CreateDirectoryAsync(newPath);

                            // Check if this is GD-ROM or CD-ROM CUE/BIN
                            if (GdiConverter.IsGdRomCue(cuePath))
                            {
                                if (EnableGDIShrink && itemsToShrink.Contains(item))
                                {
                                    // Convert to GDI in temp dir, then shrink to SD card
                                    progress.TextContent = $"Converting/Shrinking {item.Name}...";

                                    var tempCueDir = Path.Combine(tempdir, $"cue_{folderNumber}");
                                    if (!await Helper.DirectoryExistsAsync(tempCueDir))
                                        await Helper.CreateDirectoryAsync(tempCueDir);

                                    var (success, message) = await GdiConverter.ConvertToGdi(cuePath, tempCueDir);
                                    if (!success)
                                        throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                                    var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempCueDir);

                                    (success, message) = await GdiShrinker.Shrink(
                                        Path.Combine(tempCueDir, tempGdiItem.ImageFile), newPath);
                                    if (!success)
                                        throw new Exception($"Failed to shrink {tempGdiItem.ImageFile}: {message}");

                                    await Helper.DeleteDirectoryAsync(tempCueDir);

                                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                    item.CanApplyGDIShrink = false;
                                    item.WasShrunk = true;
                                }
                                else
                                {
                                    // GD-ROM: Convert to GDI format
                                    progress.TextContent = $"Converting {item.Name} to GDI...";

                                    var (success, message) = await GdiConverter.ConvertToGdi(cuePath, newPath);
                                    if (!success)
                                        throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                                    // Get the converted GDI item info
                                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);

                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                    item.CanApplyGDIShrink = true;
                                }
                            }
                            else
                            {
                                // CD-ROM: Convert to CDI format
                                progress.TextContent = $"Converting {item.Name} to CDI...";

                                var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
                                var cdiOutputPath = Path.Combine(newPath, cdiOutputName);

                                var (success, message) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, cdiOutputPath));
                                if (!success)
                                    throw new Exception($"Failed to convert {cueFile} to CDI: {message}");

                                item.FullFolderPath = newPath;
                                item.Work = WorkMode.None;
                                item.SdNumber = folderNumber;
                                item.FileFormat = FileFormat.Uncompressed;
                                item.ImageFiles.Clear();
                                item.ImageFiles.Add(cdiOutputName);
                                item.CanApplyGDIShrink = false;
                            }

                            // Copy name.txt if it exists in original folder
                            var nameFilePath = Path.Combine(originalFolderPath, Constants.NameTextFile);
                            if (await Helper.FileExistsAsync(nameFilePath))
                                await Task.Run(() => File.Copy(nameFilePath, Path.Combine(newPath, Constants.NameTextFile), overwrite: true));

                            UpdateItemLength(item);

                            // Apply region/VGA patches to converted items
                            if (EnableRegionPatch || EnableVgaPatch)
                            {
                                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu" && item.DiscType == "Game")
                                {
                                    await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                                }
                            }
                        }
                        else if (item.FileFormat == FileFormat.CueBinNonGame)
                        {
                            var folderNumber = i + 1;
                            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
                            var originalFolderPath = item.FullFolderPath;

                            // Get the CUE file path
                            if (item.ImageFiles == null || !item.ImageFiles.Any())
                                throw new Exception("Image files list is empty for CUE/BIN item");

                            var cueFile = item.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrEmpty(cueFile))
                                throw new Exception("CUE file not found in image files list");

                            var cuePath = Path.Combine(originalFolderPath, cueFile);

                            // Create target directory
                            if (!await Helper.DirectoryExistsAsync(newPath))
                                await Helper.CreateDirectoryAsync(newPath);

                            // Convert CUE to CCD/IMG/SUB
                            progress.TextContent = $"Converting {item.Name} to CCD...";

                            await Cue2CcdConverter.ConvertAsync(cuePath, newPath);

                            var baseName = Path.GetFileNameWithoutExtension(cueFile);
                            item.FullFolderPath = newPath;
                            item.Work = WorkMode.None;
                            item.SdNumber = folderNumber;
                            item.FileFormat = FileFormat.Uncompressed;
                            item.ImageFiles.Clear();
                            item.ImageFiles.Add(baseName + ".ccd");
                            item.ImageFiles.Add(baseName + ".img");
                            item.ImageFiles.Add(baseName + ".sub");
                            item.CanApplyGDIShrink = false;

                            // Copy name.txt if it exists in original folder
                            var nameFilePath = Path.Combine(originalFolderPath, Constants.NameTextFile);
                            if (await Helper.FileExistsAsync(nameFilePath))
                                await Task.Run(() => File.Copy(nameFilePath, Path.Combine(newPath, Constants.NameTextFile), overwrite: true));

                            UpdateItemLength(item);
                        }
                        else if (item.FileFormat == FileFormat.Chd)
                        {
                            var folderNumber = i + 1;
                            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
                            var originalFolderPath = item.FullFolderPath;

                            // Get the CHD file path
                            if (item.ImageFiles == null || !item.ImageFiles.Any())
                                throw new Exception("Image files list is empty for CHD item");

                            var chdFile = item.ImageFiles.FirstOrDefault(f => f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrEmpty(chdFile))
                                throw new Exception("CHD file not found in image files list");

                            var chdPath = Path.Combine(originalFolderPath, chdFile);

                            // Create target directory
                            if (!await Helper.DirectoryExistsAsync(newPath))
                                await Helper.CreateDirectoryAsync(newPath);

                            if (ChdConverter.IsGdRomChd(chdPath))
                            {
                                // GD-ROM CHD: Convert to GDI format
                                if (EnableGDIShrink && itemsToShrink.Contains(item))
                                {
                                    // Convert to GDI in temp dir, then shrink to SD card
                                    progress.TextContent = $"Decompressing/Shrinking {item.Name}...";

                                    var tempChdDir = Path.Combine(tempdir, $"chd_{folderNumber}");
                                    if (!await Helper.DirectoryExistsAsync(tempChdDir))
                                        await Helper.CreateDirectoryAsync(tempChdDir);

                                    var (success, message) = await ChdConverter.ConvertToGdi(chdPath, tempChdDir);
                                    if (!success)
                                        throw new Exception($"Failed to convert CHD to GDI: {message}");

                                    var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempChdDir);

                                    (success, message) = await GdiShrinker.Shrink(
                                        Path.Combine(tempChdDir, tempGdiItem.ImageFile), newPath);
                                    if (!success)
                                        throw new Exception($"Failed to shrink {tempGdiItem.ImageFile}: {message}");

                                    await Helper.DeleteDirectoryAsync(tempChdDir);

                                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                    item.CanApplyGDIShrink = false;
                                    item.WasShrunk = true;
                                }
                                else
                                {
                                    // Convert CHD directly to GDI on SD card
                                    progress.TextContent = $"Decompressing {item.Name} to GDI...";

                                    var (success, message) = await ChdConverter.ConvertToGdi(chdPath, newPath);
                                    if (!success)
                                        throw new Exception($"Failed to convert CHD to GDI: {message}");

                                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                    item.CanApplyGDIShrink = true;
                                }
                            }
                            else
                            {
                                // CD-ROM CHD: Convert to CUE/BIN, then to CDI
                                progress.TextContent = $"Decompressing {item.Name} to CDI...";

                                var tempChdDir = Path.Combine(tempdir, $"chd_{folderNumber}");
                                if (!await Helper.DirectoryExistsAsync(tempChdDir))
                                    await Helper.CreateDirectoryAsync(tempChdDir);

                                var (cueBinSuccess, cueBinMessage, cuePath) = await ChdConverter.ConvertToCueBin(chdPath, tempChdDir);
                                if (!cueBinSuccess)
                                    throw new Exception($"Failed to convert CHD to CUE/BIN: {cueBinMessage}");

                                var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
                                var cdiOutputPath = Path.Combine(newPath, cdiOutputName);

                                var (cdiSuccess, cdiMessage) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, cdiOutputPath));
                                if (!cdiSuccess)
                                    throw new Exception($"Failed to convert CUE/BIN to CDI: {cdiMessage}");

                                await Helper.DeleteDirectoryAsync(tempChdDir);

                                item.FullFolderPath = newPath;
                                item.Work = WorkMode.None;
                                item.SdNumber = folderNumber;
                                item.FileFormat = FileFormat.Uncompressed;
                                item.ImageFiles.Clear();
                                item.ImageFiles.Add(cdiOutputName);
                                item.CanApplyGDIShrink = false;
                            }

                            // Copy name.txt if it exists in original folder
                            var nameFilePath = Path.Combine(originalFolderPath, Constants.NameTextFile);
                            if (await Helper.FileExistsAsync(nameFilePath))
                                await Task.Run(() => File.Copy(nameFilePath, Path.Combine(newPath, Constants.NameTextFile), overwrite: true));

                            UpdateItemLength(item);

                            // Apply region/VGA patches to converted items
                            if (EnableRegionPatch || EnableVgaPatch)
                            {
                                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu" && item.DiscType == "Game")
                                {
                                    await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                                }
                            }
                        }
                        else//compressed file
                        {
                            var sourceState = item.CreateArchivePreparationCopy();
                            if (EnableGDIShrink && EnableGDIShrinkCompressed && itemsToShrink.Contains(item))
                            {
                                progress.TextContent = $"Decompressing {item.Name}...";

                                shrink = true;

                                // Extract game to temp folder.
                                var folderNumber = i + 1;
                                var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

                                var tempExtractDir = Path.Combine(tempdir, $"ext_{folderNumber}");
                                if (!await Helper.DirectoryExistsAsync(tempExtractDir))
                                    await Helper.CreateDirectoryAsync(tempExtractDir);

                                string selectedExtractedPath;
                                if (!preExtractedPaths.TryGetValue(item, out selectedExtractedPath))
                                    selectedExtractedPath = await ExtractSelectedArchiveAsync(item, tempExtractDir);

                                var gdi = await ImageHelper.CreateGdItemAsync(selectedExtractedPath);

                                // CUE/BIN needs conversion, not shrinking
                                if (gdi.FileFormat == FileFormat.RedumpCueBin)
                                {
                                    // Get the CUE file from extracted content
                                    var cueFile = gdi.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                                    if (string.IsNullOrEmpty(cueFile))
                                        throw new Exception("CUE file not found after extraction");

                                    var cuePath = Path.Combine(tempExtractDir, cueFile);

                                    // Create target directory
                                    if (!await Helper.DirectoryExistsAsync(newPath))
                                        await Helper.CreateDirectoryAsync(newPath);

                                    // Check if this is GD-ROM or CD-ROM CUE/BIN
                                    if (GdiConverter.IsGdRomCue(cuePath))
                                    {
                                        if (EnableGDIShrinkBlackList)
                                        {
                                            if (ignoreShrinkList.Contains(gdi.Ip?.ProductNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                                                shrink = false;
                                        }

                                        if (shrink)
                                        {
                                            progress.TextContent = $"Converting/Shrinking {item.Name}...";

                                            var tempCueDir = Path.Combine(tempdir, $"cue_{folderNumber}");
                                            if (!await Helper.DirectoryExistsAsync(tempCueDir))
                                                await Helper.CreateDirectoryAsync(tempCueDir);

                                            var (success, message) = await GdiConverter.ConvertToGdi(cuePath, tempCueDir);
                                            if (!success)
                                                throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                                            var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempCueDir);

                                            (success, message) = await GdiShrinker.Shrink(
                                                Path.Combine(tempCueDir, tempGdiItem.ImageFile), newPath);
                                            if (!success)
                                                throw new Exception($"Failed to shrink {tempGdiItem.ImageFile}: {message}");

                                            await Helper.DeleteDirectoryAsync(tempCueDir);
                                        }
                                        else
                                        {
                                            progress.TextContent = $"Converting {item.Name} to GDI...";

                                            var (success, message) = await GdiConverter.ConvertToGdi(cuePath, newPath);
                                            if (!success)
                                                throw new Exception($"Failed to convert {cueFile} to GDI: {message}");
                                        }

                                        await Helper.DeleteDirectoryAsync(tempExtractDir);

                                        var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);

                                        item.FullFolderPath = newPath;
                                        item.Work = WorkMode.None;
                                        item.SdNumber = folderNumber;
                                        item.FileFormat = FileFormat.Uncompressed;
                                        item.ImageFiles.Clear();
                                        item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                        item.Ip = gdi.Ip;
                                        if (shrink)
                                        {
                                            item.CanApplyGDIShrink = false;
                                            item.WasShrunk = true;
                                        }
                                    }
                                    else
                                    {
                                        // CD-ROM: Convert to CDI format
                                        progress.TextContent = $"Converting {item.Name} to CDI...";

                                        var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
                                        var cdiOutputPath = Path.Combine(newPath, cdiOutputName);

                                        var (success, message) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, cdiOutputPath));
                                        if (!success)
                                            throw new Exception($"Failed to convert {cueFile} to CDI: {message}");

                                        await Helper.DeleteDirectoryAsync(tempExtractDir);

                                        item.FullFolderPath = newPath;
                                        item.Work = WorkMode.None;
                                        item.SdNumber = folderNumber;
                                        item.FileFormat = FileFormat.Uncompressed;
                                        item.ImageFiles.Clear();
                                        item.ImageFiles.Add(cdiOutputName);
                                        item.Ip = gdi.Ip;
                                    }
                                }
                                else if (gdi.FileFormat == FileFormat.CueBinNonGame)
                                {
                                    // CUE/BIN (non-DC), convert to CCD
                                    var cueFile = gdi.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                                    if (string.IsNullOrEmpty(cueFile))
                                        throw new Exception("CUE file not found after extraction");

                                    var cuePath = Path.Combine(tempExtractDir, cueFile);

                                    if (!await Helper.DirectoryExistsAsync(newPath))
                                        await Helper.CreateDirectoryAsync(newPath);

                                    progress.TextContent = $"Converting {item.Name} to CCD...";

                                    await Cue2CcdConverter.ConvertAsync(cuePath, newPath);

                                    await Helper.DeleteDirectoryAsync(tempExtractDir);

                                    var baseName = Path.GetFileNameWithoutExtension(cueFile);
                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.Add(baseName + ".ccd");
                                    item.ImageFiles.Add(baseName + ".img");
                                    item.ImageFiles.Add(baseName + ".sub");
                                    item.Ip = gdi.Ip;
                                }
                                else if (gdi.FileFormat == FileFormat.Chd)
                                {
                                    // CHD, convert to GDI or CDI
                                    var chdFile = gdi.ImageFiles.FirstOrDefault(f => f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase));
                                    if (string.IsNullOrEmpty(chdFile))
                                        throw new Exception("CHD file not found after extraction");

                                    var extractedChdPath = Path.Combine(tempExtractDir, chdFile);

                                    // Create target directory
                                    if (!await Helper.DirectoryExistsAsync(newPath))
                                        await Helper.CreateDirectoryAsync(newPath);

                                    if (ChdConverter.IsGdRomChd(extractedChdPath))
                                    {
                                        // GD-ROM CHD: Convert to GDI, then optionally shrink
                                        if (EnableGDIShrinkBlackList)
                                        {
                                            if (ignoreShrinkList.Contains(gdi.Ip?.ProductNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                                                shrink = false;
                                        }

                                        if (shrink)
                                        {
                                            progress.TextContent = $"Decompressing/Shrinking {item.Name}...";

                                            // Convert CHD to GDI in temp, then shrink
                                            var tempChdGdiDir = Path.Combine(tempdir, $"chdgdi_{folderNumber}");
                                            if (!await Helper.DirectoryExistsAsync(tempChdGdiDir))
                                                await Helper.CreateDirectoryAsync(tempChdGdiDir);

                                            var (success, message) = await ChdConverter.ConvertToGdi(extractedChdPath, tempChdGdiDir);
                                            if (!success)
                                                throw new Exception($"Failed to convert CHD to GDI: {message}");

                                            var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempChdGdiDir);

                                            (success, message) = await GdiShrinker.Shrink(
                                                Path.Combine(tempChdGdiDir, tempGdiItem.ImageFile), newPath);
                                            if (!success)
                                                throw new Exception($"Failed to shrink {tempGdiItem.ImageFile}: {message}");

                                            await Helper.DeleteDirectoryAsync(tempChdGdiDir);
                                        }
                                        else
                                        {
                                            progress.TextContent = $"Decompressing {item.Name} to GDI...";

                                            var (success, message) = await ChdConverter.ConvertToGdi(extractedChdPath, newPath);
                                            if (!success)
                                                throw new Exception($"Failed to convert CHD to GDI: {message}");
                                        }

                                        await Helper.DeleteDirectoryAsync(tempExtractDir);

                                        var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                        item.FullFolderPath = newPath;
                                        item.Work = WorkMode.None;
                                        item.SdNumber = folderNumber;
                                        item.FileFormat = FileFormat.Uncompressed;
                                        item.ImageFiles.Clear();
                                        item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                        item.Ip = gdi.Ip;
                                        if (shrink)
                                        {
                                            item.CanApplyGDIShrink = false;
                                            item.WasShrunk = true;
                                        }
                                    }
                                    else
                                    {
                                        // CD-ROM CHD: Convert to CUE/BIN then CDI
                                        progress.TextContent = $"Decompressing {item.Name} to CDI...";

                                        var tempCueBinDir = Path.Combine(tempdir, $"chdcue_{folderNumber}");
                                        if (!await Helper.DirectoryExistsAsync(tempCueBinDir))
                                            await Helper.CreateDirectoryAsync(tempCueBinDir);

                                        var (cueBinSuccess, cueBinMessage, cuePath) = await ChdConverter.ConvertToCueBin(extractedChdPath, tempCueBinDir);
                                        if (!cueBinSuccess)
                                            throw new Exception($"Failed to convert CHD to CUE/BIN: {cueBinMessage}");

                                        var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
                                        var cdiOutputPath = Path.Combine(newPath, cdiOutputName);

                                        var (cdiSuccess, cdiMessage) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, cdiOutputPath));
                                        if (!cdiSuccess)
                                            throw new Exception($"Failed to convert CUE/BIN to CDI: {cdiMessage}");

                                        await Helper.DeleteDirectoryAsync(tempCueBinDir);
                                        await Helper.DeleteDirectoryAsync(tempExtractDir);

                                        item.FullFolderPath = newPath;
                                        item.Work = WorkMode.None;
                                        item.SdNumber = folderNumber;
                                        item.FileFormat = FileFormat.Uncompressed;
                                        item.ImageFiles.Clear();
                                        item.ImageFiles.Add(cdiOutputName);
                                        item.Ip = gdi.Ip;
                                    }
                                }
                                else
                                {
                                    // Normal GDI/CDI extraction with optional shrinking
                                    if (EnableGDIShrinkBlackList)//now with the game uncompressed we can check the blacklist
                                    {
                                        if (ignoreShrinkList.Contains(gdi.Ip?.ProductNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                                            shrink = false;
                                    }

                                    if (shrink)
                                    {
                                        progress.TextContent = $"Shrinking {item.Name}...";

                                        var (success, message) = await GdiShrinker.Shrink(
                                            Path.Combine(tempExtractDir, gdi.ImageFile), newPath);
                                        if (!success)
                                            throw new Exception($"Failed to shrink {gdi.ImageFile}: {message}");

                                        // The non-shrink branch copies the whole extracted folder,
                                        // sidecar text files included. Carry them into the shrink
                                        // output so the recognize pass below still sees them.
                                        foreach (var sidecarFile in Constants.AllSidecarTextFiles)
                                        {
                                            var sidecarSource = Path.Combine(tempExtractDir, sidecarFile);
                                            if (await Helper.FileExistsAsync(sidecarSource))
                                                await Task.Run(() => File.Copy(sidecarSource, Path.Combine(newPath, sidecarFile), true));
                                        }

                                        //get the new filenames
                                        gdi = await ImageHelper.CreateGdItemAsync(newPath);
                                    }
                                    else
                                    {
                                        progress.TextContent = $"Copying {item.Name}...";
                                        await Helper.CopyDirectoryAsync(tempExtractDir, newPath);
                                    }

                                    await Helper.DeleteDirectoryAsync(tempExtractDir);

                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdi.ImageFiles);
                                    item.Ip = gdi.Ip;
                                    if (shrink)
                                    {
                                        item.CanApplyGDIShrink = false;
                                        item.WasShrunk = true;
                                    }
                                }

                                bool wasShrunk = item.WasShrunk;
                                bool canApplyShrink = item.CanApplyGDIShrink;
                                await PublishExtractedArchiveState(
                                    item,
                                    sourceState,
                                    gdi,
                                    newPath,
                                    folderNumber,
                                    wasShrunk,
                                    canApplyShrink);

                                // Apply region/VGA patches to newly extracted items
                                if (EnableRegionPatch || EnableVgaPatch)
                                {
                                    if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu" && item.DiscType == "Game")
                                    {
                                        await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                                    }
                                }
                            }
                            else// if not shrinking, can extract directly to card
                            {
                                progress.TextContent = $"Decompressing {item.Name}...";
                                await Uncompress(item, i + 1, tempdir, progress, preExtractedPaths);//+ ammountToIncrement
                            }

                        }


                        progress.ProcessedItems++;

                        // User closed window.
                        if (!progress.IsVisible)
                            break;
                    }
                }
                progress.TextContent = "Done!";
                progress.AllowClose();
                progress.Close();
            }
            catch (IOException ioEx) when (Helper.IsDiskFullException(ioEx))
            {
                progress.AllowClose();
                progress.Close();

                // Find the incomplete folder path (current item being processed)
                string incompletePath = null;
                for (int i = 0; i < ItemList.Count; i++)
                {
                    var item = ItemList[i];
                    if (item.Work == WorkMode.New)
                    {
                        incompletePath = Path.Combine(sdPath, FormatFolderNumber(i + 1));
                        break;
                    }
                }

                await Helper.DependencyManager.ShowDiskFullError(
                    $"Failed while copying files to the SD card.\n\nError: {ioEx.Message}",
                    incompletePath);
                throw;
            }
            catch (Exception ex)
            {
                progress.TextContent = $"{progress.TextContent}\nERROR: {ex.Message}";
                progress.AllowClose();  // Enable closing so user can dismiss the error
                throw;
            }
            finally
            {
                do { await Task.Delay(200); } while (progress.IsVisible);

                progress.AllowClose();
                progress.Close();

                if (progress.ProcessedItems != total)
                    throw new Exception("Operation canceled.\nThere might be unused folders/files on the SD card.");
            }
        }

        public async ValueTask SortList()
        {
            if (ItemList.Count == 0)
                return;

            try
            {
                await LoadIpAll();
            }
            catch (ProgressWindowClosedException)
            {
                return;
            }

            // Capture order before sort for undo
            var oldOrder = new List<GdItem>(ItemList);

            var sortedlist = new List<GdItem>(ItemList.Count);
            var menuItem = ItemList.FirstOrDefault(x => x.IsMenuItem);
            if (menuItem != null)
            {
                sortedlist.Add(menuItem);
                ItemList.Remove(menuItem);
            }

            foreach (var item in ItemList
                .OrderByDescending(x => !string.IsNullOrEmpty(x.Folder))
                .ThenBy(x => x.Folder ?? "")
                .ThenBy(x => x.Name)
                .ThenBy(x => x.Ip?.Disc ?? "1/1"))
                sortedlist.Add(item);

            ItemList.Clear();
            foreach (var item in sortedlist)
                ItemList.Add(item);

            // Record undo operation
            UndoManager.RecordChange(new ListReorderOperation("Sort List")
            {
                ItemList = ItemList,
                OldOrder = oldOrder,
                NewOrder = new List<GdItem>(ItemList)
            });
        }

        public void InitializeKnownFolders()
        {
            KnownFolders.Clear();

            foreach (var item in ItemList)
            {
                if (!string.IsNullOrWhiteSpace(item.Folder) && !KnownFolders.Contains(item.Folder))
                    KnownFolders.Add(item.Folder);

                foreach (var altFolder in item.AlternativeFolders)
                {
                    if (!string.IsNullOrWhiteSpace(altFolder) && !KnownFolders.Contains(altFolder))
                        KnownFolders.Add(altFolder);
                }
            }

            var sorted = KnownFolders.OrderBy(x => x).ToList();
            KnownFolders.Clear();
            foreach (var folder in sorted)
                KnownFolders.Add(folder);
        }

        public Dictionary<string, int> GetFolderCounts()
        {
            var folderCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var item in ItemList)
            {
                // Count once per item using a set of all unique folder paths.
                var allFolders = new HashSet<string>(StringComparer.Ordinal);

                if (!string.IsNullOrWhiteSpace(item.Folder))
                    allFolders.Add(item.Folder);

                foreach (var altFolder in item.AlternativeFolders)
                {
                    if (!string.IsNullOrWhiteSpace(altFolder))
                        allFolders.Add(altFolder);
                }

                foreach (var folder in allFolders)
                {
                    if (folderCounts.ContainsKey(folder))
                        folderCounts[folder]++;
                    else
                        folderCounts[folder] = 1;
                }
            }

            return folderCounts;
        }

        // Moves folder artwork entries along with renamed folders. Uses the same
        // exact match plus prefix rewrite rules as ApplyFolderMappings so art always
        // lands on the path the games ended up with. Returns the rekeys performed
        // so the caller can record them for undo.
        public List<(string OldPath, string NewPath)> RekeyFolderArtForMappings(Dictionary<string, string> mappings)
        {
            var rekeyed = new List<(string OldPath, string NewPath)>();

            if (FolderArtDat == null || mappings == null || mappings.Count == 0)
                return rekeyed;

            foreach (var key in FolderArtDat.GetAllKeys())
            {
                var oldPath = FolderArtDat.GetPathForKey(key);
                if (string.IsNullOrEmpty(oldPath))
                    continue;

                string newPath = null;

                if (mappings.TryGetValue(oldPath, out var direct))
                {
                    newPath = direct;
                }
                else
                {
                    foreach (var mapping in mappings)
                    {
                        if (oldPath.StartsWith(mapping.Key + "\\", StringComparison.Ordinal))
                        {
                            newPath = mapping.Value + oldPath.Substring(mapping.Key.Length);
                            break;
                        }
                    }
                }

                if (newPath != null && newPath != oldPath)
                    rekeyed.Add((oldPath, newPath));
            }

            FolderArtDat.ApplyRekeys(rekeyed);
            return rekeyed;
        }

        /// <returns>Tuple of (items updated, alt folder conflicts removed).</returns>
        public (int updatedCount, int conflictsRemoved) ApplyFolderMappings(Dictionary<string, string> mappings)
        {
            if (mappings == null || mappings.Count == 0)
                return (0, 0);

            int updatedCount = 0;

            foreach (var item in ItemList)
            {
                if (!string.IsNullOrWhiteSpace(item.Folder))
                {
                    if (mappings.ContainsKey(item.Folder))
                    {
                        item.Folder = mappings[item.Folder];
                        updatedCount++;
                    }
                    else
                    {
                        foreach (var mapping in mappings)
                        {
                            if (item.Folder.StartsWith(mapping.Key + "\\", StringComparison.Ordinal))
                            {
                                item.Folder = mapping.Value + item.Folder.Substring(mapping.Key.Length);
                                updatedCount++;
                                break;
                            }
                        }
                    }
                }

                // Remap alt folders.
                for (int i = 0; i < item.AlternativeFolders.Count; i++)
                {
                    var altFolder = item.AlternativeFolders[i];
                    if (string.IsNullOrWhiteSpace(altFolder)) continue;

                    if (mappings.ContainsKey(altFolder))
                    {
                        item.AlternativeFolders[i] = mappings[altFolder];
                        updatedCount++;
                    }
                    else
                    {
                        foreach (var mapping in mappings)
                        {
                            if (altFolder.StartsWith(mapping.Key + "\\", StringComparison.Ordinal))
                            {
                                item.AlternativeFolders[i] = mapping.Value + altFolder.Substring(mapping.Key.Length);
                                updatedCount++;
                                break;
                            }
                        }
                    }
                }
            }

            // Post-apply conflict scrub.
            int conflictsRemoved = 0;
            foreach (var item in ItemList)
            {
                if (item.AlternativeFolders.Count > 0)
                {
                    // Remove alt folders that now match the primary folder.
                    if (!string.IsNullOrWhiteSpace(item.Folder))
                        conflictsRemoved += item.AlternativeFolders.RemoveAll(af => af == item.Folder);

                    // Deduplicate alt folders.
                    var distinct = item.AlternativeFolders.Distinct(StringComparer.Ordinal).ToList();
                    if (distinct.Count < item.AlternativeFolders.Count)
                    {
                        conflictsRemoved += item.AlternativeFolders.Count - distinct.Count;
                        item.AlternativeFolders = distinct;
                    }
                }
            }

            InitializeKnownFolders();

            return (updatedCount, conflictsRemoved);
        }

        private async Task Uncompress(GdItem item, int folderNumber, string tempdir, IProgressWindow progress = null, Dictionary<GdItem, string> preExtractedPaths = null)
        {
            var sourceState = item.CreateArchivePreparationCopy();
            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

            // Extract to temp folder first, not directly to SD card
            var tempExtractDir = Path.Combine(tempdir, $"ext_{folderNumber}");
            if (!await Helper.DirectoryExistsAsync(tempExtractDir))
                await Helper.CreateDirectoryAsync(tempExtractDir);

            string selectedExtractedPath;
            if (preExtractedPaths == null ||
                !preExtractedPaths.TryGetValue(item, out selectedExtractedPath))
                selectedExtractedPath = await ExtractSelectedArchiveAsync(item, tempExtractDir);

            var extracted = await RecognizeImageAsync(selectedExtractedPath);

            // Check if extracted content is CUE/BIN that needs conversion
            if (extracted.FileFormat == FileFormat.RedumpCueBin)
            {
                // Get the CUE file from extracted content
                var cueFile = extracted.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(cueFile))
                    throw new Exception("CUE file not found after extraction");

                var cuePath = Path.Combine(tempExtractDir, cueFile);

                // Create target directory on SD card
                if (!await Helper.DirectoryExistsAsync(newPath))
                    await Helper.CreateDirectoryAsync(newPath);

                // Check if this is GD-ROM or CD-ROM CUE/BIN
                if (GdiConverter.IsGdRomCue(cuePath))
                {
                    // GD-ROM: Convert to GDI format
                    if (progress != null)
                        progress.TextContent = $"Converting {item.Name} to GDI...";

                    var (success, message) = await GdiConverter.ConvertToGdi(cuePath, newPath);
                    if (!success)
                        throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                    // Get the converted GDI item info
                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);

                    item.ImageFiles.Clear();
                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                    item.Ip = extracted.Ip;
                }
                else
                {
                    // CD-ROM: Convert to CDI format
                    if (progress != null)
                        progress.TextContent = $"Converting {item.Name} to CDI...";

                    var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
                    var cdiOutputPath = Path.Combine(newPath, cdiOutputName);

                    var (success, message) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, cdiOutputPath));
                    if (!success)
                        throw new Exception($"Failed to convert {cueFile} to CDI: {message}");

                    item.ImageFiles.Clear();
                    item.ImageFiles.Add(cdiOutputName);
                    item.Ip = extracted.Ip;
                }
            }
            else if (extracted.FileFormat == FileFormat.CueBinNonGame)
            {
                // CUE/BIN (non-DC), convert to CCD
                var cueFile = extracted.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(cueFile))
                    throw new Exception("CUE file not found after extraction");

                var cuePath = Path.Combine(tempExtractDir, cueFile);

                if (!await Helper.DirectoryExistsAsync(newPath))
                    await Helper.CreateDirectoryAsync(newPath);

                if (progress != null)
                    progress.TextContent = $"Converting {item.Name} to CCD...";

                await Cue2CcdConverter.ConvertAsync(cuePath, newPath);

                var baseName = Path.GetFileNameWithoutExtension(cueFile);
                item.ImageFiles.Clear();
                item.ImageFiles.Add(baseName + ".ccd");
                item.ImageFiles.Add(baseName + ".img");
                item.ImageFiles.Add(baseName + ".sub");
                item.Ip = extracted.Ip;
            }
            else if (extracted.FileFormat == FileFormat.Chd)
            {
                // CHD, convert to GDI or CDI
                var chdFile = extracted.ImageFiles.FirstOrDefault(f => f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(chdFile))
                    throw new Exception("CHD file not found after extraction");

                var extractedChdPath = Path.Combine(tempExtractDir, chdFile);

                // Create target directory on SD card
                if (!await Helper.DirectoryExistsAsync(newPath))
                    await Helper.CreateDirectoryAsync(newPath);

                if (ChdConverter.IsGdRomChd(extractedChdPath))
                {
                    // GD-ROM CHD: Convert to GDI format
                    if (progress != null)
                        progress.TextContent = $"Decompressing {item.Name} to GDI...";

                    var (success, message) = await ChdConverter.ConvertToGdi(extractedChdPath, newPath);
                    if (!success)
                        throw new Exception($"Failed to convert CHD to GDI: {message}");

                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                    item.ImageFiles.Clear();
                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                    item.Ip = extracted.Ip;
                }
                else
                {
                    // CD-ROM CHD: Convert to CUE/BIN then CDI
                    if (progress != null)
                        progress.TextContent = $"Decompressing {item.Name} to CDI...";

                    var tempCueBinDir = Path.Combine(tempdir, $"chdcue_{folderNumber}");
                    if (!await Helper.DirectoryExistsAsync(tempCueBinDir))
                        await Helper.CreateDirectoryAsync(tempCueBinDir);

                    var (cueBinSuccess, cueBinMessage, cuePath) = await ChdConverter.ConvertToCueBin(extractedChdPath, tempCueBinDir);
                    if (!cueBinSuccess)
                        throw new Exception($"Failed to convert CHD to CUE/BIN: {cueBinMessage}");

                    var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
                    var cdiOutputPath = Path.Combine(newPath, cdiOutputName);

                    var (cdiSuccess, cdiMessage) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, cdiOutputPath));
                    if (!cdiSuccess)
                        throw new Exception($"Failed to convert CUE/BIN to CDI: {cdiMessage}");

                    await Helper.DeleteDirectoryAsync(tempCueBinDir);

                    item.ImageFiles.Clear();
                    item.ImageFiles.Add(cdiOutputName);
                    item.Ip = extracted.Ip;
                }
            }
            else
            {
                // Normal extraction, copy to SD card
                if (!await Helper.DirectoryExistsAsync(newPath))
                    await Helper.CreateDirectoryAsync(newPath);

                await Helper.CopyDirectoryAsync(tempExtractDir, newPath);

                item.ImageFiles.Clear();
                item.ImageFiles.AddRange(extracted.ImageFiles);
                item.Ip = extracted.Ip;
            }

            // Clean up temp folder
            await Helper.DeleteDirectoryAsync(tempExtractDir);

            await PublishExtractedArchiveState(
                item,
                sourceState,
                extracted,
                newPath,
                folderNumber,
                item.WasShrunk,
                item.CanApplyGDIShrink);

            // Apply region/VGA patches to newly extracted items
            if (EnableRegionPatch || EnableVgaPatch)
            {
                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu" && item.DiscType == "Game")
                {
                    await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                }
            }
        }

        private static void ValidateNormalOutput(GdItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ImageFile))
                throw new InvalidDataException("The extracted archive image was not recognized.");
            string extension = Path.GetExtension(item.ImageFile);
            if (!extension.Equals(".gdi", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".cdi", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".ccd", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".mds", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("The extracted archive image requires transformation.");
        }

        private Task<GdItem> RecognizeImageAsync(string path)
        {
            return ArchiveImageRecognizerOverride == null
                ? ImageHelper.CreateGdItemAsync(path)
                : ArchiveImageRecognizerOverride(path);
        }

        private static GdItem CreatePreparedState(
            GdItem sourceItem,
            GdItem authoritative,
            GdItem output,
            string outputRoot)
        {
            var serialState = sourceItem.CaptureArchiveMetadataFieldState(
                ArchiveMetadataField.Serial);
            var typeState = sourceItem.CaptureArchiveMetadataFieldState(
                ArchiveMetadataField.Type);
            var discState = sourceItem.CaptureArchiveMetadataFieldState(
                ArchiveMetadataField.Disc);
            var regionState = sourceItem.CaptureArchiveMetadataFieldState(
                ArchiveMetadataField.Region);
            var prepared = sourceItem.CreateArchivePreparationCopy();
            prepared.Ip = authoritative.Ip;
            prepared.ImageRegion = authoritative.ImageRegion;
            if (sourceItem.IsArchiveMetadataPending &&
                !sourceItem.HasUserEditedCompressedTitle)
                prepared.Name = authoritative.Name;
            prepared.ImageFiles.Clear();
            prepared.ImageFiles.AddRange(output.ImageFiles);
            prepared.DiscType = authoritative.DiscType;
            prepared.ProductNumber = authoritative.ProductNumber;
            prepared.FileFormat = output.FileFormat;
            prepared.CanApplyGDIShrink = output.CanApplyGDIShrink;
            prepared.WasShrunk = output.WasShrunk;
            prepared.Length = output.Length;
            prepared.FullFolderPath = outputRoot;
            if (serialState.IsManual)
                prepared.RestoreArchiveMetadataFieldState(
                    ArchiveMetadataField.Serial,
                    serialState);
            if (typeState.IsManual)
                prepared.RestoreArchiveMetadataFieldState(
                    ArchiveMetadataField.Type,
                    typeState);
            if (discState.IsManual)
                prepared.RestoreArchiveMetadataFieldState(
                    ArchiveMetadataField.Disc,
                    discState);
            if (regionState.IsManual)
                prepared.RestoreArchiveMetadataFieldState(
                    ArchiveMetadataField.Region,
                    regionState);
            prepared.IsArchiveMetadataPending = false;
            return prepared;
        }

        private async Task PublishExtractedArchiveState(
            GdItem item,
            GdItem sourceState,
            GdItem authoritativeMetadata,
            string outputPath,
            int folderNumber,
            bool wasShrunk,
            bool canApplyShrink)
        {
            GdItem output = await RecognizeImageAsync(outputPath);
            ValidateNormalOutput(output);
            GdItem resolved = CreatePreparedState(
                sourceState,
                authoritativeMetadata,
                output,
                outputPath);
            resolved.WasShrunk = wasShrunk;
            resolved.CanApplyGDIShrink = wasShrunk
                ? false
                : canApplyShrink || output.CanApplyGDIShrink;
            bool backfillBlankSerial = !sourceState
                .CaptureArchiveMetadataFieldState(ArchiveMetadataField.Serial)
                .IsManual;
            item.PublishPreparedArchiveState(
                resolved,
                outputPath,
                folderNumber,
                backfillBlankSerial);
        }

        private async Task<string> ExtractSelectedArchiveAsync(GdItem item, string extractRoot)
        {
            string archivePath = Path.Combine(item.FullFolderPath, item.ImageFile);
            if (item.SelectedArchiveEntry == null)
            {
                await Task.Run(() =>
                    Helper.DependencyManager.ExtractArchive(archivePath, extractRoot));
                return extractRoot;
            }

            string selectedPath = await Task.Run(() =>
                Helper.DependencyManager.ExtractArchiveForEntry(
                    archivePath,
                    extractRoot,
                    item.SelectedArchiveEntry));
            return selectedPath;
        }

        //todo implement
        internal static void UpdateItemLength(GdItem item)
        {
            item.Length = ByteSizeLib.ByteSize.FromBytes(item.ImageFiles.Sum(x => new FileInfo(Path.Combine(item.FullFolderPath, x)).Length));
        }

        /// <summary>
        /// Classifies top-level inputs without opening archives or retaining the choice.
        /// </summary>
        public async ValueTask<ArchiveAddMode> ChooseArchiveAddModeAsync(
            IEnumerable<string> paths)
        {
            int compressedCount = 0;
            foreach (var path in paths ?? Array.Empty<string>())
            {
                try
                {
                    var attributes = await Helper.GetAttributesAsync(path);
                    if (!attributes.HasFlag(FileAttributes.Directory))
                    {
                        if (Helper.CompressedFileExpression(path))
                            compressedCount++;
                        continue;
                    }

                    var immediateFiles = await Helper.GetFilesAsync(path);
                    bool containsDirectImage = immediateFiles.Any(file =>
                        supportedImageFormats.Any(format => format.Equals(
                            Path.GetExtension(file),
                            StringComparison.OrdinalIgnoreCase)));
                    if (!containsDirectImage &&
                        immediateFiles.Any(Helper.CompressedFileExpression))
                    {
                        compressedCount++;
                    }
                }
                catch
                {
                    // Construction reports errors for individual paths.
                }
            }

            if (compressedCount < 2)
                return ArchiveAddMode.ParseNow;

            return await Helper.DependencyManager.ShowArchiveAddModeDialog(
                compressedCount);
        }

        /// <summary>
        /// Adds top-level inputs at the requested position while isolating per-item failures.
        /// </summary>
        public async Task<AddGamesResult> AddGames(
            string[] files,
            int insertionIndex = -1,
            AddGamesUndoProfile undoProfile = AddGamesUndoProfile.Picker,
            IProgress<string> progress = null)
        {
            var result = new AddGamesResult();

            result.Mode = await ChooseArchiveAddModeAsync(files);
            if (result.Mode == ArchiveAddMode.Cancel)
                return result;

            int nextIndex = insertionIndex < 0
                ? ItemList.Count
                : Math.Max(0, Math.Min(insertionIndex, ItemList.Count));

            if (files != null)
            {
                foreach (var item in files)
                {
                    progress?.Report($"Adding {Path.GetFileName(item)}...");

                    try
                    {
                        var gdItem = await ImageHelper.CreateGdItemAsync(item, result.Mode);
                        await ArchiveSelectionWarning.ShowIfNeededAsync(gdItem);
                        int index = Math.Min(nextIndex, ItemList.Count);
                        ItemList.Insert(index, gdItem);
                        result.AddedItems.Add((gdItem, index));
                        nextIndex = index + 1;
                    }
                    catch (UnsupportedDiscFormatException)
                    {
                        result.UnsupportedRedumpGdi.Add(Path.GetFileName(item));
                    }
                    catch (Exception ex)
                    {
                        result.Invalid.Add(item);
                        result.InvalidDetails.Add((item, ex.Message));
                    }
                }
            }

            // Record undo operation if any items were added
            bool shouldRecordUndo = result.AddedItems.Count > 0 &&
                (undoProfile != AddGamesUndoProfile.WpfExternalDrop ||
                    result.Invalid.Count == 0);
            if (shouldRecordUndo)
            {
                var undoOp = new MultiItemAddOperation { ItemList = ItemList };
                undoOp.Items.AddRange(result.AddedItems);
                UndoManager.RecordChange(undoOp);
            }

            return result;
        }

        public bool SearchInItem(GdItem item, string text)
        {
            // Search in item name (title)
            if (item.Name?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) >= 0)
                return true;

            // Search in serial number
            if (item.ProductNumber?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) >= 0)
                return true;

            // Search in IP.BIN name (if available)
            if (item.Ip?.Name?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) >= 0)
                return true;

            return false;
        }

        private async Task<PatchResult> PatchItemAsync(GdItem item, bool patchRegion, bool patchVga)
        {
            return await PatchItemAsync(
                item,
                patchRegion,
                patchVga,
                saveManualRegionItems,
                () => savePatchChangedFlags = true,
                failure => savePatchFailures.Add(failure));
        }

        private async Task<PatchResult> PatchItemAsync(
            GdItem item,
            bool patchRegion,
            bool patchVga,
            ISet<GdItem> manualRegionItems,
            Action markChanged,
            Action<string> addFailure)
        {
            // Manual region edits win over the blanket region-free option.
            var pendingRegion = item.PendingRegionChange;
            var targetRegion = pendingRegion;
            if (targetRegion == null && patchRegion && !manualRegionItems.Contains(item))
                targetRegion = "JUE";

            if (targetRegion == null && !patchVga)
                return new PatchResult { Success = true };

            if (item.DiscType != "Game")
                return new PatchResult { Success = true };

            var imagePath = Path.Combine(item.FullFolderPath, item.ImageFile);

            if (!RegionPatcher.CanPatch(imagePath))
            {
                if (pendingRegion != null)
                    RevertPendingRegion(
                        item,
                        "Format not supported for patching",
                        markChanged,
                        addFailure);
                return new PatchResult { Success = true, Details = { "Format not supported for patching" } };
            }

            var result = await RegionPatcher.PatchImageAsync(imagePath, targetRegion, patchVga);

            // Region edit didn't take effect, put the old value back.
            if (pendingRegion != null && (!result.Success || result.IpBinHeaderCount == 0))
            {
                RevertPendingRegion(
                    item,
                    result.Success
                        ? "No IP.BIN header found in image"
                        : (result.ErrorMessage ?? "Patch failed"),
                    markChanged,
                    addFailure);
                return result;
            }

            // Update in-memory Ip and cached region.txt to reflect the patched disc
            if (result.Success && targetRegion != null && result.IpBinHeaderCount > 0 && item.Ip != null)
            {
                if (GdItem.NormalizeRegion(item.Ip.Region) != targetRegion)
                    markChanged();

                item.Region = targetRegion;
                item.ImageRegion = targetRegion;

                if (pendingRegion != null)
                    manualRegionItems.Add(item);

                var regionPath = Path.Combine(item.FullFolderPath, Constants.RegionTextFile);
                await Helper.WriteTextFileAsync(regionPath, targetRegion);
            }

            if (result.Success && patchVga && result.VgaPatchCount > 0 && item.Ip != null)
            {
                if (!item.Ip.Vga)
                    markChanged();

                item.Ip.Vga = true;

                var vgaPath = Path.Combine(item.FullFolderPath, Constants.VgaTextFile);
                await Helper.WriteTextFileAsync(vgaPath, "1");
            }

            return result;
        }

        // Restore the region from the image and keep the reason for the warning shown after save.
        private void RevertPendingRegion(GdItem item, string reason)
        {
            RevertPendingRegion(
                item,
                reason,
                () => savePatchChangedFlags = true,
                failure => savePatchFailures.Add(failure));
        }

        private static void RevertPendingRegion(
            GdItem item,
            string reason,
            Action markChanged,
            Action<string> addFailure)
        {
            item.Region = item.ImageRegion;
            addFailure($"{item.Name}: {reason}");
            markChanged();
        }

        // Catches edits the patch pass skipped, like a canceled progress window or a disc type change.
        private void RevertSkippedRegionEdits()
        {
            foreach (var item in ItemList.Where(x => x.PendingRegionChange != null).ToList())
                RevertPendingRegion(item, item.DiscType != "Game" ? "Disc type is not Game" : "Patching was cancelled");
        }

        // Cached serials are cleaned and translated, so they can never match the
        // blacklist, which stores raw IP.BIN values. Read the serial back from each
        // disc image instead. Unreadable images stay in the list.
        private static async Task<List<GdItem>> FilterBlacklistedAsync(List<GdItem> items, List<string> blacklist)
        {
            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TextContent = "Scanning for shrink-compatible games...";

            do { await Task.Delay(50); } while (!progress.IsInitialized);

            try
            {
                var allowed = new List<GdItem>();
                foreach (var item in items)
                {
                    IpBin ip = null;
                    try
                    {
                        ip = await ImageHelper.GetIpBinFromImage(Path.Combine(item.FullFolderPath, item.ImageFile));
                    }
                    catch { }

                    if (ip != null)
                    {
                        if (ip.Name == "GDMENU" || ip.Name == "openMenu")
                            continue;
                        if (blacklist.Contains(ip.ProductNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                            continue;
                    }

                    allowed.Add(item);
                }
                return allowed;
            }
            finally
            {
                progress.AllowClose();
                progress.Close();
            }
        }

        private async Task ShrinkExistingItemsAsync(string tempDirectory)
        {
            // Load blacklist if enabled
            var blacklist = new List<string>();
            if (EnableGDIShrinkBlackList)
            {
                try
                {
                    foreach (var line in File.ReadAllLines(Path.Combine(currentAppPath, Constants.GdiShrinkBlacklistFile)))
                    {
                        var split = line.Split(';');
                        if (split.Length > 2 && !string.IsNullOrWhiteSpace(split[1]))
                            blacklist.Add(split[1].Trim());
                    }
                }
                catch { }
            }

            // Get items that can be shrunk: on SD card, not new, not the menu, is GDI,
            // can apply shrink, is Game type. The menu checks avoid Ip.Name, which
            // stays null for lazy loaded items until a metadata scan runs.
            var itemsToShrink = ItemList.Where(x =>
                x.SdNumber > 1 &&
                x.Work != WorkMode.New &&
                !x.IsMenuItem &&
                x.Name != "GDMENU" &&
                x.Name != "openMenu" &&
                x.CanApplyGDIShrink &&
                !x.WasShrunk &&
                x.FileFormat == FileFormat.Uncompressed &&
                x.DiscType == "Game").ToList();

            if (itemsToShrink.Count > 0 && blacklist.Count > 0)
                itemsToShrink = await FilterBlacklistedAsync(itemsToShrink, blacklist);

            if (itemsToShrink.Count == 0)
                return;

            // Show dialog to let user select which items to shrink
            var selected = await Helper.DependencyManager.GdiShrinkWindowShowDialog(itemsToShrink, "GDI Shrink Selector for Existing Games");
            if (selected == null || selected.Length == 0)
                return;
            itemsToShrink = selected.ToList();

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = itemsToShrink.Count;
            progress.TextContent = "Shrinking existing disc images...";

            do { await Task.Delay(50); } while (!progress.IsInitialized);

            try
            {
                foreach (var item in itemsToShrink)
                {
                    progress.TextContent = $"Shrinking {item.Name}...";

                    // Create temp output folder
                    var tempOutputDir = Path.Combine(tempDirectory, $"shrink_{item.SdNumber}");
                    var backupDir = item.FullFolderPath + "_backup";

                    if (await Helper.DirectoryExistsAsync(tempOutputDir))
                        await Helper.DeleteDirectoryAsync(tempOutputDir);
                    await Helper.CreateDirectoryAsync(tempOutputDir);

                    try
                    {
                        var (success, _) = await GdiShrinker.Shrink(
                            Path.Combine(item.FullFolderPath, item.ImageFile), tempOutputDir);
                        if (!success)
                        {
                            // Shrink failed, clean up and continue
                            await Helper.DeleteDirectoryAsync(tempOutputDir);
                            progress.ProcessedItems++;
                            continue;
                        }

                        // Get the new shrunk GDI info and verify output
                        var shrunkGdi = await ImageHelper.CreateGdItemAsync(tempOutputDir);
                        var shrunkFiles = Directory.GetFiles(tempOutputDir);
                        if (shrunkFiles.Length == 0)
                        {
                            await Helper.DeleteDirectoryAsync(tempOutputDir);
                            progress.ProcessedItems++;
                            continue;
                        }

                        // Safely replace: rename original folder to backup first
                        if (await Helper.DirectoryExistsAsync(backupDir))
                            await Helper.DeleteDirectoryAsync(backupDir);
                        await Helper.MoveDirectoryAsync(item.FullFolderPath, backupDir);

                        // Create new folder and move shrunk files
                        await Helper.CreateDirectoryAsync(item.FullFolderPath);
                        foreach (var file in shrunkFiles)
                        {
                            var destPath = Path.Combine(item.FullFolderPath, Path.GetFileName(file));
                            await Helper.MoveFileAsync(file, destPath);
                        }

                        // Done, clean up.
                        await Helper.DeleteDirectoryAsync(backupDir);
                        await Helper.DeleteDirectoryAsync(tempOutputDir);

                        // Update item's image files
                        item.ImageFiles.Clear();
                        item.ImageFiles.AddRange(shrunkGdi.ImageFiles);

                        // Update item length
                        UpdateItemLength(item);

                        item.CanApplyGDIShrink = false;
                        item.WasShrunk = true;
                    }
                    catch
                    {
                        // Try to restore from backup if original folder is gone
                        if (await Helper.DirectoryExistsAsync(backupDir))
                        {
                            if (!await Helper.DirectoryExistsAsync(item.FullFolderPath))
                            {
                                await Helper.MoveDirectoryAsync(backupDir, item.FullFolderPath);
                            }
                            else
                            {
                                await Helper.DeleteDirectoryAsync(backupDir);
                            }
                        }

                        // Clean up temp folder
                        if (await Helper.DirectoryExistsAsync(tempOutputDir))
                            await Helper.DeleteDirectoryAsync(tempOutputDir);
                    }

                    progress.ProcessedItems++;

                    if (!progress.IsVisible)
                        break;
                }
            }
            finally
            {
                progress.AllowClose();
                progress.Close();
            }
        }

        private async Task PatchExistingItemsAsync()
        {
            var itemsToPatch = ItemList.Where(x =>
                x.SdNumber > 0 &&
                x.Work != WorkMode.New &&
                x.Ip?.Name != "GDMENU" &&
                x.Ip?.Name != "openMenu" &&
                x.FileFormat == FileFormat.Uncompressed &&
                x.DiscType == "Game" &&
                (EnableRegionPatchExisting || EnableVgaPatchExisting || x.PendingRegionChange != null)).ToList();

            if (itemsToPatch.Count == 0)
                return;

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = itemsToPatch.Count;
            progress.TextContent = "Patching existing disc images...";

            do { await Task.Delay(50); } while (!progress.IsInitialized);

            try
            {
                foreach (var item in itemsToPatch)
                {
                    progress.TextContent = $"Patching {item.Name}...";

                    var result = await PatchItemAsync(item, EnableRegionPatchExisting, EnableVgaPatchExisting);

                    if (!result.Success)
                    {
                        // Log error but continue with other items
                    }

                    progress.ProcessedItems++;

                    if (!progress.IsVisible)
                        break;
                }
            }
            finally
            {
                progress.AllowClose();
                progress.Close();
            }
        }

        private MenuKind getMenuKindFromName(string name)
        {
            switch (name)
            {
                case "GDMENU": return MenuKind.gdMenu;
                case "openMenu": return MenuKind.openMenu;
                default: return MenuKind.None;
            }
        }

    }

    public class ProgressWindowClosedException : Exception
    {
    }


}
