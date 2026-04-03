using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

    public class Manager
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

        private readonly string gdishrinkPath;

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
        public bool EnableLockCheck = true;


        public ObservableCollection<GdItem> ItemList { get; } = new ObservableCollection<GdItem>();

        public ObservableCollection<string> KnownFolders { get; } = new ObservableCollection<string>();

        public BoxDatManager BoxDat { get; private set; }
        public IconDatManager IconDat { get; private set; }
        public MetaDatManager MetaDat { get; private set; }

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

        public string GetMenuDataPath() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? MacOsDataMigration.GetUserMenuDataDir()
                : Path.Combine(currentAppPath, "tools", "openMenu", "menu_data");

        public string GetDatBackupFolder() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? MacOsDataMigration.GetUserDatBackupsDir()
                : Path.Combine(currentAppPath, "dat_backups");

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

            var boxDatPath = GetBoxDatPath();
            if (File.Exists(boxDatPath))
            {
                BoxDat.Load(boxDatPath);
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

                // Reinitialize the managers
                BoxDat = new BoxDatManager();
                IconDat = new IconDatManager();
                MetaDat = new MetaDatManager();

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

                            var backupResult = BackupAllDats();
                            if (!backupResult.success)
                                return backupResult;

                            File.WriteAllBytes(GetBoxDatPath(), boxData);
                            File.WriteAllBytes(GetIconDatPath(), iconData);
                            File.WriteAllBytes(GetMetaDatPath(), metaData);

                            BoxDat = new BoxDatManager();
                            BoxDat.Load(GetBoxDatPath());
                            IconDat = new IconDatManager();
                            IconDat.Load(GetIconDatPath());
                            MetaDat = new MetaDatManager();
                            MetaDat.Load(GetMetaDatPath());

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

            // First, handle colon specially - replace with " - " for readability
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

            // both exist, check if serials match
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

            var datPaths = new[] { GetBoxDatPath(), GetIconDatPath(), GetMetaDatPath() };

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
        /// false if user cancelled.
        /// </summary>
        public async Task<bool> EnsureDatFilesWritable()
        {
            while (true)
            {
                var lockedFiles = CheckDatFilesAccessibility();
                if (lockedFiles.Count == 0) return true;

                if (!await Helper.DependencyManager.ShowLockedFilesDialog(lockedFiles))
                    return false; // user cancelled
            }
        }

        public static Manager CreateInstance(IDependencyManager m, string[] compressedFileExtensions)
        {
            Helper.DependencyManager = m;
            Helper.CompressedFileExpression = new Func<string, bool>(x => compressedFileExtensions.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase)));

            return new Manager();
        }

        private Manager()
        {
            gdishrinkPath = Path.Combine(currentAppPath, "tools", "gdishrink.exe");
            //ipbinPath = Path.Combine(currentAppPath, "tools", "IP.BIN");
            PlayStationDB.LoadFrom(Constants.PS1GameDBFile);
        }

        public async Task LoadItemsFromCard()
        {
            ItemList.Clear();
            UndoManager.Clear();  // Clear undo history when loading new SD card
            MenuKindSelected = MenuKind.None;

            var toAdd = new List<Tuple<int, string>>();
            var rootDirs = await Helper.GetDirectoriesAsync(sdPath);
            foreach (var item in rootDirs)//.OrderBy(x => x))
            {
                if (int.TryParse(Path.GetFileName(item), out int number))
                {
                    toAdd.Add(new Tuple<int, string>(number, item));
                }
            }

            var invalid = new List<string>();
            bool isFirstItem = true;

            foreach (var item in toAdd.OrderBy(x => x.Item1))
                try
                {
                    GdItem itemToAdd = null;

                    if (EnableLazyLoading)//load item without reading ip.bin. only read name.txt+serial.txt. will be null if no name.txt or empty
                        try
                        {
                            itemToAdd = await LazyLoadItemFromCard(item.Item1, item.Item2);
                        }
                        catch { }

                    //not lazyloaded. force full reading
                    if (itemToAdd == null)
                        itemToAdd = await ImageHelper.CreateGdItemAsync(item.Item2);

                    ItemList.Add(itemToAdd);

                    // Detect menu kind immediately after loading the first item
                    if (isFirstItem)
                    {
                        isFirstItem = false;

                        //try to detect using name.txt info
                        MenuKindSelected = getMenuKindFromName(itemToAdd.Name);

                        //not detected using name.txt. Try to load from ip.bin
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

            //todo implement menu fallback? to default or forced mode (in config)
            //if (MenuKindSelected == MenuKind.None) { }

            // Initialize known folders from current items
            InitializeKnownFolders();
        }

        private async ValueTask loadIP(IEnumerable<GdItem> items)
        {
            var query = items.Where(x => x.Ip == null);
            if (!query.Any())
                return;

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = items.Count();
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
            }
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
                item.Ip.Region = (await Helper.ReadAllTextAsync(regionPath))?.Trim() ?? string.Empty;

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

                        item.Name = name;

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
                    item.Name = name;
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

                    item.Name = name;
                    count++;
                }
            }
            return count;
        }


        private async Task<GdItem> LazyLoadItemFromCard(int sdNumber, string folderPath)
        {
            var files = await Helper.GetFilesAsync(folderPath);

            var itemName = string.Empty;
            var nameFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.NameTextFile, StringComparison.OrdinalIgnoreCase));
            if (nameFile != null)
                itemName = await Helper.ReadAllTextAsync(nameFile);

            //cached "name.txt" file is required.
            if (string.IsNullOrWhiteSpace(nameFile))
                return null;

            var itemSerial = string.Empty;
            var serialFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.SerialTextFile, StringComparison.OrdinalIgnoreCase));
            if (serialFile != null)
                itemSerial = await Helper.ReadAllTextAsync(serialFile);

            //cached "serial.txt" file is required.
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
                itemRegion = itemRegion?.Trim() ?? string.Empty;
            }

            string itemImageFile = null;

            //is uncompressed?
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
            };

            // Need all cache files present; if any are missing, Ip stays null
            // and the metadata scan will parse from disc image later
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
                        // SevenZip: get uncompressed size from archive
                        if (string.IsNullOrEmpty(item.FullFolderPath) || string.IsNullOrEmpty(item.ImageFile))
                            continue;

                        result.NewItemCount++;
                        result.ContainsCompressedFiles = true;
                        try
                        {
                            var archivePath = Path.Combine(item.FullFolderPath, item.ImageFile);
                            var archiveFiles = Helper.DependencyManager.GetArchiveFiles(archivePath);
                            result.NewItemsSize += archiveFiles.Values.Sum();
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

                if (ItemList.Count == 0 || await Helper.DependencyManager.ShowYesNoDialog("Save", $"Save changes to \"{sdPath}\" drive?") == false)
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

                // Check for sufficient space before proceeding
                var spaceCheck = await CalculateRequiredSpace();
                if (!spaceCheck.HasSufficientSpace)
                {
                    var proceed = await Helper.DependencyManager.ShowSpaceWarningDialog(spaceCheck);
                    if (!proceed)
                    {
                        return false;
                    }
                }

                //load ipbin from lazy loaded items
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

                        if (lockedFiles.Count == 0)
                            break; // All files accessible, proceed with save

                        // true = retry, false = cancel
                        if (!await Helper.DependencyManager.ShowLockedFilesDialog(lockedFiles))
                        {
                            return false; // User cancelled
                        }
                        // User clicked retry, loop continues to check again
                    }
                }

                containsCompressedFile = ItemList.Any(x => x.FileFormat == FileFormat.SevenZip);

                StringBuilder sb = new StringBuilder();
                StringBuilder sb_open = new StringBuilder();

                //delete unused folders that are numbers (but skip 01 as it's the menu folder)
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

                    if (await Helper.DependencyManager.ShowYesNoDialog("Confirm", $"The following folders need to be deleted.\nConfirm deletion?\n\n{sb.ToString()}") == false)
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


                if (!tempFolderRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    tempFolderRoot += Path.DirectorySeparatorChar.ToString();

                tempDirectory = Path.Combine(tempFolderRoot, Guid.NewGuid().ToString());

                if (!await Helper.DirectoryExistsAsync(tempDirectory))
                {
                    await Helper.CreateDirectoryAsync(tempDirectory);
                }

                sb.AppendLine("[GDMENU]");
                sb_open.AppendLine("[OPENMENU]");
                sb_open.AppendLine($"num_items={ItemList.Count}");
                sb_open.AppendLine();
                sb_open.AppendLine("[ITEMS]");

                // Load menu IP.BIN data for INI generation
                var menuIpBin = ImageHelper.GetIpData(File.ReadAllBytes(ipbinPath));

                var folder01 = Path.Combine(sdPath, "01");

                if (await Helper.DirectoryExistsAsync(folder01))
                {
                    try
                    {
                        var ip01 = await ImageHelper.CreateGdItemAsync(folder01);

                        if (ip01 != null && (ip01.Ip?.Name == "GDMENU" || ip01.Ip?.Name == "openMenu"))
                        {
                            //delete sdcard menu folder 01
                            await Helper.DeleteDirectoryAsync(folder01);

                            //if user changed between GDMENU <> openMenu
                            //reload name and serial from ip.bin
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

                // ALWAYS write menu as entry 01 in the INI file
                FillListText(sb, menuIpBin, menuIpBin.ProductNumber, menuIpBin.Name, 1);
                FillListText(sb_open, menuIpBin, menuIpBin.Name, menuIpBin.ProductNumber, 1, true, null, null);

                // Write games starting from entry 02 (skip menu if it's at index 0, otherwise start at 0)
                bool menuAtIndexZero = ItemList.Count > 0 && (ItemList[0].Ip?.Name == "GDMENU" || ItemList[0].Ip?.Name == "openMenu");
                int gameStartIndex = menuAtIndexZero ? 1 : 0;
                for (int i = gameStartIndex; i < ItemList.Count; i++)
                {
                    int entryNumber = menuAtIndexZero ? i + 1 : i + 2;
                    FillListText(sb, ItemList[i].Ip, ItemList[i].Name, ItemList[i].ProductNumber, entryNumber);
                    FillListText(sb_open, ItemList[i].Ip, ItemList[i].Name, ItemList[i].ProductNumber, entryNumber, true, ItemList[i].Folder, ItemList[i].GetDiscTypeFileValue(), ItemList[i].AlternativeFolders);
                }

                // Save DAT files if there are unsaved changes (only for openMenu)
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    bool hasBoxChanges = BoxDat?.HasUnsavedChanges == true;
                    bool hasIconChanges = IconDat?.HasUnsavedChanges == true;

                    if (hasBoxChanges || hasIconChanges)
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
                                var (success, errorMessage) = SaveBothDats(true); // Proceed without backup prompt
                                if (!success)
                                {
                                    // non-fatal, continue
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

                //generate iso and save in temp
                await GenerateMenuImageAsync(tempDirectory, sb.ToString(), sb_open.ToString());
                sb.Clear();
                sb_open.Clear();

                // Ensure menu item at position 0 has correct Work mode
                bool menuCurrentlyAtIndexZero = ItemList.Count > 0 && (ItemList[0].Ip?.Name == "GDMENU" || ItemList[0].Ip?.Name == "openMenu");
                if (menuCurrentlyAtIndexZero)
                {
                    ItemList[0].SdNumber = 1;
                    ItemList[0].Work = WorkMode.New;
                }

                //define what to do with each folder (skip first item if it's the menu)
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

                //rename guid to number
                await MoveCardItems();

                //copy new folders
                await CopyNewItems(tempDirectory);

                // Shrink existing items if option is enabled (Windows only)
                if (EnableGDIShrink && EnableGDIShrinkExisting)
                {
                    await ShrinkExistingItemsAsync(tempDirectory);
                }

                // Patch existing items if option is enabled
                if (EnableRegionPatchExisting || EnableVgaPatchExisting)
                {
                    await PatchExistingItemsAsync();
                }

                //finally rename disc images, write name text file (skip menu if it's at index 0)
                foreach (var item in ItemList.Skip(menuCurrentlyAtIndexZero ? 1 : 0))
                {
                    //rename image file
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

                    //write text name into folder
                    var itemNamePath = Path.Combine(item.FullFolderPath, Constants.NameTextFile);
                    if (!await Helper.FileExistsAsync(itemNamePath) || (await Helper.ReadAllTextAsync(itemNamePath)).Trim() != item.Name)
                        await Helper.WriteTextFileAsync(itemNamePath, item.Name);

                    //write serial number into folder
                    var itemSerialPath = Path.Combine(item.FullFolderPath, Constants.SerialTextFile);
                    if (!await Helper.FileExistsAsync(itemSerialPath) || (await Helper.ReadAllTextAsync(itemSerialPath)).Trim() != item.ProductNumber)
                        await Helper.WriteTextFileAsync(itemSerialPath, item.ProductNumber.Trim());

                    //write folder path into folder
                    var itemFolderPath = Path.Combine(item.FullFolderPath, Constants.FolderTextFile);
                    var folderValue = item.Folder ?? string.Empty;
                    if (!await Helper.FileExistsAsync(itemFolderPath) || (await Helper.ReadAllTextAsync(itemFolderPath)).Trim() != folderValue)
                        await Helper.WriteTextFileAsync(itemFolderPath, folderValue);

                    //write alt folder paths
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

                    //write disc type into folder (openMenu only)
                    if (MenuKindSelected == MenuKind.openMenu)
                    {
                        var itemTypePath = Path.Combine(item.FullFolderPath, Constants.TypeTextFile);
                        var typeValue = item.GetDiscTypeFileValue();
                        if (!await Helper.FileExistsAsync(itemTypePath) || (await Helper.ReadAllTextAsync(itemTypePath)).Trim() != typeValue)
                            await Helper.WriteTextFileAsync(itemTypePath, typeValue);

                        //write disc number into folder
                        var itemDiscPath = Path.Combine(item.FullFolderPath, Constants.DiscTextFile);
                        var discValue = item.Ip?.Disc ?? "1/1";
                        if (!await Helper.FileExistsAsync(itemDiscPath) || (await Helper.ReadAllTextAsync(itemDiscPath)).Trim() != discValue)
                            await Helper.WriteTextFileAsync(itemDiscPath, discValue);

                        //write vga into folder
                        var itemVgaPath = Path.Combine(item.FullFolderPath, Constants.VgaTextFile);
                        var vgaValue = (item.Ip?.Vga ?? false) ? "1" : "0";
                        if (!await Helper.FileExistsAsync(itemVgaPath) || (await Helper.ReadAllTextAsync(itemVgaPath)).Trim() != vgaValue)
                            await Helper.WriteTextFileAsync(itemVgaPath, vgaValue);

                        //write version into folder
                        var itemVersionPath = Path.Combine(item.FullFolderPath, Constants.VersionTextFile);
                        var versionValue = item.Ip?.Version ?? string.Empty;
                        if (!await Helper.FileExistsAsync(itemVersionPath) || (await Helper.ReadAllTextAsync(itemVersionPath)).Trim() != versionValue)
                            await Helper.WriteTextFileAsync(itemVersionPath, versionValue);

                        //write date into folder
                        var itemDatePath = Path.Combine(item.FullFolderPath, Constants.DateTextFile);
                        var dateValue = item.Ip?.ReleaseDate ?? string.Empty;
                        if (!await Helper.FileExistsAsync(itemDatePath) || (await Helper.ReadAllTextAsync(itemDatePath)).Trim() != dateValue)
                            await Helper.WriteTextFileAsync(itemDatePath, dateValue);

                        //write region into folder
                        var itemRegionPath = Path.Combine(item.FullFolderPath, Constants.RegionTextFile);
                        var regionValue = item.Ip?.Region ?? string.Empty;
                        if (!await Helper.FileExistsAsync(itemRegionPath) || (await Helper.ReadAllTextAsync(itemRegionPath)).Trim() != regionValue)
                            await Helper.WriteTextFileAsync(itemRegionPath, regionValue);
                    }

                    //write info text into folder for cdi files
                    //var itemInfoPath = Path.Combine(item.FullFolderPath, infotextfile);
                    //if (item.CdiTarget > 0)
                    //{
                    //    var newTarget = $"target|{item.CdiTarget}";
                    //    if (!await Helper.FileExistsAsync(itemInfoPath) || (await Helper.ReadAllTextAsync(itemInfoPath)).Trim() != newTarget)
                    //        await Helper.WriteTextFileAsync(itemInfoPath, newTarget);
                    //}
                }

                if (containsCompressedFile)
                {
                    //build the menu again

                    var orderedList = ItemList.OrderBy(x => x.SdNumber);

                    sb.AppendLine("[GDMENU]");
                    sb_open.AppendLine("[OPENMENU]");
                    sb_open.AppendLine($"num_items={ItemList.Count}");
                    sb_open.AppendLine();
                    sb_open.AppendLine("[ITEMS]");

                    foreach (var item in orderedList)
                    {
                        FillListText(sb, item.Ip, item.Name, item.ProductNumber, item.SdNumber);
                        FillListText(sb_open, item.Ip, item.Name, item.ProductNumber, item.SdNumber, true, item.Folder, item.GetDiscTypeFileValue());
                    }

                    //generate iso and save in temp
                    await GenerateMenuImageAsync(tempDirectory, sb.ToString(), sb_open.ToString(), true);

                    //move to card
                    var menuitem = orderedList.First();

                    if (await Helper.DirectoryExistsAsync(menuitem.FullFolderPath))
                        await Helper.DeleteDirectoryAsync(menuitem.FullFolderPath);

                    //await Helper.MoveDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);
                    await Helper.CopyDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);

                    sb.Clear();
                    sb_open.Clear();
                }

                //update menu item length
                UpdateItemLength(ItemList.OrderBy(x => x.SdNumber).First());

                //write menu config to root of sdcard
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

                //write disc list to root of sdcard
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
                    if (tempDirectory != null && await Helper.DirectoryExistsAsync(tempDirectory))
                    {
                        await Helper.DeleteDirectoryAsync(tempDirectory);
                    }
                }
                catch
                {
                    // Silently fail cleanup
                }
            }
        }

        private async Task GenerateMenuImageAsync(string tempDirectory, string listText, string openmenuListText, bool isRebuilding = false)
        {
            //create low density track
            var lowdataPath = Path.Combine(tempDirectory, "lowdensity_data");
            if (!await Helper.DirectoryExistsAsync(lowdataPath))
                await Helper.CreateDirectoryAsync(lowdataPath);

            //create hi density track
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

                // On macOS, BOX/ICON/META.DAT live in Application Support, not the bundle.
                // Exclude them from the bulk bundle copy so they don't overwrite user data.
                var excludeFromBundle = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BOX.DAT", "ICON.DAT", "META.DAT" }
                    : null;
                await Helper.CopyDirectoryAsync(menuDataSrc, dataPath, excludeFromBundle);

                // Copy DATs from their authoritative source (Application Support on macOS, bundle on others).
                if (File.Exists(GetBoxDatPath()))
                    File.Copy(GetBoxDatPath(), Path.Combine(dataPath, "BOX.DAT"), overwrite: true);
                if (File.Exists(GetIconDatPath()))
                    File.Copy(GetIconDatPath(), Path.Combine(dataPath, "ICON.DAT"), overwrite: true);
                if (File.Exists(GetMetaDatPath()))
                    File.Copy(GetMetaDatPath(), Path.Combine(dataPath, "META.DAT"), overwrite: true);

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


            //generate menu gdi
            var builder = new DiscUtils.Gdrom.GDromBuilder()
            {
                RawMode = false,
                TruncateData = TruncateMenuGDI,
                VolumeIdentifier = MenuKindSelected == MenuKind.gdMenu ? "GDMENU" : "OPENMENU"
            };
            //builder.ReportProgress += ProgressReport;

            //create low density track
            List<FileInfo> fileList = new List<FileInfo>();
            //add additional files, like themes
            fileList.AddRange(new DirectoryInfo(lowdataPath).GetFiles());

            var track01Path = Path.Combine(cdiPath, "track01.iso");
            builder.CreateFirstTrack(track01Path, fileList);

            var track04Path = Path.Combine(cdiPath, "track04.raw");

            var updatetDiscTracks = builder.BuildGDROM(dataPath, ipbinPath, new List<string> { track04Path }, cdiPath);//todo await

            builder.UpdateGdiFile(updatetDiscTracks, cdiFilePath);

            var firstItemIsMenu = ItemList.Count > 0 && (ItemList.First().Ip?.Name == "GDMENU" || ItemList.First().Ip?.Name == "openMenu");

            if (firstItemIsMenu)
            {
                //long start;
                //GetIpData(cdiFilePath, out long fileLength);

                var item = ItemList[0];

                //item.CdiTarget = start;

                if (isRebuilding)
                {
                    return;
                }

                //if user's menu is not in GDI format, update to GDI format.
                if (!Path.GetExtension(item.ImageFile).Equals(".gdi", StringComparison.OrdinalIgnoreCase))
                {
                    item.ImageFiles.Clear();
                    var gdi = await ImageHelper.CreateGdItemAsync(cdiPath);
                    item.ImageFiles.AddRange(gdi.ImageFiles);
                }

                item.FullFolderPath = cdiPath;
                item.ImageFiles[0] = Path.GetFileName(cdiFilePath);
                //item.RenameImageFile(Path.GetFileName(cdiFilePath));

                item.SdNumber = 0;
                item.Work = WorkMode.New;
            }
            else if (!isRebuilding)
            {
                var newMenuItem = await ImageHelper.CreateGdItemAsync(cdiPath);
                ItemList.Insert(0, newMenuItem);
            }
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
                    using (var p = CreateProcess(gdishrinkPath))
                        if (!await RunShrinkProcess(p, Path.Combine(item.FullFolderPath, item.ImageFile), newPath))
                            throw new Exception("Error during GDIShrink");
                }
                else
                {
                    // If the destination directory exist, delete it.
                    if (Directory.Exists(newPath))
                    {
                        await Helper.DeleteDirectoryAsync(newPath);
                    }
                    //then create a new one
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
            }

            // Apply region/VGA patches to newly copied items
            if (item.Work == WorkMode.New && (EnableRegionPatch || EnableVgaPatch))
            {
                // Skip menu items
                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu")
                {
                    await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                }
            }

            item.Work = WorkMode.None;
        }

        private Process CreateProcess(string fileName)
        {
            var p = new Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.FileName = fileName;
            return p;
        }

        private async Task CopyNewItems(string tempdir)
        {
            var total = ItemList.Count(x => x.Work == WorkMode.New);
            if (total == 0)
            {
                return;
            }

            //gdishrink
            var itemsToShrink = new List<GdItem>();
            var ignoreShrinkList = new List<string>();
            if (EnableGDIShrink)
            {
                if (EnableGDIShrinkBlackList)
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(Constants.GdiShrinkBlacklistFile))
                        {
                            var split = line.Split(';');
                            if (split.Length > 2 && !string.IsNullOrWhiteSpace(split[1]))
                                ignoreShrinkList.Add(split[1].Trim());
                        }
                    }
                    catch { }
                }

                var shrinkableItems = ItemList.Where(x =>
                    x.Work == WorkMode.New && x.Ip?.Name != "GDMENU" && x.Ip?.Name != "openMenu" && x.CanApplyGDIShrink
                        && (x.FileFormat == FileFormat.Uncompressed || x.FileFormat == FileFormat.Chd || (EnableGDIShrinkCompressed)
                        && !ignoreShrinkList.Contains(x.Ip?.ProductNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    )).OrderBy(x => x.Name).ThenBy(x => x.Ip?.Disc ?? "1/1").ToArray();
                if (shrinkableItems.Any())
                {
                    var result = Helper.DependencyManager.GdiShrinkWindowShowDialog(shrinkableItems);
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
                                item.CanApplyGDIShrink = false;
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
                                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu")
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
                                    progress.TextContent = $"Converting/Shrinking {item.Name}...";

                                    var tempChdDir = Path.Combine(tempdir, $"chd_{folderNumber}");
                                    if (!await Helper.DirectoryExistsAsync(tempChdDir))
                                        await Helper.CreateDirectoryAsync(tempChdDir);

                                    var (success, message) = await ChdConverter.ConvertToGdi(chdPath, tempChdDir);
                                    if (!success)
                                        throw new Exception($"Failed to convert CHD to GDI: {message}");

                                    var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempChdDir);

                                    using (var p = CreateProcess(gdishrinkPath))
                                        if (!await RunShrinkProcess(p, Path.Combine(tempChdDir, tempGdiItem.ImageFile), newPath))
                                            throw new Exception("Error during GDIShrink");

                                    await Helper.DeleteDirectoryAsync(tempChdDir);

                                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                    item.CanApplyGDIShrink = false;
                                }
                                else
                                {
                                    // Convert CHD directly to GDI on SD card
                                    progress.TextContent = $"Converting {item.Name} to GDI...";

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
                                    item.CanApplyGDIShrink = false;
                                }
                            }
                            else
                            {
                                // CD-ROM CHD: Convert to CUE/BIN, then to CDI
                                progress.TextContent = $"Converting {item.Name} to CDI...";

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
                                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu")
                                {
                                    await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                                }
                            }
                        }
                        else//compressed file
                        {
                            if (EnableGDIShrink && EnableGDIShrinkCompressed && itemsToShrink.Contains(item))
                            {
                                progress.TextContent = $"Decompressing {item.Name}...";

                                shrink = true;

                                //extract game to temp folder
                                var folderNumber = i + 1;
                                var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

                                var tempExtractDir = Path.Combine(tempdir, $"ext_{folderNumber}");
                                if (!await Helper.DirectoryExistsAsync(tempExtractDir))
                                    await Helper.CreateDirectoryAsync(tempExtractDir);

                                await Task.Run(() => Helper.DependencyManager.ExtractArchive(Path.Combine(item.FullFolderPath, item.ImageFile), tempExtractDir));

                                var gdi = await ImageHelper.CreateGdItemAsync(tempExtractDir);

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
                                        // GD-ROM: Convert to GDI format
                                        progress.TextContent = $"Converting {item.Name} to GDI...";

                                        var (success, message) = await GdiConverter.ConvertToGdi(cuePath, newPath);
                                        if (!success)
                                            throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                                        await Helper.DeleteDirectoryAsync(tempExtractDir);

                                        // Get the converted GDI item info
                                        var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);

                                        item.FullFolderPath = newPath;
                                        item.Work = WorkMode.None;
                                        item.SdNumber = folderNumber;
                                        item.FileFormat = FileFormat.Uncompressed;
                                        item.ImageFiles.Clear();
                                        item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                        item.Ip = gdi.Ip;
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
                                            progress.TextContent = $"Converting/Shrinking {item.Name}...";

                                            // Convert CHD to GDI in temp, then shrink
                                            var tempChdGdiDir = Path.Combine(tempdir, $"chdgdi_{folderNumber}");
                                            if (!await Helper.DirectoryExistsAsync(tempChdGdiDir))
                                                await Helper.CreateDirectoryAsync(tempChdGdiDir);

                                            var (success, message) = await ChdConverter.ConvertToGdi(extractedChdPath, tempChdGdiDir);
                                            if (!success)
                                                throw new Exception($"Failed to convert CHD to GDI: {message}");

                                            var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempChdGdiDir);

                                            using (var p = CreateProcess(gdishrinkPath))
                                                if (!await RunShrinkProcess(p, Path.Combine(tempChdGdiDir, tempGdiItem.ImageFile), newPath))
                                                    throw new Exception("Error during GDIShrink");

                                            await Helper.DeleteDirectoryAsync(tempChdGdiDir);
                                        }
                                        else
                                        {
                                            progress.TextContent = $"Converting {item.Name} to GDI...";

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
                                    }
                                    else
                                    {
                                        // CD-ROM CHD: Convert to CUE/BIN then CDI
                                        progress.TextContent = $"Converting {item.Name} to CDI...";

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

                                        using (var p = CreateProcess(gdishrinkPath))
                                            if (!await RunShrinkProcess(p, Path.Combine(tempExtractDir, gdi.ImageFile), newPath))
                                                throw new Exception("Error during GDIShrink");

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
                                }

                                UpdateItemLength(item);

                                // Apply region/VGA patches to newly extracted items
                                if (EnableRegionPatch || EnableVgaPatch)
                                {
                                    if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu")
                                    {
                                        await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                                    }
                                }
                            }
                            else// if not shrinking, can extract directly to card
                            {
                                progress.TextContent = $"Decompressing {item.Name}...";
                                await Uncompress(item, i + 1, tempdir, progress);//+ ammountToIncrement
                            }

                        }


                        progress.ProcessedItems++;

                        //user closed window
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
                // count once per item using a set of all unique folder paths
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

                // remap alt folders
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

            // post-apply conflict scrub
            int conflictsRemoved = 0;
            foreach (var item in ItemList)
            {
                if (item.AlternativeFolders.Count > 0)
                {
                    // remove alt folders that now match the primary folder
                    if (!string.IsNullOrWhiteSpace(item.Folder))
                        conflictsRemoved += item.AlternativeFolders.RemoveAll(af => af == item.Folder);

                    // deduplicate alt folders
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

        private async Task Uncompress(GdItem item, int folderNumber, string tempdir, IProgressWindow progress = null)
        {
            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

            // Extract to temp folder first, not directly to SD card
            var tempExtractDir = Path.Combine(tempdir, $"ext_{folderNumber}");
            if (!await Helper.DirectoryExistsAsync(tempExtractDir))
                await Helper.CreateDirectoryAsync(tempExtractDir);

            await Task.Run(() => Helper.DependencyManager.ExtractArchive(Path.Combine(item.FullFolderPath, item.ImageFile), tempExtractDir));

            var extracted = await ImageHelper.CreateGdItemAsync(tempExtractDir);

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
                        progress.TextContent = $"Converting {item.Name} to GDI...";

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
                        progress.TextContent = $"Converting {item.Name} to CDI...";

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

            item.FullFolderPath = newPath;
            item.Work = WorkMode.None;
            item.SdNumber = folderNumber;
            item.FileFormat = FileFormat.Uncompressed;

            //compressed file by default will have its serial blank.
            //if still blank, read from the now extracted ip info
            if (string.IsNullOrWhiteSpace(item.ProductNumber))
                item.ProductNumber = extracted.ProductNumber;

            UpdateItemLength(item);

            // Apply region/VGA patches to newly extracted items
            if (EnableRegionPatch || EnableVgaPatch)
            {
                if (item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu")
                {
                    await PatchItemAsync(item, EnableRegionPatch, EnableVgaPatch);
                }
            }
        }

        private async Task<bool> RunShrinkProcess(Process p, string inputFilePath, string outputFolderPath)
        {
            if (!Directory.Exists(outputFolderPath))
                Directory.CreateDirectory(outputFolderPath);

            p.StartInfo.ArgumentList.Clear();

            p.StartInfo.ArgumentList.Add(inputFilePath);
            p.StartInfo.ArgumentList.Add(outputFolderPath);

            await RunProcess(p);
            return p.ExitCode == 0;
        }

        private Task RunProcess(Process p)
        {
            //p.StartInfo.RedirectStandardOutput = true;
            //p.StartInfo.RedirectStandardError = true;

            //p.OutputDataReceived += (ss, ee) => { Debug.WriteLine("[OUTPUT] " + ee.Data); };
            //p.ErrorDataReceived += (ss, ee) => { Debug.WriteLine("[ERROR] " + ee.Data); };

            p.Start();

            //p.BeginOutputReadLine();
            //p.BeginErrorReadLine();

            return Task.Run(() => p.WaitForExit());
        }
        //todo implement
        internal static void UpdateItemLength(GdItem item)
        {
            item.Length = ByteSizeLib.ByteSize.FromBytes(item.ImageFiles.Sum(x => new FileInfo(Path.Combine(item.FullFolderPath, x)).Length));
        }

        public async Task<List<string>> AddGames(string[] files)
        {
            var invalid = new List<string>();
            var addedItems = new List<(GdItem Item, int Index)>();

            if (files != null)
            {
                foreach (var item in files)
                {
                    try
                    {
                        var gdItem = await ImageHelper.CreateGdItemAsync(item);
                        int index = ItemList.Count;
                        ItemList.Add(gdItem);
                        addedItems.Add((gdItem, index));
                    }
                    catch
                    {
                        invalid.Add(item);
                    }
                }
            }

            // Record undo operation if any items were added
            if (addedItems.Count > 0)
            {
                var undoOp = new MultiItemAddOperation { ItemList = ItemList };
                undoOp.Items.AddRange(addedItems);
                UndoManager.RecordChange(undoOp);
            }

            return invalid;
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
            if (!patchRegion && !patchVga)
                return new PatchResult { Success = true };

            if (item.DiscType != "Game")
                return new PatchResult { Success = true };

            var imagePath = Path.Combine(item.FullFolderPath, item.ImageFile);

            if (!RegionPatcher.CanPatch(imagePath))
                return new PatchResult { Success = true, Details = { "Format not supported for patching" } };

            var result = await RegionPatcher.PatchImageAsync(imagePath, patchRegion, patchVga);

            // Update in-memory Ip and cached region.txt to reflect the patched disc
            if (result.Success && patchRegion && result.RegionPatchCount > 0 && item.Ip != null)
            {
                item.Ip.Region = "JUE";
                item.Ip.Vga = patchVga ? true : item.Ip.Vga;

                var regionPath = Path.Combine(item.FullFolderPath, Constants.RegionTextFile);
                await Helper.WriteTextFileAsync(regionPath, "JUE");
            }

            if (result.Success && patchVga && result.VgaPatchCount > 0 && item.Ip != null)
            {
                item.Ip.Vga = true;

                var vgaPath = Path.Combine(item.FullFolderPath, Constants.VgaTextFile);
                await Helper.WriteTextFileAsync(vgaPath, "1");
            }

            return result;
        }

        private async Task ShrinkExistingItemsAsync(string tempDirectory)
        {
            // Load blacklist if enabled
            var blacklist = new List<string>();
            if (EnableGDIShrinkBlackList)
            {
                try
                {
                    foreach (var line in File.ReadAllLines(Constants.GdiShrinkBlacklistFile))
                    {
                        var split = line.Split(';');
                        if (split.Length > 2 && !string.IsNullOrWhiteSpace(split[1]))
                            blacklist.Add(split[1].Trim());
                    }
                }
                catch { }
            }

            // Get items that can be shrunk: on SD card, not new, is GDI, can apply shrink, is Game type
            var itemsToShrink = ItemList.Where(x =>
                x.SdNumber > 0 &&
                x.Work != WorkMode.New &&
                x.Ip?.Name != "GDMENU" &&
                x.Ip?.Name != "openMenu" &&
                x.CanApplyGDIShrink &&
                x.FileFormat == FileFormat.Uncompressed &&
                x.DiscType == "Game" &&
                !blacklist.Contains(x.Ip?.ProductNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase)).ToList();

            if (itemsToShrink.Count == 0)
                return;

            // Show dialog to let user select which items to shrink
            if (Helper.DependencyManager.GdiShrinkWindowShowDialog != null)
            {
                var result = Helper.DependencyManager.GdiShrinkWindowShowDialog(itemsToShrink);
                if (result == null || result.Length == 0)
                    return;
                itemsToShrink = result.ToList();
            }

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
                        // Run gdishrink
                        using (var p = CreateProcess(gdishrinkPath))
                        {
                            if (!await RunShrinkProcess(p, Path.Combine(item.FullFolderPath, item.ImageFile), tempOutputDir))
                            {
                                // Shrink failed, clean up and continue
                                await Helper.DeleteDirectoryAsync(tempOutputDir);
                                progress.ProcessedItems++;
                                continue;
                            }
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
                        Directory.Move(item.FullFolderPath, backupDir);

                        // Create new folder and move shrunk files
                        await Helper.CreateDirectoryAsync(item.FullFolderPath);
                        foreach (var file in shrunkFiles)
                        {
                            var destPath = Path.Combine(item.FullFolderPath, Path.GetFileName(file));
                            File.Move(file, destPath);
                        }

                        // done, clean up
                        await Helper.DeleteDirectoryAsync(backupDir);
                        await Helper.DeleteDirectoryAsync(tempOutputDir);

                        // Update item's image files
                        item.ImageFiles.Clear();
                        item.ImageFiles.AddRange(shrunkGdi.ImageFiles);

                        // Update item length
                        UpdateItemLength(item);
                    }
                    catch
                    {
                        // Try to restore from backup if original folder is gone
                        if (await Helper.DirectoryExistsAsync(backupDir))
                        {
                            if (!await Helper.DirectoryExistsAsync(item.FullFolderPath))
                            {
                                Directory.Move(backupDir, item.FullFolderPath);
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
                x.FileFormat == FileFormat.Uncompressed).ToList();

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
